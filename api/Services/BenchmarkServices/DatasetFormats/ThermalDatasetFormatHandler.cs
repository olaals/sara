using api.Controllers.Models;

namespace api.Services.BenchmarkServices.DatasetFormats;

public sealed class ThermalDatasetFormatHandler(IBenchmarkBlobReader blobReader)
    : IBenchmarkDatasetFormatHandler
{
    public string Format => BenchmarkDatasetFormat.Thermal;
    public string PredictionsRelativePath => "review/predictions.json";

    public async Task<string?> TryResolveDefaultCoverBlobPathAsync(
        string container,
        string version,
        string split,
        CancellationToken cancellationToken = default
    )
    {
        var thermalIndex = await GetCachedDatasetIndexAsync(
            container,
            $"{version}/dataset/annotations/samples_{split}.json",
            cancellationToken
        );
        if (thermalIndex is null || thermalIndex.Samples.Count == 0)
        {
            return null;
        }

        var sample = SelectRepresentativeSample(thermalIndex);
        blobReader.ValidateRelativeImagePath(sample.PreviewFile);
        return sample.PreviewFile;
    }

    public async Task<BenchmarkImageComparisonDto> GetImageComparisonAsync(
        string container,
        string version,
        string split,
        string runId,
        int imageId,
        CancellationToken cancellationToken = default
    )
    {
        var (thermalIndex, sample) = await GetRequiredSampleAsync(
            container,
            version,
            split,
            imageId,
            cancellationToken
        );

        var prefix = $"{version}/benchmark-results/{split}/{runId}";
        var predIndex = await GetCachedPredictionsIndexAsync(
            container,
            $"{prefix}/{PredictionsRelativePath}",
            cancellationToken
        );
        var perImage = await blobReader.TryGetPerImageAsync(container, prefix, cancellationToken);
        var summary = perImage?.Items.FirstOrDefault(item => item.ImageId == imageId);

        List<BenchmarkCocoAnnotationDto> groundTruth = [];
        if (sample.Matchable && sample.GtPolygon is { Count: >= 3 })
        {
            groundTruth.Add(
                BenchmarkThermalDatasetIndex.BuildPolygonAnnotation(
                    annotationId: 1,
                    imageId: sample.Id,
                    categoryName: "target_roi",
                    polygon: sample.GtPolygon
                )
            );
        }

        List<BenchmarkCocoAnnotationDto> predictions = [];
        if (
            predIndex is not null
            && predIndex.PredictionsBySample.TryGetValue(imageId, out var pred)
            && pred.PredictedPolygon is { Count: >= 3 }
        )
        {
            predictions.Add(
                BenchmarkThermalDatasetIndex.BuildPolygonAnnotation(
                    annotationId: 1,
                    imageId: sample.Id,
                    categoryName: "predicted_roi",
                    polygon: pred.PredictedPolygon,
                    score: pred.MatchingConfidence
                )
            );
        }

        var referenceDto = BuildReferenceDto(thermalIndex, sample);

        return new BenchmarkImageComparisonDto(
            sample.Id,
            sample.FileName,
            sample.Width,
            sample.Height,
            Format,
            perImage?.Columns ?? [],
            summary,
            groundTruth,
            predictions,
            referenceDto
        );
    }

    public async Task<BenchmarkImageFile> GetDatasetImageAsync(
        string container,
        string version,
        string split,
        int imageId,
        CancellationToken cancellationToken = default
    )
    {
        var (_, sample) = await GetRequiredSampleAsync(
            container,
            version,
            split,
            imageId,
            cancellationToken
        );

        blobReader.ValidateRelativeImagePath(sample.PreviewFile);
        return await blobReader.DownloadDatasetImageFileAsync(
            container,
            sample.PreviewFile,
            sample.PreviewFile
        );
    }

    public async Task<BenchmarkImageFile> GetDatasetReferenceImageAsync(
        string container,
        string version,
        string split,
        int imageId,
        CancellationToken cancellationToken = default
    )
    {
        var (thermalIndex, sample) = await GetRequiredSampleAsync(
            container,
            version,
            split,
            imageId,
            cancellationToken
        );

        if (!thermalIndex.References.TryGetValue(sample.ReferenceId, out var reference))
        {
            throw new KeyNotFoundException(
                $"Reference '{sample.ReferenceId}' not found in thermal dataset split."
            );
        }

        blobReader.ValidateRelativeImagePath(reference.PreviewFile);
        return await blobReader.DownloadDatasetImageFileAsync(
            container,
            reference.PreviewFile,
            reference.PreviewFile
        );
    }

    private async Task<(
        BenchmarkThermalDatasetIndex Index,
        ThermalSampleEntry Sample
    )> GetRequiredSampleAsync(
        string container,
        string version,
        string split,
        int imageId,
        CancellationToken cancellationToken
    )
    {
        var thermalIndex = await GetCachedDatasetIndexAsync(
            container,
            $"{version}/dataset/annotations/samples_{split}.json",
            cancellationToken
        ) ?? throw new KeyNotFoundException("Ground-truth thermal annotations not found.");

        if (!thermalIndex.Samples.TryGetValue(imageId, out var sample))
        {
            throw new KeyNotFoundException($"Sample {imageId} not found in dataset split.");
        }

        return (thermalIndex, sample);
    }

    private Task<BenchmarkThermalDatasetIndex?> GetCachedDatasetIndexAsync(
        string container,
        string blobName,
        CancellationToken cancellationToken
    ) =>
        blobReader.GetCachedIndexAsync(
            "thermal-dataset",
            container,
            blobName,
            BenchmarkThermalDatasetIndex.ParseAsync,
            cancellationToken
        );

    private Task<BenchmarkThermalPredictionsIndex?> GetCachedPredictionsIndexAsync(
        string container,
        string blobName,
        CancellationToken cancellationToken
    ) =>
        blobReader.GetCachedIndexAsync(
            "thermal-predictions",
            container,
            blobName,
            BenchmarkThermalPredictionsIndex.ParseAsync,
            cancellationToken
        );

    private static ThermalSampleEntry SelectRepresentativeSample(
        BenchmarkThermalDatasetIndex thermalIndex
    )
    {
        var ordered = thermalIndex.Samples.Values.OrderBy(sample => sample.Id);
        return ordered.FirstOrDefault(sample => sample.Matchable) ?? ordered.First();
    }

    private static BenchmarkReferenceImageDto? BuildReferenceDto(
        BenchmarkThermalDatasetIndex thermalIndex,
        ThermalSampleEntry sample
    )
    {
        if (!thermalIndex.References.TryGetValue(sample.ReferenceId, out var reference))
        {
            return null;
        }

        List<BenchmarkCocoAnnotationDto> refAnnotations = [];
        if (reference.Polygon.Count >= 3)
        {
            refAnnotations.Add(
                BenchmarkThermalDatasetIndex.BuildPolygonAnnotation(
                    annotationId: 1,
                    imageId: sample.Id,
                    categoryName: "reference_roi",
                    polygon: reference.Polygon
                )
            );
        }

        return new BenchmarkReferenceImageDto(
            ReferenceId: reference.Id,
            FileName: reference.TiffFile,
            Width: reference.Width,
            Height: reference.Height,
            Annotations: refAnnotations
        );
    }
}
