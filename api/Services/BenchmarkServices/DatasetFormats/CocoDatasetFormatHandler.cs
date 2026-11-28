using api.Controllers.Models;

namespace api.Services.BenchmarkServices.DatasetFormats;

public sealed class CocoDatasetFormatHandler(IBenchmarkBlobReader blobReader)
    : IBenchmarkDatasetFormatHandler
{
    public string Format => BenchmarkDatasetFormat.Coco;
    public string PredictionsRelativePath => "review/instances_predictions.json";

    public async Task<string?> TryResolveDefaultCoverBlobPathAsync(
        string container,
        string version,
        string split,
        CancellationToken cancellationToken = default
    )
    {
        var gtIndex = await GetCachedCocoIndexAsync(
            container,
            $"{version}/dataset/annotations/instances_{split}.json",
            cancellationToken
        );
        if (gtIndex is null || gtIndex.Images.Count == 0)
        {
            return null;
        }

        var image = SelectRepresentativeImage(gtIndex);
        blobReader.ValidateRelativeImagePath(image.FileName);
        return $"{version}/dataset/images/{split}/{image.FileName}";
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
        var (gtIndex, image) = await GetRequiredCocoImageAsync(
            container,
            version,
            split,
            imageId,
            cancellationToken
        );

        var prefix = $"{version}/benchmark-results/{split}/{runId}";
        var predIndex = await GetCachedCocoIndexAsync(
            container,
            $"{prefix}/{PredictionsRelativePath}",
            cancellationToken
        );
        var perImage = await blobReader.TryGetPerImageAsync(container, prefix, cancellationToken);
        var summary = perImage?.Items.FirstOrDefault(item => item.ImageId == imageId);

        return new BenchmarkImageComparisonDto(
            image.Id,
            image.FileName,
            image.Width,
            image.Height,
            Format,
            perImage?.Columns ?? [],
            summary,
            gtIndex.GetAnnotations(imageId),
            predIndex?.GetAnnotations(imageId) ?? []
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
        var (_, image) = await GetRequiredCocoImageAsync(
            container,
            version,
            split,
            imageId,
            cancellationToken
        );

        blobReader.ValidateRelativeImagePath(image.FileName);
        return await blobReader.DownloadDatasetImageFileAsync(
            container,
            $"{version}/dataset/images/{split}/{image.FileName}",
            image.FileName
        );
    }

    public Task<BenchmarkImageFile> GetDatasetReferenceImageAsync(
        string container,
        string version,
        string split,
        int imageId,
        CancellationToken cancellationToken = default
    ) =>
        throw new KeyNotFoundException(
            $"Dataset format '{Format}' does not support reference images."
        );

    private async Task<(BenchmarkCocoIndex Index, CocoImageEntry Image)> GetRequiredCocoImageAsync(
        string container,
        string version,
        string split,
        int imageId,
        CancellationToken cancellationToken
    )
    {
        var gtIndex = await GetCachedCocoIndexAsync(
            container,
            $"{version}/dataset/annotations/instances_{split}.json",
            cancellationToken
        ) ?? throw new KeyNotFoundException("Ground-truth COCO annotations not found.");

        if (!gtIndex.Images.TryGetValue(imageId, out var image))
        {
            throw new KeyNotFoundException($"Image {imageId} not found in dataset split.");
        }

        return (gtIndex, image);
    }

    private Task<BenchmarkCocoIndex?> GetCachedCocoIndexAsync(
        string container,
        string blobName,
        CancellationToken cancellationToken
    ) =>
        blobReader.GetCachedIndexAsync(
            "coco",
            container,
            blobName,
            BenchmarkCocoIndex.ParseAsync,
            cancellationToken
        );

    private static CocoImageEntry SelectRepresentativeImage(BenchmarkCocoIndex gtIndex)
    {
        var ordered = gtIndex.Images.Values.OrderBy(image => image.Id);
        return ordered.FirstOrDefault(image => gtIndex.GetAnnotations(image.Id).Count > 0)
            ?? ordered.First();
    }
}
