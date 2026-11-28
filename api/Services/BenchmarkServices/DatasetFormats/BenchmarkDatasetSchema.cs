using System.Text.Json;
using System.Text.Json.Serialization;

namespace api.Services.BenchmarkServices.DatasetFormats;

public static class BenchmarkDatasetFormat
{
    public const string Coco = "coco";
    public const string Thermal = "thermal";

    private static readonly HashSet<string> SupportedFormats = new(
        [Coco, Thermal],
        StringComparer.OrdinalIgnoreCase
    );

    public static bool IsSupported(string? format) =>
        !string.IsNullOrWhiteSpace(format) && SupportedFormats.Contains(format);
}

public sealed record BenchmarkDatasetSchema(string Format, int SchemaVersion)
{
    public const string BlobName = "schema.json";

    public static async Task<BenchmarkDatasetSchema> ParseAsync(
        Stream stream,
        string container,
        CancellationToken cancellationToken = default
    )
    {
        var raw = await JsonSerializer.DeserializeAsync<RawDatasetSchema>(
            stream,
            cancellationToken: cancellationToken
        )
            ?? throw new InvalidOperationException(
                $"Dataset schema '{BlobName}' in container '{container}' was empty."
            );

        var normalizedFormat = raw.Format?.Trim().ToLowerInvariant() ?? "";
        if (!BenchmarkDatasetFormat.IsSupported(normalizedFormat))
        {
            throw new InvalidOperationException(
                $"Unsupported dataset format '{raw.Format}' in '{container}/{BlobName}'."
            );
        }

        if (raw.SchemaVersion != 1)
        {
            throw new InvalidOperationException(
                $"Unsupported dataset schema_version '{raw.SchemaVersion}' in '{container}/{BlobName}'."
            );
        }

        return new BenchmarkDatasetSchema(normalizedFormat, raw.SchemaVersion);
    }

    private sealed class RawDatasetSchema
    {
        [JsonPropertyName("format")]
        public string? Format { get; set; }

        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; set; }
    }
}
