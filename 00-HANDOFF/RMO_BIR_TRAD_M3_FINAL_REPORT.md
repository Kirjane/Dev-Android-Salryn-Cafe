# RMO-BIR-TRAD-M3 Final Report

**Date:** 2026-07-10  
**Branch:** `KIRCH-RMO-BIR-TRAD-M3-VAT-NONVAT-INVOICE-OUTPUT`  
**Disclaimer:** Engineering only. Not BIR accreditation, CoA, PTU, or legal issuance authority.

---

## 1. Source stamp

| Item | Value |
|------|--------|
| Base M2 SHA | `fa90549d008721751e2812bc90193f8dacdcd070` |
| M3 branch | `KIRCH-RMO-BIR-TRAD-M3-VAT-NONVAT-INVOICE-OUTPUT` |
| Final M3 SHA | *(branch tip after push — see `git rev-parse HEAD`)* |
| M2 → M3 | `0 behind / N ahead` (attributable M3 commits only) |
| Sandbox | `1f74cd8bcaca1a5df487aa61677ee9829dd7fedb` (untouched) |
| Installer | `6b9c46676390b8a86a989eca600cb02e406ce865` (untouched) |
| Worktree after push | clean expected |

---

## 2. Study decision

**GO WITH NARROW FIX — NO MIGRATION**

- **Canonical immutable source:** `bir_electronic_journal_entries.snapshot_payload_json` + `snapshot_payload_hash` (SHA-256 of `SALRYN_OFFICIAL_DOCUMENT_SNAPSHOT_V1`).
- **Output technology:** semantic model + self-contained HTML + fixed-width thermal text (Core pure); host-local API; no PDF/Crystal dependency.
- **Migration:** not required (catalog remains v15).

---

## 3. Official-source field matrix summary

| Area | Behavior |
|------|----------|
| VAT required | INVOICE title; VAT REG TIN + branch; seller identity; serial; datetime; lines; VATable/VAT/exempt/zero-rated buckets from immutable taxBuckets; total |
| Non-VAT required | INVOICE title; NON-VAT REG TIN + branch; seller identity; serial; datetime; lines; SSPT/exempt buckets; **THIS DOCUMENT IS NOT VALID FOR CLAIM OF INPUT TAX.** |
| VAT mixed | Bucket breakdown from snapshot taxBuckets |
| Non-VAT mixed | SSPT + NonVatExemptSales breakdown |
| Buyer | Blank spaces (not in V1 snapshot) — listed as missing optional |
| Permits | Only if present in snapshot; never invented |
| Ambiguities | Buyer capture, SC/PWD detail lines, ATP series dates not in V1 |

---

## 4. Exact changed files (grouped)

### Docs
- `00-HANDOFF/RMO_BIR_TRAD_M3_VAT_NONVAT_INVOICE_OUTPUT_STUDY.md`
- `00-HANDOFF/RMO_BIR_TRAD_M3_GO_STOP_DECISION.md`
- `00-HANDOFF/RMO_BIR_TRAD_M3_FINAL_REPORT.md`
- `00-HANDOFF/RMO_BIR_TRADITIONAL_CRM_POS_PROGRAM_LEDGER_2026-07-10.md`

### Core
- `src/Salryn.Core/Services/InvoiceOutput/*` (contracts, parser, model builder, HTML/thermal renderers, service)

### Infrastructure
- `src/Salryn.Infrastructure/Data/Services/SqliteOfficialInvoiceSnapshotReadService.cs`

### API
- `src/Salryn.Api.Local/LocalOfficialInvoiceOutputEndpoints.cs`
- `src/Salryn.Api.Local/LocalApiApplicationFactory.cs`

### Web
- `src/Salryn.App.Web/src/api/officialSetup.ts`
- `src/Salryn.App.Web/src/pages/OfficialSetupPage.tsx`

### Tests / CoreSmoke
- `tests/Salryn.Tests/RmoBirTradM3InvoiceOutputChecks.cs`
- `tools/Salryn.Tools.CoreSmoke/Program.cs`

---

## 5. Architecture delivered

1. **Immutable read:** SQLite EJ snapshot by sale ID or document number (read-only).  
2. **Parse + hash verify:** V1 JSON; reject hash mismatch / incomplete.  
3. **Semantic model:** `InvoiceRenderModel` from snapshot only.  
4. **Renderers:** HTML (escaped) + thermal text (deterministic LF).  
5. **Manifest:** package id, identities, variant, SHA-256, watermark, activation OFF, no compliance claim.  
6. **Host API:** `POST /api/official/invoice-output/generate` loopback + session.  
7. **UI:** Official Setup internal projection card (thermal preview).  

---

## 6. Variant evidence (automated)

| Variant | Result |
|---------|--------|
| VATable | pass |
| VAT-exempt | pass |
| Zero-rated | pass |
| VAT mixed | pass |
| Non-VAT SSPT | pass |
| Non-VAT exempt | pass |
| Non-VAT mixed | pass |
| Retry determinism | pass |
| HTML escape | pass |
| Hash mismatch fail | pass |
| Production-capable without buckets fail | pass |

---

## 7. Security and non-mutation

- HTML encoded via `WebUtility.HtmlEncode`.  
- Snapshot read has no INSERT/UPDATE.  
- Output does not call allocate/post.  
- Activation remains OFF; `liveOfficialPostingAllowed=false`; `complianceClaimed=false`.  
- Client cannot disable watermark via a secret flag (server sets InternalDemo by default path; ProductionCapable still watermarks while activation OFF).  

---

## 8. Migration/backup evidence

**No migration required.** Catalog remains `SupportedMigrationVersion = 15`. Future probe remains 16.

---

## 9. Complete gate table

| Gate / command | Result |
|----------------|--------|
| `dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only rmo-bir-trad-invoice-output` | **pass** |
| Full CoreSmoke | **pass** |
| `npm run build` (App.Web) | **pass** |
| M1/M2 regression (in full CoreSmoke) | **pass** |

---

## 10. Manual acceptance table

| Check | Result |
|-------|--------|
| Official Setup invoice projection UI visual | **NOT EXECUTED** (Kirch) |
| Physical printer job | **NOT EXECUTED** (no device evidence) |
| Production-capable unwatermarked delivery | **N/A — blocked** (activation OFF) |

---

## 11. Commit/push evidence

See closeout CLI: commit message `Add deterministic VAT and non-VAT invoice output`; HEAD equals origin/M3; worktree clean.

---

## 12. Honest status

**Delivered:** engineering capability to project VAT and non-VAT invoices from committed EJ snapshots with deterministic HTML/text, host preview, fail-closed completeness, and watermarked internal demo mode.

**Not delivered / still true:**

- QA1 manual Official Setup acceptance still pending Kirch  
- M3 physical printer / full visual acceptance not executed  
- Normal checkout still not bound to official posting  
- Production official activation remains OFF  
- No accreditation / compliance / PTU / legal-issuance claim  
- No Sandbox promotion; no BIR submission  

---

## 13. Remaining blockers and next lane

1. Kirch: QA1 manual UI + optional M3 visual/printer rows.  
2. **Next engineering lane:** `KIRCH-RMO-BIR-TRAD-M4-…` document lifecycle (reprint/void/refund/adjustment) — **do not start in this handoff.**

---

## Ending

**TRAD-M3 ENGINEERING COMPLETE — MANUAL OUTPUT ACCEPTANCE PENDING;**
