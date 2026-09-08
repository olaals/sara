using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using api.Database.Context;
using api.Database.Models;
using api.MQTT;
using api.Services;
using Api.Test.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Api.Test.Integration;

/// <summary>
/// End-to-end tests that drive the full SARA pipeline:
/// MQTT ingestion -> Argo Workflow creation -> Kubernetes watch event -> workflow result handlers ->
/// analysis-run completion. External I/O is recorded via fakes.
/// </summary>
public class EndToEndPipelineTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;
    private TestWebApplicationFactory<Program> _factory = null!;
    private SaraDbContext _context = null!;
    private DatabaseUtilities _db = null!;

    public async ValueTask InitializeAsync()
    {
        (_container, string cs) = await TestSetupHelpers.ConfigurePostgreSqlDatabase();
        _factory = TestSetupHelpers.ConfigureWebApplicationFactory(cs);
        _ = _factory.Services;
        _context = TestSetupHelpers.ConfigurePostgreSqlContext(cs);
        _db = new DatabaseUtilities(_context);
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
        await _factory.DisposeAsync();
        await _container.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private Task ProcessInspectionResultInScope(IsarInspectionResultMessage message)
    {
        var handler = _factory.Services.GetRequiredService<MqttEventHandler>();
        return handler.ProcessIsarInspectionResult(message);
    }

    private async Task ProcessWorkflowSucceeded(Workflow workflow, string resultJson)
    {
        await _context.Entry(workflow).ReloadAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(workflow.ArgoWorkflowName);
        Assert.NotNull(workflow.ArgoWorkflowUid);
        var isLastStep = !await _context.Workflows.AnyAsync(
            candidate =>
                candidate.AnalysisRunId == workflow.AnalysisRunId
                && candidate.StepNumber > workflow.StepNumber,
            TestContext.Current.CancellationToken
        );
        using var scope = _factory.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<IArgoWorkflowEventProcessor>();
        await processor.HandleWorkflowEventAsync(
            new ArgoWorkflowResource
            {
                Metadata = new ArgoObjectMetadata
                {
                    Name = workflow.ArgoWorkflowName,
                    Uid = workflow.ArgoWorkflowUid,
                    Labels = new Dictionary<string, string>
                    {
                        [ArgoWorkflowClient.AnalysisRunIdLabel] = workflow.AnalysisRunId.ToString(),
                    },
                },
                Status = new ArgoWorkflowStatus
                {
                    Phase = isLastStep ? "Succeeded" : "Running",
                    Nodes = new Dictionary<string, ArgoNodeStatus>
                    {
                        [workflow.Id.ToString()] = new ArgoNodeStatus
                        {
                            DisplayName = AnalysisWorkflowGraphBuilder.GetTaskName(workflow),
                            Type = "DAG",
                            Phase = "Succeeded",
                            Outputs = new ArgoOutputs
                            {
                                Parameters =
                                [
                                    new ArgoParameter { Name = "result", Value = resultJson },
                                ],
                            },
                        },
                    },
                },
            },
            TestContext.Current.CancellationToken
        );
    }

    [Fact]
    public async Task HappyPath_SingleRecordPerRecordAnalysis_RunsThroughToSuccess()
    {
        const string AnalysisName = "per-record-test";
        const string ResultJson = "{\"value\":42}";
        var message = _db.NewIsarInspectionResultMessage(requiredAnalysis: [AnalysisName]);

        await ProcessInspectionResultInScope(message);

        var workflow = await _context
            .Workflows.Include(w => w.AnalysisRun)
                .ThenInclude(r => r.Analysis)
            .SingleAsync(TestContext.Current.CancellationToken);

        await ProcessWorkflowSucceeded(workflow, ResultJson);

        await _context.Entry(workflow).ReloadAsync(TestContext.Current.CancellationToken);
        await _context
            .Entry(workflow.AnalysisRun)
            .ReloadAsync(TestContext.Current.CancellationToken);

        Assert.Single(_factory.ArgoWorkflowClient.Requests);
        Assert.NotNull(workflow.StartedAt);
        Assert.Equal(ResultJson, workflow.ResultJson);
        Assert.Equal(WorkflowStatus.Succeeded, workflow.Status);
        Assert.Equal(AnalysisRunStatus.Succeeded, workflow.AnalysisRun.Status);
    }

    [Fact]
    public async Task DuplicateInspectionResult_DoesNotCreateAnotherRecordOrAnalysisRun()
    {
        var message = _db.NewIsarInspectionResultMessage(requiredAnalysis: ["per-record-test"]);

        await ProcessInspectionResultInScope(message);
        await ProcessInspectionResultInScope(message);

        Assert.Equal(
            1,
            await _context.InspectionRecords.CountAsync(TestContext.Current.CancellationToken)
        );
        Assert.Equal(
            1,
            await _context.AnalysisRuns.CountAsync(TestContext.Current.CancellationToken)
        );
        Assert.Single(_factory.ArgoWorkflowClient.Requests);
    }

    [Fact]
    public async Task BlobDoesNotExist_NoInspectionRecordOrWorkflowCreated_AndNoAnalysisTriggered()
    {
        _factory.BlobStorageService.BlobExists = false;
        var message = _db.NewIsarInspectionResultMessage(requiredAnalysis: ["per-record-test"]);

        await ProcessInspectionResultInScope(message);

        Assert.False(
            await _context.InspectionRecords.AnyAsync(TestContext.Current.CancellationToken),
            "No inspection record should be created when the ISAR blob does not exist."
        );
        Assert.False(
            await _context.Workflows.AnyAsync(TestContext.Current.CancellationToken),
            "No workflow should be created when the ISAR blob does not exist."
        );
        Assert.Empty(_factory.ArgoWorkflowClient.Requests);
    }

    [Fact]
    public async Task MultiStepChain_SecondWorkflowTriggeredAfterFirstSucceeds()
    {
        const string AnalysisName = "multi-step-test";
        const string Step1Result = "{\"step\":1}";
        const string Step2Result = "{\"step\":2}";
        var message = _db.NewIsarInspectionResultMessage(requiredAnalysis: [AnalysisName]);

        await ProcessInspectionResultInScope(message);

        var step1 = await _context
            .Workflows.Include(w => w.AnalysisRun)
            .SingleAsync(w => w.StepNumber == 1, TestContext.Current.CancellationToken);

        Assert.Single(_factory.ArgoWorkflowClient.Requests);

        await ProcessWorkflowSucceeded(step1, Step1Result);

        var step2 = await _context.Workflows.SingleAsync(
            w => w.StepNumber == 2,
            TestContext.Current.CancellationToken
        );

        await ProcessWorkflowSucceeded(step2, Step2Result);

        await _context.Entry(step1).ReloadAsync(TestContext.Current.CancellationToken);
        await _context.Entry(step2).ReloadAsync(TestContext.Current.CancellationToken);
        await _context.Entry(step1.AnalysisRun).ReloadAsync(TestContext.Current.CancellationToken);

        var requests = _factory.ArgoWorkflowClient.Requests;
        var request = Assert.Single(requests);
        Assert.Equal(["test-1", "test-2"], request.Tasks.Select(task => task.TemplateRef.Name));
        Assert.Equal(WorkflowStatus.Succeeded, step1.Status);
        Assert.Equal(WorkflowStatus.Succeeded, step2.Status);
        Assert.Equal(AnalysisRunStatus.Succeeded, step1.AnalysisRun.Status);
    }

    [Fact]
    public async Task GroupedAnalysis_TriggersOnceBothRecordsArrive()
    {
        const string GroupId = "group-abc";
        const string Blob1 = "record-1.jpg";
        const string Blob2 = "record-2.jpg";
        var groupMessage = _db.NewAnalysisGroupMessage(
            groupId: GroupId,
            size: 2,
            analyses: ["group-test"]
        );
        var message1 = _db.NewIsarInspectionResultMessage(
            inspectionId: "rec-1",
            blobName: Blob1,
            requiredAnalysis: ["group-test"],
            analysisGroup: groupMessage
        );
        var message2 = _db.NewIsarInspectionResultMessage(
            inspectionId: "rec-2",
            blobName: Blob2,
            requiredAnalysis: ["group-test"],
            analysisGroup: groupMessage
        );

        await ProcessInspectionResultInScope(message1);

        Assert.Empty(_factory.ArgoWorkflowClient.Requests);

        await ProcessInspectionResultInScope(message2);

        var request = Assert.Single(_factory.ArgoWorkflowClient.Requests);
        var group = await _context.AnalysisGroups.SingleAsync(
            g => g.GroupId == GroupId,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AnalysisGroupStatus.Complete, group.Status);
        Assert.Contains(Blob1, request.Arguments["inputBlobStorageLocations"]);
        Assert.Contains(Blob2, request.Arguments["inputBlobStorageLocations"]);
    }

    [Fact]
    public async Task GroupedAnalysis_TimeoutMarksGroupTimedOutAndDoesNotTrigger()
    {
        const string GroupId = "group-timeout";
        using var timeoutFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration(
                (_, config) =>
                {
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Analysis:AnalysisGroupTimeoutMinutes"] = "-1",
                        }
                    );
                }
            );
        });
        _ = timeoutFactory.Services;

        var groupMessage = _db.NewAnalysisGroupMessage(
            groupId: GroupId,
            size: 2,
            analyses: ["group-test"]
        );
        var message = _db.NewIsarInspectionResultMessage(
            inspectionId: "rec-timeout",
            requiredAnalysis: ["group-test"],
            analysisGroup: groupMessage
        );

        var handler = timeoutFactory.Services.GetRequiredService<MqttEventHandler>();
        await handler.ProcessIsarInspectionResult(message);

        using (var scope = timeoutFactory.Services.CreateScope())
        {
            var processor =
                scope.ServiceProvider.GetRequiredService<IAnalysisGroupTimeoutProcessor>();
            await processor.ProcessTimedOutGroups(TestContext.Current.CancellationToken);
        }

        var group = await _context.AnalysisGroups.SingleAsync(
            g => g.GroupId == GroupId,
            TestContext.Current.CancellationToken
        );
        var analysis = await _context.Analyses.SingleAsync(
            a => a.AnalysisGroupId == group.Id,
            TestContext.Current.CancellationToken
        );
        await _context
            .Entry(analysis)
            .Collection(a => a.Runs)
            .LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AnalysisGroupStatus.TimedOut, group.Status);
        Assert.Empty(analysis.Runs);
        Assert.Empty(_factory.ArgoWorkflowClient.Requests);
    }
}
