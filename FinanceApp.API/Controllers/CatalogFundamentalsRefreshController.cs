using FinanceApp.API.Models;
using FinanceApp.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceApp.API.Controllers;

[ApiController]
[Route("api/catalog-fundamentals-refresh")]
[Authorize]
public sealed class CatalogFundamentalsRefreshController(
    ICatalogFundamentalsRefreshStatusService statusService) : ControllerBase
{
    [HttpGet("status")]
    public async Task<ActionResult<CatalogFundamentalsRefreshStatusResponse>> GetStatus(CancellationToken cancellationToken = default)
    {
        return Ok(await statusService.GetStatusAsync(cancellationToken));
    }
}
