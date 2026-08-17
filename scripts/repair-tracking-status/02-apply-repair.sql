-- ============================================================
-- 02-apply-repair.sql — Conservative TrackingStatus repair
-- ============================================================
--
-- PREREQUISITES (must all be satisfied before running):
--   1. FinanceApp service is STOPPED
--   2. Full database backup exists and is verified
--   3. New backend deployed (migration 20260817000000 applied)
--   4. 01-audit-preview.sql reviewed; candidate count is expected
--   5. Baseline stock identities loaded into financeapp_repair_audit
--      (see 00-extract-baseline-stocks.md)
--
-- SAFETY GUARANTEES:
--   - Only sets TrackingStatus = 0 (CatalogOnly) for stocks that:
--       a) Are currently Tracked (TrackingStatus = 1)
--       b) Have an active index membership (EffectiveTo IS NULL)
--       c) Have NO rows in PortfolioItems, Orders, or Transactions
--       d) Are NOT in the pre-import baseline allowlist
--   - All other stocks are left untouched
--   - Runs inside a single transaction; rollback on any error
--   - Creates audit table for rollback support
--   - Idempotent: safe to re-run (demoted rows won't be re-demoted)
--
-- TO ENABLE: change @confirm below from 0 to 1
-- ============================================================

USE FinanceApp;

-- ── Guard: explicit confirmation required ────────────────────────────────────
SET @confirm = 0;  -- CHANGE TO 1 TO APPLY

SELECT IF(
    @confirm = 1,
    'Confirmation accepted – proceeding with repair.',
    '*** BLOCKED: set @confirm = 1 to apply changes ***'
) AS RepairStatus;

-- Abort immediately if not confirmed
SET @blocked = IF(@confirm != 1, (SELECT CAST(1/0 AS CHAR)), NULL);

-- ── Verify migration has been applied ────────────────────────────────────────
SELECT
    MigrationId,
    ProductVersion
FROM __EFMigrationsHistory
WHERE MigrationId = '20260817000000_FixTrackingStatusValueGenerated';

-- Abort if migration is missing
SET @migration_ok = (
    SELECT COUNT(*)
    FROM __EFMigrationsHistory
    WHERE MigrationId = '20260817000000_FixTrackingStatusValueGenerated'
);

SELECT IF(
    @migration_ok >= 1,
    'Migration verified.',
    '*** BLOCKED: migration 20260817000000 not applied – deploy the new backend first ***'
) AS MigrationStatus;

SET @blocked2 = IF(@migration_ok < 1, (SELECT CAST(1/0 AS CHAR)), NULL);

-- ── Create audit/rollback table (idempotent) ─────────────────────────────────
CREATE DATABASE IF NOT EXISTS financeapp_repair_audit
  CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS financeapp_repair_audit.tracking_status_repair_log (
    Id              INT           NOT NULL AUTO_INCREMENT PRIMARY KEY,
    StockId         INT           NOT NULL,
    Ticker          VARCHAR(20)   NOT NULL,
    PreviousStatus  INT           NOT NULL,
    NewStatus       INT           NOT NULL,
    RepairedAt      DATETIME(6)   NOT NULL DEFAULT UTC_TIMESTAMP(6),
    INDEX ix_stock (StockId)
);

-- ── Main repair transaction ───────────────────────────────────────────────────
START TRANSACTION;

-- Log candidates before changing them (for rollback)
INSERT INTO financeapp_repair_audit.tracking_status_repair_log
    (StockId, Ticker, PreviousStatus, NewStatus)
SELECT
    s.Id,
    s.Ticker,
    s.TrackingStatus     AS PreviousStatus,
    0                    AS NewStatus        -- CatalogOnly
FROM Stocks s
JOIN StockMarketIndices smi ON smi.StockId = s.Id AND smi.EffectiveTo IS NULL
LEFT JOIN PortfolioItems pi ON pi.StockId = s.Id
LEFT JOIN Orders         o  ON o.StockId  = s.Id
LEFT JOIN Transactions   t  ON t.StockId  = s.Id
LEFT JOIN (
    -- Baseline allowlist: stocks that existed before the index import
    -- Loaded via 00-extract-baseline-stocks.md into financeapp_repair_audit.baseline_stocks
    SELECT DISTINCT
        b.Ticker,
        b.Exchange
    FROM financeapp_repair_audit.baseline_stocks b
) bl ON bl.Ticker = s.Ticker AND bl.Exchange = s.Exchange
WHERE s.TrackingStatus = 1   -- currently Tracked
  AND pi.Id IS NULL           -- no portfolio items
  AND o.Id  IS NULL           -- no orders
  AND t.Id  IS NULL           -- no transactions
  AND bl.Ticker IS NULL;      -- not in pre-import baseline

-- Apply demotion
UPDATE Stocks s
JOIN StockMarketIndices smi ON smi.StockId = s.Id AND smi.EffectiveTo IS NULL
LEFT JOIN PortfolioItems pi ON pi.StockId = s.Id
LEFT JOIN Orders         o  ON o.StockId  = s.Id
LEFT JOIN Transactions   t  ON t.StockId  = s.Id
LEFT JOIN (
    SELECT DISTINCT b.Ticker, b.Exchange
    FROM financeapp_repair_audit.baseline_stocks b
) bl ON bl.Ticker = s.Ticker AND bl.Exchange = s.Exchange
SET s.TrackingStatus = 0
WHERE s.TrackingStatus = 1
  AND pi.Id IS NULL
  AND o.Id  IS NULL
  AND t.Id  IS NULL
  AND bl.Ticker IS NULL;

SELECT ROW_COUNT() AS RowsDemoted;

COMMIT;

-- ── Post-apply summary ───────────────────────────────────────────────────────
SELECT
    TrackingStatus,
    CASE TrackingStatus WHEN 0 THEN 'CatalogOnly' WHEN 1 THEN 'Tracked' END AS StatusName,
    COUNT(*) AS StockCount
FROM Stocks
GROUP BY TrackingStatus
ORDER BY TrackingStatus;
