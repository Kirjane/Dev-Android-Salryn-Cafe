# RMO-BIR-M2 Source Deepening

**Date:** 2026-07-09  
**Branch:** `KIRCH-RMO-BIR-M2`  
**Base M1:** `5f650236`  
**Disclaimer:** Design research only. Not a legal opinion or compliance claim.

References use M1 register IDs (`S#`) from `RMO_BIR_M1_SOURCE_REGISTER.md`.

---

## Access status this task

| Source | Access | Certainty for design |
|--------|--------|----------------------|
| S2 RMO 24-2023 Digest (FINAL) | **Read** in M1 (full digest text extract) | **High** for features a–m and invoice field list |
| S1 RMO 24-2023 full PDF | CDN URL registered; not re-OCR’d page-by-page in M2 | **High** for existence; section numbers may need full-PDF cite |
| S3 Annex F DOCX | **Fetch failed** (DOCX content-type not extractable in tool) | **Low** for line-items — **unresolved** |
| S4 Annex B DOCX | Same | **Low** — **unresolved** |
| S5 RMO 15-2018 digest | M1 summary + CDN | **Medium–High** for post-eval / Z / eSales journal extract |
| S6 RR 7-2024 / S7 RMC 77-2024 | CDN located; digests summarized in M1 | **Medium** for Invoice vs OR transition |
| Retention years / eSales format | Not confirmed in digest | **Unresolved** |

**Assumptions made:** **No** invented annex requirements. Design uses only high-certainty S2 feature list + existing Salryn schema.

---

## 1. RMO 24-2023 functional features (S2 a–m)

| Feature | Digest summary | Salryn design area | Certainty | Ambiguity |
|---------|----------------|--------------------|-----------|-----------|
| F-a Accumulated Grand Total Sales | System must maintain accumulating grand total sales | Grand total model + X/Z | High | Exact reset rules vs Z |
| F-b Tamper-free | No silent mutation / suppression of sales | Integrity spine; official lock | High | Implementation depth for TWG |
| F-c Activity / Transaction Log | Log system/sales activity | Audit contract | High | Full Annex F log list unresolved |
| F-d Non-volatile memory | Durable storage | SQLite / ProgramData | High | Hardware NVRAM vs software durability |
| F-e E-journal / Audit Journal | CRM/POS journal of official events | EJ append contract | High | Exact event set in Annex F unresolved |
| F-f Sales Readings | X/Z class readings | X/Z contracts | High | Reset semantics for Z |
| F-g Backend Reports | Backend report package | REPORTS-M1 contracts | High | Exact report list partially open |
| F-h Sequential accountable forms | Sequential series min 6 digits | Official numbering | High | Reset counter strategy |
| F-i Reprint | Reprint with distinction | Reprint marker + journal | High | “COPY” wording exact form |
| F-j Push | “Push” functionality | Transmission / eSales class | **Low** | **Unresolved** without Annex F |
| F-k Verification / Validation Seal | Seal feature | Integrity / export seal | **Low** | **Unresolved** without Annex F |
| F-l Data Retention | Retain sales data | Retention policy | **Low** | **Years not in digest** |
| F-m Sales Data Transmission | Transmit sales data | eSales / export | **Low–Medium** | Taxpayer-class dependent |

---

## 2. Invoice / receipt content (S2 → RR chain)

High-certainty fields from S2 digest (must appear on generated SI/OR-class output):

| Content point | Design area | Certainty |
|---------------|-------------|-----------|
| Registered seller name; business style; address | Taxpayer config + snapshot header | High |
| VAT REG TIN / NON VAT REG TIN + 9-digit TIN + 5-digit branch | Taxpayer config | High |
| MIN; machine serial (or HDD/SW license for cloned) | Machine config | High |
| Document number min 6 digits + reset counter if applicable | Numbering | High |
| Date/time; qty; desc; unit; totals; VAT amount | Sale contract | High |
| Mixed sales: VATable / Exempt / Zero-Rated | Tax buckets | High |
| Supplementary “not valid for input tax” wording | Document type / template | High (wording); Medium (which docs) |
| EXEMPT for non-VAT principals where applicable | Template / tax mode | Medium |
| SC/PWD/NAAC/Solo Parent fields when applicable | Customer/statutory contract | Medium (laws cited; product optional later) |
| Footer: accredited supplier + AN + PTU/ATG | Invoice profile / supplier config | High |

**Unresolved:** Full RR 7-2024 / RMC 77-2024 line rules for renaming Official Receipt → Invoice on CRM/POS — design must keep `document_kind` / title configurable.

---

## 3. RMO 15-2018 post-evaluation (S5)

| Point | Design impact | Certainty |
|-------|---------------|-----------|
| PTU must show taxpayer name/address/TIN/branch, MIN, PTU #, brand/model/serial | Machine + taxpayer config fields already largely modeled | High |
| Post-eval may extract eSales journal and Z-reading | Z persistence + EJ export contracts required | Medium–High |
| Exact extract file format | **Unresolved** | Low |

---

## 4. Annex F / Annex B

| Item | Status |
|------|--------|
| Annex F line-item checklist | **Needs official DOCX copy / human compliance review** — tool cannot parse DOCX from BIR CDN |
| Annex B documentary pack list | Same |
| Implication | Do not implement F-j / F-k / retention years until annex text is captured |

---

## 5. Mapping to M2 deliverables

| Deepening theme | M2 document |
|-----------------|-------------|
| F-h, min 6 digits, no reuse | `RMO_BIR_M2_OFFICIAL_NUMBERING_STUDY.md` |
| Taxpayer/MIN/PTU/AN headers | `RMO_BIR_M2_BUSINESS_MACHINE_CONFIG_STUDY.md` |
| SI/OR field list, X/Z, EJ | `RMO_BIR_M2_RECEIPT_REPORT_DATA_CONTRACT.md` |
| F-b, void, immutability | `RMO_BIR_M2_INTEGRITY_SPINE_STUDY.md` |
| F-g templates | `RMO_BIR_M2_REPORT_ENGINE_CHECKPOINT.md` |
