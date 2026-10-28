using System.Text.Json;
using api.Database.Models;
using api.Telemetry;

namespace api.Services.ResultHandlers.WorkflowResultHandlers;

public interface IWorkflowResultHandler
{
    public string WorkflowType { get; }

    public Task OnWorkflowCompleted(Workflow workflow);
}

internal static class WorkflowResultHandlerHelpers
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static T? DeserializeResult<T>(Workflow workflow, ILogger logger)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(workflow.ResultJson))
        {
            SaraMetrics.RecordResultProcessing(
                workflow.WorkflowType,
                "deserialize",
                OperationOutcome.Failure
            );
            logger.LogWarning(
                "Workflow {WorkflowType} (Id: {WorkflowId}) has no ResultJson",
                workflow.WorkflowType,
                workflow.Id
            );
            return null;
        }

        try
        {
            var result = JsonSerializer.Deserialize<T>(workflow.ResultJson, JsonOptions);
            SaraMetrics.RecordResultProcessing(
                workflow.WorkflowType,
                "deserialize",
                result is null ? OperationOutcome.Failure : OperationOutcome.Success
            );
            return result;
        }
        catch (JsonException ex)
        {
            SaraMetrics.RecordResultProcessing(
                workflow.WorkflowType,
                "deserialize",
                OperationOutcome.Failure
            );
            logger.LogError(
                ex,
                "Failed to deserialize ResultJson for workflow {WorkflowType} (Id: {WorkflowId}) as {ResultType}: {Json}",
                workflow.WorkflowType,
                workflow.Id,
                typeof(T).Name,
                workflow.ResultJson
            );
            return null;
        }
    }
}
