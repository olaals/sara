using api.Controllers.Models;

namespace api.Services.BenchmarkServices.DatasetFormats;

public interface IBenchmarkDatasetFormatHandler
{
    string Format { get; }
    string PredictionsRelativePath { get; }

    Task<string?> TryResolveDefaultCoverBlobPathAsync(
        string container,
        string version,
        string split,
        CancellationToken cancellationToken = default
    );

    Task<BenchmarkImageComparisonDto> GetImageComparisonAsync(
        string container,
        string version,
        string split,
        string runId,
        int imageId,
        CancellationToken cancellationToken = default
    );

    Task<BenchmarkImageFile> GetDatasetImageAsync(
        string container,
        string version,
        string split,
        int imageId,
        CancellationToken cancellationToken = default
    );

    Task<BenchmarkImageFile> GetDatasetReferenceImageAsync(
        string container,
        string version,
        string split,
        int imageId,
        CancellationToken cancellationToken = default
    );
}
