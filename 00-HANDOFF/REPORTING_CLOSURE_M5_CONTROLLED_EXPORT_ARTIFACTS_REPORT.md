# REPORTING-CLOSURE-M5 Controlled Export / Artifact Hardening Report

Prepared: 2026-07-11

Repository: `Kirch-Nairu/Salryn`

Base branch: `KIRCH-REPORTING-CLOSURE-M4-CORE-REPORT-FAMILY-PROOF`

Base SHA: `77e7c74317538a218fd85a5503cff7e94734fb71`

Candidate branch: `KIRCH-REPORTING-CLOSURE-M5-CONTROLLED-EXPORT-ARTIFACTS`

Final branch SHA: see final remote compare after this report commit.

## Mode

Stacked remote authoring only.

No Codex/Sol verification has run on M2, M3, or M4 yet. M5 is authored on top of M4 and must not be treated as accepted until Sol pulls the branch and runs the required gates.

## Decision carried forward

Crystal Reports is not Salryn's primary reporting engine.

The primary reporting path is the free/native PDF engine using PDFsharp/MigraDoc from the M2 package probe and M3/M4 native renderer proof chain.

Crystal remains parked as optional isolated compatibility only.

## Changed files

Added:

- `src/Salryn.Core/Services/Reports/ReportArtifactContracts.cs`
- `src/Salryn.Infrastructure/Reports/NativePdf/NativePdfArtifactRootPolicy.cs`
- `src/Salryn.Infrastructure/Reports/NativePdf/NativePdfArtifactStore.cs`
- `src/Salryn.Infrastructure/Reports/NativePdf/NativePdfReportExportService.cs`
- `tests/Salryn.Tests/NativePdfM5ArtifactExportHardeningChecks.cs`
- `00-HANDOFF/REPORTING_CLOSURE_M5_CONTROLLED_EXPORT_ARTIFACTS_REPORT.md`

Modified:

- `src/Salryn.Api.Local/LocalProductionReportEndpoints.cs`
- `src/Salryn.Infrastructure/Reports/NativePdf/NativePdfArtifactFactory.cs`
- `tools/Salryn.Tools.CoreSmoke/Program.cs`

## Controlled export summary

M5 adds a controlled native PDF artifact/export layer around the existing server-owned reporting dataset spine.

The export path is:

```text
Local API filter request
  -> existing permission / loopback / session guard
  -> existing report catalog permission
  -> PreCrystalReportDatasetService.BuildDatasetAsync
  -> NativePdfReportExportService
  -> NativePdfReportRenderer validation/render probe
  -> NativePdfDocumentBuilder.BuildPdfBytes
  -> NativePdfArtifactStore.SaveAsync
  -> PDF artifact under controlled root
  -> sidecar manifest JSON under controlled root
  -> manifest metadata response only
```

The local API export route is:

```text
POST /api/production-reports/export
```

The request accepts report filters only:

- `ReportKind`
- `RangeStart`
- `RangeEnd`
- `BirMachineProfileId`
- `RegistrationScopeCode`
- `DocumentKind`
- `TerminalId`
- `RequestedOutputKind`

It does not accept:

- dataset envelope
- output root
- output file path
- template path
- SQL
- printer settings

## Artifact manifest summary

`ReportArtifactContracts.cs` adds the artifact manifest contract.

The manifest records:

- artifact ID
- report kind
- file name
- content type
- byte length
- PDF SHA-256
- dataset `ContentSha256`
- dataset `DatasetSha256`
- renderer name
- renderer version
- created-at timestamp
- controlled root policy label
- relative artifact path
- relative manifest path
- export scope
- safety flag snapshot
- caller path accepted flag
- retention/cleanup note

The manifest is intended to be the API-facing export result. The endpoint does not expose arbitrary filesystem reads or download-by-path behavior.

## Output root policy

`NativePdfArtifactRootPolicy` owns the controlled root.

Default root:

```text
Environment.SpecialFolder.LocalApplicationData / Salryn / Reports / NativePdf
```

Fallback root if local application data is unavailable:

```text
AppContext.BaseDirectory / Salryn / Reports / NativePdf
```

Policy rules:

- root is selected by code/config, not by HTTP body
- tests may use `CreateForTesting(root)`
- relative artifact paths are generated internally
- absolute paths are rejected
- `.` / `..` / traversal-like segments are rejected
- resolved paths must remain under the controlled root
- caller-supplied output paths are explicitly rejected

## Store behavior

`NativePdfArtifactStore`:

- writes PDF bytes under the controlled root
- creates folders when needed
- writes sidecar manifest JSON
- computes SHA-256 from PDF bytes
- reads the written PDF back and verifies hash
- round-trips the manifest JSON and verifies hash consistency
- returns `ProductionReportArtifactExportResult`
- does not print
- does not call Crystal
- does not load `.rpt`
- does not execute SQL
- does not use external templates

## Export service behavior

`NativePdfReportExportService`:

- accepts a server-built `ProductionReportDatasetEnvelope`
- validates output kind through `ProductionReportOutputKinds.TryNormalize`
- allows PDF only
- calls `NativePdfReportRenderer` for renderer-level validation/proof
- builds PDF bytes through `NativePdfDocumentBuilder`
- writes through `NativePdfArtifactStore`
- returns manifest metadata
- has no caller output path field
- has no template field
- has no SQL field
- has no printer field

## Permission boundary

The local API export endpoint preserves the existing report endpoint boundary:

- loopback/local operator guard
- authenticated session requirement
- report catalog permission check
- server-owned dataset generation
- report kind allow-list
- document kind allow-list
- range cap
- row cap through `PreCrystalReportDatasetService`
- no caller-supplied dataset envelope
- no caller output path
- no arbitrary templates
- no SQL

## Focused smoke gate

Added:

```text
reports-native-pdf-m5-artifact-export-hardening
```

Full CoreSmoke now calls:

```text
NativePdfM5ArtifactExportHardeningChecks.Run();
```

Focused `--only` now dispatches to the M5 check.

## Test coverage authored

`NativePdfM5ArtifactExportHardeningChecks` is authored to check:

- controlled test root export writes PDF
- sidecar manifest JSON exists
- manifest hash matches actual PDF bytes
- dataset `ContentSha256` is preserved
- dataset `DatasetSha256` is preserved
- safe filename behavior
- path traversal rejection
- caller output path rejection
- no caller output path in export service request contract
- unsupported output kind fails closed
- internal eSales manifest preserves unconfirmed/non-transmitting flags
- no printer support claim
- no Crystal runtime claim
- no arbitrary template path accepted
- no arbitrary SQL accepted
- no arbitrary download-by-path endpoint added

## Safety boundaries

Preserved:

- no Sandbox touch
- no Installer touch
- no installer packaging
- no Crystal code
- no SAP package
- no Crystal binary
- no `.rpt`
- no external report template
- no printer logic
- no arbitrary download-by-path endpoint
- no UI change
- no caller-supplied output paths
- no arbitrary SQL
- no arbitrary templates
- no RMO/M5 accounting rewrite
- no report dataset semantic rewrite
- no BIR accreditation claim
- no production eSales transmission claim
- no legal/licensing approval claim
- no final reporting freeze claim

## What was intentionally not implemented

Not implemented in M5:

- UI preview/download surface
- arbitrary file download by path
- printer integration
- installer packaging
- Crystal compatibility worker activation
- `.rpt` compatibility
- external templates
- SQL report designer
- cleanup scheduler
- retention UI
- final report visual polish
- final reporting freeze

## Known unverified items

Freehand did not run local commands.

Unverified:

- `dotnet restore Salryn.sln`
- `dotnet build Salryn.sln`
- M2 focused gate
- M3 focused gate
- M4 focused gate
- M5 focused gate
- Crystal M1 focused gate
- PRE-CRYSTAL M0 focused gate
- full CoreSmoke
- web build
- actual local artifact file creation
- sidecar manifest JSON round trip
- endpoint compile
- route binding compile
- any platform-specific path behavior

## M6 readiness

M6 is not ready until Sol verifies M2, M3, M4, and M5.

M6 may start only after:

- restore succeeds
- build succeeds
- M2 package probe passes
- M3 renderer core passes
- M4 report family proof passes
- M5 artifact/export hardening passes
- Crystal M1 gate still passes
- PRE-CRYSTAL M0 gate still passes
- full CoreSmoke passes
- web build passes
- Sol confirms no unsafe output, path, template, SQL, printer, installer, Crystal, or UI requirement leaked into M5

## Sol verification commands

```powershell
git fetch origin

git checkout KIRCH-REPORTING-CLOSURE-M5-CONTROLLED-EXPORT-ARTIFACTS
git pull --ff-only origin KIRCH-REPORTING-CLOSURE-M5-CONTROLLED-EXPORT-ARTIFACTS

git rev-parse HEAD

git diff --stat 77e7c74317538a218fd85a5503cff7e94734fb71..HEAD

dotnet restore Salryn.sln
dotnet build Salryn.sln

dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only reports-native-pdf-m2-package-probe
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only reports-native-pdf-m3-renderer-core
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only reports-native-pdf-m4-core-report-family-proof
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only reports-native-pdf-m5-artifact-export-hardening
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only reports-crystal-m1-runtime-decision-minimal-proof
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only reports-precrystal-m0-full-reporting-spine
dotnet run --project tools/Salryn.Tools.CoreSmoke

npm --prefix src/Salryn.App.Web run build
```

## Sol repair policy

Sol should repair only concrete compile/gate failures.

Do not start M6 from this branch until M2-M5 are verified.
