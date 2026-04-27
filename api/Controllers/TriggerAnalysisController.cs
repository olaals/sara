using api.Controllers.Models;
using api.Database.Models;
using api.Services;
using api.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers;

[ApiController]
[Route("[controller]")]
public class TriggerAnalysisController(
    IArgoWorkflowService argoWorkflowService,
    IPlantDataService plantDataService,
    ILogger<TriggerAnalysisController> logger
) : ControllerBase
{
    private readonly ILogger<TriggerAnalysisController> _logger = logger;

    private static bool IsWorkflowRunning(WorkflowStep? workflowStep) =>
        workflowStep?.Status == WorkflowStatus.Started;

    /// <summary>
    /// Trigger the workflow chain for an existing PlantData entry, by PlantData ID.
    /// </summary>
    [HttpPost]
    [Route("trigger-anonymizer/{plantDataId}")]
    [Authorize(Roles = Role.Any)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> TriggerAnonymizer([FromRoute] Guid plantDataId)
    {
        var plantData = await plantDataService.ReadById(plantDataId);
        if (plantData == null)
        {
            return NotFound($"Could not find plant data with id {plantDataId}");
        }

        _logger.LogInformation(
            "Triggering workflow chain from controller for InspectionId: {InspectionId}",
            plantData.InspectionId
        );

        var anonymizationStep = plantData.GetWorkflowStep(WorkflowStepType.Anonymization);
        var cloeStep = plantData.GetWorkflowStep(WorkflowStepType.CLOEAnalysis);
        var fencillaStep = plantData.GetWorkflowStep(WorkflowStepType.FencillaAnalysis);
        var thermalReadingStep = plantData.GetWorkflowStep(WorkflowStepType.ThermalReadingAnalysis);

        if (
            IsWorkflowRunning(anonymizationStep)
            || IsWorkflowRunning(cloeStep)
            || IsWorkflowRunning(fencillaStep)
            || IsWorkflowRunning(thermalReadingStep)
        )
        {
            return Conflict(
                "A workflow in the analysis chain is still running. Wait until the full chain has completed before triggering it again."
            );
        }

        if (anonymizationStep == null)
        {
            return Conflict(
                $"No anonymization workflow configured for plant data with id {plantDataId}."
            );
        }

        await argoWorkflowService.TriggerAnonymizer(plantData.InspectionId, anonymizationStep);

        return Ok("Workflow chain triggered successfully.");
    }

    /// <summary>
    /// Trigger the analysis workflow for existing PlantData entry, by PlantData ID.
    /// </summary>
    [HttpPost]
    [Route("trigger-analysis/{plantDataId}")]
    [Authorize(Roles = Role.Any)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> TriggerAnalysis([FromRoute] Guid plantDataId)
    {
        try
        {
            var plantData = await plantDataService.ReadById(plantDataId);
            if (plantData == null)
            {
                return NotFound($"Could not find plant data with id {plantDataId}");
            }

            _logger.LogInformation(
                "Triggering analysis workflows from controller for InspectionId: {InspectionId}",
                plantData.InspectionId
            );

            var anonymizationStep = plantData.GetWorkflowStep(WorkflowStepType.Anonymization);
            var cloeStep = plantData.GetWorkflowStep(WorkflowStepType.CLOEAnalysis);
            var fencillaStep = plantData.GetWorkflowStep(WorkflowStepType.FencillaAnalysis);
            var thermalReadingStep = plantData.GetWorkflowStep(
                WorkflowStepType.ThermalReadingAnalysis
            );

            if (anonymizationStep is { Status: WorkflowStatus.NotStarted })
            {
                await argoWorkflowService.TriggerAnonymizer(
                    plantData.InspectionId,
                    anonymizationStep
                );
                return Ok(
                    "Triggering anonymization workflow which will trigger analysis workflows."
                );
            }

            if (anonymizationStep is { Status: WorkflowStatus.Started })
            {
                return Conflict(
                    "Anonymization is still in progress. Analysis workflows will be triggered once it completes."
                );
            }

            if (anonymizationStep is { Status: WorkflowStatus.ExitFailure })
            {
                return Conflict("Cannot trigger analysis workflows because anonymization failed.");
            }

            var analysesToRun = new List<string>();
            if (cloeStep is { Status: WorkflowStatus.NotStarted or WorkflowStatus.ExitFailure })
            {
                await argoWorkflowService.TriggerCLOE(plantData.InspectionId, cloeStep);
                analysesToRun.Add("CLOE analysis");
            }
            if (fencillaStep is { Status: WorkflowStatus.NotStarted or WorkflowStatus.ExitFailure })
            {
                await argoWorkflowService.TriggerFencilla(plantData.InspectionId, fencillaStep);
                analysesToRun.Add("Fencilla analysis");
            }

            if (
                thermalReadingStep
                    is { Status: WorkflowStatus.NotStarted or WorkflowStatus.ExitFailure }
                && plantData.Tag != null
                && plantData.InspectionDescription != null
            )
            {
                await argoWorkflowService.TriggerThermalReading(
                    plantData.InspectionId,
                    plantData.Tag,
                    plantData.InspectionDescription,
                    plantData.InstallationCode,
                    thermalReadingStep
                );
                analysesToRun.Add("Thermal Reading analysis");
            }
            if (analysesToRun.Count == 0)
            {
                return Ok(
                    $"No analysis workflows configured for plant data with Id {plantDataId}."
                );
            }
            return Ok($"Triggered analysis workflows: {string.Join(", ", analysesToRun)}");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(
                ex,
                "Plant data not found for PlantDataId: {PlantDataId}",
                plantDataId
            );
            return NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error occurred while triggering analysis workflows for PlantDataId: {PlantDataId}",
                plantDataId
            );
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                "An unexpected error occurred."
            );
        }
    }
}
