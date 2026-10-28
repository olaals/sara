using api.Database.Context;
using api.Database.Models;
using api.Telemetry;
using Microsoft.EntityFrameworkCore;

namespace api.Services;

public interface IAnalysisWorkflowService
{
    Task BuildAndSubmitAsync(Guid analysisRunId);
}

/// <summary>Submits one generated Argo Workflow for an entire analysis run.</summary>
public class AnalysisWorkflowService(
    SaraDbContext context,
    IAnalysisWorkflowGraphBuilder graphBuilder,
    IArgoWorkflowClient argoWorkflowClient,
    ILogger<AnalysisWorkflowService> logger
) : IAnalysisWorkflowService
{
    public async Task BuildAndSubmitAsync(Guid analysisRunId)
    {
        AnalysisRun? run = null;
        var stage = "build";
        try
        {
            // Each service has its own context; track the inputs here before the builder changes them.
            run = await context
                .AnalysisRuns.Include(candidate => candidate.Analysis)
                .Include(candidate => candidate.Workflows)
                .SingleAsync(candidate => candidate.Id == analysisRunId);
            var resource = await graphBuilder.BuildArgoWorkflowAsync(run);
            SaraMetrics.RecordWorkflowStart(stage, OperationOutcome.Success);
            stage = "persist_inputs";
            await context.SaveChangesAsync();
            var argoName = resource.Metadata.Name!;
            await context
                .Workflows.Where(workflow => workflow.AnalysisRunId == analysisRunId)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(workflow => workflow.ArgoWorkflowName, argoName)
                );

            SaraMetrics.RecordWorkflowStart(stage, OperationOutcome.Success);
            stage = "submit";
            var created = await argoWorkflowClient.CreateWorkflowAsync(resource);
            SaraMetrics.RecordWorkflowStart(stage, OperationOutcome.Success);
            stage = "persist_identity";
            await context
                .Workflows.Where(workflow => workflow.AnalysisRunId == analysisRunId)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(workflow => workflow.ArgoWorkflowUid, created.Uid)
                );
            SaraMetrics.RecordWorkflowStart(stage, OperationOutcome.Success);
            logger.LogInformation(
                "Submitted Argo Workflow {ArgoWorkflowName} for AnalysisRun {AnalysisRunId} with {StepCount} steps",
                created.Name,
                analysisRunId,
                run.Workflows.Count
            );
        }
        catch (Exception ex)
        {
            // Record before failure persistence: a database outage must not hide the original failure.
            SaraMetrics.RecordWorkflowStart(stage, OperationOutcome.Failure);
            if (run is not null)
            {
                var firstWorkflow = run
                    .Workflows.OrderBy(workflow => workflow.StepNumber)
                    .FirstOrDefault();
                if (firstWorkflow is not null)
                {
                    await context
                        .Workflows.Where(workflow => workflow.Id == firstWorkflow.Id)
                        .ExecuteUpdateAsync(setters =>
                            setters
                                .SetProperty(workflow => workflow.Status, WorkflowStatus.Failed)
                                .SetProperty(workflow => workflow.ErrorMessage, ex.Message)
                                .SetProperty(workflow => workflow.CompletedAt, DateTime.UtcNow)
                        );
                }
                await context
                    .AnalysisRuns.Where(candidate => candidate.Id == analysisRunId)
                    .ExecuteUpdateAsync(setters =>
                        setters
                            .SetProperty(candidate => candidate.Status, AnalysisRunStatus.Failed)
                            .SetProperty(candidate => candidate.CompletedAt, DateTime.UtcNow)
                    );
            }
            throw new WorkflowTriggerFailedException(
                $"Failed to submit analysis run '{analysisRunId}' to Argo Workflows",
                ex
            );
        }
    }
}
