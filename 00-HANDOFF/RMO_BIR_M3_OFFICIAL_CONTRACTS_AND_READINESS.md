# RMO-BIR-M3 — Official Contracts and Host Readiness

**Date:** 2026-07-09  
**Branch:** `KIRCH-RMO-BIR-M3`  
**Base M2:** `9e984007`  

**Disclaimer:** Contracts and readiness only. **No** BIR/RMO compliance claim. **No** official receipt output.

---

## Contracts added

Namespace: `Salryn.Core.Official`

| Contract | Purpose |
|----------|---------|
| `OfficialSaleDocumentContract` | Versioned official sale document shape (future REPORTS-M1) |
| `OfficialSaleLineContract` / tax / payment / customer / discount | Line and breakdown shapes |
| `OfficialSaleDocumentMarkers` | Training, reprint, void, refund, non-official flags |
| `OfficialNumberingKind` | **OperationalPosNumber** vs **OfficialDocumentNumber** |
| `OfficialBusinessProfileSnapshot` / `OfficialMachineProfileSnapshot` / `OfficialReceiptProfileSnapshot` | Config snapshots |
| `OfficialDocumentSeriesContract` | Series metadata (no allocation) |
| `OfficialJournalEventContract` / audit / void-refund links | Integrity placeholders |
| `HostOfficialReadinessSnapshot` | Product readiness response |

`ContractVersion` / `OfficialContractVersions.M3 = 1`.

---

## Readiness gate

| Item | Value |
|------|--------|
| Service | `HostOfficialReadinessService` |
| Endpoint | `GET /api/official/readiness` |
| Auth | Loopback + authenticated host session |
| Mutation | **None** |
| Number allocation | **None** |
| Live posting allowed | **Always false** |
| Allowed actions | `ViewReadiness` only |

### Status values

- `Disabled` — config not accredited / output not enabled  
- `NotReady` — config incomplete or other  
- `ReadyForInternalDryRun` — existing `IOfficialCheckoutReadinessService` config gates pass; **still no live official posting**  
- `Unknown`

### Always-present product blockers

- Live official checkout not implemented  
- Official output engine not implemented  
- Live EJ binding missing  
- Void/refund integrity incomplete  
- Annex F / retention / eSales unresolved  

### Warnings

- Crystal not selected  
- Compliance not claimed  
- Operational POS numbers remain non-official  

---

## Host-local boundary

- Endpoint is **host-local only** (loopback operator console)
- `LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest`  
- Remote bridge headers rejected  
- Not under `/api/lan-control` or Remote POS routes  

---

## Still missing (by design)

- Live official checkout wiring  
- Official number allocation on complete  
- Receipt/PDF/print/Crystal  
- Config UI  
- Schema migrations (none in M3)  

---

## Why no official output claim

M3 freezes **data shapes** and a **readiness diagnostic**. It does not issue accountable documents, does not enable official mode on checkout, and explicitly reports `liveOfficialPostingAllowed: false`.

## Why Crystal remains deferred

Per M2 checkpoint: contracts must land first; engine choice stays text/HTML/PDF-first. Crystal not selected.

---

## Next implementation lane

**RMO-BIR-M4 — Official configuration profile/sequence admin (host UI) + readiness-driven setup**, still without live official checkout posting unless separately approved.
