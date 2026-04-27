using System.Text;
using System.Text.Json;
using api.Controllers.WorkflowNotification;
using api.Database.Models;
using api.Utilities;

namespace api.Services;

public record TriggerAnonymizerRequest(
    string InspectionId,
    BlobStorageLocation RawDataBlobStorageLocation,
    BlobStorageLocation AnonymizedBlobStorageLocation,
    BlobStorageLocation? PreProcessedBlobStorageLocation
);

public record TriggerCLOERequest(
    string InspectionId,
    BlobStorageLocation SourceBlobStorageLocation,
    BlobStorageLocation VisualizedBlobStorageLocation
);

public record TriggerFencillaRequest(
    string InspectionId,
    BlobStorageLocation SourceBlobStorageLocation,
    BlobStorageLocation VisualizedBlobStorageLocation
);

public record TriggerThermalReadingRequest(
    string InspectionId,
    string TagId,
    string InspectionDescription,
    string InstallationCode,
    BlobStorageLocation SourceBlobStorageLocation,
    BlobStorageLocation VisualizedBlobStorageLocation
);

public interface IArgoWorkflowService
{
    public Task TriggerAnonymizer(string inspectionId, WorkflowStep workflowStep);
    public Task TriggerCLOE(string inspectionId, WorkflowStep workflowStep);
    public Task TriggerFencilla(string inspectionId, WorkflowStep workflowStep);
    public Task TriggerThermalReading(
        string inspectionId,
        string tagId,
        string inspectionDescription,
        string installationCode,
        WorkflowStep workflowStep
    );
    public WorkflowStatus GetWorkflowStatus(
        WorkflowExitedNotification notification,
        string workflowType
    );
}

public class ArgoWorkflowService(IConfiguration configuration, ILogger<ArgoWorkflowService> logger)
    : IArgoWorkflowService
{
    private static readonly HttpClient client = new();
    private readonly string _baseUrlAnonymizer =
        configuration["ArgoWorkflowAnonymizerBaseUrl"]
        ?? throw new InvalidOperationException("ArgoWorkflowAnonymizerBaseUrl is not configured.");
    private readonly string _baseUrlCLOE =
        configuration["ArgoWorkflowCLOEBaseUrl"]
        ?? throw new InvalidOperationException("ArgoWorkflowCLOEBaseUrl is not configured.");
    private readonly string _baseUrlFencilla =
        configuration["ArgoWorkflowFencillaBaseUrl"]
        ?? throw new InvalidOperationException("ArgoWorkflowFencillaBaseUrl is not configured.");
    private readonly string _baseUrlThermalReading =
        configuration["ArgoWorkflowThermalReadingBaseUrl"]
        ?? throw new InvalidOperationException(
            "ArgoWorkflowThermalReadingBaseUrl is not configured."
        );

    private static readonly JsonSerializerOptions useCamelCaseOption = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static void EnsureStepType(WorkflowStep workflowStep, WorkflowStepType expectedStepType)
    {
        if (workflowStep.Type != expectedStepType)
        {
            throw new InvalidOperationException(
                $"Expected workflow step type {expectedStepType} but got {workflowStep.Type}."
            );
        }
    }

    public async Task TriggerAnonymizer(string inspectionId, WorkflowStep workflowStep)
    {
        EnsureStepType(workflowStep, WorkflowStepType.Anonymization);

        var postRequestData = new TriggerAnonymizerRequest(
            InspectionId: inspectionId,
            RawDataBlobStorageLocation: workflowStep.SourceBlobStorageLocation,
            AnonymizedBlobStorageLocation: workflowStep.DestinationBlobStorageLocation,
            PreProcessedBlobStorageLocation: workflowStep
                .AnonymizationData
                ?.PreProcessedBlobStorageLocation
        );

        var json = JsonSerializer.Serialize(postRequestData, useCamelCaseOption);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        logger.LogInformation(
            "Triggering Anonymizer. InspectionId: {InspectionId}, "
                + "RawDataBlobStorageLocation: {RawDataBlobStorageLocation}, "
                + "AnonymizedBlobStorageLocation: {AnonymizedBlobStorageLocation}, "
                + "PreProcessedBlobStorageLocation: {PreProcessedBlobStorageLocation}",
            inspectionId,
            workflowStep.SourceBlobStorageLocation,
            workflowStep.DestinationBlobStorageLocation,
            workflowStep.AnonymizationData?.PreProcessedBlobStorageLocation
        );

        var response = await client.PostAsync(_baseUrlAnonymizer, content);

        if (response.IsSuccessStatusCode)
        {
            logger.LogInformation("Function triggered successfully.");
        }
        else
        {
            logger.LogError("Failed to trigger function.");
        }
    }

    public async Task TriggerCLOE(string inspectionId, WorkflowStep workflowStep)
    {
        EnsureStepType(workflowStep, WorkflowStepType.CLOEAnalysis);

        var postRequestData = new TriggerCLOERequest(
            InspectionId: inspectionId,
            SourceBlobStorageLocation: workflowStep.SourceBlobStorageLocation,
            VisualizedBlobStorageLocation: workflowStep.DestinationBlobStorageLocation
        );

        var json = JsonSerializer.Serialize(postRequestData, useCamelCaseOption);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        logger.LogInformation(
            "Triggering CLOE. InspectionId: {InspectionId}, "
                + "SourceBlobStorageLocation: {SourceBlobStorageLocation}, "
                + "VisualizedBlobStorageLocation: {VisualizedBlobStorageLocation}",
            inspectionId,
            workflowStep.SourceBlobStorageLocation,
            workflowStep.DestinationBlobStorageLocation
        );

        var response = await client.PostAsync(_baseUrlCLOE, content);

        if (response.IsSuccessStatusCode)
        {
            logger.LogInformation("Function triggered successfully.");
        }
        else
        {
            logger.LogError("Failed to trigger function.");
        }
    }

    public async Task TriggerFencilla(string inspectionId, WorkflowStep workflowStep)
    {
        EnsureStepType(workflowStep, WorkflowStepType.FencillaAnalysis);

        var postRequestData = new TriggerFencillaRequest(
            InspectionId: inspectionId,
            SourceBlobStorageLocation: workflowStep.SourceBlobStorageLocation,
            VisualizedBlobStorageLocation: workflowStep.DestinationBlobStorageLocation
        );

        var json = JsonSerializer.Serialize(postRequestData, useCamelCaseOption);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        logger.LogInformation(
            "Triggering Fencilla. InspectionId: {InspectionId}, "
                + "SourceBlobStorageLocation: {SourceBlobStorageLocation}, "
                + "VisualizedBlobStorageLocation: {VisualizedBlobStorageLocation}",
            inspectionId,
            workflowStep.SourceBlobStorageLocation,
            workflowStep.DestinationBlobStorageLocation
        );

        var response = await client.PostAsync(_baseUrlFencilla, content);

        if (response.IsSuccessStatusCode)
        {
            logger.LogInformation("Function triggered successfully.");
        }
        else
        {
            logger.LogError("Failed to trigger function.");
        }
    }

    public async Task TriggerThermalReading(
        string inspectionId,
        string tagId,
        string inspectionDescription,
        string installationCode,
        WorkflowStep workflowStep
    )
    {
        EnsureStepType(workflowStep, WorkflowStepType.ThermalReadingAnalysis);

        var postRequestData = new TriggerThermalReadingRequest(
            InspectionId: inspectionId,
            TagId: tagId,
            InspectionDescription: inspectionDescription,
            InstallationCode: installationCode,
            SourceBlobStorageLocation: workflowStep.SourceBlobStorageLocation,
            VisualizedBlobStorageLocation: workflowStep.DestinationBlobStorageLocation
        );

        var json = JsonSerializer.Serialize(postRequestData, useCamelCaseOption);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        logger.LogInformation(
            "Triggering ThermalReading. InspectionId: {InspectionId}, TagId: {TagId}, "
                + "InspectionDescription: {InspectionDescription}, InstallationCode: {InstallationCode}, "
                + "SourceBlobStorageLocation: {SourceBlobStorageLocation}, "
                + "VisualizedBlobStorageLocation: {VisualizedBlobStorageLocation}",
            inspectionId,
            tagId,
            inspectionDescription,
            installationCode,
            workflowStep.SourceBlobStorageLocation,
            workflowStep.DestinationBlobStorageLocation
        );

        var response = await client.PostAsync(_baseUrlThermalReading, content);

        if (response.IsSuccessStatusCode)
        {
            logger.LogInformation("Function triggered successfully.");
        }
        else
        {
            logger.LogError("Failed to trigger function.");
        }
    }

    public WorkflowStatus GetWorkflowStatus(
        WorkflowExitedNotification notification,
        string workflowType
    )
    {
        var inspectionId = Sanitize.SanitizeUserInput(notification.InspectionId);
        var workflowFailures = Sanitize.SanitizeUserInput(notification.WorkflowFailures);

        if (
            notification.ExitHandlerWorkflowStatus == ExitHandlerWorkflowStatus.Failed
            || notification.ExitHandlerWorkflowStatus == ExitHandlerWorkflowStatus.Error
        )
        {
            logger.LogWarning(
                "{WorkflowType} workflow for InspectionId: {InspectionId} exited with status: {Status} and failures: {WorkflowFailures}.",
                workflowType,
                inspectionId,
                notification.ExitHandlerWorkflowStatus,
                workflowFailures
            );
            return WorkflowStatus.ExitFailure;
        }
        else
        {
            logger.LogInformation(
                "{WorkflowType} workflow for InspectionId: {InspectionId} exited successfully",
                workflowType,
                inspectionId
            );
            return WorkflowStatus.ExitSuccess;
        }
    }
}
