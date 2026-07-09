# RMO-BIR-M2 Official Numbering Design Study

**Date:** 2026-07-09  
**Branch:** `KIRCH-RMO-BIR-M2`  
**Sources:** S2 F-h; S2 invoice number rules; existing `bir_official_document_sequences`  
**Disclaimer:** Proposed design only. Not compliance certification.

---

## 1. Current Salryn state

| Layer | Behavior |
|-------|----------|
| Host checkout | `HostOperatorReceiptNumberAllocator` — **non-official** in-process POS prefix numbers |
| Remote POS | Separate RPOS-style allocator — **must stay non-official** |
| Official sequence table | `bir_official_document_sequences` (prefix, year, start/current/end, padding ≥ 6, kind OR/Invoice, FK machine profile) |
| Official allocator | Service exists; gated on active ACCREDITED profile + `official_output_enabled` |
| Live wiring | **Not** used by host checkout complete path |

---

## 2. Proposed series model

### Series dimensions

| Dimension | Proposal |
|-----------|----------|
| Owner | One sequence row owned by **one** `bir_machine_profile_id` |
| Document kind | `OFFICIAL_RECEIPT` **or** `OFFICIAL_INVOICE` (separate rows; never share counters) |
| Scope | `LOCAL_SINGLE_STORE` (MVP); no multi-tenant share |
| Terminal scope | **No per-terminal official series** in MVP — host owns single series per kind per machine profile |
| Format | `{prefix?}{zero-padded number}` with `padding_digits ≥ 6` |
| Reset counter | Optional year/reset field policy: if reset required, new sequence row + history; **never** rewind `current_number` |
| Training mode | **Separate non-official series or no series** — training must never allocate official numbers |

### Dual numbering policy

| Number type | Purpose | Allocator |
|-------------|---------|-----------|
| **POS operational number** | Day-to-day host checkout today | `HostOperatorReceiptNumberAllocator` |
| **Official document number** | Only when official mode + posted official sale | `OfficialDocumentSequenceAllocationService` |
| **Remote POS number** | LAN client sales (if any) | RPOS allocator — never official |

Until official mode is deliberately enabled and wired, live checkout continues operational numbers only.

---

## 3. Document lifecycle states

```text
                    ┌──────────────┐
                    │ DRAFT_CART   │  no official number
                    └──────┬───────┘
                           │ tender / complete request
                           ▼
                    ┌──────────────┐
                    │ POSTING      │  atomic: sale write + optional allocate + EJ
                    └──────┬───────┘
              success │         │ fail
                      ▼         ▼
           ┌──────────────┐  ┌─────────────────┐
           │ POSTED_OFFICIAL│  │ DRAFT/ABORTED   │  no number or rolled back
           └──────┬───────┘  └─────────────────┘
                  │
       ┌──────────┼──────────┐
       ▼          ▼          ▼
  REPRINT     VOIDED      REFUNDED
  (same #)   (reversal)  (reversal doc)
```

| State | Official number? | Mutable sale body? |
|-------|------------------|--------------------|
| DRAFT_CART | **No** | Yes |
| TENDERED_NOT_POSTED (if ever split) | **No** (prefer not allocate early) | Yes |
| POSTING | In-flight allocate only inside transaction | No external visibility |
| POSTED_OFFICIAL | **Yes, permanent** | **No** (body immutable) |
| POSTED_OPERATIONAL (non-official) | No official # | Business rules for void TBD |
| VOIDED | Original # retained; void doc links original | Original immutable |
| REFUNDED | Original # retained; refund doc / reverse sale | Original immutable |
| CANCELED_BEFORE_POST | **No** | Discard |
| REPRINT | **Same** official #; reprint counter/event | N/A |

---

## 4. Invariants (required)

1. **Posted official number is never reused.**  
2. **Posted official sale body is immutable**; corrections only via void/refund/cancel **records**.  
3. **Reprint does not allocate a new official number.**  
4. **Draft carts must not allocate official numbers** (avoids gaps and abandoned numbers).  
5. **Allocation and sale post are one host-local transaction** (or allocate last inside post with rollback on failure).  
6. **Remote client cannot allocate official numbers**; only host process with Store Host authority.  
7. **No Admin/Owner remote official posting** (existing LAN gate).  
8. **Training/demo mode never allocates official numbers** and never sets official markers.  
9. **Exhausted sequence** → allocation fails closed; no wrap; no silent restart.  
10. **Concurrent posts** serialize on sequence row update (SQLite transaction + row lock).

---

## 5. Failure behavior

| Case | Proposal |
|------|----------|
| Allocate then crash before commit | Transaction rollback → number not committed; retry allocates same next |
| Post sale without official mode | Operational number only; no EJ official events |
| Official mode on but profile invalid | Fail closed — block official complete |
| Output (print) fails after post | Sale remains posted; reprint allowed; **do not** re-allocate |
| Gap in series after void | Voids do **not** free numbers; gaps allowed |

**Open:** Whether Philippine practice requires gapless series under all failure modes — mark **needs compliance review**; engineering prefers fail-closed atomic post over pre-allocation.

---

## 6. Concurrency / LAN risk

| Risk | Mitigation |
|------|------------|
| Two cashiers complete sale | Host API only; single SQLite writer; sequence update in same TX |
| Remote POS invents official # | Forbidden by transport gate + no remote official commands |
| Offline multi-host | Out of scope MVP — one Store Host owns DB |

---

## 7. Audit events for allocation

Minimum events (design):

- `OFFICIAL_NUMBER_ALLOCATED` (profile id, sequence id, document number, sale id, user, terminal, UTC)  
- `OFFICIAL_NUMBER_ALLOCATION_FAILED`  
- `OFFICIAL_DOCUMENT_REPRINTED`  
- `OFFICIAL_DOCUMENT_VOIDED` / `REFUNDED`  

Persist in application audit **and** electronic journal where official.

---

## 8. Implementation lanes

| Slice | Content | When |
|-------|---------|------|
| **RMO-BIR-M3** | Document contracts + readiness gates in code (types/services); **no** live checkout switch | Next |
| Later | Wire host complete to official path behind explicit official mode flag | After M3 contracts |
| REPORTS-M1 | Renderers consume posted snapshot | After live path |

### Blocker questions (human/compliance)

1. Prefer **Invoice** series only vs OR under RR 7-2024 for new installs?  
2. Is a **gapless** series mandatory after failed posts?  
3. One series per machine vs per store with multiple terminals?

---

## 9. Summary recommendation

Keep **two-number design**: operational POS numbers for today’s stable checkout; official series only when ACCREDITED + enabled + host post.  
**Do not allocate on draft.**  
**Do not enable remote official allocation.**
