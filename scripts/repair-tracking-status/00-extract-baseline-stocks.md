# Safe Extraction of Baseline Stock Identities from Pre-Import Backup

## Purpose

Before the index-constituent import, the backup
`/root/financeapp-immediately-before-broker-apply-20260811-131947.sql.gz`
contains the canonical set of stocks that existed before index-only rows were
inserted. These identities are the repair allowlist and must never be demoted.

This runbook extracts baseline identities in an isolated DB and loads them into
`financeapp_repair_audit.baseline_stocks`.

## 1) Restore backup into isolated staging DB (never over production)

```bash
mysql -u root -p <<'SQL'
CREATE DATABASE IF NOT EXISTS financeapp_baseline_audit
  CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
SQL

gunzip -c /root/financeapp-immediately-before-broker-apply-20260811-131947.sql.gz \
  | mysql -u root -p financeapp_baseline_audit
```

## 2) Export identities only (NO `Id` column)

Use `--batch --silent --skip-column-names` to avoid headers and keep a stable
TSV format expected by `LOAD DATA`.

```bash
mysql -u root -p financeapp_baseline_audit \
  --batch --silent --skip-column-names \
  -e "
SELECT
  NULLIF(TRIM(Ticker), ''),
  NULLIF(TRIM(Exchange), ''),
  NULLIF(TRIM(Isin), ''),
  NULLIF(TRIM(Wkn), ''),
  NULLIF(TRIM(ProviderSymbol), '')
FROM Stocks
ORDER BY Id
" > /tmp/financeapp_baseline_stocks.tsv
```

## 3) Load baseline identities into repair DB

```bash
mysql -u root -p financeapp_repair_audit <<'SQL'
CREATE DATABASE IF NOT EXISTS financeapp_repair_audit
  CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS baseline_stocks (
    Ticker         VARCHAR(20)  NULL,
    Exchange       VARCHAR(20)  NULL,
    Isin           VARCHAR(12)  NULL,
    Wkn            VARCHAR(6)   NULL,
    ProviderSymbol VARCHAR(50)  NULL,
    LoadedAt       DATETIME(6)  NOT NULL DEFAULT UTC_TIMESTAMP(6),
    INDEX ix_baseline_isin (Isin),
    INDEX ix_baseline_provider_exchange (ProviderSymbol, Exchange),
    INDEX ix_baseline_ticker_exchange (Ticker, Exchange)
);

TRUNCATE TABLE baseline_stocks;

LOAD DATA INFILE '/tmp/financeapp_baseline_stocks.tsv'
INTO TABLE baseline_stocks
FIELDS TERMINATED BY '\t'
LINES TERMINATED BY '\n'
(Ticker, Exchange, Isin, Wkn, ProviderSymbol)
SET
  Ticker = NULLIF(NULLIF(TRIM(Ticker), ''), '\\N'),
  Exchange = NULLIF(NULLIF(TRIM(Exchange), ''), '\\N'),
  Isin = NULLIF(NULLIF(TRIM(Isin), ''), '\\N'),
  Wkn = NULLIF(NULLIF(TRIM(Wkn), ''), '\\N'),
  ProviderSymbol = NULLIF(NULLIF(TRIM(ProviderSymbol), ''), '\\N');
SQL
```

## 4) Validate baseline load before preview/apply

```bash
mysql -u root -p financeapp_repair_audit <<'SQL'
SELECT COUNT(*) AS BaselineRowCount FROM baseline_stocks;

SELECT
  SUM(CASE WHEN Isin IS NOT NULL THEN 1 ELSE 0 END) AS RowsWithIsin,
  SUM(CASE WHEN ProviderSymbol IS NOT NULL AND Exchange IS NOT NULL THEN 1 ELSE 0 END) AS RowsWithProviderIdentity,
  SUM(CASE WHEN Ticker IS NOT NULL AND Exchange IS NOT NULL THEN 1 ELSE 0 END) AS RowsWithTickerIdentity
FROM baseline_stocks;

SELECT Ticker, Exchange, Isin, Wkn, ProviderSymbol
FROM baseline_stocks
ORDER BY COALESCE(Ticker, ''), COALESCE(Exchange, ''), COALESCE(Isin, '')
LIMIT 20;

SELECT
  UPPER(TRIM(COALESCE(Ticker, ''))) AS TickerN,
  UPPER(TRIM(COALESCE(Exchange, ''))) AS ExchangeN,
  UPPER(TRIM(COALESCE(Isin, ''))) AS IsinN,
  UPPER(TRIM(COALESCE(Wkn, ''))) AS WknN,
  UPPER(TRIM(COALESCE(ProviderSymbol, ''))) AS ProviderSymbolN,
  COUNT(*) AS DuplicateCount
FROM baseline_stocks
GROUP BY TickerN, ExchangeN, IsinN, WknN, ProviderSymbolN
HAVING COUNT(*) > 1;
SQL
```

Expected:
- `BaselineRowCount > 0`.
- duplicate query returns zero rows.
- sample rows look aligned (`Ticker` is ticker text, not numeric `Id`).

## 5) Cleanup when done

```bash
mysql -u root -p -e "DROP DATABASE IF EXISTS financeapp_baseline_audit;"
rm -f /tmp/financeapp_baseline_stocks.tsv
```

## Security notes

- Do not commit credentials.
- Do not restore backup over production.
- Baseline backup from this incident: `/root/financeapp-immediately-before-broker-apply-20260811-131947.sql.gz`.
- Assumption: literal text value `\N` is not used as a real identifier value in
  source data; load step treats `\N` as SQL `NULL`.
