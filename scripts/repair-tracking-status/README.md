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
4. Restore baseline backup into isolated DB (`financeapp_baseline_audit`) per
   `00-extract-baseline-stocks.md`.
5. Export/load baseline identities correctly (Ticker, Exchange, Isin, Wkn,
   ProviderSymbol), normalize `\N`/whitespace, validate row count > 0 and no
   duplicate normalized identities.
6. Run `01-audit-preview.sql` and record:
   - candidate count
   - deterministic candidate checksum
   - baseline row count used for validation
   - intended explicit `RepairRunId`
7. Human review of candidate list, protected rows, ambiguous rows.
8. Edit `02-apply-repair.sql` variables explicitly:
   - `@confirm = 1`
   - `@ack_service_stopped = 1`
   - `@repair_run_id = '...'`
   - `@expected_candidate_count = ...`
   - `@expected_candidate_checksum = '...'`
   - `@expected_baseline_count = ...`
9. Run `02-apply-repair.sql`.
10. Run `03-post-repair-verify.sql` with the same `@repair_run_id`.
11. If verification fails, run `04-rollback.sql` for that exact run id (with
    explicit expected rows).
12. Restart service only after verification passes:
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

## Files

| File | Purpose |
|---|---|
| `00-extract-baseline-stocks.md` | Safe baseline restore/export/load validation workflow. |
| `01-audit-preview.sql` | Read-only preview: candidates/protected/ambiguous + count/checksum. |
| `02-apply-repair.sql` | Guarded apply: snapshot + audit insert + update in one transaction. |
| `03-post-repair-verify.sql` | Run verification checks scoped by `RepairRunId`. |
| `04-rollback.sql` | Guarded rollback for one selected `RepairRunId`. |
