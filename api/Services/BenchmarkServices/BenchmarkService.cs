using System.Text.Json;
using api.Configurations;
using api.Controllers.Models;
using api.Services.BenchmarkServices.DatasetFormats;
using api.Utilities;
using Azure;
using Microsoft.Extensions.Options;

namespace api.Services.BenchmarkServices;

public interface IBenchmarkService
{
    Task<IReadOnlyList<BenchmarkDatasetDto>> GetDatasetsAsync(
        CancellationToken cancellationToken = default
    );
    Task<IReadOnlyList<BenchmarkJobDto>> GetJobsAsync(
        CancellationToken cancellationToken = default
    );
    Task<BenchmarkJobDto> TriggerBenchmarkAsync(
        TriggerBenchmarkRequest request,
        CancellationToken cancellationToken = default
    );
    Task<IReadOnlyList<BenchmarkResultDto>> GetResultsAsync(
        string datasetId,
        string version,
        string split,
        CancellationToken cancellationToken = default
    );
    Task<BenchmarkRunDetailDto> GetRunDetailAsync(
        string datasetId,
        string version,
        string split,
        string runId,
        CancellationToken cancellationToken = default
    );
    Task<BenchmarkImageComparisonDto> GetImageComparisonAsync(
        string datasetId,
        string version,
        string split,
        string runId,
        int imageId,
        CancellationToken cancellationToken = default
    );
    Task<BenchmarkImageFile> GetDatasetImageAsync(
        string datasetId,
        string version,
        string split,
        int imageId,
        CancellationToken cancellationToken = default
    );
    Task<BenchmarkImageFile> GetDatasetReferenceImageAsync(
        string datasetId,
        string version,
        string split,
        int imageId,
        CancellationToken cancellationToken = default
    );
    Task<BenchmarkImageFile> GetCoverImageAsync(
        string datasetId,
        CancellationToken cancellationToken = default
    );
    Task UploadCoverImageAsync(
        string datasetId,
        Stream content,
        CancellationToken cancellationToken = default
    );
    Task ResetCoverImageAsync(string datasetId, CancellationToken cancellationToken = default);
}

public sealed class BenchmarkService(
    IArgoWorkflowClient argoWorkflowClient,
    IBenchmarkBlobReader blobReader,
    IBenchmarkCoverImageService coverImageService,
    IEnumerable<IBenchmarkDatasetFormatHandler> formatHandlers,
    IOptions<BenchmarkOptions> options,
    ILogger<BenchmarkService> logger
) : IBenchmarkService
{
    public const string BenchmarkLabel = "sara.equinor.com/benchmark";
    public const string DatasetLabel = "sara.equinor.com/benchmark-dataset";
    private const string BenchmarkSelector = BenchmarkLabel + "=true";
    private const int MaxConcurrentBlobReads = 8;

    private readonly BenchmarkOptions _options = options.Value;
    private readonly IReadOnlyDictionary<string, IBenchmarkDatasetFormatHandler> _formatHandlers =
        formatHandlers.ToDictionary(handler => handler.Format, StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<BenchmarkDatasetDto>> GetDatasetsAsync(
        CancellationToken cancellationToken = default
    )
    {
        var tasks = _options.Definitions.Select(def =>
            BuildDatasetDtoAsync(def, cancellationToken)
        );
        return await Task.WhenAll(tasks);
    }

    public async Task<IReadOnlyList<BenchmarkJobDto>> GetJobsAsync(
        CancellationToken cancellationToken = default
    )
    {
        var workflows = await argoWorkflowClient.ListWorkflowsByLabelSelectorAsync(
            BenchmarkSelector,
            cancellationToken
        );

        return workflows
            .Select(BenchmarkWorkflowMapper.MapWorkflowToJobDto)
            .OrderByDescending(j => j.CreatedAt ?? j.StartedAt ?? DateTime.MinValue)
            .ToList();
    }

    public async Task<BenchmarkJobDto> TriggerBenchmarkAsync(
        TriggerBenchmarkRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var definition = ResolveDefinition(request.DatasetId);
        BenchmarkWorkflowMapper.ValidateSegment(request.Version, nameof(request.Version));
        BenchmarkWorkflowMapper.ValidateSegment(request.Split, nameof(request.Split));

        var device = request.Device.Trim().ToLowerInvariant();
        if (
            !BenchmarkWorkflowMapper
                .ResolveDevices(definition)
                .Contains(device, StringComparer.OrdinalIgnoreCase)
        )
        {
            throw new ArgumentException(
                $"Unsupported device '{request.Device}' for dataset '{definition.Id}'."
            );
        }

        var (method, resolvedParameters) = BenchmarkWorkflowMapper.ValidateRunOptions(
            definition,
            request
        );

        var runId = string.IsNullOrWhiteSpace(request.RunId) ? "" : request.RunId.Trim();
        if (!string.IsNullOrEmpty(runId))
        {
            BenchmarkWorkflowMapper.ValidateSegment(runId, nameof(request.RunId));
            await EnsureRunIdIsAvailableAsync(
                definition,
                request.Version,
                request.Split,
                runId,
                cancellationToken
            );
        }

        var workflow = BenchmarkWorkflowMapper.BuildWorkflowResource(
            definition,
            blobReader.BuildContainerUrl(definition.Container),
            request.Version,
            request.Split,
            runId,
            device,
            method,
            resolvedParameters
        );

        var created = await argoWorkflowClient.CreateWorkflowAsync(workflow, cancellationToken);
        return new BenchmarkJobDto(
            WorkflowName: created.Name,
            WorkflowUid: created.Uid,
            DatasetId: definition.Id,
            Version: request.Version,
            Split: request.Split,
            Device: device,
            Method: method,
            Parameters: resolvedParameters,
            RunId: string.IsNullOrEmpty(runId) ? created.Name : runId,
            Phase: "Pending",
            Message: null,
            CreatedAt: DateTime.UtcNow,
            StartedAt: null,
            FinishedAt: null
        );
    }

    public async Task<IReadOnlyList<BenchmarkResultDto>> GetResultsAsync(
        string datasetId,
        string version,
        string split,
        CancellationToken cancellationToken = default
    )
    {
        var definition = ResolveDefinition(datasetId);
        BenchmarkWorkflowMapper.ValidateSegment(version, nameof(version));
        BenchmarkWorkflowMapper.ValidateSegment(split, nameof(split));

        var resultsPrefix = $"{version}/benchmark-results/{split}/";
        var runIds = await blobReader.ListChildPrefixesAsync(
            definition.Container,
            resultsPrefix,
            cancellationToken
        );

        using var throttle = new SemaphoreSlim(MaxConcurrentBlobReads);
        var tasks = runIds.Select(runId =>
            ReadRunResultAsync(
                definition.Container,
                resultsPrefix,
                runId,
                throttle,
                cancellationToken
            )
        );
        var results = await Task.WhenAll(tasks);

        return results.OrderByDescending(r => r.RunId, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<BenchmarkRunDetailDto> GetRunDetailAsync(
        string datasetId,
        string version,
        string split,
        string runId,
        CancellationToken cancellationToken = default
    )
    {
        var definition = ResolveDefinition(datasetId);
        BenchmarkWorkflowMapper.ValidateSegment(version, nameof(version));
        BenchmarkWorkflowMapper.ValidateSegment(split, nameof(split));
        BenchmarkWorkflowMapper.ValidateSegment(runId, nameof(runId));

        var prefix = $"{version}/benchmark-results/{split}/{runId}";
        BenchmarkRunMetadataDto? run;
        BenchmarkMetricsDto? metrics;
        try
        {
            run = await blobReader.DownloadJsonAsync<BenchmarkRunMetadataDto>(
                definition.Container,
                $"{prefix}/run.json",
                cancellationToken
            );
            metrics = await blobReader.DownloadJsonAsync<BenchmarkMetricsDto>(
                definition.Container,
                $"{prefix}/metrics.json",
                cancellationToken
            );
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new KeyNotFoundException($"Benchmark run '{runId}' was not found.");
        }
        catch (Exception ex) when (ex is RequestFailedException or JsonException)
        {
            return new BenchmarkRunDetailDto(runId, null, null, null, false, ex.Message);
        }

        var formatHandler = await ResolveFormatHandlerAsync(
            definition.Container,
            cancellationToken
        );
        var perImage = await blobReader.TryGetPerImageAsync(
            definition.Container,
            prefix,
            cancellationToken
        );
        var hasPredictions = await blobReader.ExistsAsync(
            definition.Container,
            $"{prefix}/{formatHandler.PredictionsRelativePath}"
        );

        return new BenchmarkRunDetailDto(runId, run, metrics, perImage, hasPredictions, null);
    }

    public async Task<BenchmarkImageComparisonDto> GetImageComparisonAsync(
        string datasetId,
        string version,
        string split,
        string runId,
        int imageId,
        CancellationToken cancellationToken = default
    )
    {
        var definition = ResolveDefinition(datasetId);
        BenchmarkWorkflowMapper.ValidateSegment(version, nameof(version));
        BenchmarkWorkflowMapper.ValidateSegment(split, nameof(split));
        BenchmarkWorkflowMapper.ValidateSegment(runId, nameof(runId));

        var formatHandler = await ResolveFormatHandlerAsync(
            definition.Container,
            cancellationToken
        );
        return await formatHandler.GetImageComparisonAsync(
            definition.Container,
            version,
            split,
            runId,
            imageId,
            cancellationToken
        );
    }

    public async Task<BenchmarkImageFile> GetDatasetImageAsync(
        string datasetId,
        string version,
        string split,
        int imageId,
        CancellationToken cancellationToken = default
    )
    {
        var definition = ResolveDefinition(datasetId);
        BenchmarkWorkflowMapper.ValidateSegment(version, nameof(version));
        BenchmarkWorkflowMapper.ValidateSegment(split, nameof(split));

        var formatHandler = await ResolveFormatHandlerAsync(
            definition.Container,
            cancellationToken
        );
        return await formatHandler.GetDatasetImageAsync(
            definition.Container,
            version,
            split,
            imageId,
            cancellationToken
        );
    }

    public async Task<BenchmarkImageFile> GetDatasetReferenceImageAsync(
        string datasetId,
        string version,
        string split,
        int imageId,
        CancellationToken cancellationToken = default
    )
    {
        var definition = ResolveDefinition(datasetId);
        BenchmarkWorkflowMapper.ValidateSegment(version, nameof(version));
        BenchmarkWorkflowMapper.ValidateSegment(split, nameof(split));

        var formatHandler = await ResolveFormatHandlerAsync(
            definition.Container,
            cancellationToken
        );
        return await formatHandler.GetDatasetReferenceImageAsync(
            definition.Container,
            version,
            split,
            imageId,
            cancellationToken
        );
    }

    public async Task<BenchmarkImageFile> GetCoverImageAsync(
        string datasetId,
        CancellationToken cancellationToken = default
    )
    {
        var definition = ResolveDefinition(datasetId);
        var formatHandler = await ResolveFormatHandlerAsync(
            definition.Container,
            cancellationToken
        );
        return await coverImageService.GetCoverImageAsync(
            definition,
            formatHandler,
            cancellationToken
        );
    }

    public Task UploadCoverImageAsync(
        string datasetId,
        Stream content,
        CancellationToken cancellationToken = default
    ) =>
        coverImageService.UploadCoverImageAsync(
            ResolveDefinition(datasetId),
            content,
            cancellationToken
        );

    public Task ResetCoverImageAsync(
        string datasetId,
        CancellationToken cancellationToken = default
    ) => coverImageService.ResetCoverImageAsync(ResolveDefinition(datasetId), cancellationToken);

    private async Task<IBenchmarkDatasetFormatHandler> ResolveFormatHandlerAsync(
        string container,
        CancellationToken cancellationToken
    )
    {
        var schema = await blobReader.GetRequiredDatasetSchemaAsync(container, cancellationToken);
        if (!_formatHandlers.TryGetValue(schema.Format, out var handler))
        {
            throw new InvalidOperationException(
                $"No dataset format handler registered for format '{schema.Format}'."
            );
        }

        return handler;
    }

    private async Task<BenchmarkDatasetDto> BuildDatasetDtoAsync(
        BenchmarkDefinition definition,
        CancellationToken cancellationToken
    )
    {
        var schema = await blobReader.GetRequiredDatasetSchemaAsync(
            definition.Container,
            cancellationToken
        );
        var versions = await blobReader.ListDatasetVersionsAsync(definition, cancellationToken);

        var parameters = definition
            .Parameters.Select(p => new BenchmarkParameterDefinitionDto(
                p.Name,
                p.Label,
                p.Min,
                p.MinExclusive,
                p.Max,
                p.Step,
                p.Placeholder,
                p.OnlyForMethods
            ))
            .ToList();

        return new BenchmarkDatasetDto(
            definition.Id,
            definition.Label,
            definition.Description,
            definition.Container,
            schema.Format,
            definition.DefaultSplit,
            BenchmarkWorkflowMapper.ResolveDevices(definition),
            definition.Methods,
            parameters,
            versions
        );
    }

    private async Task EnsureRunIdIsAvailableAsync(
        BenchmarkDefinition definition,
        string version,
        string split,
        string runId,
        CancellationToken cancellationToken
    )
    {
        var prefix = $"{version}/benchmark-results/{split}/{runId}";
        var runExists = await blobReader.ExistsAsync(definition.Container, $"{prefix}/run.json");
        if (runExists)
        {
            throw new ArgumentException($"Benchmark run '{runId}' already exists.");
        }

        var workflows = await argoWorkflowClient.ListWorkflowsByLabelSelectorAsync(
            BenchmarkSelector,
            cancellationToken
        );
        var hasActiveRun = workflows
            .Select(BenchmarkWorkflowMapper.MapWorkflowToJobDto)
            .Any(job =>
                BenchmarkWorkflowMapper.IsActiveWorkflowPhase(job.Phase)
                && string.Equals(job.DatasetId, definition.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(job.Version, version, StringComparison.OrdinalIgnoreCase)
                && string.Equals(job.Split, split, StringComparison.OrdinalIgnoreCase)
                && string.Equals(job.RunId, runId, StringComparison.OrdinalIgnoreCase)
            );
        if (hasActiveRun)
        {
            throw new ArgumentException($"Benchmark run '{runId}' is already in progress.");
        }
    }

    private async Task<BenchmarkResultDto> ReadRunResultAsync(
        string container,
        string resultsPrefix,
        string runId,
        SemaphoreSlim throttle,
        CancellationToken cancellationToken
    )
    {
        await throttle.WaitAsync(cancellationToken);
        try
        {
            var run = await blobReader.DownloadJsonAsync<BenchmarkRunMetadataDto>(
                container,
                $"{resultsPrefix}{runId}/run.json",
                cancellationToken
            );
            var metrics = await blobReader.DownloadJsonAsync<BenchmarkMetricsDto>(
                container,
                $"{resultsPrefix}{runId}/metrics.json",
                cancellationToken
            );
            return new BenchmarkResultDto(runId, run, metrics, null);
        }
        catch (Exception ex) when (ex is RequestFailedException or JsonException)
        {
            logger.LogWarning(
                ex,
                "Failed to load benchmark result '{RunId}' from '{Container}/{Prefix}'",
                runId,
                container,
                Sanitize.SanitizeUserInput(resultsPrefix)
            );
            return new BenchmarkResultDto(runId, null, null, ex.Message);
        }
        finally
        {
            throttle.Release();
        }
    }

    private BenchmarkDefinition ResolveDefinition(string datasetId) =>
        _options.Definitions.FirstOrDefault(d =>
            string.Equals(d.Id, datasetId, StringComparison.OrdinalIgnoreCase)
        ) ?? throw new ArgumentException($"Unknown benchmark dataset '{datasetId}'.");
}
