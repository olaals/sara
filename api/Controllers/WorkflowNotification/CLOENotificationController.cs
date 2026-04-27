using api.Controllers.Models;
using api.Database.Models;
using api.MQTT;
using api.Services;
using api.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.WorkflowNotification;

[ApiController]
[Route("workflow-notification/constant-level-oiler-estimator")]
public class CLOEWorkflowNotificationController(
    ILogger<CLOEWorkflowNotificationController> logger,
    IPlantDataService plantDataService,
    IArgoWorkflowService workflowService,
    ITimeseriesService timeseriesService,
    IMqttPublisherService mqttPublisherService,
    IConfiguration configuration
) : ControllerBase
{
    /// <summary>
    /// Notify that the CLOE workflow has started
    /// </summary>
    [HttpPut]
    [Authorize(Roles = Role.WorkflowStatusWrite)]
    [Route("started")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PlantDataResponse>> CLOEStarted(
        [FromBody] WorkflowStartedNotification notification
    )
    {
        var inspectionId = Sanitize.SanitizeUserInput(notification.InspectionId);
        logger.LogDebug(
            "Received notification that the CLOE workflow has started for inspection id {inspectionId}",
            inspectionId
        );

        PlantData updatedPlantData;
        try
        {
            updatedPlantData = await plantDataService.UpdateCLOEWorkflowStatus(
                inspectionId,
                WorkflowStatus.Started
            );
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Error occurred while updating CLOE workflow status");
            return BadRequest(ex.Message);
        }

        return Ok(new PlantDataResponse(updatedPlantData));
    }

    /// <summary>
    /// Notify about the result of the CLOE workflow
    /// </summary>
    [HttpPut]
    [Authorize(Roles = Role.WorkflowStatusWrite)]
    [Route("result")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PlantDataResponse>> CLOEResult(
        [FromBody] CLOEWorkflowResultNotification notification
    )
    {
        logger.LogDebug(
            "Received notification with result from the CLOE workflow with inspection id {id}. OilLevel: {oilLevel}. Confidence: {confidence}",
            notification.InspectionId,
            notification.OilLevel,
            notification.Confidence
        );

        PlantData updatedPlantData;
        try
        {
            updatedPlantData = await plantDataService.UpdateCLOEResult(
                notification.InspectionId,
                notification.OilLevel,
                notification.Confidence
            );
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Error occurred while updating CLOE result");
            return BadRequest(ex.Message);
        }

        float cloeConfidenceThreshold = configuration
            .GetSection("Thresholds")
            .GetValue<float>("CLOETimeseriesUploadConfidenceThreshold");

        if (notification.Confidence < cloeConfidenceThreshold)
        {
            logger.LogWarning(
                "Not sending CLOE result to timeseries upload due to low confidence of {confidence} for inspection id {id}",
                notification.Confidence,
                notification.InspectionId
            );
            return Ok(new PlantDataResponse(updatedPlantData));
        }

        var uploadRequest = new TriggerTimeseriesUploadRequest
        {
            Name =
                $"{updatedPlantData.InstallationCode}_{updatedPlantData.Tag}_{updatedPlantData.InspectionDescription}",
            Facility = updatedPlantData.InstallationCode,
            ExternalId = "",
            Description = "CLOE-oil-level",
            Unit = "percentage",
            AssetId = updatedPlantData.InstallationCode,
            Value = notification.OilLevel,
            Timestamp = updatedPlantData.Timestamp ?? DateTime.UtcNow,
            Step = true,
            Metadata = new Dictionary<string, string>
            {
                { "Confidence", notification.Confidence.ToString() },
            },
        };
        await timeseriesService.TriggerTimeseriesUpload(uploadRequest);

        return Ok(new PlantDataResponse(updatedPlantData));
    }

    /// <summary>
    /// Notify that the CLOE workflow has exited with success or failure
    /// </summary>
    [HttpPut]
    [Authorize(Roles = Role.WorkflowStatusWrite)]
    [Route("exited")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlantDataResponse>> CLOEExited(
        [FromBody] WorkflowExitedNotification notification
    )
    {
        var workflowStatus = workflowService.GetWorkflowStatus(notification, "CLOE");

        PlantData updatedPlantData;
        try
        {
            updatedPlantData = await plantDataService.UpdateCLOEWorkflowStatus(
                notification.InspectionId,
                workflowStatus
            );
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Error occurred while updating CLOE workflow status");
            return BadRequest(ex.Message);
        }

        var cloeStep =
            updatedPlantData.GetWorkflowStep(WorkflowStepType.CLOEAnalysis)
            ?? throw new InvalidOperationException(
                $"CLOE analysis is not set up for plant data with inspection id {notification.InspectionId}"
            );
        var cloeData =
            cloeStep.CLOEData
            ?? throw new InvalidOperationException(
                $"CLOE data is not set up for plant data with inspection id {notification.InspectionId}"
            );
        var oilLevel = cloeData.OilLevel ?? 0F;
        var confidence = cloeData.Confidence ?? 0F;

        const float confidenceThreshold = 0.3F;
        const float lowOilLevelThreshold = 0.05F;

        string? warning = null;
        if (oilLevel < lowOilLevelThreshold && confidence >= confidenceThreshold)
        {
            warning = "Oil Level is below 5%";
        }

        string? value = null;
        if (confidence >= confidenceThreshold)
        {
            value = (oilLevel * 100).ToString();
        }

        var message = new SaraAnalysisResultMessage
        {
            InspectionId = updatedPlantData.InspectionId,
            AnalysisType = nameof(AnalysisType.ConstantLevelOiler),
            Value = value,
            Unit = "percentage",
            Confidence = confidence * 100,
            Warning = warning,
            StorageAccount = cloeStep.DestinationBlobStorageLocation.StorageAccount,
            BlobContainer = cloeStep.DestinationBlobStorageLocation.BlobContainer,
            BlobName = cloeStep.DestinationBlobStorageLocation.BlobName,
        };

        await mqttPublisherService.PublishSaraAnalysisResultAvailable(message);

        return Ok(new PlantDataResponse(updatedPlantData));
    }
}
