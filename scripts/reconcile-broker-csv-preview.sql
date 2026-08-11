-- =============================================================================
-- READ-ONLY PREVIEW SCRIPT
-- Broker CSV Reconciliation – Step 2: Match staging rows against Transactions
--
-- This script is STRICTLY read-only with respect to the Transactions table.
-- It updates broker_csv_staging.MatchStatus / MatchScore / MatchEvidence
-- and broker_csv_staging.MatchedTransactionId for review, but never modifies
-- Transactions.
--
-- !! MAKE A FULL DATABASE BACKUP BEFORE ANY APPLY STEP !!
-- !! Run this preview script and review carefully before running the apply script !!
--
-- Run:
--   mysql -h HOST -u USER -p DATABASE \
--     < scripts/reconcile-broker-csv-preview.sql
--
-- Output categories per staging row:
--   MATCHED_EXACT      – single confident match with strong evidence
--   MATCHED_PROBABLE   – single match with good but not perfect evidence
--   AMBIGUOUS          – multiple candidates; do not update automatically
--   UNMATCHED          – no candidate found
--   CORPORATE_ACTION   – row is a split/capitalisation/custody transfer
--   CURRENCY_MISMATCH  – currencies cannot be reconciled
--   PARSE_ERROR        – row could not be parsed on import
--   SKIPPED_ALREADY_FILLED – all target fields already populated
--
-- MariaDB 10.5+ compatible.
-- =============================================================================

SET NAMES utf8mb4;

-- ---------------------------------------------------------------------------
-- STEP 1: Reset match state for all PENDING rows (idempotent re-run)
-- ---------------------------------------------------------------------------
UPDATE `broker_csv_staging`
SET
    `MatchStatus`            = 'PENDING',
    `MatchScore`             = NULL,
    `MatchEvidence`          = NULL,
    `MatchedTransactionId`   = NULL
WHERE `MatchStatus` NOT IN ('PARSE_ERROR');

-- ---------------------------------------------------------------------------
-- STEP 2: Mark corporate actions immediately
-- ---------------------------------------------------------------------------
UPDATE `broker_csv_staging`
SET
    `MatchStatus`   = 'CORPORATE_ACTION',
    `MatchEvidence` = CONCAT('CorporateAction: ', COALESCE(`CorporateActionHint`, `Buchungsinformation`))
WHERE `TradeType` = 'CorporateAction'
  AND `MatchStatus` = 'PENDING';

-- ---------------------------------------------------------------------------
-- STEP 3: Mark rows where all target fields are already filled
--   (requires a match to exist; we check after we find candidates below;
--    for now mark only if there is no ISIN/date available to match on)
-- ---------------------------------------------------------------------------
-- We intentionally defer this check to after matching to produce the
-- SKIPPED_ALREADY_FILLED status inside the candidate scoring section.

-- ---------------------------------------------------------------------------
-- STEP 4: Build candidate matches with scored evidence
--   Uses a temporary table to hold all (staging_row, candidate_transaction) pairs
--   with individual evidence scores.
-- ---------------------------------------------------------------------------
DROP TEMPORARY TABLE IF EXISTS `_csv_candidates`;

CREATE TEMPORARY TABLE `_csv_candidates` (
    `StagingId`        INT            NOT NULL,
    `TransactionId`    INT            NOT NULL,
    `Score`            TINYINT        NOT NULL DEFAULT 0,
    `Evidence`         TEXT           NULL,
    PRIMARY KEY (`StagingId`, `TransactionId`),
    INDEX (`StagingId`),
    INDEX (`TransactionId`)
);

-- ---------------------------------------------------------------------------
-- 4a. Candidate search: ISIN + TradeType + Buchungstag (or Valuta window)
--     Score contributions:
--       +30  BrokerRef match in Description
--       +25  TaNr match in Description
--       +20  ISIN + Type + exact Buchungstag
--       +15  ISIN + Type + exact Valuta
--       +10  Betrag/Amount consistency (within 20 EUR or 5% of amount)
--       +5   additional date window hit (±3 days)
--
-- We search in Description for embedded fields using the formats documented
-- in the problem statement (BrokerRef=..., TA-Nr=..., Buchungstag=..., etc.)
-- ---------------------------------------------------------------------------
INSERT INTO `_csv_candidates`
    (`StagingId`, `TransactionId`, `Score`, `Evidence`)
SELECT
    s.`Id`                      AS `StagingId`,
    t.`Id`                      AS `TransactionId`,
    -- Score accumulation
    (
        -- BrokerRef match (+30): BrokerRef appears in Description
        CASE WHEN s.`BrokerRef` IS NOT NULL
              AND t.`Description` LIKE CONCAT('%', s.`BrokerRef`, '%')
             THEN 30 ELSE 0 END
        +
        -- TaNr match (+25): TA-Nr appears in Description
        CASE WHEN s.`TaNr` IS NOT NULL
              AND (    t.`Description` LIKE CONCAT('%TA-Nr=', s.`TaNr`, '%')
                    OR t.`Description` LIKE CONCAT('%TA.-Nr.=', s.`TaNr`, '%')
                    OR t.`Description` LIKE CONCAT('%TA-Nr=', s.`TaNr`, ';%')
                    OR t.`Description` LIKE CONCAT('%', s.`TaNr`, '%'))
             THEN 25 ELSE 0 END
        +
        -- Exact Buchungstag date match (+20)
        CASE WHEN s.`Buchungstag` IS NOT NULL
              AND (    t.`Description` LIKE CONCAT('%Buchungstag=', DATE_FORMAT(s.`Buchungstag`, '%d.%m.%Y'), '%')
                    OR t.`Description` LIKE CONCAT('%Buchungstag=', DATE_FORMAT(s.`Buchungstag`, '%Y-%m-%d'), '%')
                    OR DATE(t.`CreatedAt`) = s.`Buchungstag`)
             THEN 20 ELSE 0 END
        +
        -- Exact Valuta match (+15)
        CASE WHEN s.`Valuta` IS NOT NULL
              AND (    t.`Description` LIKE CONCAT('%Valuta=', DATE_FORMAT(s.`Valuta`, '%d.%m.%Y'), '%')
                    OR t.`Description` LIKE CONCAT('%Valuta=', DATE_FORMAT(s.`Valuta`, '%Y-%m-%d'), '%'))
             THEN 15 ELSE 0 END
        +
        -- ISIN match in Description (+10; base requirement already in WHERE)
        CASE WHEN s.`ISIN` IS NOT NULL
              AND t.`Description` LIKE CONCAT('%ISIN=', s.`ISIN`, '%')
             THEN 10 ELSE 0 END
        +
        -- Amount consistency (+10): ABS(t.Amount) within 20 EUR or within 5% of CSV Betrag
        CASE WHEN s.`Betrag` IS NOT NULL AND s.`BetragCurrency` = 'EUR'
              AND (    ABS(ABS(t.`Amount`) - s.`Betrag`) <= 20.00
                    OR (s.`Betrag` > 0 AND ABS(ABS(t.`Amount`) - s.`Betrag`) / s.`Betrag` <= 0.05))
             THEN 10 ELSE 0 END
    )                           AS `Score`,
    CONCAT(
        'ISIN=', COALESCE(s.`ISIN`, 'NULL'), '; ',
        'Type=', s.`TradeType`, '; ',
        'Buchungstag=', COALESCE(CAST(s.`Buchungstag` AS CHAR), 'NULL'), '; ',
        'Valuta=', COALESCE(CAST(s.`Valuta` AS CHAR), 'NULL'), '; ',
        'BrokerRef=', COALESCE(s.`BrokerRef`, 'NULL'), '; ',
        'TaNr=', COALESCE(s.`TaNr`, 'NULL'), '; ',
        'CsvBetrag=', COALESCE(CAST(s.`Betrag` AS CHAR), 'NULL'), ' ',
        COALESCE(s.`BetragCurrency`, ''), '; ',
        'TxAmount=', COALESCE(CAST(t.`Amount` AS CHAR), 'NULL'), '; ',
        'TxId=', CAST(t.`Id` AS CHAR)
    )                           AS `Evidence`
FROM `broker_csv_staging` s
INNER JOIN `Transactions` t
    ON  t.`InstrumentCode` = s.`ISIN`
     OR (s.`ISIN` IS NOT NULL AND t.`Description` LIKE CONCAT('%', s.`ISIN`, '%'))
WHERE s.`MatchStatus` = 'PENDING'
  AND s.`TradeType` IN ('Buy', 'Sell')
  AND s.`ISIN` IS NOT NULL
  AND (
        -- Type must match
        (s.`TradeType` = 'Buy'  AND t.`Type` = 'Buy')
     OR (s.`TradeType` = 'Sell' AND t.`Type` = 'Sell')
  )
  AND (
        -- Date window: Buchungstag ±3 days OR Valuta ±3 days OR date in Description
           (s.`Buchungstag` IS NOT NULL
            AND ABS(DATEDIFF(DATE(t.`CreatedAt`), s.`Buchungstag`)) <= 3)
        OR (s.`Valuta` IS NOT NULL
            AND ABS(DATEDIFF(DATE(t.`CreatedAt`), s.`Valuta`)) <= 3)
        OR (s.`Buchungstag` IS NOT NULL
            AND t.`Description` LIKE CONCAT('%Buchungstag=', DATE_FORMAT(s.`Buchungstag`, '%d.%m.%Y'), '%'))
        OR (s.`Valuta` IS NOT NULL
            AND t.`Description` LIKE CONCAT('%Valuta=', DATE_FORMAT(s.`Valuta`, '%d.%m.%Y'), '%'))
        OR (s.`Buchungstag` IS NOT NULL
            AND t.`Description` LIKE CONCAT('%Buchungstag=', DATE_FORMAT(s.`Buchungstag`, '%Y-%m-%d'), '%'))
  );

-- ---------------------------------------------------------------------------
-- 4b. Remove zero-score rows (no meaningful evidence at all)
-- ---------------------------------------------------------------------------
DELETE FROM `_csv_candidates` WHERE `Score` = 0;

-- ---------------------------------------------------------------------------
-- STEP 5: Currency mismatch detection
--   If top candidate has mismatched currency and we cannot reconcile, mark CURRENCY_MISMATCH
-- ---------------------------------------------------------------------------
-- We check: if BetragCurrency != 'EUR' and Description doesn't contain
-- a compatible Originalbetrag/BetragEUR field, flag it.
UPDATE `broker_csv_staging` s
INNER JOIN (
    SELECT `StagingId`, MAX(`Score`) AS `TopScore`
    FROM `_csv_candidates`
    GROUP BY `StagingId`
) best ON best.`StagingId` = s.`Id`
INNER JOIN `_csv_candidates` c ON c.`StagingId` = s.`Id` AND c.`Score` = best.`TopScore`
INNER JOIN `Transactions` t ON t.`Id` = c.`TransactionId`
SET
    s.`MatchStatus`  = 'CURRENCY_MISMATCH',
    s.`MatchEvidence` = CONCAT('CurrencyMismatch: CsvCurrency=',
        COALESCE(s.`BetragCurrency`, '?'), '; ',
        c.`Evidence`)
WHERE s.`MatchStatus` = 'PENDING'
  AND s.`BetragCurrency` IS NOT NULL
  AND s.`BetragCurrency` != 'EUR'
  AND s.`KursCurrency`   IS NOT NULL
  AND s.`KursCurrency`  != 'EUR'
  AND (
        t.`Description` NOT LIKE '%Originalbetrag=%'
    AND t.`Description` NOT LIKE '%BetragEUR=%'
  );

-- ---------------------------------------------------------------------------
-- STEP 6: Determine final match status for remaining PENDING rows
-- ---------------------------------------------------------------------------

-- Count candidates per staging row
DROP TEMPORARY TABLE IF EXISTS `_csv_candidate_counts`;
CREATE TEMPORARY TABLE `_csv_candidate_counts` AS
SELECT
    `StagingId`,
    COUNT(*)        AS `CandidateCount`,
    MAX(`Score`)    AS `MaxScore`,
    MIN(`Score`)    AS `MinScore`
FROM `_csv_candidates`
GROUP BY `StagingId`;

-- 6a: AMBIGUOUS – multiple candidates, or top score not clearly better
UPDATE `broker_csv_staging` s
INNER JOIN `_csv_candidate_counts` cc ON cc.`StagingId` = s.`Id`
SET
    s.`MatchStatus`  = 'AMBIGUOUS',
    s.`MatchScore`   = cc.`MaxScore`,
    s.`MatchEvidence` = CONCAT('AmbiguousCount=', cc.`CandidateCount`,
        '; MaxScore=', cc.`MaxScore`, '; MinScore=', cc.`MinScore`)
WHERE s.`MatchStatus` = 'PENDING'
  AND cc.`CandidateCount` > 1;

-- 6b: UNMATCHED – no candidates at all
UPDATE `broker_csv_staging` s
LEFT JOIN `_csv_candidate_counts` cc ON cc.`StagingId` = s.`Id`
SET
    s.`MatchStatus`  = 'UNMATCHED',
    s.`MatchEvidence` = 'No candidate transactions found'
WHERE s.`MatchStatus` = 'PENDING'
  AND cc.`StagingId` IS NULL;

-- 6c: Single candidate – determine MATCHED_EXACT vs MATCHED_PROBABLE
--     MATCHED_EXACT   : score >= 50 (requires BrokerRef + ISIN/date evidence)
--     MATCHED_PROBABLE: score >= 30 (ISIN + date/amount evidence, no BrokerRef)
UPDATE `broker_csv_staging` s
INNER JOIN `_csv_candidate_counts` cc ON cc.`StagingId` = s.`Id`
INNER JOIN `_csv_candidates` c ON c.`StagingId` = s.`Id` AND c.`Score` = cc.`MaxScore`
SET
    s.`MatchStatus`          = CASE WHEN cc.`MaxScore` >= 50 THEN 'MATCHED_EXACT' ELSE 'MATCHED_PROBABLE' END,
    s.`MatchScore`           = cc.`MaxScore`,
    s.`MatchedTransactionId` = c.`TransactionId`,
    s.`MatchEvidence`        = c.`Evidence`
WHERE s.`MatchStatus` = 'PENDING'
  AND cc.`CandidateCount` = 1
  AND cc.`MaxScore` >= 30;

-- 6d: Single candidate but score too low → UNMATCHED
UPDATE `broker_csv_staging` s
INNER JOIN `_csv_candidate_counts` cc ON cc.`StagingId` = s.`Id`
SET
    s.`MatchStatus`  = 'UNMATCHED',
    s.`MatchEvidence` = CONCAT('SingleCandidateButScoreTooLow=', cc.`MaxScore`)
WHERE s.`MatchStatus` = 'PENDING'
  AND cc.`CandidateCount` = 1
  AND cc.`MaxScore` < 30;

-- 6e: SKIPPED_ALREADY_FILLED – matched but all target fields already populated
UPDATE `broker_csv_staging` s
INNER JOIN `Transactions` t ON t.`Id` = s.`MatchedTransactionId`
SET
    s.`MatchStatus`  = 'SKIPPED_ALREADY_FILLED',
    s.`MatchEvidence` = CONCAT(s.`MatchEvidence`,
        '; AllFieldsFilled: Qty=', CAST(t.`Quantity` AS CHAR),
        ' Price=', CAST(t.`UnitPrice` AS CHAR),
        ' ISIN=', COALESCE(t.`InstrumentCode`, 'NULL'))
WHERE s.`MatchStatus` IN ('MATCHED_EXACT', 'MATCHED_PROBABLE')
  AND t.`Quantity`       IS NOT NULL
  AND t.`UnitPrice`      IS NOT NULL
  AND t.`InstrumentCode` IS NOT NULL
  AND t.`InstrumentCode` != '';

-- ---------------------------------------------------------------------------
-- STEP 7: Preview output
-- ---------------------------------------------------------------------------

-- Summary by status
SELECT
    `MatchStatus`                        AS `Status`,
    COUNT(*)                             AS `RowCount`
FROM `broker_csv_staging`
GROUP BY `MatchStatus`
ORDER BY FIELD(`MatchStatus`,
    'MATCHED_EXACT', 'MATCHED_PROBABLE',
    'SKIPPED_ALREADY_FILLED',
    'AMBIGUOUS', 'UNMATCHED',
    'CORPORATE_ACTION', 'CURRENCY_MISMATCH',
    'PARSE_ERROR', 'PENDING');

-- Detail: actionable rows (will be updated by apply script)
SELECT
    s.`Id`                               AS `StagingId`,
    s.`SourceFile`,
    s.`SourceRow`,
    s.`MatchStatus`,
    s.`MatchScore`,
    s.`TradeType`,
    s.`ISIN`,
    s.`Buchungstag`,
    s.`Valuta`,
    s.`Nominal`,
    s.`Kurs`,
    s.`Betrag`,
    s.`BetragCurrency`,
    s.`BrokerRef`,
    s.`TaNr`,
    -- Current transaction values (if matched)
    t.`Id`                               AS `TxId`,
    t.`Type`                             AS `TxType`,
    t.`InstrumentCode`                   AS `TxISIN`,
    t.`InstrumentCodeType`               AS `TxISINType`,
    t.`Quantity`                         AS `TxCurrentQty`,
    t.`UnitPrice`                        AS `TxCurrentPrice`,
    t.`Amount`                           AS `TxAmount`,
    LEFT(t.`Description`, 200)           AS `TxDescPreview`,
    s.`MatchEvidence`
FROM `broker_csv_staging` s
LEFT JOIN `Transactions` t ON t.`Id` = s.`MatchedTransactionId`
WHERE s.`MatchStatus` IN ('MATCHED_EXACT', 'MATCHED_PROBABLE')
ORDER BY s.`MatchStatus` DESC, s.`MatchScore` DESC, s.`Id`;

-- Detail: ambiguous rows (need operator review)
SELECT
    s.`Id`                               AS `StagingId`,
    s.`SourceFile`,
    s.`SourceRow`,
    s.`MatchStatus`,
    s.`MatchScore`,
    s.`TradeType`,
    s.`ISIN`,
    s.`Buchungstag`,
    s.`Valuta`,
    s.`BrokerRef`,
    s.`TaNr`,
    s.`MatchEvidence`,
    -- All candidate transaction IDs for this staging row
    GROUP_CONCAT(c.`TransactionId` ORDER BY c.`Score` DESC SEPARATOR ', ')
                                         AS `CandidateTxIds`,
    GROUP_CONCAT(c.`Score`          ORDER BY c.`Score` DESC SEPARATOR ', ')
                                         AS `CandidateScores`
FROM `broker_csv_staging` s
LEFT JOIN `_csv_candidates` c ON c.`StagingId` = s.`Id`
WHERE s.`MatchStatus` = 'AMBIGUOUS'
GROUP BY s.`Id`, s.`SourceFile`, s.`SourceRow`, s.`MatchStatus`, s.`MatchScore`,
         s.`TradeType`, s.`ISIN`, s.`Buchungstag`, s.`Valuta`,
         s.`BrokerRef`, s.`TaNr`, s.`MatchEvidence`
ORDER BY s.`Id`;

-- Detail: unmatched rows
SELECT
    s.`Id`                               AS `StagingId`,
    s.`SourceFile`,
    s.`SourceRow`,
    s.`TradeType`,
    s.`ISIN`,
    s.`Buchungstag`,
    s.`Valuta`,
    s.`BrokerRef`,
    s.`TaNr`,
    LEFT(s.`Buchungsinformation`, 150)   AS `Buchungsinformation`
FROM `broker_csv_staging`                AS s
WHERE `MatchStatus` = 'UNMATCHED'
ORDER BY s.`Buchungstag`, s.`ISIN`;

-- Detail: corporate actions
SELECT
    s.`Id`                               AS `StagingId`,
    s.`SourceFile`,
    s.`SourceRow`,
    s.`ISIN`,
    s.`Buchungstag`,
    s.`Nominal`,
    s.`CorporateActionHint`,
    LEFT(s.`Buchungsinformation`, 150)   AS `Buchungsinformation`
FROM `broker_csv_staging`                AS s
WHERE `MatchStatus` = 'CORPORATE_ACTION'
ORDER BY s.`Buchungstag`, s.`ISIN`;

-- Detail: currency mismatches
SELECT
    s.`Id`                               AS `StagingId`,
    s.`SourceFile`,
    s.`SourceRow`,
    s.`ISIN`,
    s.`Buchungstag`,
    s.`Betrag`,
    s.`BetragCurrency`,
    s.`Kurs`,
    s.`KursCurrency`,
    s.`Devisenkurs`,
    s.`MatchEvidence`
FROM `broker_csv_staging`                AS s
WHERE `MatchStatus` = 'CURRENCY_MISMATCH'
ORDER BY s.`Buchungstag`, s.`ISIN`;

-- Detail: parse errors
SELECT
    `Id`                                 AS `StagingId`,
    `SourceFile`,
    `SourceRow`,
    `ParseError`
FROM `broker_csv_staging`
WHERE `MatchStatus` = 'PARSE_ERROR'
ORDER BY `Id`;

-- ---------------------------------------------------------------------------
-- Cleanup
-- ---------------------------------------------------------------------------
DROP TEMPORARY TABLE IF EXISTS `_csv_candidates`;
DROP TEMPORARY TABLE IF EXISTS `_csv_candidate_counts`;

SELECT 'PREVIEW COMPLETE – no Transactions rows were modified.' AS notice;
