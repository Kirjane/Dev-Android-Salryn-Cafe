# RMO-BIR-M9 Code Study

**Date:** 2026-07-10  
**Branch:** `KIRCH-RMO-BIR-M9-FULL-CLOSURE`  
**Base M8:** `aef6b9fb`  

**Disclaimer:** Internal engineering study only. No compliance claim.

---

## RMO spine inventory

| Area | Location | Status |
|------|----------|--------|
| Contracts | `src/Salryn.Core/Official/*` | Setup, readiness, numbering, integrity, dry-run plan/package, report preview, spine, void/refund, sale document |
| Services (core) | `src/Salryn.Core/Services/*Official*` | Readiness, report builders, X/Z, evidence, output contracts (mostly engineering) |
| Infrastructure | `src/Salryn.Infrastructure/Data/Services/*Official*` + dry-run package/planner | SQLite host services |
| API | `src/Salryn.Api.Local/LocalOfficial*` | Host-local loopback + auth endpoints |
| UI | `OfficialSetupPage.tsx`, `officialSetup.ts` | Admin official setup + internal dry-run panels |
| Migrations | v13 receipt profiles; v14 dry-run packages | `SupportedMigrationVersion = 14`; DB-A2 future probe 15 |
| Checkout | `HostOperatorCheckoutApiService`, `RemotePosCheckoutService`, POS services | **No** official allocator / dry-run / package / numbering references (gated) |

---

## Checkout / sales path

- Operational sales: `sales` + line/payment tables via `SqliteSaleWriteRepository` / `SqliteSaleReadRepository`.
- Read model: `SaleDetailReadModel` with lines, payments, totals, status, void fields — **safe for read-only adapter**.
- Transaction / receipt numbers: operational POS allocators (`HostOperatorReceiptNumberAllocator`, Remote POS allocator) — **not** official document sequence.
- Shift open/close: host shift services — independent of official series.
- Inventory mutation: sale write path only — dry-run must never call write repos.
- Audit / EJ: append ports exist; dry-run must never call live append.

---

## Database

| Object | Notes |
|--------|-------|
| `official_receipt_profiles` (v13) | Receipt markers, dry-run only |
| `official_dry_run_packages` (v14) | Already has `source_mode`, `source_sale_id`, forced live flags = 0 |
| Document sequences | Official sequence tables; allocation service exists but not checkout-wired |
| EJ tables | Schema present; live bind missing |
| Sale tables | Operational only |

**No new migration required** for source-sale read-only: reuse v14 `source_mode` / `source_sale_id`.

---

## Answers

### Can a source-sale read-only adapter be implemented safely?

**Yes.** Use `ISaleReadRepository.FindSaleByIdAsync` (or equivalent SELECT-only path). Map to dry-run request lines/totals. Persist package with `source_mode = OperationalSaleReadOnly`. Never open write transactions on sales/sequence/EJ/inventory/shift.

### Can official sale posting be designed without refactoring checkout?

**Yes.** Design a separate host service that would run *after* operational sale post (or as a future optional branch), with its own atomic unit of work (allocate number + official sale row + EJ). Checkout remains unchanged until an explicit later gate.

### Can a guarded official posting service be implemented disabled-by-default?

**Yes as always-blocked candidate skeleton.** Legal closure incomplete → service must return blocked for all activation paths; never called from checkout; no UI live button.

### What schema is missing?

For **live** posting (future): immutable official sale header/lines, official number bind to sale, EJ foreign keys, grand-total accumulator/reset counter, void/refund adjustment series tables, optional eSales outbox. **Not** implemented in M9.

### What would be dangerous to implement now?

- Checkout bind to official allocator
- Sequence mutation during sale
- Live EJ append without full invariants
- “Go live” UI
- Crystal/PDF “official” output without legal seal/push/output closure
- Claiming compliance from partial Annex F text

### Is Crystal necessary?

**No evidence it is required.** Annex F/RMO require reports and receipt content, not Crystal Reports specifically. Internal HTML/text/JSON previews suffice for engineering.

### What reports can be built internally without Crystal?

Receipt/invoice **preview**, X/Z **preview**, EJ export **preview**, void/refund **preview**, sales summary **preview** — watermarked, non-official, no file/printer/PDF legal issuance.

---

## Risk notes

- M8 package `ToContract` hardcoded `SourceMode: Synthetic` on load — must fix when adding operational source mode.
- GetById must load `source_sale_id` / `source_mode` columns.
- M8 gate asserts `SourceSaleReadOnlyDeferred` wording — M9 updates readiness warning to available/non-mutating.
