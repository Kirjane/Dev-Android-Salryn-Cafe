# REPORTING-CLOSURE-M7 — Freehand Quality Pass Report

## Remote truth

Repository: `Kirch-Nairu/Salryn`

Base branch: `KIRCH-REPORTING-CLOSURE-M6-INTEGRATION-FREEZE-CANDIDATE`

Base SHA: `5560772ee4bea7397c41d3e024951c1543e33256`

Candidate branch: `KIRCH-REPORTING-CLOSURE-M7-FREEHAND-QUALITY-PASS`

Protected refs expected unchanged:

- `KIRCH-SANDBOX = 1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
- `KIRCH-INSTALLER-M1 = 6b9c46676390b8a86a989eca600cb02e406ce865`

Final SHA: resolved by the final remote branch compare after this report and the Sol handoff are committed.

## Mode

M7 is a Freehand author-side quality pass only.

No local restore, build, CoreSmoke, web build, endpoint run, or PDF generation was performed by Freehand.

M2, M3, M4, M5, M6, and M7 remain unverified until Sol runs the required gates.

## Quality pass scope

Reviewed the stacked reporting closure lane for source-level risks only:

- obvious compile-risk naming mismatches
- focused smoke gate consistency
- stale or misleading status wording
- false-positive test scope after later milestones added artifact storage
- caller path/template/SQL/printer leaks
- unsafe path handling
- Crystal/native renderer identity consistency
- docs or checks that could overclaim verification

## Fixes applied

### 1. Renderer version identity property mismatch

File:

- `src/Salryn.Infrastructure/Reports/NativePdf/NativePdfRendererIdentity.cs`

Issue:

- `NativePdfRendererIdentity.RendererVersion` referenced `package.PackageVersion`.
- `NativePdfPackageProbeResult` exposes `RequestedPackageVersion`.

Fix:

- Changed the version composition to use `package.RequestedPackageVersion`.

Reason:

- Mechanical compile-risk correction.
- No behavior expansion.
- No package/version change.

### 2. Native PDF status wording no longer says M3-only

File:

- `src/Salryn.Infrastructure/Reports/NativePdf/NativePdfReportRenderer.cs`

Issue:

- Renderer status message still said no export endpoint, storage, or report-family proof was claimed in M3.
- By M6, report-family proof and controlled export/artifact layers are present as stacked authored code.

Fix:

- Reworded the status message to state:
  - native PDF renderer is available for in-memory PDF generation,
  - controlled export is handled by M5 export service,
  - no print, installer, UI preview/download, Crystal runtime, or final reporting freeze is claimed.

Reason:

- Reduces misleading status wording without changing capability flags.

### 3. M6 manifest caller-path assertion aligned with actual contract

File:

- `tests/Salryn.Tests/NativePdfM6IntegrationFreezeCandidateChecks.cs`

Issue:

- M6 check searched for `NoCallerPathAccepted`.
- The actual artifact manifest contract field is `CallerPathAccepted`, expected to be `false` in the manifest.

Fix:

- Updated the assertion to require `CallerPathAccepted`.

Reason:

- Mechanical test/source alignment.
- No contract semantics changed.

### 4. M4 no-disk check scoped back to renderer core

File:

- `tests/Salryn.Tests/NativePdfM4CoreReportFamilyProofChecks.cs`

Issue:

- M4 no-disk-output check scanned the whole `NativePdf` folder.
- M5 intentionally added `NativePdfArtifactStore`, which writes controlled artifacts to disk.
- That made the older M4 check a false positive after M5.

Fix:

- Scoped the M4 no-disk-output check to renderer-core files only:
  - `NativePdfReportRenderer.cs`
  - `NativePdfDocumentBuilder.cs`
  - `NativePdfArtifactFactory.cs`
  - `NativePdfRendererIdentity.cs`

Reason:

- Preserves the original M4 rule: renderer core remains in-memory only.
- Does not weaken M5 controlled artifact-store checks.

### 5. Export service constructor arguments normalized

File:

- `src/Salryn.Infrastructure/Reports/NativePdf/NativePdfReportExportService.cs`

Issue:

- Constructor invocation mixed named and positional arguments in a way that could increase compile-review friction.

Fix:

- Normalized to positional arguments for `ProductionReportRenderRequest`.

Reason:

- Mechanical compile-risk reduction.
- No behavior change.

### 6. Controlled root comparison made platform-aware

File:

- `src/Salryn.Infrastructure/Reports/NativePdf/NativePdfArtifactRootPolicy.cs`

Issue:

- Controlled root containment comparison used ordinal string comparison.
- Windows paths are case-insensitive.

Fix:

- Use `StringComparison.OrdinalIgnoreCase` on Windows and `StringComparison.Ordinal` elsewhere.

Reason:

- Safety hardening for path containment without adding new scope.

## No-fix findings

No new product features were added.

No changes were made to:

- package selection
- PDFsharp/MigraDoc version
- report dataset semantics
- PRE-CRYSTAL dataset service behavior
- RMO/M5 accounting/projection logic
- Crystal worker contracts
- installer packaging
- UI
- printer paths

No arbitrary download-by-path endpoint was added.

No caller output path, template path, SQL, or printer fields were added to the local export request contract.

## Scope audit

M2 package probe remains type-load only:

- no rendering
- no file output
- no endpoint
- no UI
- no artifact creation

M3 renderer remains renderer-core only:

- native PDF renderer identity
- generic MigraDoc document builder
- in-memory PDF bytes
- artifact metadata from in-memory bytes
- no disk output in renderer core
- no printer
- no Crystal

M4 remains core report-family proof only:

- six report families through `PreCrystalReportDatasetService`
- fake `IOfficialReportingProjectionService` in tests
- no database, network, filesystem, printer, UI, or installer dependency
- no production eSales transmission claim

M5 remains controlled artifact/export hardening:

- controlled root policy
- sidecar manifest JSON
- PDF SHA-256 verification
- dataset hashes preserved
- permission-gated local export endpoint
- no arbitrary download-by-path endpoint

M6 remains integration/freeze candidate:

- native PDF reported as primary local renderer
- Crystal reported as parked optional compatibility only
- done-definition evidence map added
- final reporting freeze not claimed

## Known unverified items

Freehand did not run:

- `dotnet restore Salryn.sln`
- `dotnet build Salryn.sln`
- any focused CoreSmoke gate
- full CoreSmoke
- web build
- endpoint runtime checks
- local PDF byte generation
- local controlled artifact write
- manifest JSON round trip

M2-M7 require Sol verification.

## Sol verification handoff

Use:

```powershell
git fetch origin

git checkout KIRCH-REPORTING-CLOSURE-M7-FREEHAND-QUALITY-PASS
git pull --ff-only origin KIRCH-REPORTING-CLOSURE-M7-FREEHAND-QUALITY-PASS

git rev-parse HEAD
# expected: use the final SHA reported by the final Freehand response

git diff --stat 5560772ee4bea7397c41d3e024951c1543e33256..HEAD

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

Sol should repair only concrete compile/gate failures. No new reporting features belong in the Sol verification pass.
