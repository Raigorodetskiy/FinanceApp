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
    public DbSet<StockHistoricalPrice> StockHistoricalPrices { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<StockHistoricalPrice>(entity =>
        {
            entity.HasIndex(x => new { x.StockId, x.Timestamp, x.Interval }).IsUnique();
            entity.HasIndex(x => new { x.StockId, x.Timestamp });
            entity.Property(x => x.Interval).HasMaxLength(10);
            entity.Property(x => x.QuoteCurrency).HasMaxLength(8);
            entity.Property(x => x.FinancialCurrency).HasMaxLength(8);
            entity.Property(x => x.NormalizedQuoteCurrency).HasMaxLength(8);
            entity.Property(x => x.QuoteUnitMultiplier).HasDefaultValue(1m);
            entity.Property(x => x.Volume).HasDefaultValue(0L);
        });

        modelBuilder.Entity<Stock>(entity =>
        {
            entity.Property(x => x.CommonName).HasDefaultValue(string.Empty);
            entity.Property(x => x.Exchange).HasMaxLength(32).HasDefaultValue(StockExchanges.Nyse);
            entity.Property(x => x.Wkn).HasMaxLength(6);
            entity.Property(x => x.Isin).HasMaxLength(12);
            entity.HasIndex(x => x.Wkn).IsUnique().HasFilter("`Wkn` IS NOT NULL");
            entity.HasIndex(x => x.Isin).IsUnique().HasFilter("`Isin` IS NOT NULL");
        });
    }
}
