using System.Text.Json;
using System.Text.Json.Serialization;
using api.Controllers.Models;

namespace api.Services.BenchmarkServices.DatasetFormats;

public record CocoImageEntry(int Id, string FileName, int Width, int Height);

public sealed class BenchmarkCocoIndex
{
    public IReadOnlyDictionary<int, CocoImageEntry> Images { get; }
    public IReadOnlyDictionary<
        int,
        IReadOnlyList<BenchmarkCocoAnnotationDto>
    > AnnotationsByImage { get; }

    private BenchmarkCocoIndex(
        Dictionary<int, CocoImageEntry> images,
        Dictionary<int, IReadOnlyList<BenchmarkCocoAnnotationDto>> annotationsByImage
    )
    {
        Images = images;
        AnnotationsByImage = annotationsByImage;
    }

    public IReadOnlyList<BenchmarkCocoAnnotationDto> GetAnnotations(int imageId) =>
        AnnotationsByImage.TryGetValue(imageId, out var list) ? list : [];

    public static async Task<BenchmarkCocoIndex> ParseAsync(
        Stream stream,
        CancellationToken cancellationToken = default
    )
    {
        var raw = await JsonSerializer.DeserializeAsync<RawCocoDataset>(
            stream,
            cancellationToken: cancellationToken
        ) ?? throw new JsonException("COCO dataset JSON was null.");

        var categories = raw.Categories.ToDictionary(c => c.Id, c => c.Name);
        var images = raw.Images.ToDictionary(
            img => img.Id,
            img => new CocoImageEntry(img.Id, img.FileName, img.Width, img.Height)
        );

        var grouped = new Dictionary<int, List<BenchmarkCocoAnnotationDto>>();
        foreach (var ann in raw.Annotations)
        {
            if (!grouped.TryGetValue(ann.ImageId, out var list))
            {
                list = [];
                grouped[ann.ImageId] = list;
            }
            var categoryName = categories.GetValueOrDefault(
                ann.CategoryId,
                $"class-{ann.CategoryId}"
            );
            list.Add(
                new BenchmarkCocoAnnotationDto(
                    ann.Id,
                    ann.ImageId,
                    ann.CategoryId,
                    categoryName,
                    ann.Bbox,
                    ann.Area,
                    ann.Score,
                    ann.Segmentation?.Clone()
                )
            );
        }

        return new BenchmarkCocoIndex(
            images,
            grouped.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyList<BenchmarkCocoAnnotationDto>)kvp.Value
            )
        );
    }

    private sealed class RawCocoDataset
    {
        [JsonPropertyName("categories")]
        public List<RawCocoCategory> Categories { get; set; } = [];

        [JsonPropertyName("images")]
        public List<RawCocoImage> Images { get; set; } = [];

        [JsonPropertyName("annotations")]
        public List<RawCocoAnnotation> Annotations { get; set; } = [];
    }

    private sealed class RawCocoCategory
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
    }

    private sealed class RawCocoImage
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("file_name")]
        public string FileName { get; set; } = "";

        [JsonPropertyName("width")]
        public int Width { get; set; }

        [JsonPropertyName("height")]
        public int Height { get; set; }
    }

    private sealed class RawCocoAnnotation
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("image_id")]
        public int ImageId { get; set; }

        [JsonPropertyName("category_id")]
        public int CategoryId { get; set; }

        [JsonPropertyName("bbox")]
        public List<double>? Bbox { get; set; }

        [JsonPropertyName("area")]
        public double? Area { get; set; }

        [JsonPropertyName("score")]
        public double? Score { get; set; }

        [JsonPropertyName("segmentation")]
        public JsonElement? Segmentation { get; set; }
    }
}
