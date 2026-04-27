using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

#pragma warning disable CS8618
namespace api.Database.Models;

public enum WorkflowStatus
{
    NotStarted,
    Started,
    ExitSuccess,
    ExitFailure,
}

[Owned]
public class BlobStorageLocation
{
    [Required]
    public required string StorageAccount { get; set; }

    [Required]
    public required string BlobContainer { get; set; }

    [Required]
    public required string BlobName { get; set; }

    public override string ToString() => $"{StorageAccount}/{BlobContainer}/{BlobName}";
}

public enum WorkflowStepType
{
    Anonymization,
    CLOEAnalysis,
    FencillaAnalysis,
    ThermalReadingAnalysis,
}

public class Workflow
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    public List<WorkflowStep> WorkflowSteps { get; set; } = [];

    public WorkflowStep? GetStep(WorkflowStepType stepType) =>
        WorkflowSteps.FirstOrDefault(step => step.Type == stepType);

    public WorkflowStep EnsureStep(WorkflowStepType stepType)
    {
        var existingStep = GetStep(stepType);
        if (existingStep != null)
        {
            return existingStep;
        }

        var createdStep = new WorkflowStep { Type = stepType };
        WorkflowSteps.Add(createdStep);
        return createdStep;
    }

    public void RemoveStep(WorkflowStepType stepType)
    {
        var step = GetStep(stepType);
        if (step != null)
        {
            WorkflowSteps.Remove(step);
        }
    }
}

public class WorkflowStep
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    [Required]
    public Guid WorkflowId { get; set; }

    [Required]
    public Workflow Workflow { get; set; }

    [Required]
    public WorkflowStepType Type { get; set; }

    [Required]
    public BlobStorageLocation SourceBlobStorageLocation { get; set; } = null!;

    [Required]
    public BlobStorageLocation DestinationBlobStorageLocation { get; set; } = null!;

    [Required]
    public DateTime DateCreated { get; set; } = DateTime.UtcNow;

    public WorkflowStatus Status { get; set; } = WorkflowStatus.NotStarted;

    public AnonymizationData? AnonymizationData { get; set; }

    public CLOEData? CLOEData { get; set; }

    public FencillaData? FencillaData { get; set; }

    public ThermalReadingData? ThermalReadingData { get; set; }
}

public class AnonymizationData
{
    [Key]
    [ForeignKey(nameof(WorkflowStep))]
    public Guid WorkflowStepId { get; set; }

    [Required]
    public WorkflowStep WorkflowStep { get; set; } = null!;

    public bool? IsPersonInImage { get; set; }

    public BlobStorageLocation? PreProcessedBlobStorageLocation { get; set; }
}

public class CLOEData
{
    [Key]
    [ForeignKey(nameof(WorkflowStep))]
    public Guid WorkflowStepId { get; set; }

    [Required]
    public WorkflowStep WorkflowStep { get; set; } = null!;

    public float? OilLevel { get; set; }

    public float? Confidence { get; set; }
}

public class FencillaData
{
    [Key]
    [ForeignKey(nameof(WorkflowStep))]
    public Guid WorkflowStepId { get; set; }

    [Required]
    public WorkflowStep WorkflowStep { get; set; } = null!;

    public bool? IsBreak { get; set; }

    public float? Confidence { get; set; }
}

public class ThermalReadingData
{
    [Key]
    [ForeignKey(nameof(WorkflowStep))]
    public Guid WorkflowStepId { get; set; }

    [Required]
    public WorkflowStep WorkflowStep { get; set; } = null!;

    public float? Temperature { get; set; }
}
