using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using api.Database.Context;
using api.Database.Models;
using api.MQTT;
using api.Services;
using Api.Test.Database;
using Api.Test.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Api.Test.Services;

[Collection("Operation metrics")]
public class AnalysisTriggerServiceTests : IAsyncLifetime
{
    private static readonly Guid ArgoNameTestRunId = Guid.Parse(
        "01234567-89ab-cdef-0123-456789abcdef"
    );

    [Theory]
    [InlineData("invalid-workflow", "build")]
    [InlineData("test-workflow-1", "submit")]
    public async Task WorkflowStartFailure_RecordsTheFailingStage(string workflowType, string stage)
    {
        using var metrics = new MetricsCapture();
        var record = await _db.NewInspectionRecord();
        var analysis = await _db.NewAnalysis(type: "per-record-test", inspectionRecords: [record]);
        var run = await _db.NewAnalysisRun(analysis);
        run.StartedAt = DateTime.UtcNow;
        run.Status = AnalysisRunStatus.InProgress;
        await _db.NewWorkflow(run, workflowType: workflowType);
        _factory.ArgoWorkflowClient.CreateException = new InvalidOperationException(
            "Argo unavailable"
        );
        using var scope = _factory.Services.CreateScope();

        await Assert.ThrowsAsync<WorkflowTriggerFailedException>(() =>
            scope
                .ServiceProvider.GetRequiredService<IAnalysisWorkflowService>()
                .BuildAndSubmitAsync(run.Id)
        );

        Assert.Equal(
            1,
            metrics.Count("sara_workflow_start", ("stage", stage), ("outcome", "failure"))
        );
        Assert.Equal(1, metrics.Count("sara_workflow_start", ("outcome", "failure")));
        if (stage == "submit")
            Assert.Equal(
                1,
                metrics.Count("sara_workflow_start", ("stage", "build"), ("outcome", "success"))
            );
    }

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

    private async Task OnInspectionRecordCreatedInScope(InspectionRecord record)
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAnalysisTriggerService>();
        await service.OnInspectionRecordCreated(record);
    }

    private Task<Analysis> LoadOnlyAnalysisAsync() =>
        _context
            .Analyses.Include(a => a.InspectionRecords)
            .Include(a => a.Runs)
                .ThenInclude(r => r.Workflows)
            .SingleAsync(TestContext.Current.CancellationToken);

    private static T ReadArgument<T>(ArgoDagTask task, string name) =>
        JsonSerializer.Deserialize<T>(
            task.Arguments.Parameters.Single(parameter => parameter.Name == name).Value!,
            JsonSerializerOptions.Web
        )!;

    private Task<Analysis> LoadAnalysisByNameAsync(string name) =>
        _context
            .Analyses.Include(a => a.InspectionRecords)
            .Include(a => a.Runs)
                .ThenInclude(r => r.Workflows)
            .SingleAsync(a => a.AnalysisType == name, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("Anonymize", "anonymize-0123456789abcdef0123456789abcdef")]
    [InlineData("Fencilla", "fencilla-0123456789abcdef0123456789abcdef")]
    [InlineData("CLOE", "cloe-0123456789abcdef0123456789abcdef")]
    [InlineData("ThermalReading", "thermalreading-0123456789abcdef0123456789abcdef")]
    [InlineData("Passthrough", "passthrough-0123456789abcdef0123456789abcdef")]
    [InlineData("Custom Analysis", "custom-analysis-0123456789abcdef0123456789abcdef")]
    public void ArgoWorkflowName_UsesDnsSafeAnalysisType(string analysisType, string expected)
    {
        Assert.Equal(
            expected,
            AnalysisWorkflowGraphBuilder.GetArgoWorkflowName(analysisType, ArgoNameTestRunId)
        );
    }

    [Fact]
    public async Task OnInspectionRecordCreated_NoMatchingAnalysis_DoesNothing()
    {
        var record = await _db.NewInspectionRecord(blobName: "image.dat");

        await OnInspectionRecordCreatedInScope(record);

        Assert.Empty(await _context.Analyses.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(_factory.ArgoWorkflowClient.Requests);
    }

    [Fact]
    public async Task OnInspectionRecordCreated_MixedKnownAndUnknownAnalyses_RunsKnownSkipsUnknown()
    {
        const string knownAnalysis = "per-record-test";
        var newAnalysis = await _db.NewAnalysis(type: knownAnalysis);
        var record = await _db.NewInspectionRecord(analyses: [newAnalysis]);

        await OnInspectionRecordCreatedInScope(record);

        var analysis = await LoadAnalysisByNameAsync(knownAnalysis);
        Assert.Equal(knownAnalysis, analysis.AnalysisType);
        Assert.Single(_factory.ArgoWorkflowClient.Requests);
    }

    [Fact]
    public async Task OnInspectionRecordCreated_AllUnknownAnalyses_CreatesNothing()
    {
        var record = await _db.NewInspectionRecord();

        await OnInspectionRecordCreatedInScope(record);

        Assert.Empty(await _context.Analyses.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(_factory.ArgoWorkflowClient.Requests);
    }

    [Fact]
    public async Task OnInspectionRecordCreated_NonGroupedHappyPath_CreatesAnalysisAndTriggersFirstWorkflow()
    {
        const string analysisName = "per-record-test";
        const string workflowType = "test-workflow-1";
        var newAnalysis = await _db.NewAnalysis(type: analysisName);
        var record = await _db.NewInspectionRecord(analyses: [newAnalysis]);

        await OnInspectionRecordCreatedInScope(record);

        var analysis = await LoadOnlyAnalysisAsync();
        var workflow = analysis.Runs.Single().Workflows.Single();
        Assert.Equal(workflowType, workflow.WorkflowType);

        var request = Assert.Single(_factory.ArgoWorkflowClient.Requests);
        Assert.Equal(_factory.WorkflowTemplateNameFor(workflowType), request.WorkflowTemplateName);
    }

    [Fact]
    public async Task OnInspectionRecordCreated_MultiStepChain_SubmitsOneArgoDag()
    {
        const string analysisName = "multi-step-test";
        const string firstWorkflowType = "test-workflow-1";
        const string secondWorkflowType = "test-workflow-2";
        var newAnalysis = await _db.NewAnalysis(type: analysisName);
        var record = await _db.NewInspectionRecord(analyses: [newAnalysis]);

        await OnInspectionRecordCreatedInScope(record);

        var analysis = await LoadOnlyAnalysisAsync();
        var workflows = analysis.Runs.Single().Workflows.OrderBy(w => w.StepNumber).ToList();
        Assert.Equal(
            [firstWorkflowType, secondWorkflowType],
            workflows.Select(w => w.WorkflowType)
        );

        var request = Assert.Single(_factory.ArgoWorkflowClient.Requests);
        Assert.Equal(2, request.Tasks.Count);
        var requestedOutput = ReadArgument<BlobStorageLocation>(
            request.Tasks[0],
            "outputBlobStorageLocation"
        );
        Assert.Equal(
            requestedOutput.ToString(),
            workflows[1].InputBlobStorageLocations[0].ToString()
        );
        Assert.Equal(
            [
                _factory.WorkflowTemplateNameFor(firstWorkflowType),
                _factory.WorkflowTemplateNameFor(secondWorkflowType),
            ],
            request.Tasks.Select(task => task.TemplateRef.Name)
        );
        Assert.All(request.Tasks, task => Assert.Equal("main", task.TemplateRef.Template));
        Assert.Null(request.Tasks[0].Depends);
        Assert.Contains(request.Tasks[0].Name, request.Tasks[1].Depends);
        var argoNames = await _context
            .Workflows.AsNoTracking()
            .Select(workflow => workflow.ArgoWorkflowName)
            .Distinct()
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal([request.WorkflowName], argoNames);
    }

    [Fact]
    public async Task OnInspectionRecordCreated_UsesWorkflowTypesInArgoTaskNames()
    {
        var analysis = await _db.NewAnalysis(type: "multi-step-gated-test");
        var record = await _db.NewInspectionRecord(analyses: [analysis]);

        await OnInspectionRecordCreatedInScope(record);

        var tasks = Assert.Single(_factory.ArgoWorkflowClient.Requests).Tasks;
        Assert.Matches("^test-workflow-1-[0-9a-f]{32}$", tasks[0].Name);
        Assert.Matches("^test-gate-[0-9a-f]{32}$", tasks[1].Name);
        Assert.Matches("^test-workflow-2-[0-9a-f]{32}$", tasks[2].Name);
    }

    [Fact]
    public async Task OnInspectionRecordCreated_FencillaTaskPassesInspectionMetadata()
    {
        var analysis = await _db.NewAnalysis(type: "Fencilla");
        var record = await _db.NewInspectionRecord(
            analyses: [analysis],
            tag: "test-tag",
            missionName: "Perimeterrunde - Nordsiden",
            inspectionDescription: "Perimeter 1"
        );

        await OnInspectionRecordCreatedInScope(record);

        var task = Assert
            .Single(_factory.ArgoWorkflowClient.Requests)
            .Tasks.Single(candidate => candidate.TemplateRef.Name == "fencilla");
        Assert.Equal(
            [
                "extras",
                "inputBlobStorageLocations",
                "inspectionMetadata",
                "outputBlobStorageLocation",
            ],
            task.Arguments.Parameters.Select(parameter => parameter.Name).Order()
        );
        var metadataJson = task
            .Arguments.Parameters.Single(parameter => parameter.Name == "inspectionMetadata")
            .Value;
        using var metadata = JsonDocument.Parse(metadataJson!);
        var item = Assert.Single(metadata.RootElement.EnumerateArray());
        Assert.Equal("Perimeterrunde - Nordsiden", item.GetProperty("missionName").GetString());
        Assert.Equal("Perimeter 1", item.GetProperty("inspectionDescription").GetString());
    }

    [Fact]
    public async Task OnInspectionRecordCreated_GroupedAnalysisIncomplete_DefersAndDoesNotTrigger()
    {
        const string analysisName = "group-test";

        var group = await _db.NewAnalysisGroup();
        var newAnalysis = await _db.NewAnalysis(type: analysisName, analysisGroup: group);
        var record = await _db.NewInspectionRecord(analysisGroup: group, analyses: [newAnalysis]);

        await OnInspectionRecordCreatedInScope(record);

        var analysis = await LoadOnlyAnalysisAsync();
        Assert.Empty(analysis.Runs);
        Assert.Empty(_factory.ArgoWorkflowClient.Requests);
    }

    [Fact]
    public async Task OnInspectionRecordCreated_GroupedAnalysisCompletes_TriggersDeferredAnalysisWithAllRecords()
    {
        const string analysisName = "group-test";
        var group = await _db.NewAnalysisGroup();
        var newAnalysis = await _db.NewAnalysis(type: analysisName, analysisGroup: group);
        var firstRecord = await _db.NewInspectionRecord(
            inspectionId: "inspection-1",
            analysisGroup: group,
            analyses: [newAnalysis]
        );
        var secondRecord = await _db.NewInspectionRecord(
            inspectionId: "inspection-2",
            analysisGroup: group,
            analyses: [newAnalysis]
        );

        await OnInspectionRecordCreatedInScope(firstRecord);
        await OnInspectionRecordCreatedInScope(secondRecord);

        var analysis = await LoadOnlyAnalysisAsync();
        Assert.Equal(2, analysis.InspectionRecords.Count);
        Assert.Equal(2, analysis.Runs.Single().Workflows.Single().InputBlobStorageLocations.Count);
        Assert.Single(_factory.ArgoWorkflowClient.Requests);
    }

    [Fact]
    public async Task OnInspectionRecordCreated_MixedGroupedAndNonGrouped_TriggersNonGroupedImmediately()
    {
        const string nonGroupedAnalysisType = "per-record-test";
        const string groupedAnalysisType = "group-test";
        const string firstWorkflowType = "test-workflow-1";
        var group = await _db.NewAnalysisGroup();
        var groupedAnalysis = await _db.NewAnalysis(
            type: groupedAnalysisType,
            analysisGroup: group
        );
        var nonGroupedAnalysis = await _db.NewAnalysis(type: nonGroupedAnalysisType);
        var record = await _db.NewInspectionRecord(
            analyses: [nonGroupedAnalysis, groupedAnalysis],
            analysisGroup: group
        );

        await OnInspectionRecordCreatedInScope(record);

        Assert.Empty((await LoadAnalysisByNameAsync(groupedAnalysisType)).Runs);
        Assert.Single((await LoadAnalysisByNameAsync(nonGroupedAnalysisType)).Runs);

        var request = Assert.Single(_factory.ArgoWorkflowClient.Requests);
        Assert.Equal(
            _factory.WorkflowTemplateNameFor(firstWorkflowType),
            request.WorkflowTemplateName
        );
    }

    [Fact]
    public async Task OnInspectionRecordCreated_GatedChain_PersistsDistinctBlobsPerWorkflow()
    {
        const string analysisName = "multi-step-gated-test";
        var newAnalysis = await _db.NewAnalysis(type: analysisName);
        var record = await _db.NewInspectionRecord(analyses: [newAnalysis]);

        await OnInspectionRecordCreatedInScope(record);

        var analysis = await _context
            .Analyses.Include(a => a.Runs)
                .ThenInclude(r => r.Workflows)
                    .ThenInclude(w => w.InputBlobStorageLocations)
            .SingleAsync(
                a => a.AnalysisType == analysisName,
                TestContext.Current.CancellationToken
            );

        var run = Assert.Single(analysis.Runs);
        var workflows = run.Workflows.OrderBy(w => w.StepNumber).ToList();
        Assert.Equal(3, workflows.Count);

        // Each workflow's owned inputs must be distinct CLR instances for EF tracking.
        var allOwnedBlobs = workflows.SelectMany(w => w.InputBlobStorageLocations).ToList();
        Assert.Equal(allOwnedBlobs.Count, allOwnedBlobs.Distinct().Count());

        // Step 2 is the gate; step 3 must inherit step 2's input (the pre-gate output of step 1),
        // not step 2's own output, with equal-by-value but distinct input instances.
        var tasks = Assert.Single(_factory.ArgoWorkflowClient.Requests).Tasks;
        var preGateOutput = ReadArgument<BlobStorageLocation>(
            tasks[0],
            "outputBlobStorageLocation"
        );
        var gateInput = Assert.Single(workflows[1].InputBlobStorageLocations);
        var postGateInput = Assert.Single(workflows[2].InputBlobStorageLocations);
        Assert.Equal(preGateOutput.BlobName, gateInput.BlobName);
        Assert.Equal(preGateOutput.BlobName, postGateInput.BlobName);
        Assert.NotSame(gateInput, postGateInput);

        Assert.Null(tasks[1].When);
        Assert.Contains("jsonpath", tasks[2].When);
        Assert.Contains("$.skip", tasks[2].When);
    }
}
