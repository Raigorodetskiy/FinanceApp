using FinanceApp.API.Models;
using FinanceApp.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceApp.API.Controllers;

[ApiController]
[Route("api/catalog-refresh")]
[Authorize]
public sealed class CatalogStockRefreshController : ControllerBase
{
    private readonly ICatalogStockRefreshStatusService _statusService;

    public CatalogStockRefreshController(ICatalogStockRefreshStatusService statusService)
    {
        _statusService = statusService;
    }

    [HttpGet("status")]
    public async Task<ActionResult<CatalogStockRefreshStatusResponse>> GetStatus(CancellationToken cancellationToken = default)
    {
        return Ok(await _statusService.GetStatusAsync(cancellationToken));
    }
}
