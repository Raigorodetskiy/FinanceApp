-- =============================================================================
-- DRY-RUN / PREVIEW SCRIPT
-- Backfill Transactions.Quantity and Transactions.UnitPrice
-- from German broker text stored in Transactions.Description
--
-- !! MAKE A FULL DATABASE BACKUP BEFORE PROCEEDING !!
-- !! Review the SELECT output carefully before running the apply script !!
--
-- Supported description format (Flatex German broker):
--   Buchtag 10.08.2026 Valuta 12.08.2026 ISIN US67066G1040
--   Bezeichnung NVIDIA CORP. Nominal 10,000 Stück
--   Betrag 1.947,40 € Kurs 194,74 € Devisenk.1,000
--   TA.-Nr. 5154283314 ...
--
-- Expected extraction for NVIDIA example:
--   Quantity  = 10.00000000   (from "Nominal 10,000 Stück")
--   UnitPrice = 194.74000000  (from "Kurs 194,74 €")
--   "Betrag 1.947,40 €" is intentionally ignored (total amount, not unit price)
--
-- Rules:
--   - Only fills columns that are currently NULL
--   - Fills one field independently when only that field can be parsed
--   - Malformed numbers become NULL, not zero
--   - Rejects negative values
--   - Compatible with MariaDB 10.5+
--
-- USAGE (dry-run — no data is changed):
--   mysql -h HOST -u USER -p DATABASE < scripts/backfill-transaction-quantity-unit-price.sql
--
-- APPLY:
--   mysql -h HOST -u USER -p DATABASE < scripts/backfill-transaction-quantity-unit-price-apply.sql
--
-- WARNING: Do NOT embed passwords on the command line (use -p and type at the prompt
--          or use MYSQL_PWD env var only in controlled environments).
-- =============================================================================

SET NAMES utf8mb4;

-- ---------------------------------------------------------------------------
-- STEP 1: Build staging table with parsed values
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

-- ---------------------------------------------------------------------------
-- Helper: normalise description
--   • Replace CR, LF, tab with a single space
--   • Collapse repeated whitespace (via REGEXP_REPLACE)
--   • Replace UTF-8 non-breaking space (U+00A0, encoded as 0xC2A0 in utf8mb4)
-- ---------------------------------------------------------------------------
INSERT INTO `_txn_backfill_staging`
    (`TransactionId`, `RawDescription`, `NormDesc`)
SELECT
    `Id`,
    `Description`,
    -- Collapse whitespace including NBSP (\xc2\xa0)
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

-- ---------------------------------------------------------------------------
-- STEP 2: Extract raw number strings from normalised description
-- ---------------------------------------------------------------------------
-- Quantity: "Nominal <number> Stück"  (case-insensitive, German decimal)
-- UnitPrice: "Kurs <number> €"        (case-insensitive, German decimal)
-- Anchored strictly so we do NOT capture Betrag, Devisenk., dates, ISINs.
-- Number pattern: optional thousands (digits with optional . separators)
--                 followed by optional , decimal part
-- ---------------------------------------------------------------------------
UPDATE `_txn_backfill_staging`
SET
    `QuantityRaw` = TRIM(
        -- Remove the leading "Nominal " (any case) prefix and trailing " Stück" suffix
        REGEXP_REPLACE(
            REGEXP_REPLACE(
                -- Extract "Nominal <number> Stück" segment
                REGEXP_SUBSTR(`NormDesc`,
                    '(?i)nominal[[:space:]]+[0-9][0-9.]*(?:,[0-9]+)?[[:space:]]+St[uü]ck'),
                '(?i)^nominal[[:space:]]+', ''),
            '(?i)[[:space:]]+St[uü]ck$', '')
    ),
    `UnitPriceRaw` = TRIM(
        REGEXP_REPLACE(
            REGEXP_REPLACE(
                -- Extract "Kurs <number> €" segment
                REGEXP_SUBSTR(`NormDesc`,
                    '(?i)kurs[[:space:]]+[0-9][0-9.]*(?:,[0-9]+)?[[:space:]]*€'),
                '(?i)^kurs[[:space:]]+', ''),
            '[[:space:]]*€$', '')
    );

-- ---------------------------------------------------------------------------
-- STEP 3: Convert German-formatted numbers to DECIMAL(18,8)
--   German format: thousands separator = '.'  decimal separator = ','
--   Conversion: strip '.', replace ',' with '.', then CAST
--   Guard: CAST('') = 0 in MySQL/MariaDB, so we use NULLIF on empty string
--           and reject negative results as a safety measure.
-- ---------------------------------------------------------------------------
UPDATE `_txn_backfill_staging`
SET
    `ParsedQuantity` = CASE
        WHEN `QuantityRaw` IS NULL OR TRIM(`QuantityRaw`) = '' THEN NULL
        -- Reject if the value looks malformed (contains letters after stripping separators)
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

-- Remove rows where nothing could be parsed and columns are already filled
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
-- STEP 4: PREVIEW — inspect before applying
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

-- ---------------------------------------------------------------------------
-- STEP 5: Aggregate counts
-- ---------------------------------------------------------------------------
SELECT
    COUNT(*)                                        AS `MatchedRows`,
    SUM(s.`ParsedQuantity`  IS NOT NULL
        AND t.`Quantity`    IS NULL)                AS `WillFillQuantity`,
    SUM(s.`ParsedUnitPrice` IS NOT NULL
        AND t.`UnitPrice`   IS NULL)                AS `WillFillUnitPrice`
FROM `_txn_backfill_staging` s
INNER JOIN `Transactions` t ON t.`Id` = s.`TransactionId`;

-- ---------------------------------------------------------------------------
-- STEP 6: Cleanup
-- ---------------------------------------------------------------------------
DROP TEMPORARY TABLE IF EXISTS `_txn_backfill_staging`;

SELECT 'DRY-RUN COMPLETE. No data was changed. Review the preview above, then run backfill-transaction-quantity-unit-price-apply.sql to apply.' AS notice;
