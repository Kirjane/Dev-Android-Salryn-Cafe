# RMO-BIR-M6 — Official Integrity Skeleton (EJ + Void/Refund)

**Date:** 2026-07-09  
**Branch:** `KIRCH-RMO-BIR-M6-INTEGRITY`  
**Base M5:** `fe6c2fb0`  

**Disclaimer:** Integrity status only. **liveOfficialPostingAllowed remains false.** No BIR/RMO compliance claim. No official receipt issuance.

---

## EJ skeleton decision

| Item | Decision |
|------|----------|
| Schema | **Reuse** existing `bir_electronic_journal_*` tables (no M6 migration) |
| Status service | Probes schema presence + row counts (read-only) |
| Live append | **Not** exposed on host integrity API |
| Checkout bind | **Not** implemented |
| Hash chain / export | Reported false; not productized |

## Void/refund skeleton decision

| Item | Decision |
|------|----------|
| Schema | **No** reason-catalog migration |
| Placeholder reasons | Static codes in service (not live-enforced) |
| Live void/refund product path | **Not** implemented |
| Operational POS void | Remains separate (if present elsewhere) |
| Checkout integration | **false** |

---

## Contracts

- `OfficialJournalContracts.cs` — status, event types, append plan, blockers/warnings  
- `OfficialVoidRefundIntegrityContracts.cs` — status, reasons, link status, validation  
- `OfficialIntegrityContracts.cs` — combined host status  

Always false on host surface: live posting, checkout integrated, remote POS official, official output, compliance claimed, checkout allocator.

## Services

- `IHostOfficialIntegrityService.GetStatusAsync`
- `SqliteHostOfficialIntegrityService` — SQLite schema probe + static void/refund placeholders  

## Endpoints (host-local)

| Method | Path |
|--------|------|
| GET | `/api/official/integrity/status` |

Guards: loopback operator + authenticated session. GET-only. No LAN / Remote POS.

## UI

Official Setup → **Official integrity status** card:

- EJ skeleton present / live-bound = no  
- Void/refund skeleton present / live integrated = no  
- Warnings: not live-bound, no official posting  
- No void/refund action buttons  

## Readiness

Blockers retained with clarified text:

- Live official checkout not implemented  
- Official output engine missing  
- EJ skeleton may exist but **not live-bound**  
- Void/refund skeleton may exist but **live integration incomplete**  
- Annex F / retention / eSales unresolved  

Warning: integrity skeleton is status-only.

Never: Compliant / Accredited / Certified / BIRReady / RMOReady / LiveReady.

---

## Checkout disconnection proof

| Component | Official integrity? |
|-----------|---------------------|
| Host checkout | No integrity / EJ / numbering service refs |
| Remote POS checkout | No integrity / official allocator refs |
| Integrity API | Read-only host-local status |

## Remaining blockers

- Live official checkout posting  
- Official number allocation during sale  
- Output engine / Crystal / PDF / printer  
- EJ live bind  
- Void/refund live product path  
- Annex F / eSales / retention  

## Schema

**No migration** in M6.

## Crystal

Deferred (M2 checkpoint). Integrity does not select report engines.

---

## Next lane

**RMO-BIR-M7** — official sale posting dry-run planner (still disconnected from live complete), **or** REPORTS-M1 data-contract-to-report study.
