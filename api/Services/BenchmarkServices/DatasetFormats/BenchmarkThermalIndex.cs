using System.Text.Json;
using System.Text.Json.Serialization;
using api.Controllers.Models;

namespace api.Services.BenchmarkServices.DatasetFormats;

public record ThermalReferenceEntry(
    string Id,
    string InstallationCode,
    string TagId,
    string InspectionDescription,
    string TiffFile,
    string PreviewFile,
    int Width,
    int Height,
    IReadOnlyList<double[]> Polygon
);

public record ThermalSampleEntry(
    int Id,
    string FileName,
    string PreviewFile,
    int Width,
    int Height,
    string ReferenceId,
    bool Matchable,
    IReadOnlyList<double[]>? GtPolygon
);

public record ThermalPredictionEntry(
    int SampleId,
    IReadOnlyList<double[]>? PredictedPolygon,
    double? PredictedTemperature,
    double? GtTemperature,
    double? TemperatureErrorC,
    double? PolygonIou,
    double? CentroidErrorPx,
    double? MatchingConfidence,
    bool Accepted
);

public sealed class BenchmarkThermalDatasetIndex
{
    public IReadOnlyDictionary<string, ThermalReferenceEntry> References { get; }
    public IReadOnlyDictionary<int, ThermalSampleEntry> Samples { get; }

    private BenchmarkThermalDatasetIndex(
        Dictionary<string, ThermalReferenceEntry> references,
        Dictionary<int, ThermalSampleEntry> samples
    )
    {
        References = references;
        Samples = samples;
    }

    public static async Task<BenchmarkThermalDatasetIndex> ParseAsync(
        Stream stream,
        CancellationToken cancellationToken = default
    )
    {
        var raw = await JsonSerializer.DeserializeAsync<RawThermalDataset>(
            stream,
            cancellationToken: cancellationToken
        ) ?? throw new JsonException("Thermal dataset annotation JSON was null.");

        var references = raw.References.ToDictionary(
            r => r.Id,
            r => new ThermalReferenceEntry(
                r.Id,
                r.InstallationCode,
                r.TagId,
                r.InspectionDescription,
                r.TiffFile,
                r.PreviewFile,
                r.Width,
                r.Height,
                r.Polygon
            ),
            StringComparer.Ordinal
        );

        var samples = raw.Samples.ToDictionary(
            s => s.Id,
            s => new ThermalSampleEntry(
                s.Id,
                s.FileName,
                s.PreviewFile,
                s.Width,
                s.Height,
                s.ReferenceId,
                s.Matchable,
                s.GtPolygon
            )
        );

        return new BenchmarkThermalDatasetIndex(references, samples);
    }

    public static BenchmarkCocoAnnotationDto BuildPolygonAnnotation(
        int annotationId,
        int imageId,
        string categoryName,
        IReadOnlyList<double[]> polygon,
        double? score = null
    )
    {
        var xs = polygon.Select(pt => pt[0]).ToList();
        var ys = polygon.Select(pt => pt[1]).ToList();
        var minX = xs.Min();
        var minY = ys.Min();
        var maxX = xs.Max();
        var maxY = ys.Max();
        var bbox = new List<double> { minX, minY, maxX - minX, maxY - minY };

        var flatCoords = new List<double>(polygon.Count * 2);
        foreach (var pt in polygon)
        {
            flatCoords.Add(pt[0]);
            flatCoords.Add(pt[1]);
        }

        var segmentation = JsonSerializer.SerializeToElement(new[] { flatCoords });

        return new BenchmarkCocoAnnotationDto(
            Id: annotationId,
            ImageId: imageId,
            CategoryId: 1,
            CategoryName: categoryName,
            Bbox: bbox,
            Area: ComputePolygonArea(polygon),
            Score: score,
            Segmentation: segmentation
        );
    }

    private static double ComputePolygonArea(IReadOnlyList<double[]> polygon)
    {
        if (polygon.Count < 3)
        {
            return 0.0;
        }

        var sum = 0.0;
        for (var i = 0; i < polygon.Count; i++)
        {
            var current = polygon[i];
            var next = polygon[(i + 1) % polygon.Count];
            sum += current[0] * next[1] - next[0] * current[1];
        }

        return Math.Abs(sum) * 0.5;
    }

    private sealed class RawThermalDataset
    {
        [JsonPropertyName("references")]
        public List<RawThermalReference> References { get; set; } = [];

        [JsonPropertyName("samples")]
        public List<RawThermalSample> Samples { get; set; } = [];
    }

    private sealed class RawThermalReference
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("installation_code")]
        public string InstallationCode { get; set; } = "";

        [JsonPropertyName("tag_id")]
        public string TagId { get; set; } = "";

        [JsonPropertyName("inspection_description")]
        public string InspectionDescription { get; set; } = "";

        [JsonPropertyName("tiff_file")]
        public string TiffFile { get; set; } = "";

        [JsonPropertyName("preview_file")]
        public string PreviewFile { get; set; } = "";

        [JsonPropertyName("width")]
        public int Width { get; set; }

        [JsonPropertyName("height")]
        public int Height { get; set; }

        [JsonPropertyName("polygon")]
        public List<double[]> Polygon { get; set; } = [];
    }

    private sealed class RawThermalSample
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("file_name")]
        public string FileName { get; set; } = "";

        [JsonPropertyName("preview_file")]
        public string PreviewFile { get; set; } = "";

        [JsonPropertyName("width")]
        public int Width { get; set; }

        [JsonPropertyName("height")]
        public int Height { get; set; }

        [JsonPropertyName("reference_id")]
        public string ReferenceId { get; set; } = "";

        [JsonPropertyName("matchable")]
        public bool Matchable { get; set; } = true;

        [JsonPropertyName("gt_polygon")]
        public List<double[]>? GtPolygon { get; set; }
    }
}

public sealed class BenchmarkThermalPredictionsIndex
{
    public IReadOnlyDictionary<int, ThermalPredictionEntry> PredictionsBySample { get; }

    private BenchmarkThermalPredictionsIndex(
        Dictionary<int, ThermalPredictionEntry> predictionsBySample
    )
    {
        PredictionsBySample = predictionsBySample;
    }

    public static async Task<BenchmarkThermalPredictionsIndex> ParseAsync(
        Stream stream,
        CancellationToken cancellationToken = default
    )
    {
        var raw = await JsonSerializer.DeserializeAsync<RawThermalPredictions>(
            stream,
            cancellationToken: cancellationToken
        ) ?? throw new JsonException("Thermal predictions JSON was null.");

        var bySample = raw.Items.ToDictionary(
            item => item.SampleId,
            item => new ThermalPredictionEntry(
                item.SampleId,
                item.PredictedPolygon,
                item.PredictedTemperature,
                item.GtTemperature,
                item.TemperatureErrorC,
                item.PolygonIou,
                item.CentroidErrorPx,
                item.MatchingConfidence,
                item.Accepted
            )
        );

        return new BenchmarkThermalPredictionsIndex(bySample);
    }

    private sealed class RawThermalPredictions
    {
        [JsonPropertyName("items")]
        public List<RawThermalPredictionItem> Items { get; set; } = [];
    }

    private sealed class RawThermalPredictionItem
    {
        [JsonPropertyName("sample_id")]
        public int SampleId { get; set; }

        [JsonPropertyName("predicted_polygon")]
        public List<double[]>? PredictedPolygon { get; set; }

        [JsonPropertyName("predicted_temperature")]
        public double? PredictedTemperature { get; set; }

        [JsonPropertyName("gt_temperature")]
        public double? GtTemperature { get; set; }

        [JsonPropertyName("temperature_error_c")]
        public double? TemperatureErrorC { get; set; }

        [JsonPropertyName("polygon_iou")]
        public double? PolygonIou { get; set; }

        [JsonPropertyName("centroid_error_px")]
        public double? CentroidErrorPx { get; set; }

        [JsonPropertyName("matching_confidence")]
        public double? MatchingConfidence { get; set; }

        [JsonPropertyName("accepted")]
        public bool Accepted { get; set; }
    }
}
