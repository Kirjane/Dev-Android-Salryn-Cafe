# RMO-BIR-M4-BATCH — Official Setup Admin

**Date:** 2026-07-09  
**Branch:** `KIRCH-RMO-BIR-M4-BATCH`  
**Base M3:** `f0725eb5`  

**Disclaimer:** Configuration setup only. **liveOfficialPostingAllowed remains false.** No BIR/RMO compliance claim. No official receipt issuance.

---

## Schema / storage

| Object | Change |
|--------|--------|
| `bir_machine_profiles` | **Reused** for business + machine setup |
| `bir_official_document_sequences` | **Reused** for series setup (`current = start - 1`; not checkout allocation) |
| `official_receipt_profiles` | **Added** (migration v13) for title/footer/markers |

Setup always forces:

- `compliance_mode = NOT_ACCREDITED`
- `official_output_enabled = 0`

---

## Contracts / services

- `OfficialSetup*Contract` / save requests in `Salryn.Core.Official`
- `IHostOfficialSetupService` + `SqliteHostOfficialSetupService`

## Endpoints (host-local only)

| Method | Path |
|--------|------|
| GET | `/api/official/setup` |
| PUT | `/api/official/setup/business` |
| PUT | `/api/official/setup/machine` |
| PUT | `/api/official/setup/receipt` |
| PUT | `/api/official/setup/series` |

Guards: loopback + authenticated session. Responses include warnings that official output is not implemented.

## UI

- React page **Official Setup** (`/official-setup` page id)
- Warning banner: not compliant / official output not implemented
- Forms: business, machine, receipt, series
- Readiness card from `/api/official/readiness`
- **No** official print/generate/Crystal controls

## Readiness rules

| Status | Meaning |
|--------|---------|
| Disabled | No meaningful setup |
| SetupIncomplete | Partial setup |
| ConfiguredForInternalDryRun | Required setup fields filled; product spine still blocked |
| Never | Compliant / Accredited / LiveOfficialReady |

Always false: `liveOfficialPostingAllowed`.

Always-on product blockers remain (checkout wire, output engine, EJ, void/refund, Annex F).

## What remains missing

- Live official checkout posting  
- Official number allocation during sale  
- Output engine / Crystal / PDF / printer  
- Full audit log write for every setup edit  
- Annex F / eSales / retention resolution  

## Why no official output claim

Setup stores metadata and series **starting points** only. It does not issue documents, does not enable ACCREDITED mode via UI, and readiness never allows live posting.

## Why Crystal remains deferred

Per M2 checkpoint — contracts/setup first; engine choice later.

---

## Verification (completed)

| Check | Result |
|-------|--------|
| CoreSmoke `--only rmo-bir-official-setup` | Pass |
| Full CoreSmoke | Pass |
| DB-A2 `SupportedMigrationVersion` | Updated to **13**; critical table `official_receipt_profiles` |
| Live official posting | **Not implemented** (`liveOfficialPostingAllowed` always false) |

## Next lane

**RMO-BIR-M5 — Official numbering allocator skeleton (still disconnected from live checkout).**
