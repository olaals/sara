using System.Text.Json;
using api.Configurations;
using api.Database.Context;
using api.Database.Models;
using api.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace api.Services;

/// <summary>
/// Reconciles Argo Workflow events with SARA by updating workflow-step and
/// analysis-run statuses, persisting step results, and dispatching completion handlers.
/// Stale, duplicate, and unrelated events are ignored.
/// </summary>
public interface IArgoWorkflowEventProcessor
{
    Task HandleWorkflowEventAsync(
        ArgoWorkflowResource resource,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Reconciles one generated Argo Workflow and its DAG nodes into SARA.</summary>
public class ArgoWorkflowEventProcessor(
    SaraDbContext context,
    IWorkflowService workflowService,
    IArgoWorkflowClient argoWorkflowClient,
    IOptions<AnalysisOptions> analysisOptions,
    ILogger<ArgoWorkflowEventProcessor> logger
) : IArgoWorkflowEventProcessor
{
    private readonly AnalysisOptions _options = analysisOptions.Value;

    public async Task HandleWorkflowEventAsync(
        ArgoWorkflowResource resource,
        CancellationToken cancellationToken = default
    )
    {
        if (
            resource.Metadata.Labels.GetValueOrDefault(ArgoWorkflowClient.ReconciledLabel) == "true"
        )
        {
            return;
        }

        var identity = ReadIdentity(resource);
        if (identity is null)
        {
            logger.LogWarning("Ignoring Argo Workflow with missing or invalid SARA identity");
            return;
        }

        var workflows = await context
            .Workflows.AsNoTracking()
            .Where(workflow => workflow.AnalysisRunId == identity.Value.AnalysisRunId)
            .OrderBy(workflow => workflow.StepNumber)
            .ToListAsync(cancellationToken);
        if (
            workflows.Count == 0
            || workflows.Any(workflow =>
                workflow.ArgoWorkflowName != identity.Value.Name
                || (
                    workflow.ArgoWorkflowUid is not null
                    && workflow.ArgoWorkflowUid != identity.Value.Uid
                )
            )
        )
        {
            logger.LogDebug(
                "Ignoring stale Argo Workflow event for AnalysisRun {AnalysisRunId}",
                identity.Value.AnalysisRunId
            );
            return;
        }

        await RecoverUidAsync(identity.Value, cancellationToken);
        foreach (var workflow in workflows)
        {
            var nodeEntry = resource.Status?.Nodes.FirstOrDefault(candidate =>
                candidate.Value.DisplayName == AnalysisWorkflowGraphBuilder.GetTaskName(workflow)
            );
            if (nodeEntry is not { Key: { Length: > 0 } nodeId, Value: { } node })
            {
                continue;
            }

            await PersistNodeIdAsync(workflow.Id, nodeId, cancellationToken);

            if (node.Phase is "Pending" or "Running")
            {
                await MarkInProgressAsync(workflow.Id, node, cancellationToken);
            }
            else if (node.Phase is "Succeeded" or "Failed" or "Error" or "Skipped" or "Omitted")
            {
                await CompleteWorkflowAsync(workflow, node, cancellationToken);
            }
        }

        await CompleteAnalysisRunAsync(resource, identity.Value.AnalysisRunId, cancellationToken);

        // A failed run may still have unresolved steps. Only acknowledge fully persisted state,
        // including on retries where the database committed but the previous label patch failed.
        if (
            !resource.IsDeleted
            && resource.Status?.Phase is "Succeeded" or "Failed" or "Error"
            && await context.AnalysisRuns.AnyAsync(
                run =>
                    run.Id == identity.Value.AnalysisRunId
                    && (
                        run.Status == AnalysisRunStatus.Succeeded
                        || run.Status == AnalysisRunStatus.Failed
                        || run.Status == AnalysisRunStatus.Skipped
                    )
                    && !run.Workflows.Any(workflow =>
                        workflow.Status == WorkflowStatus.Pending
                        || workflow.Status == WorkflowStatus.InProgress
                    ),
                cancellationToken
            )
        )
        {
            await argoWorkflowClient.MarkWorkflowReconciledAsync(
                resource.Metadata,
                cancellationToken
            );
        }
    }

    private async Task PersistNodeIdAsync(
        Guid workflowId,
        string nodeId,
        CancellationToken cancellationToken
    )
    {
        await context
            .Workflows.Where(workflow => workflow.Id == workflowId && workflow.ArgoNodeId != nodeId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(workflow => workflow.ArgoNodeId, nodeId),
                cancellationToken
            );
    }

    private async Task CompleteWorkflowAsync(
        Workflow workflow,
        ArgoNodeStatus node,
        CancellationToken cancellationToken
    )
    {
        var completion = ReadCompletion(node);
        var output = completion.OutputBlobStorageLocation;
        var completed = await context
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(async () =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(
                    cancellationToken
                );
                var updated = await context
                    .Workflows.Where(candidate =>
                        candidate.Id == workflow.Id
                        && (
                            candidate.Status == WorkflowStatus.Pending
                            || candidate.Status == WorkflowStatus.InProgress
                        )
                    )
                    .ExecuteUpdateAsync(
                        setters =>
                            setters
                                .SetProperty(candidate => candidate.Status, completion.Status)
                                .SetProperty(
                                    candidate => candidate.ResultJson,
                                    completion.ResultJson
                                )
                                .SetProperty(
                                    candidate => candidate.ErrorMessage,
                                    completion.ErrorMessage
                                )
                                .SetProperty(
                                    candidate =>
                                        candidate.OutputBlobStorageLocation!.StorageAccount,
                                    output == null ? null! : output.StorageAccount
                                )
                                .SetProperty(
                                    candidate => candidate.OutputBlobStorageLocation!.BlobContainer,
                                    output == null ? null! : output.BlobContainer
                                )
                                .SetProperty(
                                    candidate => candidate.OutputBlobStorageLocation!.BlobName,
                                    output == null ? null! : output.BlobName
                                )
                                .SetProperty(
                                    candidate => candidate.StartedAt,
                                    candidate =>
                                        node.StartedAt ?? candidate.StartedAt ?? DateTime.UtcNow
                                )
                                .SetProperty(
                                    candidate => candidate.CompletedAt,
                                    node.FinishedAt ?? DateTime.UtcNow
                                ),
                        cancellationToken
                    );
                if (updated == 0)
                {
                    return (Updated: false, Workflow: (Workflow?)null);
                }

                Workflow? completedWorkflow = null;
                if (completion.Status == WorkflowStatus.Succeeded)
                {
                    completedWorkflow = await context
                        .Workflows.AsNoTracking()
                        .Include(candidate => candidate.AnalysisRun)
                        .SingleAsync(candidate => candidate.Id == workflow.Id, cancellationToken);
                }
                await transaction.CommitAsync(cancellationToken);
                return (Updated: true, Workflow: completedWorkflow);
            });

        // Readiness consumers must see committed results. Keep external side effects outside
        // the execution strategy so a transaction retry cannot publish or upload twice.
        // Count only the committed transition, not duplicate watch events or transaction retries.
        // Ordinary Argo execution failures are covered by the controller alert instead.
        if (completed.Updated && node.Phase == "Succeeded")
        {
            SaraMetrics.RecordResultProcessing(
                workflow.WorkflowType,
                "validate",
                completion.Status == WorkflowStatus.Succeeded && !completion.InvalidOutputReference
                    ? OperationOutcome.Success
                    : OperationOutcome.Failure
            );
        }
        if (completed.Workflow is not null)
        {
            await workflowService.OnWorkflowCompleted(completed.Workflow);
        }
    }

    private async Task MarkInProgressAsync(
        Guid workflowId,
        ArgoNodeStatus node,
        CancellationToken cancellationToken
    )
    {
        await context
            .Workflows.Where(workflow =>
                workflow.Id == workflowId && workflow.Status == WorkflowStatus.Pending
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(workflow => workflow.Status, WorkflowStatus.InProgress)
                        .SetProperty(
                            workflow => workflow.StartedAt,
                            node.StartedAt ?? DateTime.UtcNow
                        ),
                cancellationToken
            );
    }

    private async Task CompleteAnalysisRunAsync(
        ArgoWorkflowResource resource,
        Guid analysisRunId,
        CancellationToken cancellationToken
    )
    {
        if (resource.Status?.Phase is not ("Succeeded" or "Failed" or "Error"))
        {
            return;
        }

        var workflows = await context
            .Workflows.AsNoTracking()
            .Where(workflow => workflow.AnalysisRunId == analysisRunId)
            .ToListAsync(cancellationToken);
        var failed =
            workflows.Any(workflow => workflow.Status == WorkflowStatus.Failed)
            || resource.Status.Phase is "Failed" or "Error";
        if (
            !failed
            && workflows.Any(workflow =>
                workflow.Status is WorkflowStatus.Pending or WorkflowStatus.InProgress
            )
        )
        {
            logger.LogWarning(
                "Deferring completion of AnalysisRun {AnalysisRunId} because one or more Argo nodes have not been reconciled",
                analysisRunId
            );
            return;
        }
        var skipped =
            !failed && workflows.Any(workflow => workflow.Status == WorkflowStatus.Skipped);
        var status =
            failed ? AnalysisRunStatus.Failed
            : skipped ? AnalysisRunStatus.Skipped
            : AnalysisRunStatus.Succeeded;
        var skipReason = skipped ? GetSkipReason(workflows) : null;

        var updated = await context
            .AnalysisRuns.Where(run =>
                run.Id == analysisRunId && run.Status == AnalysisRunStatus.InProgress
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(run => run.Status, status)
                        .SetProperty(run => run.SkipReason, skipReason)
                        .SetProperty(run => run.CompletedAt, DateTime.UtcNow),
                cancellationToken
            );
        if (updated == 0 || status != AnalysisRunStatus.Succeeded)
        {
            return;
        }

        var run = await context
            .AnalysisRuns.AsNoTracking()
            .Include(candidate => candidate.Workflows)
            .SingleAsync(candidate => candidate.Id == analysisRunId, cancellationToken);
        await workflowService.OnAnalysisCompleted(run);
    }

    private string? GetSkipReason(IReadOnlyList<Workflow> workflows)
    {
        foreach (var workflow in workflows.OrderBy(candidate => candidate.StepNumber))
        {
            if (
                !_options.Workflows.TryGetValue(workflow.WorkflowType, out var config)
                || config.SkipChainIf is null
                || workflow.ResultJson is null
            )
            {
                continue;
            }

            try
            {
                using var result = JsonDocument.Parse(workflow.ResultJson);
                if (
                    result.RootElement.TryGetProperty(
                        config.SkipChainIf.ResultJsonKeyToCheckForSkipBoolean,
                        out var value
                    )
                    && string.Equals(
                        value.ToString(),
                        config.SkipChainIf.Value,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return $"{workflow.WorkflowType} gate matched: {config.SkipChainIf.ResultJsonKeyToCheckForSkipBoolean}={config.SkipChainIf.Value}";
                }
            }
            catch (JsonException) { }
        }
        return "Downstream workflows were skipped by an Argo gate";
    }

    private Task RecoverUidAsync(WorkflowIdentity identity, CancellationToken cancellationToken) =>
        context
            .Workflows.Where(workflow =>
                workflow.AnalysisRunId == identity.AnalysisRunId
                && workflow.ArgoWorkflowName == identity.Name
                && workflow.ArgoWorkflowUid == null
            )
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(workflow => workflow.ArgoWorkflowUid, identity.Uid),
                cancellationToken
            );

    private WorkflowCompletion ReadCompletion(ArgoNodeStatus node)
    {
        if (node.Phase is "Skipped" or "Omitted")
        {
            return new WorkflowCompletion(WorkflowStatus.Skipped, null, node.Message);
        }
        if (node.Phase is "Failed" or "Error")
        {
            return new WorkflowCompletion(WorkflowStatus.Failed, null, node.Message);
        }

        var resultJson = node
            .Outputs?.Parameters.FirstOrDefault(parameter => parameter.Name == "result")
            ?.Value;
        try
        {
            if (resultJson is null)
            {
                throw new JsonException("Result parameter is missing");
            }
            using var result = JsonDocument.Parse(resultJson);
            var output = ReadOutputBlobStorageLocation(
                result.RootElement,
                node.DisplayName,
                out var invalidOutput
            );
            return new WorkflowCompletion(
                WorkflowStatus.Succeeded,
                resultJson,
                null,
                output,
                invalidOutput
            );
        }
        catch (JsonException ex)
        {
            return new WorkflowCompletion(
                WorkflowStatus.Failed,
                null,
                $"Workflow succeeded without a valid JSON result: {ex.Message}"
            );
        }
    }

    private BlobStorageLocation? ReadOutputBlobStorageLocation(
        JsonElement result,
        string? nodeName,
        out bool invalidOutput
    )
    {
        invalidOutput = false;
        if (
            result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("outputBlobStorageLocation", out var output)
            || output.ValueKind == JsonValueKind.Null
        )
        {
            return null;
        }

        try
        {
            var location = output.Deserialize<BlobStorageLocation>(JsonSerializerOptions.Web);
            if (
                location is not null
                && !string.IsNullOrWhiteSpace(location.StorageAccount)
                && !string.IsNullOrWhiteSpace(location.BlobContainer)
                && !string.IsNullOrWhiteSpace(location.BlobName)
            )
            {
                return location;
            }
        }
        catch (JsonException) { }

        invalidOutput = true;
        logger.LogError(
            "Argo node {NodeName} reported an invalid outputBlobStorageLocation; expected nonblank storageAccount, blobContainer and blobName strings. Keeping the result without an output reference",
            nodeName
        );
        return null;
    }

    private static WorkflowIdentity? ReadIdentity(ArgoWorkflowResource resource)
    {
        if (
            resource.Metadata.Name is not { } name
            || resource.Metadata.Uid is not { } uid
            || !resource.Metadata.Labels.TryGetValue(
                ArgoWorkflowClient.AnalysisRunIdLabel,
                out var analysisRunIdText
            )
            || !Guid.TryParse(analysisRunIdText, out var analysisRunId)
        )
        {
            return null;
        }
        return new WorkflowIdentity(analysisRunId, name, uid);
    }

    private readonly record struct WorkflowIdentity(Guid AnalysisRunId, string Name, string Uid);

    private readonly record struct WorkflowCompletion(
        WorkflowStatus Status,
        string? ResultJson,
        string? ErrorMessage,
        BlobStorageLocation? OutputBlobStorageLocation = null,
        bool InvalidOutputReference = false
    );
}
