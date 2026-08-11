-- =============================================================================
-- READ-ONLY TRANSACTION COST / COMMISSION PREVIEW
-- Broker CSV Reconciliation – Step 4 (optional)
--
-- Produces an estimate of transaction costs for each matched ordinary trade.
-- This script NEVER writes to Transactions.  Values are labelled
-- TotalCostDifference (not guaranteed Commission) because the difference
-- between the broker's gross trade amount and the bank booking amount may
-- include taxes, external fees, rounding, or FX conversion.
--
-- FORMULA:
--   CsvGrossAmount  = ABS(CSV Betrag)   ← preferred over Quantity × UnitPrice
--   For EUR Buy  : TotalCostDifference = ABS(DatabaseAmount) − CsvGrossAmount
--   For EUR Sell : TotalCostDifference = CsvGrossAmount − ABS(DatabaseAmount)
--
-- Flags:
--   NegativeDifference     – unexpected direction (refund / data issue)
--   ImplausiblyLarge       – difference > 5% of CsvGrossAmount
--   FxAffected             – Devisenkurs != 1 (non-EUR trade)
--   RoundingOnlyCandidate  – small difference explainable by price rounding
--     (e.g. 30 × 33.28 = 998.40 vs Betrag 998.55; diff 0.15 ≤ Qty × 0.01)
--
-- !! This script is purely informational; review carefully before drawing
--    any commission conclusions !!
--
-- Run:
--   mysql -h HOST -u USER -p DATABASE \
--     < scripts/reconcile-broker-csv-costs-preview.sql
--
-- MariaDB 10.5+ compatible.
-- =============================================================================

SET NAMES utf8mb4;

SELECT
    -- Identifiers
    s.`Id`                                       AS `StagingId`,
    t.`Id`                                       AS `TxId`,
    s.`SourceFile`,
    s.`SourceRow`,
    s.`MatchStatus`,
    s.`MatchScore`,

    -- Trade details
    s.`TradeType`,
    s.`ISIN`,
    s.`Buchungstag`                              AS `CsvBuchungstag`,
    s.`Valuta`                                   AS `CsvValuta`,

    -- Quantity & price
    s.`Nominal`                                  AS `CsvQuantity`,
    s.`Kurs`                                     AS `CsvUnitPrice`,
    s.`BetragCurrency`                           AS `CsvCurrency`,
    s.`KursCurrency`                             AS `CsvPriceCurrency`,
    s.`Devisenkurs`                              AS `CsvFxRate`,

    -- CSV gross amount (preferred baseline; avoids rounding in displayed unit price)
    s.`Betrag`                                   AS `CsvGrossAmount`,

    -- Computed gross from Qty × Price (for comparison/sanity check)
    CASE
        WHEN s.`Nominal` IS NOT NULL AND s.`Kurs` IS NOT NULL
             THEN ROUND(s.`Nominal` * s.`Kurs`, 2)
        ELSE NULL
    END                                          AS `ComputedGross_QtyXPrice`,

    -- Difference between Betrag and Qty × Price (rounding indicator)
    CASE
        WHEN s.`Nominal` IS NOT NULL AND s.`Kurs` IS NOT NULL AND s.`Betrag` IS NOT NULL
             THEN ROUND(s.`Betrag` - s.`Nominal` * s.`Kurs`, 4)
        ELSE NULL
    END                                          AS `BetragVsQtyXPriceDiff`,

    -- Database transaction values
    t.`Amount`                                   AS `TxAmount`,
    t.`InstrumentCode`                           AS `TxISIN`,
    t.`Quantity`                                 AS `TxQuantity`,
    t.`UnitPrice`                                AS `TxUnitPrice`,

    -- Core cost calculation (EUR only)
    CASE
        WHEN s.`BetragCurrency` != 'EUR' OR s.`KursCurrency` != 'EUR'
             THEN NULL   -- FX rows handled separately
        WHEN s.`TradeType` = 'Buy'
             THEN ROUND(ABS(t.`Amount`) - s.`Betrag`, 2)
        WHEN s.`TradeType` = 'Sell'
             THEN ROUND(s.`Betrag` - ABS(t.`Amount`), 2)
        ELSE NULL
    END                                          AS `TotalCostDifference`,

    -- Currency flags
    CASE WHEN s.`BetragCurrency` != 'EUR' OR s.`KursCurrency` != 'EUR'
         THEN 'YES' ELSE 'NO'
    END                                          AS `FxAffected`,

    -- For FX rows: show FX-converted estimate only (informational)
    CASE
        WHEN (s.`BetragCurrency` != 'EUR' OR s.`KursCurrency` != 'EUR')
             AND s.`Devisenkurs` IS NOT NULL AND s.`Devisenkurs` != 0
             AND s.`Betrag` IS NOT NULL
             THEN ROUND(s.`Betrag` / s.`Devisenkurs`, 2)
        ELSE NULL
    END                                          AS `CsvGrossInEUR_Estimate`,

    CASE
        WHEN (s.`BetragCurrency` != 'EUR' OR s.`KursCurrency` != 'EUR')
             AND s.`Devisenkurs` IS NOT NULL AND s.`Devisenkurs` != 0
             AND s.`Betrag` IS NOT NULL
             AND s.`TradeType` = 'Buy'
             THEN ROUND(ABS(t.`Amount`) - ROUND(s.`Betrag` / s.`Devisenkurs`, 2), 2)
        WHEN (s.`BetragCurrency` != 'EUR' OR s.`KursCurrency` != 'EUR')
             AND s.`Devisenkurs` IS NOT NULL AND s.`Devisenkurs` != 0
             AND s.`Betrag` IS NOT NULL
             AND s.`TradeType` = 'Sell'
             THEN ROUND(ROUND(s.`Betrag` / s.`Devisenkurs`, 2) - ABS(t.`Amount`), 2)
        ELSE NULL
    END                                          AS `TotalCostDifference_FxConverted`,

    -- Flags
    CASE
        WHEN s.`BetragCurrency` = 'EUR' AND s.`KursCurrency` = 'EUR'
             AND (
                  (s.`TradeType` = 'Buy'  AND ROUND(ABS(t.`Amount`) - s.`Betrag`, 2) < 0)
               OR (s.`TradeType` = 'Sell' AND ROUND(s.`Betrag` - ABS(t.`Amount`), 2) < 0)
             )
             THEN 'YES' ELSE 'NO'
    END                                          AS `NegativeDifference`,

    CASE
        WHEN s.`BetragCurrency` = 'EUR' AND s.`KursCurrency` = 'EUR'
             AND s.`Betrag` > 0
             AND (
                  (s.`TradeType` = 'Buy'
                   AND ABS(ROUND(ABS(t.`Amount`) - s.`Betrag`, 2)) / s.`Betrag` > 0.05)
               OR (s.`TradeType` = 'Sell'
                   AND ABS(ROUND(s.`Betrag` - ABS(t.`Amount`), 2)) / s.`Betrag` > 0.05)
             )
             THEN 'YES' ELSE 'NO'
    END                                          AS `ImplausiblyLarge`,

    -- Rounding-only candidate: diff <= Quantity × 0.01 (1 cent per share)
    CASE
        WHEN s.`BetragCurrency` = 'EUR' AND s.`KursCurrency` = 'EUR'
             AND s.`Nominal` IS NOT NULL AND s.`Nominal` > 0
             AND ABS(
                  CASE WHEN s.`TradeType` = 'Buy'
                       THEN ROUND(ABS(t.`Amount`) - s.`Betrag`, 4)
                       ELSE ROUND(s.`Betrag` - ABS(t.`Amount`), 4)
                  END
             ) <= s.`Nominal` * 0.01
             THEN 'YES' ELSE 'NO'
    END                                          AS `RoundingOnlyCandidate`,

    s.`MatchEvidence`

FROM `broker_csv_staging` s
INNER JOIN `Transactions` t ON t.`Id` = s.`MatchedTransactionId`
WHERE s.`MatchStatus` IN ('MATCHED_EXACT', 'MATCHED_PROBABLE')
  AND s.`TradeType` IN ('Buy', 'Sell')
ORDER BY s.`TradeType`, s.`ISIN`, s.`Buchungstag`;

-- Summary statistics (EUR trades only)
SELECT
    s.`TradeType`,
    COUNT(*)                                                    AS `Rows`,
    SUM(CASE WHEN s.`BetragCurrency` = 'EUR' THEN 1 ELSE 0 END) AS `EurRows`,
    ROUND(SUM(
        CASE
            WHEN s.`BetragCurrency` = 'EUR' AND s.`KursCurrency` = 'EUR'
                 AND s.`TradeType` = 'Buy'
                 THEN ROUND(ABS(t.`Amount`) - s.`Betrag`, 2)
            WHEN s.`BetragCurrency` = 'EUR' AND s.`KursCurrency` = 'EUR'
                 AND s.`TradeType` = 'Sell'
                 THEN ROUND(s.`Betrag` - ABS(t.`Amount`), 2)
            ELSE 0
        END
    ), 2)                                                       AS `TotalCostDifference_EUR`,
    ROUND(AVG(
        CASE
            WHEN s.`BetragCurrency` = 'EUR' AND s.`KursCurrency` = 'EUR'
                 AND s.`TradeType` = 'Buy'
                 THEN ROUND(ABS(t.`Amount`) - s.`Betrag`, 2)
            WHEN s.`BetragCurrency` = 'EUR' AND s.`KursCurrency` = 'EUR'
                 AND s.`TradeType` = 'Sell'
                 THEN ROUND(s.`Betrag` - ABS(t.`Amount`), 2)
            ELSE NULL
        END
    ), 2)                                                       AS `AvgCostDifference_EUR`,
    SUM(
        CASE
            WHEN s.`BetragCurrency` = 'EUR' AND s.`KursCurrency` = 'EUR'
                 AND (
                      (s.`TradeType` = 'Buy'  AND ROUND(ABS(t.`Amount`) - s.`Betrag`, 2) < 0)
                   OR (s.`TradeType` = 'Sell' AND ROUND(s.`Betrag` - ABS(t.`Amount`), 2) < 0)
                 )
            THEN 1 ELSE 0
        END
    )                                                           AS `NegativeDifferenceCount`
FROM `broker_csv_staging` s
INNER JOIN `Transactions` t ON t.`Id` = s.`MatchedTransactionId`
WHERE s.`MatchStatus` IN ('MATCHED_EXACT', 'MATCHED_PROBABLE')
  AND s.`TradeType` IN ('Buy', 'Sell')
GROUP BY s.`TradeType`;

SELECT 'COST PREVIEW COMPLETE – no Transactions rows were modified.' AS notice;
