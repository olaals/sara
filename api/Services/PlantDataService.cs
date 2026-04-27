using api.Database.Context;
using api.Database.Models;
using api.Utilities;
using Microsoft.EntityFrameworkCore;

namespace api.Services;

public interface IPlantDataService
{
    public Task<PagedList<PlantData>> GetPlantData(PlantDataParameters parameters);

    public Task<PlantData?> ReadById(Guid id);

    public Task<List<PlantData>> ReadByTagIdAndInspectionDescription(
        string tagId,
        string inspectionDescription
    );

    public Task<bool> ExistsByInspectionId(string inspectionId);

    public Task<PlantData?> ReadByInspectionId(string inspectionId);

    public Task<PlantData> CreatePlantData(
        string inspectionId,
        string installationCode,
        string tagID,
        string inspectionDescription,
        string rawStorageAccount,
        string rawBlobContainer,
        string rawBlobName,
        string? robotName = null
    );

    public Task WritePlantData(PlantData plantData);

    public Task<PlantData> UpdateAnonymizerWorkflowStatus(
        string inspectionId,
        WorkflowStatus status
    );

    public Task<PlantData> UpdateCLOEWorkflowStatus(string inspectionId, WorkflowStatus status);

    public Task<PlantData> UpdateFencillaWorkflowStatus(
        string inspectionId,
        WorkflowStatus started
    );

    public Task<PlantData> UpdateThermalReadingWorkflowStatus(
        string inspectionId,
        WorkflowStatus started
    );

    public Task<PlantData> UpdateAnonymizerResult(string inspectionId, bool isPersonInImage);

    public Task<PlantData> UpdateCLOEResult(string inspectionId, float oilLevel, float confidence);

    public Task<PlantData> UpdateFencillaResult(
        string inspectionId,
        bool isBreak,
        float confidence
    );

    public Task<PlantData> UpdateThermalReadingResult(string inspectionId, float temperature);

    public Task UpdatePlantDataFromAnalysisMapping(
        string tagId,
        string inspectionDescription,
        AnalysisType analysisType
    );
}

public class PlantDataService(
    SaraDbContext context,
    IAnalysisMappingService analysisMappingService,
    IBlobService blobService,
    ILogger<PlantDataService> logger
) : IPlantDataService
{
    private IQueryable<PlantData> QueryPlantDataWithWorkflow()
    {
        return context
            .PlantData.Include(plantData => plantData.Workflow)
                .ThenInclude(workflow => workflow!.WorkflowSteps)
                    .ThenInclude(step => step.AnonymizationData)
            .Include(plantData => plantData.Workflow)
                .ThenInclude(workflow => workflow!.WorkflowSteps)
                    .ThenInclude(step => step.CLOEData)
            .Include(plantData => plantData.Workflow)
                .ThenInclude(workflow => workflow!.WorkflowSteps)
                    .ThenInclude(step => step.FencillaData)
            .Include(plantData => plantData.Workflow)
                .ThenInclude(workflow => workflow!.WorkflowSteps)
                    .ThenInclude(step => step.ThermalReadingData);
    }

    private static WorkflowStep GetRequiredStep(
        PlantData plantData,
        WorkflowStepType stepType,
        string inspectionId,
        string message
    )
    {
        return plantData.GetWorkflowStep(stepType)
            ?? throw new InvalidOperationException(message.Replace("{inspectionId}", inspectionId));
    }

    public async Task<PagedList<PlantData>> GetPlantData(PlantDataParameters parameters)
    {
        var query = QueryPlantDataWithWorkflow().AsQueryable();

        if (!string.IsNullOrWhiteSpace(parameters.InspectionId))
            query = query.Where(p =>
                p.InspectionId.ToLower().Contains(parameters.InspectionId.ToLower())
            );

        if (!string.IsNullOrWhiteSpace(parameters.Tag))
            query = query.Where(p =>
                p.Tag != null && p.Tag.ToLower().Contains(parameters.Tag.ToLower())
            );

        if (!string.IsNullOrWhiteSpace(parameters.InstallationCode))
            query = query.Where(p =>
                p.InstallationCode.ToLower().Contains(parameters.InstallationCode.ToLower())
            );

        if (
            !string.IsNullOrWhiteSpace(parameters.AnonymizationStatus)
            && Enum.TryParse<WorkflowStatus>(
                parameters.AnonymizationStatus,
                true,
                out var parsedStatus
            )
        )
            query = query.Where(p =>
                p.Workflow != null
                && p.Workflow.WorkflowSteps.Any(step =>
                    step.Type == WorkflowStepType.Anonymization && step.Status == parsedStatus
                )
            );

        if (!string.IsNullOrWhiteSpace(parameters.AnalysisType))
        {
            if (
                parameters.AnalysisType.Equals(
                    "ConstantLevelOiler",
                    StringComparison.OrdinalIgnoreCase
                )
            )
                query = query.Where(p =>
                    p.Workflow != null
                    && p.Workflow.WorkflowSteps.Any(step =>
                        step.Type == WorkflowStepType.CLOEAnalysis
                    )
                );
            else if (parameters.AnalysisType.Equals("Fencilla", StringComparison.OrdinalIgnoreCase))
                query = query.Where(p =>
                    p.Workflow != null
                    && p.Workflow.WorkflowSteps.Any(step =>
                        step.Type == WorkflowStepType.FencillaAnalysis
                    )
                );
            else if (
                parameters.AnalysisType.Equals("ThermalReading", StringComparison.OrdinalIgnoreCase)
            )
                query = query.Where(p =>
                    p.Workflow != null
                    && p.Workflow.WorkflowSteps.Any(step =>
                        step.Type == WorkflowStepType.ThermalReadingAnalysis
                    )
                );
        }

        if (parameters.HasIncompleteWorkflows == true)
            query = query.Where(p =>
                p.Workflow != null
                && (
                    !p.Workflow.WorkflowSteps.Any(step =>
                        step.Type == WorkflowStepType.Anonymization
                        && step.Status == WorkflowStatus.ExitSuccess
                    )
                    || p.Workflow.WorkflowSteps.Any(step =>
                        step.Type == WorkflowStepType.CLOEAnalysis
                        && step.Status != WorkflowStatus.ExitSuccess
                    )
                    || p.Workflow.WorkflowSteps.Any(step =>
                        step.Type == WorkflowStepType.FencillaAnalysis
                        && step.Status != WorkflowStatus.ExitSuccess
                    )
                    || p.Workflow.WorkflowSteps.Any(step =>
                        step.Type == WorkflowStepType.ThermalReadingAnalysis
                        && step.Status != WorkflowStatus.ExitSuccess
                    )
                )
            );

        query = query.OrderByDescending(p => p.DateCreated).ThenByDescending(p => p.Id);

        return await PagedList<PlantData>.ToPagedListAsync(
            query,
            parameters.PageNumber,
            parameters.PageSize
        );
    }

    public async Task<PlantData?> ReadById(Guid id)
    {
        return await QueryPlantDataWithWorkflow().FirstOrDefaultAsync(i => i.Id == id);
    }

    public async Task<bool> ExistsByInspectionId(string inspectionId)
    {
        return await context.PlantData.AnyAsync(i => i.InspectionId.Equals(inspectionId));
    }

    public async Task<List<PlantData>> ReadByTagIdAndInspectionDescription(
        string tagId,
        string inspectionDescription
    )
    {
        return await QueryPlantDataWithWorkflow()
            .Where(i =>
                i.Tag != null
                && i.Tag.ToLower().Equals(tagId.ToLower())
                && i.InspectionDescription != null
                && i.InspectionDescription.ToLower().Equals(inspectionDescription.ToLower())
            )
            .ToListAsync();
    }

    public async Task<PlantData?> ReadByInspectionId(string inspectionId)
    {
        return await QueryPlantDataWithWorkflow()
            .FirstOrDefaultAsync(i => i.InspectionId.Equals(inspectionId));
    }

    public async Task<PlantData> CreatePlantData(
        string inspectionId,
        string installationCode,
        string tagID,
        string inspectionDescription,
        string rawStorageAccount,
        string rawBlobContainer,
        string rawBlobName,
        string? robotName = null
    )
    {
        List<AnalysisType> analysisToBeRun;
        try
        {
            analysisToBeRun = await analysisMappingService.GetAnalysesToBeRun(
                tagID,
                inspectionDescription
            );
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error occurred while fetching analysis mapping for TagID: {TagID} and InspectionDescription: {InspectionDescription}",
                tagID,
                inspectionDescription
            );
            throw new InvalidOperationException("Error occurred while fetching analysis mapping");
        }

        var workflow = new Workflow();
        var anonymizationStep = workflow.EnsureStep(WorkflowStepType.Anonymization);
        anonymizationStep.SourceBlobStorageLocation = blobService.CreateRawBlobStorageLocation(
            rawStorageAccount,
            rawBlobContainer,
            rawBlobName
        );
        anonymizationStep.DestinationBlobStorageLocation =
            blobService.CreateAnonymizedBlobStorageLocation(rawBlobContainer, rawBlobName);
        anonymizationStep.AnonymizationData ??= new AnonymizationData();

        if (analysisToBeRun.Contains(AnalysisType.ConstantLevelOiler))
        {
            logger.LogInformation(
                "Analysis type ConstantLevelOilerEstimator is set to be run for InspectionId: {InspectionId}",
                inspectionId
            );
            var cloeStep = workflow.EnsureStep(WorkflowStepType.CLOEAnalysis);
            cloeStep.SourceBlobStorageLocation = anonymizationStep.DestinationBlobStorageLocation;
            cloeStep.DestinationBlobStorageLocation =
                blobService.CreateVisualizedBlobStorageLocation(
                    rawBlobContainer,
                    rawBlobName,
                    "cloe"
                );
            cloeStep.CLOEData ??= new CLOEData();
        }

        if (analysisToBeRun.Contains(AnalysisType.Fencilla))
        {
            logger.LogInformation(
                "Analysis type Fencilla is set to be run for InspectionId: {InspectionId}",
                inspectionId
            );
            var fencillaStep = workflow.EnsureStep(WorkflowStepType.FencillaAnalysis);
            fencillaStep.SourceBlobStorageLocation =
                anonymizationStep.DestinationBlobStorageLocation;
            fencillaStep.DestinationBlobStorageLocation =
                blobService.CreateVisualizedBlobStorageLocation(
                    rawBlobContainer,
                    rawBlobName,
                    "fencilla"
                );
            fencillaStep.FencillaData ??= new FencillaData();
        }

        if (analysisToBeRun.Contains(AnalysisType.ThermalReading))
        {
            logger.LogInformation(
                "Analysis type ThermalReading is set to be run for InspectionId: {InspectionId}",
                inspectionId
            );

            var preProcessedBlobName = BlobService.ReplaceFileEnding(rawBlobName, ".fff");
            var preProcessedBlobStorageLocation = blobService.CreatePreProcessedBlobStorageLocation(
                rawBlobContainer,
                preProcessedBlobName
            );
            var visualizedBlobName = BlobService.ReplaceFileEnding(rawBlobName, ".jpg");
            var visualizedBlobStorageLocation = blobService.CreateVisualizedBlobStorageLocation(
                rawBlobContainer,
                visualizedBlobName,
                "thermalReading"
            );

            anonymizationStep.AnonymizationData.PreProcessedBlobStorageLocation =
                preProcessedBlobStorageLocation;

            var thermalReadingStep = workflow.EnsureStep(WorkflowStepType.ThermalReadingAnalysis);
            thermalReadingStep.SourceBlobStorageLocation = preProcessedBlobStorageLocation;
            thermalReadingStep.DestinationBlobStorageLocation = visualizedBlobStorageLocation;
            thermalReadingStep.ThermalReadingData ??= new ThermalReadingData();
        }

        var plantData = new PlantData
        {
            InspectionId = inspectionId,
            InstallationCode = installationCode,
            Tag = tagID,
            InspectionDescription = inspectionDescription,
            RobotName = robotName,
            Workflow = workflow,
        };
        await WritePlantData(plantData);
        return plantData;
    }

    public async Task WritePlantData(PlantData plantData)
    {
        await context.PlantData.AddAsync(plantData);
        await context.SaveChangesAsync();
    }

    public async Task<PlantData> UpdateAnonymizerWorkflowStatus(
        string inspectionId,
        WorkflowStatus status
    )
    {
        var plantData =
            await ReadByInspectionId(inspectionId)
            ?? throw new InvalidOperationException(
                $"Could not find plant data with inspection id {inspectionId}"
            );
        var step = GetRequiredStep(
            plantData,
            WorkflowStepType.Anonymization,
            inspectionId,
            "Anonymization is not set up for plant data with inspection id {inspectionId}"
        );
        step.Status = status;
        await context.SaveChangesAsync();
        return plantData;
    }

    public async Task<PlantData> UpdateCLOEWorkflowStatus(
        string inspectionId,
        WorkflowStatus status
    )
    {
        var plantData =
            await ReadByInspectionId(inspectionId)
            ?? throw new InvalidOperationException(
                $"Could not find plant data with inspection id {inspectionId}"
            );
        var step = GetRequiredStep(
            plantData,
            WorkflowStepType.CLOEAnalysis,
            inspectionId,
            "CLOE analysis is not set up for plant data with inspection id {inspectionId}"
        );
        step.Status = status;
        await context.SaveChangesAsync();
        return plantData;
    }

    public async Task<PlantData> UpdateFencillaWorkflowStatus(
        string inspectionId,
        WorkflowStatus status
    )
    {
        var plantData =
            await ReadByInspectionId(inspectionId)
            ?? throw new InvalidOperationException(
                $"Could not find plant data with inspection id {inspectionId}"
            );
        var step = GetRequiredStep(
            plantData,
            WorkflowStepType.FencillaAnalysis,
            inspectionId,
            "Fencilla analysis is not set up for plant data with inspection id {inspectionId}"
        );
        step.Status = status;
        await context.SaveChangesAsync();
        return plantData;
    }

    public async Task<PlantData> UpdateThermalReadingWorkflowStatus(
        string inspectionId,
        WorkflowStatus status
    )
    {
        var plantData =
            await ReadByInspectionId(inspectionId)
            ?? throw new InvalidOperationException(
                $"Could not find plant data with inspection id {inspectionId}"
            );
        var step = GetRequiredStep(
            plantData,
            WorkflowStepType.ThermalReadingAnalysis,
            inspectionId,
            "Thermal Reading analysis is not set up for plant data with inspection id {inspectionId}"
        );
        step.Status = status;
        await context.SaveChangesAsync();
        return plantData;
    }

    public async Task<PlantData> UpdateAnonymizerResult(string inspectionId, bool isPersonInImage)
    {
        var plantData =
            await ReadByInspectionId(inspectionId)
            ?? throw new InvalidOperationException(
                $"Could not find plant data with inspection id {inspectionId}"
            );
        var step = GetRequiredStep(
            plantData,
            WorkflowStepType.Anonymization,
            inspectionId,
            "Anonymization is not set up for plant data with inspection id {inspectionId}"
        );
        step.AnonymizationData ??= new AnonymizationData();
        step.AnonymizationData.IsPersonInImage = isPersonInImage;
        await context.SaveChangesAsync();
        return plantData;
    }

    public async Task<PlantData> UpdateCLOEResult(
        string inspectionId,
        float oilLevel,
        float confidence
    )
    {
        var plantData =
            await ReadByInspectionId(inspectionId)
            ?? throw new InvalidOperationException(
                $"Could not find plant data with inspection id {inspectionId}"
            );
        var step = GetRequiredStep(
            plantData,
            WorkflowStepType.CLOEAnalysis,
            inspectionId,
            "CLOE analysis is not set up for plant data with inspection id {inspectionId}"
        );
        if (oilLevel < 0 || oilLevel > 100)
        {
            throw new InvalidOperationException(
                $"Invalid oil level {oilLevel} received for inspection id {inspectionId}. Must be between 0 and 100."
            );
        }
        step.CLOEData ??= new CLOEData();
        step.CLOEData.OilLevel = oilLevel;
        step.CLOEData.Confidence = confidence;
        await context.SaveChangesAsync();
        return plantData;
    }

    public async Task<PlantData> UpdateFencillaResult(
        string inspectionId,
        bool isBreak,
        float confidence
    )
    {
        var plantData =
            await ReadByInspectionId(inspectionId)
            ?? throw new InvalidOperationException(
                $"Could not find plant data with inspection id {inspectionId}"
            );
        var step = GetRequiredStep(
            plantData,
            WorkflowStepType.FencillaAnalysis,
            inspectionId,
            "Fencilla analysis is not set up for plant data with inspection id {inspectionId}"
        );
        if (isBreak.GetType() != typeof(bool))
        {
            throw new InvalidOperationException(
                $"Invalid IsBreak value {isBreak} received for inspection id {inspectionId}. Must be a boolean."
            );
        }
        if (confidence < 0 || confidence > 1)
        {
            throw new InvalidOperationException(
                $"Invalid Confidence value {confidence} received for inspection id {inspectionId}. Must be between 0 and 1."
            );
        }
        step.FencillaData ??= new FencillaData();
        step.FencillaData.IsBreak = isBreak;
        step.FencillaData.Confidence = confidence;
        await context.SaveChangesAsync();
        return plantData;
    }

    public async Task<PlantData> UpdateThermalReadingResult(string inspectionId, float temperature)
    {
        var plantData =
            await ReadByInspectionId(inspectionId)
            ?? throw new InvalidOperationException(
                $"Could not find plant data with inspection id {inspectionId}"
            );
        var step = GetRequiredStep(
            plantData,
            WorkflowStepType.ThermalReadingAnalysis,
            inspectionId,
            "Thermal Reading analysis is not set up for plant data with inspection id {inspectionId}"
        );
        if (float.IsNaN(temperature) || temperature < -50 || temperature > 300)
        {
            throw new InvalidOperationException(
                $"Invalid Temperature value {temperature} received for inspection id {inspectionId}. Must be between -50 and 300 Celsius."
            );
        }
        step.ThermalReadingData ??= new ThermalReadingData();
        step.ThermalReadingData.Temperature = temperature;
        await context.SaveChangesAsync();
        return plantData;
    }

    public async Task UpdatePlantDataFromAnalysisMapping(
        string tagId,
        string inspectionDescription,
        AnalysisType analysisType
    )
    {
        var plantDataEntries = await ReadByTagIdAndInspectionDescription(
            tagId,
            inspectionDescription
        );

        if (plantDataEntries.Count == 0)
        {
            return;
        }

        foreach (var plantData in plantDataEntries)
        {
            var anonymizationStep = plantData.GetWorkflowStep(WorkflowStepType.Anonymization);
            if (anonymizationStep == null)
            {
                continue;
            }

            var blobContainer = anonymizationStep.DestinationBlobStorageLocation.BlobContainer;
            var blobName = anonymizationStep.DestinationBlobStorageLocation.BlobName;

            if (
                analysisType == AnalysisType.ConstantLevelOiler
                && plantData.GetWorkflowStep(WorkflowStepType.CLOEAnalysis) is null
            )
            {
                var cloeStep = plantData.EnsureWorkflowStep(WorkflowStepType.CLOEAnalysis);
                cloeStep.SourceBlobStorageLocation =
                    blobService.CreateAnonymizedBlobStorageLocation(blobContainer, blobName);
                cloeStep.DestinationBlobStorageLocation =
                    blobService.CreateVisualizedBlobStorageLocation(
                        blobContainer,
                        blobName,
                        "cloe"
                    );
                cloeStep.CLOEData ??= new CLOEData();
            }

            if (
                analysisType == AnalysisType.Fencilla
                && plantData.GetWorkflowStep(WorkflowStepType.FencillaAnalysis) is null
            )
            {
                var fencillaStep = plantData.EnsureWorkflowStep(WorkflowStepType.FencillaAnalysis);
                fencillaStep.SourceBlobStorageLocation =
                    blobService.CreateAnonymizedBlobStorageLocation(blobContainer, blobName);
                fencillaStep.DestinationBlobStorageLocation =
                    blobService.CreateVisualizedBlobStorageLocation(
                        blobContainer,
                        blobName,
                        "fencilla"
                    );
                fencillaStep.FencillaData ??= new FencillaData();
            }

            if (
                analysisType == AnalysisType.ThermalReading
                && plantData.GetWorkflowStep(WorkflowStepType.ThermalReadingAnalysis) is null
            )
            {
                var thermalReadingStep = plantData.EnsureWorkflowStep(
                    WorkflowStepType.ThermalReadingAnalysis
                );
                thermalReadingStep.SourceBlobStorageLocation =
                    blobService.CreateAnonymizedBlobStorageLocation(blobContainer, blobName);
                thermalReadingStep.DestinationBlobStorageLocation =
                    blobService.CreateVisualizedBlobStorageLocation(
                        blobContainer,
                        blobName,
                        "thermalReading"
                    );
                thermalReadingStep.ThermalReadingData ??= new ThermalReadingData();
            }

            await context.SaveChangesAsync();
        }
    }
}
