using System.Text.Json;
using System.Text.Json.Serialization;

namespace api.Controllers.Models;

public record BenchmarkParameterDefinitionDto(
    string Name,
    string Label,
    double? Min,
    bool MinExclusive,
    double? Max,
    double? Step,
    string? Placeholder,
    IReadOnlyList<string> OnlyForMethods
);

public record BenchmarkDatasetDto(
    string Id,
    string Label,
    string Description,
    string Container,
    string Format,
    string DefaultSplit,
    IReadOnlyList<string> Devices,
    IReadOnlyList<string> Methods,
    IReadOnlyList<BenchmarkParameterDefinitionDto> Parameters,
    IReadOnlyList<string> Versions
);

public record TriggerBenchmarkRequest(
    string DatasetId,
    string Version,
    string Split,
    string Device,
    string? RunId,
    string? Method = null,
    Dictionary<string, double?>? Parameters = null
);

public record BenchmarkJobDto(
    string WorkflowName,
    string? WorkflowUid,
    string DatasetId,
    string Version,
    string Split,
    string Device,
    string? Method,
    IReadOnlyDictionary<string, string> Parameters,
    string RunId,
    string Phase,
    string? Message,
    DateTime? CreatedAt,
    DateTime? StartedAt,
    DateTime? FinishedAt
);

public record BenchmarkResultDto(
    string RunId,
    BenchmarkRunMetadataDto? Run,
    BenchmarkMetricsDto? Metrics,
    string? Error
);

public record BenchmarkRunMetadataDto(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("run_id")] string RunId,
    [property: JsonPropertyName("dataset")] BenchmarkRunDatasetDto Dataset,
    [property: JsonPropertyName("artifact")] string Artifact,
    [property: JsonPropertyName("evaluator")] string Evaluator,
    [property: JsonPropertyName("protocol")] string Protocol,
    [property: JsonPropertyName("configuration")] Dictionary<string, JsonElement?>? Configuration,
    [property: JsonPropertyName("execution")] BenchmarkExecutionDto Execution
);

public record BenchmarkRunDatasetDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("split")] string Split,
    [property: JsonPropertyName("manifest_sha256")] string ManifestSha256
);

public record BenchmarkExecutionDto(
    [property: JsonPropertyName("hardware")] string Hardware,
    [property: JsonPropertyName("timing_scope")] string TimingScope
);

public record BenchmarkSummaryMetricDto(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("value")] double? Value,
    [property: JsonPropertyName("format")] string Format,
    [property: JsonPropertyName("subtitle")] string? Subtitle = null,
    [property: JsonPropertyName("primary")] bool Primary = true
);

public record BenchmarkMetricsDto(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("run_id")] string RunId,
    [property: JsonPropertyName("processing")] BenchmarkProcessingDto Processing,
    [property: JsonPropertyName("summary_metrics")]
        IReadOnlyList<BenchmarkSummaryMetricDto> SummaryMetrics,
    [property: JsonPropertyName("evaluations")]
        Dictionary<string, BenchmarkEvaluationDto> Evaluations
);

public record BenchmarkProcessingDto(
    [property: JsonPropertyName("unit")] string Unit,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("seconds")] double Seconds,
    [property: JsonPropertyName("per_second")] double PerSecond
);

public record BenchmarkEvaluationDto(
    [property: JsonPropertyName("labels")] List<string> Labels,
    [property: JsonPropertyName("confusion_matrix")] List<List<int?>> ConfusionMatrix
);

public record BenchmarkPerImageColumnDto(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("format")] string Format
);

public record BenchmarkImageResultDto(
    [property: JsonPropertyName("image_id")] int ImageId,
    [property: JsonPropertyName("file_name")] string FileName,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("passed")] bool Passed,
    [property: JsonPropertyName("missed")] bool Missed,
    [property: JsonPropertyName("false_positive")] bool FalsePositive,
    [property: JsonPropertyName("values")] Dictionary<string, JsonElement?> Values
);

public record BenchmarkPerImageTableDto(
    [property: JsonPropertyName("columns")] IReadOnlyList<BenchmarkPerImageColumnDto> Columns,
    [property: JsonPropertyName("items")] IReadOnlyList<BenchmarkImageResultDto> Items
);

public record BenchmarkRunDetailDto(
    string RunId,
    BenchmarkRunMetadataDto? Run,
    BenchmarkMetricsDto? Metrics,
    BenchmarkPerImageTableDto? PerImage,
    bool HasPredictions,
    string? Error
);

public record BenchmarkCocoAnnotationDto(
    int Id,
    int ImageId,
    int CategoryId,
    string CategoryName,
    IReadOnlyList<double>? Bbox,
    double? Area,
    double? Score,
    JsonElement? Segmentation
);

public record BenchmarkReferenceImageDto(
    string ReferenceId,
    string FileName,
    int Width,
    int Height,
    IReadOnlyList<BenchmarkCocoAnnotationDto> Annotations
);

public record BenchmarkImageComparisonDto(
    int ImageId,
    string FileName,
    int Width,
    int Height,
    string Format,
    IReadOnlyList<BenchmarkPerImageColumnDto> SummaryColumns,
    BenchmarkImageResultDto? Summary,
    IReadOnlyList<BenchmarkCocoAnnotationDto> GroundTruth,
    IReadOnlyList<BenchmarkCocoAnnotationDto> Predictions,
    BenchmarkReferenceImageDto? Reference = null
);
