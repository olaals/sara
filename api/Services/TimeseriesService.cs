using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using api.MQTT;
using api.Telemetry;

namespace api.Services;

public record TriggerTimeseriesUploadRequest
{
    public required string Name { get; init; }
    public required string Facility { get; init; }
    public required string ExternalId { get; init; }
    public required string Description { get; init; }
    public required string Unit { get; init; }
    public required string AssetId { get; init; }
    public float Value { get; init; }
    public DateTime Timestamp { get; init; }
    public bool Step { get; init; } = true;
    public Dictionary<string, string> Metadata { get; init; } = [];
}

public record TriggerTimeseriesUploadResponse
{
    public string? TimeseriesId { get; init; }
    public int StatusCode { get; init; }
    public string? Message { get; init; }
}

public record FetchCO2MeasurementRequest
{
    [JsonPropertyName("facility")]
    public required string Facility { get; set; }

    [JsonPropertyName("task_start_time")]
    public required string TaskStartTime { get; set; }

    [JsonPropertyName("task_end_time")]
    public required string TaskEndTime { get; set; }

    [JsonPropertyName("inspection_name")]
    public required string InspectionName { get; set; }
}

public interface ITimeseriesService
{
    public Task TriggerTimeseriesUpload(TriggerTimeseriesUploadRequest isarInspectionValueMessage);
    public Task<double?> FetchCO2ConcentrationFromTimeseries(
        FetchCO2MeasurementRequest fetchRequest
    );
}

public class TimeseriesService(
    IConfiguration configuration,
    ILogger<TimeseriesService> logger,
    HttpClient? httpClient = null
) : ITimeseriesService
{
    private static readonly HttpClient SharedClient = new();
    private readonly HttpClient client = httpClient ?? SharedClient;
    private readonly string _baseUrl =
        configuration["SARATimeseriesBaseUrl"]
        ?? throw new InvalidOperationException("SARATimeseriesBaseUrl is not configured.");
    private static readonly JsonSerializerOptions useCamelCaseOption = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task TriggerTimeseriesUpload(TriggerTimeseriesUploadRequest uploadRequest)
    {
        if (_baseUrl == "")
        {
            SaraMetrics.RecordDownstreamDelivery("timeseries", "upload", OperationOutcome.Skipped);
            return;
        }

        var outcome = OperationOutcome.Failure;
        try
        {
            var url = $"{_baseUrl}/timeseries/datapoint";

            var json = JsonSerializer.Serialize(uploadRequest, useCamelCaseOption);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                var uploadResponse =
                    await response.Content.ReadFromJsonAsync<TriggerTimeseriesUploadResponse>(
                        useCamelCaseOption
                    );
                outcome =
                    uploadResponse is not null && uploadResponse.StatusCode < 400
                        ? OperationOutcome.Success
                        : OperationOutcome.Failure;
                logger.LogInformation(
                    "Successfully uploaded datapoint with name: {Name} and description: {Description} to Timeseries. TimeseriesId: {TimeseriesId}",
                    uploadRequest.Name,
                    uploadRequest.Description,
                    uploadResponse?.TimeseriesId
                );
            }
            else
            {
                var errorMessage = await response.Content.ReadAsStringAsync();
                logger.LogError(
                    "Failed to upload datapoint with name: {Name} and description: {Description} to Timeseries with statusCode: '{StatusCode}' and message: '{Message}'.",
                    uploadRequest.Name,
                    uploadRequest.Description,
                    response.StatusCode,
                    errorMessage
                );
            }
        }
        finally
        {
            // Includes non-success responses that are logged rather than thrown.
            SaraMetrics.RecordDownstreamDelivery("timeseries", "upload", outcome);
        }
    }

    public async Task<double?> FetchCO2ConcentrationFromTimeseries(
        FetchCO2MeasurementRequest fetchRequest
    )
    {
        if (_baseUrl == "")
        {
            SaraMetrics.RecordDownstreamDelivery(
                "timeseries",
                "fetch_co2",
                OperationOutcome.Skipped
            );
            return null;
        }

        var outcome = OperationOutcome.Failure;
        try
        {
            var url = $"{_baseUrl}/timeseries/get-co2-concentration";

            var json = JsonSerializer.Serialize(fetchRequest, useCamelCaseOption);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                var value = await response.Content.ReadFromJsonAsync<double?>();
                outcome = value is null ? OperationOutcome.Skipped : OperationOutcome.Success;
                return value;
            }
            else
            {
                var errorMessage = await response.Content.ReadAsStringAsync();
                logger.LogError(
                    "Failed to fetch CO2 concentration from Timeseries with statusCode: '{StatusCode}' and message: '{Message}'",
                    response.StatusCode,
                    errorMessage
                );
                throw new HttpRequestException(
                    errorMessage ?? "Failed to fetch CO2 concentration from Timeseries",
                    null,
                    response.StatusCode
                );
            }
        }
        finally
        {
            SaraMetrics.RecordDownstreamDelivery("timeseries", "fetch_co2", outcome);
        }
    }
}
