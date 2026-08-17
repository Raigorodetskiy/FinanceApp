# Safe Extraction of Baseline Stock Identities from Pre-Import Backup

## Purpose

Before the index-constituent import, the file
`/root/financeapp-immediately-before-broker-apply-20260811-131947.sql.gz`
contains the canonical list of stocks that existed **before** any index members
were inserted. Those stocks are considered legitimately `Tracked` and must
**never** be demoted by the repair.

This document describes how to extract their identities (Ticker, Exchange, ISIN,
WKN, ProviderSymbol) into a temporary staging database **without touching
production** and without storing any production credentials in the repository.

---

## Step 1 — Create a temporary staging database

```bash
# Connect to MySQL (adjust credentials as appropriate for your environment)
mysql -u root -p <<'SQL'
CREATE DATABASE IF NOT EXISTS financeapp_baseline_audit
  CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
GRANT ALL ON financeapp_baseline_audit.* TO 'financeapp'@'localhost';
FLUSH PRIVILEGES;
SQL
```

---

## Step 2 — Restore the baseline backup into the staging DB

```bash
gunzip -c /root/financeapp-immediately-before-broker-apply-20260811-131947.sql.gz \
  | mysql -u root -p financeapp_baseline_audit
```

> **Note:** The baseline does NOT contain `StockMarketIndices` — this is
> expected because index-constituent tracking was added in PR #129.

---

## Step 3 — Export the allowlist of stock identities

```sql
-- Run against financeapp_baseline_audit, NOT the production database
USE financeapp_baseline_audit;

SELECT
    Id,
    Ticker,
    Exchange,
    Isin,
    Wkn,
    ProviderSymbol
FROM Stocks
ORDER BY Id;
```

Save the result to a file:

```bash
mysql -u root -p financeapp_baseline_audit \
  -e "SELECT Id, Ticker, Exchange, Isin, Wkn, ProviderSymbol FROM Stocks ORDER BY Id" \
  --batch --silent \
  > /tmp/financeapp_baseline_stocks.tsv
```

---

## Step 4 — Load allowlist into the repair audit table

The repair script (`02-apply-repair.sql`) references
`financeapp_repair_audit.baseline_stocks` (created automatically by that
script). You can also pre-populate it manually:

```bash
mysql -u root -p financeapp_repair_audit <<'SQL'
CREATE TABLE IF NOT EXISTS baseline_stocks (
    Ticker       VARCHAR(20),
    Exchange     VARCHAR(20),
    Isin         VARCHAR(12),
    Wkn          VARCHAR(6),
    ProviderSymbol VARCHAR(50),
    INDEX ix_ticker_exchange (Ticker, Exchange),
    INDEX ix_isin (Isin),
    INDEX ix_provider (ProviderSymbol)
);
LOAD DATA INFILE '/tmp/financeapp_baseline_stocks.tsv'
INTO TABLE baseline_stocks
FIELDS TERMINATED BY '\t'
LINES TERMINATED BY '\n'
IGNORE 1 ROWS
(Ticker, Exchange, Isin, Wkn, ProviderSymbol);
SQL
```

---

## Step 5 — Clean up the staging database when done

```bash
mysql -u root -p -e "DROP DATABASE IF EXISTS financeapp_baseline_audit;"
```

---

## Security notes

- Do **not** commit any passwords or connection strings to this repository.
- The staging DB name `financeapp_baseline_audit` is hard-coded in the repair
  scripts; change it in all scripts if you use a different name.
- The staging DB must be on the same MySQL server as production (or accessible
  via the same client session) for the cross-DB JOIN in the repair script to
  work.
