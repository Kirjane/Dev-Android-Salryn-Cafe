# RMO-BIR-M9 GO / STOP Decision

**Date:** 2026-07-10  
**Branch:** `KIRCH-RMO-BIR-M9-FULL-CLOSURE`  
**Inputs:** `RMO_BIR_M9_LEGAL_SOURCE_CLOSURE.md`, `RMO_BIR_M9_CODE_STUDY.md`

**Disclaimer:** Engineering decision only. No compliance claim.

---

## Decisions

| Item | Decision | Rationale |
|------|----------|-----------|
| **A. Legal closure** | **Incomplete** | Annex F text + retention closed enough for planning; validation seal form and eSales class/schema remain open; official output still blocked |
| **B. Source-sale read-only adapter** | **GO** | Safe read path via sale read repository; v14 columns already support source mode; no mutations |
| **C. Guarded official posting service** | **GO (always-blocked candidate only)** | Design + skeleton that returns blocked unless impossible gate path; **no production activation**; legal incomplete → never enables live post |
| **D. Live checkout binding** | **STOP** | Legal incomplete; product spine blockers remain; gates forbid checkout references |
| **E. Internal reports** | **GO** | Expand watermarked JSON/text/HTML-internal previews; no Crystal/PDF/printer |
| **F. Crystal** | **STOP** | Not proven necessary by source or technical evidence |
| **G. Schema** | **No migration** | v14 sufficient for source-sale packages; live posting tables deferred |

---

## Rules applied

- Legal incomplete → **no live official posting implementation**.
- Source-sale read path safe → implement read-only adapter.
- Official posting needs schema + legal closure → **design** + always-blocked candidate only.
- Output requirements clear for field *lists* but production engine blocked → internal previews only.

---

## Implementation scope allowed in this branch

1. Legal/source/GO-STOP/design/QA docs  
2. Source-sale read-only adapter + package integration  
3. Internal report renderers (watermarked)  
4. Always-blocked live posting candidate contracts/service  
5. Host-local API + Official Setup UI sections  
6. Source gates `rmo-bir-full-closure`  
7. Code quality pass (centralize flags/watermarks where safe)

## Explicitly out of scope

- Live checkout binding  
- Sequence mutation during sale  
- Live EJ append on sale  
- Official PDF/printer/Crystal  
- eSales live push  
- Installer / Sandbox / LAN refactors  
- Compliance claims  
