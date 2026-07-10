# RMO-BIR TRAD-M3 — VAT/Non-VAT Invoice Output Study

**Date:** 2026-07-10  
**Branch:** `KIRCH-RMO-BIR-TRAD-M3-VAT-NONVAT-INVOICE-OUTPUT`  
**Base M2 SHA:** `fa90549d008721751e2812bc90193f8dacdcd070`  
**Decision:** **GO WITH NARROW FIX — READ/RENDER FROM EXISTING IMMUTABLE EJ SNAPSHOT (NO MIGRATION)**

**Disclaimer:** Engineering study only. Not BIR accreditation, CoA, PTU, or legal issuance authority.

---

## 6.1 Source and branch stamp

| Item | Value |
|------|--------|
| Local HEAD at study start | `fa90549d008721751e2812bc90193f8dacdcd070` |
| M2 origin | matches local |
| Sandbox | `1f74cd8bcaca1a5df487aa61677ee9829dd7fedb` (untouched) |
| Installer | `6b9c46676390b8a86a989eca600cb02e406ce865` (untouched) |
| Worktree | clean at branch create |
| Promotion / BIR submit | none |

---

## 6.2 Architecture inventory

| Concern | Location |
|---------|----------|
| Snapshot draft contracts | `OfficialDocumentSnapshotDraft*.cs` |
| Snapshot JSON + SHA-256 | `OfficialDocumentSnapshotEvidenceBuilder` → schema `SALRYN_OFFICIAL_DOCUMENT_SNAPSHOT_V1` |
| Persist path | M2 `SqliteOfficialCheckoutPersistenceService` → EJ `snapshot_payload_json` / `snapshot_payload_hash` |
| EJ tables | `bir_electronic_journal_entries` (append-only, no-update triggers) |
| Evidence range export | `SqliteOfficialEvidenceReadService` |
| Dry-run report preview | `OfficialReportPreviewContracts` (watermarked, non-official) |
| Tax buckets / modes | TRAD-M1 engine + draft kinds including SSPT/exempt |
| Activation | `ProductionOfficialPostingActivation` default OFF |
| Host auth | loopback + session on `/api/official/*` |
| Text-file output (evidence package) | `OfficialOutput/*` text-file adapters (not VAT invoice layout) |

**Canonical immutable invoice source (decision):**  
`bir_electronic_journal_entries.snapshot_payload_json` verified by `snapshot_payload_hash` (SHA-256 of canonical UTF-8 payload).  
Not `sales.receipt_snapshot`, not live `bir_machine_profiles`, not current product catalog.

---

## 6.3 Field provenance (summary)

| Field group | Source | Missing behavior |
|-------------|--------|------------------|
| Seller name, TIN, branch, address | Snapshot header | Production-capable fail |
| Document number, datetime, cashier/terminal | Snapshot header | Production-capable fail |
| MIN / serial / accreditation / PTU | Snapshot header only if present | Omit (never invent); production may still render with internal marker if policy requires permits later |
| Lines + amounts + payments | Snapshot | Fail if absent |
| Tax buckets / taxpayer mode | Snapshot `taxBuckets` | Production-capable fail if null/incomplete |
| Buyer name/TIN/address | Not in V1 snapshot | Blank spaces + missing-field note; not invented |
| VAT REG / NON-VAT REG phrase | Derived from `taxBuckets.taxpayerMode` | Fail if mode unknown |
| Non-VAT input-tax disclaimer | Fixed primary-orientation phrase when non-VAT | Always on non-VAT demo/prod-capable |
| Internal watermark | Output mode, not snapshot | Always for InternalDemo mode |

---

## 6.4 Output technology decision

| Option | Decision |
|--------|----------|
| Semantic render model | **Yes** — Core, pure |
| Canonical HTML (self-contained) | **Yes** — printable, escaped |
| Deterministic thermal/plain text | **Yes** — fixed-width 42 cols |
| PDF dependency | **No** — not in repo; not required for M3 |
| Crystal Reports | **No** |
| Core printer coupling | **No** — optional Windows adapter boundary only; M3 host service returns HTML/text package; print is UI/browser or later adapter |
| Browser tax math | **Forbidden** |

---

## 6.5 Data-completeness decision

**GO WITH NARROW FIX — EXTEND IMMUTABLE SNAPSHOT/READ MODEL (read-only)**

- No migration v16 for M3: V1 EJ payload + tax buckets is sufficient for engineering invoice projection when buckets are present.
- Add typed **parse + hash-verify + render** path; never reconstruct from mutable setup.
- Production-capable mode requires complete seller identity, document number, lines, amounts, payments, tax buckets.
- InternalDemo mode always watermarks and lists missing optional fields (buyer, permits).
- Line-level tax classification is not in V1 line objects; bucket aggregates from immutable taxBuckets are authoritative for totals presentation.

---

## 6.6 Scope decision

**In:**

1. VATable-only VAT Invoice  
2. VAT-exempt under VAT seller  
3. Zero-rated under VAT seller  
4. VAT mixed  
5. Non-VAT SSPT  
6. Non-VAT exempt  
7. Non-VAT SSPT + exempt mixed  
8. Host-local preview/export of HTML + plain text  
9. Deterministic SHA-256 of rendered bytes  
10. Retry identity: same snapshot → same official number + same canonical render hash  

**Out:**

- void/refund/adjustment documents (M4)  
- X/Z, EJ export productization, eSales (M5)  
- installer (M6)  
- Annex F / eAccReg (M7/M8)  
- production activation enable  
- checkout wire-up  
- EIS  

---

## Primary sources (orientation)

- RMC 77-2024 + Annex A1–B6 (VAT / Non-VAT Invoice sample fields)  
- RR 7-2024 Invoice-first  
- RMO 24-2023 Traditional CRM/POS context  
- Repo: `RMO_BIR_TRAD_M1_OFFICIAL_SOURCE_REGISTER.md`

Ambiguities recorded: buyer fields optional/blank when not captured; permit/ATP series dates only when snapshotted; SC/PWD lines as blank spaces when not in snapshot.
