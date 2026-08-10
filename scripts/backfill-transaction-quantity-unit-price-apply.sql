-- =============================================================================
-- APPLY SCRIPT
-- Backfill Transactions.Quantity and Transactions.UnitPrice
-- from German broker text stored in Transactions.Description
--
-- !! MAKE A FULL DATABASE BACKUP BEFORE RUNNING THIS SCRIPT !!
-- !! Run the dry-run script first and review the preview output !!
--
--   Dry-run:
--     mysql -h HOST -u USER -p DATABASE \
--       < scripts/backfill-transaction-quantity-unit-price.sql
--
--   Apply:
--     mysql -h HOST -u USER -p DATABASE \
--       < scripts/backfill-transaction-quantity-unit-price-apply.sql
--
-- WARNING: Do NOT embed passwords on the command line.
-- =============================================================================

SET NAMES utf8mb4;

-- ---------------------------------------------------------------------------
-- STEP 1: Build staging table (same logic as dry-run)
-- ---------------------------------------------------------------------------
DROP TEMPORARY TABLE IF EXISTS `_txn_backfill_staging`;

CREATE TEMPORARY TABLE `_txn_backfill_staging` (
    `TransactionId`     INT            NOT NULL PRIMARY KEY,
    `RawDescription`    TEXT           NULL,
    `NormDesc`          TEXT           NULL,
    `QuantityRaw`       VARCHAR(40)    NULL,
    `UnitPriceRaw`      VARCHAR(40)    NULL,
    `ParsedQuantity`    DECIMAL(18,8)  NULL,
    `ParsedUnitPrice`   DECIMAL(18,8)  NULL
);

INSERT INTO `_txn_backfill_staging`
    (`TransactionId`, `RawDescription`, `NormDesc`)
SELECT
    `Id`,
    `Description`,
    REGEXP_REPLACE(
        REPLACE(
            REPLACE(
                REPLACE(
                    REPLACE(`Description`, '\r', ' '),
                '\n', ' '),
            '\t', ' '),
        _utf8mb4 '\u00a0', ' '),
    '[[:space:]]+', ' ')
FROM `Transactions`
WHERE `Description` IS NOT NULL
  AND (
        (`Quantity`   IS NULL AND `Description` REGEXP '(?i)nominal[[:space:]]+[0-9]')
     OR (`UnitPrice`  IS NULL AND `Description` REGEXP '(?i)kurs[[:space:]]+[0-9]')
  );

UPDATE `_txn_backfill_staging`
SET
    `QuantityRaw` = TRIM(
        REGEXP_REPLACE(
            REGEXP_REPLACE(
                REGEXP_SUBSTR(`NormDesc`,
                    '(?i)nominal[[:space:]]+[0-9][0-9.]*(?:,[0-9]+)?[[:space:]]+St[uü]ck'),
                '(?i)^nominal[[:space:]]+', ''),
            '(?i)[[:space:]]+St[uü]ck$', '')
    ),
    `UnitPriceRaw` = TRIM(
        REGEXP_REPLACE(
            REGEXP_REPLACE(
                REGEXP_SUBSTR(`NormDesc`,
                    '(?i)kurs[[:space:]]+[0-9][0-9.]*(?:,[0-9]+)?[[:space:]]*€'),
                '(?i)^kurs[[:space:]]+', ''),
            '[[:space:]]*€$', '')
    );

UPDATE `_txn_backfill_staging`
SET
    `ParsedQuantity` = CASE
        WHEN `QuantityRaw` IS NULL OR TRIM(`QuantityRaw`) = '' THEN NULL
        WHEN REGEXP_REPLACE(REPLACE(REPLACE(TRIM(`QuantityRaw`), '.', ''), ',', '.'), '[0-9.]', '') <> '' THEN NULL
        WHEN CAST(REPLACE(REPLACE(TRIM(`QuantityRaw`), '.', ''), ',', '.') AS DECIMAL(18,8)) <= 0 THEN NULL
        ELSE CAST(REPLACE(REPLACE(TRIM(`QuantityRaw`), '.', ''), ',', '.') AS DECIMAL(18,8))
    END,
    `ParsedUnitPrice` = CASE
        WHEN `UnitPriceRaw` IS NULL OR TRIM(`UnitPriceRaw`) = '' THEN NULL
        WHEN REGEXP_REPLACE(REPLACE(REPLACE(TRIM(`UnitPriceRaw`), '.', ''), ',', '.'), '[0-9.]', '') <> '' THEN NULL
        WHEN CAST(REPLACE(REPLACE(TRIM(`UnitPriceRaw`), '.', ''), ',', '.') AS DECIMAL(18,8)) <= 0 THEN NULL
        ELSE CAST(REPLACE(REPLACE(TRIM(`UnitPriceRaw`), '.', ''), ',', '.') AS DECIMAL(18,8))
    END;

DELETE FROM `_txn_backfill_staging`
WHERE `ParsedQuantity` IS NULL AND `ParsedUnitPrice` IS NULL;

DELETE FROM `_txn_backfill_staging` s
WHERE s.`TransactionId` IN (
    SELECT `Id` FROM `Transactions`
    WHERE `Id` = s.`TransactionId`
      AND `Quantity` IS NOT NULL
      AND `UnitPrice` IS NOT NULL
);

-- ---------------------------------------------------------------------------
-- STEP 2: Pre-update preview (same as dry-run)
-- ---------------------------------------------------------------------------
SELECT
    s.`TransactionId`,
    t.`Type`                                        AS `TxType`,
    t.`Quantity`                                    AS `CurrentQuantity`,
    t.`UnitPrice`                                   AS `CurrentUnitPrice`,
    s.`QuantityRaw`,
    s.`UnitPriceRaw`,
    s.`ParsedQuantity`,
    s.`ParsedUnitPrice`,
    LEFT(s.`RawDescription`, 300)                   AS `DescriptionPreview`
FROM `_txn_backfill_staging` s
INNER JOIN `Transactions` t ON t.`Id` = s.`TransactionId`
ORDER BY s.`TransactionId`;

SELECT
    COUNT(*)                                        AS `MatchedRows`,
    SUM(s.`ParsedQuantity`  IS NOT NULL
        AND t.`Quantity`    IS NULL)                AS `WillFillQuantity`,
    SUM(s.`ParsedUnitPrice` IS NOT NULL
        AND t.`UnitPrice`   IS NULL)                AS `WillFillUnitPrice`
FROM `_txn_backfill_staging` s
INNER JOIN `Transactions` t ON t.`Id` = s.`TransactionId`;

-- ---------------------------------------------------------------------------
-- STEP 3: Apply UPDATE inside a transaction
-- ---------------------------------------------------------------------------
START TRANSACTION;

UPDATE `Transactions` t
INNER JOIN `_txn_backfill_staging` s ON s.`TransactionId` = t.`Id`
SET
    t.`Quantity`  = CASE
        WHEN t.`Quantity`  IS NULL AND s.`ParsedQuantity`  IS NOT NULL
            THEN s.`ParsedQuantity`
        ELSE t.`Quantity`
    END,
    t.`UnitPrice` = CASE
        WHEN t.`UnitPrice` IS NULL AND s.`ParsedUnitPrice` IS NOT NULL
            THEN s.`ParsedUnitPrice`
        ELSE t.`UnitPrice`
    END
WHERE
    (t.`Quantity`  IS NULL AND s.`ParsedQuantity`  IS NOT NULL)
 OR (t.`UnitPrice` IS NULL AND s.`ParsedUnitPrice` IS NOT NULL);

SELECT ROW_COUNT() AS `UpdatedRows`;

COMMIT;

-- ---------------------------------------------------------------------------
-- STEP 4: Post-update verification
-- ---------------------------------------------------------------------------
SELECT
    t.`Id`,
    t.`Type`,
    t.`Quantity`,
    t.`UnitPrice`,
    LEFT(t.`Description`, 300) AS `Description`
FROM `Transactions` t
INNER JOIN `_txn_backfill_staging` s ON s.`TransactionId` = t.`Id`
ORDER BY t.`Id`;

-- ---------------------------------------------------------------------------
-- STEP 5: Cleanup
-- ---------------------------------------------------------------------------
DROP TEMPORARY TABLE IF EXISTS `_txn_backfill_staging`;

SELECT 'APPLY COMPLETE.' AS notice;
