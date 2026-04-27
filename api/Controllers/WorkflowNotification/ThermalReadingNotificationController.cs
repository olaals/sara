using api.Controllers.Models;
using api.Database.Models;
using api.MQTT;
using api.Services;
using api.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.WorkflowNotification;

[ApiController]
[Route("workflow-notification/thermal-reading")]
public class ThermalReadingWorkflowNotificationController(
    ILogger<ThermalReadingWorkflowNotificationController> logger,
    IPlantDataService plantDataService,
    IArgoWorkflowService workflowService,
    IMqttPublisherService mqttPublisherService,
    ITimeseriesService timeseriesService
) : ControllerBase
{
    /// <summary>
    /// Notify that the ThermalReading workflow has started
    /// </summary>
    [HttpPut]
    [Authorize(Roles = Role.WorkflowStatusWrite)]
    [Route("started")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PlantDataResponse>> ThermalReadingStarted(
        [FromBody] WorkflowStartedNotification notification
    )
    {
        var inspectionId = Sanitize.SanitizeUserInput(notification.InspectionId);
        logger.LogDebug(
            "Received notification that the ThermalReading workflow has started for inspection id {inspectionId}",
            inspectionId
        );

        PlantData updatedPlantData;
        try
        {
            updatedPlantData = await plantDataService.UpdateThermalReadingWorkflowStatus(
                notification.InspectionId,
                WorkflowStatus.Started
            );
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Error occurred while updating ThermalReading workflow status");
            return BadRequest(ex.Message);
        }

        return Ok(new PlantDataResponse(updatedPlantData));
    }

    /// <summary>
    /// Notify about the result of the ThermalReading workflow
    /// </summary>
    [HttpPut]
    [Authorize(Roles = Role.WorkflowStatusWrite)]
    [Route("result")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PlantDataResponse>> ThermalReadingResult(
        [FromBody] ThermalReadingWorkflowResultNotification notification
    )
    {
        logger.LogDebug(
            "Received notification with result from the ThermalReading workflow with inspection id {id}. Temperature: {temperature}",
            notification.InspectionId,
            notification.Temperature
        );

        PlantData plantData;
        try
        {
            plantData = await plantDataService.UpdateThermalReadingResult(
                notification.InspectionId,
                notification.Temperature
            );
        }
        catch (InvalidOperationException e)
        {
            logger.LogError(e, "Error occurred while updating ThermalReading result");
            return BadRequest(e.Message);
        }

        string description = plantData.InspectionDescription?.Replace(" ", "-") ?? string.Empty;
        // Note that the name does not contain the robot name
        var name = $"{plantData.InstallationCode}_" + $"{plantData.Tag}_" + $"{description}";

        var uploadRequest = new TriggerTimeseriesUploadRequest
        {
            Name = name,
            Facility = plantData.InstallationCode,
            ExternalId = "",
            Description = "ThermalReading",
            Unit = "°C",
            AssetId = plantData.InstallationCode,
            Value = notification.Temperature,
            Timestamp = plantData.Timestamp ?? DateTime.UtcNow,
            Metadata = new Dictionary<string, string>
            {
                { "tag_id", plantData.Tag ?? "" },
                { "inspection_description", plantData.InspectionDescription ?? "" },
                { "robot_name", plantData.RobotName ?? "" },
            },
        };
        await timeseriesService.TriggerTimeseriesUpload(uploadRequest);

        return Ok(new PlantDataResponse(plantData));
    }

    /// <summary>
    /// Notify that the ThermalReading workflow has exited with success or failure
    /// </summary>
    [HttpPut]
    [Authorize(Roles = Role.WorkflowStatusWrite)]
    [Route("exited")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PlantDataResponse>> ThermalReadingExited(
        [FromBody] WorkflowExitedNotification notification
    )
    {
        var workflowStatus = workflowService.GetWorkflowStatus(notification, "ThermalReading");

        PlantData updatedPlantData;
        try
        {
            updatedPlantData = await plantDataService.UpdateThermalReadingWorkflowStatus(
                notification.InspectionId,
                workflowStatus
            );
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Error occurred while updating ThermalReading workflow status");
            return BadRequest(ex.Message);
        }

        var thermalReadingStep =
            updatedPlantData.GetWorkflowStep(WorkflowStepType.ThermalReadingAnalysis)
            ?? throw new InvalidOperationException(
                $"Thermal reading analysis is not set up for plant data with inspection id {notification.InspectionId}"
            );
        var thermalReadingData =
            thermalReadingStep.ThermalReadingData
            ?? throw new InvalidOperationException(
                $"Thermal reading data is not set up for plant data with inspection id {notification.InspectionId}"
            );

        var message = new SaraAnalysisResultMessage
        {
            InspectionId = updatedPlantData.InspectionId,
            AnalysisType = nameof(AnalysisType.ThermalReading),
            Value = thermalReadingData.Temperature.ToString(),
            Unit = "temperature [°C]",
            StorageAccount = thermalReadingStep.DestinationBlobStorageLocation.StorageAccount,
            BlobContainer = thermalReadingStep.DestinationBlobStorageLocation.BlobContainer,
            BlobName = thermalReadingStep.DestinationBlobStorageLocation.BlobName,
        };

        await mqttPublisherService.PublishSaraAnalysisResultAvailable(message);

        return Ok(new PlantDataResponse(updatedPlantData));
    }
}
