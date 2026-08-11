# Broker CSV Reconciliation Workflow

This document describes how to recover missing `InstrumentCode`/`InstrumentCodeType`,
`Quantity`, and `UnitPrice` in the `Transactions` table from historical broker CSV exports,
and how to produce a separate read-only estimate of transaction costs.

---

## Overview of scripts

| Script | Purpose |
|---|---|
| `import-broker-csv-staging.sql` | Create the persistent `broker_csv_staging` table |
| `import-broker-csv.sh` | Parse and load one or more broker CSV files into staging |
| `reconcile-broker-csv-preview.sql` | Read-only matching report (no writes to `Transactions`) |
| `reconcile-broker-csv-apply.sql` | Update only high-confidence matched rows (transactional) |
| `reconcile-broker-csv-costs-preview.sql` | Read-only transaction-cost/commission estimate |
| `reconcile-broker-csv-fixtures.sql` | Parsing/matching/safeguard tests (standalone) |

These scripts are **separate** from the existing `backfill-transaction-quantity-unit-price*`
scripts which parse data already embedded in `Transactions.Description`.
Use both workflows in combination for maximum coverage.

---

## Prerequisites

- MariaDB 10.5+ client (`mysql`)
- `iconv` (usually part of glibc-utils / libc-bin)
- `awk`, `sed` (standard POSIX utilities)
- Shell: bash 4+
- A full database backup before any apply step

Credentials: supply the password via `MYSQL_PWD` environment variable or a
`~/.my.cnf` `[client]` section. **Never pass passwords on the command line.**

---

## Step-by-step operator guide

### 0. Backup

```bash
mysqldump -h HOST -u USER -p DATABASE > backup_$(date +%Y%m%d_%H%M%S).sql
```

Keep the backup file accessible until you have verified the apply step.

### 1. Create the staging table (once per database)

```bash
mysql -h HOST -u USER -p DATABASE \
  < scripts/import-broker-csv-staging.sql
```

Safe to re-run: the table is created only if it does not already exist.

### 2. Normalise CSV encoding

Broker exports from Flatex/flatexDEGIRO are often saved in **Windows-1252 / ISO-8859-1**.
When opened as UTF-8, German characters appear as mojibake:

| Original | Appears as |
|---|---|
| `Stück` | `St\xc3\xbck` or `St?ck` |
| `Ausführung` | `Ausf\xc3\xbchrung` or `Ausf?hrung` |
| `Verhältnis` | `Verh\xc3\xa4ltnis` or `Verh?ltnis` |

The importer script detects and converts non-UTF-8 files automatically using
`iconv -f ISO-8859-1 -t UTF-8`.

If your files are already UTF-8, no conversion occurs.

If automatic detection fails, convert manually:
```bash
iconv -f CP1252 -t UTF-8 export_2022.csv > export_2022_utf8.csv
```

### 3. Import CSV files into staging

```bash
# Import one year
bash scripts/import-broker-csv.sh \
  -h HOST -P 3306 -u USER -d DATABASE \
  export_2020.csv

# Import multiple years at once
bash scripts/import-broker-csv.sh \
  -h HOST -P 3306 -u USER -d DATABASE \
  export_2020.csv export_2021.csv export_2022.csv \
  export_2024.csv export_2025.csv export_2026.csv

# Fresh run (clear previous staging data first)
bash scripts/import-broker-csv.sh \
  -h HOST -P 3306 -u USER -d DATABASE \
  --truncate \
  export_*.csv

# Dry run (print SQL without executing)
bash scripts/import-broker-csv.sh --dry-run export_2020.csv
```

After importing, review any parse errors:
```sql
SELECT SourceFile, SourceRow, ParseError
FROM broker_csv_staging
WHERE MatchStatus = 'PARSE_ERROR';
```

### 4. Run the preview (read-only)

```bash
mysql -h HOST -u USER -p DATABASE \
  < scripts/reconcile-broker-csv-preview.sql
```

The preview produces:
- A **summary table** with row counts per `MatchStatus`
- A **detail table** of actionable rows (`MATCHED_EXACT`, `MATCHED_PROBABLE`) showing
  current and proposed field values, match score, and evidence
- **Ambiguous rows** with all candidate transaction IDs and scores
- **Unmatched rows** — may need manual staging-table edits or description corrections
- **Corporate actions** — excluded from ordinary trade matching
- **Currency mismatch rows** — need FX reconciliation
- **Parse errors** — rows that failed to parse on import

#### Match status definitions

| Status | Meaning |
|---|---|
| `MATCHED_EXACT` | Single candidate, score ≥ 50 (BrokerRef + ISIN + date evidence) |
| `MATCHED_PROBABLE` | Single candidate, score 30–49 (ISIN + date/amount, no BrokerRef) |
| `AMBIGUOUS` | Multiple candidates; do not update automatically |
| `UNMATCHED` | No candidate found |
| `CORPORATE_ACTION` | Split / capitalisation / custody transfer row |
| `CURRENCY_MISMATCH` | Non-EUR CSV amount that cannot be reconciled without FX data |
| `PARSE_ERROR` | Row could not be parsed at import time |
| `SKIPPED_ALREADY_FILLED` | Matched but all target fields already non-null |
| `PENDING` | Preview not yet run |

#### Match scoring

Scores are additive; a row needs at least 30 to be applied:

| Evidence | Score |
|---|---|
| BrokerRef from `Buchungsinformation` found in `Transactions.Description` | +30 |
| `TA.-Nr.` found in `Transactions.Description` | +25 |
| Exact `Buchungstag` date in Description | +20 |
| Exact `Valuta` date in Description | +15 |
| ISIN found in Description (beyond base join condition) | +10 |
| Amount consistency (within 20 EUR or 5% of CSV Betrag) | +10 |

> **Note on TA.-Nr.:** A known production discrepancy exists where the CSV
> `TA.-Nr.` was `2701786841` while the transaction Description had `TARef=2701786847`.
> For this reason, `TA.-Nr.` contributes to the score as supporting evidence but
> is not used as a hard join key.

### 5. Review and adjust before applying

Before running the apply script:

1. Check that `MATCHED_EXACT` / `MATCHED_PROBABLE` rows have the correct
   `TxId`, `CsvISIN`, `CsvQuantity`, and `CsvUnitPrice`.
2. Investigate `AMBIGUOUS` rows. You may manually set
   `MatchStatus = 'MATCHED_EXACT'` and `MatchedTransactionId = <correct ID>`
   for a specific row if you are certain of the match.
3. Investigate `UNMATCHED` rows. They may correspond to transactions not yet
   imported, or transactions with descriptions in a format not yet covered.
4. `CORPORATE_ACTION` rows are never applied; they are for information only.
5. `CURRENCY_MISMATCH` rows require manual review. If `Devisenkurs` is available
   in the transaction description, you may adjust the scoring manually.

### 6. Apply (update Transactions)

```bash
# Apply
mysql -h HOST -u USER -p DATABASE \
  < scripts/reconcile-broker-csv-apply.sql
```

The apply script:
1. Shows a **before-state** summary and per-row preview of what will change.
2. Executes the UPDATE inside a `START TRANSACTION … COMMIT` block.
3. Shows `ROW_COUNT()` — inspect this number before relying on the commit.
4. Shows a **post-update verification** of all applied rows.
5. Shows any remaining gaps (rows still missing Quantity or UnitPrice after apply).

**Safeguards:**
- Never overwrites a non-null `Quantity`.
- Never overwrites a non-null `UnitPrice`.
- Never overwrites a meaningful (non-empty, non-null) `InstrumentCode`.
- Never overwrites a non-null `InstrumentCodeType`.
- Only processes `MATCHED_EXACT` or `MATCHED_PROBABLE` rows.
- Is idempotent: running a second time makes no additional changes.

### 7. Verify

After applying, run the existing backfill scripts to catch any remaining gaps
that can be filled from `Transactions.Description` text:

```bash
# Dry-run of description-based backfill
mysql -h HOST -u USER -p DATABASE \
  < scripts/backfill-transaction-quantity-unit-price.sql

# Apply description-based backfill
mysql -h HOST -u USER -p DATABASE \
  < scripts/backfill-transaction-quantity-unit-price-apply.sql
```

Then check overall fill rates:
```sql
SELECT
    COUNT(*) AS Total,
    SUM(Quantity IS NOT NULL) AS HasQuantity,
    SUM(UnitPrice IS NOT NULL) AS HasUnitPrice,
    SUM(InstrumentCode IS NOT NULL AND InstrumentCode != '') AS HasISIN
FROM Transactions
WHERE Type IN ('Buy', 'Sell');
```

### 8. Transaction cost preview (optional)

```bash
mysql -h HOST -u USER -p DATABASE \
  < scripts/reconcile-broker-csv-costs-preview.sql
```

This produces a per-trade cost estimate using:
- `CsvGrossAmount = ABS(CSV Betrag)` (preferred over `Quantity × UnitPrice` due to rounding)
- For EUR Buy: `TotalCostDifference = ABS(DatabaseAmount) − CsvGrossAmount`
- For EUR Sell: `TotalCostDifference = CsvGrossAmount − ABS(DatabaseAmount)`

The value is labelled `TotalCostDifference` (not guaranteed `Commission`) because
it may include taxes, external fees, and other deductions.

**Known reference case:**
- 2022-02-17 Adobe sale, ISIN `US00724F1012`, BrokerRef `186573183`
- CSV gross: `409.50 EUR` | DB amount: `401.60 EUR`
- `TotalCostDifference = 7.90 EUR`

**Flags in the output:**

| Flag | Meaning |
|---|---|
| `FxAffected = YES` | Non-EUR currencies; EUR formula not applicable |
| `NegativeDifference = YES` | Proceeds exceed gross — unexpected; check data |
| `ImplausiblyLarge = YES` | Difference > 5% of gross amount |
| `RoundingOnlyCandidate = YES` | Difference ≤ `Quantity × 0.01` (1 cent/share) — likely rounding, not commission |

Do not write these values to production tables unless an explicit commission column
exists and has been reviewed.

### 9. Rollback

If you need to undo the apply step, use the backup taken in step 0:

```bash
mysql -h HOST -u USER -p DATABASE < backup_YYYYMMDD_HHMMSS.sql
```

Alternatively, to roll back only the CSV-reconciliation changes, you can use the
information in `broker_csv_staging.MatchEvidence` (which contains the before-state
column values for `TxId`, `Before_Quantity`, `Before_UnitPrice`, etc.) to build
a targeted reversal statement.

### 10. Rerun

The workflow is idempotent. To reprocess with updated CSVs or after fixing
staging data:

```bash
# Re-import (truncate staging first)
bash scripts/import-broker-csv.sh \
  -h HOST -u USER -d DATABASE \
  --truncate \
  export_*.csv

# Re-run preview (resets all non-PARSE_ERROR statuses)
mysql -h HOST -u USER -p DATABASE \
  < scripts/reconcile-broker-csv-preview.sql

# Re-run apply (only processes rows not yet applied)
mysql -h HOST -u USER -p DATABASE \
  < scripts/reconcile-broker-csv-apply.sql
```

---

## CSV format reference

Semicolon-delimited Flatex/flatexDEGIRO broker export:

```
Buchungstag;Valuta;Bezeichnung;ISIN;Nominal (Stk.);;Betrag;;Kurs;;Devisenkurs;TA.-Nr.;Buchungsinformation
```

| Column index | Name | Notes |
|---|---|---|
| 0 | Buchungstag | Booking date, DD.MM.YYYY |
| 1 | Valuta | Value date, DD.MM.YYYY |
| 2 | Bezeichnung | Security name |
| 3 | ISIN | 12-char ISIN |
| 4 | Nominal (Stk.) | Signed quantity (negative for Sell) |
| 5 | *(unit)* | `Stück` (often mojibake as `St?ck`) |
| 6 | Betrag | Gross trade amount, German format |
| 7 | *(currency)* | Currency of Betrag (EUR, USD, …) |
| 8 | Kurs | Unit price, German format |
| 9 | *(currency)* | Currency of Kurs |
| 10 | Devisenkurs | FX rate (1.000 for EUR) |
| 11 | TA.-Nr. | Broker transaction number |
| 12 | Buchungsinformation | Trade type + BrokerRef |

**German number format:** `.` as thousands separator, `,` as decimal separator.
Example: `2.752,00` = 2752.00 EUR.

---

## Corporate actions excluded from matching

The following `Buchungsinformation` patterns are classified as `CORPORATE_ACTION`
and excluded from ordinary trade matching and cost calculations:

- `Split im Verhältnis ...` (stock splits, e.g. Apple 1:4, NVIDIA 1:4 / 1:10, 21Shares 1:14)
- `Kapitalerhöhung aus Gesellschaftsmitteln ...` (e.g. FlatexDEGIRO bonus shares)
- `Lagerstellenwechsel ...` (custody transfers; come in paired +/- rows)

---

## Related scripts

- `scripts/backfill-transaction-quantity-unit-price.sql` – dry-run backfill from `Description` text
- `scripts/backfill-transaction-quantity-unit-price-apply.sql` – apply description-based backfill
- `scripts/backfill-transaction-quantity-unit-price-fixtures.sql` – fixtures for description parser
- `scripts/README-backfill-quantity-unit-price.md` – documentation for the description-based workflow

---

## Testing

Run the fixtures standalone (no live `Transactions` table required):

```bash
mysql -h HOST -u USER -p DATABASE \
  < scripts/reconcile-broker-csv-fixtures.sql
```

All test cases should show `PASS`. Test groups covered:

| Group | Coverage |
|---|---|
| A. Parsing | German number format, edge cases, malformed input rejection |
| B. Dates | DD.MM.YYYY parsing, invalid format rejection |
| C. ISIN | 12-char validation, case normalisation, invalid rejection |
| D. Staging rows | Buy/Sell/CorporateAction classification; splits, capitalisation, Lagerstellenwechsel |
| E. Safeguards | No-overwrite of existing non-null fields |
| F. Costs | TotalCostDifference formula; FX flag; rounding candidate |
| G. Idempotency | Re-applying fill produces identical result |
