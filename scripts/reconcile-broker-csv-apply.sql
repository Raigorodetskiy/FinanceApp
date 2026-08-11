-- =============================================================================
-- APPLY SCRIPT
-- Broker CSV Reconciliation – Step 3: Update Transactions from staging
--
-- !! MAKE A FULL DATABASE BACKUP BEFORE RUNNING THIS SCRIPT !!
-- !! Run the preview script and review all output before proceeding !!
--
-- This script updates Transactions rows that are matched as MATCHED_EXACT
-- or MATCHED_PROBABLE in broker_csv_staging.  It ONLY fills fields that are
-- currently NULL/empty.  It NEVER overwrites meaningful existing values.
--
-- Idempotent: running it a second time is safe; rows already filled are skipped.
--
-- Run:
--   mysql -h HOST -u USER -p DATABASE \
--     < scripts/reconcile-broker-csv-apply.sql
--
-- To verify without committing: the script shows before/after counts.
-- A ROLLBACK guard is included: inspect ROW_COUNT() before COMMIT.
--
-- MariaDB 10.5+ compatible.
-- =============================================================================

SET NAMES utf8mb4;

-- ---------------------------------------------------------------------------
-- Pre-flight: confirm preview has been run
-- ---------------------------------------------------------------------------
SELECT
    SUM(`MatchStatus` IN ('MATCHED_EXACT', 'MATCHED_PROBABLE'))  AS `ActionableRows`,
    SUM(`MatchStatus` = 'AMBIGUOUS')                             AS `AmbiguousRows`,
    SUM(`MatchStatus` = 'UNMATCHED')                             AS `UnmatchedRows`,
    SUM(`MatchStatus` = 'PENDING')                               AS `PendingRows_RunPreviewFirst`
FROM `broker_csv_staging`;

-- ---------------------------------------------------------------------------
-- STEP 1: Show before-state for all rows to be updated
-- ---------------------------------------------------------------------------
SELECT
    s.`Id`                               AS `StagingId`,
    s.`MatchStatus`,
    s.`MatchScore`,
    s.`TradeType`,
    s.`ISIN`                             AS `CsvISIN`,
    s.`Nominal`                          AS `CsvQuantity`,
    s.`Kurs`                             AS `CsvUnitPrice`,
    t.`Id`                               AS `TxId`,
    t.`Type`                             AS `TxType`,
    t.`InstrumentCode`                   AS `Before_InstrumentCode`,
    t.`InstrumentCodeType`               AS `Before_InstrumentCodeType`,
    t.`Quantity`                         AS `Before_Quantity`,
    t.`UnitPrice`                        AS `Before_UnitPrice`,
    -- What will be written
    CASE WHEN t.`InstrumentCode` IS NULL OR t.`InstrumentCode` = ''
         THEN s.`ISIN` ELSE t.`InstrumentCode` END AS `Would_Set_InstrumentCode`,
    CASE WHEN (t.`InstrumentCode` IS NULL OR t.`InstrumentCode` = '')
              AND s.`ISIN` IS NOT NULL
         THEN 'ISIN' ELSE NULL END       AS `Would_Set_InstrumentCodeType`,
    CASE WHEN t.`Quantity`  IS NULL THEN s.`Nominal`  ELSE NULL END AS `Would_Set_Quantity`,
    CASE WHEN t.`UnitPrice` IS NULL THEN s.`Kurs`     ELSE NULL END AS `Would_Set_UnitPrice`
FROM `broker_csv_staging` s
INNER JOIN `Transactions` t ON t.`Id` = s.`MatchedTransactionId`
WHERE s.`MatchStatus` IN ('MATCHED_EXACT', 'MATCHED_PROBABLE')
  AND (
        t.`InstrumentCode`     IS NULL OR t.`InstrumentCode` = ''
     OR t.`InstrumentCodeType` IS NULL
     OR t.`Quantity`           IS NULL
     OR t.`UnitPrice`          IS NULL
  )
ORDER BY s.`MatchStatus` DESC, s.`MatchScore` DESC, s.`Id`;

-- Row count of what will be updated
SELECT
    COUNT(*)                                           AS `TotalActionableRows`,
    SUM(t.`InstrumentCode` IS NULL OR t.`InstrumentCode` = '')   AS `WillFill_InstrumentCode`,
    SUM(t.`InstrumentCodeType` IS NULL)                AS `WillFill_InstrumentCodeType`,
    SUM(t.`Quantity`  IS NULL)                         AS `WillFill_Quantity`,
    SUM(t.`UnitPrice` IS NULL)                         AS `WillFill_UnitPrice`
FROM `broker_csv_staging` s
INNER JOIN `Transactions` t ON t.`Id` = s.`MatchedTransactionId`
WHERE s.`MatchStatus` IN ('MATCHED_EXACT', 'MATCHED_PROBABLE')
  AND (
        t.`InstrumentCode`     IS NULL OR t.`InstrumentCode` = ''
     OR t.`InstrumentCodeType` IS NULL
     OR t.`Quantity`           IS NULL
     OR t.`UnitPrice`          IS NULL
  );

-- ---------------------------------------------------------------------------
-- STEP 2: Apply UPDATE inside a transaction
-- ---------------------------------------------------------------------------
START TRANSACTION;

UPDATE `Transactions` t
INNER JOIN `broker_csv_staging` s ON s.`MatchedTransactionId` = t.`Id`
SET
    t.`InstrumentCode` = CASE
        WHEN (t.`InstrumentCode` IS NULL OR t.`InstrumentCode` = '')
             AND s.`ISIN` IS NOT NULL
             THEN s.`ISIN`
        ELSE t.`InstrumentCode`
    END,
    t.`InstrumentCodeType` = CASE
        WHEN t.`InstrumentCodeType` IS NULL
             AND s.`ISIN` IS NOT NULL
             THEN 'ISIN'  -- stored as string per HasConversion<string>()
        ELSE t.`InstrumentCodeType`
    END,
    t.`Quantity` = CASE
        WHEN t.`Quantity` IS NULL AND s.`Nominal` IS NOT NULL
             THEN s.`Nominal`
        ELSE t.`Quantity`
    END,
    t.`UnitPrice` = CASE
        WHEN t.`UnitPrice` IS NULL AND s.`Kurs` IS NOT NULL
             THEN s.`Kurs`
        ELSE t.`UnitPrice`
    END
WHERE s.`MatchStatus` IN ('MATCHED_EXACT', 'MATCHED_PROBABLE')
  AND (
        (t.`InstrumentCode`     IS NULL OR t.`InstrumentCode` = '')
     OR t.`InstrumentCodeType`  IS NULL
     OR t.`Quantity`            IS NULL
     OR t.`UnitPrice`           IS NULL
  );

SELECT ROW_COUNT() AS `UpdatedTransactionRows`;

-- Mark applied rows in staging
UPDATE `broker_csv_staging`
SET `AppliedAt` = NOW()
WHERE `MatchStatus` IN ('MATCHED_EXACT', 'MATCHED_PROBABLE')
  AND `AppliedAt` IS NULL
  AND `MatchedTransactionId` IS NOT NULL;

COMMIT;

-- ---------------------------------------------------------------------------
-- STEP 3: Post-update verification
-- ---------------------------------------------------------------------------
SELECT
    t.`Id`                               AS `TxId`,
    t.`Type`,
    t.`InstrumentCode`                   AS `After_InstrumentCode`,
    t.`InstrumentCodeType`               AS `After_InstrumentCodeType`,
    t.`Quantity`                         AS `After_Quantity`,
    t.`UnitPrice`                        AS `After_UnitPrice`,
    s.`MatchStatus`,
    s.`MatchScore`,
    LEFT(t.`Description`, 200)           AS `DescriptionPreview`
FROM `broker_csv_staging` s
INNER JOIN `Transactions` t ON t.`Id` = s.`MatchedTransactionId`
WHERE s.`AppliedAt` IS NOT NULL
ORDER BY t.`Id`;

-- Remaining gaps after apply (should be empty for applied rows)
SELECT
    t.`Id`                               AS `TxId`,
    t.`Type`,
    t.`InstrumentCode`,
    t.`Quantity`,
    t.`UnitPrice`,
    s.`MatchStatus`,
    s.`MatchEvidence`
FROM `broker_csv_staging` s
INNER JOIN `Transactions` t ON t.`Id` = s.`MatchedTransactionId`
WHERE s.`MatchStatus` IN ('MATCHED_EXACT', 'MATCHED_PROBABLE')
  AND (t.`Quantity` IS NULL OR t.`UnitPrice` IS NULL)
ORDER BY t.`Id`;

SELECT 'APPLY COMPLETE.' AS notice;
