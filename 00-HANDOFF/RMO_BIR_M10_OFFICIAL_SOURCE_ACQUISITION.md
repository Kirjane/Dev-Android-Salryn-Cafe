# RMO-BIR-M10 Official Source Acquisition

**Date:** 2026-07-10  
**Branch:** `KIRCH-RMO-BIR-M10-OFFICIAL-SOURCE-ACQUISITION`  
**Base M9:** `406e78df`  

**Disclaimer:** Engineering source-acquisition register only. **Not a legal opinion.** Does **not** claim Salryn is BIR compliant, RMO ready, accredited, or certified. Does **not** authorize official receipt/invoice issuance or live posting.

---

## Mission

Close M9 residual **official-text** gaps for:

1. Verification / Validation Seal (F-k)  
2. Push functionality (F-j)  
3. eSales / sales data transmission (F-m + traditional eSales)  
4. Class applicability (traditional CRM/POS vs EIS e-invoicing)

**Rule:** Official BIR CDN / full issuances only. Do not invent protocols. If a field schema is not in the acquired text, mark **unresolved**.

---

## Sources acquired this session

| ID | Source | Type | Official URL | Extract method | Confidence |
|----|--------|------|--------------|----------------|------------|
| S1 | RMO No. 24-2023 (full, 28 pp.) | RMO | https://bir-cdn.bir.gov.ph/local/pdf/RMO%20No.%2024-2023.pdf | BIR CDN PDF; pages 8–12 image extract for features a–m | **High** |
| S2 | RMO No. 24-2023 Digest (FINAL) | Digest | https://bir-cdn.bir.gov.ph/local/pdf/RMO%20No.%2024-2023%20Digest%20FINAL.pdf | Prior M1/M9 | **High** |
| S3 | RMO 24-2023 Annex F | Checklist DOCX | BIR CDN Annex F | Prior M9 full DOCX text extract | **High** |
| S5 | RMO No. 15-2018 (full) | RMO | https://bir-cdn.bir.gov.ph/local/pdf/RMO_No.15-2018.pdf | BIR CDN PDF text extract (pp. 1–8+) | **High** |
| S18 | RR No. 8-2022 (full, 4 pp.) | RR | https://bir-cdn.bir.gov.ph/local/pdf/RR%20No.%208-2022.pdf (also `RR%208-2022.pdf`) | BIR CDN PDF text extract | **High** |
| S19 | RMO No. 12-2012 (eSales monthly reporting) | RMO | https://bir-cdn.bir.gov.ph/BIR/pdf/63728RMO%2012-2012.pdf | BIR CDN index + crawl extract of definitions/deadlines | **High** for process; field list partial from crawl |
| S20 | RR No. 11-2025 | RR | https://bir-cdn.bir.gov.ph/BIR/pdf/RR%20No.%2011-2025.pdf | Retrieved; text layer weak/image-heavy — **not fully line-audited** | **Low–Medium** |
| L-M9 | M9 legal closure | Internal | `00-HANDOFF/RMO_BIR_M9_LEGAL_SOURCE_CLOSURE.md` | Cross-check | Medium |

**Not used as authority:** vendor blogs, Facebook, Scribd (discovery only).

---

## Target topic extracts (verbatim / near-verbatim from official text)

### 1. Verification or Validation Seal (S1 § features k)

From RMO 24-2023 full text (page ~10):

> **k. Verification or Validation Seal.** Serves as a security feature that will determine the authenticity of the e-Invoices/e-Receipts. This functionality must be inherent in all e-Invoices or e-Receipts generated from “Sales Machines/Software” used under a Subscription-Based Agreement. Most common verification or validation seals are Quick Response (QR) Codes, Bar Codes, Uniform Resource Locator (URL) and any other form of seal or security features.

**Engineering reading (not legal advice):**

| Point | Implication |
|-------|-------------|
| Scope of seal text | Tied to **e-Invoices/e-Receipts** under **Subscription-Based Agreement** |
| Examples | QR code, barcode, URL, other seal/security features |
| Crypto/protocol | **Not** specified beyond seal *forms* |
| Traditional paper SI/OR from non-subscription POS | Seal requirement as written is for e-invoice/e-receipt subscription path; do not invent a seal for non-e documents |

**Certainty:** **High** for quoted text. **Medium** for applicability outside subscription e-invoicing.

### 2. “Push” Functionality (S1 § features j)

From RMO 24-2023 full text (page ~10):

> **j. “Push” Functionality.** All sales data or information (as required under the e-Invoicing/e-Receipting Project of the BIR) that will be generated from a “Sales Machines/Software”, if applicable, must be able to send or push to the BIR on a “real-time” or “near real-time” the EIS e-Invoice JavaScript Object Notation (JSON) File Format for CRM/POS based on the provisions of the applicable e-Invoicing/e-Receipting revenue issuances.

**Certainty:** **High** for quoted text. “If applicable” + cross-reference to e-invoicing issuances.

### 3. Sales Data Transmission (S1 § features m)

From RMO 24-2023 full text (page ~11):

> **m. Sales Data Transmission.** For e-invoice/e-receipt, the system must be able to transmit the sales data in JSON File Format with an EIS Unique ID to verify the transmitted sales data.

**Certainty:** **High** for quoted text.

### 4. EIS / e-invoicing class (S18 RR 8-2022)

Mandated under Sec. 237 NIRC as amended (RR 8-2022 §2):

1. Taxpayers engaged in export of goods and services  
2. Taxpayers engaged in e-commerce  
3. Taxpayers under Large Taxpayers Service (LTS)

Sec. 237-A: above **except e-commerce** must electronically transmit sales data via Sales Data Transmission System.

Key operational rules (RR 8-2022 §4):

| Rule | Summary |
|------|---------|
| API | Sales Data Transmission System based on **Standard API Guidelines** (separate issuance) |
| Enrollment | Required before transmission |
| EIS CERT | BIR certification of transmission system |
| PTT | **Permit to Transmit** required to allow transmission |
| Timing | Real-time or near real-time; **within three (3) calendar days** from transaction date |
| Format | Encrypted sales data in **JSON** File Format |
| Images | Scanned/image of e-receipt/e-invoice **not** required |
| Non-mandated | May continue manual or CAS/POS-generated receipts based on **existing revenue issuances**; optional EIS path available |

**Certainty:** **High** for class + process. **Unresolved:** exact JSON field schema / API Guidelines document (explicitly deferred by RR 8-2022 to “separate issuance”).

### 5. Traditional eSales (S5 RMO 15-2018 + S19 RMO 12-2012)

**RMO 15-2018:**

- Post-evaluation includes extraction from **eSales Journal** and **Z-Reading**.  
- POS machines must generate corresponding **eSales Journal**; CRM dual rollers / sales book; capable of Z-reading.  
- Training / no-sale mode should be **disabled** for machines used in business operations.  
- Taxpayers enrolled in eSales System (with or without sales) must submit **Monthly Sales Report per machine** (cites RMO 12-2012).  
- PTU must be conspicuously attached (name/address/TIN+branch, MIN, PTU number, brand/model/serial or software license).

**RMO 12-2012 (from official CDN crawl extract):**

- All taxpayers using CRM/POS/other invoice-receipt machines enrolled in eSales System submit **monthly sales report per machine**.  
- Deadline: **8th** day of following month if last digit of 9-digit TIN is **even**; **10th** day if **odd**.  
- MIN is unique system-generated control number for each machine in eSales.  
- Report includes machine identification (MIN) and gross monthly sales components (VAT taxable net of VAT, zero-rated, exempt, percentage tax, etc., as stored in non-volatile memory — per definition extract).  
- File upload naming conventions for POS SALES CSV appear in related eSales user materials (Annex C path on CDN) — **CSV path is for eSales portal upload**, distinct from EIS JSON.

**Certainty:** **High** for monthly eSales obligation existence + journal extractability. **Medium** for complete monthly CSV column dictionary without full Annex line-audit.

---

## Dual-track model (Salryn engineering frame)

| Track | Who | Source | Format / cadence | Seal / push |
|-------|-----|--------|------------------|-------------|
| **A. Traditional CRM/POS** | Taxpayers using accredited CRM/POS generating SI/OR under existing PTU rules, not necessarily EIS-mandated | S5, S19, S1 general features | Monthly eSales report per machine; examinable eSales Journal + Z-reading | Push/seal as written primarily for e-invoicing project; traditional path emphasizes journal + Z + monthly report |
| **B. EIS e-invoice/e-receipt** | Export / LTS (mandatory transmit); e-commerce e-invoice mandate per RR 8-2022; optional adopters | S18, S1 j/k/m, Annex F subscription section | JSON to EIS real-time/near real-time ≤ 3 days; EIS Unique ID; EIS CERT + PTT | Validation seal (QR/barcode/URL/other) on e-invoice/e-receipt; push JSON |

Salryn’s default product posture (host POS, not subscription e-invoicing EIS-certified): engineering should assume **Track A minimum** for future official design, and treat **Track B** as a **separate gated product path** (EIS CERT/PTT/API), not invent Track B live push now.

---

## Updated closure table (M10 delta)

| Topic | M9 status | M10 status | Certainty | Blocks live posting | Notes |
|-------|-----------|------------|-----------|---------------------|-------|
| Validation seal technical form | Unresolved | **Source-closed for subscription e-invoice/e-receipt** (QR/barcode/URL/other) | High (text) | **Yes** until product path + class clear | Not a free pass to issue e-docs |
| Push requirement | Partial | **Source-closed as EIS JSON real-time/near-real-time when applicable** | High (text) | **Yes** for EIS path | “If applicable” — not every POS is EIS-mandated |
| eSales traditional monthly | Partial | **Source-closed process** (monthly per machine; TIN parity deadlines; MIN) | High process / Medium field dictionary | **Yes** for live “eSales complete” claim | CSV/portal path ≠ EIS JSON |
| eSales Journal + Z extract | Partial | **Source-closed for post-evaluation** (POS journal + Z required) | High | **Yes** for examiner-ready claim | Aligns with EJ + Z engineering spine |
| EIS JSON field schema / API Guidelines | Open | **Still open** (separate BIR issuance not acquired line-by-line) | Low | **Yes** for EIS live push implementation | Do not invent JSON schema |
| Retention | Closed M9 | **Unchanged closed** | High | No alone | 3 / 10 years |
| Annex F checklist | Closed M9 | **Unchanged closed** | High | Yes for product demo | Text ≠ product |

---

## STOP / GO after acquisition

| Question | Decision |
|----------|----------|
| Is official **text** for seal/push/eSales now adequate for engineering design? | **Mostly yes** (dual-track model) |
| Is exact EIS JSON API schema closed? | **No** |
| May Salryn implement **live official posting** in production? | **No** — product + accreditation + gates incomplete |
| May Salryn enable live EIS push? | **No** — schema + EIS CERT/PTT + class gates missing |
| Next engineering without live bind? | Design notes + blockers only; keep always-blocked candidate |

**Live official posting remains STOP.**  
**Live eSales/EIS push remains STOP.**  
**Compliance / accreditation claims remain forbidden.**

---

## Explicit non-claims

- Salryn is **not** claimed BIR compliant.  
- Salryn is **not** claimed RMO ready, accredited, or certified.  
- No official receipt/invoice is issued.  
- `liveOfficialPostingAllowed` remains **false**.  
- Acquisition of source text is **not** product readiness.
