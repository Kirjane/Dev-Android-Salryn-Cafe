# RMO-BIR-M7 Spine Sprint Study

**Date:** 2026-07-09  
**Branch:** `KIRCH-RMO-BIR-M7-SPINE-SPRINT`  
**Base M6:** `09bfc618`  

**Disclaimer:** Pre-live internal package only. No live official posting. No compliance claim.

---

## Decision: **GO IMPLEMENT**

| Gate | Result |
|------|--------|
| No schema migration | **Yes** |
| No checkout mutation | **Yes** |
| Pure dry-run planner | **Yes** — synthetic request only |
| Host-local endpoints | **Yes** |
| No official number consumption | **Yes** |
| No live EJ append | **Yes** |
| No official output | **Yes** |

---

## A. Current RMO spine shape

| Component | State (M6 tip) |
|-----------|----------------|
| Setup/profile | Host APIs + UI (M4); forces NOT_ACCREDITED |
| Readiness | Host readiness + product spine blockers (M3/M4) |
| Numbering | Preview/validate only (M5) |
| Integrity | EJ + void/refund status skeleton (M6) |
| UI | Official Setup page with setup/numbering/integrity |
| Source gates | `rmo-bir-official-{readiness,setup,numbering,integrity}` |

## B. Missing before reports/output

- Dry-run official sale package planner  
- Report data handoff contracts (no engine)  
- Aggregated spine status  
- Explicit not-live-bound / disconnected proof package  

## C. Dry-run without checkout mutation

**Yes.** Planner builds an in-memory plan package only. No sequence/sales/EJ writes.

## D. Live sales records?

**Preferred:** synthetic/internal dry-run payload only.  
`sourceSaleId` **deferred** (avoid checkout/read-path coupling risk).

## E. Schema

**No migration.** No sales/sequence/EJ mutation.

## F. UI/API

- `GET /api/official/spine/status`  
- `POST /api/official/dry-run/posting-plan`  
- Official Setup: spine status + dry-run planner cards  
- No print/generate/issue/accredit buttons  

## Checkout disconnection proof

Host checkout and Remote POS do not reference setup/numbering/integrity/spine/dry-run services. Operational numbers remain separate.
