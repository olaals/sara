using api.Controllers.Models;
using api.Database.Models;
using api.MQTT;
using api.Services;
using api.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.WorkflowNotification;

[ApiController]
[Route("workflow-notification/anonymizer")]
public class AnonymizerWorkflowNotificationController(
    ILogger<AnonymizerWorkflowNotificationController> logger,
    IPlantDataService plantDataService,
    IArgoWorkflowService workflowService,
    IMqttPublisherService mqttPublisherService
) : ControllerBase
{
    /// <summary>
    /// Notify that the anonymizer workflow has started
    /// </summary>
    [HttpPut]
    [Authorize(Roles = Role.WorkflowStatusWrite)]
    [Route("started")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PlantDataResponse>> AnonymizerStarted(
        [FromBody] WorkflowStartedNotification notification
    )
    {
        var inspectionId = Sanitize.SanitizeUserInput(notification.InspectionId);
        logger.LogDebug(
            "Received notification that the anonymizer workflow has started for inspection id {inspectionId}",
            inspectionId
        );

        PlantData updatedPlantData;
        try
        {
            updatedPlantData = await plantDataService.UpdateAnonymizerWorkflowStatus(
                notification.InspectionId,
                WorkflowStatus.Started
            );
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Error occured while updating Anonymizer workflow status");
            return BadRequest(ex.Message);
        }

        return Ok(new PlantDataResponse(updatedPlantData));
    }

    /// <summary>
    /// Notify about the result of the anonymizer workflow
    /// </summary>
    [HttpPut]
    [Authorize(Roles = Role.WorkflowStatusWrite)]
    [Route("result")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PlantDataResponse>> AnonymizerResult(
        [FromBody] AnonymizerWorkflowResultNotification notification
    )
    {
        logger.LogDebug(
            "Received notification with result from the anonymizer workflow with inspection id {id}. IsPersonInImage: {isPersonInImage}",
            notification.InspectionId,
            notification.IsPersonInImage
        );

        PlantData updatedPlantData;
        try
        {
            updatedPlantData = await plantDataService.UpdateAnonymizerResult(
                notification.InspectionId,
                notification.IsPersonInImage
            );
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Error occurred while updating Anonymizer result");
            return BadRequest(ex.Message);
        }

        return Ok(new PlantDataResponse(updatedPlantData));
    }

    /// <summary>
    /// Notify that the anonymizer workflow has exited with success or failure
    /// </summary>
    [HttpPut]
    [Authorize(Roles = Role.WorkflowStatusWrite)]
    [Route("exited")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PlantDataResponse>> AnonymizerExited(
        [FromBody] WorkflowExitedNotification notification
    )
    {
        var workflowStatus = workflowService.GetWorkflowStatus(notification, "Anonymizer");

        PlantData updatedPlantData;
        try
        {
            updatedPlantData = await plantDataService.UpdateAnonymizerWorkflowStatus(
                notification.InspectionId,
                workflowStatus
            );
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Error occurred while updating anonymizer workflow status");
            return BadRequest(ex.Message);
        }

        if (workflowStatus == WorkflowStatus.ExitFailure)
        {
            logger.LogWarning(
                "Anonymizer workflow failure. Handler is not proceeding to trigger subsequent workflows"
            );
            return Ok(new PlantDataResponse(updatedPlantData));
        }

        var anonymizationStep = updatedPlantData.GetWorkflowStep(WorkflowStepType.Anonymization);
        if (anonymizationStep == null)
        {
            logger.LogError(
                "Anonymization step is missing after anonymizer exit for InspectionId: {InspectionId}",
                updatedPlantData.InspectionId
            );
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                "Anonymization step is missing"
            );
        }

        var message = new SaraVisualizationAvailableMessage
        {
            InspectionId = notification.InspectionId,
            StorageAccount = anonymizationStep.SourceBlobStorageLocation.StorageAccount,
            BlobContainer = anonymizationStep.SourceBlobStorageLocation.BlobContainer,
            BlobName = anonymizationStep.SourceBlobStorageLocation.BlobName,
        };
        await mqttPublisherService.PublishSaraVisualizationAvailable(message);

        var cloeStep = updatedPlantData.GetWorkflowStep(WorkflowStepType.CLOEAnalysis);
        if (cloeStep != null)
        {
            await workflowService.TriggerCLOE(updatedPlantData.InspectionId, cloeStep);
        }

        var fencillaStep = updatedPlantData.GetWorkflowStep(WorkflowStepType.FencillaAnalysis);
        if (fencillaStep != null)
        {
            await workflowService.TriggerFencilla(updatedPlantData.InspectionId, fencillaStep);
        }

        var thermalReadingStep = updatedPlantData.GetWorkflowStep(
            WorkflowStepType.ThermalReadingAnalysis
        );
        if (thermalReadingStep != null)
        {
            if (updatedPlantData.Tag is null || updatedPlantData.InspectionDescription is null)
            {
                logger.LogError(
                    "Cannot trigger Thermal Reading workflow because Tag or InspectionDescription is null for InspectionId: {InspectionId}",
                    updatedPlantData.InspectionId
                );
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    "Tag or InspectionDescription is null"
                );
            }
            await workflowService.TriggerThermalReading(
                updatedPlantData.InspectionId,
                updatedPlantData.Tag,
                updatedPlantData.InspectionDescription,
                updatedPlantData.InstallationCode,
                thermalReadingStep
            );
        }

        return Ok(new PlantDataResponse(updatedPlantData));
    }
}
