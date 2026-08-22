# RMO / BIR / Reporting Engine Deep Repo Study

Date: 2026-07-15

Branch: `KIRCH-POS-SURFACE3-LAN-COMING-SOON-RMO-REPORTING-STUDY`

Base SHA: `b21fe40cc6437200a853cf5bcd44edbea209da6c`

Mode: repo-backed study. No RMO/BIR/reporting implementation was performed in this lane.

Status: `READY_FOR_SOL_VERIFICATION`

---

## Executive finding

The repo contains several reporting layers:

1. **Owner Reports**: operational store reports computed from live SQLite sales/product data. Internally usable.
2. **Official Setup / official-output foundation**: host-local setup, readiness, numbering preview, integrity/spine status, dry-run packages, source-sale read-only adapters, and internal previews. This is foundation/internal lab work, not official issuance.
3. **Traditional Reporting**: host-only X/Z/EJ/eSales draft endpoints and UI. This is lifecycle-aware and guarded, but claim-risky before compliance validation.
4. **Production Reports / report artifacts**: native-PDF-primary artifact pipeline with Crystal parked/unavailable. It can generate datasets/artifacts through guarded local APIs, but the label is risky and should not imply production/legal readiness.

Current claim boundary:

- `OWNER_REPORTS_READY_INTERNAL`
- `OFFICIAL_REPORTING_FOUNDATION_ONLY`
- `ARTIFACT_EXPORT_FOUNDATION_ONLY`
- `PRODUCTION_TRANSMISSION_NOT_READY`
- `BIR_COMPLIANCE_NOT_CLAIMABLE`

---

## Source inventory reviewed

### Frontend surfaces

- `src/Salryn.App.Web/src/navigation.ts`
- `src/Salryn.App.Web/src/security/permissions.ts`
- `src/Salryn.App.Web/src/App.tsx`
- `src/Salryn.App.Web/src/components/AppShell.tsx`
- `src/Salryn.App.Web/src/pages/DashboardPage.tsx`
- `src/Salryn.App.Web/src/pages/OwnerReportsPage.tsx`
- `src/Salryn.App.Web/src/pages/HostLanControlPage.tsx`
- `src/Salryn.App.Web/src/pages/OfficialSetupPage.tsx`
- `src/Salryn.App.Web/src/pages/TraditionalReportingPage.tsx`
- `src/Salryn.App.Web/src/pages/ProductionReportsPage.tsx`
- `src/Salryn.App.Web/src/api/ownerReports.ts`
- `src/Salryn.App.Web/src/api/officialSetup.ts`
- `src/Salryn.App.Web/src/api/traditionalReporting.ts`
- `src/Salryn.App.Web/src/api/productionReports.ts`

### Backend/API surfaces

- `src/Salryn.Api.Local/LocalOwnerReportsEndpoints.cs`
- `src/Salryn.Api.Local/Services/OwnerReportsApiService.cs`
- `src/Salryn.Api.Local/LocalOfficialSetupEndpoints.cs`
- `src/Salryn.Api.Local/LocalOfficialReadinessEndpoints.cs`
- `src/Salryn.Api.Local/LocalOfficialNumberingEndpoints.cs`
- `src/Salryn.Api.Local/LocalOfficialIntegrityEndpoints.cs`
- `src/Salryn.Api.Local/LocalOfficialSpineEndpoints.cs`
- `src/Salryn.Api.Local/LocalOfficialDryRunPackageEndpoints.cs`
- `src/Salryn.Api.Local/LocalOfficialTraditionalReportingEndpoints.cs`
- `src/Salryn.Api.Local/LocalProductionReportEndpoints.cs`

### Inferred service classes from endpoint composition

- `OwnerReportsApiService`
- `SqliteHostOfficialSetupService`
- `HostOfficialReadinessService`
- `SqliteOfficialCheckoutReadinessService`
- `SqliteHostOfficialNumberingService`
- `SqliteHostOfficialIntegrityService`
- `SqliteHostOfficialSpineStatusService`
- `SqliteHostOfficialDryRunPostingPlanner`
- `SqliteHostOfficialDryRunPackageService`
- `SqliteHostOfficialSourceSaleReadOnlyAdapter`
- `HostOfficialReportPreviewService`
- `OfficialInternalSalesSummaryPreviewRenderer`
- `HostTraditionalReportingService`
- `SqliteOfficialReportingProjectionService`
- `PreCrystalReportDatasetService`
- `UnavailableCrystalReportRenderer`
- `NativePdfReportRenderer`
- `NativePdfReportExportService`
- `NativePdfArtifactStore`
- `NativePdfArtifactRootPolicy`

---

## Advanced tab full map

### 1. Host LAN Control

Page label: `Host LAN Control`

Route/page id: `host-lan-control`

Component file: `src/Salryn.App.Web/src/pages/HostLanControlPage.tsx`

API/client files: `src/Salryn.App.Web/src/hooks/useLocalLanControl.ts`, `src/Salryn.App.Web/src/api/remoteReadOnlyClient.ts`, `src/Salryn.App.Web/src/types/remoteReadOnlyClient.ts`

Permission required: `StoreTerminal.View`, `StoreTerminal.Manage`, `StoreDeployment.Manage` style permission group from page gating.

Role access:

- Admin: visible if permissions present.
- Manager: not in manager allowlist.
- Cashier: no.

Current wording:

- Host-local controls for LAN connection, pairing, and terminal trust.
- Remote clients cannot open this page.
- Status cards include LAN connection, Remote POS runtime, pending requests, visible terminals.

Risky wording:

- `Ready` and `Remote POS runtime` can look more production-ready than intended.
- Pairing controls are real admin operations, so the page should be internal/admin only until release posture is clear.

Mutation risk:

- Yes. It can enable/disable transport/runtime, approve/reject pairing, rotate join code, revoke/disable terminals, and clear sessions.

Data source:

- Host LAN status/pairing runtime APIs.

Output/export risk:

- No file export. It can expose client URL/join code and terminal information.

Client-facing status:

- Not client-facing. Host/admin only.

Production-readiness status:

- Internal preview / coming soon after Surface3 copy change.

Recommended treatment:

- `KEEP_VISIBLE_BUT_COMING_SOON`
- Keep permission-gated.
- Do not expose in remote client flows.
- Do not call LAN production-ready until entitlement/licensing/client acceptance is done.

---

### 2. Official Setup

Page label: `Official Setup`

Route/page id: `official-setup`

Component file: `src/Salryn.App.Web/src/pages/OfficialSetupPage.tsx`

API client file: `src/Salryn.App.Web/src/api/officialSetup.ts`

Backend endpoints:

- `GET /api/official/setup`
- `PUT /api/official/setup/business`
- `PUT /api/official/setup/machine`
- `PUT /api/official/setup/receipt`
- `PUT /api/official/setup/series`
- `GET /api/official/readiness`
- `GET /api/official/numbering/preview`
- `POST /api/official/numbering/validate`
- `GET /api/official/integrity/status`
- `GET /api/official/spine/status`
- `POST /api/official/dry-run/posting-plan`
- `GET /api/official/dry-run/packages`
- `POST /api/official/dry-run/packages`
- `POST /api/official/dry-run/packages/from-sale`
- `GET /api/official/source-sale/{saleId}/dry-run-readonly`
- `GET /api/official/internal-reports/{packageId}`
- `POST /api/official/internal-reports/preview`
- `POST /api/official/dry-run/report-preview`
- `POST /api/official/invoice-output/generate`
- host-local invoice output HTML/thermal/print-view links from UI
- `GET /api/official/live-posting-candidate/status`

Permission required:

- Frontend route: `Settings.Manage`.
- Backend endpoints inspected mostly require loopback and authenticated session. The frontend route is the main permission boundary.

Role access:

- Admin: visible if `Settings.Manage`.
- Manager: no by current manager allowlist.
- Cashier: no.

Current wording:

- Internal configuration for future official output.
- Not compliance; not live official posting.
- Official output is not implemented.
- Does not make Salryn BIR-compliant.
- Live posting blocked.
- Internal dry-run only.
- Not an official receipt or invoice.

Risky wording:

- `Official Setup`
- `Official numbering preview`
- `Official integrity status`
- `Official spine status`
- `VAT / Non-VAT invoice projection`
- `Official dry-run planner`
- `Internal official package`
- accreditation/PTU/MIN fields
- `Live posting candidate`

Mutation risk:

- Yes for setup rows and dry-run package rows. The dry-run and source-sale adapter paths explicitly avoid sale/sequence/EJ/live mutation.

Data source:

- Official setup tables, official readiness services, dry-run package storage, sales source-sale read-only adapter, electronic journal/projection services for preview.

Output/export risk:

- Internal reports and invoice projection can produce/download HTML/thermal text/print-view style output. This is a claim risk even with disclaimers.

Client-facing status:

- Not client-facing. Admin/compliance/internal only.

Production-readiness status:

- `COMPLIANCE_ONLY_FUTURE` / `DEV_ONLY` for standard release.

Recommended treatment:

- `HIDE_FROM_STANDARD_CLIENT_BUILD`
- `COMPLIANCE_ONLY_FUTURE`
- Rename later to `Internal Compliance Setup` or `Official Output Lab` if kept visible internally.

---

### 3. Traditional Reporting

Page label: `Traditional Reporting`

Route/page id: `traditional-reporting`

Component file: `src/Salryn.App.Web/src/pages/TraditionalReportingPage.tsx`

API client file: `src/Salryn.App.Web/src/api/traditionalReporting.ts`

Backend endpoint file: `src/Salryn.Api.Local/LocalOfficialTraditionalReportingEndpoints.cs`

Backend endpoints:

- `GET /api/official/traditional-reporting/status`
- `POST /api/official/traditional-reporting/x-reading`
- `POST /api/official/traditional-reporting/z-reading/finalize`
- `POST /api/official/traditional-reporting/ej-export`
- `POST /api/official/traditional-reporting/esales-draft`
- `GET /api/official/traditional-reporting/grand-total`

Permission required:

- Frontend route: `Reports.View`, `OfficialReading.View`, `OfficialEvidence.Export`.
- Backend: host loopback, authenticated session, then service logic.

Role access:

- Admin: visible if permissions present.
- Manager: allowed by role allowlist if permissions present.
- Cashier: no.

Current wording:

- Host-only X/Z/EJ/eSales draft.
- Lifecycle-aware.
- Does not submit to BIR.
- Activation OFF.
- Manual acceptance pending.

Risky wording:

- `Traditional official reporting`
- `Finalize Z-reading`
- `Export EJ`
- `Export eSales draft`
- `OFFICIAL_INVOICE`

Mutation risk:

- X-reading preview: read/projection.
- Z-reading finalize: yes, by name and endpoint it finalizes a Z-reading and requires actor id. Treat as production-sensitive even if host-only.
- EJ/eSales draft exports: produces export package content but does not transmit.

Data source:

- `SqliteOfficialReportingProjectionService`, official/electronic-journal projection data.

Output/export risk:

- Yes. EJ and eSales draft return UTF-8 content and SHA/control totals.

Client-facing status:

- Not client-facing.

Production-readiness status:

- `COMPLIANCE_ONLY_FUTURE` / `BLOCKER_BEFORE_BETA` if visible without internal-only framing.

Recommended treatment:

- `HIDE_FROM_STANDARD_CLIENT_BUILD`
- Rename later to `Internal Traditional Reporting Drafts` or `Compliance Draft Reports`.
- Keep strong permission gating.

---

### 4. Production Reports

Page label: `Production Reports`

Route/page id: `production-reports`

Component file: `src/Salryn.App.Web/src/pages/ProductionReportsPage.tsx`

API client file: `src/Salryn.App.Web/src/api/productionReports.ts`

Backend endpoint file: `src/Salryn.Api.Local/LocalProductionReportEndpoints.cs`

Backend endpoints:

- `GET /api/production-reports/catalog`
- `GET /api/production-reports/renderer/status`
- `POST /api/production-reports/dataset`
- `POST /api/production-reports/render`
- `POST /api/production-reports/export`

Permission required:

- `Reports.View` or report catalog required permission.
- Backend checks required permission per report family.

Role access:

- Admin: visible if permissions present.
- Manager: allowed by role allowlist if permissions present.
- Cashier: no.

Current wording:

- Native PDF export readiness for host-local operators.
- Controlled artifacts only.
- No Crystal activation, printer path, or caller-selected output directory.
- Does not enable BIR approval, eSales production transmission, or legal production submission.

Risky wording:

- `Production Reports`
- `Native PDF primary`
- `Native PDF export completed`

Mutation risk:

- Dataset generation is read/projection.
- Export writes controlled local artifacts and manifest metadata.

Data source:

- `PreCrystalReportDatasetService` backed by `SqliteOfficialReportingProjectionService`.

Output/export risk:

- Yes. Native PDF export writes controlled local artifact/manifest data.

Client-facing status:

- Not client-facing.

Production-readiness status:

- `ARTIFACT_EXPORT_FOUNDATION_ONLY`.

Recommended treatment:

- `RENAME_ONLY` or `HIDE_FROM_STANDARD_CLIENT_BUILD` depending beta posture.
- Rename later to `Report Artifacts` or `Internal Report Exports`.

---

## Reporting engine explanation: what it is

In this repo, the reporting engine is not one single library. It is a layered pipeline:

1. **Operational reporting layer**: Owner Reports calculate live summaries from operational tables.
2. **Official projection layer**: official/traditional reporting services project report-shaped data from official/electronic-journal structures and sale snapshots.
3. **Dataset layer**: production report dataset service builds allow-listed report datasets.
4. **Renderer/export layer**: native PDF renderer/export service writes controlled local artifacts and manifests; Crystal is parked/unavailable.
5. **UI layer**: Dashboard, Owner Reports, Traditional Reporting, Production Reports, and Official Setup expose different parts of that pipeline.

---

## Data sources

### Owner Reports

Uses live SQLite tables:

- `sales`
- `sale_items`
- `users`
- `products`
- `categories`
- `units`

Computed metrics:

- daily/weekly/monthly sales counts and amounts
- void counts and amounts
- top products
- cashier totals
- category totals
- low stock
- inventory value

Boundary:

- Operational only; not official/BIR/RMO report.

### Traditional / official reports

Uses official reporting projection services and electronic-journal-related structures.

Terms and data involved:

- official invoice / official receipt document kind
- BIR machine profile ID
- registration scope
- X-reading
- Z-reading
- EJ export
- eSales draft
- grand total snapshot
- dry-run package
- invoice projection
- official numbering preview
- official setup/business/machine/receipt/series profile tables

Boundary:

- Host-local/internal/dry-run/foundation unless separately proven.

### Production Reports

Uses:

- `PreCrystalReportCatalog`
- `PreCrystalReportDatasetService`
- `SqliteOfficialReportingProjectionService`
- `NativePdfReportRenderer`
- `NativePdfReportExportService`
- `NativePdfArtifactStore`
- `NativePdfArtifactRootPolicy`

Boundary:

- Native PDF artifacts are controlled local report outputs, not legal production submission and not external transmission.

---

## Read models / DTOs observed

### Owner Reports

- `OwnerReportsResponse`
- `OwnerSalesPeriodSummary`
- `OwnerTopProductRow`
- `OwnerCashierTotalRow`
- `OwnerCategoryTotalRow`
- `OwnerLowStockRow`
- `OwnerInventoryValueSummary`

### Official setup / readiness / numbering / integrity / spine

- `OfficialSetupSnapshotContract`
- `OfficialReadinessSnapshot`
- `OfficialNumberingPreviewContract`
- `OfficialNumberingValidationResult`
- `OfficialIntegrityStatusContract`
- `OfficialSpineStatusContract`
- `OfficialDryRunPostingPlan`
- `OfficialDryRunPackageContract`
- `OfficialReportPreviewPackageContract`
- `OfficialInvoiceOutputPackage`

### Traditional / production reports

- `TraditionalXReadingView`
- `TraditionalZReadingView`
- `TraditionalReportingExportPackage`
- `TraditionalGrandTotalSnapshot`
- `ProductionReportCatalogItem`
- `ProductionReportDatasetRequest`
- `ProductionReportDatasetEnvelope`
- `ProductionReportRenderResult`
- `ProductionReportArtifactExportResult`
- `ProductionReportArtifactManifest`

---

## Repository/service map

### Owner Reports service

`OwnerReportsApiService` computes live operational report summaries directly from SQLite. It is not snapshot-based and does not write report artifacts.

Status: internally usable.

Risk: low, because copy says operational-only and endpoint requires `Reports.View`.

### Official setup services

Setup endpoints can save business, machine, receipt, and series configuration rows. Responses force `liveOfficialPostingAllowed = false` and include no-compliance notes.

Status: foundation only.

Risk: medium/high because page label and fields look compliance-oriented.

### Official readiness/numbering/integrity/spine

Readiness, numbering preview, integrity, and spine endpoints mostly return read-only status. They repeatedly force live posting / checkout allocation / compliance claim to false.

Status: foundation/readiness visibility only.

Risk: medium because terms are official/BIR-heavy.

### Dry-run packages and internal reports

Dry-run package endpoints can create internal dry-run package rows and render internal preview strings. They explicitly mark non-official, no sequence mutation, no sale mutation, no EJ live append, no official receipt issued, no compliance claim.

Status: internal lab.

Risk: high if exposed to normal client users because the artifacts contain receipt/invoice/report language.

### Traditional reporting service

Traditional reporting endpoints expose X-reading preview, Z-reading finalize, EJ export, eSales draft, and grand total. They are host-only and authenticated. They do not submit to BIR, but Z finalize is still a mutation-like operation and eSales/EJ exports are output-generating.

Status: compliance/internal future.

Risk: high before explicit compliance acceptance.

### Production report service

Production report endpoints expose catalog, renderer status, dataset generation, render, and export. Crystal is unavailable/fail-closed. Native PDF is primary and writes controlled local artifacts/manifests.

Status: artifact-export foundation.

Risk: medium because `Production Reports` naming implies more readiness than the code claims.

---

## Endpoint map

| Method | Path | UI consumer | Guard / permission | Mutation | Export/file | External send | Status |
|---|---|---|---|---:|---:|---:|---|
| GET | `/api/owner-reports/summary` | Dashboard, Owner Reports | `Reports.View` | No | No | No | internal operational ready |
| GET | `/api/official/setup` | Official Setup | loopback + auth | No | No | No | foundation |
| PUT | `/api/official/setup/business` | Official Setup | loopback + auth | Yes | No | No | setup metadata |
| PUT | `/api/official/setup/machine` | Official Setup | loopback + auth | Yes | No | No | setup metadata |
| PUT | `/api/official/setup/receipt` | Official Setup | loopback + auth | Yes | No | No | setup metadata |
| PUT | `/api/official/setup/series` | Official Setup | loopback + auth | Yes | No | No | dry-run/setup only |
| GET | `/api/official/readiness` | Official Setup | loopback + auth | No | No | No | readiness only |
| GET | `/api/official/numbering/preview` | Official Setup | loopback + auth | No | No | No | preview only |
| POST | `/api/official/numbering/validate` | Official Setup | loopback + auth | No | No | No | validation only |
| GET | `/api/official/integrity/status` | Official Setup | loopback + auth | No | No | No | status only |
| GET | `/api/official/spine/status` | Official Setup | loopback + auth | No | No | No | status only |
| POST | `/api/official/dry-run/posting-plan` | Official Setup | loopback + auth | No | No | No | dry-run only |
| GET | `/api/official/dry-run/packages` | Official Setup | loopback + auth | No | No | No | list dry-run packages |
| POST | `/api/official/dry-run/packages` | Official Setup | loopback + auth | Yes | internal preview stored | No | internal package |
| POST | `/api/official/dry-run/packages/from-sale` | Official Setup | loopback + auth | Yes package row only | internal preview stored | No | source sale read-only |
| GET | `/api/official/source-sale/{saleId}/dry-run-readonly` | Official Setup | loopback + auth | No | No | No | source sale probe |
| GET | `/api/official/internal-reports/{packageId}` | Official Setup | loopback + auth | No | Preview text | No | internal report preview |
| POST | `/api/official/internal-reports/preview` | Official Setup | loopback + auth | No | Preview text | No | internal report preview |
| POST | `/api/official/dry-run/report-preview` | Official Setup | loopback + auth | No | Preview text | No | internal report preview |
| POST | `/api/official/traditional-reporting/x-reading` | Traditional Reporting | loopback + auth | No | Preview | No | internal/compliance future |
| POST | `/api/official/traditional-reporting/z-reading/finalize` | Traditional Reporting | loopback + auth | Yes | View/result | No | high risk |
| POST | `/api/official/traditional-reporting/ej-export` | Traditional Reporting | loopback + auth | No | UTF-8 export package | No | draft/export only |
| POST | `/api/official/traditional-reporting/esales-draft` | Traditional Reporting | loopback + auth | No | UTF-8 draft | No | not production transmission |
| GET | `/api/official/traditional-reporting/grand-total` | Traditional Reporting | loopback + auth | No | No | No | status/projection |
| GET | `/api/production-reports/catalog` | Production Reports | `Reports.View` | No | No | No | catalog |
| GET | `/api/production-reports/renderer/status` | Production Reports | `Reports.View` | No | No | No | renderer status |
| POST | `/api/production-reports/dataset` | Production Reports | report required permission | No | Dataset response | No | dataset foundation |
| POST | `/api/production-reports/render` | not current main UI | report permission | No | Renderer result | No | Crystal unavailable path |
| POST | `/api/production-reports/export` | Production Reports | report permission | No DB business mutation; writes artifact | PDF + manifest | No | artifact foundation |

---

## What is automated-proven versus not proven

### Automated-proven or partially proven by existing smoke lanes

The repo has many RMO/BIR/official/traditional/native-PDF smoke checks registered in CoreSmoke from prior lanes. Based on endpoint/source inspection, these gates likely prove contracts, schemas, boundaries, and no-claim behavior for their own milestones.

Examples from Program.cs naming include:

- RMO official readiness/setup/numbering/integrity/spine/endgame/full closure/source acquisition/live posting candidate
- Traditional tax invoice, atomic posting, invoice output, output delivery, snapshot capture, document lifecycle, accounting runtime, X/Z/EJ/eSales completion
- PreCrystal and Native PDF M1-M6 reporting lanes

### Not proven by this lane

- Local gates for Surface3 were not run.
- No manual compliance proof was done.
- No BIR acceptance was done.
- No official receipt/invoice production approval was done.
- No eSales production transmission was done.
- No legal/tax validation was done.

---

## Claim risk inventory

| File | Exact/near label | Risk | Why | Recommended copy/treatment |
|---|---|---:|---|---|
| `navigation.ts` | `Official Setup` | HIGH | sounds production official setup | hide or rename `Internal Compliance Setup` |
| `OfficialSetupPage.tsx` | `Official numbering preview` | MEDIUM | official numbering term | keep only under compliance lab |
| `OfficialSetupPage.tsx` | `VAT / Non-VAT invoice projection` | HIGH | invoice projection may look usable | `Internal invoice projection - not legal issuance` |
| `OfficialSetupPage.tsx` | `Download HTML`, `Download thermal text`, `Open print preview` | HIGH | output actions can look legal | keep watermarked/internal and hide in client build |
| `TraditionalReportingPage.tsx` | `Traditional official reporting` | HIGH | official reporting label | `Internal Traditional Reporting Drafts` |
| `TraditionalReportingPage.tsx` | `Finalize Z-reading` | BLOCKER | finalize sounds operational/fiscal | hide or rename to `Internal Z-reading finalize test` |
| `TraditionalReportingPage.tsx` | `Export eSales draft` | HIGH | eSales term is regulated/fiscal | hide unless compliance lane active |
| `ProductionReportsPage.tsx` | `Production Reports` | MEDIUM | sounds release/production ready | rename to `Report Artifacts` |
| `ProductionReportsPage.tsx` | `Native PDF primary` | LOW/MEDIUM | fine technically, risky next to Production label | keep if page renamed |
| `OfficialSetupPage.tsx` | `Accreditation number`, `PTU number`, `MIN` | MEDIUM/HIGH | regulatory identifiers | compliance lab only |

---

## What must be hidden/deferred before beta/client build

Recommended hidden/deferred surfaces for standard client-facing beta:

- Official Setup
- Traditional Reporting
- Production Reports if not renamed to internal artifacts
- any invoice/official output print/download action
- eSales draft/export
- Z-reading finalize
- legal/source closure status
- live posting candidate

Keep visible only to internal/admin/compliance users if needed:

- Host LAN Control with Coming Soon/internal preview tag
- Production report artifacts if renamed and clearly internal

---

## What is safe to call complete now

Safe:

- Owner Reports internal operational summaries: yes, as internal store reporting.
- Dashboard owner report summary integration: yes, source-reviewed and previous gates passed in prior lane.
- PRINTER1 Windows print wiring: SOL-verified separately at prior PRINTER1 SHA per user-provided truth, then docs-only commit followed.
- Surface3 LAN coming-soon copy: code-written, not locally verified here.

Not safe:

- BIR ready
- official receipt ready
- official invoice ready
- eSales production ready
- compliance guaranteed
- beta ready
- installer ready
- licensing final
- LAN client production ready

---

## Required before any official/BIR/eSales claim

Minimum before claim:

1. Legal/accounting requirements source lock by jurisdiction and taxpayer mode.
2. Formal BIR/RMO requirement matrix mapped to implemented behavior.
3. Official numbering live allocation integrated with checkout only after approval.
4. Electronic journal append live-bound and hash/audit semantics validated.
5. X/Z-reading lifecycle rules reviewed with real store workflows.
6. Void/cancel/refund/reprint official document lifecycle fully validated.
7. eSales schema confirmed against authoritative source and sample files accepted.
8. External transmission path designed, permissioned, logged, and failure-safe.
9. Artifact retention and backup/restore lifecycle validated.
10. Manual QA on real hardware/OS/db backup/restore and operational edge cases.
11. Compliance/legal signoff by Kirch or qualified advisor.

---

## Roadmap after study

### Immediate hygiene

1. `KIRCH-REPAIR-USER-M1-SMOKE-DATA-ISOLATION` push/verify.
2. Dev DB cleanup tool only if explicitly approved.

### Surface cleanup

1. Surface3 LAN coming soon: this branch.
2. Dashboard / Owner Reports naming: future rename `Owner Reports` -> `Detailed Reports`.
3. Advanced claim-risk cleanup: hide/rename Official Setup, Traditional Reporting, Production Reports.

### Compliance study

1. Freeze RMO/BIR source-of-truth matrix.
2. Decide which official surfaces remain internal lab.
3. Decide whether production report artifacts are release surface or internal tool.

### Release blockers

1. Backup UI.
2. LIC-FINAL offline license and LAN entitlements.
3. I1 installer/package proof.
4. Final manual acceptance.
5. Private beta.

Status: `READY_FOR_SOL_VERIFICATION`
