using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

#pragma warning disable CS8618
namespace api.Database.Models;

public class PlantData
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    [Required]
    public required string InspectionId { get; set; }

    [Required]
    public required string InstallationCode { get; set; }

    private DateTime _dateCreated = DateTime.UtcNow;

    [Required]
    public DateTime DateCreated
    {
        get => _dateCreated;
        set => _dateCreated = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    }

    public string? Tag { get; set; }

    public string? Coordinates { get; set; }

    public string? InspectionDescription { get; set; }

    public string? RobotName { get; set; }

    private DateTime? _timestamp;
    public DateTime? Timestamp
    {
        get => _timestamp;
        set => _timestamp = value?.Kind == DateTimeKind.Utc ? value : value?.ToUniversalTime();
    }

    public Guid? WorkflowId { get; set; }

    [JsonIgnore]
    public Workflow? Workflow { get; set; }

    public WorkflowStep? GetWorkflowStep(WorkflowStepType stepType) => Workflow?.GetStep(stepType);

    public WorkflowStep GetRequiredWorkflowStep(WorkflowStepType stepType, string inspectionId)
    {
        return GetWorkflowStep(stepType)
            ?? throw new InvalidOperationException(
                $"{stepType} is not set up for plant data with inspection id {inspectionId}"
            );
    }

    public Workflow EnsureWorkflow()
    {
        Workflow ??= new Workflow();
        return Workflow;
    }

    public WorkflowStep EnsureWorkflowStep(WorkflowStepType stepType)
    {
        return EnsureWorkflow().EnsureStep(stepType);
    }

    public void RemoveWorkflowStep(WorkflowStepType stepType)
    {
        Workflow?.RemoveStep(stepType);
        if (Workflow is { WorkflowSteps.Count: 0 })
        {
            Workflow = null;
        }
    }
}
