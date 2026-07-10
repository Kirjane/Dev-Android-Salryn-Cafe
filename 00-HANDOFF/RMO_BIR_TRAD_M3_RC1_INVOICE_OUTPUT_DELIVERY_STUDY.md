# RMO-BIR TRAD-M3-RC1 — Invoice Output Delivery Closeout Study

**Date:** 2026-07-10  
**Branch:** `KIRCH-RMO-BIR-TRAD-M3-RC1-INVOICE-OUTPUT-DELIVERY-CLOSEOUT`  
**Base M3 tip:** `e465770d2000d02a19295afef0e7ff1fcefe7f7a`  
**Decision:** **GO — BROWSER PRINT + DOWNLOAD + VERSIONED OPTIONAL SNAPSHOT FIELDS (NO MIGRATION)**

**Disclaimer:** Engineering only. Not BIR accreditation, CoA, PTU, or legal issuance.

---

## 6.1 Stamp

| Item | Value |
|------|--------|
| M3 base | `e465770d…` |
| M2 | `fa90549d…` |
| Sandbox | `1f74cd8b…` untouched |
| Installer | `6b9c4667…` untouched |
| Worktree at branch create | clean |
| Promotion / BIR submit | none |

---

## 6.2 Delivery inventory

| Surface | State before RC1 |
|---------|------------------|
| Generate API | JSON with `utf8Text` only |
| Download | absent |
| Print | absent (study deferred) |
| Client mode | can request `ProductionCapable` |
| Snapshot buyer/statutory | not capturable in V1 write path |
| UI wording | “Official output is still not implemented.” |
| Manual acceptance file | missing |

---

## 6.3 Delivery technology decision

**Option A — browser-host print workflow (chosen)**

Evidence: host is WebView2 + local API; self-contained HTML already deterministic; no printer-spool package in Core.

- Typed authenticated download endpoints return raw HTML/text bytes + Content-Disposition  
- Web opens Blob URL in a new window for preview/print (`window.print`)  
- No `dangerouslySetInnerHTML` in main shell  
- Object URL revoked after use  
- Native Windows spool deferred (not required if browser print is real)

**Option B native spool:** not selected for RC1 (unnecessary platform complexity; no existing spool dependency).

---

## 6.4 Export/download design

| Format | Content-Type | Disposition |
|--------|--------------|-------------|
| HTML | `text/html; charset=utf-8` | `attachment; filename="<safe>.html"` or `inline` for print-view |
| Thermal | `text/plain; charset=utf-8` | `attachment; filename="<safe>.txt"` |

- Server-selected filename from official document number (sanitized)  
- `Cache-Control: no-store`  
- Max size: same as generated artifact (bounded by snapshot)  
- Loopback + session required  
- Hash in response headers optional: `X-Salryn-Content-Sha256`

---

## 6.5 Immutable conditional fields

**Backward-compatible optional JSON objects** on existing schema string `SALRYN_OFFICIAL_DOCUMENT_SNAPSHOT_V1`:

- `buyer`: name, tin, address (nullable strings)  
- `statutory`: discountCategory, governmentIdNumber, signatureReference, withholdingTaxCents  

Rules:

- Omitted on historical rows → V1 behavior (blank spaces + missing-optional list)  
- Written only when supplied at post time → becomes part of hash  
- Never filled from live setup/catalog at render time  
- Production-capable (future) may require buyer when policy says so; InternalDemo always watermarks  

No SQLite migration: fields live inside snapshot JSON already stored in EJ.

---

## 6.6 Decision

**GO — BROWSER PRINT + DOWNLOAD + OPTIONAL SNAPSHOT EXTENSION (NO MIGRATION)**
