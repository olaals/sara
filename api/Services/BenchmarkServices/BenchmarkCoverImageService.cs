using api.Configurations;
using api.Services.BenchmarkServices.DatasetFormats;
using Azure;

namespace api.Services.BenchmarkServices;

public interface IBenchmarkCoverImageService
{
    Task<BenchmarkImageFile> GetCoverImageAsync(
        BenchmarkDefinition definition,
        IBenchmarkDatasetFormatHandler formatHandler,
        CancellationToken cancellationToken = default
    );
    Task UploadCoverImageAsync(
        BenchmarkDefinition definition,
        Stream content,
        CancellationToken cancellationToken = default
    );
    Task ResetCoverImageAsync(
        BenchmarkDefinition definition,
        CancellationToken cancellationToken = default
    );
}

public sealed class BenchmarkCoverImageService(
    IBlobStorageService blobStorageService,
    IBenchmarkBlobReader blobReader,
    ILogger<BenchmarkCoverImageService> logger
) : IBenchmarkCoverImageService
{
    private const string CoverImageBlobName = "cover-image";
    private const int MaxCoverImageBytes = 5 * 1024 * 1024;

    public async Task<BenchmarkImageFile> GetCoverImageAsync(
        BenchmarkDefinition definition,
        IBenchmarkDatasetFormatHandler formatHandler,
        CancellationToken cancellationToken = default
    )
    {
        var savedCover = await TryDownloadSavedCoverImageAsync(definition);
        if (savedCover is not null)
        {
            return savedCover;
        }

        var defaultCover = await TrySelectAndSaveDefaultCoverImageAsync(
            definition,
            formatHandler,
            cancellationToken
        );
        return defaultCover
            ?? throw new KeyNotFoundException(
                $"No cover image is available for benchmark dataset '{definition.Id}'."
            );
    }

    public async Task UploadCoverImageAsync(
        BenchmarkDefinition definition,
        Stream content,
        CancellationToken cancellationToken = default
    )
    {
        var bytes = await ReadBoundedImageBytesAsync(content, cancellationToken);
        var contentType = DetectImageContentType(bytes)
            ?? throw new ArgumentException("Cover image must be a valid PNG, JPEG, or WebP file.");

        using var uploadStream = new MemoryStream(bytes);
        await blobStorageService.UploadBlobAsync(
            blobReader.BuildBlobLocation(definition.Container, CoverImageBlobName),
            uploadStream,
            contentType
        );
    }

    public Task ResetCoverImageAsync(
        BenchmarkDefinition definition,
        CancellationToken cancellationToken = default
    ) =>
        blobStorageService.DeleteBlobAsync(
            blobReader.BuildBlobLocation(definition.Container, CoverImageBlobName),
            cancellationToken
        );

    private async Task<BenchmarkImageFile?> TryDownloadSavedCoverImageAsync(
        BenchmarkDefinition definition
    )
    {
        try
        {
            using var stream = await blobStorageService.DownloadBlobAsync(
                blobReader.BuildBlobLocation(definition.Container, CoverImageBlobName)
            );
            var bytes = stream.ToArray();
            var contentType = DetectImageContentType(bytes) ?? "application/octet-stream";
            return new BenchmarkImageFile(bytes, contentType);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private async Task<BenchmarkImageFile?> TrySelectAndSaveDefaultCoverImageAsync(
        BenchmarkDefinition definition,
        IBenchmarkDatasetFormatHandler formatHandler,
        CancellationToken cancellationToken
    )
    {
        var split = string.IsNullOrWhiteSpace(definition.DefaultSplit)
            ? "val"
            : definition.DefaultSplit;
        var versions = await blobReader.ListDatasetVersionsAsync(definition, cancellationToken);

        foreach (var version in versions)
        {
            var coverBlobPath = await formatHandler.TryResolveDefaultCoverBlobPathAsync(
                definition.Container,
                version,
                split,
                cancellationToken
            );
            if (coverBlobPath is null)
            {
                continue;
            }

            var imageLocation = blobReader.BuildBlobLocation(definition.Container, coverBlobPath);
            try
            {
                using var stream = await blobStorageService.DownloadBlobAsync(imageLocation);
                var bytes = stream.ToArray();
                var contentType = DetectImageContentType(bytes)
                    ?? blobReader.ContentTypeForImage(coverBlobPath);
                await TrySaveCoverImageAsync(definition, bytes, contentType);
                return new BenchmarkImageFile(bytes, contentType);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                continue;
            }
        }

        return null;
    }

    private async Task TrySaveCoverImageAsync(
        BenchmarkDefinition definition,
        byte[] bytes,
        string contentType
    )
    {
        try
        {
            using var uploadStream = new MemoryStream(bytes);
            await blobStorageService.UploadBlobAsync(
                blobReader.BuildBlobLocation(definition.Container, CoverImageBlobName),
                uploadStream,
                contentType
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Failed to persist default cover image for benchmark dataset '{DatasetId}'",
                definition.Id
            );
        }
    }

    private static async Task<byte[]> ReadBoundedImageBytesAsync(
        Stream content,
        CancellationToken cancellationToken
    )
    {
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        var totalRead = 0;

        while (true)
        {
            var bytesRead = await content.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            totalRead += bytesRead;
            if (totalRead > MaxCoverImageBytes)
            {
                throw new ArgumentException("Cover image must be 5 MB or smaller.");
            }

            memory.Write(buffer, 0, bytesRead);
        }

        if (totalRead == 0)
        {
            throw new ArgumentException("Cover image file is empty.");
        }

        return memory.ToArray();
    }

    private static string? DetectImageContentType(ReadOnlySpan<byte> bytes)
    {
        if (
            bytes.Length >= 8
            && bytes[0] == 0x89
            && bytes[1] == 0x50
            && bytes[2] == 0x4E
            && bytes[3] == 0x47
            && bytes[4] == 0x0D
            && bytes[5] == 0x0A
            && bytes[6] == 0x1A
            && bytes[7] == 0x0A
        )
        {
            return "image/png";
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (
            bytes.Length >= 12
            && bytes[0] == 0x52
            && bytes[1] == 0x49
            && bytes[2] == 0x46
            && bytes[3] == 0x46
            && bytes[8] == 0x57
            && bytes[9] == 0x45
            && bytes[10] == 0x42
            && bytes[11] == 0x50
        )
        {
            return "image/webp";
        }

        return null;
    }
}
