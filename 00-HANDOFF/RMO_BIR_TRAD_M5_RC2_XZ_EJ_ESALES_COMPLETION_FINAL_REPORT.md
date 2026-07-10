# M5-RC2 X/Z/EJ/eSales Completion — Final Report

**Branch:** `KIRCH-RMO-BIR-TRAD-M5-RC2-XZ-EJ-ESALES-COMPLETION`  
**Base (M4-RC2):** `05af9e22fbfabbdf77b70379323ec68960293782`  
**Date:** 2026-07-10

## 1. Starting refs

| Ref | SHA |
|-----|-----|
| M4-RC2 base | `05af9e22fbfabbdf77b70379323ec68960293782` |
| Old M5-RC1 (archival) | `0e3b21d341c25fe4b954cb0251270a5d220bf332` |
| Sandbox | `1f74cd8bcaca1a5df487aa61677ee9829dd7fedb` |
| Installer | `6b9c46676390b8a86a989eca600cb02e406ce865` |

## 2. Final branch / SHA

Filled after push. M4-RC2…HEAD ahead/behind reported exactly.

## 3. Study decision

**GO WITH LIFECYCLE-AWARE M5 COMPLETION.** No v18. BIR eSales schema unconfirmed; adapter sealed.

## 4. Old M5 handling

| Component | Disposition |
|-----------|-------------|
| Endpoint map pattern | Reused |
| Gross-only draft source | **Rejected** — rewritten as projection |
| Status honesty flags | Reused/expanded |
| Blind merge of M5-RC1 branch | **Not done** |

## 5–10. Architecture

- **Canonical projection:** `SqliteOfficialReportingProjectionService` over EJ + lifecycle_state + events + settlements  
- **X:** read-only projection preview  
- **Z:** transactional finalize + unique range + lifecycle control totals in `metadata_json`; failure injection rollback  
- **EJ:** `SALRYN_TRAD_EJ_EXPORT_V2` with ORIGINAL/REPRINT/VOID/REFUND stream  
- **eSales:** `SALRYN_TRAD_ESALES_DRAFT_V2` + `ITraditionalESalesSchemaProvider` (unconfirmed; transmission false)  

## 11. VAT/non-VAT/refund/void/reprint matrix

Runtime gate proves VAT mixed, non-VAT SSPT, non-VAT exempt, partial refund netting, void, reprint non-increase, hash fail-closed.

## 12. API/UI

- Host endpoints under `/api/official/traditional-reporting/*`  
- Host page `Traditional Reporting` (loopback/session)  

## 13. Migration

**v17 retained** (no v18).

## 14–15. Gates

| Gate | Result |
|------|--------|
| `rmo-bir-trad-m5-rc2-xz-ej-esales-completion` | PASS |
| M4-RC2 accounting runtime | PASS (regression) |
| QA1 real-db | PASS |
| Full CoreSmoke | PASS (this run) |

## 16–17. Manual

UI/print/installer/Annex F: **NOT EXECUTED** (human).

## 18–20. Push / freeze

See freeze doc. No Sandbox/installer change. No M6+.

## 21. External blockers

BIR eSales authoritative schema; legal filing; Kirch authorization; BIR evaluation.
