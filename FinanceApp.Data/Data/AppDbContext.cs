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
    public DbSet<CompanyFundamentalsSnapshot> FundamentalsSnapshots { get; set; } = null!;
    public DbSet<FinancialPeriod> FinancialPeriods { get; set; } = null!;
    public DbSet<EarningsEvent> EarningsEvents { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasIndex(x => x.OrderId).IsUnique().HasFilter("`OrderId` IS NOT NULL");
            entity.HasOne(x => x.Stock)
                .WithMany()
                .HasForeignKey(x => x.StockId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.Property(x => x.InstrumentCode)
                .HasMaxLength(32);
            entity.Property(x => x.InstrumentCodeType)
                .HasConversion<string>()
                .HasMaxLength(8);
            entity.Property(x => x.Quantity)
                .HasPrecision(18, 8);
            entity.Property(x => x.UnitPrice)
                .HasPrecision(18, 8);
        });

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
            entity.Property(x => x.FinanzenNetSlug).HasMaxLength(120);
        });

        modelBuilder.Entity<CompanyFundamentalsSnapshot>(entity =>
        {
            entity.HasIndex(x => x.StockId).IsUnique();
            entity.Property(x => x.SourceSymbol).HasMaxLength(32);
            entity.Property(x => x.Currency).HasMaxLength(8);
            entity.Property(x => x.Source).HasMaxLength(64).HasDefaultValue("Yahoo Finance");
            entity.HasOne(x => x.Stock)
                .WithMany()
                .HasForeignKey(x => x.StockId)
                .OnDelete(DeleteBehavior.Cascade);

            foreach (var prop in new[]
            {
                nameof(CompanyFundamentalsSnapshot.MarketCap),
                nameof(CompanyFundamentalsSnapshot.EnterpriseValue),
                nameof(CompanyFundamentalsSnapshot.TotalDebt),
                nameof(CompanyFundamentalsSnapshot.CashAndEquivalents),
                nameof(CompanyFundamentalsSnapshot.RevenueTtm),
                nameof(CompanyFundamentalsSnapshot.NetIncomeTtm),
                nameof(CompanyFundamentalsSnapshot.EbitdaTtm),
                nameof(CompanyFundamentalsSnapshot.OperatingIncomeTtm),
                nameof(CompanyFundamentalsSnapshot.FreeCashFlowTtm),
                nameof(CompanyFundamentalsSnapshot.TotalAssets),
                nameof(CompanyFundamentalsSnapshot.TotalLiabilities),
            })
            {
                entity.Property(prop).HasColumnType("decimal(28,2)");
            }

            entity.Property(x => x.PeRatio).HasColumnType("decimal(18,4)");
            entity.Property(x => x.ForwardPeRatio).HasColumnType("decimal(18,4)");
            entity.Property(x => x.PbRatio).HasColumnType("decimal(18,4)");
            entity.Property(x => x.DividendYield).HasColumnType("decimal(18,6)");
        });

        modelBuilder.Entity<FinancialPeriod>(entity =>
        {
            entity.HasIndex(x => new { x.SnapshotId, x.PeriodType, x.PeriodEndDate }).IsUnique();
            entity.Property(x => x.PeriodType).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.ReportedCurrency).HasMaxLength(8);
            entity.Property(x => x.Source).HasMaxLength(64).HasDefaultValue("Yahoo Finance");
            entity.HasOne(x => x.Snapshot)
                .WithMany(x => x.Periods)
                .HasForeignKey(x => x.SnapshotId)
                .OnDelete(DeleteBehavior.Cascade);

            foreach (var prop in new[]
            {
                nameof(FinancialPeriod.Revenue),
                nameof(FinancialPeriod.OperatingIncome),
                nameof(FinancialPeriod.NetIncome),
                nameof(FinancialPeriod.Ebitda),
                nameof(FinancialPeriod.TotalDebt),
                nameof(FinancialPeriod.TotalAssets),
                nameof(FinancialPeriod.TotalLiabilities),
                nameof(FinancialPeriod.FreeCashFlow),
            })
            {
                entity.Property(prop).HasColumnType("decimal(28,2)");
            }

            entity.Property(x => x.EpsReported).HasColumnType("decimal(18,4)");
            entity.Property(x => x.EpsEstimate).HasColumnType("decimal(18,4)");
        });

        modelBuilder.Entity<EarningsEvent>(entity =>
        {
            entity.HasIndex(x => new { x.SnapshotId, x.ReportDate, x.FiscalPeriod }).IsUnique();
            entity.Property(x => x.DateStatus).HasConversion<string>().HasMaxLength(16).HasDefaultValue(EarningsDateStatus.Unknown);
            entity.Property(x => x.FiscalPeriod).HasMaxLength(32);
            entity.Property(x => x.Source).HasMaxLength(64).HasDefaultValue("Yahoo Finance");
            entity.HasOne(x => x.Snapshot)
                .WithMany(x => x.EarningsEvents)
                .HasForeignKey(x => x.SnapshotId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(x => x.EpsEstimate).HasColumnType("decimal(18,4)");
            entity.Property(x => x.EpsReported).HasColumnType("decimal(18,4)");
            entity.Property(x => x.RevenueEstimate).HasColumnType("decimal(28,2)");
            entity.Property(x => x.RevenueReported).HasColumnType("decimal(28,2)");
        });
    }
}
