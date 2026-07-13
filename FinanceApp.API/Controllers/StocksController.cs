using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FinanceApp.Data.Data;
using FinanceApp.Core.Models;

namespace FinanceApp.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class StocksController : ControllerBase
{
    private readonly AppDbContext _context;
    public StocksController(AppDbContext context) { _context = context; }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Stock>>> GetAll()
        => await _context.Stocks.ToListAsync();

    [HttpGet("{id}")]
    public async Task<ActionResult<Stock>> GetById(int id)
    {
        var stock = await _context.Stocks.FindAsync(id);
        if (stock == null) return NotFound();
        return stock;
    }

    [HttpPost]
    public async Task<ActionResult<Stock>> Create(Stock stock)
    {
        stock.UpdatedAt = DateTime.UtcNow;
        _context.Stocks.Add(stock);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = stock.Id }, stock);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, Stock stock)
    {
        if (id != stock.Id) return BadRequest();
        stock.UpdatedAt = DateTime.UtcNow;
        _context.Entry(stock).State = EntityState.Modified;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var stock = await _context.Stocks.FindAsync(id);
        if (stock == null) return NotFound();
        _context.Stocks.Remove(stock);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
