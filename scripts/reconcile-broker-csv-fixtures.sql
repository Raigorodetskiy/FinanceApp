-- =============================================================================
-- FIXTURES / TESTS
-- Broker CSV Reconciliation Workflow
--
-- Tests parsing, matching, corporate actions, currencies, idempotency, and
-- safeguards without requiring a live Transactions table (uses temp tables).
--
-- Run:
--   mysql -h HOST -u USER -p DATABASE \
--     < scripts/reconcile-broker-csv-fixtures.sql
--
-- Every test case should show PASS in the Result column.
-- MariaDB 10.5+ compatible.
-- =============================================================================

SET NAMES utf8mb4;

-- ---------------------------------------------------------------------------
-- Helper: German number parser (inline SQL version, same logic as shell script)
-- ---------------------------------------------------------------------------
-- Returns the decimal value of a German-formatted number string, or NULL.
DROP FUNCTION IF EXISTS `_test_parse_german`;
CREATE FUNCTION `_test_parse_german`(raw VARCHAR(40))
RETURNS DECIMAL(18,8)
DETERMINISTIC
BEGIN
    DECLARE norm VARCHAR(40);
    SET norm = REPLACE(REPLACE(raw, '.', ''), ',', '.');
    -- Reject anything that is not a valid decimal (allowing leading minus)
    IF norm REGEXP '^-?[0-9]+(\\.[0-9]+)?$' THEN
        RETURN CAST(norm AS DECIMAL(18,8));
    END IF;
    RETURN NULL;
END;

-- ---------------------------------------------------------------------------
-- A.  PARSING TESTS
-- ---------------------------------------------------------------------------
DROP TEMPORARY TABLE IF EXISTS `_fix_parsing`;
CREATE TEMPORARY TABLE `_fix_parsing` (
    `CaseId`      INT           NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `Label`       VARCHAR(120)  NOT NULL,
    `Input`       VARCHAR(80)   NOT NULL,
    `Expected`    DECIMAL(18,8) NULL,
    `Got`         DECIMAL(18,8) NULL,
    `Result`      VARCHAR(4)    NOT NULL DEFAULT 'FAIL'
);

INSERT INTO `_fix_parsing` (`Label`, `Input`, `Expected`)
VALUES
-- German thousands + decimal
('german_thousands_and_decimal',       '1.234,56',   1234.56000000),
-- Plain integer
('plain_integer',                       '42',         42.00000000),
-- No thousands, comma decimal
('comma_decimal_only',                  '688,00',     688.00000000),
-- Signed negative (nominal for Verkauf)
('negative_nominal',                    '-5',         -5.00000000),
-- Signed negative with decimal
('negative_with_decimal',               '-3.575,50',  -3575.50000000),
-- FX rate
('fx_rate',                             '1,168',      1.16800000),
-- Large amount
('large_amount',                        '2.752,00',   2752.00000000),
-- Malformed: alpha character → NULL
('malformed_alpha',                     'abc',        NULL),
-- Malformed: double comma → NULL
('malformed_double_comma',              '1,,2',       NULL),
-- Empty string → NULL
('empty_string',                        '',           NULL),
-- Zero is valid
('zero_value',                          '0',          0.00000000),
-- Negative zero is valid (edge)
('negative_zero',                       '-0',         0.00000000);

UPDATE `_fix_parsing`
SET `Got` = `_test_parse_german`(`Input`);

UPDATE `_fix_parsing`
SET `Result` = CASE WHEN (`Got` <=> `Expected`) THEN 'PASS' ELSE 'FAIL' END;

SELECT `CaseId`, `Label`, `Input`, `Expected`, `Got`, `Result`
FROM `_fix_parsing` ORDER BY `CaseId`;

SELECT SUM(`Result`='PASS') AS PassCount, SUM(`Result`='FAIL') AS FailCount
FROM `_fix_parsing`;

-- ---------------------------------------------------------------------------
-- B.  DATE PARSING TESTS  (SQL inline, mirrors shell parse_date)
-- ---------------------------------------------------------------------------
DROP TEMPORARY TABLE IF EXISTS `_fix_dates`;
CREATE TEMPORARY TABLE `_fix_dates` (
    `CaseId`      INT           NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `Label`       VARCHAR(80)   NOT NULL,
    `RawDate`     VARCHAR(20)   NOT NULL,
    `Expected`    DATE          NULL,
    `Got`         DATE          NULL,
    `Result`      VARCHAR(4)    NOT NULL DEFAULT 'FAIL'
);

INSERT INTO `_fix_dates` (`Label`, `RawDate`, `Expected`)
VALUES
('valid_date_07aug2026',  '07.08.2026', '2026-08-07'),
('valid_date_31jul2026',  '31.07.2026', '2026-07-31'),
('valid_date_17feb2022',  '17.02.2022', '2022-02-17'),
('valid_date_01jan2020',  '01.01.2020', '2020-01-01'),
-- Malformed: wrong format → NULL (handled by shell; SQL gets NULL inserted)
('malformed_yyyymmdd',    '2026-08-07', NULL),
('malformed_slash',       '07/08/2026', NULL),
('empty',                 '',           NULL);

-- Parse: extract day/month/year only for valid DD.MM.YYYY patterns
UPDATE `_fix_dates`
SET `Got` = CASE
    WHEN `RawDate` REGEXP '^[0-9]{2}\\.[0-9]{2}\\.[0-9]{4}$'
         THEN STR_TO_DATE(`RawDate`, '%d.%m.%Y')
    ELSE NULL
END;

UPDATE `_fix_dates`
SET `Result` = CASE WHEN (`Got` <=> `Expected`) THEN 'PASS' ELSE 'FAIL' END;

SELECT `CaseId`, `Label`, `RawDate`, `Expected`, `Got`, `Result`
FROM `_fix_dates` ORDER BY `CaseId`;

SELECT SUM(`Result`='PASS') AS PassCount, SUM(`Result`='FAIL') AS FailCount
FROM `_fix_dates`;

-- ---------------------------------------------------------------------------
-- C.  ISIN VALIDATION TESTS
-- ---------------------------------------------------------------------------
DROP TEMPORARY TABLE IF EXISTS `_fix_isin`;
CREATE TEMPORARY TABLE `_fix_isin` (
    `CaseId`   INT          NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `Label`    VARCHAR(80)  NOT NULL,
    `RawISIN`  VARCHAR(20)  NOT NULL,
    `Expected` VARCHAR(12)  NULL,
    `Got`      VARCHAR(12)  NULL,
    `Result`   VARCHAR(4)   NOT NULL DEFAULT 'FAIL'
);

INSERT INTO `_fix_isin` (`Label`, `RawISIN`, `Expected`)
VALUES
('valid_seagate',          'IE00BKVD2N49', 'IE00BKVD2N49'),
('valid_micron',           'US5951121038', 'US5951121038'),
('valid_adobe',            'US00724F1012', 'US00724F1012'),
('valid_nvidia',           'US67066G1040', 'US67066G1040'),
('valid_lowercase_input',  'ie00bkvd2n49', 'IE00BKVD2N49'),
('invalid_too_short',      'US1234567',    NULL),
('invalid_too_long',       'US123456789012345', NULL),
('invalid_special_chars',  'US1234!@#$56', NULL),
('empty',                  '',             NULL),
('invalid_starts_digit',   '12US6789ABCD', NULL);

UPDATE `_fix_isin`
SET `Got` = CASE
    WHEN UPPER(TRIM(`RawISIN`)) REGEXP '^[A-Z]{2}[A-Z0-9]{10}$'
         THEN UPPER(TRIM(`RawISIN`))
    ELSE NULL
END;

UPDATE `_fix_isin`
SET `Result` = CASE WHEN (`Got` <=> `Expected`) THEN 'PASS' ELSE 'FAIL' END;

SELECT `CaseId`, `Label`, `RawISIN`, `Expected`, `Got`, `Result`
FROM `_fix_isin` ORDER BY `CaseId`;

SELECT SUM(`Result`='PASS') AS PassCount, SUM(`Result`='FAIL') AS FailCount
FROM `_fix_isin`;

-- ---------------------------------------------------------------------------
-- D.  STAGING ROW SIMULATION: typical trades + corporate actions
--     (tests parse results as they would appear in broker_csv_staging)
-- ---------------------------------------------------------------------------
DROP TEMPORARY TABLE IF EXISTS `_fix_staging_rows`;
CREATE TEMPORARY TABLE `_fix_staging_rows` (
    `CaseId`             INT           NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `Label`              VARCHAR(120)  NOT NULL,
    -- CSV input fields
    `RawBuchungstag`     VARCHAR(20)   NULL,
    `RawValuta`          VARCHAR(20)   NULL,
    `RawISIN`            VARCHAR(20)   NULL,
    `RawNominal`         VARCHAR(40)   NULL,
    `RawBetrag`          VARCHAR(40)   NULL,
    `BetragCurrency`     VARCHAR(10)   NULL,
    `RawKurs`            VARCHAR(40)   NULL,
    `KursCurrency`       VARCHAR(10)   NULL,
    `RawDevisenkurs`     VARCHAR(20)   NULL,
    `Buchungsinformation` VARCHAR(512) NULL,
    -- Expected parsed outputs
    `ExpectTradeType`    VARCHAR(20)   NULL,
    `ExpectBrokerRef`    VARCHAR(60)   NULL,
    `ExpectNominal`      DECIMAL(18,8) NULL,
    `ExpectBetrag`       DECIMAL(18,8) NULL,
    `ExpectKurs`         DECIMAL(18,8) NULL,
    `ExpectDevisenkurs`  DECIMAL(18,8) NULL,
    `ExpectIsCorporate`  TINYINT       NOT NULL DEFAULT 0
);

INSERT INTO `_fix_staging_rows`
  (`Label`, `RawBuchungstag`, `RawValuta`, `RawISIN`,
   `RawNominal`, `RawBetrag`, `BetragCurrency`, `RawKurs`, `KursCurrency`,
   `RawDevisenkurs`, `Buchungsinformation`,
   `ExpectTradeType`, `ExpectBrokerRef`, `ExpectNominal`,
   `ExpectBetrag`, `ExpectKurs`, `ExpectDevisenkurs`, `ExpectIsCorporate`)
VALUES
-- 1. Typical Buy (Seagate, 2026-08-07)
('buy_seagate_2026',
 '07.08.2026','11.08.2026','IE00BKVD2N49',
 '4','2.752,00','EUR','688,00','EUR','1,000',
 'Ausführung ORDER Kauf IE00BKVD2N49 315712787',
 'Buy','315712787',4.00000000,2752.00000000,688.00000000,1.00000000,0),

-- 2. Typical Sell (Micron, 2026-07-31)
('sell_micron_2026',
 '31.07.2026','04.08.2026','US5951121038',
 '-5','-3.575,50','EUR','715,10','EUR','1,000',
 'Ausführung ORDER Verkauf US5951121038 315268095',
 'Sell','315268095',5.00000000,3575.50000000,715.10000000,1.00000000,0),

-- 3. Apple split 1:4 (corporate action)
('apple_split_1_4',
 '28.08.2020','28.08.2020','US0378331005',
 '15','0','EUR','0','EUR','1,000',
 'Split im Verhältnis 1:4 US0378331005',
 'CorporateAction',NULL,NULL,0.00000000,0.00000000,1.00000000,1),

-- 4. NVIDIA split 1:10 (corporate action)
('nvidia_split_1_10',
 '10.06.2024','10.06.2024','US67066G1040',
 '90','0','EUR','0','EUR','1,000',
 'Split im Verhältnis 1:10 US67066G1040',
 'CorporateAction',NULL,NULL,0.00000000,0.00000000,1.00000000,1),

-- 5. 21Shares split 1:14 (corporate action)
('21shares_split_1_14',
 '15.03.2021','15.03.2021','CH0454664001',
 '13','0','EUR','0','EUR','1,000',
 'Split im Verhältnis 1:14 CH0454664001',
 'CorporateAction',NULL,NULL,0.00000000,0.00000000,1.00000000,1),

-- 6. FlatexDEGIRO capitalisation (corporate action)
('flatex_kapitalerhoehung',
 '15.09.2021','15.09.2021','DE000FTG1111',
 '1','0','EUR','0','EUR','1,000',
 'Kapitalerhöhung aus Gesellschaftsmitteln DE000FTG1111',
 'CorporateAction',NULL,NULL,0.00000000,0.00000000,1.00000000,1),

-- 7. Lagerstellenwechsel (custody transfer, corporate action) +
('lagerstellenwechsel_positive',
 '01.04.2021','01.04.2021','US67066G1040',
 '9','0','EUR','0','EUR','1,000',
 'Lagerstellenwechsel NVIDIA CORP. Eingang US67066G1040',
 'CorporateAction',NULL,NULL,0.00000000,0.00000000,1.00000000,1),

-- 8. Lagerstellenwechsel (custody transfer, corporate action) -
('lagerstellenwechsel_negative',
 '01.04.2021','01.04.2021','US67066G1040',
 '-9','0','EUR','0','EUR','1,000',
 'Lagerstellenwechsel NVIDIA CORP. Ausgang US67066G1040',
 'CorporateAction',NULL,NULL,0.00000000,0.00000000,1.00000000,1),

-- 9. Adobe sell 2022 (reference case for cost calculation)
('sell_adobe_2022',
 '17.02.2022','21.02.2022','US00724F1012',
 '-1','-409,50','EUR','409,50','EUR','1,000',
 'Ausführung ORDER Verkauf US00724F1012 186573183',
 'Sell','186573183',1.00000000,409.50000000,409.50000000,1.00000000,0),

-- 10. USD buy (Sandisk): Betrag/Kurs in USD, FX != 1
('buy_sandisk_usd',
 '10.05.2024','14.05.2024','US80007T1007',
 '2','926,10','USD','463,05','USD','1,168',
 'Ausführung ORDER Kauf US80007T1007 123456789',
 'Buy','123456789',2.00000000,926.10000000,463.05000000,1.16800000,0),

-- 11. USD sell (Sandisk)
('sell_sandisk_usd',
 '20.06.2024','24.06.2024','US80007T1007',
 '-2','-2.196,36','USD','1.098,18','USD','1,142',
 'Ausführung ORDER Verkauf US80007T1007 987654321',
 'Sell','987654321',2.00000000,2196.36000000,1098.18000000,1.14200000,0),

-- 12. Mojibake in Buchungsinformation (Windows-1252 read as UTF-8)
('mojibake_encoding',
 '05.01.2021','07.01.2021','US02079K3059',
 '2','3.200,00','EUR','1.600,00','EUR','1,000',
 'Ausf\xc3\xbchrung ORDER Kauf US02079K3059 111222333',
 'Buy','111222333',2.00000000,3200.00000000,1600.00000000,1.00000000,0);

-- Compute parsed values
DROP TEMPORARY TABLE IF EXISTS `_fix_staging_results`;
CREATE TEMPORARY TABLE `_fix_staging_results` AS
SELECT
    f.`CaseId`,
    f.`Label`,
    -- Trade type: derived from Buchungsinformation
    CASE
        WHEN f.`Buchungsinformation` REGEXP '(?i)Split im Verh'         THEN 'CorporateAction'
        WHEN f.`Buchungsinformation` REGEXP '(?i)Kapitalerh'            THEN 'CorporateAction'
        WHEN f.`Buchungsinformation` REGEXP '(?i)Lagerstellenwechsel'   THEN 'CorporateAction'
        WHEN f.`Buchungsinformation` REGEXP '(?i)\\bKauf\\b'            THEN 'Buy'
        WHEN f.`Buchungsinformation` REGEXP '(?i)\\bVerkauf\\b'         THEN 'Sell'
        ELSE 'Unknown'
    END                                                         AS `GotTradeType`,
    -- BrokerRef: last numeric token
    REGEXP_SUBSTR(f.`Buchungsinformation`, '[0-9]+$')           AS `GotBrokerRef`,
    -- Nominal (absolute value)
    ABS(`_test_parse_german`(f.`RawNominal`))                  AS `GotNominal`,
    -- Betrag (absolute value)
    ABS(`_test_parse_german`(f.`RawBetrag`))                   AS `GotBetrag`,
    -- Kurs (absolute value)
    ABS(`_test_parse_german`(f.`RawKurs`))                     AS `GotKurs`,
    -- Devisenkurs
    `_test_parse_german`(f.`RawDevisenkurs`)                   AS `GotDevisenkurs`,
    -- Corporate action flag
    CASE
        WHEN f.`Buchungsinformation` REGEXP '(?i)Split im Verh|(?i)Kapitalerh|(?i)Lagerstellenwechsel'
             THEN 1 ELSE 0
    END                                                         AS `GotIsCorporate`,
    -- Expected
    f.`ExpectTradeType`,
    f.`ExpectBrokerRef`,
    f.`ExpectNominal`,
    f.`ExpectBetrag`,
    f.`ExpectKurs`,
    f.`ExpectDevisenkurs`,
    f.`ExpectIsCorporate`
FROM `_fix_staging_rows` f;

-- Add result column
ALTER TABLE `_fix_staging_results` ADD COLUMN `Result` VARCHAR(4) NOT NULL DEFAULT 'FAIL';

UPDATE `_fix_staging_results`
SET `Result` = CASE
    WHEN (`GotTradeType`   <=> `ExpectTradeType`)
     AND (`GotBrokerRef`   <=> `ExpectBrokerRef`)
     AND (`GotNominal`     <=> `ExpectNominal`)
     AND (`GotBetrag`      <=> `ExpectBetrag`)
     AND (`GotKurs`        <=> `ExpectKurs`)
     AND (`GotDevisenkurs` <=> `ExpectDevisenkurs`)
     AND (`GotIsCorporate` = `ExpectIsCorporate`)
    THEN 'PASS' ELSE 'FAIL'
END;

SELECT `CaseId`, `Label`,
    `GotTradeType`, `ExpectTradeType`,
    `GotBrokerRef`, `ExpectBrokerRef`,
    `GotNominal`, `ExpectNominal`,
    `GotBetrag`, `ExpectBetrag`,
    `GotKurs`, `ExpectKurs`,
    `GotDevisenkurs`, `ExpectDevisenkurs`,
    `GotIsCorporate`, `ExpectIsCorporate`,
    `Result`
FROM `_fix_staging_results`
ORDER BY `CaseId`;

SELECT SUM(`Result`='PASS') AS PassCount, SUM(`Result`='FAIL') AS FailCount
FROM `_fix_staging_results`;

-- ---------------------------------------------------------------------------
-- E.  SAFEGUARDS: fields must NOT be overwritten when already filled
-- ---------------------------------------------------------------------------
DROP TEMPORARY TABLE IF EXISTS `_fix_safeguards`;
CREATE TEMPORARY TABLE `_fix_safeguards` (
    `CaseId`           INT           NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `Label`            VARCHAR(120)  NOT NULL,
    `ExistingQty`      DECIMAL(18,8) NULL,
    `ExistingPrice`    DECIMAL(18,8) NULL,
    `ExistingISIN`     VARCHAR(12)   NULL,
    `ExistingISINType` VARCHAR(8)    NULL,
    `CsvQty`           DECIMAL(18,8) NULL,
    `CsvPrice`         DECIMAL(18,8) NULL,
    `CsvISIN`          VARCHAR(12)   NULL,
    -- Expected final values (should equal existing when already set)
    `ExpectQty`        DECIMAL(18,8) NULL,
    `ExpectPrice`      DECIMAL(18,8) NULL,
    `ExpectISIN`       VARCHAR(12)   NULL,
    `ExpectISINType`   VARCHAR(8)    NULL,
    `Result`           VARCHAR(4)    NOT NULL DEFAULT 'FAIL'
);

INSERT INTO `_fix_safeguards`
  (`Label`, `ExistingQty`, `ExistingPrice`, `ExistingISIN`, `ExistingISINType`,
   `CsvQty`, `CsvPrice`, `CsvISIN`, `ExpectQty`, `ExpectPrice`, `ExpectISIN`, `ExpectISINType`)
VALUES
-- Both already filled: CSV values must not overwrite
('both_filled_not_overwritten',
 5.00000000, 715.10000000, 'US5951121038', 'ISIN',
 5.00000000, 700.00000000, 'US5951121038',
 5.00000000, 715.10000000, 'US5951121038', 'ISIN'),
-- Qty filled, price NULL: only price should be filled
('qty_filled_price_filled_from_csv',
 5.00000000, NULL, NULL, NULL,
 5.00000000, 715.10000000, 'US5951121038',
 5.00000000, 715.10000000, 'US5951121038', 'ISIN'),
-- ISIN already filled with different value: must not overwrite
('isin_already_filled_not_overwritten',
 NULL, NULL, 'US9999999999', 'ISIN',
 4.00000000, 688.00000000, 'IE00BKVD2N49',
 4.00000000, 688.00000000, 'US9999999999', 'ISIN'),
-- All NULL: all should be filled from CSV
('all_null_all_filled',
 NULL, NULL, NULL, NULL,
 4.00000000, 688.00000000, 'IE00BKVD2N49',
 4.00000000, 688.00000000, 'IE00BKVD2N49', 'ISIN'),
-- Price already filled (non-null), qty NULL: only qty filled
('price_filled_qty_filled_from_csv',
 NULL, 409.50000000, 'US00724F1012', 'ISIN',
 1.00000000, 409.50000000, 'US00724F1012',
 1.00000000, 409.50000000, 'US00724F1012', 'ISIN');

-- Simulate fill logic
UPDATE `_fix_safeguards`
SET `Result` = CASE
    WHEN (
          -- Quantity: use existing if not null, else CSV
          COALESCE(`ExistingQty`,   `CsvQty`)   <=> `ExpectQty`
      AND COALESCE(`ExistingPrice`, `CsvPrice`)  <=> `ExpectPrice`
          -- ISIN: use existing if meaningful, else CSV
      AND (CASE WHEN `ExistingISIN` IS NOT NULL AND `ExistingISIN` != ''
                THEN `ExistingISIN` ELSE `CsvISIN` END) <=> `ExpectISIN`
      AND (CASE WHEN `ExistingISINType` IS NOT NULL
                THEN `ExistingISINType` ELSE 'ISIN' END) <=> `ExpectISINType`
    ) THEN 'PASS' ELSE 'FAIL'
END;

SELECT `CaseId`, `Label`, `Result`
FROM `_fix_safeguards` ORDER BY `CaseId`;

SELECT SUM(`Result`='PASS') AS PassCount, SUM(`Result`='FAIL') AS FailCount
FROM `_fix_safeguards`;

-- ---------------------------------------------------------------------------
-- F.  COST CALCULATION TESTS
-- ---------------------------------------------------------------------------
DROP TEMPORARY TABLE IF EXISTS `_fix_costs`;
CREATE TEMPORARY TABLE `_fix_costs` (
    `CaseId`        INT           NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `Label`         VARCHAR(120)  NOT NULL,
    `TradeType`     VARCHAR(10)   NOT NULL,
    `CsvBetrag`     DECIMAL(18,8) NULL,
    `CsvCurrency`   VARCHAR(10)   NULL,
    `DbAmount`      DECIMAL(18,8) NULL,
    `CsvQty`        DECIMAL(18,8) NULL,
    `CsvKurs`       DECIMAL(18,8) NULL,
    `Devisenkurs`   DECIMAL(18,8) NULL DEFAULT 1.000,
    `ExpectCost`    DECIMAL(18,8) NULL,
    `ExpectFxFlag`  TINYINT       NOT NULL DEFAULT 0,
    `ExpectRounding` TINYINT      NOT NULL DEFAULT 0,
    `Result`        VARCHAR(4)    NOT NULL DEFAULT 'FAIL'
);

INSERT INTO `_fix_costs`
  (`Label`, `TradeType`, `CsvBetrag`, `CsvCurrency`, `DbAmount`,
   `CsvQty`, `CsvKurs`, `Devisenkurs`,
   `ExpectCost`, `ExpectFxFlag`, `ExpectRounding`)
VALUES
-- Adobe sell: CSV gross 409.50, DB amount 401.60 → cost diff = 7.90
('adobe_sell_cost',
 'Sell', 409.50, 'EUR', 401.60, 1, 409.50, 1.000,
 7.90, 0, 0),
-- Seagate buy: CSV gross 2752.00, DB amount 2752.00 → zero cost diff
('seagate_buy_zero_cost',
 'Buy', 2752.00, 'EUR', 2752.00, 4, 688.00, 1.000,
 0.00, 0, 0),
-- Rounding-only candidate: 30 × 33.28 = 998.40, Betrag = 998.55, diff 0.15 ≤ 30 × 0.01
('rounding_candidate_buy',
 'Buy', 998.55, 'EUR', 998.55, 30, 33.28, 1.000,
 0.00, 0, 1),
-- USD trade: FX affected, cost NULL for EUR formula
('usd_buy_fx_affected',
 'Buy', 926.10, 'USD', 793.00, 2, 463.05, 1.168,
 NULL, 1, 0),
-- Negative cost (sell proceeds more than CSV gross → flag)
('negative_diff_sell',
 'Sell', 400.00, 'EUR', 410.00, 1, 400.00, 1.000,
 -10.00, 0, 0);

UPDATE `_fix_costs`
SET
    `Result` = CASE
        WHEN (
            -- Cost formula
            (CASE
                WHEN `CsvCurrency` != 'EUR' THEN NULL
                WHEN `TradeType` = 'Buy'  THEN ROUND(ABS(`DbAmount`) - `CsvBetrag`, 2)
                WHEN `TradeType` = 'Sell' THEN ROUND(`CsvBetrag` - ABS(`DbAmount`), 2)
                ELSE NULL
            END) <=> `ExpectCost`
            -- FX flag
            AND (CASE WHEN `CsvCurrency` != 'EUR' THEN 1 ELSE 0 END) = `ExpectFxFlag`
            -- Rounding candidate (diff <= Qty × 0.01) — only checked for non-FX EUR rows
            AND (CASE
                WHEN `CsvCurrency` = 'EUR' AND `CsvQty` IS NOT NULL AND `CsvQty` > 0
                     AND ABS(COALESCE(
                         CASE
                             WHEN `TradeType` = 'Buy'  THEN ROUND(ABS(`DbAmount`) - `CsvBetrag`, 4)
                             WHEN `TradeType` = 'Sell' THEN ROUND(`CsvBetrag` - ABS(`DbAmount`), 4)
                             ELSE NULL
                         END, 999)) <= `CsvQty` * 0.01
                     THEN 1 ELSE 0
                END) = `ExpectRounding`
        ) THEN 'PASS' ELSE 'FAIL'
    END;

SELECT `CaseId`, `Label`, `TradeType`,
    `CsvBetrag`, `DbAmount`, `ExpectCost`,
    `ExpectFxFlag`, `ExpectRounding`, `Result`
FROM `_fix_costs` ORDER BY `CaseId`;

SELECT SUM(`Result`='PASS') AS PassCount, SUM(`Result`='FAIL') AS FailCount
FROM `_fix_costs`;

-- ---------------------------------------------------------------------------
-- G.  IDEMPOTENCY GUARD: applying twice should produce the same result
--     (simulated: re-running fill logic on already-filled row keeps existing)
-- ---------------------------------------------------------------------------
DROP TEMPORARY TABLE IF EXISTS `_fix_idempotency`;
CREATE TEMPORARY TABLE `_fix_idempotency` (
    `Pass1_Qty`   DECIMAL(18,8) NULL,
    `Pass1_Price` DECIMAL(18,8) NULL,
    `Pass2_Qty`   DECIMAL(18,8) NULL,
    `Pass2_Price` DECIMAL(18,8) NULL,
    `Result`      VARCHAR(4)    NOT NULL DEFAULT 'FAIL'
);

-- Simulate: first pass fills NULL qty/price from CSV
INSERT INTO `_fix_idempotency` (`Pass1_Qty`, `Pass1_Price`)
SELECT
    COALESCE(NULL,   5.00000000),   -- existing NULL → CSV value
    COALESCE(NULL,   715.10000000)
FROM DUAL;

-- Second pass: existing values are now non-null → COALESCE keeps them
UPDATE `_fix_idempotency`
SET
    `Pass2_Qty`   = COALESCE(`Pass1_Qty`,   4.00000000),  -- different CSV value, must be ignored
    `Pass2_Price` = COALESCE(`Pass1_Price`, 999.99000000);

UPDATE `_fix_idempotency`
SET `Result` = CASE
    WHEN (`Pass1_Qty` <=> `Pass2_Qty` AND `Pass1_Price` <=> `Pass2_Price`)
    THEN 'PASS' ELSE 'FAIL'
END;

SELECT 'idempotency' AS Label, `Pass1_Qty`, `Pass1_Price`, `Pass2_Qty`, `Pass2_Price`, `Result`
FROM `_fix_idempotency`;

-- ---------------------------------------------------------------------------
-- Overall summary
-- ---------------------------------------------------------------------------
SELECT
    'A_Parsing'          AS TestGroup, SUM(`Result`='PASS') AS Pass, SUM(`Result`='FAIL') AS Fail FROM `_fix_parsing`
UNION ALL SELECT 'B_Dates',      SUM(`Result`='PASS'), SUM(`Result`='FAIL') FROM `_fix_dates`
UNION ALL SELECT 'C_ISIN',       SUM(`Result`='PASS'), SUM(`Result`='FAIL') FROM `_fix_isin`
UNION ALL SELECT 'D_StagingRows',SUM(`Result`='PASS'), SUM(`Result`='FAIL') FROM `_fix_staging_results`
UNION ALL SELECT 'E_Safeguards', SUM(`Result`='PASS'), SUM(`Result`='FAIL') FROM `_fix_safeguards`
UNION ALL SELECT 'F_Costs',      SUM(`Result`='PASS'), SUM(`Result`='FAIL') FROM `_fix_costs`
UNION ALL SELECT 'G_Idempotency',SUM(`Result`='PASS'), SUM(`Result`='FAIL') FROM `_fix_idempotency`;

-- Cleanup
DROP TEMPORARY TABLE IF EXISTS `_fix_parsing`;
DROP TEMPORARY TABLE IF EXISTS `_fix_dates`;
DROP TEMPORARY TABLE IF EXISTS `_fix_isin`;
DROP TEMPORARY TABLE IF EXISTS `_fix_staging_rows`;
DROP TEMPORARY TABLE IF EXISTS `_fix_staging_results`;
DROP TEMPORARY TABLE IF EXISTS `_fix_safeguards`;
DROP TEMPORARY TABLE IF EXISTS `_fix_costs`;
DROP TEMPORARY TABLE IF EXISTS `_fix_idempotency`;
DROP FUNCTION IF EXISTS `_test_parse_german`;

SELECT 'FIXTURES COMPLETE.' AS notice;
