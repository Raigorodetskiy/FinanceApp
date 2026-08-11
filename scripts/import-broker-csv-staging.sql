-- =============================================================================
-- STAGING TABLE SCHEMA
-- Broker CSV Reconciliation Workflow – Step 1: Create staging table
--
-- Creates the persistent staging table `broker_csv_staging` that the shell
-- importer populates.  Safe to re-run: the table is only created if it does
-- not already exist.
--
-- !! Do NOT run this against a database that already holds a staging table
--    from a previous partial import unless you intend to accumulate rows.
--    Use TRUNCATE TABLE `broker_csv_staging` before a fresh import cycle.
--
-- MariaDB 10.5+ compatible.
-- =============================================================================

SET NAMES utf8mb4;

CREATE TABLE IF NOT EXISTS `broker_csv_staging` (
    -- -----------------------------------------------------------------------
    -- Source traceability
    -- -----------------------------------------------------------------------
    `Id`                  INT            NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `SourceFile`          VARCHAR(512)   NOT NULL COMMENT 'Original CSV filename',
    `SourceRow`           INT            NOT NULL COMMENT '1-based row number in source file (header = 0)',

    -- -----------------------------------------------------------------------
    -- Raw parsed fields (German format preserved here)
    -- -----------------------------------------------------------------------
    `RawBuchungstag`      VARCHAR(20)    NULL COMMENT 'DD.MM.YYYY as read from CSV',
    `RawValuta`           VARCHAR(20)    NULL COMMENT 'DD.MM.YYYY as read from CSV',
    `Bezeichnung`         VARCHAR(512)   NULL COMMENT 'Security name from CSV',
    `RawISIN`             VARCHAR(20)    NULL COMMENT 'ISIN column value before normalisation',
    `RawNominal`          VARCHAR(40)    NULL COMMENT 'Nominal (Stk.) column value, may be signed',
    `NominalUnit`         VARCHAR(40)    NULL COMMENT 'Unit column (St.ck / Stück etc.)',
    `RawBetrag`           VARCHAR(40)    NULL COMMENT 'Amount column value, German format',
    `BetragCurrency`      VARCHAR(10)    NULL COMMENT 'Currency code following Betrag',
    `RawKurs`             VARCHAR(40)    NULL COMMENT 'Unit-price column value, German format',
    `KursCurrency`        VARCHAR(10)    NULL COMMENT 'Currency code following Kurs',
    `RawDevisenkurs`      VARCHAR(20)    NULL COMMENT 'FX rate column value, German format',
    `TaNr`                VARCHAR(40)    NULL COMMENT 'TA.-Nr. column value',
    `Buchungsinformation` VARCHAR(1024)  NULL COMMENT 'Full Buchungsinformation text',

    -- -----------------------------------------------------------------------
    -- Normalised / parsed fields
    -- -----------------------------------------------------------------------
    `Buchungstag`         DATE           NULL COMMENT 'Parsed booking date',
    `Valuta`              DATE           NULL COMMENT 'Parsed value date',
    `ISIN`                VARCHAR(12)    NULL COMMENT 'Uppercase ISIN, exactly 12 chars; NULL if invalid',
    `Nominal`             DECIMAL(18,8)  NULL COMMENT 'ABS(parsed nominal); NULL if parse fails',
    `NominalSigned`       DECIMAL(18,8)  NULL COMMENT 'Signed parsed nominal (negative = Verkauf row)',
    `Betrag`              DECIMAL(18,8)  NULL COMMENT 'Parsed absolute amount; NULL if parse fails',
    `Kurs`                DECIMAL(18,8)  NULL COMMENT 'Parsed unit price; NULL if parse fails',
    `Devisenkurs`         DECIMAL(18,8)  NULL COMMENT 'Parsed FX rate; NULL if parse fails',
    `TradeType`           ENUM('Buy','Sell','CorporateAction','Unknown')
                                         NOT NULL DEFAULT 'Unknown'
                          COMMENT 'Derived from Buchungsinformation keyword',
    `BrokerRef`           VARCHAR(60)    NULL COMMENT 'Reference number at end of Buchungsinformation',
    `CorporateActionHint` VARCHAR(255)   NULL COMMENT 'Non-null when row looks like a corporate action',

    -- -----------------------------------------------------------------------
    -- Parse-error flag
    -- -----------------------------------------------------------------------
    `ParseError`          VARCHAR(512)   NULL COMMENT 'Non-null if any mandatory field failed to parse',

    -- -----------------------------------------------------------------------
    -- Reconciliation result (populated by preview/apply scripts)
    -- -----------------------------------------------------------------------
    `MatchStatus`         ENUM(
                              'PENDING',
                              'MATCHED_EXACT',
                              'MATCHED_PROBABLE',
                              'AMBIGUOUS',
                              'UNMATCHED',
                              'CORPORATE_ACTION',
                              'CURRENCY_MISMATCH',
                              'PARSE_ERROR',
                              'SKIPPED_ALREADY_FILLED'
                          ) NOT NULL DEFAULT 'PENDING',
    `MatchScore`          TINYINT        NULL COMMENT 'Composite match score 0-100',
    `MatchEvidence`       TEXT           NULL COMMENT 'Human-readable evidence summary',
    `MatchedTransactionId` INT           NULL COMMENT 'FK to Transactions.Id if matched',

    -- -----------------------------------------------------------------------
    -- Apply tracking
    -- -----------------------------------------------------------------------
    `AppliedAt`           DATETIME       NULL COMMENT 'When the apply script last wrote to Transactions',

    INDEX `idx_bcs_isin`        (`ISIN`),
    INDEX `idx_bcs_buchungstag` (`Buchungstag`),
    INDEX `idx_bcs_broker_ref`  (`BrokerRef`),
    INDEX `idx_bcs_ta_nr`       (`TaNr`),
    INDEX `idx_bcs_status`      (`MatchStatus`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Staging table for broker CSV import; read-only with respect to Transactions';

SELECT 'broker_csv_staging table ready.' AS notice;
