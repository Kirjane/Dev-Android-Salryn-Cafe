# RMO-BIR-M7 Spine Sprint and Quality

**Date:** 2026-07-09  
**Branch:** `KIRCH-RMO-BIR-M7-SPINE-SPRINT`  
**Base M6:** `09bfc618`  

**Disclaimer:** Pre-live package. **liveOfficialPostingAllowed remains false.** No compliance claim. Crystal deferred.

---

## Spine status added

- `OfficialSpineStatusContracts` + `IHostOfficialSpineStatusService` / `SqliteHostOfficialSpineStatusService`
- Aggregates setup, series, numbering preview, integrity/EJ/void skeletons, dry-run planner, report contracts
- Output engine + live checkout binding remain **Missing**

## Dry-run planner added

- `OfficialDryRunPostingContracts` + `IHostOfficialDryRunPostingPlanner` / `SqliteHostOfficialDryRunPostingPlanner`
- Synthetic request only (`sourceSaleId` deferred)
- Integrates numbering preview, EJ plan (not live), void/refund plan (not live), report package readiness
- Flags always false: sequence/sales mutate, EJ live append, output generate, live posting, compliance

## Report data contracts added

- `OfficialReportDataContracts` — receipt/X/Z/journal export/void-refund data shapes + output readiness
- `reportEngineSelected=false`, `crystalSelected=false`, `outputGenerated=false`, `officialReceiptIssued=false`

## Endpoint / UI

| Method | Path |
|--------|------|
| GET | `/api/official/spine/status` |
| POST | `/api/official/dry-run/posting-plan` |

Host-local only. Official Setup: **RMO spine status** + **Official dry-run planner** cards. Button: **Build dry-run plan** only.

## Readiness

Warnings added: dry-run planner available not live-bound; report data contracts defined, no engine.  
Live blockers unchanged (checkout, output, EJ live bind, void/refund live, Annex F).

## Quality cleanup

- Repeated truth flags forced false in endpoint DTO projection (not trust client)
- Synthetic empty-body dry-run request defaulted on API
- Consistent internal dry-run / no-compliance wording across UI/API/services
- No broad checkout/LAN/installer refactor

## Checkout disconnected proof

Host checkout and Remote POS do not reference spine/dry-run/numbering/integrity services. No official allocation during sale.

## Schema

**No migration.**

## Next lane

**REPORTS-M1** — data-contract-to-internal-report implementation (non-Crystal first), still not official issuance.
