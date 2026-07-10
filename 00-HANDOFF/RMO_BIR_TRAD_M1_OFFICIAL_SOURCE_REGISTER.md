# RMO-BIR TRAD-M1 — Official Source Register

**Date:** 2026-07-10  
**Scope:** Traditional CRM/POS tax/taxpayer mode and Invoice-first document policy  
**Disclaimer:** Source register for engineering. Not legal advice. Not a compliance claim.

---

## Primary sources (required for policy spine)

| # | Source | Use in TRAD-M1 | Acquisition status |
|---|--------|----------------|--------------------|
| 1 | RMO No. 24-2023 (Enhanced eAccReg) | Accreditation path, Traditional CRM/POS track, Annex F functional checklist context | Use existing program copies / BIR CDN PDF when available |
| 2 | RR No. 7-2024 (EOPT invoicing), as amended by RR No. 11-2024 | Invoice as primary sales document; OR supplementary; VAT invoice required for VAT sellers every sale | Cited via secondary professional summaries; primary PDF to be retained in applicant package later |
| 3 | RMC No. 77-2024 | VAT Invoice vs Non-VAT Invoice content; non-VAT issuance threshold (~₱500, CPI-adjustable); mixed transaction breakdown | BIR CDN: `https://bir-cdn.bir.gov.ph/BIR/pdf/RMC%20No.%2077-2024.pdf` |
| 4 | RMC 77-2024 Annex A1–B6 sample invoice formats | VAT Invoice, Non-VAT Invoice, exempt/zero-rated/mixed layout fields | BIR CDN annex PDF |

Secondary clarifications (non-primary, orientation only):

- Professional digests of RR 7-2024 / RMC 77-2024 (e.g. counsel firm articles) — **do not invent rules beyond primary text**.

---

## Policy conclusions drawn for engineering (non-legal claim)

1. **VAT-registered sellers:** issue a **VAT Invoice** for every sale of goods/services (no amount threshold for the VAT seller duty).  
2. **Non-VAT sellers:** Non-VAT Invoice required when transaction (or daily aggregate of small sales) meets the statutory threshold or on buyer request; threshold is **not hard-coded permanently** as ₱500 in checkout logic.  
3. **Invoice-first:** Official Receipt is generally **supplementary** under EOPT transition; Salryn primary sales document kind remains `OFFICIAL_INVOICE`.  
4. **Mixed sales:** VAT mixed requires VATable / VAT-exempt / zero-rated breakdown (or separate invoices). Non-VAT mixed requires SSPT vs exempt breakdown when applicable.  
5. **Wrong mode fail-closed:** Non-VAT installation must never emit a VAT Invoice or charge VAT because a product was mislabeled.

---

## Explicitly out of TRAD-M1

- EIS electronic invoicing JSON/API schema  
- Live official number allocation  
- Real printer/PDF invoice rendering  
- eSales monthly file schema (later TRAD-M5; hard-stop if schema unavailable)  
- BIR application submission  

---

## Honest boundary

All tax buckets, modes, and invoice policy constants in code are **engineering policy spine** for Traditional CRM/POS readiness. They do **not** constitute BIR accreditation or legal VAT advice.
