# RMO-BIR TRAD-M3 / M3-RC1 — Manual Output Acceptance Checklist

**Date:** 2026-07-10  
**Branch tip under test:** *(fill when executing)*  
**Operator:** Kirch  
**Disclaimer:** Manual engineering acceptance only. Not BIR accreditation or legal issuance authority.

---

## Preconditions

- [ ] Host runs on loopback with authenticated operator session  
- [ ] Production official activation is OFF  
- [ ] A committed official sale exists with EJ snapshot (tax buckets present) for a test sale ID  
- [ ] No production taxpayer data used for screenshots committed to the repo  

---

## A. Projection / generate

| # | Check | PASS / FAIL / N/A | Notes |
|---|--------|-------------------|-------|
| A1 | Official Setup → invoice projection card opens | | |
| A2 | Enter committed sale ID → Generate returns package + SHA-256 | | |
| A3 | Thermal preview shows **INTERNAL TEST — NOT AN OFFICIAL INVOICE** | | |
| A4 | Registration phrase matches mode (VAT REG vs NON-VAT REG) | | |
| A5 | Non-VAT shows input-tax disclaimer | | |
| A6 | Generate does not change sequence / sale total / stock | | |

## B. Download delivery (RC1)

| # | Check | PASS / FAIL / N/A | Notes |
|---|--------|-------------------|-------|
| B1 | Download HTML opens/saves `.html` with correct content type | | |
| B2 | Download thermal opens/saves `.txt` | | |
| B3 | Downloaded HTML contains same SHA-256 as generate (or matches header) | | |
| B4 | Filename is server-derived / sanitized (not path traversal) | | |
| B5 | Unauthenticated or non-loopback request is rejected | | |

## C. Browser print workflow (RC1)

| # | Check | PASS / FAIL / N/A | Notes |
|---|--------|-------------------|-------|
| C1 | Open print preview window from Official Setup | | |
| C2 | Preview shows watermark and invoice content | | |
| C3 | Browser print dialog opens via user gesture (`window.print`) | | |
| C4 | Optional: print to **Microsoft Print to PDF** / virtual printer | | |
| C5 | Physical printer job (only if device available) | | **NOT EXECUTED** unless observed |

## D. Trust / wording

| # | Check | PASS / FAIL / N/A | Notes |
|---|--------|-------------------|-------|
| D1 | Save business/machine no longer says “Official output is still not implemented.” | | |
| D2 | Messaging distinguishes internal projection vs production issuance OFF | | |
| D3 | Client cannot obtain unwatermarked output while activation OFF | | |

## E. Conditional fields (when test snapshot supplies buyer)

| # | Check | PASS / FAIL / N/A | Notes |
|---|--------|-------------------|-------|
| E1 | Buyer name/TIN/address appear when present in snapshot | | |
| E2 | Absent buyer still shows blank spaces (not invented) | | |

---

## Result summary

| Overall | Status |
|---------|--------|
| Automated M3/RC1 gates | *(engineering)* |
| Manual visual | **NOT EXECUTED** until Kirch fills |
| Browser print | **NOT EXECUTED** until Kirch fills |
| Physical printer | **NOT EXECUTED** unless observed |

**Operator sign-off:** _________________ **Date:** ________  

**Ending labels (choose one after manual work):**

- TRAD-M3-RC1 ENGINEERING COMPLETE — MANUAL PRINT ACCEPTANCE PENDING  
- TRAD-M3-RC1 FULL ACCEPTANCE COMPLETE — NO ACCREDITATION CLAIM  
