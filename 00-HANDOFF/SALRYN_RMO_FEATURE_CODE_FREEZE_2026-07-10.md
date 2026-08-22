# Salryn Traditional CRM/POS — RMO Feature Code Freeze

**Date:** 2026-07-10  
**Freeze tip:** M5-RC2 `KIRCH-RMO-BIR-TRAD-M5-RC2-XZ-EJ-ESALES-COMPLETION` (after push)  
**Base:** M4-RC2 `05af9e22fbfabbdf77b70379323ec68960293782`

## Declaration

**RMO feature coding is FROZEN** after M5-RC2 automated gates pass.

Do **not** invent M6/M7/M8/M9 RMO feature branches for Traditional CRM/POS readiness.

## RMO feature code

| Area | Status |
|------|--------|
| Taxpayer/VAT policy spine (M1) | COMPLETE |
| Atomic official posting foundation (M2) | COMPLETE (activation OFF) |
| Invoice projection / RC1–RC2 (M3) | COMPLETE |
| Lifecycle accounting (M4-RC2) | COMPLETE |
| X/Z/EJ/eSales lifecycle-aware reporting (M5-RC2) | COMPLETE |
| BIR authoritative eSales schema | **UNCONFIRMED** (adapter sealed; transmission OFF) |
| Production official posting | **OFF** |

## Acceptance defects (may reopen code)

Only when a reproducible defect is found during:

- manual UI / print / device acceptance;
- installer execution on a real machine;
- Annex F operator walkthrough;
- Kirch-authorized regression of frozen RMO paths.

## Human / application work (not feature coding)

- Manual invoice/refund/print/device acceptance;
- Installer install/upgrade/uninstall evidence;
- Internal Annex F demonstration;
- Applicant legal identity / SEC-DTI / signatures / notarization;
- eAccReg package assembly and filing **only after Kirch explicit authorization**;
- BIR/TWG evaluation and corrections.

## Next product-engineering lanes (outside this freeze document’s RMO code)

- Crystal Reports / production document presentation;
- Local licensing;
- Salryn Cloud / Vercel licensing;
- Main frontend completion and polish;
- Installer/release engineering;
- Official product release preparation.

## Hard boundaries that remain true

- BIR eSales schema confirmed: **no**
- eSales transmission enabled: **no**
- BIR submitted: **no**
- BIR accredited: **no**
- Sandbox promoted: **no**
- Installer branch modified by RMO program: **no**
- Production activation: **OFF**
