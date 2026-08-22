# RMO-BIR-M3 Implementation Study

**Date:** 2026-07-09  
**Branch:** `KIRCH-RMO-BIR-M3`  
**Base M2:** `9e984007`  
**Disclaimer:** Narrow contracts + readiness only. No official output claim.

---

## Decision: **GO IMPLEMENT**

| Gate | Result |
|------|--------|
| C# contracts can be added safely | **Yes** — Core models/services, no migration |
| Readiness gate read-only | **Yes** — host-local GET only |
| Schema migration required? | **No** — reuse `bir_machine_profiles` / sequences via existing `IOfficialCheckoutReadinessService` |
| Checkout behavior change? | **No** |
| Official output generated? | **No** |

---

## A. Where should official contracts live?

**`Salryn.Core.Official`** (new namespace under `src/Salryn.Core/Official/`).

| Option | Verdict |
|--------|---------|
| `Salryn.Core.Contracts` | Too generic; mixed with host/client |
| `Salryn.Api.Local.Contracts` | Wrong — contracts must not be API-only |
| **`Salryn.Core.Official`** | **Chosen** — versioned spine next to Core services |

Existing `OfficialDocumentSnapshotDraft*` remains the older draft shape; M3 contracts are the **versioned product spine** (operational vs official number, markers).

---

## B. Can readiness be computed without schema changes?

**Yes.**

| Source | Use |
|--------|-----|
| `SqliteOfficialCheckoutReadinessService` | Active profile + sequence config blocks |
| Product-level constants | Always report missing live spine (checkout wire, EJ live bind, void path, output engine, unresolved Annex F) |

No new tables.

---

## C. Host-local readiness endpoint?

**Yes:** `GET /api/official/readiness`

| Rule | Implementation |
|------|----------------|
| Loopback only | `LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest` |
| Authenticated | Require authenticated session (same as dashboard) |
| No LAN client | No remote route; bridge headers fail loopback guard |
| No mutation | GET only |
| No allocation | Service never touches sequence current_number |

---

## D. React UI?

**Defer.** Non-trivial copy risk; readiness available via API for later UIUX/RMO-M4.

---

## E. Gates

`--only rmo-bir-official-readiness` source checks + full CoreSmoke regression.
