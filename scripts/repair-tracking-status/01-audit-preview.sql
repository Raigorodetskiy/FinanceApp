-- ============================================================
-- 01-audit-preview.sql  — READ-ONLY audit
-- Safe to run at any time; produces no changes.
-- ============================================================
--
-- PURPOSE:
--   Show which stocks are candidates for demotion to CatalogOnly.
--   A stock is a candidate ONLY if ALL of the following are true:
--     1. TrackingStatus = 1 (currently Tracked)
--     2. Is a current constituent of at least one index (EffectiveTo IS NULL)
--     3. Has NO rows in PortfolioItems, Orders, or Transactions
--     4. Is NOT present in the pre-import baseline backup allowlist
--        (run 00-extract-baseline-stocks.md first and load the allowlist
--        into financeapp_repair_audit.baseline_stocks)
--
-- Run against the PRODUCTION database (read-only).
-- Replace `FinanceApp` with your actual database name if different.
-- ============================================================

USE FinanceApp;

-- ── 1. Overall TrackingStatus distribution ──────────────────────────────────
SELECT
    TrackingStatus,
    CASE TrackingStatus
        WHEN 0 THEN 'CatalogOnly'
        WHEN 1 THEN 'Tracked'
        ELSE 'Unknown'
    END AS StatusName,
    COUNT(*) AS StockCount
FROM Stocks
GROUP BY TrackingStatus
ORDER BY TrackingStatus;

-- ── 2. Per-index breakdown ───────────────────────────────────────────────────
SELECT
    mi.Code                         AS IndexCode,
    s.TrackingStatus,
    CASE s.TrackingStatus
        WHEN 0 THEN 'CatalogOnly'
        WHEN 1 THEN 'Tracked'
        ELSE 'Unknown'
    END                             AS StatusName,
    COUNT(DISTINCT s.Id)            AS StockCount
FROM StockMarketIndices smi
JOIN MarketIndices mi ON mi.Id = smi.MarketIndexId
JOIN Stocks s         ON s.Id  = smi.StockId
WHERE smi.EffectiveTo IS NULL
GROUP BY mi.Code, s.TrackingStatus
ORDER BY mi.Code, s.TrackingStatus;

-- ── 3. Stocks with user-owned references (must NOT be demoted) ───────────────
SELECT
    s.Id, s.Ticker, s.Exchange, s.TrackingStatus,
    COUNT(DISTINCT pi.Id)  AS PortfolioItemCount,
    COUNT(DISTINCT o.Id)   AS OrderCount,
    COUNT(DISTINCT t.Id)   AS TransactionCount
FROM Stocks s
LEFT JOIN PortfolioItems pi ON pi.StockId = s.Id
LEFT JOIN Orders         o  ON o.StockId  = s.Id
LEFT JOIN Transactions   t  ON t.StockId  = s.Id
WHERE s.TrackingStatus = 1
  AND (pi.Id IS NOT NULL OR o.Id IS NOT NULL OR t.Id IS NOT NULL)
GROUP BY s.Id, s.Ticker, s.Exchange, s.TrackingStatus
ORDER BY s.Ticker;

-- ── 4. Candidate preview: index-only Tracked stocks with no user references ──
--    These are the stocks the repair script will demote (if baseline allowlist
--    confirms they were not present before the import).
SELECT
    s.Id,
    s.Ticker,
    s.Name,
    s.Exchange,
    s.ProviderSymbol,
    GROUP_CONCAT(DISTINCT mi.Code ORDER BY mi.Code SEPARATOR ', ') AS IndexMemberships
FROM Stocks s
JOIN StockMarketIndices smi ON smi.StockId = s.Id AND smi.EffectiveTo IS NULL
JOIN MarketIndices mi       ON mi.Id = smi.MarketIndexId
LEFT JOIN PortfolioItems pi ON pi.StockId = s.Id
LEFT JOIN Orders         o  ON o.StockId  = s.Id
LEFT JOIN Transactions   t  ON t.StockId  = s.Id
WHERE s.TrackingStatus = 1
  AND pi.Id IS NULL
  AND o.Id  IS NULL
  AND t.Id  IS NULL
GROUP BY s.Id, s.Ticker, s.Name, s.Exchange, s.ProviderSymbol
ORDER BY s.Ticker;

-- ── 5. Count summary ─────────────────────────────────────────────────────────
SELECT
    (SELECT COUNT(*) FROM Stocks WHERE TrackingStatus = 1)   AS TotalTracked,
    (SELECT COUNT(*) FROM Stocks WHERE TrackingStatus = 0)   AS TotalCatalogOnly,
    (
        SELECT COUNT(DISTINCT s.Id)
        FROM Stocks s
        JOIN StockMarketIndices smi ON smi.StockId = s.Id AND smi.EffectiveTo IS NULL
        LEFT JOIN PortfolioItems pi ON pi.StockId = s.Id
        LEFT JOIN Orders         o  ON o.StockId  = s.Id
        LEFT JOIN Transactions   t  ON t.StockId  = s.Id
        WHERE s.TrackingStatus = 1
          AND pi.Id IS NULL AND o.Id IS NULL AND t.Id IS NULL
    )                                                         AS CandidatesForDemotion;
