using api.Database.Models;

namespace api.Tests;

internal static class WorkflowTestFactory
{
    public static BlobStorageLocation CreateLocation(
        string blobName,
        string storageAccount = "storage",
        string blobContainer = "container"
    ) =>
        new()
        {
            StorageAccount = storageAccount,
            BlobContainer = blobContainer,
            BlobName = blobName,
        };

    public static WorkflowStep CreateAnonymizationStep(
        WorkflowStatus status = WorkflowStatus.NotStarted,
        BlobStorageLocation? source = null,
        BlobStorageLocation? destination = null,
        bool? isPersonInImage = null,
        BlobStorageLocation? preProcessedBlobStorageLocation = null
    )
    {
        return new WorkflowStep
        {
            Type = WorkflowStepType.Anonymization,
            Status = status,
            SourceBlobStorageLocation = source ?? CreateLocation("source.jpg", "source-account"),
            DestinationBlobStorageLocation =
                destination ?? CreateLocation("destination.jpg", "destination-account"),
            AnonymizationData = new AnonymizationData
            {
                IsPersonInImage = isPersonInImage,
                PreProcessedBlobStorageLocation = preProcessedBlobStorageLocation,
            },
            Workflow = null!,
        };
    }

    public static WorkflowStep CreateCLOEStep(
        WorkflowStatus status = WorkflowStatus.NotStarted,
        BlobStorageLocation? source = null,
        BlobStorageLocation? destination = null,
        float? oilLevel = null,
        float? confidence = null
    )
    {
        return new WorkflowStep
        {
            Type = WorkflowStepType.CLOEAnalysis,
            Status = status,
            SourceBlobStorageLocation =
                source ?? CreateLocation("cloe-source.jpg", "source-account"),
            DestinationBlobStorageLocation =
                destination ?? CreateLocation("cloe-destination.jpg", "destination-account"),
            CLOEData = new CLOEData { OilLevel = oilLevel, Confidence = confidence },
            Workflow = null!,
        };
    }

    public static WorkflowStep CreateFencillaStep(
        WorkflowStatus status = WorkflowStatus.NotStarted,
        BlobStorageLocation? source = null,
        BlobStorageLocation? destination = null,
        bool? isBreak = null,
        float? confidence = null
    )
    {
        return new WorkflowStep
        {
            Type = WorkflowStepType.FencillaAnalysis,
            Status = status,
            SourceBlobStorageLocation =
                source ?? CreateLocation("fencilla-source.jpg", "source-account"),
            DestinationBlobStorageLocation =
                destination ?? CreateLocation("fencilla-destination.jpg", "destination-account"),
            FencillaData = new FencillaData { IsBreak = isBreak, Confidence = confidence },
            Workflow = null!,
        };
    }

    public static WorkflowStep CreateThermalReadingStep(
        WorkflowStatus status = WorkflowStatus.NotStarted,
        BlobStorageLocation? source = null,
        BlobStorageLocation? destination = null,
        float? temperature = null
    )
    {
        return new WorkflowStep
        {
            Type = WorkflowStepType.ThermalReadingAnalysis,
            Status = status,
            SourceBlobStorageLocation =
                source ?? CreateLocation("thermal-source.jpg", "source-account"),
            DestinationBlobStorageLocation =
                destination ?? CreateLocation("thermal-destination.jpg", "destination-account"),
            ThermalReadingData = new ThermalReadingData { Temperature = temperature },
            Workflow = null!,
        };
    }

    public static PlantData CreatePlantData(
        string inspectionId,
        string installationCode,
        params WorkflowStep[] steps
    )
    {
        var plantData = new PlantData
        {
            InspectionId = inspectionId,
            InstallationCode = installationCode,
        };

        if (steps.Length == 0)
        {
            return plantData;
        }

        var workflow = new Workflow();
        plantData.Workflow = workflow;

        foreach (var step in steps)
        {
            step.Workflow = workflow;
            workflow.WorkflowSteps.Add(step);
        }

        return plantData;
    }
}
