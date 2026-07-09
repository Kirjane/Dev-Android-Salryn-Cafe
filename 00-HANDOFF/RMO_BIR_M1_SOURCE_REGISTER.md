# RMO-BIR-M1 Source Register

**Date:** 2026-07-09  
**Branch:** `KIRCH-RMO-BIR-M1` (from `KIRCH-SANDBOX` @ `1f74cd8b`)  
**Mode:** Source research only. Not a legal opinion or accreditation claim.

**Disclaimer:** This register maps public BIR issuances and repo-local prior matrices. It does **not** certify Salryn, claim compliance, or substitute for legal counsel or BIR TWG evaluation.

---

## Primary official sources (high confidence)

| # | Source title | Type | Date / effectivity | URL or path | Why relevant | Confidence | Notes |
|---|--------------|------|--------------------|-------------|--------------|------------|-------|
| S1 | **Revenue Memorandum Order No. 24-2023** (full text) | RMO | Issued **26 Jun 2023** | https://bir-cdn.bir.gov.ph/local/pdf/RMO%20No.%2024-2023.pdf | Primary accreditation procedure for CRM/POS/similar sales machines/software | **High** | Official BIR CDN PDF (28 pages). Primary authority for POS software accreditation process. |
| S2 | **RMO No. 24-2023 Digest (FINAL)** | RMO digest | Issued **26 Jun 2023** | https://bir-cdn.bir.gov.ph/local/pdf/RMO%20No.%2024-2023%20Digest%20FINAL.pdf | Condensed text of S1; lists machine types, accreditation vs registration, and **functional specs (a–m)** | **High** | Digest reviewed in this task (image/PDF extract). Use full S1 when exact article/section numbers needed. |
| S3 | **RMO 24-2023 Annex F — Functional and Technical Evaluation Checklist** | Checklist / annex | Related to RMO 24-2023 | https://bir-cdn.bir.gov.ph/local/pdf/RMO%2024-2023%20ANNEX%20F_12072022_Functional%20and%20Technical%20Evaluation%20Checklist_RAF.DOCX | TWG demo evaluation guide for sales machines/software features | **High** | Official annex link on BIR CDN; not fully line-item extracted in this task. |
| S4 | **RMO 24-2023 Annex B — Checklist of Documentary Requirements** | Checklist / annex | Related to RMO 24-2023 | https://bir-cdn.bir.gov.ph/local/pdf/RMO%2024-2023%20Annex%20B_Checklist%20of%20Documentary%20Requirements%20v2.docx | Documents required for Application for Accreditation | **High** | Official annex; details not fully extracted here. |
| S5 | **Revenue Memorandum Order No. 15-2018** (digest) | RMO digest | Issued **14 Mar 2018** | https://bir-cdn.bir.gov.ph/local/pdf/RMO_NO.%2015-2018_digest.pdf | Post-evaluation of CRM/POS; eSales journal / Z-reading extraction; PTU attachment content | **High** | Still relevant for post-evaluation / examination practices. Cross-check full RMO text if implementing examiner extract. |
| S6 | **Revenue Regulations No. 7-2024** | RR | Published **29 Mar 2024** (BIR CDN) | https://bir-cdn.bir.gov.ph/BIR/pdf/RR%20No.%207-%202024.pdf | Invoicing rules affecting CRM/POS / e-receipting / e-invoicing; OR→Invoice transition context | **High** | Full RR PDF available; line-item invoice field rules should be re-read before REPORTS-M1 templates. |
| S7 | **Revenue Memorandum Circular No. 77-2024** (digest) | RMC digest | **11 Jul 2024** | https://bir-cdn.bir.gov.ph/BIR/pdf/RMC%20No.%2077-2024%20Digest.pdf | Clarifies renaming Official Receipt → Invoice on CRM/POS systems; serial continuity; notice requirements | **High** | Important for document title/series policy; not a free pass to claim official output. |
| S8 | **RMC No. 72-2025** | RMC | **14 Jul 2025** | https://bir-cdn.bir.gov.ph/BIR/pdf/RMC%20NO.%2072-2025.pdf | Validity period of Certificates of Accreditation set to expire (administrative continuity) | **Medium–High** | Operational for suppliers; less about product feature matrix. |
| S9 | **Bureau of Internal Revenue (portal)** | Agency site | Ongoing | https://www.bir.gov.ph/ | Index of issuances; e-SUBMISSION / e-Sales reminders | **High** (portal) / **Medium** (individual circulars until opened) | Use as discovery surface; always open the specific RR/RMO/RMC PDF for claims. |

---

## Related official sources cited by S2 (secondary authority chain)

These are **named in RMO 24-2023 digest** as governing invoice/receipt content. Full PDFs were **not** re-extracted line-by-line in M1; treat as **must-read before REPORTS-M1**.

| # | Source | Type | Confidence | Notes |
|---|--------|------|------------|-------|
| S10 | RR No. 16-2018 | RR | Medium (cited by S2; PDF not fully re-read in M1) | Invoice/receipt information required under older CRM/POS rules |
| S11 | RR No. 6-2022 | RR | Medium | Cited with S10 re Sec. 6.0 of RR 11-2004 |
| S12 | RR No. 11-2004 | RR | Medium | Historical CRM/POS base regulation referenced by digest |
| S13 | RR No. 5-2005 (as amended) | RR | Medium | Machine serial / HDD / software license for cloned machines |
| S14 | RMC No. 55-2019 | RMC | Medium | Business name/style clarifications |
| S15 | RMC No. 64-2015 / RR No. 16-2005 | RMC / RR | Medium | Customer/buyer details for VAT SI/OR where applicable |
| S16 | NIRC of 1997 as amended (e.g. Sec. 109; RA 10378 context) | Law | Medium | Non-VAT / percentage tax / exemption framing in digest |
| S17 | RA 9994, 10754, 10699, 10699-related SC/PWD/NAAC/Solo Parent | Law | Medium | Statutory discount capture fields in digest |

---

## Repo-local prior matrices (engineering, not law)

| # | Title | Type | Path | Confidence | Notes |
|---|-------|------|------|------------|-------|
| L1 | RMO 24-2023 Compliance Matrix (J21-1) | Internal engineering matrix | `00-COMPLIANCE/RMO_24_2023_COMPLIANCE_MATRIX.md` | Medium | Control IDs C01–C13; **must stay subordinate to S1–S2** |
| L2 | KIRCH-SANDBOX RMO readiness plan | Internal plan | `00-COMPLIANCE/KIRCH_SANDBOX_RMO_24_2023_READINESS_PLAN.md` | Medium | Notes prior PDF was image-only; engineering spine status |
| L3 | Electronic journal / machine profile seals | Internal seals | `00-COMPLIANCE/RMO_24_2023_*` | Medium | Implementation seals; not accreditation |

---

## Functional requirements extracted from S2 (RMO 24-2023 digest)

### A. Scope (accreditation vs registration)

From S2:

- **Subject to accreditation + registration:** CRM; POS (bundled/software); e-Invoicing/e-Receipting under subscription; other sales machines generating invoices/receipts (taximeter, handheld/mobile linked to server, vending, etc.); **POS as component of CAS**.
- **Registration only (not accreditation under Order):** certain Special Purpose Machines; CAS itself; e-invoicing as component of CAS (per digest table).
- Accreditation/registration via **Enhanced eAccReg**.
- Suppliers/developers/providers/users enroll with **Sworn Declaration**, then Application for Accreditation + documentary checklist; TWG demo; **Functional and Technical Evaluation Checklist**.

### B. Specifications/features that systems must declare (S2 § features a–m)

Accredited sales machines/software must comply with and declare (not exhaustively implemented by Salryn):

| Code | Feature (digest wording) |
|------|---------------------------|
| F-a | Accumulated Grand Total Sales |
| F-b | Tamper-free |
| F-c | Activity Log or Transaction Log |
| F-d | Non-volatile memory |
| F-e | E-journal or Audit Journal (for CRM and POS) |
| F-f | Sales Readings |
| F-g | Backend Reports |
| F-h | Sequential series of accountable forms/documents |
| F-i | Reprint Functionality |
| F-j | “Push” Functionality |
| F-k | Verification or Validation Seal |
| F-l | Data Retention |
| F-m | Sales Data Transmission |

### C. Invoice/receipt content (S2 → RR chain)

Digest requires invoices/receipts show information under RR 16-2018 / RR 6-2022 / RR 11-2004 Sec. 6.0, including among others:

- Registered seller name; business name/style; detailed address  
- “VAT REG TIN” or “NON VAT REG TIN” + 9-digit TIN + 5-digit branch code  
- MIN; machine serial (or HDD/software license for cloned)  
- Invoice/receipt number **minimum six digits**, running serial + reset counter if applicable  
- Date; customer details where VAT SI/OR rules apply  
- Qty, description, unit cost, total cost; VAT amount if 12% VAT  
- Mixed sales: VATable / VAT Exempt / Zero-Rated breakdown  
- Supplementary document rules (“THIS DOCUMENT IS NOT VALID FOR CLAIM OF INPUT TAX.”)  
- EXEMPT wording for non-VAT principals where applicable  
- SC/PWD/NAAC/Solo Parent fields where applicable  
- Footer: accredited supplier name/address/TIN; accreditation number/dates; PTU or ATG number  

### D. Post-evaluation / examination (S5)

RMO 15-2018 digest: post-evaluation of CRM/POS; extraction from **Electronic Sales (eSales) Journal** and **Z-Reading**; PTU must be conspicuously attached with taxpayer name/address/TIN/branch, MIN, PTU number, brand/model/serial or software license.

---

## Secondary sources (not primary authority)

| Source | Used? | Notes |
|--------|-------|-------|
| Grant Thornton / business blogs | **No** as requirement authority | Mentioned only to discover RR 7-2024 / RMO 24-2023 cross-links; requirements taken from BIR CDN |
| Facebook / vendor posts | **No** | Not cited as requirements |
| Scribd third-party POS guidelines | **No** | Prefer BIR annexes |

---

## Unresolved / needs maintainer or deeper official PDF read

| Topic | Status |
|-------|--------|
| Exact **retention period** in years for e-journal / sales data | **Unresolved** in digest extract — check full S1 + related RRs before implementing retention policy |
| Exact **“Push” functionality** technical definition (F-j) | **Unresolved** — need Annex F / full S1 |
| Exact **Verification or Validation Seal** technical form (F-k) | **Unresolved** — need Annex F / full S1 |
| **eSales** mandatory transmission format for Salryn class of taxpayer | **Unresolved** — depends on taxpayer size/category and current e-Sales rules |
| Full **RR 7-2024 / RR 11-2024** invoice naming and principal vs supplementary document matrix | **Partially open** — PDFs located (S6, RR 11-2024 CDN); not line-audited in M1 |
| Local PDF previously at `C:\Users\kirch\Downloads\RMO No. 24-2023.pdf` | Historical note in L2; prefer S1 CDN |

**Rule:** Do not invent requirements for unresolved rows. Mark matrix items `unknown` / `UNSOURCED` until official text is attached.

---

## How this register is used

1. **Requirements matrix** cites `S#` IDs.  
2. Engineering prior art (L1–L3) may inform implementation lanes but **cannot** create new legal requirements.  
3. Before any public or customer-facing claim, re-verify against S1 full text + Annex F.
