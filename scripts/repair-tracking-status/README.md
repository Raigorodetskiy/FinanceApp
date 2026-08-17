# TrackingStatus Repair Scripts

Safe production repair tooling for the `TrackingStatus` incident described in
the hotfix PR. **Do not run these scripts automatically or embed production
credentials anywhere.**

## Background

Migration `20260816162000_AddStockTrackingAndMembershipHistory` created the
`TrackingStatus` column with `DEFAULT 1`. EF Core's `HasDefaultValue(Tracked)`
also marked the property as `ValueGeneratedOnAdd`, causing EF to omit
`TrackingStatus` from `INSERT` statements when the application set it to
`CatalogOnly = 0` (the CLR default for `int`). MySQL then substituted the
column default `1`, silently overwriting every new index constituent with
`Tracked`. All 598 stocks appeared in the main Stocks view.

The code fix (`ValueGeneratedNever()` + new migration
`20260817000000_FixTrackingStatusValueGenerated`) prevents this from happening
going forward. These scripts repair the existing production data.

---

## Deployment order

```
1. systemctl stop financeapp.service
2. Take a full database backup
3. git pull / deploy the new build
4. dotnet ef database update  (applies migration 20260817000000)
5. Run 01-audit-preview.sql   (read-only, review counts)
6. Perform 00-extract-baseline-stocks.md  (one-time, per-incident)
7. Run 02-apply-repair.sql    (set @confirm = 1)
8. Run 03-post-repair-verify.sql
9. If verify shows problems → 04-rollback.sql
10. systemctl start financeapp.service
```

---

## Files

| File | Description |
|------|-------------|
| `00-extract-baseline-stocks.md` | How to extract pre-import stock identities from the baseline backup into a staging DB. Run once before the repair. |
| `01-audit-preview.sql` | Read-only: shows TrackingStatus distribution, user-owned stocks, and demotion candidates. |
| `02-apply-repair.sql` | Demotes index-only Tracked stocks to CatalogOnly. Requires `@confirm = 1`. Logs changes for rollback. |
| `03-post-repair-verify.sql` | Verifies the repair completed correctly and no user-owned stocks were affected. |
| `04-rollback.sql` | Restores previous TrackingStatus values from the audit log. Requires `@confirm = 1`. |

---

## Demotion criteria (conservative)

A stock is demoted **only if all** of the following are true:

- `TrackingStatus = 1` (currently Tracked)
- Is an active index constituent (`EffectiveTo IS NULL`)
- Has **no** rows in `PortfolioItems`, `Orders`, or `Transactions`
- Is **not** present in the pre-import baseline backup allowlist

Any stock that is ambiguous (e.g. promoted after import, no baseline data) is
left as `Tracked`.
