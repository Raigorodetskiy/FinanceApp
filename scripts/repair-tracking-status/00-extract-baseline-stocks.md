# Safe Extraction of Baseline Stock Identities from Pre-Import Backup

> Do not execute SQL automatically.
> Never restore this backup over production.
> Keep `financeapp.service` stopped until `03-post-repair-verify.sql` passes.

## Purpose

Before the index-constituent import, the backup
`/root/financeapp-immediately-before-broker-apply-20260811-131947.sql.gz`
contains the canonical set of stocks that existed before index-only rows were
inserted. These identities are the repair allowlist and must never be demoted.

This runbook restores that backup into an isolated DB, exports **exactly five**
identity columns in this order:

1. `Ticker`
2. `Exchange`
3. `Isin`
4. `Wkn`
5. `ProviderSymbol`

If the pre-migration schema does not contain `ProviderSymbol`, the export uses
`NULL AS ProviderSymbol`, which the MySQL client emits as `\N` in
`--batch --silent --skip-column-names` mode. The load step expects that exact
headerless TSV format.

## 0) Shell variables

```bash
set -Eeuo pipefail

BACKUP="/root/financeapp-immediately-before-broker-apply-20260811-131947.sql.gz"
BASELINE_DB="financeapp_baseline_audit"
REPAIR_DB="financeapp_repair_audit"
BASELINE_TSV="/tmp/financeapp_baseline_stocks.tsv"
BASELINE_FALLBACK_SQL="/tmp/financeapp_baseline_stocks_fallback.sql"

umask 077
```

## 1) Verify backup and restore into isolated baseline DB

```bash
sha256sum "$BACKUP"
gunzip -t "$BACKUP"

mysql -u root -p <<SQL
CREATE DATABASE IF NOT EXISTS \`$BASELINE_DB\`
  CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
SQL

gunzip -c "$BACKUP" | mysql -u root -p "$BASELINE_DB"
```

Read-only schema checks against the restored baseline DB:

```bash
mysql -u root -p --table -e "SHOW COLUMNS FROM ${BASELINE_DB}.Stocks;"

mysql -u root -p --table "$BASELINE_DB" -e "
SELECT column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_schema = DATABASE()
  AND table_name = 'Stocks'
  AND column_name IN ('Ticker', 'Exchange', 'Isin', 'Wkn', 'ProviderSymbol')
ORDER BY FIELD(column_name, 'Ticker', 'Exchange', 'Isin', 'Wkn', 'ProviderSymbol');
"
```

## 2) Export identities only (NO `Id`, NO header)

Use `--batch --silent --skip-column-names` so the export stays compatible with
`LOAD DATA LOCAL INFILE`.

```bash
required_columns=(Ticker Exchange Isin Wkn)
for column in "${required_columns[@]}"; do
  exists=$(mysql -u root -p --batch --silent --skip-column-names "$BASELINE_DB" \
    -e "SELECT COUNT(*)
        FROM information_schema.columns
        WHERE table_schema = DATABASE()
          AND table_name = 'Stocks'
          AND column_name = '${column}';")

  if [ "$exists" -ne 1 ]; then
    echo "ABORT: restored baseline schema is missing required Stocks.${column}." >&2
    exit 1
  fi
done

provider_symbol_exists=$(mysql -u root -p --batch --silent --skip-column-names "$BASELINE_DB" \
  -e "SELECT COUNT(*)
      FROM information_schema.columns
      WHERE table_schema = DATABASE()
        AND table_name = 'Stocks'
        AND column_name = 'ProviderSymbol';")

literal_backslash_n_query="
SELECT COUNT(*)
FROM Stocks
WHERE TRIM(Ticker) = '\\\\N'
   OR TRIM(Exchange) = '\\\\N'
   OR TRIM(Isin) = '\\\\N'
   OR TRIM(Wkn) = '\\\\N'
"

if [ "$provider_symbol_exists" -eq 1 ]; then
  provider_symbol_select="NULLIF(TRIM(ProviderSymbol), '') AS ProviderSymbol"
  literal_backslash_n_query="${literal_backslash_n_query}
   OR TRIM(ProviderSymbol) = '\\\\N'"
else
  provider_symbol_select="NULL AS ProviderSymbol"
fi

literal_backslash_n_count=$(mysql -u root -p --batch --silent --skip-column-names "$BASELINE_DB" \
  -e "$literal_backslash_n_query")

if [ "$literal_backslash_n_count" -ne 0 ]; then
  echo "ABORT: restored baseline contains literal \\N identity values; do not continue with TSV workflow." >&2
  exit 1
fi

restored_stock_row_count=$(mysql -u root -p --batch --silent --skip-column-names "$BASELINE_DB" \
  -e "SELECT COUNT(*) FROM Stocks;")

mysql -u root -p "$BASELINE_DB" \
  --batch --silent --skip-column-names \
  -e "
SELECT
  NULLIF(TRIM(Ticker), '') AS Ticker,
  NULLIF(TRIM(Exchange), '') AS Exchange,
  NULLIF(TRIM(Isin), '') AS Isin,
  NULLIF(TRIM(Wkn), '') AS Wkn,
  ${provider_symbol_select}
FROM Stocks
ORDER BY Id;
" > "$BASELINE_TSV"

extracted_row_count=$(wc -l < "$BASELINE_TSV")
if [ "$restored_stock_row_count" -ne "$extracted_row_count" ]; then
  echo "ABORT: restored Stocks row count (${restored_stock_row_count}) != exported TSV row count (${extracted_row_count})." >&2
  exit 1
fi

awk -F '\t' '
  NF != 5 {
    printf("ABORT: line %d has %d columns, expected 5.\n", NR, NF) > "/dev/stderr";
    bad = 1;
  }
  /\r/ {
    printf("ABORT: line %d contains a carriage return.\n", NR) > "/dev/stderr";
    bad = 1;
  }
  END {
    if (NR == 0) {
      print "ABORT: export produced zero baseline rows." > "/dev/stderr";
      bad = 1;
    }
    exit bad;
  }
' "$BASELINE_TSV"
```

`mysql --batch --silent --skip-column-names` emits SQL `NULL` as literal `\N`.
That is required here: when `ProviderSymbol` is absent in the restored
pre-migration schema, the fifth TSV column must be `\N`, not an empty fake
identity.

## 3) Create repair DB first, then load into staging

Create the repair DB **before** connecting to it:

```bash
mysql -u root -p <<SQL
CREATE DATABASE IF NOT EXISTS \`$REPAIR_DB\`
  CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
SQL

mysql -u root -p "$REPAIR_DB" <<'SQL'
CREATE TABLE IF NOT EXISTS baseline_stocks (
    BaselineLoadId CHAR(64) NOT NULL,
    Ticker         VARCHAR(20)  NULL,
    Exchange       VARCHAR(20)  NULL,
    Isin           VARCHAR(12)  NULL,
    Wkn            VARCHAR(6)   NULL,
    ProviderSymbol VARCHAR(50)  NULL,
    LoadedAt       DATETIME(6)  NOT NULL DEFAULT UTC_TIMESTAMP(6),
    INDEX ix_baseline_isin (Isin),
    INDEX ix_baseline_provider_exchange (ProviderSymbol, Exchange),
    INDEX ix_baseline_ticker_exchange (Ticker, Exchange)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS baseline_stocks_stage LIKE baseline_stocks;

CREATE TABLE IF NOT EXISTS baseline_stocks_loads (
    BaselineLoadId CHAR(64) NOT NULL PRIMARY KEY,
    BackupPath VARCHAR(1024) NOT NULL,
    BackupFileName VARCHAR(255) NOT NULL,
    BackupSha256 CHAR(64) NOT NULL,
    RestoredStockRowCount INT NOT NULL,
    ExtractedRowCount INT NOT NULL,
    LoadedRowCount INT NOT NULL,
    PromotedRowCount INT NOT NULL,
    ProviderSymbolColumnPresent TINYINT(1) NOT NULL,
    ExtractedAt DATETIME(6) NOT NULL,
    PromotedAt DATETIME(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    IsCurrent TINYINT(1) NOT NULL DEFAULT 0
) ENGINE=InnoDB;

TRUNCATE TABLE baseline_stocks_stage;
SQL
```

Preflight `LOCAL INFILE`, then prefer `LOAD DATA LOCAL INFILE` with explicit
client opt-in. If `LOCAL` is disabled, use the client-side INSERT fallback
below. Do **not** weaken `secure_file_priv` or other server-wide settings.

```bash
backup_sha256=$(sha256sum "$BACKUP" | awk '{print $1}')
extracted_at_utc=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
baseline_load_id=$(printf '%s' "${backup_sha256}|${restored_stock_row_count}|${extracted_at_utc}" | sha256sum | awk '{print $1}')
local_infile_value=$(mysql -u root -p --batch --silent --skip-column-names \
  -e "SHOW VARIABLES LIKE 'local_infile';" | awk '{print toupper($2)}')

use_insert_fallback=0
if [ "$local_infile_value" = "ON" ] || [ "$local_infile_value" = "1" ]; then
  if ! mysql --local-infile=1 -u root -p "$REPAIR_DB" <<SQL
LOAD DATA LOCAL INFILE '$BASELINE_TSV'
INTO TABLE baseline_stocks_stage
FIELDS TERMINATED BY '\t'
LINES TERMINATED BY '\n'
(Ticker, Exchange, Isin, Wkn, ProviderSymbol)
SET
  BaselineLoadId = '${baseline_load_id}',
  Ticker = NULLIF(NULLIF(TRIM(Ticker), ''), '\\N'),
  Exchange = NULLIF(NULLIF(TRIM(Exchange), ''), '\\N'),
  Isin = NULLIF(NULLIF(TRIM(Isin), ''), '\\N'),
  Wkn = NULLIF(NULLIF(TRIM(Wkn), ''), '\\N'),
  ProviderSymbol = NULLIF(NULLIF(TRIM(ProviderSymbol), ''), '\\N');
SQL
  then
    echo "LOCAL INFILE failed; switching to client-side INSERT fallback." >&2
    use_insert_fallback=1
  fi
else
  echo "LOCAL INFILE is disabled; switching to client-side INSERT fallback." >&2
  use_insert_fallback=1
fi

if [ "$use_insert_fallback" -eq 1 ]; then
  python3 - <<'PY' "$BASELINE_TSV" "$BASELINE_FALLBACK_SQL" "$baseline_load_id"
import csv
import sys
from pathlib import Path

source_path = Path(sys.argv[1])
sql_path = Path(sys.argv[2])
baseline_load_id = sys.argv[3]


def sql_literal(value: str) -> str:
    trimmed = value.strip()
    if trimmed == "" or trimmed == r"\N":
        return "NULL"
    return "'" + trimmed.replace("\\", "\\\\").replace("'", "''") + "'"

with source_path.open("r", encoding="utf-8", newline="") as source, sql_path.open("w", encoding="utf-8") as sql:
    reader = csv.reader(source, delimiter='\t')
    sql.write("START TRANSACTION;\n")
    batch = []

    def flush() -> None:
        if not batch:
            return
        sql.write("INSERT INTO baseline_stocks_stage (BaselineLoadId, Ticker, Exchange, Isin, Wkn, ProviderSymbol) VALUES\n")
        sql.write(",\n".join(batch))
        sql.write(";\n")
        batch.clear()

    for row_number, row in enumerate(reader, start=1):
        if len(row) != 5:
            raise SystemExit(f"ABORT: fallback generator expected 5 columns on line {row_number}, got {len(row)}")
        values = [sql_literal(baseline_load_id)] + [sql_literal(value) for value in row]
        batch.append("(" + ", ".join(values) + ")")
        if len(batch) == 500:
            flush()

    flush()
    sql.write("COMMIT;\n")
PY

  mysql -u root -p "$REPAIR_DB" < "$BASELINE_FALLBACK_SQL"
fi
```

## 4) Validate staged rows, compare counts, then promote atomically

```bash
loaded_row_count=$(mysql -u root -p --batch --silent --skip-column-names "$REPAIR_DB" \
  -e "SELECT COUNT(*)
      FROM baseline_stocks_stage
      WHERE BaselineLoadId = '${baseline_load_id}';")

if [ "$restored_stock_row_count" -ne "$loaded_row_count" ]; then
  echo "ABORT: restored Stocks row count (${restored_stock_row_count}) != loaded staging row count (${loaded_row_count})." >&2
  exit 1
fi

malformed_row_count=$(mysql -u root -p --batch --silent --skip-column-names "$REPAIR_DB" \
  -e "SELECT COUNT(*)
      FROM baseline_stocks_stage
      WHERE BaselineLoadId = '${baseline_load_id}'
        AND NULLIF(TRIM(COALESCE(Isin, '')), '') IS NULL
        AND NOT (
              NULLIF(TRIM(COALESCE(ProviderSymbol, '')), '') IS NOT NULL
          AND NULLIF(TRIM(COALESCE(Exchange, '')), '') IS NOT NULL
        )
        AND NOT (
              NULLIF(TRIM(COALESCE(Ticker, '')), '') IS NOT NULL
          AND NULLIF(TRIM(COALESCE(Exchange, '')), '') IS NOT NULL
        );")

duplicate_identity_count=$(mysql -u root -p --batch --silent --skip-column-names "$REPAIR_DB" \
  -e "SELECT COUNT(*)
      FROM (
          SELECT
              UPPER(TRIM(COALESCE(Ticker, ''))) AS TickerN,
              UPPER(TRIM(COALESCE(Exchange, ''))) AS ExchangeN,
              UPPER(TRIM(COALESCE(Isin, ''))) AS IsinN,
              UPPER(TRIM(COALESCE(Wkn, ''))) AS WknN,
              UPPER(TRIM(COALESCE(ProviderSymbol, ''))) AS ProviderSymbolN,
              COUNT(*) AS DuplicateCount
          FROM baseline_stocks_stage
          WHERE BaselineLoadId = '${baseline_load_id}'
          GROUP BY TickerN, ExchangeN, IsinN, WknN, ProviderSymbolN
          HAVING COUNT(*) > 1
      ) duplicate_rows;")

mysql -u root -p --table "$REPAIR_DB" <<SQL
SELECT
  '${baseline_load_id}' AS BaselineLoadId,
  '${BACKUP}' AS BackupPath,
  '${backup_sha256}' AS BackupSha256,
  ${restored_stock_row_count} AS RestoredBaselineStocksCount,
  ${extracted_row_count} AS ExtractedRowCount,
  ${loaded_row_count} AS LoadedRowCount,
  ${provider_symbol_exists} AS ProviderSymbolColumnPresent,
  '${extracted_at_utc}' AS ExtractedAtUtc;

SELECT
  SUM(CASE WHEN Isin IS NOT NULL THEN 1 ELSE 0 END) AS RowsWithIsin,
  SUM(CASE WHEN ProviderSymbol IS NOT NULL AND Exchange IS NOT NULL THEN 1 ELSE 0 END) AS RowsWithProviderIdentity,
  SUM(CASE WHEN Ticker IS NOT NULL AND Exchange IS NOT NULL THEN 1 ELSE 0 END) AS RowsWithTickerIdentity
FROM baseline_stocks_stage
WHERE BaselineLoadId = '${baseline_load_id}';

SELECT Ticker, Exchange, Isin, Wkn, ProviderSymbol
FROM baseline_stocks_stage
WHERE BaselineLoadId = '${baseline_load_id}'
ORDER BY COALESCE(Ticker, ''), COALESCE(Exchange, ''), COALESCE(Isin, '')
LIMIT 20;

SELECT Ticker, Exchange, Isin, Wkn, ProviderSymbol
FROM baseline_stocks_stage
WHERE BaselineLoadId = '${baseline_load_id}'
  AND NULLIF(TRIM(COALESCE(Isin, '')), '') IS NULL
  AND NOT (
        NULLIF(TRIM(COALESCE(ProviderSymbol, '')), '') IS NOT NULL
    AND NULLIF(TRIM(COALESCE(Exchange, '')), '') IS NOT NULL
  )
  AND NOT (
        NULLIF(TRIM(COALESCE(Ticker, '')), '') IS NOT NULL
    AND NULLIF(TRIM(COALESCE(Exchange, '')), '') IS NOT NULL
  )
ORDER BY COALESCE(Ticker, ''), COALESCE(Exchange, ''), COALESCE(Isin, '')
LIMIT 20;

SELECT
  UPPER(TRIM(COALESCE(Ticker, ''))) AS TickerN,
  UPPER(TRIM(COALESCE(Exchange, ''))) AS ExchangeN,
  UPPER(TRIM(COALESCE(Isin, ''))) AS IsinN,
  UPPER(TRIM(COALESCE(Wkn, ''))) AS WknN,
  UPPER(TRIM(COALESCE(ProviderSymbol, ''))) AS ProviderSymbolN,
  COUNT(*) AS DuplicateCount
FROM baseline_stocks_stage
WHERE BaselineLoadId = '${baseline_load_id}'
GROUP BY TickerN, ExchangeN, IsinN, WknN, ProviderSymbolN
HAVING COUNT(*) > 1;

SELECT
  (SELECT COUNT(*) FROM ${BASELINE_DB}.Stocks) AS RestoredBaselineStocksCount,
  (SELECT COUNT(*) FROM baseline_stocks_stage WHERE BaselineLoadId = '${baseline_load_id}') AS LoadedBaselineRowCount;
SQL

if [ "$malformed_row_count" -ne 0 ]; then
  echo "ABORT: staging baseline contains malformed rows; validated baseline was left untouched." >&2
  exit 1
fi

if [ "$duplicate_identity_count" -ne 0 ]; then
  echo "ABORT: staging baseline contains duplicate normalized full identities; validated baseline was left untouched." >&2
  exit 1
fi

mysql -u root -p "$REPAIR_DB" <<SQL
DROP TABLE IF EXISTS baseline_stocks_promoted;
CREATE TABLE baseline_stocks_promoted LIKE baseline_stocks;

INSERT INTO baseline_stocks_promoted
    (BaselineLoadId, Ticker, Exchange, Isin, Wkn, ProviderSymbol, LoadedAt)
SELECT
    BaselineLoadId,
    Ticker,
    Exchange,
    Isin,
    Wkn,
    ProviderSymbol,
    UTC_TIMESTAMP(6)
FROM baseline_stocks_stage
WHERE BaselineLoadId = '${baseline_load_id}';
SQL

promoted_row_count=$(mysql -u root -p --batch --silent --skip-column-names "$REPAIR_DB" \
  -e "SELECT COUNT(*)
      FROM baseline_stocks_promoted
      WHERE BaselineLoadId = '${baseline_load_id}';")

if [ "$promoted_row_count" -ne "$loaded_row_count" ]; then
  echo "ABORT: promoted row count (${promoted_row_count}) != loaded staging row count (${loaded_row_count})." >&2
  exit 1
fi

mysql -u root -p "$REPAIR_DB" <<SQL
INSERT INTO baseline_stocks_loads
    (BaselineLoadId, BackupPath, BackupFileName, BackupSha256, RestoredStockRowCount, ExtractedRowCount, LoadedRowCount, PromotedRowCount, ProviderSymbolColumnPresent, ExtractedAt, IsCurrent)
VALUES
    ('${baseline_load_id}', '${BACKUP}', '$(basename "$BACKUP")', '${backup_sha256}', ${restored_stock_row_count}, ${extracted_row_count}, ${loaded_row_count}, ${promoted_row_count}, ${provider_symbol_exists}, '${extracted_at_utc}', 0)
ON DUPLICATE KEY UPDATE
    BackupPath = VALUES(BackupPath),
    BackupFileName = VALUES(BackupFileName),
    BackupSha256 = VALUES(BackupSha256),
    RestoredStockRowCount = VALUES(RestoredStockRowCount),
    ExtractedRowCount = VALUES(ExtractedRowCount),
    LoadedRowCount = VALUES(LoadedRowCount),
    PromotedRowCount = VALUES(PromotedRowCount),
    ProviderSymbolColumnPresent = VALUES(ProviderSymbolColumnPresent),
    ExtractedAt = VALUES(ExtractedAt),
    PromotedAt = UTC_TIMESTAMP(6),
    IsCurrent = 0;

DROP TABLE IF EXISTS baseline_stocks_previous;
RENAME TABLE
    baseline_stocks TO baseline_stocks_previous,
    baseline_stocks_promoted TO baseline_stocks;

TRUNCATE TABLE baseline_stocks_stage;
UPDATE baseline_stocks_loads SET IsCurrent = 0;
UPDATE baseline_stocks_loads SET IsCurrent = 1 WHERE BaselineLoadId = '${baseline_load_id}';
SQL
```

If any validation step fails, stop immediately. `baseline_stocks` remains the last
validated allowlist until the atomic promotion succeeds.

## 5) Record validated provenance and hand off to preview/apply

```bash
mysql -u root -p --table "$REPAIR_DB" -e "
SELECT
  BaselineLoadId,
  BackupPath,
  BackupFileName,
  BackupSha256,
  RestoredStockRowCount,
  ExtractedRowCount,
  LoadedRowCount,
  PromotedRowCount,
  ProviderSymbolColumnPresent,
  ExtractedAt,
  PromotedAt,
  IsCurrent
FROM baseline_stocks_loads
ORDER BY PromotedAt DESC
LIMIT 5;
"

mysql -u root -p --table "$REPAIR_DB" -e "
SELECT
  (SELECT COUNT(*) FROM ${BASELINE_DB}.Stocks) AS RestoredBaselineStocksCount,
  (SELECT COUNT(*) FROM ${REPAIR_DB}.baseline_stocks) AS ValidatedBaselineRowCount;
"
```

Expected before `01-audit-preview.sql` / `02-apply-repair.sql`:

- restored baseline `Stocks` row count equals validated `baseline_stocks` row count;
- latest `baseline_stocks_loads.IsCurrent = 1` row matches the backup path and
  SHA-256 you just verified;
- `02-apply-repair.sql @expected_baseline_count` is copied from validated
  `baseline_stocks` row count.

Do **not** run repair automatically from this document.

## 6) Cleanup after the full repair window

Only after preview/apply/verify are complete:

```bash
rm -f "$BASELINE_TSV" "$BASELINE_FALLBACK_SQL"
mysql -u root -p -e "DROP DATABASE IF EXISTS \`$BASELINE_DB\`;"
```

## Coverage note

Repository tests for this follow-up are contract tests over the runbook and SQL
artifacts. They validate the required command shapes and safeguards but do not
replace a disposable MySQL/MariaDB operator dry run before production use.
