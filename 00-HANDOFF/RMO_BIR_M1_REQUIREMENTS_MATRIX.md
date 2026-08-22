# RMO-BIR-M1 Requirements Matrix

**Date:** 2026-07-09  
**Branch:** `KIRCH-RMO-BIR-M1`  
**Sources:** See `RMO_BIR_M1_SOURCE_REGISTER.md` (`S#` IDs).  
**Disclaimer:** Mapping only. **No compliance claim.**

### Legend

| Field | Values |
|-------|--------|
| Support | supported / partial / missing / not applicable / unknown |
| Risk | blocker / high / medium / low / unknown |

---

## Matrix

| ID | Category | Source | Requirement summary | Support | Evidence | Gap | Risk | Lane | Notes |
|----|----------|--------|---------------------|---------|----------|-----|------|------|-------|
| RMO-BIR-001 | Machine identity | S1, S2 | POS software generating invoices/receipts is subject to **BIR accreditation** + registration via eAccReg | partial | `bir_machine_profiles` schema/migrations; no accreditation process automation | Operator/supplier accreditation workflow is external; product must expose profile fields for PTU/AN | high | RMO-BIR-M2 | Salryn is a candidate system under scope table for POS software |
| RMO-BIR-002 | Machine identity | S2, S5 | Machine/software identity: brand/model/serial or license; MIN; software version | partial | Machine profile fields (J21-2) | UI + validated active profile for production | high | RMO-BIR-M2 | |
| RMO-BIR-003 | Machine identity | S2, S5 | PTU number, dates; accreditation number/validity; conspicuously available on site | partial | Profile fields | UI; print footer consumption | high | RMO-BIR-M2 / REPORTS-M1 | |
| RMO-BIR-004 | Taxpayer info | S2 | Header: registered seller name, business style, address, VAT/NON-VAT REG TIN + branch code | partial | Profile + snapshot draft builders | Live snapshot from profile on every official doc | high | RMO-BIR-M2 | |
| RMO-BIR-005 | Numbering | S2 F-h, C-list | Sequential accountable invoice/receipt numbers, **min 6 digits**, reset counter if applicable | partial | `bir_official_document_sequences` + allocation service | **Not used** by live host checkout allocator | **blocker** | RMO-BIR-M2 | Host uses `HostOperatorReceiptNumberAllocator` |
| RMO-BIR-006 | Numbering | S2 | Transaction number series distinct from SI/OR series if both exist | unknown | not found | Policy decision + dual series if needed | unknown | BACKLOG | Needs product decision |
| RMO-BIR-007 | Invoice/receipt output | S2 → S10–S12 | Generate principal invoices/receipts with full required body fields (qty, desc, unit, totals, VAT, etc.) | partial | Snapshot draft + tax bucket engines | No official render/print; checkout not official path | **blocker** | REPORTS-M1 after M2 | Crystal not required yet |
| RMO-BIR-008 | Invoice/receipt output | S6, S7 | Align document title/series with RR 7-2024 / RMC 77-2024 Invoice vs OR rules | unknown | not found in product | Policy + series naming | high | RMO-BIR-M2 | Full RR 7-2024 line audit still open |
| RMO-BIR-009 | Invoice/receipt output | S2 | Footer: accredited supplier name/TIN; AN; PTU/ATG | missing | draft shape only | Renderer + config | high | REPORTS-M1 | |
| RMO-BIR-010 | Invoice/receipt output | S2 | Supplementary docs marked not valid for input tax; EXEMPT wording rules | missing | not found | Template rules | high | REPORTS-M1 | |
| RMO-BIR-011 | Sales transaction | Inventory | Persist sale items, payments, tax, discount, timestamps, cashier | supported | Sale models/repos; host checkout | Official lock + journal append | medium | RMO-BIR-M2 | Good base for non-official POS |
| RMO-BIR-012 | User access | S2 (cashier id) | Identify cashier/user on official documents | partial | Session user; snapshot shapes | Official path wiring | medium | RMO-BIR-M2 | |
| RMO-BIR-013 | Void/refund/cancel | S2 F-b/F-e, C-list | Traceable void/cancel/refund/return documents; no silent edit of posted sales | partial | Void fields; journal correction migration | Productized official correction path | **blocker** | RMO-BIR-M2 | |
| RMO-BIR-014 | Security/tamper | S2 F-b | Tamper-free: no sales suppression; no silent mutation of official data | partial | EJ append-only triggers; host write boundaries | Official checkout lock; training mode watermark | **blocker** | RMO-BIR-M2 / D lane | |
| RMO-BIR-015 | Audit trail | S2 F-c | Activity/transaction log of critical system activity | partial | App audit reads; official audit event services | Complete coverage list vs Annex F | high | RMO-BIR-M2 | |
| RMO-BIR-016 | Electronic journal | S2 F-e | E-journal/audit journal for CRM/POS official events | partial | `bir_electronic_journal_*` + append service | Live checkout append + export | **blocker** | RMO-BIR-M2 | |
| RMO-BIR-017 | Electronic journal | S5 | Examiner can extract eSales journal / sales journal data | missing | not found as product export | Export contract | high | REPORTS-M1 | |
| RMO-BIR-018 | Shift/day close | S2 F-f | Sales readings (X/Z class reports) | partial | X/Z services + `bir_z_readings` | Operator UX; sample packs | high | REPORTS-M1 | |
| RMO-BIR-019 | X/Z reading | S2, S5 | X-reading without reset; Z-reading preserved closing report with totals/ranges | partial | Official X/Z services | Product UI + tax buckets on report | high | REPORTS-M1 | |
| RMO-BIR-020 | Reports/export | S2 F-g | Backend reports (sales book, VAT breakdowns, voids, cashier, payments) | missing | engineering layouts only | Report package decision | high | REPORTS-M1 | Crystal only if justified |
| RMO-BIR-021 | Sales data transmission | S2 F-m | Sales data transmission (“push” / eSales class requirements) | missing / unknown | not found | Exact taxpayer obligations unresolved | high | BACKLOG | Needs taxpayer category + official eSales rules |
| RMO-BIR-022 | Push / validation seal | S2 F-j, F-k | “Push” functionality; verification/validation seal | unknown | not found | Annex F deep-read | unknown | BACKLOG | Do not invent |
| RMO-BIR-023 | Non-volatile memory | S2 F-d | Durable non-volatile storage of sales data | partial | SQLite on disk / ProgramData | Document durability model for TWG | medium | RMO-BIR-M2 | Engineering docs |
| RMO-BIR-024 | Grand total | S2 F-a | Accumulated grand total sales | missing | not found as continuous grand total | Model + Z/X integration | high | RMO-BIR-M2 | |
| RMO-BIR-025 | Reprint | S2 F-i | Reprint functionality with marking | partial | Official reprint service contracts | Live path + journal event | high | RMO-BIR-M2 | |
| RMO-BIR-026 | Data retention | S2 F-l | Retain official sales/journal data for required period | partial / unknown | ProgramData + backups models | Legal retention years unresolved | high | BACKLOG / INSTALLER | Source gap |
| RMO-BIR-027 | Backup/restore | S2 F-l, inventory | Backup/restore preserves sequences + journals | partial | Continuity models; DB safety work | Product restore drills | medium | INSTALLER-M1 / BACKLOG | Installer lane separate |
| RMO-BIR-028 | Customer / statutory discount | S2 C-list | SC/PWD/NAAC/Solo Parent fields when applicable | missing | not found | Data model + UI | medium | BACKLOG | After spine |
| RMO-BIR-029 | VAT buckets | S2 | VATable / exempt / zero-rated breakdown on mixed sales | partial | Tax bucket engine/drafts | Live calc + product classification | high | RMO-BIR-M2 | |
| RMO-BIR-030 | Training/demo mode | S2 F-b (integrity) | Training output must not look official | missing | not found | Mode flag + watermark | high | RMO-BIR-M2 | |
| RMO-BIR-031 | LAN / multi-terminal | Inventory, S2 | Remote terminals generating receipts may be in scope; host must own official data | supported (non-official LAN) | LAN gate blocks official remote commands | Keep official generation host-only | high | NO ACTION (policy) | Do not enable remote official |
| RMO-BIR-032 | Accreditation evidence pack | S2, S3, S4 | Manuals, samples (SI/OR, X, Z, EJ), architecture, demo checklist | missing | handoffs only | Evidence pack after features exist | medium | BACKLOG | After M2+REPORTS |
| RMO-BIR-033 | Installer/deployment | Inventory | Production data path separated from dev; no casual prod reset | partial | `DbPaths`; installer on other branch | Complete install/upgrade tests | medium | INSTALLER-M1 | Paused lane |
| RMO-BIR-034 | Licensing | Roadmap | Not RMO accreditation; commercial license separate | missing | not found | Out of RMO matrix | low | LICENSE-M1 | Do not conflate |
| RMO-BIR-035 | Crystal Reports | Roadmap | Optional report engine only if REPORTS-M1 chooses it | missing | not found | Decision after contracts | low | CRYSTAL-M1 only if justified | **Not ready to choose Crystal** |

---

## Summary counts (approximate)

| Support | Count |
|---------|-------|
| supported | 2 |
| partial | 18 |
| missing | 10 |
| unknown | 4 |
| not applicable | 0 |
| **Total rows** | **35** |

| Risk | Count (approx) |
|------|----------------|
| blocker | 5 |
| high | 20 |
| medium | 7 |
| low | 2 |
| unknown | 2 |

### Blockers (must address before any “official output” claim)

1. **RMO-BIR-005** — official sequential numbering not on live checkout  
2. **RMO-BIR-007** — no official document generation path  
3. **RMO-BIR-013** — void/refund/cancel traceability incomplete  
4. **RMO-BIR-014** — tamper-free official lock incomplete  
5. **RMO-BIR-016** — e-journal not live-integrated  

---

## Mapping note vs J21 C01–C13

Internal L1 controls C01–C13 remain useful engineering IDs. This matrix prefers **S1/S2 feature letters** and explicit BIR field lists. Where L1 and S2 conflict, **S1/S2 win**.
