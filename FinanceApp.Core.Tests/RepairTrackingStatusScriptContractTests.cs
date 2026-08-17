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
        Assert.Contains("awk -F '\\t'", extract);
        Assert.Contains("NF != 5", extract);
    }

    [Fact]
    public void BaselineExtraction_SupportsLegacySchemaWithoutProviderSymbol_AndPreservesProviderValuesWhenPresent()
    {
        var extract = Read("scripts/repair-tracking-status/00-extract-baseline-stocks.md");

        Assert.Contains("SHOW COLUMNS FROM ${BASELINE_DB}.Stocks", extract);
        Assert.Contains("information_schema.columns", extract);
        Assert.Contains("provider_symbol_exists", extract);
        Assert.Contains("provider_symbol_select=\"NULL AS ProviderSymbol\"", extract);
        Assert.Contains("provider_symbol_select=\"NULLIF(TRIM(ProviderSymbol), '') AS ProviderSymbol\"", extract);
        Assert.Contains("If the pre-migration schema does not contain `ProviderSymbol`, the export uses", extract);
        Assert.Contains("the fifth TSV column must be `\\N`", extract);
    }

    [Fact]
    public void BaselineExtraction_HardAbortsWhenRequiredLegacyColumnsAreMissing()
    {
        var extract = Read("scripts/repair-tracking-status/00-extract-baseline-stocks.md");

        Assert.Contains("required_columns=(Ticker Exchange Isin Wkn)", extract);
        Assert.Contains("ABORT: restored baseline schema is missing required Stocks.${column}.", extract);
        Assert.Contains("restored baseline contains literal", extract);
        Assert.Contains("ABORT: restored Stocks row count (${restored_stock_row_count}) != exported TSV row count (${extracted_row_count}).", extract);
    }

    [Fact]
    public void BaselineLoad_CreatesAuditDatabaseBeforeConnecting_AndUsesLocalInfileOrFallback()
    {
        var extract = Read("scripts/repair-tracking-status/00-extract-baseline-stocks.md");

        AssertOccursInOrder(
            extract,
            @"CREATE DATABASE IF NOT EXISTS \`$REPAIR_DB\`",
            "mysql -u root -p \"$REPAIR_DB\" <<'SQL'");

        Assert.Contains("SHOW VARIABLES LIKE 'local_infile';", extract);
        Assert.Contains("mysql --local-infile=1 -u root -p \"$REPAIR_DB\"", extract);
        Assert.Contains("LOAD DATA LOCAL INFILE '$BASELINE_TSV'", extract);
        Assert.Contains("use_insert_fallback", extract);
        Assert.Contains("python3 - <<'PY' \"$BASELINE_TSV\" \"$BASELINE_FALLBACK_SQL\" \"$baseline_load_id\"", extract);
        Assert.Contains("Do **not** weaken `secure_file_priv`", extract);
    }

    [Fact]
    public void BaselineLoad_ValidatesSourceAndLoadedCounts_SamplesMalformedRows_AndDuplicateNormalizedIdentities()
    {
        var extract = Read("scripts/repair-tracking-status/00-extract-baseline-stocks.md");

        Assert.Contains("restored_stock_row_count", extract);
        Assert.Contains("extracted_row_count", extract);
        Assert.Contains("loaded_row_count", extract);
        Assert.Contains("RestoredBaselineStocksCount", extract);
        Assert.Contains("LoadedBaselineRowCount", extract);
        Assert.Contains("malformed_row_count", extract);
        Assert.Contains("duplicate_identity_count", extract);
        Assert.Contains("ABORT: staging baseline contains malformed rows; validated baseline was left untouched.", extract);
        Assert.Contains("ABORT: staging baseline contains duplicate normalized full identities; validated baseline was left untouched.", extract);
        Assert.Contains("SELECT Ticker, Exchange, Isin, Wkn, ProviderSymbol", extract);
        Assert.Contains("HAVING COUNT(*) > 1", extract);
    }

    [Fact]
    public void BaselineLoad_StagesAndPromotesAtomically_WithoutDestroyingPreviousValidatedBaseline()
    {
        var extract = Read("scripts/repair-tracking-status/00-extract-baseline-stocks.md");

        Assert.Contains("baseline_stocks_stage", extract);
        Assert.Contains("baseline_stocks_promoted", extract);
        Assert.Contains("baseline_stocks_previous", extract);
        Assert.Contains("RENAME TABLE", extract);
        Assert.DoesNotContain("TRUNCATE TABLE baseline_stocks;", extract);
        Assert.Contains("validated allowlist until the atomic promotion succeeds.", extract);
    }

    [Fact]
    public void BaselineLoad_RecordsValidatedProvenanceMetadata_ForPreviewAndApplyHandOff()
    {
        var extract = Read("scripts/repair-tracking-status/00-extract-baseline-stocks.md");

        Assert.Contains("baseline_stocks_loads", extract);
        Assert.Contains("BackupPath", extract);
        Assert.Contains("BackupFileName", extract);
        Assert.Contains("BackupSha256", extract);
        Assert.Contains("ExtractedAt", extract);
        Assert.Contains("IsCurrent", extract);
        Assert.Contains("@expected_baseline_count", extract);
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
    public void PreviewAndApply_RemainCompatibleWithValidatedBaselineWorkflow()
    {
        var preview = Read("scripts/repair-tracking-status/01-audit-preview.sql");
        var apply = Read("scripts/repair-tracking-status/02-apply-repair.sql");
        var readme = Read("scripts/repair-tracking-status/README.md");

        Assert.Contains("financeapp_repair_audit.baseline_stocks", preview);
        Assert.Contains("financeapp_repair_audit.baseline_stocks", apply);
        Assert.Contains("@expected_baseline_count", readme);
        Assert.Contains("Run `01-audit-preview.sql`", readme);
        Assert.Contains("Run `02-apply-repair.sql`", readme);
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

    [Fact]
    public void OperatorRunbook_KeepsServiceStopped_AndDoesNotAutoRunRepair()
    {
        var extract = Read("scripts/repair-tracking-status/00-extract-baseline-stocks.md");
        var readme = Read("scripts/repair-tracking-status/README.md");

        Assert.Contains("Do not execute SQL automatically.", extract);
        Assert.Contains("Keep `financeapp.service` stopped", extract);
        Assert.Contains("Do **not** run repair automatically from this document.", extract);
        Assert.Contains("Restart service only after verification passes", readme);
        Assert.Contains("systemctl stop financeapp.service", readme);
    }

    private static string Read(string relativePath)
    {
        var fullPath = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(fullPath), $"Expected file not found: {fullPath}");
        return File.ReadAllText(fullPath);
    }

    private static void AssertOccursInOrder(string text, string first, string second)
    {
        var firstIndex = text.IndexOf(first, StringComparison.Ordinal);
        var secondIndex = text.IndexOf(second, StringComparison.Ordinal);

        Assert.True(firstIndex >= 0, $"Expected to find: {first}");
        Assert.True(secondIndex > firstIndex, $"Expected '{first}' to appear before '{second}'.");
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
