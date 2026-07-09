# RMO-BIR-M1 Gap Map / Implementation Lanes

**Date:** 2026-07-09  
**Branch:** `KIRCH-RMO-BIR-M1`  
**Input:** `RMO_BIR_M1_REQUIREMENTS_MATRIX.md`, inventory, source register.

**Disclaimer:** Planning only. No compliance claim.

---

## A. Must-have before official output

These gaps are **blockers or near-blockers**. Do not start Crystal/print productization until the data contracts and integrity spine are closed.

| Theme | Matrix IDs | Gap | Suggested lane |
|-------|------------|-----|----------------|
| Official sequence on live sales | 005 | Host checkout uses non-official allocator | **RMO-BIR-M2** |
| Active machine/taxpayer profile | 001–004, 012 | Fields exist; no operator-facing gate for official mode | **RMO-BIR-M2** |
| Official snapshot contract on complete | 007, 011, 029 | Draft builders exist; not wired to sale completion | **RMO-BIR-M2** |
| E-journal append on official events | 016, 015 | Schema/service exist; not live | **RMO-BIR-M2** |
| Void/refund/cancel reverse path | 013, 014 | Traceable correction + no silent edit | **RMO-BIR-M2** |
| Reprint marked + journaled | 025 | Service contracts incomplete product path | **RMO-BIR-M2** |
| Grand total accumulator | 024 | Missing model | **RMO-BIR-M2** |
| Training/demo non-official watermark | 030 | Missing | **RMO-BIR-M2** |
| Tax bucket live policy | 029 | Engine partial | **RMO-BIR-M2** |

**Policy already good (keep):**

- Official commands **not** allowed over LAN remote transport (031).  
- Remote POS numbering remains non-official.

---

## B. Reporting / output lane

Start **after** M2 defines stable data contracts (snapshot, Z summary, EJ export shape).

| Theme | Matrix IDs | Gap | Suggested lane |
|-------|------------|-----|----------------|
| Principal invoice/receipt template | 007–010 | No product template/renderer | **REPORTS-M1** |
| X-reading / Z-reading operator UI + samples | 018–019 | Services partial | **REPORTS-M1** |
| Backend reports package | 020 | Missing product reports | **REPORTS-M1** |
| Examiner export (journal / eSales-class) | 017, 021 | Missing / unknown exact format | **REPORTS-M1** then BACKLOG if eSales-specific |
| Crystal Reports | 035 | Not justified yet | **CRYSTAL-M1 only if REPORTS-M1 chooses** |

**Decision guidance:** Prefer text/HTML/PDF export architecture unless Annex F / customer requirement forces Crystal. **Do not start Crystal now.**

---

## C. Schema / data lane

Much schema already landed (machine profile, sequences, EJ, Z). Remaining likely:

| Theme | Matrix IDs | Gap | Suggested lane |
|-------|------------|-----|----------------|
| Grand total / continuous counters | 024 | New fields/tables possible | **RMO-BIR-M2** (study first) |
| Statutory discount / customer | 028 | New models | BACKLOG after spine |
| Document series policy (Invoice vs OR) | 008 | Config enum + series ownership | **RMO-BIR-M2** |
| Retention metadata | 026 | Unknown legal period | BACKLOG until sourced |

**Rule:** No schema change in M1. M2 should still prefer study → minimal migrations only when contracts freeze.

---

## D. Security / tamper lane

| Theme | Matrix IDs | Gap | Suggested lane |
|-------|------------|-----|----------------|
| Immutable posted official sales | 014 | Policy + DB guards | **RMO-BIR-M2** |
| Privileged void/refund only | 013 | Permission matrix | **RMO-BIR-M2** |
| Audit on profile/sequence/config changes | 015 | Completeness | **RMO-BIR-M2** |
| Backup integrity for official data | 027 | Restore drills | INSTALLER / BACKLOG |

---

## E. Deferred / unknown

| Theme | Matrix IDs | Why deferred |
|-------|------------|--------------|
| Exact retention years | 026 | Not confirmed in digest extract |
| “Push” + validation seal technical form | 022 | Need Annex F / full RMO |
| eSales transmission obligations | 021 | Taxpayer-class dependent |
| Full RR 7-2024 / 11-2024 line audit | 008 | PDF located; not fully line-read |
| External BIR TWG demo logistics | 032 | Process external to software |
| Licensing commercial | 034 | Separate LICENSE-M1 |
| Installer real-machine tests | 033 | Installer branch paused |

---

## Recommended sequence (engineering)

```text
RMO-BIR-M1 (this)  matrix complete
        │
        ▼
RMO-BIR-M2         official numbering + profile + receipt/report DATA CONTRACTS
                   (+ integrity: void, EJ append, official lock)  [study→minimal impl]
        │
        ▼
REPORTS-M1         templates, X/Z, backend reports, exports (engine decision)
        │
        ▼
CRYSTAL-M1         only if REPORTS-M1 selects Crystal
        │
        ▼
Evidence pack + installer production hardening
```

Installer lane (`KIRCH-INSTALLER-M1`) continues independently; do not block M2 on MSI.
