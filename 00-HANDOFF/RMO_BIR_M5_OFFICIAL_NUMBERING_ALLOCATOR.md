# RMO-BIR-M5 — Official Numbering Allocator Skeleton

**Date:** 2026-07-09  
**Branch:** `KIRCH-RMO-BIR-M5`  
**Base M4:** `b8250be3`  

**Disclaimer:** Preview/validate only. **liveOfficialPostingAllowed remains false.** No BIR/RMO compliance claim. No official receipt issuance.

---

## Allocation model

| Number type | Owner | M5 behavior |
|-------------|-------|-------------|
| Operational POS | `HostOperatorReceiptNumberAllocator` | Unchanged; host checkout only |
| Remote POS | `RemotePosReceiptNumberAllocator` | Unchanged; never official |
| Official document | `bir_official_document_sequences` | **Preview/validate only** — no mutation |

Invariants (documented; live wiring later): dual numbering; no draft allocation; no reuse after commit; reprint same number; void/refund link original; remote cannot allocate; fail closed on exhausted range.

---

## Contracts

`Salryn.Core.Official.OfficialNumberingContracts`:

- `OfficialNumberingPreviewContract`
- `OfficialNumberingSeriesStateContract`
- `OfficialNumberingValidationResult`
- `OfficialNumberingBlocker`
- `OfficialNumberingAllocationRequest` / `Result` (shape only; live allocate not exposed)
- `OfficialNumberingAllocationMode`: PreviewOnly | InternalDryRun | LiveOfficialBlocked

Always false on host surface: `LiveOfficialPostingAllowed`, `OfficialCheckoutAllocationEnabled`, `CheckoutUsesAllocator`, `AllocationAllowed` (live), `PreviewMutatesSequence`.

---

## Service

- `IHostOfficialNumberingService`
  - `GetPreviewAsync` — compute next = current + 1, format prefix+pad; **no write**
  - `ValidateSeriesAsync` — series field/guard checks; **no write**
- `SqliteHostOfficialNumberingService` — SQLite read of active machine profile + series

**Not present on host interface:** live allocate, internal dry-run allocate.

Existing J21-3B `OfficialDocumentSequenceAllocationService` remains in tree for future post-path work; host checkout / Remote POS / M5 APIs do not call it.

---

## API (host-local only)

| Method | Path | Mutates series? |
|--------|------|-----------------|
| GET | `/api/official/numbering/preview` | No |
| POST | `/api/official/numbering/validate` | No |

Guards: loopback operator + authenticated session.  
No dry-run-allocate endpoint. No LAN / Remote POS routes.

Response flags always:

- `liveOfficialPostingAllowed: false`
- `officialCheckoutAllocationEnabled: false`
- `checkoutUsesAllocator: false`
- official output not implemented / compliance not claimed

---

## UI

Official Setup page:

- Numbering preview card (current/next/formatted, blockers)
- Validate series button (POST validate; no allocate)
- Warning: preview does not issue official numbers; checkout not connected
- **No** allocate button

---

## Readiness

- Series completeness now requires active + not exhausted
- Warning: numbering preview-only; checkout disconnected
- Product spine blockers unchanged (live checkout, output engine, EJ, void/refund, Annex F)
- `liveOfficialPostingAllowed` remains **false**
- Status may be `ConfiguredForInternalDryRun`; never Compliant/Accredited/LiveReady

---

## What preview does

Reads active series; if valid, shows next official number format. Does not advance `current_number`.

## What validate does

Same series checks as preview; returns `isValidForPreview` and blockers. Does not allocate.

## Internal dry-run allocation

**Not added** (preferred safe scope).

---

## Live checkout disconnected proof

| Component | Official series? |
|-----------|------------------|
| `HostOperatorCheckoutApiService` | No reference to numbering service / J21 allocate |
| `RemotePosCheckoutService` | Operational RPOS numbers only |
| Series setup save | `current = start - 1` only |
| Numbering API | Read-only |

---

## Remaining blockers

- Live official checkout posting  
- Official number allocation during sale  
- Output engine / Crystal / PDF / printer  
- EJ live bind  
- Void/refund integrity product path  
- Annex F / retention / eSales  

## Schema

**No migration** in M5.

## Gate note

Full CoreSmoke previously false-failed `LanM16QaBRemotePosPairingGateChecks` after M4 added `Migrations/` on HEAD (freehand window scanned to `HEAD`). M5 freezes that migration/seed ban to freehand→Sandbox boundary `1f74cd8b` so later product lanes are not blocked.

---

## Next lane

**RMO-BIR-M6** — electronic journal live-bind skeleton **or** void/refund integrity skeleton, still disconnected from official posting.
