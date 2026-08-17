-- ============================================================
-- 03-post-repair-verify.sql — Post-repair verification
-- Run AFTER 02-apply-repair.sql to confirm results are correct.
-- Read-only; produces no changes.
-- ============================================================

USE FinanceApp;

-- ── 1. Overall distribution ──────────────────────────────────────────────────
SELECT
    TrackingStatus,
    CASE TrackingStatus WHEN 0 THEN 'CatalogOnly' WHEN 1 THEN 'Tracked' END AS StatusName,
    COUNT(*) AS StockCount
FROM Stocks
GROUP BY TrackingStatus
ORDER BY TrackingStatus;

-- ── 2. Verify no user-owned stocks were demoted ──────────────────────────────
-- This query must return 0 rows. Any row here means a stock with user references
-- was accidentally demoted and rollback (04-rollback.sql) must be run immediately.
SELECT
    s.Id, s.Ticker, s.Exchange, s.TrackingStatus,
    COUNT(DISTINCT pi.Id)  AS PortfolioItemCount,
    COUNT(DISTINCT o.Id)   AS OrderCount,
    COUNT(DISTINCT t.Id)   AS TransactionCount
FROM Stocks s
JOIN PortfolioItems pi ON pi.StockId = s.Id
LEFT JOIN Orders     o  ON o.StockId  = s.Id
LEFT JOIN Transactions t ON t.StockId = s.Id
WHERE s.TrackingStatus = 0
GROUP BY s.Id, s.Ticker, s.Exchange, s.TrackingStatus
HAVING PortfolioItemCount > 0 OR OrderCount > 0 OR TransactionCount > 0;

-- ── 3. What the repair log recorded ─────────────────────────────────────────
SELECT
    COUNT(*)         AS RowsDemoted,
    MIN(RepairedAt)  AS FirstRepairAt,
    MAX(RepairedAt)  AS LastRepairAt
FROM financeapp_repair_audit.tracking_status_repair_log;

-- ── 4. Remaining Tracked stocks (should include all pre-import + promoted) ───
SELECT
    s.Id, s.Ticker, s.Name, s.Exchange
FROM Stocks s
WHERE s.TrackingStatus = 1
ORDER BY s.Ticker;
