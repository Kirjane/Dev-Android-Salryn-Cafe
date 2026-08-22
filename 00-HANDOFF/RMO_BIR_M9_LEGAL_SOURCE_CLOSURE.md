# RMO-BIR-M9 Legal / Source Closure

**Date:** 2026-07-10  
**Branch:** `KIRCH-RMO-BIR-M9-FULL-CLOSURE`  
**Base M8:** `aef6b9fb`  

**Disclaimer:** Engineering source-closure register only. **Not a legal opinion.** Does **not** claim Salryn is BIR compliant, RMO ready, accredited, or certified. Does **not** authorize official receipt/invoice issuance.

---

## Sources used (official where possible)

| ID | Source | Type | URL / path | Used for |
|----|--------|------|------------|----------|
| S1 | RMO No. 24-2023 (full) | RMO | https://bir-cdn.bir.gov.ph/local/pdf/RMO%20No.%2024-2023.pdf | Primary accreditation procedure |
| S2 | RMO No. 24-2023 Digest (FINAL) | Digest | https://bir-cdn.bir.gov.ph/local/pdf/RMO%20No.%2024-2023%20Digest%20FINAL.pdf | Features a–m; invoice/receipt content chain |
| S3 | RMO 24-2023 **Annex F** Functional and Technical Evaluation Checklist | Checklist DOCX | https://bir-cdn.bir.gov.ph/local/pdf/RMO%2024-2023%20ANNEX%20F_12072022_Functional%20and%20Technical%20Evaluation%20Checklist_RAF.DOCX | Line-item TWG evaluation; retention years; push; X/Z; training; void; reprint; e-invoicing JSON note |
| S4 | RMO 24-2023 Annex B | Checklist | BIR CDN Annex B | Documentary requirements (not re-line-audited in M9) |
| S5 | RMO No. 15-2018 digest | Digest | BIR CDN | Post-evaluation; eSales journal / Z-reading extraction context |
| S6 | RR No. 7-2024 | RR | BIR CDN | Invoice naming / e-receipting context (not fully re-audited) |
| S7 | RMC No. 77-2024 digest | RMC | BIR CDN | OR → Invoice naming continuity |
| S8 | RMC No. 72-2025 | RMC | BIR CDN | Certificate validity administrative note |
| L1 | Prior M1 source register | Internal | `00-HANDOFF/RMO_BIR_M1_SOURCE_REGISTER.md` | Cross-check IDs |

**Method:** S2 digest and S3 Annex F text extracted from official BIR CDN in this session. S1 PDF was retrieved; text extraction of the full 28-page image/PDF was partial. Where Annex F and digest agree, certainty is high. Where technical protocol is still ambiguous after official extract, rows are marked **unresolved**.

---

## Closure table

| Topic | Source | Exact requirement summary | Certainty | Salryn current support | Remaining gap | Blocks live posting | Blocks official output | Notes |
|-------|--------|---------------------------|-----------|------------------------|---------------|---------------------|------------------------|-------|
| 1. Annex F functional requirements | S3 + S2 | TWG checklist covers non-volatile memory/backup, activity log, retention, training mode markers, grand total digits/reset counter, dual numbering (SI/OR vs transaction no.), reprint markers, void/cancel/refund/return with distinct series, X-reading, Z-reading/EOD fields, e-journal (POS), push readiness, invoice face fields, SC/PWD/NAAC/Solo Parent, backend/BIR reportorial list, e-invoicing JSON transmit (subscription class) | **High** (checklist extracted) | Partial engineering spine (setup, numbering preview, EJ skeleton, dry-run, internal previews) | Full feature implementation + TWG demo readiness | **Yes** (until product proves checklist) | **Yes** | Closure of *text* ≠ product readiness |
| 2. Push requirement | S2 F-j; S3 | System must be **ready/capable of pushing data/information to BIR**; e-invoicing note: readiness for TRAIN e-invoicing | **Medium–High** (capability stated; protocol not fully specified for all classes) | Not implemented | Wire-format, auth, endpoint, and when mandatory for traditional POS vs subscription e-invoicing | **Yes** | **Yes** (for classes requiring live transmit) | Do not invent protocol |
| 3. Validation seal | S2 F-k | “Verification or Validation Seal” listed as required feature | **Low–Medium** | Not implemented | Exact technical form (seal content, placement, crypto vs visual) not line-specified in Annex F extract | **Yes** | **Yes** | **Unresolved technical form** |
| 4. Retention years | S3 (Annex F) + TRAIN / RR 17-2013 chain cited in Annex F | **3 years** for invoices/receipts (TRAIN); **10 years** for books of accounts and other accounting records (RR 17-2013 as amended by RR 5-2014; RMC 29-2019) | **High** | Backup/restore engineering exists; no policy-enforced retention purge UI | Product policy + purge guards + examiner export | **No** alone | **No** alone | **Closed as source text** for engineering planning |
| 5. eSales format / transmission | S3 e-invoicing section; S5; S2 F-m | For **e-invoicing/receipting under subscription**: transmit required sales data (**JSON File Format for CRM/POS**) simultaneously with SI/OR; near-real-time or batch must be declared; **mandatory** in that section. Traditional CRM/POS eSales journal examination still referenced via S5 | **Medium** | Not implemented | Taxpayer-class applicability for Salryn default POS; full eSales schema beyond “JSON for CRM/POS” note | **Yes** | **Yes** for mandatory classes | **Partially unresolved** for non-subscription POS class |
| 6. Z-reading extraction | S3 | Z/EOD: Z-counter, reset counter, SI/OR series range (Beg/End), previous/present reading, sales for day, transaction summary, discount and sales breakdowns; reprintable with “REPRINT”; after Z, subsequent sales use next operation day (industry exceptions) | **High** | Internal Z preview shape only; persisted Z services exist but not live-bound | Live totals from official sales + range bind | **Yes** for live official day-close | **Yes** for legal Z | |
| 7. X-reading expectation | S3 | X-reading / cashier accountability / end-of-shift or equivalent required; reconfigure if missing | **High** | Operational shift close + internal X preview shape | Official X from official sales | **Yes** for official accountability | Partial | |
| 8. Official invoice/receipt content | S2 + S3 | Header (seller name/style/address, VAT/NON-VAT TIN+branch, MIN, serial/HDD/license); min 6-digit SI/OR; date; buyer fields when required; qty/desc/unit/total; VAT/mixed breakdown; EXEMPT; SC/PWD fields; footer supplier AN/PTU/ATG; supplementary disclaimer | **High** for field list | Setup stores many fields; no legal output | Layout proof + output engine + accreditation metadata real values | **Yes** | **Yes** | |
| 9. Document numbering | S2 F-h; S3 | Distinct SI/OR series vs transaction number; min 6 digits; sequential; reset counter rules; offline series rules if applicable | **High** | Official sequence schema + preview/validate; **no checkout allocation** | Atomic live allocate + immutability proof | **Yes** | **Yes** | |
| 10. Void/refund/cancel | S3 | Adjustments allowed with controls; adjustment docs with **own sequential series** different from SI/OR; reference original SI/OR | **High** | Skeleton contracts only | Live reversal series + reason + auth + EJ | **Yes** | **Yes** | |
| 11. Reprint | S3 | Reprint allowed; word **REPRINT**; date/time of reprint; same info as original | **High** | Marker text in receipt profile; no official reprint path | Official reprint without re-allocation | **Yes** if issuing | **Yes** | |
| 12. Training mode / non-official marker | S3 | If training/no-sales mode exists: whether available after deploy; invoice shows **TRAINING MODE** after header and **THIS IS NOT AN OFFICIAL INVOICE/RECEIPT…** | **High** | Training marker field on profile; mode not productized as official gate | Product training-mode isolation from official series | **Yes** if mode can issue official series | **Yes** | |
| 13. Grand total / accumulated total | S2 F-a; S3 | Accumulated grand total sales; ≥12 digits incl. 2 decimals; **reset counter mandatory** when max exhausted | **High** | Not implemented as official accumulator | Persistent grand total + reset counter | **Yes** | **Yes** for Z | |
| 14. Machine serial / MIN / PTU / accreditation | S2; S3 | MIN, serial (or HDD/license), supplier AN + dates, PTU or ATG on document footer | **High** for field presence | Setup fields exist (metadata) | Real PTU/AN lifecycle; print binding | **Yes** for issuance | **Yes** | |
| 15. Cashier/user identification | S3 activity log; X-reading | Activity/transaction log: who (incl. root/admin), values, datetime; X-reading cashier accountability | **High** | Operational cashier/shift; audit fragments | Official bind of cashier to official sale + EJ | Partial | Partial | |
| 16. Taxpayer profile fields | S2; S3 | Registered name, style, address, TIN+branch, VAT mode | **High** | Official business setup | Completeness validation vs legal print | Partial | **Yes** for print | |
| 17. Backup/restore/data retention | S3 | Backup of sales, e-journal, backend reports; frequency/media; retention periods above; cloud decryptability if cloud | **High** for requirement text | Host backup/restore path exists | Retention policy UI; examiner-readable export; RMO table integrity under restore | **No** alone | Partial | Engineering QA required |
| 18. Audit / electronic journal | S2 F-e; S3 | POS: store e-journal on HDD/server; activity log; backend sales post | **High** for presence | EJ schema + append ports; **not live-bound to official sale** | Transactional bind with official post | **Yes** | **Yes** for examination | |

---

## STOP decision (legal)

| Critical area | Status |
|---------------|--------|
| Annex F text acquisition | **Closed enough for engineering** (full checklist text extracted from official DOCX) |
| Retention years | **Closed** (3 / 10 years per Annex F citations) |
| eSales / sales data transmission | **Incomplete** for Salryn traditional POS class (JSON note is for e-invoicing subscription section; class applicability + full schema open) |
| Validation seal technical form | **Unresolved** |
| Official output production readiness | **Blocked** (no accreditation, no legal output engine, push/seal gaps) |

**Live official posting must NOT be implemented in this branch.**

Continue only with: legal docs, source-sale **read-only** dry-run, internal report previews, live-posting **design**, always-blocked candidate skeleton, QA, and source gates.

---

## Explicit non-claims

- Salryn is **not** claimed BIR compliant.
- Salryn is **not** claimed RMO ready, accredited, or certified.
- No official receipt/invoice is issued by this work.
- `liveOfficialPostingAllowed` remains **false**.
