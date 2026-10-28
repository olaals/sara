using System.Text.Json;
using System.Text.Json.Serialization;
using api.Telemetry;
using MQTTnet.Extensions.ManagedClient;

namespace api.MQTT
{
    public interface IMqttPublisherService
    {
        public Task PublishSaraInspectionRecordAvailable(
            SaraInspectionRecordAvailableMessage inspectionRecordAvailableMessage
        );
        public Task PublishSaraVisualizationAvailable(
            SaraVisualizationAvailableMessage visualizationAvailableMessage
        );
        public Task PublishSaraAnalysisResultAvailable(
            SaraAnalysisResultMessage saraAnalysisResultMessage
        );
    }

    public class MqttPublisherService(ILogger<MqttService> logger, IManagedMqttClient mqttClient)
        : IMqttPublisherService
    {
        private readonly ILogger<MqttService> _logger = logger;

        private readonly IManagedMqttClient _mqttClient = mqttClient;

        private static readonly JsonSerializerOptions serializerOptions = new()
        {
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };

        public Task PublishSaraInspectionRecordAvailable(
            SaraInspectionRecordAvailableMessage inspectionRecordAvailableMessage
        ) =>
            SerializeAndPublishAsync(
                "sara/inspection_record_available",
                inspectionRecordAvailableMessage,
                "enqueue_inspection_record"
            );

        public async Task PublishSaraVisualizationAvailable(
            SaraVisualizationAvailableMessage visualizationAvailableMessage
        )
        {
            var topic = "sara/visualization_available";
            await SerializeAndPublishAsync(
                topic,
                visualizationAvailableMessage,
                "enqueue_visualization"
            );
        }

        public async Task PublishSaraAnalysisResultAvailable(
            SaraAnalysisResultMessage analysisResultMessage
        )
        {
            var topic = "sara/analysis_result_available";
            await SerializeAndPublishAsync(topic, analysisResultMessage, "enqueue_analysis_result");
        }

        private async Task SerializeAndPublishAsync<T>(string topic, T message, string operation)
        {
            string payload;
            try
            {
                payload = JsonSerializer.Serialize(message, serializerOptions);
            }
            catch
            {
                SaraMetrics.RecordDownstreamDelivery("mqtt", operation, OperationOutcome.Failure);
                throw;
            }
            await PublishAsync(topic, payload, operation);
        }

        private async Task PublishAsync(string topic, string payload, string operation)
        {
            _logger.LogInformation("Topic: {topic} - Payload to send: \n{payload}", topic, payload);

            try
            {
                await _mqttClient.EnqueueAsync(topic, payload);
                SaraMetrics.RecordDownstreamDelivery("mqtt", operation, OperationOutcome.Success);
            }
            catch (Exception ex)
            {
                SaraMetrics.RecordDownstreamDelivery("mqtt", operation, OperationOutcome.Failure);
                _logger.LogError(
                    "Could not send MQTT message '{message}' object on topic '{topic}'. {exception}",
                    payload,
                    topic,
                    ex.Message
                );
                return;
            }
        }
    }
}
