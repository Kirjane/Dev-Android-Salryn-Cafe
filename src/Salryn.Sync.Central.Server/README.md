# Salryn Cafe — Central Sync Server (`Salryn.Sync.Central.Server`)

## Architecture & Containment
`Salryn.Sync.Central.Server` supports two explicit modes controlled by `CentralServerOptions.EnableCanonicalDomainProcessing`:
- **Default (`false` — Transport-Only Containment):** Stores transport receipts and returns `503 ERR_BOOTSTRAP_DOMAIN_SNAPSHOT_UNAVAILABLE` for canonical bootstrap.
- **Canonical Mode (`true`):** Initializes the reconciled Cafe SQLite schema (v43) at the **same physical database file** before transport tables, scoped to `(CanonicalTenantId, CanonicalStoreId)`. Existing unclassified transport-only, retail, or foreign-store databases fail closed without mutation. Interrupted fresh initialization recovers cleanly via `.canonical-init-pending` only when `sync_canonical_domain_scope` and `sync_server_state` are empty.

## Security & Operator Authority
- Device authentication (enrollment, approval, challenge-response asymmetric token) is strictly separated from operator authority.
- Canonical `/api/sync/v1/push` requires both device bearer auth and an independent operator token (`X-Salryn-Operator-Token`) obtained via `POST /api/sync/v1/operators/login`.
- Operator sessions are hash-stored, bound to `(tenant, store, device, user, server_epoch, authority_hash)`, rate-limited (5-attempt lockout), and immediately invalidated on `POST /api/sync/v1/operators/logout` or role/permission/password changes.

## Supported vs. Unsupported Operation Families
- **Canonically Applied (`Applied` + `sync_change_feed` in one SQLite transaction):**
  - `OrderCompletedV1`
  - `SaleCommittedV1`
  - `SaleCommittedV2` (via `SharedSqliteCompletedSaleWriteParticipant.ExecuteC5Async` + `SharedSqliteCafeFinancialParticipant.ExecuteAsync`)
  - Cash Movement, Refund, Inventory, and Ingredient Consumption canonical operations
- **Explicitly Unsupported (Fail-Closed `NeedsReview` / `DOMAIN_PROCESSOR_UNAVAILABLE`, zero `sync_change_feed` emission):**
  - `SaleCommittedV3`, Delivery, Booking, Attendance, and Incident operation families. Central **never** fabricates `Applied` for unsupported families.

## Verification
```powershell
dotnet test tests/Salryn.Cafe.I6ATests/Salryn.Cafe.I6ATests.csproj
```
Verified 28/28 `Salryn.Cafe.I6ATests` PASS, including live HTTPS `HostToCentralLiveAcceptanceTests` (canonical sale, duplicate replay, tampered payload rejection, stale-epoch fencing, unsupported `NeedsReview`, canonical bootstrap/pull, operator logout revocation, and restart persistence).
