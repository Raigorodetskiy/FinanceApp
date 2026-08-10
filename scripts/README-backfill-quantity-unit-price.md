# Backfill: Transactions.Quantity and Transactions.UnitPrice

Fills the nullable `Quantity` and `UnitPrice` columns of the `Transactions`
table by parsing German broker text stored in `Transactions.Description`.

---

## Supported description format

Text produced by Flatex (and compatible German brokers), for example:

```
Buchtag 10.08.2026 Valuta 12.08.2026 ISIN US67066G1040
Bezeichnung NVIDIA CORP. Nominal 10,000 Stück
Betrag 1.947,40 € Kurs 194,74 € Devisenk.1,000
TA.-Nr. 5154283314 Buchungsinformationen Ausführung ORDER Kauf US67066G1040 315730363
```

### Expected extraction — NVIDIA example

| Column | Extracted from | Value |
|---|---|---|
| `Quantity` | `Nominal 10,000 Stück` | `10.00000000` |
| `UnitPrice` | `Kurs 194,74 €` | `194.74000000` |

`Betrag 1.947,40 €` (total transaction amount) is intentionally **not** used as
`UnitPrice`.

---

## Prerequisites and backup warning

> **⚠ MAKE A FULL DATABASE BACKUP BEFORE RUNNING EITHER SCRIPT.**
>
> These scripts modify production data. A backup is the only reliable rollback
> path.

```bash
# Example mysqldump backup (fill in your connection details)
MYSQL_PWD="$(read -rsp 'DB password: ' p; echo "$p")" \
mysqldump \
  --host=HOST --port=3306 --user=USER \
  --single-transaction --routines --triggers \
  financeapp > financeapp-backup-$(date +%Y%m%d-%H%M%S).sql
```

> **⚠ Do NOT embed the password as a command-line argument** (`-pSECRET`).
> Use `-p` (prompt) or set `MYSQL_PWD` only in a controlled shell session
> to avoid leaking credentials into shell history or process listings.

---

## Files

| File | Purpose |
|---|---|
| `backfill-transaction-quantity-unit-price.sql` | **Dry-run / preview** — reads only, shows what would be updated |
| `backfill-transaction-quantity-unit-price-apply.sql` | **Apply** — runs the actual UPDATE inside a transaction |
| `backfill-transaction-quantity-unit-price-fixtures.sql` | **Validation fixtures** — self-contained parsing tests |

---

## Dry-run / preview (no data changed)

```bash
mysql -h HOST -u USER -p DATABASE \
  < scripts/backfill-transaction-quantity-unit-price.sql
```

The script prints:

1. A row-level preview table: transaction ID, current values, extracted raw
   strings, and parsed decimals.
2. An aggregate summary: how many rows matched and how many columns would be
   filled.

**Review this output before proceeding to the apply step.**

---

## Apply

Only run after reviewing the dry-run output.

```bash
mysql -h HOST -u USER -p DATABASE \
  < scripts/backfill-transaction-quantity-unit-price-apply.sql
```

The apply script:

1. Rebuilds the staging data (same logic as dry-run).
2. Shows the same preview and aggregate counts.
3. Runs the `UPDATE` inside `START TRANSACTION … COMMIT`.
4. Prints `ROW_COUNT()` (number of rows updated).
5. Runs a post-update verification `SELECT`.

---

## Validation fixtures

Run without a live `Transactions` table to verify the parsing logic:

```bash
mysql -h HOST -u USER -p DATABASE \
  < scripts/backfill-transaction-quantity-unit-price-fixtures.sql
```

All rows in the output should show `PASS` in the `Result` column.  
The summary row should show `FailCount = 0`.

---

## Verification queries

After applying, check results manually:

```sql
-- Rows where Quantity or UnitPrice was recently filled
SELECT Id, Type, Quantity, UnitPrice, LEFT(Description, 200)
FROM Transactions
WHERE Quantity  IS NOT NULL
  AND UnitPrice IS NOT NULL
  AND Description REGEXP '(?i)nominal[[:space:]]+[0-9]'
ORDER BY Id DESC
LIMIT 50;

-- Sanity check: no negative values
SELECT COUNT(*) AS NegativeQty  FROM Transactions WHERE Quantity  < 0;
SELECT COUNT(*) AS NegativePrice FROM Transactions WHERE UnitPrice < 0;
```

---

## Rollback

Restore from the backup taken before running the apply script:

```bash
mysql -h HOST -u USER -p DATABASE < financeapp-backup-TIMESTAMP.sql
```

---

## Behaviour and rules

- **Only fills `NULL` columns** — existing non-null `Quantity` or `UnitPrice`
  are never overwritten.
- **Independent fill** — if only one value can be parsed, that column is
  filled; the other is left unchanged.
- **Safe conversion** — malformed numeric strings become `NULL`, not `0`.
- **Rejects non-positive values** — negative or zero parsed values are
  discarded.
- **Idempotent** — the `WHERE … IS NULL` guard means re-running the apply
  script after a successful run is safe (no rows will be updated again).

---

## Limitations and unsupported formats

- **`Nominal … Stück`** is the only recognised quantity label.
  Descriptions without this segment will not have `Quantity` filled.
- **`Kurs … €`** is the only recognised unit-price label.
  Prices in foreign currencies (USD, GBP, etc.) are not captured.
- Descriptions without German broker format (e.g. manual deposits, custom
  descriptions) are silently skipped.
- The `InstrumentCode`/`InstrumentCodeType` columns are not modified by these
  scripts; they are handled by the EF migration
  `AddTransactionInstrumentSnapshots`.
