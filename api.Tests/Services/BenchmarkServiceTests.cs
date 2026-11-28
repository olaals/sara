using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using api.Configurations;
using api.Controllers.Models;
using api.Database.Models;
using api.Services;
using api.Services.BenchmarkServices;
using api.Services.BenchmarkServices.DatasetFormats;
using Api.Test.Mocks;
using Azure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Api.Test.Services;

public class BenchmarkServiceTests
{
    private static (BenchmarkService Service, FakeArgoWorkflowClient Argo) CreateService(
        IBlobStorageService? blobStorage = null
    )
    {
        var argo = new FakeArgoWorkflowClient();
        var options = Options.Create(
            new BenchmarkOptions
            {
                StorageAccount = "saradatasets",
                Definitions =
                [
                    new BenchmarkDefinition
                    {
                        Id = "anonymizer",
                        Label = "Anonymizer",
                        Description = "Evaluates face and person anonymization models.",
                        Container = "anonymizer",
                        WorkflowTemplate = "anonymizer-benchmark",
                        DefaultSplit = "val",
                        Devices = ["cpu", "gpu"],
                        Methods = ["segmentation", "bounding_box"],
                        Parameters =
                        [
                            new BenchmarkParameterDefinition
                            {
                                Name = "confidence-threshold",
                                Label = "Confidence",
                                Min = 0.0,
                                MinExclusive = true,
                                Max = 1.0,
                                Step = 0.05,
                                Placeholder = "0.3",
                                OnlyForMethods = ["bounding_box"],
                            },
                            new BenchmarkParameterDefinition
                            {
                                Name = "box-padding",
                                Label = "Box padding",
                                Min = 0.0,
                                Max = 1.0,
                                Step = 0.05,
                                Placeholder = "0.1",
                                OnlyForMethods = ["bounding_box"],
                            },
                        ],
                    },
                    new BenchmarkDefinition
                    {
                        Id = "rain-drop-detection",
                        Label = "Rain Drop Detector",
                        Description = "Evaluates raindrop detection models.",
                        Container = "rain-drop-detection",
                        WorkflowTemplate = "rain-drop-detector-benchmark",
                        DefaultSplit = "val",
                        Devices = ["cpu"],
                        Parameters =
                        [
                            new BenchmarkParameterDefinition
                            {
                                Name = "confidence-threshold",
                                Label = "Confidence",
                                Min = 0.0,
                                MinExclusive = true,
                                Max = 1.0,
                                Step = 0.05,
                                Placeholder = "0.25",
                            },
                        ],
                    },
                    new BenchmarkDefinition
                    {
                        Id = "thermal-reading",
                        Label = "Thermal Reading",
                        Description = "Evaluates thermal image alignment and temperature reading.",
                        Container = "thermal-reading",
                        WorkflowTemplate = "thermal-reading-benchmark",
                        DefaultSplit = "val",
                        Devices = ["cpu"],
                        Methods = ["phase_correlation", "orb_bf"],
                        Parameters =
                        [
                            new BenchmarkParameterDefinition
                            {
                                Name = "confidence-threshold",
                                Label = "Confidence",
                                Min = 0.0,
                                Max = 1.0,
                                Step = 0.05,
                                Placeholder = "0.5",
                            },
                            new BenchmarkParameterDefinition
                            {
                                Name = "iou-threshold",
                                Label = "Min IoU",
                                Min = 0.0,
                                MinExclusive = true,
                                Max = 1.0,
                                Step = 0.05,
                                Placeholder = "0.7",
                            },
                        ],
                    },
                ],
            }
        );
        var storage = blobStorage ?? new BlobStorageServiceMock { BlobExists = false };
        var cache = new BenchmarkIndexCache(options, NullLogger<BenchmarkIndexCache>.Instance);
        var blobReader = new BenchmarkBlobReader(
            storage,
            cache,
            options,
            NullLogger<BenchmarkBlobReader>.Instance
        );
        IBenchmarkDatasetFormatHandler[] formatHandlers =
        [
            new CocoDatasetFormatHandler(blobReader),
            new ThermalDatasetFormatHandler(blobReader),
        ];
        var coverImageService = new BenchmarkCoverImageService(
            storage,
            blobReader,
            NullLogger<BenchmarkCoverImageService>.Instance
        );
        var service = new BenchmarkService(
            argo,
            blobReader,
            coverImageService,
            formatHandlers,
            options,
            NullLogger<BenchmarkService>.Instance
        );
        return (service, argo);
    }

    [Fact]
    public async Task TriggerBenchmarkPassesBoundingBoxParametersToArgo()
    {
        var (service, argo) = CreateService();

        var job = await service.TriggerBenchmarkAsync(
            new TriggerBenchmarkRequest(
                DatasetId: "anonymizer",
                Version: "v001",
                Split: "val",
                Device: "gpu",
                RunId: "rtmdet-run",
                Method: "bounding_box",
                Parameters: new Dictionary<string, double?>
                {
                    ["confidence-threshold"] = 0.25,
                    ["box-padding"] = 0.15,
                }
            ),
            TestContext.Current.CancellationToken
        );

        Assert.Equal("bounding_box", job.Method);
        Assert.Equal("0.25", job.Parameters["confidence-threshold"]);
        Assert.Equal("0.15", job.Parameters["box-padding"]);

        var created = Assert.Single(argo.Requests).Resource;
        var parameters = created.Spec!.Arguments!.Parameters.ToDictionary(
            p => p.Name,
            p => p.Value ?? ""
        );
        Assert.Equal("bounding_box", parameters["method"]);
        Assert.Equal("0.25", parameters["confidence-threshold"]);
        Assert.Equal("0.15", parameters["box-padding"]);
    }

    [Fact]
    public async Task TriggerBenchmarkDefaultsToFirstConfiguredMethod()
    {
        var (service, argo) = CreateService();

        var job = await service.TriggerBenchmarkAsync(
            new TriggerBenchmarkRequest(
                DatasetId: "anonymizer",
                Version: "v001",
                Split: "val",
                Device: "cpu",
                RunId: null
            ),
            TestContext.Current.CancellationToken
        );

        Assert.Equal("segmentation", job.Method);
        Assert.Empty(job.Parameters);

        var parameters = Assert
            .Single(argo.Requests)
            .Resource.Spec!.Arguments!.Parameters.ToDictionary(p => p.Name, p => p.Value ?? "");
        Assert.Equal("segmentation", parameters["method"]);
        Assert.Equal("", parameters["confidence-threshold"]);
        Assert.Equal("", parameters["box-padding"]);
    }

    [Fact]
    public async Task TriggerRainDropBenchmarkSupportsConfidenceOverrideWithoutMethod()
    {
        var (service, argo) = CreateService();

        var job = await service.TriggerBenchmarkAsync(
            new TriggerBenchmarkRequest(
                DatasetId: "rain-drop-detection",
                Version: "v001",
                Split: "val",
                Device: "cpu",
                RunId: "rain-run-1",
                Parameters: new Dictionary<string, double?> { ["confidence-threshold"] = 0.4 }
            ),
            TestContext.Current.CancellationToken
        );

        Assert.Null(job.Method);
        Assert.Equal("0.4", job.Parameters["confidence-threshold"]);

        var created = Assert.Single(argo.Requests).Resource;
        Assert.Equal("rain-drop-detector-benchmark", created.Spec!.WorkflowTemplateRef!.Name);
        var parameters = created.Spec.Arguments!.Parameters.ToDictionary(
            p => p.Name,
            p => p.Value ?? ""
        );
        Assert.False(parameters.ContainsKey("method"));
        Assert.Equal("0.4", parameters["confidence-threshold"]);
        Assert.Equal("cpu", parameters["device"]);
    }

    [Theory]
    [InlineData("anonymizer", "gpu", "segmentation", "confidence-threshold", 0.3)]
    [InlineData("anonymizer", "gpu", "segmentation", "box-padding", 0.1)]
    [InlineData("anonymizer", "gpu", "bounding_box", "confidence-threshold", 0.0)]
    [InlineData("anonymizer", "gpu", "bounding_box", "confidence-threshold", 1.2)]
    [InlineData("anonymizer", "gpu", "bounding_box", "box-padding", -0.1)]
    [InlineData("anonymizer", "gpu", "bounding_box", "box-padding", 1.5)]
    [InlineData("anonymizer", "gpu", "unknown_method", null, null)]
    [InlineData("rain-drop-detection", "gpu", null, null, null)]
    [InlineData("rain-drop-detection", "cpu", "segmentation", null, null)]
    [InlineData("rain-drop-detection", "cpu", null, "confidence-threshold", 0.0)]
    [InlineData("rain-drop-detection", "cpu", null, "box-padding", 0.1)]
    public async Task TriggerBenchmarkRejectsInvalidOptions(
        string datasetId,
        string device,
        string? method,
        string? paramName,
        double? paramValue
    )
    {
        var (service, _) = CreateService();
        var parameters = paramName is not null
            ? new Dictionary<string, double?> { [paramName] = paramValue }
            : null;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.TriggerBenchmarkAsync(
                new TriggerBenchmarkRequest(
                    DatasetId: datasetId,
                    Version: "v001",
                    Split: "val",
                    Device: device,
                    RunId: null,
                    Method: method,
                    Parameters: parameters
                ),
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public async Task GetJobsReadsMethodAndExtraParametersFromArgoWorkflow()
    {
        var (service, argo) = CreateService();
        argo.ListedWorkflows.Add(
            new ArgoWorkflowResource
            {
                Metadata = new ArgoObjectMetadata
                {
                    Name = "anonymizer-benchmark-abc",
                    Labels = new Dictionary<string, string>
                    {
                        [BenchmarkService.DatasetLabel] = "anonymizer",
                    },
                },
                Spec = new ArgoWorkflowSpec
                {
                    Arguments = new ArgoArguments
                    {
                        Parameters =
                        [
                            new ArgoParameter { Name = "version", Value = "v001" },
                            new ArgoParameter { Name = "split", Value = "val" },
                            new ArgoParameter { Name = "device", Value = "gpu" },
                            new ArgoParameter { Name = "method", Value = "bounding_box" },
                            new ArgoParameter { Name = "confidence-threshold", Value = "0.3" },
                            new ArgoParameter { Name = "box-padding", Value = "0.1" },
                        ],
                    },
                },
            }
        );

        var jobs = await service.GetJobsAsync(TestContext.Current.CancellationToken);

        var job = Assert.Single(jobs);
        Assert.Equal("bounding_box", job.Method);
        Assert.Equal("0.3", job.Parameters["confidence-threshold"]);
        Assert.Equal("0.1", job.Parameters["box-padding"]);
    }

    [Fact]
    public async Task CoverImageLifecycleSelectsDefaultSavesOverridesAndResets()
    {
        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01 };
        var jpegBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
        const string cocoJson = """
            {
              "categories": [{ "id": 1, "name": "person" }],
              "images": [
                { "id": 1, "file_name": "empty.png", "width": 100, "height": 100 },
                { "id": 2, "file_name": "sample.png", "width": 100, "height": 100 }
              ],
              "annotations": [
                { "id": 10, "image_id": 2, "category_id": 1, "bbox": [1, 2, 3, 4] }
              ]
            }
            """;

        var storage = new InMemoryBlobStorage();
        storage.Put(
            "anonymizer",
            "schema.json",
            Encoding.UTF8.GetBytes("""{"format":"coco","schema_version":1}""")
        );
        storage.Put(
            "rain-drop-detection",
            "schema.json",
            Encoding.UTF8.GetBytes("""{"format":"coco","schema_version":1}""")
        );
        storage.Put(
            "thermal-reading",
            "schema.json",
            Encoding.UTF8.GetBytes("""{"format":"thermal","schema_version":1}""")
        );
        storage.Put("anonymizer", "v001/dataset/manifest.json", Encoding.UTF8.GetBytes("{}"));
        storage.Put(
            "anonymizer",
            "v001/dataset/annotations/instances_val.json",
            Encoding.UTF8.GetBytes(cocoJson)
        );
        storage.Put("anonymizer", "v001/dataset/images/val/sample.png", pngBytes);

        var (service, _) = CreateService(storage);
        var ct = TestContext.Current.CancellationToken;

        var datasets = await service.GetDatasetsAsync(ct);
        var anonDataset = datasets.Single(d => d.Id == "anonymizer");
        Assert.Equal("Evaluates face and person anonymization models.", anonDataset.Description);
        Assert.Equal("coco", anonDataset.Format);
        Assert.Equal("thermal", datasets.Single(d => d.Id == "thermal-reading").Format);

        var defaultCover = await service.GetCoverImageAsync("anonymizer", ct);
        Assert.Equal("image/png", defaultCover.ContentType);
        Assert.Equal(pngBytes, defaultCover.Content);
        Assert.True(storage.Contains("anonymizer", "cover-image"));

        using (var uploadStream = new MemoryStream(jpegBytes))
        {
            await service.UploadCoverImageAsync("anonymizer", uploadStream, ct);
        }

        var uploadedCover = await service.GetCoverImageAsync("anonymizer", ct);
        Assert.Equal("image/jpeg", uploadedCover.ContentType);
        Assert.Equal(jpegBytes, uploadedCover.Content);

        await service.ResetCoverImageAsync("anonymizer", ct);
        Assert.False(storage.Contains("anonymizer", "cover-image"));
    }

    [Fact]
    public async Task CoverImageRejectsInvalidUploadAndReturnsNotFoundWithoutDataset()
    {
        var storage = new InMemoryBlobStorage();
        storage.Put(
            "rain-drop-detection",
            "schema.json",
            Encoding.UTF8.GetBytes("""{"format":"coco","schema_version":1}""")
        );
        var (service, _) = CreateService(storage);
        var ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.GetCoverImageAsync("rain-drop-detection", ct)
        );

        using var invalidStream = new MemoryStream(Encoding.UTF8.GetBytes("not an image"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UploadCoverImageAsync("rain-drop-detection", invalidStream, ct)
        );
    }

    [Fact]
    public async Task MissingOrInvalidSchemaJsonFailsWithoutFallback()
    {
        var storage = new InMemoryBlobStorage();
        var (service, _) = CreateService(storage);
        var ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetDatasetsAsync(ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetCoverImageAsync("thermal-reading", ct)
        );

        storage.Put(
            "thermal-reading",
            "schema.json",
            Encoding.UTF8.GetBytes("""{"format":"unknown","schema_version":1}""")
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetCoverImageAsync("thermal-reading", ct)
        );
    }

    private sealed class InMemoryBlobStorage : IBlobStorageService
    {
        private readonly Dictionary<string, byte[]> _blobs = new(StringComparer.Ordinal);

        public void Put(string container, string blobName, byte[] content) =>
            _blobs[$"{container}/{blobName}"] = content;

        public bool Contains(string container, string blobName) =>
            _blobs.ContainsKey($"{container}/{blobName}");

        public Task<MemoryStream> DownloadBlobAsync(BlobStorageLocation location)
        {
            if (!_blobs.TryGetValue($"{location.BlobContainer}/{location.BlobName}", out var data))
            {
                throw new RequestFailedException(404, "Not found");
            }

            return Task.FromResult(new MemoryStream(data));
        }

        public Task UploadBlobAsync(
            BlobStorageLocation destination,
            Stream content,
            string contentType
        )
        {
            using var memory = new MemoryStream();
            content.CopyTo(memory);
            _blobs[$"{destination.BlobContainer}/{destination.BlobName}"] = memory.ToArray();
            return Task.CompletedTask;
        }

        public Task DeleteBlobAsync(
            BlobStorageLocation location,
            CancellationToken cancellationToken = default
        )
        {
            _blobs.Remove($"{location.BlobContainer}/{location.BlobName}");
            return Task.CompletedTask;
        }

        public async Task CopyBlobAsync(BlobStorageLocation source, BlobStorageLocation destination)
        {
            using var stream = await DownloadBlobAsync(source);
            await UploadBlobAsync(destination, stream, "application/octet-stream");
        }

        public Task<Uri?> TryCreateReadSasUriAsync(
            BlobStorageLocation location,
            string? contentDisposition = null
        ) => Task.FromResult<Uri?>(null);

        public Task<bool> ExistsAsync(BlobStorageLocation location) =>
            Task.FromResult(_blobs.ContainsKey($"{location.BlobContainer}/{location.BlobName}"));

        public Task<IReadOnlyList<string>> ListChildPrefixesAsync(
            string storageAccount,
            string containerName,
            string prefix = "",
            CancellationToken cancellationToken = default
        )
        {
            var containerPrefix = $"{containerName}/{prefix}";
            var children = _blobs
                .Keys.Where(k => k.StartsWith(containerPrefix, StringComparison.Ordinal))
                .Select(k => k[containerPrefix.Length..])
                .Where(rest => rest.Contains('/'))
                .Select(rest => rest[..rest.IndexOf('/')])
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return Task.FromResult<IReadOnlyList<string>>(children);
        }
    }
}
