# REPORTING-CLOSURE-M6 — INTEGRATION FREEZE CANDIDATE REPORT

Mode: stacked remote authoring only.

No Codex/Sol verification has run on M2, M3, M4, or M5 at the time this report was authored. This report does not claim local verification, green gates, final acceptance, or final reporting closure.

## Remote truth

Repo: `Kirch-Nairu/Salryn`

Base branch: `KIRCH-REPORTING-CLOSURE-M5-CONTROLLED-EXPORT-ARTIFACTS`

Base SHA: `5eb11e40eae12c944fe1cddc162fdf28449f1a1f`

Candidate branch: `KIRCH-REPORTING-CLOSURE-M6-INTEGRATION-FREEZE-CANDIDATE`

Final branch/SHA: confirm by final remote compare after this report commit.

Protected refs expected unchanged:

- `KIRCH-SANDBOX = 1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
- `KIRCH-INSTALLER-M1 = 6b9c46676390b8a86a989eca600cb02e406ce865`

## Changed files

Expected M6 files:

- `src/Salryn.Api.Local/LocalProductionReportEndpoints.cs`
- `tests/Salryn.Tests/NativePdfM6IntegrationFreezeCandidateChecks.cs`
- `tools/Salryn.Tools.CoreSmoke/Program.cs`
- `00-HANDOFF/REPORTING_CLOSURE_M6_DONE_DEFINITION_EVIDENCE_MAP.md`
- `00-HANDOFF/REPORTING_CLOSURE_M6_INTEGRATION_FREEZE_CANDIDATE_REPORT.md`

## Current reporting engine architecture

Reporting closure lane now has the intended architecture:

1. Server receives report filters only.
2. Local API enforces loopback/local operator access.
3. Local API enforces authenticated session.
4. Local API checks report catalog permission.
5. Local API rebuilds the dataset server-side through `PreCrystalReportDatasetService`.
6. Native PDF renderer renders from the server-owned `ProductionReportDatasetEnvelope`.
7. M5 export path writes controlled PDF artifacts and sidecar manifest JSON under the controlled native PDF root.
8. Crystal remains optional parked compatibility only.

## Primary renderer status

The local renderer status endpoint is revised so native PDF is the primary reporting engine.

Endpoint:

- `GET /api/production-reports/renderer/status`

Status shape now includes:

- `reportingEngine = "native-pdf-primary"`
- `primaryRenderer = NativePdfReportRenderer.GetStatus()`
- `primaryCapability = NativePdfReportRenderer.GetCapability()`
- `nativePdf.status`
- `nativePdf.capability`
- `allowedOutputKinds = ProductionReportOutputKinds.All`

The legacy `nativePdf` block remains as an alias for safer consumer compatibility.

## Crystal parked status

Crystal is not activated.

The status response also includes:

- `crystalCompatibility`
- `crystalCompatibility.status`
- `crystalCompatibility.capability`
- `crystalCompatibility.workerProtocol`
- `crystalCompatibility.deploymentMode`
- `crystalCompatibility.mode = "optional-compatibility-parked"`
- `crystalCompatibility.disabledByDefault = true`

The legacy `crystal` block remains as an alias for safer consumer compatibility.

Crystal remains:

- unavailable
- fail-closed
- optional compatibility only
- isolated-worker only
- not the primary renderer
- disabled by default

## Export/artifact status

M5 export/artifact layer remains the intended controlled path:

- `POST /api/production-reports/export`
- accepts filters only
- rebuilds dataset server-side
- uses `NativePdfReportExportService`
- writes controlled PDF artifact
- writes sidecar manifest JSON
- returns manifest metadata only
- does not expose arbitrary download-by-path
- does not accept caller output root/path
- does not accept template path
- does not accept SQL
- does not print

## Endpoint boundary status

M6 adds `NativePdfM6IntegrationFreezeCandidateChecks`.

The check asserts:

- export request contract accepts filters only
- export request contract has no `OutputRoot`
- export request contract has no `OutputPath`
- export request contract has no `ArtifactPath`
- export request contract has no `TemplatePath`
- export request contract has no `Sql` or `SqlText`
- export request contract has no `Printer` or `PrinterName`
- render/export path is server-owned
- native PDF is primary
- Crystal remains parked
- production eSales transmission remains false/unclaimed
- BIR accreditation remains false/unclaimed
- no printer support is claimed
- no external runtime is required for native PDF primary path

## Done-definition coverage summary

The done-definition evidence map was added at:

`00-HANDOFF/REPORTING_CLOSURE_M6_DONE_DEFINITION_EVIDENCE_MAP.md`

It maps these items to branch/file/test evidence:

- primary free/native PDF engine selected
- PDFsharp/MigraDoc package probe
- native PDF renderer core
- all six core report families proved
- controlled artifact/export path
- controlled root policy
- manifest JSON
- PDF SHA-256
- dataset `ContentSha256` and `DatasetSha256` preserved
- safe filenames
- no caller output path
- no arbitrary templates
- no arbitrary SQL
- permission-gated export endpoint
- range/row caps preserved
- Crystal parked optional only
- no printer claim
- no installer claim
- no BIR accreditation claim
- no production eSales transmission claim

## What remains for M7

M7 should be final quality pass only after Sol verifies M2-M6.

Expected M7 work:

1. Repair concrete compile/gate failures only.
2. Re-run focused gates.
3. Re-run full CoreSmoke.
4. Re-run web build.
5. Confirm no forbidden scope was introduced.
6. Produce final reporting closure/freeze report only if gates pass.

M7 must not add new reporting features.

## Known unverified items

Not verified by Freehand:

- `dotnet restore Salryn.sln`
- `dotnet build Salryn.sln`
- M2 focused gate
- M3 focused gate
- M4 focused gate
- M5 focused gate
- M6 focused gate
- Crystal M1 focused gate
- PRE-CRYSTAL M0 focused gate
- full CoreSmoke
- web build
- endpoint runtime behavior
- local artifact write behavior
- manifest JSON round trip
- PDF byte generation on the target machine
- package restore compatibility on the target machine

## Explicit non-claims

M6 does not claim:

- Sol/local verification
- green gates
- final reporting closure
- final reporting freeze
- BIR accreditation
- production eSales transmission
- legal/licensing approval
- Crystal runtime proof
- Crystal compatibility activation
- printer support
- installer readiness
- UI preview/download

## Sol verification commands

```powershell
git fetch origin

git checkout KIRCH-REPORTING-CLOSURE-M6-INTEGRATION-FREEZE-CANDIDATE
git pull --ff-only origin KIRCH-REPORTING-CLOSURE-M6-INTEGRATION-FREEZE-CANDIDATE

git rev-parse HEAD

git diff --stat 5eb11e40eae12c944fe1cddc162fdf28449f1a1f..HEAD

dotnet restore Salryn.sln
dotnet build Salryn.sln

dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only reports-native-pdf-m2-package-probe
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only reports-native-pdf-m3-renderer-core
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only reports-native-pdf-m4-core-report-family-proof
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only reports-native-pdf-m5-artifact-export-hardening
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only reports-native-pdf-m6-integration-freeze-candidate
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only reports-crystal-m1-runtime-decision-minimal-proof
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only reports-precrystal-m0-full-reporting-spine
dotnet run --project tools/Salryn.Tools.CoreSmoke

npm --prefix src/Salryn.App.Web run build
```

## Stop/repair rules for Sol

Repair only concrete failures.

Do not add:

- printer logic
- installer packaging
- Crystal runtime activation
- SAP packages
- Crystal binaries
- `.rpt` files
- external templates
- arbitrary download-by-path
- UI preview/download
- caller-supplied output paths
- arbitrary SQL
- arbitrary templates
- RMO/M5 accounting/projection changes
