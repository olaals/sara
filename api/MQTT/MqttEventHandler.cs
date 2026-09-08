using System.ComponentModel.DataAnnotations;
using api.Database.Models;
using api.Services;
using api.Utilities;

namespace api.MQTT
{
    /// <summary>
    ///     A background service which listens to events and performs callback functions.
    /// </summary>
    public class MqttEventHandler : EventHandlerBase
    {
        private readonly ILogger<MqttEventHandler> _logger;

        private readonly IServiceScopeFactory _scopeFactory;

        public MqttEventHandler(ILogger<MqttEventHandler> logger, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            // Reason for using factory: https://www.thecodebuzz.com/using-dbcontext-instance-in-ihostedservice/
            _scopeFactory = scopeFactory;

            Subscribe();
        }

        private IInspectionRecordService InspectionRecordService =>
            _scopeFactory
                .CreateScope()
                .ServiceProvider.GetRequiredService<IInspectionRecordService>();
        private IAnalysisTriggerService AnalysisTriggerService =>
            _scopeFactory
                .CreateScope()
                .ServiceProvider.GetRequiredService<IAnalysisTriggerService>();
        private ITimeseriesService TimeseriesService =>
            _scopeFactory.CreateScope().ServiceProvider.GetRequiredService<ITimeseriesService>();
        private IBlobStorageService BlobStorageService =>
            _scopeFactory.CreateScope().ServiceProvider.GetRequiredService<IBlobStorageService>();

        public override void Subscribe()
        {
            MqttService.MqttIsarInspectionResultReceived += OnIsarInspectionResult;
            MqttService.MqttIsarInspectionValueReceived += OnIsarInspectionValue;
        }

        public override void Unsubscribe()
        {
            MqttService.MqttIsarInspectionResultReceived -= OnIsarInspectionResult;
            MqttService.MqttIsarInspectionValueReceived -= OnIsarInspectionValue;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await stoppingToken;
        }

        private bool ValidateIsarInspectionResultMessage(IsarInspectionResultMessage message)
        {
            var validationResults = new List<ValidationResult>();
            bool isValid = Validator.TryValidateObject(
                message,
                new ValidationContext(message),
                validationResults,
                true
            );
            isValid &= Validator.TryValidateObject(
                message.InspectionDataPath,
                new ValidationContext(message.InspectionDataPath),
                validationResults,
                true
            );
            if (message.AcousticMetadata is not null)
            {
                isValid &= Validator.TryValidateObject(
                    message.AcousticMetadata,
                    new ValidationContext(message.AcousticMetadata),
                    validationResults,
                    true
                );
            }
            if (message.AnalysisGroup is not null)
            {
                isValid &= Validator.TryValidateObject(
                    message.AnalysisGroup,
                    new ValidationContext(message.AnalysisGroup),
                    validationResults,
                    true
                );
            }
            if (message.RobotPose is not null)
            {
                isValid &= Validator.TryValidateObject(
                    message.RobotPose,
                    new ValidationContext(message.RobotPose),
                    validationResults,
                    true
                );
                if (message.RobotPose.Position is not null)
                {
                    isValid &= Validator.TryValidateObject(
                        message.RobotPose.Position,
                        new ValidationContext(message.RobotPose.Position),
                        validationResults,
                        true
                    );
                }
                if (message.RobotPose.Orientation is not null)
                {
                    isValid &= Validator.TryValidateObject(
                        message.RobotPose.Orientation,
                        new ValidationContext(message.RobotPose.Orientation),
                        validationResults,
                        true
                    );
                }
            }
            if (message.TargetPosition is not null)
            {
                isValid &= Validator.TryValidateObject(
                    message.TargetPosition,
                    new ValidationContext(message.TargetPosition),
                    validationResults,
                    true
                );
            }
            if (!isValid)
            {
                foreach (var validationResult in validationResults)
                {
                    _logger.LogError(
                        "Message validation error: {ErrorMessage}",
                        validationResult.ErrorMessage
                    );
                }
            }
            return isValid;
        }

        private async void OnIsarInspectionResult(object? sender, MqttReceivedArgs mqttArgs)
        {
            if (mqttArgs.Message is not IsarInspectionResultMessage isarInspectionResultMessage)
            {
                _logger.LogError(
                    "Received ISAR inspection result message is not of type IsarInspectionResultMessage"
                );
                return;
            }
            await ProcessIsarInspectionResult(isarInspectionResultMessage);
        }

        /// <summary>
        /// Processes a validated ISAR inspection result message: creates the
        /// inspection record and triggers analyses. Exposed as <c>internal</c>
        /// so integration tests can drive the pipeline deterministically
        /// without relying on the MQTT static event.
        /// </summary>
        internal async Task ProcessIsarInspectionResult(
            IsarInspectionResultMessage isarInspectionResultMessage
        )
        {
            if (!ValidateIsarInspectionResultMessage(isarInspectionResultMessage))
            {
                _logger.LogError(
                    "Received ISAR inspection result message is invalid for InspectionId: {InspectionId}",
                    isarInspectionResultMessage.InspectionId
                );
                return;
            }

            _logger.LogInformation(
                "Received ISAR inspection result message with InspectionId: {InspectionId}, TagID: {TagID}, InspectionDescription: {InspectionDescription}",
                isarInspectionResultMessage.InspectionId,
                isarInspectionResultMessage.TagId,
                isarInspectionResultMessage.InspectionDescription
            );

            var inspectionDataPath = isarInspectionResultMessage.InspectionDataPath;
            var blobStorageLocation = new BlobStorageLocation
            {
                StorageAccount = inspectionDataPath.StorageAccount,
                BlobContainer = inspectionDataPath.BlobContainer,
                BlobName = inspectionDataPath.BlobName,
            };

            bool blobExists;
            try
            {
                blobExists = await BlobStorageService.ExistsAsync(blobStorageLocation);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to verify blob existence for ISAR inspection result. InspectionId: {InspectionId}, "
                        + "BlobPath: {StorageAccount}/{BlobContainer}/{BlobName}, ErrorMessage: {ErrorMessage}.",
                    isarInspectionResultMessage.InspectionId,
                    inspectionDataPath.StorageAccount,
                    inspectionDataPath.BlobContainer,
                    inspectionDataPath.BlobName,
                    ex.Message
                );
                return;
            }

            if (!blobExists)
            {
                _logger.LogError(
                    "Blob location referenced by ISAR inspection result does not exist. No inspection record "
                        + "or analysis will be created. InspectionId: {InspectionId}, "
                        + "BlobPath: {StorageAccount}/{BlobContainer}/{BlobName}.",
                    isarInspectionResultMessage.InspectionId,
                    inspectionDataPath.StorageAccount,
                    inspectionDataPath.BlobContainer,
                    inspectionDataPath.BlobName
                );
                return;
            }

            InspectionRecord? inspectionRecord;
            try
            {
                inspectionRecord = await InspectionRecordService.CreateFromMqttMessage(
                    isarInspectionResultMessage
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error occurred while processing MQTT message from ISAR for InspectionId: {InspectionId}. "
                        + "TagID: {TagID}, InstallationCode: {InstallationCode}, RobotName: {RobotName}, "
                        + "RawBlobPath: {RawStorageAccount}/{RawBlobContainer}/{RawBlobName}, ErrorMessage: {ErrorMessage}, "
                        + "InnerErrorMessage: {InnerErrorMessage}.",
                    isarInspectionResultMessage.InspectionId,
                    isarInspectionResultMessage.TagId,
                    isarInspectionResultMessage.InstallationCode,
                    isarInspectionResultMessage.RobotName,
                    isarInspectionResultMessage.InspectionDataPath.StorageAccount,
                    isarInspectionResultMessage.InspectionDataPath.BlobContainer,
                    isarInspectionResultMessage.InspectionDataPath.BlobName,
                    ex.Message,
                    ex.InnerException?.Message
                );
                return;
            }

            if (inspectionRecord is null)
            {
                _logger.LogInformation(
                    "Skipping duplicate ISAR inspection result because an inspection record already exists. "
                        + "InspectionId: {InspectionId}, TagID: {TagID}, InstallationCode: {InstallationCode}",
                    isarInspectionResultMessage.InspectionId,
                    isarInspectionResultMessage.TagId,
                    isarInspectionResultMessage.InstallationCode
                );
                return;
            }

            try
            {
                await AnalysisTriggerService.OnInspectionRecordCreated(inspectionRecord);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error occurred while triggering analyses for InspectionId: {InspectionId}",
                    isarInspectionResultMessage.InspectionId
                );
                return;
            }
        }

        private async void OnIsarInspectionValue(object? sender, MqttReceivedArgs mqttArgs)
        {
            try
            {
                var isarInspectionValueMessage = (IsarInspectionValueMessage)mqttArgs.Message;
                _logger.LogInformation(
                    "Received ISAR inspection value message with InspectionId: {InspectionId}",
                    isarInspectionValueMessage.InspectionId
                );
                var name = CreateTimeseriesNameFromMQTT(isarInspectionValueMessage);
                var uploadRequest = new TriggerTimeseriesUploadRequest
                {
                    Name = name,
                    Facility = isarInspectionValueMessage.InstallationCode,
                    ExternalId = "",
                    Description = isarInspectionValueMessage.InspectionType,
                    Unit = isarInspectionValueMessage.Unit,
                    AssetId = isarInspectionValueMessage.InstallationCode, // TODO: check what assetId is
                    Value = isarInspectionValueMessage.Value,
                    Timestamp = isarInspectionValueMessage.Timestamp,
                    Metadata = new Dictionary<string, string>
                    {
                        { "tag_id", isarInspectionValueMessage.TagID },
                        {
                            "inspection_description",
                            isarInspectionValueMessage.InspectionDescription
                        },
                        { "robot_name", isarInspectionValueMessage.RobotName },
                    },
                };
                await TimeseriesService.TriggerTimeseriesUpload(uploadRequest);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error occurred while processing ISAR inspection value message"
                );
            }
        }

        private static string CreateTimeseriesNameFromMQTT(
            IsarInspectionValueMessage isarInspectionValueMessage
        )
        {
            string description =
                isarInspectionValueMessage.InspectionDescription?.Replace(" ", "-") ?? string.Empty;
            var name =
                $"{isarInspectionValueMessage.InstallationCode}_"
                + $"{FloorWithTolerance(isarInspectionValueMessage.X)}E_"
                + $"{FloorWithTolerance(isarInspectionValueMessage.Y)}N_"
                + $"{FloorWithTolerance(isarInspectionValueMessage.Z)}U_"
                + $"{isarInspectionValueMessage.TagID}_"
                + $"{isarInspectionValueMessage.RobotName}_"
                + $"{description}";
            return name;
        }

        // Tolerance set to 0.06 by default to mimic expected fault tolerance in a robot positioning system
        public static int FloorWithTolerance(double value, double tolerance = 0.06)
        {
            var floored = (int)Math.Floor(value);
            if (value - floored >= 1 - tolerance)
                return floored + 1;
            return floored;
        }
    }
}
