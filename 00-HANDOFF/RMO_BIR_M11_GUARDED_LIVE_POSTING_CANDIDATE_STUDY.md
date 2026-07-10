# RMO-BIR-M11 Guarded Live Posting Candidate Study

**Date:** 2026-07-10  
**Branch:** `KIRCH-RMO-BIR-M11-GUARDED-LIVE-POSTING-CANDIDATE-HARDENING`  
**Base M10:** `bab5abd4`

**Disclaimer:** Engineering study only. No compliance claim. No live activation.

---

## Answers

| # | Question | Answer |
|---|----------|--------|
| 1 | Can a guarded candidate service be added without checkout binding? | **Yes.** Separate host service + host-local endpoint; checkout has no reference. |
| 2 | Can it reuse source-sale read-only snapshots? | **Yes.** Optional `OperationalSaleId` probed via existing read-only adapter; no mutation. |
| 3 | Can it model both tracks? | **Yes.** `TraditionalCrmPos` and `EisInvoiceReceipt` with distinct transmission plan stages. |
| 4 | Can it safely return blocked candidate results? | **Yes.** Terminal stage always `BlockBeforeMutation`; forced flags deny mutation/output/transmit. |
| 5 | Can it mutate sequence/sales/EJ? | **No** in M11. All mutate flags false; no allocate/append/write paths. |
| 6 | Is EIS JSON schema complete? | **No.** Unresolved; EIS track remains blocked candidate only. |
| 7 | Can live official posting be activated? | **No.** Production activation and checkout binding remain STOP. |

---

## Confirmed inventory

- Official setup/profile, numbering preview, integrity, dry-run package, source-sale read-only, report previews present (M3–M10).
- Host checkout and Remote POS do not call official numbering allocation, package service, candidate service, or live EJ append.
- `liveOfficialPostingAllowed` / `officialCheckoutAllocationEnabled` / `checkoutUsesAllocator` remain false on spine/readiness/candidate.
- EIS JSON schema remains unresolved (M10 acquisition).

---

## Risk notes

- Expanding candidate contracts must preserve forced blocked flags for M9/M10 gates.
- UI must avoid WEB-A3/A4 forbidden tokens (`printer`, `Crystal`, `RMO`, etc.).
- No schema migration required.
