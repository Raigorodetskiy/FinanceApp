-- ============================================================
-- 04-rollback.sql — Rollback repair (restore previous statuses)
-- ============================================================
--
-- WHEN TO RUN:
--   Only run this if 03-post-repair-verify.sql shows unexpected results,
--   e.g. user-owned stocks were accidentally demoted, or the repair count
--   does not match expectations.
--
-- EFFECT:
--   Restores TrackingStatus to the value recorded in the audit log
--   for every stock that was changed by 02-apply-repair.sql.
--   Idempotent: safe to re-run.
--
-- TO ENABLE: change @confirm below from 0 to 1
-- ============================================================

USE FinanceApp;

SET @confirm = 0;  -- CHANGE TO 1 TO APPLY ROLLBACK
SET @blocked = IF(@confirm != 1, (SELECT CAST(1/0 AS CHAR)), NULL);

START TRANSACTION;

UPDATE Stocks s
JOIN financeapp_repair_audit.tracking_status_repair_log r ON r.StockId = s.Id
SET s.TrackingStatus = r.PreviousStatus
WHERE s.TrackingStatus != r.PreviousStatus;

SELECT ROW_COUNT() AS RowsRestored;

COMMIT;

-- Verify
SELECT
    TrackingStatus,
    CASE TrackingStatus WHEN 0 THEN 'CatalogOnly' WHEN 1 THEN 'Tracked' END AS StatusName,
    COUNT(*) AS StockCount
FROM Stocks
GROUP BY TrackingStatus
ORDER BY TrackingStatus;
