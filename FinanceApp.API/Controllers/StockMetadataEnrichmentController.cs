using FinanceApp.API.Models;
using FinanceApp.API.Services;
using FinanceApp.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FinanceApp.API.Controllers;

[ApiController]
[Route("api/stocks/metadata-enrichment")]
[Authorize]
public sealed class StockMetadataEnrichmentController : ControllerBase
{
    private readonly IStockMetadataEnrichmentService _service;

    public StockMetadataEnrichmentController(IStockMetadataEnrichmentService service)
    {
        _service = service;
    }

    [HttpPost("jobs")]
    public async Task<IActionResult> CreateJob(CreateStockMetadataEnrichmentJobRequest request, CancellationToken cancellationToken)
    {
        if (request.Scope == StockMetadataEnrichmentScope.Selected && (request.SelectedStockIds is null || request.SelectedStockIds.Count == 0))
        {
            return BadRequest("Для режима Selected требуется список stockIds.");
        }

        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        try
        {
            var jobId = await _service.CreateJobAsync(request, userId, cancellationToken);
            return Accepted(new { jobId });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    [HttpGet("jobs/{jobId:guid}")]
    public async Task<ActionResult<StockMetadataEnrichmentJobResponse>> GetJob(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _service.GetJobAsync(jobId, cancellationToken);
        return job is null ? NotFound() : Ok(job);
    }

    [HttpGet("jobs/{jobId:guid}/results")]
    public async Task<ActionResult<StockMetadataEnrichmentResultPageResponse>> GetResults(
        Guid jobId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? decision = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.GetResultsAsync(jobId, page, pageSize, decision, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("jobs/{jobId:guid}/apply")]
    public async Task<IActionResult> Apply(Guid jobId, ApplyStockMetadataEnrichmentJobRequest request, CancellationToken cancellationToken)
    {
        var (success, message) = await _service.ApplyAsync(jobId, request, cancellationToken);
        return success ? Ok(new { message }) : BadRequest(message);
    }

    [HttpPost("jobs/{jobId:guid}/results/{resultId:long}/review")]
    public async Task<IActionResult> Review(Guid jobId, long resultId, ReviewStockMetadataEnrichmentResultRequest request, CancellationToken cancellationToken)
    {
        var (success, message) = await _service.ReviewAsync(jobId, resultId, request, cancellationToken);
        return success ? Ok(new { message }) : BadRequest(message);
    }

    [HttpPost("jobs/{jobId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid jobId, CancellationToken cancellationToken)
    {
        var cancelled = await _service.CancelAsync(jobId, cancellationToken);
        return cancelled ? Ok() : NotFound();
    }

    [HttpPost("jobs/{jobId:guid}/retry")]
    public async Task<IActionResult> Retry(Guid jobId, RetryStockMetadataEnrichmentJobRequest request, CancellationToken cancellationToken)
    {
        var retried = await _service.RetryAsync(jobId, request, cancellationToken);
        return retried ? Ok() : BadRequest();
    }
}
