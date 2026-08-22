# RMO-BIR-M6-INTEGRITY Study

**Date:** 2026-07-09  
**Branch:** `KIRCH-RMO-BIR-M6-INTEGRITY`  
**Base M5:** `fe6c2fb0`  

**Disclaimer:** Integrity skeleton only. No live official posting. No compliance claim.

---

## Decision: **GO IMPLEMENT** (preferred safe scope)

| Gate | Result |
|------|--------|
| Skeleton without checkout change | **Yes** |
| Host-local API only | **Yes** |
| Readiness stays non-compliant | **Yes** |
| No official posting enabled | **Yes** |
| No official sale mutation | **Yes** |
| Schema migration required | **No** |

---

## A. Existing electronic journal support

| Layer | State |
|-------|--------|
| Tables | `bir_electronic_journal_entries`, `*_entry_events`, `*_preservation_batches` (migration v4+) |
| Append services | J21-5x shells/participants exist; infrastructure smoke covers append |
| Host checkout | **Does not** bind EJ append to sale complete |
| M3 contracts | `OfficialJournalEventContract` (spine placeholder) |
| Hash chain / export | Not productized as host-official live path |

**M6 decision:** Report schema presence via read-only probe. Do **not** append from host integrity API. Do **not** bind checkout.

---

## B. Existing void/refund/cancel support

| Layer | State |
|-------|--------|
| Operational sales void | POS-STAB / inventory void paths exist for operational sales |
| Host React checkout | No void/refund official action wired |
| Official void/refund | Lifecycle/audit event smoke paths exist for EJ events; **not** product host UI/checkout |
| Reason catalog | No dedicated official reason catalog table |
| Posted sale immutability (official) | Not productized for official documents |

**M6 decision:** Status + static placeholder reason codes only. No sales mutation. No void/refund action API.

---

## C. Safe M6 scope (chosen)

- Host integrity contracts (journal status + void/refund status)  
- `IHostOfficialIntegrityService` read-only status  
- `GET /api/official/integrity/status` host-local  
- Readiness blocker text: skeleton present / not live-bound  
- Official Setup integrity status card  
- **No** schema migration  
- **No** live EJ append  
- **No** void/refund mutation  

---

## D. API boundary

| Method | Path | Mutates? |
|--------|------|----------|
| GET | `/api/official/integrity/status` | No |

Always: `liveOfficialPostingAllowed=false`, `checkoutIntegrated=false`, `remotePosOfficialAllowed=false`, `officialOutputImplemented=false`, `complianceClaimed=false`.

---

## E. UI boundary

Official Setup integrity card: EJ status, void/refund status, not live-bound, no official posting. No void/refund buttons. No print/PDF/Crystal.

---

## F. Readiness

| Capability | Readiness treatment |
|------------|---------------------|
| EJ schema present | Warning/capability: skeleton present |
| EJ live-bound | **Blocker remains** (not live-bound) |
| Void/refund skeleton | Capability present |
| Void/refund live | **Blocker remains** (not productized / not live-bound) |
| Live official checkout | Blocker remains |
| Output engine | Blocker remains |
| Annex F / eSales | Blocker remains |

Never: Compliant / Accredited / Certified / BIRReady / RMOReady / LiveReady.

---

## Checkout disconnection proof

| Component | Official EJ/void? |
|-----------|-------------------|
| `HostOperatorCheckoutApiService` | Operational complete only |
| `RemotePosCheckoutService` | Operational RPOS only |
| Integrity service | Status probe only |
| Integrity API | Host-local read-only |
