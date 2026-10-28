using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using api.Database.Context;
using api.Database.Models;
using api.Services;
using Api.Test.Database;
using Api.Test.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Api.Test.Services;

[Collection("Operation metrics")]
public class ArgoWorkflowEventProcessorTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;
    private TestWebApplicationFactory<Program> _factory = null!;
    private SaraDbContext _context = null!;
    private DatabaseUtilities _db = null!;

    public async ValueTask InitializeAsync()
    {
        (_container, string connectionString) =
            await TestSetupHelpers.ConfigurePostgreSqlDatabase();
        _factory = TestSetupHelpers.ConfigureWebApplicationFactory(connectionString);
        _ = _factory.Services;
        _context = TestSetupHelpers.ConfigurePostgreSqlContext(connectionString);
        _db = new DatabaseUtilities(_context);
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
        await _factory.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task NodeEvents_ReconcileEveryStepAndCompleteRun()
    {
        var analysis = await _db.NewAnalysis();
        var run = await _db.NewAnalysisRun(analysis);
        run.Status = AnalysisRunStatus.InProgress;
        var first = await _db.NewWorkflow(run, workflowType: "test-workflow-1", stepNumber: 1);
        var second = await _db.NewWorkflow(run, workflowType: "test-workflow-2", stepNumber: 2);
        SetArgoIdentity(run, "argo-uid");
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Process(
            run,
            "Succeeded",
            Node(first, "Succeeded", "{\"step\":1}"),
            Node(second, "Succeeded", "{\"step\":2}")
        );

        await _context.Entry(first).ReloadAsync(TestContext.Current.CancellationToken);
        await _context.Entry(second).ReloadAsync(TestContext.Current.CancellationToken);
        await _context.Entry(run).ReloadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(WorkflowStatus.Succeeded, first.Status);
        Assert.Equal(WorkflowStatus.Succeeded, second.Status);
        Assert.Equal("node-0", first.ArgoNodeId);
        Assert.Equal("node-1", second.ArgoNodeId);
        Assert.Equal(AnalysisRunStatus.Succeeded, run.Status);
        var acknowledged = Assert.Single(_factory.ArgoWorkflowClient.ReconciledWorkflows);
        Assert.Equal(first.ArgoWorkflowName, acknowledged.Name);
        Assert.Equal("argo-uid", acknowledged.Uid);
        Assert.Equal("10", acknowledged.ResourceVersion);
    }

    [Fact]
    public async Task GateOmittedNodes_MarkRunSkipped()
    {
        using var metrics = new MetricsCapture();
        var analysis = await _db.NewAnalysis();
        var run = await _db.NewAnalysisRun(analysis);
        run.Status = AnalysisRunStatus.InProgress;
        var gate = await _db.NewWorkflow(run, workflowType: "test-gate", stepNumber: 1);
        var downstream = await _db.NewWorkflow(run, workflowType: "test-workflow-2", stepNumber: 2);
        SetArgoIdentity(run, "argo-uid");
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Process(
            run,
            "Succeeded",
            Node(gate, "Succeeded", "{\"skip\":true}"),
            Node(downstream, "Omitted")
        );

        await _context.Entry(downstream).ReloadAsync(TestContext.Current.CancellationToken);
        await _context.Entry(run).ReloadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(WorkflowStatus.Skipped, downstream.Status);
        Assert.Equal(AnalysisRunStatus.Skipped, run.Status);
        Assert.Contains("test-gate gate matched", run.SkipReason);
        Assert.Single(_factory.ArgoWorkflowClient.ReconciledWorkflows);
        Assert.Equal(0, metrics.Count("sara_result_processing", ("outcome", "failure")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicateTerminalEvent_RetriesAcknowledgmentWithoutRedispatch(bool patchFails)
    {
        using var metrics = new MetricsCapture();
        var record = await _db.NewInspectionRecord(inspectionId: "inspection-cloe");
        var analysis = await _db.NewAnalysis(inspectionRecords: [record]);
        var run = await _db.NewAnalysisRun(analysis);
        run.Status = AnalysisRunStatus.InProgress;
        var workflow = await _db.NewWorkflow(run, workflowType: "cloe");
        SetArgoIdentity(run, "argo-uid");
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var node = Node(workflow, "Succeeded", "{\"oilLevel\":0.42,\"confidence\":0.9}");

        if (patchFails)
        {
            _factory.ArgoWorkflowClient.ReconcileException = new InvalidOperationException(
                "Patch failed"
            );
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Process(run, "Succeeded", node)
            );
            Assert.Empty(_factory.ArgoWorkflowClient.ReconciledWorkflows);
            await _context.Entry(run).ReloadAsync(TestContext.Current.CancellationToken);
            Assert.Equal(AnalysisRunStatus.Succeeded, run.Status);
            _factory.ArgoWorkflowClient.ReconcileException = null;
        }
        else
        {
            await Process(run, "Succeeded", node);
        }
        await Process(run, "Succeeded", node);

        Assert.Single(_factory.MqttPublisher.AnalysisResultMessages);
        Assert.Equal(
            1,
            metrics.Count("sara_result_processing", ("stage", "validate"), ("outcome", "success"))
        );
        Assert.Equal(patchFails ? 1 : 2, _factory.ArgoWorkflowClient.ReconciledWorkflows.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-json")]
    [InlineData("{\"outputBlobStorageLocation\":{\"storageAccount\":\"\"}}")]
    public async Task InvalidResult_CountsOneFailureAcrossDuplicateEvents(string? result)
    {
        using var metrics = new MetricsCapture();
        var analysis = await _db.NewAnalysis();
        var run = await _db.NewAnalysisRun(analysis);
        run.Status = AnalysisRunStatus.InProgress;
        var workflow = await _db.NewWorkflow(run, workflowType: "test-workflow-1");
        SetArgoIdentity(run, "argo-uid");
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Process(run, "Succeeded", Node(workflow, "Succeeded", result));
        await Process(run, "Succeeded", Node(workflow, "Succeeded", result));

        Assert.Equal(
            1,
            metrics.Count("sara_result_processing", ("stage", "validate"), ("outcome", "failure"))
        );
    }

    private const string ReadyResult = """
        {"oilLevel":0.42,"confidence":1.0,"temperature":21,"isBreak":true,
         "outputBlobStorageLocation":{"storageAccount":"outstorage","blobContainer":"output","blobName":"visualized.jpg"},
         "preProcessedBlobStorageLocation":{"storageAccount":"outstorage","blobContainer":"output","blobName":"thermal.tiff"}}
        """;

    [Theory]
    [InlineData("cloe", false)]
    [InlineData("thermal-reading", false)]
    [InlineData("fencilla", false)]
    [InlineData("anonymizer", true)]
    [InlineData("copy-raw-to-visualized", true)]
    public async Task ReadinessNotification_SeesCommittedResult(
        string workflowType,
        bool visualization
    )
    {
        var record = await _db.NewInspectionRecord();
        var analysis = await _db.NewAnalysis(inspectionRecords: [record]);
        var run = await _db.NewAnalysisRun(analysis);
        run.Status = AnalysisRunStatus.InProgress;
        var workflow = await _db.NewWorkflow(run, workflowType: workflowType);
        Workflow? downstream = null;
        if (workflowType == "anonymizer")
        {
            downstream = await _db.NewWorkflow(run, workflowType: "thermal-reading", stepNumber: 2);
        }
        SetArgoIdentity(run, "argo-uid");
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Workflow? snapshot = null;
        string? downstreamInput = null;
        _factory.MqttPublisher.BeforePublish = async id =>
        {
            snapshot = await _context
                .Workflows.AsNoTracking()
                .SingleAsync(w => w.Id == id, TestContext.Current.CancellationToken);
            if (downstream is not null)
            {
                var next = await _context
                    .Workflows.AsNoTracking()
                    .SingleAsync(w => w.Id == downstream.Id, TestContext.Current.CancellationToken);
                downstreamInput = Assert.Single(next.InputBlobStorageLocations).BlobName;
            }
        };

        await Process(run, "Running", Node(workflow, "Succeeded", ReadyResult));

        Assert.NotNull(snapshot);
        Assert.Equal(WorkflowStatus.Succeeded, snapshot.Status);
        Assert.Equal(ReadyResult, snapshot.ResultJson);
        Assert.NotNull(snapshot.CompletedAt);
        Assert.Equal("visualized.jpg", snapshot.OutputBlobStorageLocation?.BlobName);
        Assert.Equal(visualization ? 1 : 0, _factory.MqttPublisher.VisualizationMessages.Count);
        Assert.Equal(visualization ? 0 : 1, _factory.MqttPublisher.AnalysisResultMessages.Count);
        if (downstream is not null)
            Assert.Equal("thermal.tiff", downstreamInput);
        await _context.Entry(run).ReloadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(AnalysisRunStatus.InProgress, run.Status);
    }

    [Fact]
    public async Task StaleUid_IsIgnored()
    {
        var analysis = await _db.NewAnalysis();
        var run = await _db.NewAnalysisRun(analysis);
        run.Status = AnalysisRunStatus.InProgress;
        var workflow = await _db.NewWorkflow(run, workflowType: "test-workflow-1");
        SetArgoIdentity(run, "current-uid");
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Process(run, "Succeeded", Node(workflow, "Succeeded", "{}"), "stale-uid");

        await _context.Entry(workflow).ReloadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(WorkflowStatus.Pending, workflow.Status);
        Assert.Empty(_factory.ArgoWorkflowClient.ReconciledWorkflows);
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("Error")]
    public async Task FailedWorkflow_IsAcknowledgedAfterEveryStepIsReconciled(string phase)
    {
        using var metrics = new MetricsCapture();
        var analysis = await _db.NewAnalysis();
        var run = await _db.NewAnalysisRun(analysis);
        run.Status = AnalysisRunStatus.InProgress;
        var first = await _db.NewWorkflow(run, workflowType: "test-workflow-1", stepNumber: 1);
        var second = await _db.NewWorkflow(run, workflowType: "test-workflow-2", stepNumber: 2);
        SetArgoIdentity(run, "argo-uid");
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Process(run, phase, Node(first, phase));

        await _context.Entry(run).ReloadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(AnalysisRunStatus.Failed, run.Status);
        Assert.Empty(_factory.ArgoWorkflowClient.ReconciledWorkflows);

        await Process(run, phase, Node(first, phase), Node(second, "Omitted"));

        await _context.Entry(second).ReloadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(WorkflowStatus.Skipped, second.Status);
        Assert.Single(_factory.ArgoWorkflowClient.ReconciledWorkflows);
        Assert.Equal(0, metrics.Count("sara_result_processing", ("outcome", "failure")));
    }

    [Theory]
    [InlineData("Running", "Succeeded")]
    [InlineData("Succeeded", "Running")]
    public async Task IncompleteReconciliation_IsNotAcknowledged(string phase, string nodePhase)
    {
        var analysis = await _db.NewAnalysis();
        var run = await _db.NewAnalysisRun(analysis);
        run.Status = AnalysisRunStatus.InProgress;
        var workflow = await _db.NewWorkflow(run, workflowType: "test-workflow-1");
        SetArgoIdentity(run, "argo-uid");
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Process(run, phase, Node(workflow, nodePhase, "{}"));

        await _context.Entry(run).ReloadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(AnalysisRunStatus.InProgress, run.Status);
        Assert.Empty(_factory.ArgoWorkflowClient.ReconciledWorkflows);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task AcknowledgedOrDeletedWorkflow_IsNotPatched(bool reconciled, bool deleted)
    {
        var analysis = await _db.NewAnalysis();
        var run = await _db.NewAnalysisRun(analysis);
        run.Status = AnalysisRunStatus.InProgress;
        var workflow = await _db.NewWorkflow(run, workflowType: "test-workflow-1");
        SetArgoIdentity(run, "argo-uid");
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Process(
            run,
            "Succeeded",
            [Node(workflow, "Succeeded", "{}")],
            "argo-uid",
            reconciled,
            deleted
        );

        await _context.Entry(workflow).ReloadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            reconciled ? WorkflowStatus.Pending : WorkflowStatus.Succeeded,
            workflow.Status
        );
        Assert.Empty(_factory.ArgoWorkflowClient.ReconciledWorkflows);
    }

    private void SetArgoIdentity(AnalysisRun run, string uid)
    {
        foreach (var workflow in run.Workflows)
        {
            workflow.ArgoWorkflowName = AnalysisWorkflowGraphBuilder.GetArgoWorkflowName(
                run.Analysis.AnalysisType,
                run.Id
            );
            workflow.ArgoWorkflowUid = uid;
        }
    }

    private static ArgoNodeStatus Node(Workflow workflow, string phase, string? result = null) =>
        new()
        {
            DisplayName = AnalysisWorkflowGraphBuilder.GetTaskName(workflow),
            Type = "DAG",
            Phase = phase,
            Outputs = result is null
                ? null
                : new ArgoOutputs
                {
                    Parameters = [new ArgoParameter { Name = "result", Value = result }],
                },
        };

    private async Task Process(AnalysisRun run, string phase, params ArgoNodeStatus[] nodes) =>
        await Process(run, phase, nodes, "argo-uid");

    private async Task Process(AnalysisRun run, string phase, ArgoNodeStatus node, string uid) =>
        await Process(run, phase, [node], uid);

    private async Task Process(
        AnalysisRun run,
        string phase,
        ArgoNodeStatus[] nodes,
        string uid,
        bool reconciled = false,
        bool deleted = false
    )
    {
        using var scope = _factory.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<IArgoWorkflowEventProcessor>();
        await processor.HandleWorkflowEventAsync(
            new ArgoWorkflowResource
            {
                IsDeleted = deleted,
                Metadata = new ArgoObjectMetadata
                {
                    Name = AnalysisWorkflowGraphBuilder.GetArgoWorkflowName(
                        run.Analysis.AnalysisType,
                        run.Id
                    ),
                    Uid = uid,
                    ResourceVersion = "10",
                    Labels = new Dictionary<string, string>
                    {
                        [ArgoWorkflowClient.AnalysisRunIdLabel] = run.Id.ToString(),
                        [ArgoWorkflowClient.ReconciledLabel] = reconciled ? "true" : "false",
                    },
                },
                Status = new ArgoWorkflowStatus
                {
                    Phase = phase,
                    Nodes = nodes
                        .Select((node, index) => (node, index))
                        .ToDictionary(pair => $"node-{pair.index}", pair => pair.node),
                },
            },
            TestContext.Current.CancellationToken
        );
    }
}
