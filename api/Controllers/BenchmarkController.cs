using api.Controllers.Models;
using api.Services;
using api.Services.BenchmarkServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers;

[ApiController]
[Route("benchmark")]
[Authorize(Policy = InstallationAccess.AdminPolicy)]
public class BenchmarkController(IBenchmarkService benchmarkService) : ControllerBase
{
    private const string DatasetImageCacheControl = "private, max-age=600";
    private const string CoverImageCacheControl = "private, no-store";
    private const long MaxCoverImageRequestBytes = 6 * 1024 * 1024;

    [HttpGet("datasets")]
    [ProducesResponseType(typeof(IReadOnlyList<BenchmarkDatasetDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<BenchmarkDatasetDto>>> GetDatasets(
        CancellationToken cancellationToken
    )
    {
        try
        {
            return Ok(await benchmarkService.GetDatasetsAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    [HttpGet("datasets/{datasetId}/cover-image")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCoverImage(
        [FromRoute] string datasetId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var image = await benchmarkService.GetCoverImageAsync(datasetId, cancellationToken);
            Response.Headers.CacheControl = CoverImageCacheControl;
            return File(image.Content, image.ContentType);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    [HttpPut("datasets/{datasetId}/cover-image")]
    [RequestSizeLimit(MaxCoverImageRequestBytes)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadCoverImage(
        [FromRoute] string datasetId,
        IFormFile? file,
        CancellationToken cancellationToken
    )
    {
        if (file is null)
        {
            return BadRequest("No image file was provided.");
        }

        try
        {
            await using var stream = file.OpenReadStream();
            await benchmarkService.UploadCoverImageAsync(datasetId, stream, cancellationToken);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("datasets/{datasetId}/cover-image")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetCoverImage(
        [FromRoute] string datasetId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await benchmarkService.ResetCoverImageAsync(datasetId, cancellationToken);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("jobs")]
    [ProducesResponseType(typeof(IReadOnlyList<BenchmarkJobDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<BenchmarkJobDto>>> GetJobs(
        CancellationToken cancellationToken
    ) => Ok(await benchmarkService.GetJobsAsync(cancellationToken));

    [HttpPost("runs")]
    [ProducesResponseType(typeof(BenchmarkJobDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BenchmarkJobDto>> TriggerRun(
        [FromBody] TriggerBenchmarkRequest request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var job = await benchmarkService.TriggerBenchmarkAsync(request, cancellationToken);
            return Accepted(job);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("results")]
    [ProducesResponseType(typeof(IReadOnlyList<BenchmarkResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<BenchmarkResultDto>>> GetResults(
        [FromQuery] string datasetId,
        [FromQuery] string version,
        [FromQuery] string split,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return Ok(
                await benchmarkService.GetResultsAsync(datasetId, version, split, cancellationToken)
            );
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("results/{runId}")]
    [ProducesResponseType(typeof(BenchmarkRunDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BenchmarkRunDetailDto>> GetRunDetail(
        [FromRoute] string runId,
        [FromQuery] string datasetId,
        [FromQuery] string version,
        [FromQuery] string split,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return Ok(
                await benchmarkService.GetRunDetailAsync(
                    datasetId,
                    version,
                    split,
                    runId,
                    cancellationToken
                )
            );
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    [HttpGet("results/{runId}/images/{imageId:int}")]
    [ProducesResponseType(typeof(BenchmarkImageComparisonDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BenchmarkImageComparisonDto>> GetImageComparison(
        [FromRoute] string runId,
        [FromRoute] int imageId,
        [FromQuery] string datasetId,
        [FromQuery] string version,
        [FromQuery] string split,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return Ok(
                await benchmarkService.GetImageComparisonAsync(
                    datasetId,
                    version,
                    split,
                    runId,
                    imageId,
                    cancellationToken
                )
            );
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    [HttpGet("results/{runId}/images/{imageId:int}/image")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDatasetImage(
        [FromRoute] string runId,
        [FromRoute] int imageId,
        [FromQuery] string datasetId,
        [FromQuery] string version,
        [FromQuery] string split,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var image = await benchmarkService.GetDatasetImageAsync(
                datasetId,
                version,
                split,
                imageId,
                cancellationToken
            );
            Response.Headers.CacheControl = DatasetImageCacheControl;
            return File(image.Content, image.ContentType);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    [HttpGet("results/{runId}/images/{imageId:int}/reference-image")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDatasetReferenceImage(
        [FromRoute] string runId,
        [FromRoute] int imageId,
        [FromQuery] string datasetId,
        [FromQuery] string version,
        [FromQuery] string split,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var image = await benchmarkService.GetDatasetReferenceImageAsync(
                datasetId,
                version,
                split,
                imageId,
                cancellationToken
            );
            Response.Headers.CacheControl = DatasetImageCacheControl;
            return File(image.Content, image.ContentType);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }
}
