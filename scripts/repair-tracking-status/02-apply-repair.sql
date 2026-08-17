-- ============================================================
-- 02-apply-repair.sql — conservative TrackingStatus repair
-- ============================================================
-- Hard requirements:
--   * service is stopped
--   * full backup verified
--   * migration 20260817000000 applied
--   * baseline loaded and validated
--   * expected candidate count/checksum copied from 01-audit-preview.sql
--
-- This script NEVER auto-loads baseline data and never updates rows outside the
-- exact candidate snapshot used for this run.
-- ============================================================

USE FinanceApp;

SET @confirm = 0;                              -- REQUIRED: set to 1
SET @ack_service_stopped = 0;                  -- REQUIRED: set to 1
SET @repair_run_id = 'REPLACE_WITH_RUN_ID';    -- REQUIRED: stable run identifier (e.g., UUID)
SET @expected_candidate_count = -1;            -- REQUIRED: from preview output
SET @expected_candidate_checksum = 'REPLACE_WITH_64_CHAR_SHA256'; -- REQUIRED: from preview output
SET @expected_baseline_count = -1;             -- REQUIRED: baseline row count validated by operator

DROP PROCEDURE IF EXISTS financeapp_apply_tracking_status_repair;
DELIMITER $$
CREATE PROCEDURE financeapp_apply_tracking_status_repair()
BEGIN
    DECLARE v_migration_count INT DEFAULT 0;
    DECLARE v_baseline_table_exists INT DEFAULT 0;
    DECLARE v_baseline_row_count INT DEFAULT 0;
    DECLARE v_baseline_duplicate_count INT DEFAULT 0;
    DECLARE v_baseline_malformed_count INT DEFAULT 0;
    DECLARE v_candidate_count INT DEFAULT 0;
    DECLARE v_rows_logged INT DEFAULT 0;
    DECLARE v_rows_updated INT DEFAULT 0;
    DECLARE v_run_exists INT DEFAULT 0;
    DECLARE v_observed_checksum CHAR(64) DEFAULT NULL;

    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        ROLLBACK;
        RESIGNAL;
    END;

    SET SESSION group_concat_max_len = 1000000;

    IF COALESCE(@confirm, 0) <> 1 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'BLOCKED: set @confirm = 1 before applying repair.';
    END IF;

    IF COALESCE(@ack_service_stopped, 0) <> 1 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'BLOCKED: set @ack_service_stopped = 1 only after service is stopped.';
    END IF;

    IF @repair_run_id IS NULL
       OR TRIM(@repair_run_id) = ''
       OR UPPER(TRIM(@repair_run_id)) = 'REPLACE_WITH_RUN_ID' THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'BLOCKED: set @repair_run_id to an explicit run identifier.';
    END IF;

    IF COALESCE(@expected_candidate_count, -1) < 0 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'BLOCKED: set @expected_candidate_count from preview output.';
    END IF;

    IF @expected_candidate_checksum IS NULL
       OR LENGTH(TRIM(@expected_candidate_checksum)) <> 64
       OR UPPER(TRIM(@expected_candidate_checksum)) = 'REPLACE_WITH_64_CHAR_SHA256' THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'BLOCKED: set @expected_candidate_checksum to the 64-char SHA256 from preview.';
    END IF;

    IF COALESCE(@expected_baseline_count, -1) <= 0 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'BLOCKED: set @expected_baseline_count (> 0) from validated baseline load.';
    END IF;

    SELECT COUNT(*) INTO v_migration_count
    FROM __EFMigrationsHistory
    WHERE MigrationId = '20260817000000_FixTrackingStatusValueGenerated';

    IF v_migration_count <> 1 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'BLOCKED: migration 20260817000000_FixTrackingStatusValueGenerated is missing.';
    END IF;

    SELECT COUNT(*) INTO v_baseline_table_exists
    FROM information_schema.tables
    WHERE table_schema = 'financeapp_repair_audit'
      AND table_name = 'baseline_stocks';

    IF v_baseline_table_exists = 0 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'BLOCKED: financeapp_repair_audit.baseline_stocks is missing. Do not apply without baseline.';
    END IF;

    SELECT COUNT(*) INTO v_baseline_row_count
    FROM financeapp_repair_audit.baseline_stocks;

    IF v_baseline_row_count <= 0 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'BLOCKED: baseline_stocks is empty. Do not apply repair.';
    END IF;

    IF v_baseline_row_count <> @expected_baseline_count THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'BLOCKED: baseline row count mismatch vs operator-expected value.';
    END IF;

    SELECT COUNT(*) INTO v_baseline_duplicate_count
    FROM (
        SELECT
            UPPER(TRIM(COALESCE(NULLIF(Ticker, '\\N'), ''))) AS TickerN,
            UPPER(TRIM(COALESCE(NULLIF(Exchange, '\\N'), ''))) AS ExchangeN,
            UPPER(TRIM(COALESCE(NULLIF(Isin, '\\N'), ''))) AS IsinN,
            UPPER(TRIM(COALESCE(NULLIF(Wkn, '\\N'), ''))) AS WknN,
            UPPER(TRIM(COALESCE(NULLIF(ProviderSymbol, '\\N'), ''))) AS ProviderSymbolN,
            COUNT(*) AS DuplicateCount
        FROM financeapp_repair_audit.baseline_stocks
        GROUP BY TickerN, ExchangeN, IsinN, WknN, ProviderSymbolN
        HAVING COUNT(*) > 1
    ) baseline_duplicates;

    IF v_baseline_duplicate_count > 0 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'BLOCKED: baseline_stocks contains duplicate normalized identities.';
    END IF;

    SELECT COUNT(*) INTO v_baseline_malformed_count
    FROM financeapp_repair_audit.baseline_stocks b
    WHERE NULLIF(TRIM(NULLIF(b.Isin, '\\N')), '') IS NULL
      AND (
           (NULLIF(TRIM(NULLIF(b.ProviderSymbol, '\\N')), '') IS NULL OR NULLIF(TRIM(NULLIF(b.Exchange, '\\N')), '') IS NULL)
       AND (NULLIF(TRIM(NULLIF(b.Ticker, '\\N')), '') IS NULL OR NULLIF(TRIM(NULLIF(b.Exchange, '\\N')), '') IS NULL)
      );

    IF v_baseline_malformed_count > 0 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'BLOCKED: baseline_stocks contains malformed rows without usable identities.';
    END IF;

    CREATE DATABASE IF NOT EXISTS financeapp_repair_audit
      CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

    CREATE TABLE IF NOT EXISTS financeapp_repair_audit.tracking_status_repair_runs (
        RepairRunId CHAR(64) NOT NULL PRIMARY KEY,
        ExpectedCandidateCount INT NOT NULL,
        ObservedCandidateCount INT NOT NULL,
        ExpectedCandidateChecksum CHAR(64) NOT NULL,
        ObservedCandidateChecksum CHAR(64) NOT NULL,
        ExpectedBaselineCount INT NOT NULL,
        AppliedBy VARCHAR(128) NULL,
        ServiceStoppedAck TINYINT(1) NOT NULL,
        AppliedAt DATETIME(6) NOT NULL DEFAULT UTC_TIMESTAMP(6)
    ) ENGINE=InnoDB;

    CREATE TABLE IF NOT EXISTS financeapp_repair_audit.tracking_status_repair_log (
        Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
        RepairRunId CHAR(64) NOT NULL,
        StockId INT NOT NULL,
        Ticker VARCHAR(20) NULL,
        Exchange VARCHAR(20) NULL,
        PreviousStatus INT NOT NULL,
        NewStatus INT NOT NULL,
        CandidateReason VARCHAR(64) NOT NULL,
        CandidateChecksum CHAR(64) NOT NULL,
        LoggedAt DATETIME(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
        RolledBackAt DATETIME(6) NULL,
        UNIQUE KEY ux_tracking_status_repair_log_run_stock (RepairRunId, StockId),
        INDEX ix_tracking_status_repair_log_run (RepairRunId),
        INDEX ix_tracking_status_repair_log_stock (StockId),
        CONSTRAINT fk_tracking_status_repair_log_run
            FOREIGN KEY (RepairRunId)
            REFERENCES financeapp_repair_audit.tracking_status_repair_runs (RepairRunId)
            ON DELETE RESTRICT ON UPDATE RESTRICT
    ) ENGINE=InnoDB;

    SELECT COUNT(*) INTO v_run_exists
    FROM financeapp_repair_audit.tracking_status_repair_runs
    WHERE RepairRunId = TRIM(@repair_run_id);

    IF v_run_exists > 0 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'BLOCKED: this RepairRunId already exists. Use a new run id.';
    END IF;

    START TRANSACTION;

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

    SELECT COUNT(*) INTO v_candidate_count
    FROM tmp_candidate_snapshot
    WHERE CandidateReason = 'DEMOTION_CANDIDATE';

    SELECT COALESCE(SHA2(GROUP_CONCAT(CAST(StockId AS CHAR) ORDER BY StockId SEPARATOR ','), 256), SHA2('', 256))
      INTO v_observed_checksum
    FROM tmp_candidate_snapshot
    WHERE CandidateReason = 'DEMOTION_CANDIDATE';

    IF v_candidate_count <> @expected_candidate_count THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'BLOCKED: candidate count mismatch vs preview. Re-run preview and review changes.';
    END IF;

    IF UPPER(v_observed_checksum) <> UPPER(TRIM(@expected_candidate_checksum)) THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'BLOCKED: candidate checksum mismatch vs preview. Re-run preview and review changes.';
    END IF;

    INSERT INTO financeapp_repair_audit.tracking_status_repair_runs
        (RepairRunId, ExpectedCandidateCount, ObservedCandidateCount, ExpectedCandidateChecksum, ObservedCandidateChecksum, ExpectedBaselineCount, AppliedBy, ServiceStoppedAck)
    VALUES
        (TRIM(@repair_run_id), @expected_candidate_count, v_candidate_count, UPPER(TRIM(@expected_candidate_checksum)), UPPER(v_observed_checksum), @expected_baseline_count, CURRENT_USER(), @ack_service_stopped);

    INSERT INTO financeapp_repair_audit.tracking_status_repair_log
        (RepairRunId, StockId, Ticker, Exchange, PreviousStatus, NewStatus, CandidateReason, CandidateChecksum)
    SELECT
        TRIM(@repair_run_id),
        c.StockId,
        c.Ticker,
        c.Exchange,
        1 AS PreviousStatus,
        0 AS NewStatus,
        c.CandidateReason,
        UPPER(v_observed_checksum)
    FROM tmp_candidate_snapshot c
    WHERE c.CandidateReason = 'DEMOTION_CANDIDATE';

    SET v_rows_logged = ROW_COUNT();

    IF v_rows_logged <> v_candidate_count THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'ABORTED: logged row count mismatch; transaction rolled back.';
    END IF;

    UPDATE Stocks s
    JOIN tmp_candidate_snapshot c ON c.StockId = s.Id
    SET s.TrackingStatus = 0
    WHERE c.CandidateReason = 'DEMOTION_CANDIDATE'
      AND s.TrackingStatus = 1;

    SET v_rows_updated = ROW_COUNT();

    IF v_rows_updated <> v_candidate_count THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'ABORTED: updated row count mismatch; transaction rolled back.';
    END IF;

    COMMIT;

    SELECT
        TRIM(@repair_run_id) AS RepairRunId,
        v_candidate_count AS CandidateCount,
        UPPER(v_observed_checksum) AS CandidateChecksum,
        v_rows_logged AS RowsLogged,
        v_rows_updated AS RowsUpdated;
END$$
DELIMITER ;

CALL financeapp_apply_tracking_status_repair();
DROP PROCEDURE financeapp_apply_tracking_status_repair;
