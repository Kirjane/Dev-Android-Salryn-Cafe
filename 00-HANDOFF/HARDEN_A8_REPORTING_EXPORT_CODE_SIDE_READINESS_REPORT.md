# HARDEN-A8 — Reporting / Export Code-Side Readiness Report

## 1. Branch / Base

Branch: `KIRCH-HARDEN-A8-REPORTING-EXPORT-CODE-SIDE-READINESS`

Base branch: `KIRCH-HARDEN-A7-FRONTEND-CODE-SIDE-READINESS`

Base SHA: `efa836b41fded78e070d1744afab6711aae455ee`

Protected refs supplied by Kirch and not intentionally touched:

- `KIRCH-SANDBOX = 1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
- `KIRCH-INSTALLER-M1 = 6b9c46676390b8a86a989eca600cb02e406ce865`

Final SHA note: this report cannot contain the SHA of the commit that contains itself. Use the Freehand final response or repository branch head after this report commit for the exact final SHA.

## 2. Scope

Milestone: `HARDEN-A8 — Reporting / Export Code-Side Readiness`

This lane hardens the existing reporting/export frontend and API-client surface before manual proof, installer work, or beta planning. It does not reopen the reporting engine and does not change RMO/BIR/eSales business semantics.

## 3. Files changed

- `00-HANDOFF/HARDEN_A8_REPORTING_EXPORT_CODE_SIDE_READINESS_REPORT.md`
- `src/Salryn.App.Web/src/api/productionReports.ts`
- `src/Salryn.App.Web/src/pages/ProductionReportsPage.tsx`
- `tests/Salryn.Tests/HardenA8ReportingExportCodeSideReadinessChecks.cs`

## 4. Reporting/export surfaces found

Frontend:

- `src/Salryn.App.Web/src/pages/ProductionReportsPage.tsx`
- `src/Salryn.App.Web/src/api/productionReports.ts`
- `src/Salryn.App.Web/src/pages/TraditionalReportingPage.tsx`
- `src/Salryn.App.Web/src/api/traditionalReporting.ts`
- disabled navigation entry for `Official Output Text File`
- enabled navigation entries for `Traditional Reporting` and `Production Reports`
- permission mapping for `production-reports` requiring `Reports.View` or `OfficialReading.View`

Backend/API:

- `GET /api/production-reports/catalog`
- `GET /api/production-reports/renderer/status`
- `POST /api/production-reports/dataset`
- `POST /api/production-reports/render`
- `POST /api/production-reports/export`
- native PDF artifact root policy and artifact store
- Crystal compatibility status remains unavailable/parked/disabled by default

Smoke/tests:

- existing native PDF checks remain present in CoreSmoke history
- A8 adds a compile-included source-boundary check class but does not register a focused CoreSmoke `--only` runner in this connector session

## 5. Reporting/export surfaces updated

Updated `ProductionReportsPage` only.

What changed:

- Applied A7 Salryn Ledger Console UI primitives to the reporting page.
- Added status pills for `Native PDF primary`, `Crystal parked`, and `No printer integration`.
- Added renderer status panel from the existing `GET /api/production-reports/renderer/status` endpoint.
- Replaced ad-hoc catalog buttons with the shared compact `DataTable` primitive.
- Preserved dataset generation through `POST /api/production-reports/dataset`.
- Added native PDF export action through the existing `POST /api/production-reports/export` endpoint.
- Added safe artifact metadata display for manifest fields returned by the existing backend.
- Added clearer loading, success, error, selected-report, and empty states.
- Added explicit frontend copy that no caller-controlled output path or filename field is sent.

Not changed:

- No Traditional Reporting behavior was changed.
- No Official Setup behavior was changed.
- No disabled Official Output Text File navigation behavior was changed.
- No backend reporting engine behavior was changed.

## 6. API clients or routes touched

Touched:

- `src/Salryn.App.Web/src/api/productionReports.ts`

Added client wrappers for existing endpoints:

- `fetchProductionReportRendererStatus()` -> `GET /api/production-reports/renderer/status`
- `exportProductionReportNativePdf()` -> `POST /api/production-reports/export`

The export request is constrained to `requestedOutputKind: "pdf"` and does not expose arbitrary output path, output directory, caller filename, cloud destination, printer target, or template path fields.

Backend routes were inspected but not changed.

## 7. Focused A8 smoke gate status

A focused CoreSmoke gate named `harden-a8-reporting-export-code-side-readiness` was **not registered** in this connector session.

Reason: registering a new `--only` case requires full safe editing of the very large CoreSmoke `Program.cs`; this connector pass avoided a brittle large-file rewrite without local compile feedback.

A compile-included source check class was added:

- `tests/Salryn.Tests/HardenA8ReportingExportCodeSideReadinessChecks.cs`

It checks source boundaries for:

- native PDF primary UI copy
- Crystal parked UI copy
- no printer integration UI copy
- export endpoint client use
- renderer status client use
- PDF-only client request type
- manifest/hash/relative artifact metadata display
- no outputPath/outputDirectory fields in the client
- backend guarded export endpoint markers
- native PDF primary backend status marker
- Crystal disabled-by-default backend marker
- artifact root caller-path/path-traversal protections

This class still needs Sol/Codex/local integration if the maintainer wants it callable from CoreSmoke.

## 8. Native PDF / artifact / manifest / hash readiness

Verified by source inspection and code-side additions:

- Native PDF is the primary label on the frontend.
- Existing export endpoint is called without adding caller-controlled paths.
- Artifact metadata panel displays safe returned fields when available.
- Displayed fields include artifact id, safe file name, report kind, created timestamp, byte length, PDF SHA-256, dataset SHA-256, relative manifest path, relative artifact path, renderer name, and caller-path acceptance state.
- If data is unavailable, the UI shows `Not available` rather than fake metadata.
- The page displays safe names and relative artifact labels rather than adding local full-path selection.

No new hash algorithm, manifest writer, artifact root, renderer, or file-store behavior was added.

## 9. Crystal boundary status

Preserved.

- Crystal remains parked/disabled compatibility.
- No Crystal runtime was enabled.
- No SAP package dependency was added.
- No `.rpt` file or template was added.
- No Crystal rendering workflow was introduced by A8.

## 10. Printer boundary status

Preserved.

- No printer integration added.
- No direct OS print command added.
- No `window.print` UI action added.
- No printer selection UI added.
- No silent print behavior added.

## 11. Path safety status

Preserved and surfaced.

- No output path input added.
- No output directory selector added.
- No caller filename field added.
- No arbitrary SQL field added.
- Existing backend controlled artifact root remains the source of export behavior.
- UI states that no caller-controlled output path or filename field is sent.

## 12. Permission / guard status

Preserved by backend source inspection.

- Production report backend endpoints continue to call `Guard(httpContext, sessions, ...)`.
- Guard still checks loopback/local operator access, authenticated session, and report-specific permission.
- Frontend route permission mapping for `production-reports` remains in `security/permissions.ts` and requires reporting/official-reading permissions.
- A8 does not redesign roles, sessions, permissions, or 401/403 behavior.

Manual HTTP proof is still required for real 401/403 behavior.

## 13. BIR / eSales / legal non-claims

A8 does not claim or implement:

- BIR approval
- legal approval
- production eSales transmission
- official government submission readiness
- production official output readiness
- automatic government upload
- production compliance certification

Traditional Reporting retains its existing explicit non-production wording; A8 did not change it.

## 14. Required report answers

| Question | Answer |
| --- | --- |
| Did you reopen the reporting engine? | No |
| Did you enable Crystal Reports? | No |
| Did you add SAP runtime dependencies? | No |
| Did you add `.rpt` templates? | No |
| Did you add printer integration? | No |
| Did you add arbitrary output path selection? | No |
| Did you add cloud report upload? | No |
| Did you change RMO/BIR/eSales semantics? | No |
| Did you claim production official output readiness? | No |
| Did you update/adapt reporting/export UI surfaces? | Yes: Production Reports page only |
| Did you preserve A7 frontend build stability? | Not locally verified in this connector session |

## 15. Commands / gates run

No local PowerShell commands were run by this ChatGPT connector session.

Required local commands still need Sol/Codex/local verification:

```powershell
cd C:\Users\kirch\Salryn
git fetch origin
git checkout KIRCH-HARDEN-A8-REPORTING-EXPORT-CODE-SIDE-READINESS
git status
git rev-parse HEAD
git rev-parse origin/KIRCH-SANDBOX
git rev-parse origin/KIRCH-INSTALLER-M1

dotnet restore Salryn.sln
dotnet build Salryn.sln
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only licensing-m1-local-license-foundation
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only licensing-m2-signed-license-install-import
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only licensing-m3-local-enforcement-boundaries
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only harden-a1-a6-pre-beta-foundation
dotnet run --project tools/Salryn.Tools.CoreSmoke
npm --prefix src/Salryn.App.Web run build
git diff --check
git status
```

If Sol/Codex registers the A8 source check into CoreSmoke, also run:

```powershell
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only harden-a8-reporting-export-code-side-readiness
```

Connector actions performed:

- Fetched accepted A7 commit `efa836b41fded78e070d1744afab6711aae455ee`.
- Created branch `KIRCH-HARDEN-A8-REPORTING-EXPORT-CODE-SIDE-READINESS` from the accepted A7 SHA.
- Fetched selected frontend, API, backend reporting, UI primitive, and protected-ref commit sources for inspection.
- Updated `productionReports.ts`.
- Updated `ProductionReportsPage.tsx`.
- Added `HardenA8ReportingExportCodeSideReadinessChecks.cs`.
- Added this report.
- Ran connector compare against the accepted A7 base.

## 16. Gate results

Local gates: **not run here**.

Connector compare before this report showed the branch was ahead of A7 with only the reporting API client, Production Reports page, and A8 source-check file changed.

No CI/check status should be treated as local verification unless Sol/Codex or GitHub Actions produces real logs.

## 17. Manual proof still required

Manual proof remains required for:

- actual app launch
- WebView rendering
- opening Production Reports page
- opening Traditional Reporting page after A8
- trigger native PDF export through UI
- verify artifact appears
- verify manifest/hash display with real returned metadata
- verify download/open behavior if later supported
- verify permission-denied behavior through a real session
- verify generated PDF manually
- verify Windows path behavior
- verify controlled artifact directory on a real machine
- verify operator usability
- stale bundle behavior

## 18. Non-claims

This lane does not claim:

- beta readiness
- Cloud readiness
- installer readiness
- production release readiness
- Crystal Reports production readiness
- SAP runtime readiness
- printer readiness
- BIR approval
- legal approval
- eSales production transmission
- official government submission readiness
- manual app launch proof
- local build/test/smoke GREEN from this ChatGPT connector session

## 19. Status

HARDEN-A8 is a connector-authored candidate only.

Sol/Codex/local verification must run before this lane is accepted or called GREEN.
