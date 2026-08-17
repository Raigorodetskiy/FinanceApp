-- ============================================================
-- 03-post-repair-verify.sql — post-repair verification
-- ============================================================
-- Read-only verification.
-- Requires explicit @repair_run_id from 02-apply-repair.sql output.
-- ============================================================

USE FinanceApp;

SET @repair_run_id = 'REPLACE_WITH_RUN_ID';

DROP PROCEDURE IF EXISTS financeapp_verify_tracking_status_repair;
DELIMITER $$
CREATE PROCEDURE financeapp_verify_tracking_status_repair()
BEGIN
    DECLARE v_run_exists INT DEFAULT 0;

    IF @repair_run_id IS NULL
       OR TRIM(@repair_run_id) = ''
       OR UPPER(TRIM(@repair_run_id)) = 'REPLACE_WITH_RUN_ID' THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'BLOCKED: set @repair_run_id before verification.';
    END IF;

    SELECT COUNT(*) INTO v_run_exists
    FROM financeapp_repair_audit.tracking_status_repair_runs
    WHERE RepairRunId = TRIM(@repair_run_id);

    IF v_run_exists = 0 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'BLOCKED: unknown RepairRunId.';
    END IF;

    -- 1) Current status distribution
    SELECT
        TrackingStatus,
        CASE TrackingStatus WHEN 0 THEN 'CatalogOnly' WHEN 1 THEN 'Tracked' ELSE 'Unknown' END AS StatusName,
        COUNT(*) AS StockCount
    FROM Stocks
    GROUP BY TrackingStatus
    ORDER BY TrackingStatus;

    -- 2) Run-level summary
    SELECT
        r.RepairRunId,
        r.ExpectedCandidateCount,
        r.ObservedCandidateCount,
        r.ExpectedCandidateChecksum,
        r.ObservedCandidateChecksum,
        r.ExpectedBaselineCount,
        r.AppliedBy,
        r.AppliedAt
    FROM financeapp_repair_audit.tracking_status_repair_runs r
    WHERE r.RepairRunId = TRIM(@repair_run_id);

    -- 3) No user-owned stock may be CatalogOnly
    SELECT
        s.Id,
        s.Ticker,
        s.Exchange,
        COUNT(DISTINCT pi.Id) AS PortfolioItemCount,
        COUNT(DISTINCT o.Id) AS OrderCount,
        COUNT(DISTINCT t.Id) AS TransactionCount
    FROM Stocks s
    LEFT JOIN PortfolioItems pi ON pi.StockId = s.Id
    LEFT JOIN Orders o ON o.StockId = s.Id
    LEFT JOIN Transactions t ON t.StockId = s.Id
    WHERE s.TrackingStatus = 0
    GROUP BY s.Id, s.Ticker, s.Exchange
    HAVING PortfolioItemCount > 0 OR OrderCount > 0 OR TransactionCount > 0;

    -- 4) Baseline-matching tracked/index rows must remain Tracked
    SELECT
        s.Id,
        s.Ticker,
        s.Exchange,
        s.Isin,
        s.ProviderSymbol,
        s.TrackingStatus
    FROM Stocks s
    JOIN StockMarketIndices smi ON smi.StockId = s.Id AND smi.EffectiveTo IS NULL
    WHERE s.TrackingStatus = 0
      AND (
        (
            NULLIF(UPPER(TRIM(NULLIF(s.Isin, '\\N'))), '') IS NOT NULL
            AND EXISTS (
                SELECT 1
                FROM financeapp_repair_audit.baseline_stocks b
                WHERE NULLIF(UPPER(TRIM(NULLIF(b.Isin, '\\N'))), '') = NULLIF(UPPER(TRIM(NULLIF(s.Isin, '\\N'))), '')
            )
        )
        OR (
            NULLIF(UPPER(TRIM(NULLIF(s.ProviderSymbol, '\\N'))), '') IS NOT NULL
            AND NULLIF(UPPER(TRIM(NULLIF(s.Exchange, '\\N'))), '') IS NOT NULL
            AND EXISTS (
                SELECT 1
                FROM financeapp_repair_audit.baseline_stocks b
                WHERE NULLIF(UPPER(TRIM(NULLIF(b.ProviderSymbol, '\\N'))), '') = NULLIF(UPPER(TRIM(NULLIF(s.ProviderSymbol, '\\N'))), '')
                  AND NULLIF(UPPER(TRIM(NULLIF(b.Exchange, '\\N'))), '') = NULLIF(UPPER(TRIM(NULLIF(s.Exchange, '\\N'))), '')
            )
        )
        OR (
            NULLIF(UPPER(TRIM(NULLIF(s.Ticker, '\\N'))), '') IS NOT NULL
            AND NULLIF(UPPER(TRIM(NULLIF(s.Exchange, '\\N'))), '') IS NOT NULL
            AND EXISTS (
                SELECT 1
                FROM financeapp_repair_audit.baseline_stocks b
                WHERE NULLIF(UPPER(TRIM(NULLIF(b.Ticker, '\\N'))), '') = NULLIF(UPPER(TRIM(NULLIF(s.Ticker, '\\N'))), '')
                  AND NULLIF(UPPER(TRIM(NULLIF(b.Exchange, '\\N'))), '') = NULLIF(UPPER(TRIM(NULLIF(s.Exchange, '\\N'))), '')
            )
        )
      )
    ORDER BY s.Ticker, s.Exchange, s.Id;

    -- 5) Repaired IDs must exactly match run log (and not be duplicated)
    SELECT
        l.StockId,
        l.Ticker,
        l.Exchange,
        l.NewStatus,
        l.RolledBackAt,
        s.TrackingStatus AS CurrentStatus
    FROM financeapp_repair_audit.tracking_status_repair_log l
    JOIN Stocks s ON s.Id = l.StockId
    WHERE l.RepairRunId = TRIM(@repair_run_id)
      AND l.RolledBackAt IS NULL
      AND s.TrackingStatus <> l.NewStatus
    ORDER BY l.StockId;

    SELECT
        RepairRunId,
        StockId,
        COUNT(*) AS DuplicateEntries
    FROM financeapp_repair_audit.tracking_status_repair_log
    GROUP BY RepairRunId, StockId
    HAVING COUNT(*) > 1;

    SELECT
        COUNT(*) AS RunLogRows,
        SUM(CASE WHEN RolledBackAt IS NULL THEN 1 ELSE 0 END) AS ActiveRunRows,
        SUM(CASE WHEN RolledBackAt IS NOT NULL THEN 1 ELSE 0 END) AS RolledBackRows
    FROM financeapp_repair_audit.tracking_status_repair_log
    WHERE RepairRunId = TRIM(@repair_run_id);
END$$
DELIMITER ;

CALL financeapp_verify_tracking_status_repair();
DROP PROCEDURE financeapp_verify_tracking_status_repair;
