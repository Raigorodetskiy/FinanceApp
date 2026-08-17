# TrackingStatus Repair Runbook (follow-up to PR #135)

Urgent production-safety follow-up for the merged TrackingStatus fix.

> Do not execute SQL automatically.
> Keep `financeapp.service` stopped for the full repair window.

## Scope and non-scope

- Keeps code-level persistence fix/migration (`ValueGeneratedNever` +
  `20260817000000_FixTrackingStatusValueGenerated`) intact.
- Repairs only `Stocks.TrackingStatus` for strictly validated candidates.
- Never deletes prices/history/fundamentals/memberships.
- Does not modify index provider/snapshot logic.

## Required operator sequence

1. Stop service: `systemctl stop financeapp.service`
2. Create and verify full production backup.
3. Deploy merged backend and apply migration `20260817000000`.
4. Verify the baseline backup checksum, restore it into isolated
   `financeapp_baseline_audit`, and run the read-only schema checks from
   `00-extract-baseline-stocks.md`.
5. Export/load the baseline allowlist with the guarded workflow in
   `00-extract-baseline-stocks.md`:
   - export exactly `Ticker, Exchange, Isin, Wkn, ProviderSymbol`
   - if baseline `Stocks.ProviderSymbol` is absent, export `NULL AS ProviderSymbol`
   - hard-abort if required legacy columns are missing
   - create `financeapp_repair_audit` before connecting to it
   - prefer `LOAD DATA LOCAL INFILE` with `mysql --local-infile=1`
   - if `LOCAL` is unavailable, use the documented client-side INSERT fallback
   - validate restored row count == loaded row count before preview/apply
   - promote staged rows atomically so failed validation does not destroy the
     previous validated baseline
6. Record the validated baseline load id / count / backup SHA-256 provenance.
7. Run `01-audit-preview.sql` and record:
   - candidate count
   - deterministic candidate checksum
   - baseline row count used for validation
   - intended explicit `RepairRunId`
8. Human review of candidate list, protected rows, ambiguous rows.
9. Edit `02-apply-repair.sql` variables explicitly:
   - `@confirm = 1`
   - `@ack_service_stopped = 1`
   - `@repair_run_id = '...'`
   - `@expected_candidate_count = ...`
   - `@expected_candidate_checksum = '...'`
   - `@expected_baseline_count = ...`
10. Run `02-apply-repair.sql`.
11. Run `03-post-repair-verify.sql` with the same `@repair_run_id`.
12. If verification fails, run `04-rollback.sql` for that exact run id (with
    explicit expected rows).
13. Restart service only after verification passes:
    `systemctl start financeapp.service`.

## Safety guarantees in scripts

- Hard abort via `SIGNAL SQLSTATE '45000'` (no divide-by-zero guard hacks).
- Baseline DB/table must exist and be non-empty; malformed/duplicate baseline
  rows are rejected.
- Apply requires explicit confirmation, service-stop acknowledgement, migration
  present, expected count, expected checksum, expected baseline count.
- Candidate set is snapshotted atomically in apply and update touches only that
  snapshot.
- Apply verifies `ROW_COUNT == expected candidate count`; mismatch aborts and
  transaction rolls back.
- Audit log rows are immutable per run and unique on `(RepairRunId, StockId)`.
- Rollback is scoped to explicit `RepairRunId`; it cannot restore other runs.
- The baseline extraction workflow keeps the previous validated allowlist until a
  new staged load passes validation and is promoted atomically.

## Files

| File | Purpose |
|---|---|
| `00-extract-baseline-stocks.md` | Safe baseline restore/export/load validation workflow. |
| `01-audit-preview.sql` | Read-only preview: candidates/protected/ambiguous + count/checksum. |
| `02-apply-repair.sql` | Guarded apply: snapshot + audit insert + update in one transaction. |
| `03-post-repair-verify.sql` | Run verification checks scoped by `RepairRunId`. |
| `04-rollback.sql` | Guarded rollback for one selected `RepairRunId`. |
