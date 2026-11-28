using System.Text.Json;
using api.Configurations;
using api.Controllers.Models;
using api.Database.Models;
using api.Services.BenchmarkServices.DatasetFormats;
using Azure;
using Microsoft.Extensions.Options;

namespace api.Services.BenchmarkServices;

public record BenchmarkImageFile(byte[] Content, string ContentType);

public interface IBenchmarkBlobReader
{
    BlobStorageLocation BuildBlobLocation(string container, string blobName);
    string BuildContainerUrl(string container);
    Task<bool> ExistsAsync(string container, string blobName);
    Task<IReadOnlyList<string>> ListChildPrefixesAsync(
        string container,
        string prefix,
        CancellationToken cancellationToken = default
    );
    Task<List<string>> ListDatasetVersionsAsync(
        BenchmarkDefinition definition,
        CancellationToken cancellationToken = default
    );
    Task<BenchmarkDatasetSchema> GetRequiredDatasetSchemaAsync(
        string container,
        CancellationToken cancellationToken = default
    );
    Task<T?> DownloadJsonAsync<T>(
        string container,
        string blobName,
        CancellationToken cancellationToken = default
    );
    Task<BenchmarkPerImageTableDto?> TryGetPerImageAsync(
        string container,
        string runPrefix,
        CancellationToken cancellationToken = default
    );
    Task<T?> GetCachedIndexAsync<T>(
        string namespaceKey,
        string container,
        string blobName,
        Func<Stream, CancellationToken, Task<T>> parser,
        CancellationToken cancellationToken = default
    )
        where T : class;
    Task<BenchmarkImageFile> DownloadDatasetImageFileAsync(
        string container,
        string blobName,
        string displayFileName
    );
    void ValidateRelativeImagePath(string fileName);
    string ContentTypeForImage(string fileName);
}

public sealed class BenchmarkBlobReader(
    IBlobStorageService blobStorageService,
    IBenchmarkIndexCache indexCache,
    IOptions<BenchmarkOptions> options,
    ILogger<BenchmarkBlobReader> logger
) : IBenchmarkBlobReader
{
    private const long ParsedJsonMemoryMultiplier = 4;

    private readonly BenchmarkOptions _options = options.Value;

    public BlobStorageLocation BuildBlobLocation(string container, string blobName) =>
        new()
        {
            StorageAccount = _options.StorageAccount,
            BlobContainer = container,
            BlobName = blobName,
        };

    public string BuildContainerUrl(string container) =>
        $"https://{_options.StorageAccount}.blob.core.windows.net/{container}";

    public Task<bool> ExistsAsync(string container, string blobName) =>
        blobStorageService.ExistsAsync(BuildBlobLocation(container, blobName));

    public Task<IReadOnlyList<string>> ListChildPrefixesAsync(
        string container,
        string prefix,
        CancellationToken cancellationToken = default
    ) =>
        blobStorageService.ListChildPrefixesAsync(
            _options.StorageAccount,
            container,
            prefix,
            cancellationToken
        );

    public async Task<List<string>> ListDatasetVersionsAsync(
        BenchmarkDefinition definition,
        CancellationToken cancellationToken = default
    )
    {
        var versions = new List<string>();
        try
        {
            var prefixes = await ListChildPrefixesAsync(
                definition.Container,
                "",
                cancellationToken
            );

            var checks = await Task.WhenAll(
                prefixes.Select(async prefix =>
                {
                    var hasManifest = await ExistsAsync(
                        definition.Container,
                        $"{prefix}/dataset/manifest.json"
                    );
                    return (prefix, hasManifest);
                })
            );

            versions.AddRange(
                checks
                    .Where(c => c.hasManifest)
                    .Select(c => c.prefix)
                    .OrderByDescending(v => v, StringComparer.OrdinalIgnoreCase)
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Failed to list versions for benchmark dataset '{DatasetId}' in container '{Container}'",
                definition.Id,
                definition.Container
            );
        }

        return versions;
    }

    public async Task<BenchmarkDatasetSchema> GetRequiredDatasetSchemaAsync(
        string container,
        CancellationToken cancellationToken = default
    )
    {
        var cacheKey =
            $"benchmark:schema:{_options.StorageAccount}/{container}/{BenchmarkDatasetSchema.BlobName}";
        var schema = await indexCache.GetOrLoadAsync<BenchmarkDatasetSchema>(
            cacheKey,
            async ct =>
            {
                try
                {
                    using var stream = await blobStorageService.DownloadBlobAsync(
                        BuildBlobLocation(container, BenchmarkDatasetSchema.BlobName)
                    );
                    var rawBytes = stream.Length;
                    var parsed = await BenchmarkDatasetSchema.ParseAsync(stream, container, ct);
                    return new CachedBenchmarkPayload<BenchmarkDatasetSchema>(
                        parsed,
                        EstimateParsedJsonBytes(rawBytes)
                    );
                }
                catch (RequestFailedException ex) when (ex.Status == 404)
                {
                    throw new InvalidOperationException(
                        $"Missing required '{BenchmarkDatasetSchema.BlobName}' in benchmark container '{container}'.",
                        ex
                    );
                }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException(
                        $"Invalid '{BenchmarkDatasetSchema.BlobName}' in benchmark container '{container}': {ex.Message}",
                        ex
                    );
                }
            },
            cancellationToken
        );

        return schema
            ?? throw new InvalidOperationException(
                $"Missing required '{BenchmarkDatasetSchema.BlobName}' in benchmark container '{container}'."
            );
    }

    public async Task<T?> DownloadJsonAsync<T>(
        string container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        var (value, _) = await DownloadJsonWithSizeAsync<T>(container, blobName, cancellationToken);
        return value;
    }

    public Task<BenchmarkPerImageTableDto?> TryGetPerImageAsync(
        string container,
        string runPrefix,
        CancellationToken cancellationToken = default
    )
    {
        var blobName = $"{runPrefix}/review/per_image.json";
        var cacheKey = $"benchmark:per-image:{_options.StorageAccount}/{container}/{blobName}";
        return indexCache.GetOrLoadAsync<BenchmarkPerImageTableDto>(
            cacheKey,
            async ct =>
            {
                try
                {
                    var (table, rawBytes) =
                        await DownloadJsonWithSizeAsync<BenchmarkPerImageTableDto>(
                            container,
                            blobName,
                            ct
                        );
                    return new CachedBenchmarkPayload<BenchmarkPerImageTableDto>(
                        table,
                        EstimateParsedJsonBytes(rawBytes)
                    );
                }
                catch (RequestFailedException ex) when (ex.Status == 404)
                {
                    return new CachedBenchmarkPayload<BenchmarkPerImageTableDto>(null, 0);
                }
            },
            cancellationToken
        );
    }

    public Task<T?> GetCachedIndexAsync<T>(
        string namespaceKey,
        string container,
        string blobName,
        Func<Stream, CancellationToken, Task<T>> parser,
        CancellationToken cancellationToken = default
    )
        where T : class
    {
        var cacheKey = $"benchmark:{namespaceKey}:{_options.StorageAccount}/{container}/{blobName}";
        return indexCache.GetOrLoadAsync<T>(
            cacheKey,
            async ct =>
            {
                try
                {
                    using var stream = await blobStorageService.DownloadBlobAsync(
                        BuildBlobLocation(container, blobName)
                    );
                    var rawBytes = stream.Length;
                    var index = await parser(stream, ct);
                    return new CachedBenchmarkPayload<T>(index, EstimateParsedJsonBytes(rawBytes));
                }
                catch (RequestFailedException ex) when (ex.Status == 404)
                {
                    return new CachedBenchmarkPayload<T>(null, 0);
                }
            },
            cancellationToken
        );
    }

    public async Task<BenchmarkImageFile> DownloadDatasetImageFileAsync(
        string container,
        string blobName,
        string displayFileName
    )
    {
        var location = BuildBlobLocation(container, blobName);
        try
        {
            using var stream = await blobStorageService.DownloadBlobAsync(location);
            return new BenchmarkImageFile(stream.ToArray(), ContentTypeForImage(displayFileName));
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new KeyNotFoundException($"Image blob '{displayFileName}' was not found.");
        }
    }

    public void ValidateRelativeImagePath(string fileName)
    {
        if (
            string.IsNullOrWhiteSpace(fileName)
            || fileName.StartsWith('/')
            || fileName.Contains('\\')
            || fileName.Split('/').Any(part => part is "" or "." or "..")
        )
        {
            throw new ArgumentException($"Invalid dataset image path '{fileName}'.");
        }
    }

    public string ContentTypeForImage(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "application/octet-stream",
        };

    private async Task<(T? Value, long RawBytes)> DownloadJsonWithSizeAsync<T>(
        string container,
        string blobName,
        CancellationToken cancellationToken
    )
    {
        var location = BuildBlobLocation(container, blobName);
        using var stream = await blobStorageService.DownloadBlobAsync(location);
        var rawBytes = stream.Length;
        var value = await JsonSerializer.DeserializeAsync<T>(
            stream,
            cancellationToken: cancellationToken
        );
        return (value, rawBytes);
    }

    private static long EstimateParsedJsonBytes(long rawBytes)
    {
        if (rawBytes <= 0)
        {
            return 1;
        }

        return rawBytes > long.MaxValue / ParsedJsonMemoryMultiplier
            ? long.MaxValue
            : rawBytes * ParsedJsonMemoryMultiplier;
    }
}
