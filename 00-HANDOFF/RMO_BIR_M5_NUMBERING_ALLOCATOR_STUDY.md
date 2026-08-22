# RMO-BIR-M5 Numbering Allocator Study

**Date:** 2026-07-09  
**Branch:** `KIRCH-RMO-BIR-M5`  
**Base M4:** `b8250be3`  

**Disclaimer:** Skeleton design only. No live official posting. No compliance claim.

---

## Decision: **GO IMPLEMENT** (preferred safe scope)

| Gate | Result |
|------|--------|
| Existing series schema sufficient | **Yes** — `bir_official_document_sequences` v3 |
| Migration required | **No** |
| Allocator disconnected from checkout | **Yes** — host preview/validate only |
| Host-local API boundary clear | **Yes** — same loopback + session pattern as M4 |
| Live official posting stays off | **Yes** — flags always false |
| Sale flow consumes numbers | **No** — host/Remote POS keep operational allocators |

---

## A. Existing sequence storage

| Field | Role |
|-------|------|
| `bir_machine_profile_id` | Series ownership (FK machine profile) |
| `document_kind` | `OFFICIAL_RECEIPT` / `OFFICIAL_INVOICE` |
| `sequence_scope_code` | `LOCAL_SINGLE_STORE` MVP |
| `prefix`, `year`, `start_number`, `current_number`, `end_number`, `padding_digits` | Deterministic range |
| `is_active`, `is_exhausted` | Guardrails; exhausted rows inactive |

**Semantics (setup M4):** save sets `current_number = start_number - 1` so first *future* allocation would be `start`. Preview computes `next = current + 1` without writing.

**Concurrency:** live allocation (later) must use existing shared-transaction participant (`SharedSqliteOfficialDocumentSequenceAllocationParticipant`). M5 does **not** mutate sequences.

**Existing allocator:** `OfficialDocumentSequenceAllocationService` (J21-3B) advances sequence when ACCREDITED + `official_output_enabled=1`. **Not called** by host checkout, Remote POS, or M5 host APIs. M5 does not expose that mutator on the host operator surface.

---

## B. Allocation invariants (documented for later wiring)

1. Operational POS number ≠ official document number.  
2. Official allocation only after host-authorized post point (not in M5).  
3. No draft/cart allocation.  
4. No reuse after committed allocation.  
5. Reprint keeps same number.  
6. Void/refund links to original.  
7. Failure/crash: allocate only inside post transaction with rollback.  
8. Remote client cannot allocate independently.  
9. Admin/Owner remote official posting remains forbidden.  
10. Exhausted series fails closed (no wrap).

---

## C. Safe M5 scope (chosen)

**Preferred safe scope:**

- Contracts for preview / series state / validation / blocked live modes  
- `IHostOfficialNumberingService` with `GetPreviewAsync` + `ValidateSeriesAsync`  
- **No** sequence mutation  
- **No** internal dry-run allocate endpoint  
- **No** live allocation method on the host interface  

**Rejected for M5:** dry-run allocate that mutates `current_number` (risk of consuming ranges before product spine exists).

---

## D. API boundary

| Method | Path | Mutates? |
|--------|------|----------|
| GET | `/api/official/numbering/preview` | No |
| POST | `/api/official/numbering/validate` | No |

- Host-local loopback + authenticated session  
- No LAN / Remote POS routes  
- Responses always: `liveOfficialPostingAllowed=false`, `officialCheckoutAllocationEnabled=false`, `checkoutUsesAllocator=false`  
- No dry-run-allocate endpoint  

---

## E. UI boundary

Official Setup page (low risk):

- Numbering preview card (next formatted number, validation status)  
- Warning: preview does not issue official numbers; checkout not connected  
- **No** allocate button  

---

## F. Schema decision

**No migration.** Reuse `bir_official_document_sequences` as configured by M4 setup.

---

## Checkout disconnection proof (source)

| Path | Allocator |
|------|-----------|
| Host checkout | `HostOperatorReceiptNumberAllocator` (operational) |
| Remote POS | `RemotePosReceiptNumberAllocator` (RPOS) |
| Host numbering API | Preview/validate only; does not call J21-3B `AllocateNextAsync` |
| Host setup series save | Sets range start; `current = start - 1`; not sale allocation |
