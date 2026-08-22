# RMO-BIR TRAD-M1 Implementation Report

**Date:** 2026-07-10  
**Branch:** `KIRCH-RMO-BIR-TRAD-M1-TAXPAYER-MODE-AND-INVOICE-POLICY-SPINE`  
**Predecessor:** `KIRCH-RMO-BIR-QA1-REAL-MACHINE-DB-UPGRADE-BACKUP-RESTORE` @ `ef36a3f0a3816b44c4b2b13bbfa34460eab29daf`  
**Decision:** GO WITH MIGRATION v15  

**Disclaimer:** Engineering milestone only. Not BIR compliance, accreditation, CoA, PTU, or authority to issue official invoices.

---

## 1. Study decision

| Question | Decision |
|----------|----------|
| Persist tax mode? | Yes — `bir_machine_profiles.vat_registration_mode` (nullable) |
| Migration? | **v15** `VatRegistrationModeSchemaMigration` |
| Catalog | `SupportedMigrationVersion = 15` |
| DB-A2 future probe | **16** |
| Modes | `VAT_REGISTERED`, `NON_VAT_PERCENTAGE_TAX`, `NON_VAT_EXEMPT` |
| Product classifications | + `NonVatExempt=5`; `NonVat=4` = SSPT |
| Buckets | + `SsptSales`, `NonVatExemptSales` |
| Invoice-first | `OFFICIAL_INVOICE` primary; always-issue POS operational policy |
| Non-VAT ₱500 threshold | Orientation-only constant; not a permanent checkout gate |

---

## 2. Changed surface (summary)

### Core

- `TaxpayerRegistrationMode` + codes parse/persist  
- `SalrynTaxpayerTaxPolicy` fail-closed combination matrix + `OfficialTaxPolicySnapshot` freeze  
- `SalrynInvoiceDocumentPolicy` Invoice-first versioned policy  
- Tax bucket enums expanded (modes + SSPT/exempt kinds)  
- `OfficialDocumentTaxBucketEngine` mode-aware calculation; rejects invalid line classifications  
- Validator rejects VAT buckets on non-VAT drafts and non-VAT buckets on VAT drafts  
- Product tax display: SSPT / Non-VAT exempt  

### Infrastructure

- Migration v15 + factory registration  
- Catalog version 15  
- Official setup save/read `vat_registration_mode`  

### Web

- Official Setup business form: VAT/non-VAT mode select  

### Tests / gates

- `RmoBirTradM1TaxInvoicePolicyChecks`  
- CoreSmoke `--only rmo-bir-trad-tax-invoice-policy`  
- QA1 updated for v15 clean/upgrade and future v16 rejection  
- M4/M8/M9/DB-A2 version assertions updated  

### Handoffs

- Program ledger, TRAD-M1 study, GO/STOP, source register, this report  

---

## 3. Gate evidence (independently observed)

| Gate | Result |
|------|--------|
| `rmo-bir-trad-tax-invoice-policy` | **pass** |
| `rmo-bir-qa1-real-db` | **pass** (v15 clean, v12→v15, v14→v15, future v16 reject, backup/restore) |
| Full CoreSmoke | **pass** |
| `npm run build` (App.Web) | **pass** |

---

## 4. Invariants preserved

- `liveOfficialPostingAllowed` remains false for setup  
- No live sequence/EJ/sale official mutation from this milestone  
- No Sandbox promotion  
- No BIR submission  
- No compliance/accreditation claim in UI or policy notes  

---

## 5. Honest non-claims

- Not ready to issue official invoices  
- Not accredited / certified / PTU-ready  
- Not waiting for BIR inspector  
- QA1 **manual** Official Setup UI acceptance still **pending Kirch**  

---

## 6. Next milestone authorization

**TRAD-M2:** Atomic official posting (allocate serial + immutable official sale + EJ + audit in one transaction; still activation-gated / blocked until later accreditation activation rules).

Base branch after this push: `KIRCH-RMO-BIR-TRAD-M1-TAXPAYER-MODE-AND-INVOICE-POLICY-SPINE`.
