-- =============================================================================
-- PARSING VALIDATION FIXTURES
-- Validates the regex/conversion logic used in the backfill scripts
-- against representative inputs (no live Transactions table required).
--
-- Run standalone:
--   mysql -h HOST -u USER -p DATABASE \
--     < scripts/backfill-transaction-quantity-unit-price-fixtures.sql
--
-- Expected: every row in the final SELECT should show PASS in `Result`.
-- =============================================================================

SET NAMES utf8mb4;

DROP TEMPORARY TABLE IF EXISTS `_fixture_cases`;

CREATE TEMPORARY TABLE `_fixture_cases` (
    `CaseId`          INT           NOT NULL PRIMARY KEY AUTO_INCREMENT,
    `Label`           VARCHAR(120)  NOT NULL,
    `Description`     TEXT          NOT NULL,
    -- pre-existing values (NULL = not yet filled)
    `ExistingQty`     DECIMAL(18,8) NULL,
    `ExistingPrice`   DECIMAL(18,8) NULL,
    -- expected extracted strings (NULL means nothing should be extracted)
    `ExpectQtyRaw`    VARCHAR(40)   NULL,
    `ExpectPriceRaw`  VARCHAR(40)   NULL,
    -- expected final values after fill (NULL means column stays NULL)
    `ExpectQty`       DECIMAL(18,8) NULL,
    `ExpectPrice`     DECIMAL(18,8) NULL
);

-- ---------------------------------------------------------------------------
-- Test cases
-- ---------------------------------------------------------------------------
INSERT INTO `_fixture_cases`
    (`Label`, `Description`, `ExistingQty`, `ExistingPrice`, `ExpectQtyRaw`, `ExpectPriceRaw`, `ExpectQty`, `ExpectPrice`)
VALUES
-- 1. NVIDIA example from the problem statement
('nvidia_full',
 'Buchtag 10.08.2026 Valuta 12.08.2026 ISIN US67066G1040 Bezeichnung NVIDIA CORP. Nominal 10,000 Stück Betrag 1.947,40 € Kurs 194,74 € Devisenk.1,000 TA.-Nr. 5154283314 Buchungsinformationen Ausführung ORDER Kauf US67066G1040 315730363',
 NULL, NULL, '10,000', '194,74', 10.00000000, 194.74000000),

-- 2. German thousands separator in quantity (e.g. 1.250 Stück = 1250)
('german_thousands_qty',
 'Nominal 1.250,500 Stück Kurs 50,25 €',
 NULL, NULL, '1.250,500', '50,25', 1250.50000000, 50.25000000),

-- 3. Newlines and non-breaking spaces (NBSP via CHAR(0xc2, 0xa0))
('nbsp_newlines',
 CONCAT('Nominal', _utf8mb4 '\u00a0', '5,000 St', _utf8mb4 '\u00fc', 'ck\nKurs 99,99 €'),
 NULL, NULL, '5,000', '99,99', 5.00000000, 99.99000000),

-- 4. Existing non-null Quantity must NOT be overwritten
('existing_qty_preserved',
 'Nominal 7,000 Stück Kurs 10,00 €',
 7.00000000, NULL, '7,000', '10,00', 7.00000000, 10.00000000),

-- 5. Quantity present, price absent from description
('qty_only',
 'Nominal 3,000 Stück Transaktionsnummer 12345',
 NULL, NULL, '3,000', NULL, 3.00000000, NULL),

-- 6. Price present, quantity absent from description
('price_only',
 'Kurs 75,50 € Buchung erfolgt',
 NULL, NULL, NULL, '75,50', NULL, 75.50000000),

-- 7. Malformed quantity string → NULL, not zero
('malformed_qty',
 'Nominal abc Stück Kurs 10,00 €',
 NULL, NULL, NULL, '10,00', NULL, 10.00000000),

-- 8. Betrag must NOT be captured as unit price
('betrag_not_price',
 'Betrag 1.947,40 € Kurs 194,74 €',
 NULL, NULL, NULL, '194,74', NULL, 194.74000000),

-- 9. Case-insensitive labels
('case_insensitive',
 'nominal 2,000 stück kurs 25,00 €',
 NULL, NULL, '2,000', '25,00', 2.00000000, 25.00000000),

-- 10. Both columns already filled — nothing should change
('both_already_filled',
 'Nominal 5,000 Stück Kurs 10,00 €',
 5.00000000, 10.00000000, '5,000', '10,00', 5.00000000, 10.00000000),

-- 11. Devisenk. must not be captured as unit price
('devisenkurs_not_price',
 'Nominal 4,000 Stück Betrag 800,00 € Kurs 200,00 € Devisenk.1,050',
 NULL, NULL, '4,000', '200,00', 4.00000000, 200.00000000),

-- 12. ISIN digits in description must not be captured as quantity/price
('isin_not_captured',
 'ISIN US67066G1040 Nominal 10,000 Stück Kurs 194,74 €',
 NULL, NULL, '10,000', '194,74', 10.00000000, 194.74000000);

-- ---------------------------------------------------------------------------
-- Compute extracted values and final results
-- ---------------------------------------------------------------------------
DROP TEMPORARY TABLE IF EXISTS `_fixture_results`;

CREATE TEMPORARY TABLE `_fixture_results` AS
SELECT
    f.`CaseId`,
    f.`Label`,
    -- normalise description
    REGEXP_REPLACE(
        REPLACE(REPLACE(REPLACE(
            REPLACE(f.`Description`, '\r', ' '),
        '\n', ' '), '\t', ' '),
        _utf8mb4 '\u00a0', ' '),
    '[[:space:]]+', ' ')                    AS `NormDesc`,
    f.`ExistingQty`,
    f.`ExistingPrice`,
    f.`ExpectQtyRaw`,
    f.`ExpectPriceRaw`,
    f.`ExpectQty`,
    f.`ExpectPrice`
FROM `_fixture_cases` f;

-- Extract raw strings
ALTER TABLE `_fixture_results`
    ADD COLUMN `QtyRaw`   VARCHAR(40) NULL,
    ADD COLUMN `PriceRaw` VARCHAR(40) NULL;

UPDATE `_fixture_results`
SET
    `QtyRaw` = NULLIF(TRIM(
        REGEXP_REPLACE(
            REGEXP_REPLACE(
                REGEXP_SUBSTR(`NormDesc`,
                    '(?i)nominal[[:space:]]+[0-9][0-9.]*(?:,[0-9]+)?[[:space:]]+St[uü]ck'),
                '(?i)^nominal[[:space:]]+', ''),
            '(?i)[[:space:]]+St[uü]ck$', '')
    ), ''),
    `PriceRaw` = NULLIF(TRIM(
        REGEXP_REPLACE(
            REGEXP_REPLACE(
                REGEXP_SUBSTR(`NormDesc`,
                    '(?i)kurs[[:space:]]+[0-9][0-9.]*(?:,[0-9]+)?[[:space:]]*€'),
                '(?i)^kurs[[:space:]]+', ''),
            '[[:space:]]*€$', '')
    ), '');

-- Add parsed decimal columns
ALTER TABLE `_fixture_results`
    ADD COLUMN `ParsedQty`   DECIMAL(18,8) NULL,
    ADD COLUMN `ParsedPrice` DECIMAL(18,8) NULL;

UPDATE `_fixture_results`
SET
    `ParsedQty` = CASE
        WHEN `QtyRaw` IS NULL THEN NULL
        WHEN REGEXP_REPLACE(REPLACE(REPLACE(`QtyRaw`, '.', ''), ',', '.'), '[0-9.]', '') <> '' THEN NULL
        WHEN CAST(REPLACE(REPLACE(`QtyRaw`, '.', ''), ',', '.') AS DECIMAL(18,8)) <= 0 THEN NULL
        ELSE CAST(REPLACE(REPLACE(`QtyRaw`, '.', ''), ',', '.') AS DECIMAL(18,8))
    END,
    `ParsedPrice` = CASE
        WHEN `PriceRaw` IS NULL THEN NULL
        WHEN REGEXP_REPLACE(REPLACE(REPLACE(`PriceRaw`, '.', ''), ',', '.'), '[0-9.]', '') <> '' THEN NULL
        WHEN CAST(REPLACE(REPLACE(`PriceRaw`, '.', ''), ',', '.') AS DECIMAL(18,8)) <= 0 THEN NULL
        ELSE CAST(REPLACE(REPLACE(`PriceRaw`, '.', ''), ',', '.') AS DECIMAL(18,8))
    END;

-- Compute final values (simulate COALESCE fill, respecting existing non-null)
ALTER TABLE `_fixture_results`
    ADD COLUMN `FinalQty`   DECIMAL(18,8) NULL,
    ADD COLUMN `FinalPrice` DECIMAL(18,8) NULL;

UPDATE `_fixture_results`
SET
    `FinalQty`   = COALESCE(`ExistingQty`,   `ParsedQty`),
    `FinalPrice` = COALESCE(`ExistingPrice`, `ParsedPrice`);

-- ---------------------------------------------------------------------------
-- RESULTS — every row should show PASS
-- ---------------------------------------------------------------------------
SELECT
    `CaseId`,
    `Label`,
    `QtyRaw`,
    `PriceRaw`,
    `ParsedQty`,
    `ParsedPrice`,
    `FinalQty`,
    `FinalPrice`,
    `ExpectQty`,
    `ExpectPrice`,
    CASE
        WHEN (`FinalQty`   <=> `ExpectQty`)
         AND (`FinalPrice` <=> `ExpectPrice`)
        THEN 'PASS'
        ELSE 'FAIL'
    END AS `Result`
FROM `_fixture_results`
ORDER BY `CaseId`;

-- Summary
SELECT
    SUM(`Result` = 'PASS') AS `PassCount`,
    SUM(`Result` = 'FAIL') AS `FailCount`
FROM (
    SELECT
        CASE
            WHEN (`FinalQty` <=> `ExpectQty`) AND (`FinalPrice` <=> `ExpectPrice`)
            THEN 'PASS' ELSE 'FAIL'
        END AS `Result`
    FROM `_fixture_results`
) counts;

DROP TEMPORARY TABLE IF EXISTS `_fixture_cases`;
DROP TEMPORARY TABLE IF EXISTS `_fixture_results`;
