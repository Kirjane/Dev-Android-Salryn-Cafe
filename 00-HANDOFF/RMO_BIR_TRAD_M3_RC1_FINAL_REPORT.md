# RMO-BIR-TRAD-M3-RC1 Final Report

**Date:** 2026-07-10  
**Branch:** `KIRCH-RMO-BIR-TRAD-M3-RC1-INVOICE-OUTPUT-DELIVERY-CLOSEOUT`  
**Disclaimer:** Engineering only. Not BIR accreditation, CoA, PTU, or legal issuance.

---

## 1. Source stamp

| Item | Value |
|------|--------|
| M3 base | `e465770d2000d02a19295afef0e7ff1fcefe7f7a` |
| RC1 branch | `KIRCH-RMO-BIR-TRAD-M3-RC1-INVOICE-OUTPUT-DELIVERY-CLOSEOUT` |
| Final tip | *(after push)* |
| M3 → RC1 | `0 behind / N ahead` |
| Sandbox | `1f74cd8b…` untouched |
| Installer | `6b9c4667…` untouched |
| Worktree | clean expected |

## 2. Study decision

**GO — BROWSER PRINT + DOWNLOAD + OPTIONAL SNAPSHOT EXTENSION (NO MIGRATION)**

- Delivery: browser-host print via `print-view` + raw HTML/thermal downloads  
- Export: `text/html` / `text/plain`, `no-store`, `X-Salryn-Content-Sha256`, sanitized filename  
- Conditional fields: optional `buyer` / `statutory` JSON on V1 schema (omit when null → historical hashes stable)  
- Migration: **none**

## 3. Exact changed files

### Docs
- `RMO_BIR_TRAD_M3_RC1_INVOICE_OUTPUT_DELIVERY_STUDY.md`
- `RMO_BIR_TRAD_M3_RC1_GO_STOP_DECISION.md`
- `RMO_BIR_TRAD_M3_MANUAL_OUTPUT_ACCEPTANCE.md`
- `RMO_BIR_TRAD_M3_RC1_FINAL_REPORT.md`
- program ledger

### Core
- snapshot draft buyer/statutory records
- evidence writer optional sections
- parser + render model + HTML/thermal buyer/statutory display
- print CSS in HTML

### API
- `LocalOfficialInvoiceOutputEndpoints` — generate (reject client ProductionCapable), html, thermal, print-view

### Web
- download / print preview buttons
- stale wording fix
- client no longer sends mode

### Tests
- `RmoBirTradM3Rc1InvoiceOutputDeliveryChecks`
- CoreSmoke `rmo-bir-trad-invoice-output-delivery`

## 4. Delivery capability

| Capability | Status |
|------------|--------|
| HTML download | GET `/api/official/invoice-output/html` → raw bytes + attachment filename |
| Thermal download | GET `/api/official/invoice-output/thermal` |
| Print view | GET `/api/official/invoice-output/print-view` (inline) |
| Preview | Official Setup thermal + open print window |
| Cache | `no-store` |
| Hash | `X-Salryn-Content-Sha256` header; canonical hash preserved from M3 pipeline |

## 5. Immutable conditional-field capability

| | Behavior |
|--|----------|
| V1 without buyer | Omits `buyer` property; blanks + missingOptional |
| V1+ with buyer/statutory | Written into snapshot JSON at post; hashed; rendered when present |
| Never | Invent buyer/permits from live setup at render time |

## 6. Security evidence

- Server-owned InternalDemo only on public endpoints  
- Client `ProductionCapable` → 403 `InvoiceOutput.ClientModeNotAllowed`  
- Loopback + session  
- HTML encode; sanitized filenames  
- No allocate/post on delivery  

## 7. Non-mutation / retry

- Delivery re-generates from same immutable snapshot → same official number + content SHA-256  
- Activation OFF; live posting false; complianceClaimed false  

## 8. Variant regression

M3 seven variants still pass via full CoreSmoke; RC1 adds buyer-supplied case + V1-without-buyer case.

## 9. Migration

**No migration required** — optional JSON only inside existing EJ payload column.

## 10. Gates

| Gate | Result |
|------|--------|
| `--only rmo-bir-trad-invoice-output-delivery` | **pass** |
| `--only rmo-bir-trad-invoice-output` | **pass** |
| Full CoreSmoke | **pass** |
| `npm run build` | *(run at closeout)* |

## 11. Manual acceptance

| Check | Status |
|-------|--------|
| Visual UI | **NOT EXECUTED** (see checklist doc) |
| Browser print | **NOT EXECUTED** |
| Virtual printer | **NOT EXECUTED** |
| Physical printer | **NOT EXECUTED** |

Checklist: `00-HANDOFF/RMO_BIR_TRAD_M3_MANUAL_OUTPUT_ACCEPTANCE.md`

## 12. Commit/push

See CLI closeout.

## 13. Honest status and next lane

RC1 engineering closes download/print-view delivery, server-owned mode, optional buyer/statutory snapshot capability, UI wording, and manual checklist artifact.

Still true: production activation OFF; checkout unbound; no accreditation claim; QA1 manual pending; physical print not observed.

**Next engineering (not started):** TRAD-M4 document lifecycle.

---

**TRAD-M3-RC1 ENGINEERING COMPLETE — MANUAL PRINT ACCEPTANCE PENDING;**
