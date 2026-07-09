# RMO-BIR-M1 Current Capability Inventory

**Date:** 2026-07-09  
**Branch:** `KIRCH-RMO-BIR-M1`  
**Base Sandbox:** `1f74cd8b`  
**Mode:** Code inventory only. No compliance claim.

---

## Snapshot

| Area | Support level | RMO/BIR risk impact |
|------|---------------|---------------------|
| Host POS checkout (LAN-stable React + API) | **Supported** (business POS) | High — must not be confused with official output |
| Official document sequence schema + allocator | **Partial** (not wired to live checkout) | High |
| BIR machine profile schema | **Partial** (schema/services; no operator UI gate) | High |
| Electronic journal schema + append boundary | **Partial** (not live-checkout integrated) | High |
| X/Z reading services + Z persistence | **Partial** (engineering; not productized UI) | High |
| Backend official report layouts (text/layout builders) | **Partial** | Medium–High |
| Crystal / PDF / ESC/POS printer | **Missing** | Medium (output lane) |
| ProgramData production DB + backup models | **Partial** | Medium |
| LAN Remote POS (non-official) | **Supported** (stable) | Medium — must stay non-official |
| Licensing / accreditation evidence pack | **Missing** | Medium |

---

## 1. Checkout / sales transaction model

| Item | Detail |
|------|--------|
| **Support** | **Supported** for internal POS sales |
| **Evidence** | `src/Salryn.Api.Local/Services/HostOperatorCheckoutApiService.cs`; sale write repos under `src/Salryn.Infrastructure/Data`; Core sale models |
| **Numbering today** | `HostOperatorReceiptNumberAllocator` — **in-process POS prefix numbers**, not BIR official sequences |
| **Limitations** | Live host checkout does **not** allocate from `bir_official_document_sequences`; no official snapshot persistence on complete |
| **Stability** | Candidate-stable for **non-official** host checkout (V2 gates) |
| **RMO risk** | **High** if operators treat POS numbers as official SI/OR |

---

## 2. Shift open/close

| Item | Detail |
|------|--------|
| **Support** | **Supported** (host shift API) |
| **Evidence** | `LocalHostShiftEndpoints`; cashier session services/repos; V2 `v2-host-shift` |
| **Limitations** | Shift is operational; **not** equivalent to Z-reading or eSales close |
| **RMO risk** | Medium — day-close reporting still missing |

---

## 3. Audit trail (application)

| Item | Detail |
|------|--------|
| **Support** | **Partial** |
| **Evidence** | Audit read models/endpoints (remote-readonly audit); official document audit event services in Core |
| **Limitations** | General audit ≠ RMO **e-journal of every official print/void/reprint** |
| **RMO risk** | High for F-c / F-e |

---

## 4. User roles / cashier identity

| Item | Detail |
|------|--------|
| **Support** | **Supported** (login, roles, session cookie) |
| **Evidence** | `LocalApiSessionService`; user admin endpoints; permission guards |
| **Limitations** | Cashier identity available for host ops; official header wiring incomplete |
| **RMO risk** | Medium |

---

## 5. Backup / recovery

| Item | Detail |
|------|--------|
| **Support** | **Partial** |
| **Evidence** | `DbPaths` ProgramData layout; `BackupMobileReadinessModels`; host backup authority rules; DB_A1/A2 handoffs |
| **Limitations** | Production backup/restore UX and official retention policy incomplete |
| **RMO risk** | Medium (F-l) |

---

## 6. Product / price / tax fields

| Item | Detail |
|------|--------|
| **Support** | **Partial** |
| **Evidence** | Product models/repos; sale tax field; store tax mode; `OfficialDocumentTaxBucketEngine` / draft validators |
| **Limitations** | Full VAT/exempt/zero-rated **product classification + live calculator on checkout** not productized; C05 still incomplete |
| **RMO risk** | High for mixed VAT output |

---

## 7. Void / cancel / refund

| Item | Detail |
|------|--------|
| **Support** | **Partial** |
| **Evidence** | Void-related model fields historically; official lifecycle / correction event migrations (`BirElectronicJournalCorrectionEventSchemaMigration`) |
| **Limitations** | End-to-end privileged void/refund/cancel **product UX + journal events** not closed for official path |
| **RMO risk** | **Blocker** for F-b / F-e / supplementary docs |

---

## 8. Electronic journal

| Item | Detail |
|------|--------|
| **Support** | **Partial** (strong foundation) |
| **Evidence** | Migrations: `BirElectronicJournalSchemaMigration`, hardening, correction events; `ElectronicJournalAppendService`; SQLite append ports; append-only hardening seals in `00-COMPLIANCE` |
| **Limitations** | **Not proven wired** from live host checkout completion; export/preservation workflow incomplete |
| **RMO risk** | **Blocker** for F-e until live path |

---

## 9. Official sequences & machine profile

| Item | Detail |
|------|--------|
| **Support** | **Partial** (schema + services) |
| **Evidence** | `bir_machine_profiles` migration; `bir_official_document_sequences`; `OfficialDocumentSequenceAllocationService` (Core); gates on ACCREDITED + official_output_enabled |
| **Limitations** | No complete store UI for setup; not the default host checkout number path |
| **RMO risk** | **Blocker** for F-h / accreditation demo |

---

## 10. X-reading / Z-reading

| Item | Detail |
|------|--------|
| **Support** | **Partial** |
| **Evidence** | `OfficialXReading` / `OfficialZReading` services; `SqliteOfficialZReadingService` → `bir_z_readings`; host command handlers `OfficialReadingHostCommandHandlers` |
| **Limitations** | Not a finished operator product UI; must not claim examiner-ready samples |
| **RMO risk** | High (F-f; RMO 15-2018 post-eval) |

---

## 11. Official snapshots / reprint / evidence

| Item | Detail |
|------|--------|
| **Support** | **Partial** |
| **Evidence** | `OfficialDocumentSnapshotDraftBuilder`; tax bucket engine; reprint service contracts; `OfficialEngineeringEvidencePackageAssembler`; text layout builders for audit evidence |
| **Limitations** | No Crystal/PDF; no production print path; drafts ≠ issued official documents |
| **RMO risk** | High (output content S2 C-list) |

---

## 12. Terminal identity / LAN boundaries

| Item | Detail |
|------|--------|
| **Support** | **Supported** for multi-terminal **non-official** Remote POS |
| **Evidence** | Terminal registry, pairing, `SalrynLanRemoteTransportGate` forbidding official commands remotely |
| **Limitations** | Remote POS uses separate receipt allocator (`RemotePosReceiptNumberAllocator` / RPOS-style) — must remain non-official |
| **RMO risk** | Medium — confusion risk if not labeled |

---

## 13. Reports / Crystal / printer / PDF

| Item | Detail |
|------|--------|
| **Support** | **Missing** product reports; some **engineering text layouts** exist |
| **Evidence** | No Crystal package; no ESC/POS printer stack as product; roadmap defers REPORTS-M1 after matrix |
| **RMO risk** | High for F-g backend reports and sample pack |

---

## 14. Database schema / migrations

| Item | Detail |
|------|--------|
| **Support** | **Partial–strong foundation** |
| **Evidence** | BIR migrations listed under `src/Salryn.Infrastructure/Data/Migrations/Bir*.cs`; sales/inventory/session tables from earlier phases |
| **Limitations** | Schema ≠ accreditation readiness; more fields may still be needed after Annex F deep-read |
| **RMO risk** | Medium if further schema changes freeze wrong models |

---

## 15. Installer / data directory

| Item | Detail |
|------|--------|
| **Support** | **Installer lane paused** on `KIRCH-INSTALLER-M1` (not this branch) |
| **Evidence** | Production `DbPaths.CreateProduction` → `%ProgramData%\Salryn\data\salryn.db` on Sandbox codebase |
| **Limitations** | Sandbox does not carry latest installer commits; RMO work must not depend on installer branch |
| **RMO risk** | Low for matrix; Medium later for production retention/backup |

---

## Explicit non-claims

- Salryn is **not** BIR-accredited.  
- Host checkout receipts are **not** official SI/OR.  
- Electronic journal foundations are **not** complete compliance.  
- X/Z services are **not** examiner-certified outputs.
