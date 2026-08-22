# RMO-BIR-M2 Receipt / Report Data Contracts

**Date:** 2026-07-09  
**Branch:** `KIRCH-RMO-BIR-M2`  
**Mode:** Contracts only — no renderer/print/Crystal.  
**Disclaimer:** Not official output. Not compliance.

Anchors in repo: `OfficialDocumentSnapshotDraft*`, tax bucket drafts, X/Z services, electronic journal append contracts.

---

## A. Official sale document contract

Canonical payload produced **at post** (immutable thereafter). Future print/PDF/Crystal **only read** this.

| Field group | Fields | Current support | Gap |
|-------------|--------|-----------------|-----|
| Taxpayer | registered name, business style, address, TIN, branch code, VAT/NON-VAT phrase | Partial (profile + draft header) | Denormalize into snapshot at post |
| Machine | MIN, serial, terminal #, software version/build | Partial | Stamp build from release |
| Supplier footer | supplier name/address/TIN, AN, PTU/ATG | Missing | Config + snapshot |
| Operator | cashier user id/display name, role | Partial (session) | Snapshot |
| Document | kind, title, official number, series id, issued UTC, timezone | Partial drafts | Wire allocate |
| Sale | sale id, operational POS number (if dual), shift id | Supported sale | Link fields |
| Lines | product snapshot, qty, unit, unit price, line discount, line total, tax class | Supported items | Tax class per line |
| Totals | subtotal, discount total, tax total, grand total, amount paid, change | Supported | — |
| Tax buckets | VATable, VAT amount, exempt, zero-rated | Partial engine | Live calc |
| Payments | method, amount, timestamp | Supported | — |
| Customer | name, address, TIN, business style (when required) | Missing | Optional phase |
| Statutory | SC/PWD/NAAC/Solo Parent ids/discounts/signature flag | Missing | Optional phase |
| Markers | is_reprint, reprint_count, is_training, is_voided, is_refunded, is_supplementary | Partial reprint service | Finalize |
| Integrity | content hash placeholder, schema version | Missing | Add to contract |
| Legal | footer text, supplementary disclaimer, EXEMPT flag | Missing | Template config |

**Schema dependency:** Snapshot persistence table (if not only derived) — **future**; today drafts are in-memory shapes.  
**Report dependency:** REPORTS-M1 maps contract → layout.  
**Open questions:** Invoice vs OR title defaults; whether customer block is always blank for B2C retail.

---

## B. X-reading contract

Inquiry report; **does not reset** counters or close day.

| Field | Notes | Support | Gap |
|-------|-------|---------|-----|
| Scope | active shift and/or business day; terminal optional | Partial services | Product scope rules |
| Generated at | UTC + local | Partial | — |
| Cashier / terminal filters | optional | Partial | UX |
| Sales count / totals | gross, net, discount | Partial | Stabilize |
| Tax breakdown | VATable/VAT/exempt/zero | Partial | — |
| Payment breakdown | cash/card/etc. | Partial | — |
| Void/refund/discount counts | required for accountability | Partial | Complete |
| Official number range covered | first–last in scope | Partial Z has range; X needs | X range |
| Grand total (running) | F-a | Missing | Model |
| Training sales excluded | yes | Missing | Flag filter |
| Not-reset proof | document says non-closing | Policy | — |

**Schema dependency:** Read from sales + payments + voids; no X table required if computed.  
**Report dependency:** REPORTS-M1 layout.

---

## C. Z-reading contract

Closing reading; **preserved**; may close shift/day counters per product policy.

| Field | Notes | Support | Gap |
|-------|-------|---------|-----|
| Z id | durable key | `bir_z_readings` | Operator finalize UX |
| Machine profile id | FK | Yes | — |
| Period start/end | shift or day | Partial | Freeze definition |
| First/last official document numbers | required | Partial columns | Enforce |
| Totals / tax / payments | same as X | Partial JSON enrichment | Stabilize schema |
| Void/refund/discount | required | Partial | — |
| Grand total after close | F-a | Missing | Model |
| eSales/export relationship | placeholder export id | Missing | Unresolved format |
| Immutable after finalize | yes | Partial | Hard lock |

**Invariant:** Finalized Z is append-only; corrections via new Z / correction events only.  
**Schema dependency:** existing `bir_z_readings` + possible grand total.  
**Report dependency:** REPORTS-M1 + examiner sample pack.

---

## D. Electronic journal contract

Append-only official event stream (F-e).

| Event type | Payload essentials | Support | Gap |
|------------|--------------------|---------|-----|
| SALE_POSTED_OFFICIAL | sale id, official #, snapshot hash, user, terminal, UTC | Append service exists | Live checkout wire |
| SALE_POSTED_OPERATIONAL | optional non-official audit | App audit | Policy |
| REPRINT | original #, reprint n, user | Reprint contracts | Live |
| VOID / CANCEL / REFUND | original #, reverse id, reason, user | Correction migration | Product path |
| PROFILE_CHANGED | profile id, before/after hash | Missing | Config audit |
| SEQUENCE_ACTIVATED / EXHAUSTED | sequence id | Missing | — |
| Z_FINALIZED | z id | Partial | Wire |
| X_GENERATED | optional | Missing | Optional |

| Property | Proposal |
|----------|----------|
| Ordering | Monotonic sequence per machine profile |
| Mutation | DB triggers block UPDATE/DELETE on EJ tables (existing hardening) |
| Export shape | Versioned JSON/CSV placeholder — format **unresolved** vs eSales |
| Hash chain | Optional next-hash field for tamper evidence — design optional M3+ |

---

## E. Audit contract (application)

Broader than EJ; includes non-official admin actions.

| Field | Required | Support |
|-------|----------|---------|
| User id / username | Yes | Session |
| Role | Yes | Session |
| Timestamp UTC | Yes | — |
| Terminal / machine | Yes when available | Partial |
| Operation code | Yes | Partial |
| Entity type/id | Yes | Partial |
| Before/after metadata | When safe | Partial |
| Reason code | Void/refund/config | Missing completeness |
| Correlation id | Sale/post TX | Partial |

**Open:** Whether every X generation is EJ or only audit.

---

## F. Contract versioning

| Rule | Proposal |
|------|----------|
| `contract_schema_version` on snapshot/EJ/Z | Integer, start at 1 |
| Renderers accept only known versions | Fail closed |
| Migrations never rewrite old snapshots | Append new versions only |

---

## G. What M3 should implement first

1. Freeze **C# record contracts** (or extend existing drafts) for OfficialSaleDocument, XReading, ZReading, EjEvent.  
2. Host readiness service: can post official?  
3. **No** print path; **no** Crystal.  
4. Unit tests: invariants on records (padding, required header fields when official).
