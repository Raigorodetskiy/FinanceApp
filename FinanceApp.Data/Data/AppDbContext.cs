using Microsoft.EntityFrameworkCore;
using FinanceApp.Core.Models;

namespace FinanceApp.Data.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Stock> Stocks { get; set; } = null!;
    public DbSet<Portfolio> Portfolios { get; set; } = null!;
    public DbSet<PortfolioItem> PortfolioItems { get; set; } = null!;
    public DbSet<Order> Orders { get; set; } = null!;
    public DbSet<Transaction> Transactions { get; set; } = null!;
    public DbSet<Dividend> Dividends { get; set; } = null!;
}
