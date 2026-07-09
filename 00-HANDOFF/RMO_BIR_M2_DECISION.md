# RMO-BIR-M2 Decision Document

**Date:** 2026-07-09  
**Branch:** `KIRCH-RMO-BIR-M2`  
**Base M1:** `5f650236`  

---

## 1. Can implementation start now?

**Yes — narrow, contract-first slice only.**

| Allowed | Not allowed |
|---------|-------------|
| C# contract types / readiness service skeletons | Crystal, PDF, printer |
| Host-only readiness APIs | Live official checkout switch without maintainer GO |
| Tests for contract invariants | Schema migrations unless separate approved task |
| Docs / no-claim guards | Compliance claims, accreditation UI for customers |

**Do not** flip host checkout to official numbering until contracts + integrity orchestrator design are reviewed.

---

## 2. First implementation lane

### RMO-BIR-M3 — Official sale data contracts + host official readiness gate (no live output)

**Why this first (narrowest high value):**

1. Freezes snapshot / X / Z / EJ **shapes in code** so REPORTS-M1 and future checkout wiring share one source of truth.  
2. Surfaces `OfficialCheckoutReadiness` / profile+sequence gates without changing cashier UX.  
3. Avoids premature sequence wiring into cart (numbering study: no draft allocate).  
4. Config UI (profile editor) can follow as M3b once read models exist.

**Not first:** Crystal, full void product UI, eSales push, installer.

---

## 3. What remains unresolved?

| Topic | Status |
|-------|--------|
| Annex F line items (Push F-j, Seal F-k) | **Unresolved** — DOCX not extractable; needs human copy |
| Retention years (F-l) | **Unresolved** |
| eSales transmission format (F-m) | **Unresolved** / taxpayer-class |
| Invoice vs OR default under RR 7-2024 | **Open product policy** |
| Gapless series after failed posts | **Open compliance review** |
| Refund as separate official series | **Open** |
| SC/PWD mandatory for all retail tickets | **Open** (conditional fields) |

---

## 4. What should not start yet?

- Crystal Reports / CRYSTAL-M1  
- PDF/printer productization  
- Official receipt UI for customers  
- Licensing / cloud / Vercel  
- Sandbox promotion of “official mode”  
- Claims of BIR/RMO readiness or official receipts  
- Live checkout mutation to official path without M3 contracts + review  

---

## 5. Decision table (exact style)

| Decision | Value |
|----------|--------|
| Implementation can start | **yes** (narrow M3 contracts/readiness only) |
| First implementation lane | **RMO-BIR-M3 — Official sale data contracts + host official readiness gate** |
| Crystal Reports decision ready | **no** |
| Official output claim allowed | **no** |
| Compliance claim allowed | **no** |

---

## 6. Design spine summary

| Area | Result |
|------|--------|
| Numbering | Dual: operational POS # vs official sequence; no draft allocate; host-only; atomic with post |
| Config | Use/extend `bir_machine_profiles` + sequences; ACCREDITED + enabled gate |
| Contracts | Official document, X, Z, EJ, audit versioned payloads |
| Integrity | Immutable posted; reversal only; EJ in same TX; remote official forbidden |
| Reports | Text/HTML/PDF first; Crystal deferred |

---

## 7. Honest limits

This is **engineering design** grounded in RMO 24-2023 digest features and current schema. It is **not** a BIR accreditation package and **not** a substitute for legal counsel or TWG evaluation.
