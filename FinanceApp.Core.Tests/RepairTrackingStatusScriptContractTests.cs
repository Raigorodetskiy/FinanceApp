using System.Text.RegularExpressions;
using Xunit;

namespace FinanceApp.Core.Tests;

public class RepairTrackingStatusScriptContractTests
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    [Fact]
    public void ApplyAndRollback_UseSignalAbort_NotDivideByZeroGuards()
    {
        var apply = Read("scripts/repair-tracking-status/02-apply-repair.sql");
        var rollback = Read("scripts/repair-tracking-status/04-rollback.sql");

        Assert.Contains("SIGNAL SQLSTATE '45000'", apply);
        Assert.Contains("SIGNAL SQLSTATE '45000'", rollback);
        Assert.DoesNotContain("1/0", apply);
        Assert.DoesNotContain("1/0", rollback);
    }

    [Fact]
    public void BaselineExtraction_UsesFiveIdentityColumns_AndHeaderlessExport()
    {
        var extract = Read("scripts/repair-tracking-status/00-extract-baseline-stocks.md");

        Assert.Contains("--skip-column-names", extract);
        Assert.DoesNotMatch(new Regex(@"SELECT\s+Id\s*,", RegexOptions.IgnoreCase), extract);
        Assert.Contains("(Ticker, Exchange, Isin, Wkn, ProviderSymbol)", extract);
        Assert.DoesNotContain("IGNORE 1 ROWS", extract);
    }

    [Fact]
    public void PreviewAndApply_ShareExactCandidateDefinitionBlock()
    {
        var preview = Read("scripts/repair-tracking-status/01-audit-preview.sql");
        var apply = Read("scripts/repair-tracking-status/02-apply-repair.sql");

        var previewBlock = Normalize(ExtractCandidateDefinition(preview));
        var applyBlock = Normalize(ExtractCandidateDefinition(apply));

        Assert.Equal(previewBlock, applyBlock);
    }

    [Fact]
    public void ApplyScript_RequiresExplicitRunIdAndHasUniqueRunStockLogConstraint()
    {
        var apply = Read("scripts/repair-tracking-status/02-apply-repair.sql");

        Assert.Contains("@repair_run_id", apply);
        Assert.Contains("@expected_candidate_count", apply);
        Assert.Contains("@expected_candidate_checksum", apply);
        Assert.Contains("@expected_baseline_count", apply);
        Assert.Contains("UNIQUE KEY ux_tracking_status_repair_log_run_stock (RepairRunId, StockId)", apply);
    }

    [Fact]
    public void RollbackScript_IsScopedByRunIdAndExpectedRestoreCount()
    {
        var rollback = Read("scripts/repair-tracking-status/04-rollback.sql");

        Assert.Contains("@repair_run_id", rollback);
        Assert.Contains("@expected_rows_to_restore", rollback);
        Assert.Contains("RepairRunId = TRIM(@repair_run_id)", rollback);
        Assert.Contains("RolledBackAt IS NULL", rollback);
    }

    private static string Read(string relativePath)
    {
        var fullPath = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(fullPath), $"Expected file not found: {fullPath}");
        return File.ReadAllText(fullPath);
    }

    private static string ExtractCandidateDefinition(string sql)
    {
        var startMarker = "-- BEGIN CANDIDATE_DEFINITION";
        var endMarker = "-- END CANDIDATE_DEFINITION";

        var start = sql.IndexOf(startMarker, StringComparison.Ordinal);
        var end = sql.IndexOf(endMarker, StringComparison.Ordinal);

        Assert.True(start >= 0, "Missing candidate definition start marker");
        Assert.True(end > start, "Missing candidate definition end marker");

        return sql[start..end];
    }

    private static string Normalize(string value)
        => Regex.Replace(value, "\\s+", " ").Trim();
}
