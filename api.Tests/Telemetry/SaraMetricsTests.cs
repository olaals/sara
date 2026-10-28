using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using api.Database.Context;
using api.Database.Models;
using api.MQTT;
using api.Services;
using api.Services.ResultHandlers.WorkflowResultHandlers;
using api.Telemetry;
using Azure.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MQTTnet.Extensions.ManagedClient;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using Xunit;

namespace Api.Test.Telemetry;

[Collection("Operation metrics")]
public class SaraMetricsTests
{
    [Fact]
    public void Baselines_InitializeEveryBoundedSeriesToZero()
    {
        using var metrics = new MetricsCapture();
        SaraMetrics.InitializeBaselines();

        Assert.Equal(141, metrics.Measurements.Length);
        Assert.All(metrics.Measurements, measurement => Assert.Equal(0, measurement.Value));
        Assert.Contains(
            metrics.Measurements,
            measurement =>
                measurement.Name == "sara_workflow_start"
                && measurement.Tags["stage"] == "submit"
                && measurement.Tags["outcome"] == "failure"
        );
        Assert.Contains(
            metrics.Measurements,
            measurement =>
                measurement.Name == "sara_result_processing"
                && measurement.Tags["workflow_type"] == "thermal-reading"
                && measurement.Tags["outcome"] == "failure"
        );
        Assert.Contains(
            metrics.Measurements,
            measurement =>
                measurement.Name == "sara_downstream_delivery"
                && measurement.Tags["destination"] == "mqtt"
                && measurement.Tags["outcome"] == "failure"
        );
    }

    [Fact]
    public void Labels_NormalizeUnknownValuesWithoutLeakingIdentifiers()
    {
        using var metrics = new MetricsCapture();
        SaraMetrics.RecordResultProcessing(
            Guid.NewGuid().ToString(),
            "invalid-stage",
            OperationOutcome.Failure
        );
        SaraMetrics.RecordDownstreamDelivery(
            "https://secret.example",
            "inspection-id",
            OperationOutcome.Failure
        );

        Assert.Equal(
            1,
            metrics.Count(
                "sara_result_processing",
                ("workflow_type", "unknown"),
                ("stage", "unknown")
            )
        );
        Assert.Equal(
            1,
            metrics.Count(
                "sara_downstream_delivery",
                ("destination", "unknown"),
                ("operation", "unknown")
            )
        );
    }

    [Theory]
    [InlineData(null, "failure")]
    [InlineData("", "failure")]
    [InlineData("not-json", "failure")]
    [InlineData("null", "failure")]
    [InlineData("{\"isBreak\":true}", "success")]
    public void DeserializeResult_RecordsOutcomeWithoutChangingReturnBehavior(
        string? json,
        string outcome
    )
    {
        using var metrics = new MetricsCapture();
        var workflow = new Workflow
        {
            WorkflowType = "fencilla",
            ResultJson = json,
            AnalysisRun = null!,
            InputBlobStorageLocations = [],
        };
        var result = WorkflowResultHandlerHelpers.DeserializeResult<FencillaResult>(
            workflow,
            NullLogger.Instance
        );

        Assert.Equal(outcome == "success", result is not null);
        Assert.Equal(
            1,
            metrics.Count("sara_result_processing", ("stage", "deserialize"), ("outcome", outcome))
        );
        Assert.Equal(1, metrics.Count("sara_result_processing"));
    }

    [Theory]
    [InlineData(200, "{\"statusCode\":200,\"timeseriesId\":\"id\"}", "success")]
    [InlineData(500, "downstream error", "failure")]
    [InlineData(429, "throttled", "failure")]
    [InlineData(200, "{\"statusCode\":500}", "failure")]
    [InlineData(200, "null", "failure")]
    public async Task TimeseriesUpload_RecordsHttpAndApplicationOutcomes(
        int status,
        string body,
        string outcome
    )
    {
        using var metrics = new MetricsCapture();
        using var client = new HttpClient(
            new StubHttpHandler(() =>
                new HttpResponseMessage((HttpStatusCode)status)
                {
                    Content = new StringContent(body),
                }
            )
        );
        var service = CreateTimeseriesService(client);

        // HTTP errors are currently swallowed; telemetry must still report them once.
        await service.TriggerTimeseriesUpload(UploadRequest());

        Assert.Equal(
            1,
            metrics.Count(
                "sara_downstream_delivery",
                ("destination", "timeseries"),
                ("outcome", outcome)
            )
        );
        Assert.Equal(1, metrics.Count("sara_downstream_delivery"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TimeseriesUpload_RecordsThrownFailuresOnce(bool networkError)
    {
        using var metrics = new MetricsCapture();
        using var client = new HttpClient(
            new StubHttpHandler(() =>
                networkError
                    ? throw new HttpRequestException("Network unavailable")
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("invalid-json"),
                    }
            )
        );
        var service = CreateTimeseriesService(client);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            service.TriggerTimeseriesUpload(UploadRequest())
        );

        Assert.Equal(1, metrics.Count("sara_downstream_delivery", ("outcome", "failure")));
        Assert.Equal(1, metrics.Count("sara_downstream_delivery"));
    }

    [Fact]
    public async Task TimeseriesUpload_SerializationFailureIsRecordedBeforeSending()
    {
        using var metrics = new MetricsCapture();
        using var client = new HttpClient(
            new StubHttpHandler(() => throw new Exception("Must not send"))
        );
        var request = UploadRequest() with { Value = float.NaN };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateTimeseriesService(client).TriggerTimeseriesUpload(request)
        );

        Assert.Equal(
            1,
            metrics.Count(
                "sara_downstream_delivery",
                ("operation", "upload"),
                ("outcome", "failure")
            )
        );
    }

    [Fact]
    public async Task DisabledTimeseriesUpload_IsSkippedNotFailed()
    {
        using var metrics = new MetricsCapture();
        using var client = new HttpClient(
            new StubHttpHandler(() => throw new Exception("Must not send"))
        );
        await CreateTimeseriesService(client, "").TriggerTimeseriesUpload(UploadRequest());
        Assert.Equal(1, metrics.Count("sara_downstream_delivery", ("outcome", "skipped")));
        Assert.Equal(0, metrics.Count("sara_downstream_delivery", ("outcome", "failure")));
    }

    [Theory]
    [InlineData(true, "failure")]
    [InlineData(false, "success")]
    public async Task ResultHandler_RecordsSwallowedExceptions(bool failHandler, string outcome)
    {
        using var metrics = new MetricsCapture();
        using var context = new SaraDbContext(
            new DbContextOptionsBuilder<SaraDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        var handler = new Mock<IWorkflowResultHandler>();
        handler.SetupGet(candidate => candidate.WorkflowType).Returns("fencilla");
        handler
            .Setup(candidate => candidate.OnWorkflowCompleted(It.IsAny<Workflow>()))
            .Returns(
                failHandler
                    ? Task.FromException(new InvalidOperationException("Handler failed"))
                    : Task.CompletedTask
            );
        var service = new WorkflowService(
            context,
            [handler.Object],
            [],
            Mock.Of<IAnalysisTriggerService>(),
            NullLogger<WorkflowService>.Instance,
            null!
        );

        await service.OnWorkflowCompleted(
            new Workflow
            {
                WorkflowType = "fencilla",
                Status = WorkflowStatus.Succeeded,
                AnalysisRun = null!,
                InputBlobStorageLocations = [],
            }
        );

        Assert.Equal(
            1,
            metrics.Count("sara_result_processing", ("stage", "handle"), ("outcome", outcome))
        );
        Assert.Equal(1, metrics.Count("sara_result_processing"));
    }

    [Theory]
    [InlineData(200, "42", "success")]
    [InlineData(200, "null", "skipped")]
    [InlineData(500, "downstream error", "failure")]
    public async Task TimeseriesFetch_RecordsOutcome(int status, string body, string outcome)
    {
        using var metrics = new MetricsCapture();
        using var client = new HttpClient(
            new StubHttpHandler(() =>
                new HttpResponseMessage((HttpStatusCode)status)
                {
                    Content = new StringContent(body),
                }
            )
        );
        var request = new FetchCO2MeasurementRequest
        {
            Facility = "test",
            TaskStartTime = "start",
            TaskEndTime = "end",
            InspectionName = "test",
        };
        if (status >= 400)
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                CreateTimeseriesService(client).FetchCO2ConcentrationFromTimeseries(request)
            );
        else
            await CreateTimeseriesService(client).FetchCO2ConcentrationFromTimeseries(request);

        Assert.Equal(
            1,
            metrics.Count(
                "sara_downstream_delivery",
                ("operation", "fetch_co2"),
                ("outcome", outcome)
            )
        );
        Assert.Equal(1, metrics.Count("sara_downstream_delivery"));
    }

    [Theory]
    [InlineData(false, "success")]
    [InlineData(true, "failure")]
    public async Task Mqtt_RecordsLocalEnqueueOutcome(bool failEnqueue, string outcome)
    {
        using var metrics = new MetricsCapture();
        // Strict mocks throw on enqueue; the service deliberately logs and swallows that failure.
        var client = new Mock<IManagedMqttClient>(
            failEnqueue ? MockBehavior.Strict : MockBehavior.Loose
        );
        var publisher = new MqttPublisherService(NullLogger<MqttService>.Instance, client.Object);

        await publisher.PublishSaraAnalysisResultAvailable(
            new SaraAnalysisResultMessage
            {
                InspectionIds = ["test"],
                AnalysisId = Guid.NewGuid(),
                WorkflowId = Guid.NewGuid(),
                AnalysisRunId = Guid.NewGuid(),
                AnalysisType = "fencilla",
            }
        );

        Assert.Equal(
            1,
            metrics.Count(
                "sara_downstream_delivery",
                ("operation", "enqueue_analysis_result"),
                ("outcome", outcome)
            )
        );
        Assert.Equal(1, metrics.Count("sara_downstream_delivery"));
        Assert.Single(client.Invocations);
    }

    [Fact]
    public async Task Email_RecordsDisabledAndConfigurationFailureOutcomes()
    {
        using var metrics = new MetricsCapture();
        var service = new EmailService(
            Mock.Of<TokenCredential>(),
            Options.Create(new EmailOptions())
        );
        await service.SendFencillaResultEmail("inspection-id", 1, "not-configured");
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.SendMailAsync("subject", "body", EmailTarget.Fencilla, "not-configured")
        );

        Assert.Equal(
            1,
            metrics.Count(
                "sara_downstream_delivery",
                ("destination", "email"),
                ("outcome", "skipped")
            )
        );
        Assert.Equal(
            1,
            metrics.Count(
                "sara_downstream_delivery",
                ("destination", "email"),
                ("outcome", "failure")
            )
        );
    }

    [Fact]
    public void OpenTelemetryProvider_ExportsZeroBaselinesAndIncrements()
    {
        using var exporter = new CapturingMetricExporter();
        using var provider = Sdk.CreateMeterProviderBuilder()
            .AddMeter(SaraMetrics.MeterName)
            .AddReader(new BaseExportingMetricReader(exporter))
            .Build();

        SaraMetrics.InitializeBaselines();
        Assert.True(provider.ForceFlush());
        Assert.Equal(141, exporter.Points.Count);
        Assert.All(exporter.Points, point => Assert.Equal(0, point.Value));

        exporter.Points.Clear();
        SaraMetrics.RecordWorkflowStart("submit", OperationOutcome.Failure);
        Assert.True(provider.ForceFlush());
        Assert.Contains(
            exporter.Points,
            point => point.Name == "sara_workflow_start" && point.Value == 1
        );
    }

    private static TimeseriesService CreateTimeseriesService(
        HttpClient client,
        string baseUrl = "https://timeseries.example"
    ) =>
        new(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?> { ["SARATimeseriesBaseUrl"] = baseUrl }
                )
                .Build(),
            NullLogger<TimeseriesService>.Instance,
            client
        );

    private static TriggerTimeseriesUploadRequest UploadRequest() =>
        new()
        {
            Name = "test",
            Facility = "test",
            ExternalId = "test",
            Description = "test",
            Unit = "test",
            AssetId = "test",
        };

    private sealed class StubHttpHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(respond());
    }

    private sealed class CapturingMetricExporter : BaseExporter<Metric>
    {
        public List<(string Name, long Value)> Points { get; } = [];

        public override ExportResult Export(in Batch<Metric> batch)
        {
            foreach (var metric in batch)
            foreach (ref readonly var point in metric.GetMetricPoints())
                Points.Add((metric.Name, point.GetSumLong()));
            return ExportResult.Success;
        }
    }
}
