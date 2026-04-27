using api.Database.Models;

namespace api.Controllers.Models
{
    public class WorkflowResponse
    {
        public Guid Id { get; set; }

        public List<WorkflowStepResponse> Steps { get; set; } = [];

        public WorkflowResponse() { }

        public WorkflowResponse(Workflow workflow)
        {
            Id = workflow.Id;
            Steps = workflow.WorkflowSteps.Select(step => new WorkflowStepResponse(step)).ToList();
        }
    }

    public class WorkflowStepResponse
    {
        public Guid Id { get; set; }

        public WorkflowStepType Type { get; set; }

        public BlobStorageLocation SourceBlobStorageLocation { get; set; } = null!;

        public BlobStorageLocation DestinationBlobStorageLocation { get; set; } = null!;

        public DateTime DateCreated { get; set; }

        public WorkflowStatus Status { get; set; }

        public AnonymizationStepDataResponse? AnonymizationData { get; set; }

        public CLOEStepDataResponse? CLOEData { get; set; }

        public FencillaStepDataResponse? FencillaData { get; set; }

        public ThermalReadingStepDataResponse? ThermalReadingData { get; set; }

        public WorkflowStepResponse() { }

        public WorkflowStepResponse(WorkflowStep step)
        {
            Id = step.Id;
            Type = step.Type;
            SourceBlobStorageLocation = step.SourceBlobStorageLocation;
            DestinationBlobStorageLocation = step.DestinationBlobStorageLocation;
            DateCreated = step.DateCreated;
            Status = step.Status;
            AnonymizationData =
                step.AnonymizationData != null
                    ? new AnonymizationStepDataResponse(step.AnonymizationData)
                    : null;
            CLOEData = step.CLOEData != null ? new CLOEStepDataResponse(step.CLOEData) : null;
            FencillaData =
                step.FencillaData != null ? new FencillaStepDataResponse(step.FencillaData) : null;
            ThermalReadingData =
                step.ThermalReadingData != null
                    ? new ThermalReadingStepDataResponse(step.ThermalReadingData)
                    : null;
        }
    }

    public class AnonymizationStepDataResponse
    {
        public bool? IsPersonInImage { get; set; }

        public BlobStorageLocation? PreProcessedBlobStorageLocation { get; set; }

        public AnonymizationStepDataResponse() { }

        public AnonymizationStepDataResponse(AnonymizationData data)
        {
            IsPersonInImage = data.IsPersonInImage;
            PreProcessedBlobStorageLocation = data.PreProcessedBlobStorageLocation;
        }
    }

    public class CLOEStepDataResponse
    {
        public float? OilLevel { get; set; }

        public float? Confidence { get; set; }

        public CLOEStepDataResponse() { }

        public CLOEStepDataResponse(CLOEData data)
        {
            OilLevel = data.OilLevel;
            Confidence = data.Confidence;
        }
    }

    public class FencillaStepDataResponse
    {
        public bool? IsBreak { get; set; }

        public float? Confidence { get; set; }

        public FencillaStepDataResponse() { }

        public FencillaStepDataResponse(FencillaData data)
        {
            IsBreak = data.IsBreak;
            Confidence = data.Confidence;
        }
    }

    public class ThermalReadingStepDataResponse
    {
        public float? Temperature { get; set; }

        public ThermalReadingStepDataResponse() { }

        public ThermalReadingStepDataResponse(ThermalReadingData data)
        {
            Temperature = data.Temperature;
        }
    }

    public class PlantDataResponse
    {
        public Guid Id { get; set; }

        public string InspectionId { get; set; } = string.Empty;

        public string InstallationCode { get; set; } = string.Empty;

        public DateTime DateCreated { get; set; }

        public string? Tag { get; set; }

        public string? Coordinates { get; set; }

        public string? InspectionDescription { get; set; }

        public string? RobotName { get; set; }

        public DateTime? Timestamp { get; set; }

        public WorkflowResponse? Workflow { get; set; }

        public PlantDataResponse() { }

        public PlantDataResponse(PlantData plantData)
        {
            Id = plantData.Id;
            InspectionId = plantData.InspectionId;
            InstallationCode = plantData.InstallationCode;
            DateCreated = plantData.DateCreated;
            Tag = plantData.Tag;
            Coordinates = plantData.Coordinates;
            InspectionDescription = plantData.InspectionDescription;
            RobotName = plantData.RobotName;
            Timestamp = plantData.Timestamp;
            Workflow = plantData.Workflow != null ? new WorkflowResponse(plantData.Workflow) : null;
        }
    }
}
