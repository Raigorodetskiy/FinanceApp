-- ============================================================
-- 01-audit-preview.sql — tracking-status repair preview
-- ============================================================
-- Read-only preview for repair candidates.
--
-- Shows:
--   * baseline validation summary
--   * baseline-protected stocks
--   * stocks protected by PortfolioItems/Orders/Transactions
--   * ambiguous stocks excluded from auto-demotion
--   * exact demotion candidates + deterministic checksum
--
-- Run against production DB (default name: FinanceApp).
-- ============================================================

USE FinanceApp;

DROP PROCEDURE IF EXISTS financeapp_repair_preview_tracking_status;
DELIMITER $$
CREATE PROCEDURE financeapp_repair_preview_tracking_status()
BEGIN
    DECLARE v_baseline_table_exists INT DEFAULT 0;
    DECLARE v_baseline_row_count INT DEFAULT 0;

    SET SESSION group_concat_max_len = 1000000;

    SELECT COUNT(*) INTO v_baseline_table_exists
    FROM information_schema.tables
    WHERE table_schema = 'financeapp_repair_audit'
      AND table_name = 'baseline_stocks';

    IF v_baseline_table_exists = 0 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'BLOCKED: financeapp_repair_audit.baseline_stocks is missing. Run 00-extract-baseline-stocks.md first.';
    END IF;

    SELECT COUNT(*) INTO v_baseline_row_count
    FROM financeapp_repair_audit.baseline_stocks;

    IF v_baseline_row_count = 0 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'BLOCKED: baseline_stocks is empty. Load baseline identities before preview/apply.';
    END IF;

    DROP TEMPORARY TABLE IF EXISTS tmp_baseline_norm;
    CREATE TEMPORARY TABLE tmp_baseline_norm AS
    SELECT
        NULLIF(UPPER(TRIM(NULLIF(Ticker, '\\N'))), '') AS TickerN,
        NULLIF(UPPER(TRIM(NULLIF(Exchange, '\\N'))), '') AS ExchangeN,
        NULLIF(UPPER(TRIM(NULLIF(Isin, '\\N'))), '') AS IsinN,
        NULLIF(UPPER(TRIM(NULLIF(Wkn, '\\N'))), '') AS WknN,
        NULLIF(UPPER(TRIM(NULLIF(ProviderSymbol, '\\N'))), '') AS ProviderSymbolN
    FROM financeapp_repair_audit.baseline_stocks;

    DROP TEMPORARY TABLE IF EXISTS tmp_baseline_isin_unique;
    CREATE TEMPORARY TABLE tmp_baseline_isin_unique AS
    SELECT
        IsinN,
        COUNT(DISTINCT CONCAT_WS('|', COALESCE(TickerN, ''), COALESCE(ExchangeN, ''), COALESCE(ProviderSymbolN, ''), COALESCE(WknN, ''))) AS DistinctIdentityCount
    FROM tmp_baseline_norm
    WHERE IsinN IS NOT NULL
    GROUP BY IsinN;

    DROP TEMPORARY TABLE IF EXISTS tmp_baseline_provider_unique;
    CREATE TEMPORARY TABLE tmp_baseline_provider_unique AS
    SELECT
        ProviderSymbolN,
        ExchangeN,
        COUNT(DISTINCT CONCAT_WS('|', COALESCE(TickerN, ''), COALESCE(IsinN, ''), COALESCE(WknN, ''))) AS DistinctIdentityCount
    FROM tmp_baseline_norm
    WHERE ProviderSymbolN IS NOT NULL AND ExchangeN IS NOT NULL
    GROUP BY ProviderSymbolN, ExchangeN;

    DROP TEMPORARY TABLE IF EXISTS tmp_baseline_ticker_unique;
    CREATE TEMPORARY TABLE tmp_baseline_ticker_unique AS
    SELECT
        TickerN,
        ExchangeN,
        COUNT(DISTINCT CONCAT_WS('|', COALESCE(IsinN, ''), COALESCE(ProviderSymbolN, ''), COALESCE(WknN, ''))) AS DistinctIdentityCount
    FROM tmp_baseline_norm
    WHERE TickerN IS NOT NULL AND ExchangeN IS NOT NULL
    GROUP BY TickerN, ExchangeN;

    DROP TEMPORARY TABLE IF EXISTS tmp_stock_references;
    CREATE TEMPORARY TABLE tmp_stock_references AS
    SELECT
        s.Id AS StockId,
        COUNT(DISTINCT pi.Id) AS PortfolioItemCount,
        COUNT(DISTINCT o.Id) AS OrderCount,
        COUNT(DISTINCT t.Id) AS TransactionCount
    FROM Stocks s
    LEFT JOIN PortfolioItems pi ON pi.StockId = s.Id
    LEFT JOIN Orders o ON o.StockId = s.Id
    LEFT JOIN Transactions t ON t.StockId = s.Id
    GROUP BY s.Id;

    DROP TEMPORARY TABLE IF EXISTS tmp_stock_ambiguity;
    CREATE TEMPORARY TABLE tmp_stock_ambiguity AS
    SELECT
        s.Id AS StockId,
        CASE WHEN isin_counts.IsinDupCount > 1 THEN 1 ELSE 0 END AS IsinAmbiguous,
        CASE WHEN provider_counts.ProviderDupCount > 1 THEN 1 ELSE 0 END AS ProviderAmbiguous,
        CASE WHEN ticker_counts.TickerDupCount > 1 THEN 1 ELSE 0 END AS TickerAmbiguous
    FROM Stocks s
    LEFT JOIN (
        SELECT
            NULLIF(UPPER(TRIM(NULLIF(Isin, '\\N'))), '') AS IsinN,
            COUNT(*) AS IsinDupCount
        FROM Stocks
        WHERE NULLIF(UPPER(TRIM(NULLIF(Isin, '\\N'))), '') IS NOT NULL
        GROUP BY NULLIF(UPPER(TRIM(NULLIF(Isin, '\\N'))), '')
    ) isin_counts ON isin_counts.IsinN = NULLIF(UPPER(TRIM(NULLIF(s.Isin, '\\N'))), '')
    LEFT JOIN (
        SELECT
            NULLIF(UPPER(TRIM(NULLIF(ProviderSymbol, '\\N'))), '') AS ProviderSymbolN,
            NULLIF(UPPER(TRIM(NULLIF(Exchange, '\\N'))), '') AS ExchangeN,
            COUNT(*) AS ProviderDupCount
        FROM Stocks
        WHERE NULLIF(UPPER(TRIM(NULLIF(ProviderSymbol, '\\N'))), '') IS NOT NULL
          AND NULLIF(UPPER(TRIM(NULLIF(Exchange, '\\N'))), '') IS NOT NULL
        GROUP BY
            NULLIF(UPPER(TRIM(NULLIF(ProviderSymbol, '\\N'))), ''),
            NULLIF(UPPER(TRIM(NULLIF(Exchange, '\\N'))), '')
    ) provider_counts
        ON provider_counts.ProviderSymbolN = NULLIF(UPPER(TRIM(NULLIF(s.ProviderSymbol, '\\N'))), '')
       AND provider_counts.ExchangeN = NULLIF(UPPER(TRIM(NULLIF(s.Exchange, '\\N'))), '')
    LEFT JOIN (
        SELECT
            NULLIF(UPPER(TRIM(NULLIF(Ticker, '\\N'))), '') AS TickerN,
            NULLIF(UPPER(TRIM(NULLIF(Exchange, '\\N'))), '') AS ExchangeN,
            COUNT(*) AS TickerDupCount
        FROM Stocks
        WHERE NULLIF(UPPER(TRIM(NULLIF(Ticker, '\\N'))), '') IS NOT NULL
          AND NULLIF(UPPER(TRIM(NULLIF(Exchange, '\\N'))), '') IS NOT NULL
        GROUP BY
            NULLIF(UPPER(TRIM(NULLIF(Ticker, '\\N'))), ''),
            NULLIF(UPPER(TRIM(NULLIF(Exchange, '\\N'))), '')
    ) ticker_counts
        ON ticker_counts.TickerN = NULLIF(UPPER(TRIM(NULLIF(s.Ticker, '\\N'))), '')
       AND ticker_counts.ExchangeN = NULLIF(UPPER(TRIM(NULLIF(s.Exchange, '\\N'))), '');

    DROP TEMPORARY TABLE IF EXISTS tmp_candidate_snapshot;
    CREATE TEMPORARY TABLE tmp_candidate_snapshot (
        StockId INT NOT NULL PRIMARY KEY,
        Ticker VARCHAR(20) NULL,
        Name VARCHAR(200) NULL,
        Exchange VARCHAR(20) NULL,
        Isin VARCHAR(12) NULL,
        ProviderSymbol VARCHAR(50) NULL,
        TrackingStatus INT NOT NULL,
        IndexMemberships TEXT NOT NULL,
        PortfolioItemCount INT NOT NULL,
        OrderCount INT NOT NULL,
        TransactionCount INT NOT NULL,
        BaselineMatchStrategy VARCHAR(40) NULL,
        IsAmbiguous TINYINT(1) NOT NULL,
        CandidateReason VARCHAR(64) NOT NULL
    ) ENGINE=InnoDB;

    -- BEGIN CANDIDATE_DEFINITION
    INSERT INTO tmp_candidate_snapshot
    SELECT
        s.Id AS StockId,
        s.Ticker,
        s.Name,
        s.Exchange,
        s.Isin,
        s.ProviderSymbol,
        s.TrackingStatus,
        GROUP_CONCAT(DISTINCT mi.Code ORDER BY mi.Code SEPARATOR ', ') AS IndexMemberships,
        COALESCE(sr.PortfolioItemCount, 0) AS PortfolioItemCount,
        COALESCE(sr.OrderCount, 0) AS OrderCount,
        COALESCE(sr.TransactionCount, 0) AS TransactionCount,
        CASE
            WHEN bi.IsinN IS NOT NULL AND bi.DistinctIdentityCount = 1 THEN 'ISIN'
            WHEN bp.ProviderSymbolN IS NOT NULL AND bp.DistinctIdentityCount = 1 THEN 'PROVIDER_SYMBOL_EXCHANGE'
            WHEN bt.TickerN IS NOT NULL AND bt.DistinctIdentityCount = 1 THEN 'TICKER_EXCHANGE'
            ELSE NULL
        END AS BaselineMatchStrategy,
        CASE
            WHEN COALESCE(sa.IsinAmbiguous, 0) = 1
              OR COALESCE(sa.ProviderAmbiguous, 0) = 1
              OR COALESCE(sa.TickerAmbiguous, 0) = 1
            THEN 1 ELSE 0
        END AS IsAmbiguous,
        CASE
            WHEN COALESCE(sr.PortfolioItemCount, 0) > 0
              OR COALESCE(sr.OrderCount, 0) > 0
              OR COALESCE(sr.TransactionCount, 0) > 0
            THEN 'PROTECTED_BY_REFERENCES'
            WHEN bi.IsinN IS NOT NULL AND bi.DistinctIdentityCount = 1
            THEN 'PROTECTED_BY_BASELINE_ISIN'
            WHEN bp.ProviderSymbolN IS NOT NULL AND bp.DistinctIdentityCount = 1
            THEN 'PROTECTED_BY_BASELINE_PROVIDER'
            WHEN bt.TickerN IS NOT NULL AND bt.DistinctIdentityCount = 1
            THEN 'PROTECTED_BY_BASELINE_TICKER'
            WHEN (COALESCE(sa.IsinAmbiguous, 0) = 1
               OR COALESCE(sa.ProviderAmbiguous, 0) = 1
               OR COALESCE(sa.TickerAmbiguous, 0) = 1)
            THEN 'AMBIGUOUS'
            ELSE 'DEMOTION_CANDIDATE'
        END AS CandidateReason
    FROM Stocks s
    JOIN StockMarketIndices smi ON smi.StockId = s.Id AND smi.EffectiveTo IS NULL
    JOIN MarketIndices mi ON mi.Id = smi.MarketIndexId
    LEFT JOIN tmp_stock_references sr ON sr.StockId = s.Id
    LEFT JOIN tmp_stock_ambiguity sa ON sa.StockId = s.Id
    LEFT JOIN tmp_baseline_isin_unique bi
        ON bi.IsinN = NULLIF(UPPER(TRIM(NULLIF(s.Isin, '\\N'))), '')
    LEFT JOIN tmp_baseline_provider_unique bp
        ON bp.ProviderSymbolN = NULLIF(UPPER(TRIM(NULLIF(s.ProviderSymbol, '\\N'))), '')
       AND bp.ExchangeN = NULLIF(UPPER(TRIM(NULLIF(s.Exchange, '\\N'))), '')
    LEFT JOIN tmp_baseline_ticker_unique bt
        ON bt.TickerN = NULLIF(UPPER(TRIM(NULLIF(s.Ticker, '\\N'))), '')
       AND bt.ExchangeN = NULLIF(UPPER(TRIM(NULLIF(s.Exchange, '\\N'))), '')
    WHERE s.TrackingStatus = 1
    GROUP BY
        s.Id, s.Ticker, s.Name, s.Exchange, s.Isin, s.ProviderSymbol, s.TrackingStatus,
        sr.PortfolioItemCount, sr.OrderCount, sr.TransactionCount,
        bi.IsinN, bi.DistinctIdentityCount,
        bp.ProviderSymbolN, bp.DistinctIdentityCount,
        bt.TickerN, bt.DistinctIdentityCount,
        sa.IsinAmbiguous, sa.ProviderAmbiguous, sa.TickerAmbiguous;
    -- END CANDIDATE_DEFINITION

    -- Baseline diagnostics
    SELECT
        v_baseline_row_count AS BaselineRowCount,
        (SELECT COUNT(*) FROM tmp_baseline_norm WHERE IsinN IS NOT NULL) AS BaselineRowsWithIsin,
        (SELECT COUNT(*) FROM tmp_baseline_norm WHERE ProviderSymbolN IS NOT NULL AND ExchangeN IS NOT NULL) AS BaselineRowsWithProviderIdentity,
        (SELECT COUNT(*) FROM tmp_baseline_norm WHERE TickerN IS NOT NULL AND ExchangeN IS NOT NULL) AS BaselineRowsWithTickerIdentity;

    SELECT
        TickerN,
        ExchangeN,
        IsinN,
        WknN,
        ProviderSymbolN,
        COUNT(*) AS DuplicateCount
    FROM tmp_baseline_norm
    GROUP BY TickerN, ExchangeN, IsinN, WknN, ProviderSymbolN
    HAVING COUNT(*) > 1;

    -- Expected values for apply script guard
    SELECT
        COUNT(*) AS CandidateCount,
        COALESCE(SHA2(GROUP_CONCAT(CAST(StockId AS CHAR) ORDER BY StockId SEPARATOR ','), 256), SHA2('', 256)) AS CandidateChecksum
    FROM tmp_candidate_snapshot
    WHERE CandidateReason = 'DEMOTION_CANDIDATE';

    -- Candidate details (to be manually reviewed)
    SELECT
        StockId,
        Ticker,
        Exchange,
        Isin,
        ProviderSymbol,
        IndexMemberships
    FROM tmp_candidate_snapshot
    WHERE CandidateReason = 'DEMOTION_CANDIDATE'
    ORDER BY Ticker, Exchange, StockId;

    -- Baseline-protected rows
    SELECT
        StockId,
        Ticker,
        Exchange,
        BaselineMatchStrategy,
        IndexMemberships
    FROM tmp_candidate_snapshot
    WHERE CandidateReason IN (
        'PROTECTED_BY_BASELINE_ISIN',
        'PROTECTED_BY_BASELINE_PROVIDER',
        'PROTECTED_BY_BASELINE_TICKER'
    )
    ORDER BY CandidateReason, Ticker, Exchange, StockId;

    -- Rows protected by references
    SELECT
        StockId,
        Ticker,
        Exchange,
        PortfolioItemCount,
        OrderCount,
        TransactionCount,
        IndexMemberships
    FROM tmp_candidate_snapshot
    WHERE CandidateReason = 'PROTECTED_BY_REFERENCES'
    ORDER BY Ticker, Exchange, StockId;

    -- Ambiguous rows (manual review only)
    SELECT
        StockId,
        Ticker,
        Exchange,
        Isin,
        ProviderSymbol,
        IndexMemberships
    FROM tmp_candidate_snapshot
    WHERE CandidateReason = 'AMBIGUOUS'
    ORDER BY Ticker, Exchange, StockId;

    -- Reason summary
    SELECT CandidateReason, COUNT(*) AS StockCount
    FROM tmp_candidate_snapshot
    GROUP BY CandidateReason
    ORDER BY CandidateReason;
END$$
DELIMITER ;

CALL financeapp_repair_preview_tracking_status();
DROP PROCEDURE financeapp_repair_preview_tracking_status;
