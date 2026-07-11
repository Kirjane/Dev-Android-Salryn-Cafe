# REPORTING-CLOSURE-M2 Free Native PDF Package Probe Report

Prepared: 2026-07-11

Repository: `Kirch-Nairu/Salryn`
Base branch: `KIRCH-REPORTING-CLOSURE-M1-STRATEGY-AND-PHASE-LOCK`
Base SHA: `2a6c4bda19da4495e36143e26a949ae6f4231d4c`
Candidate branch: `KIRCH-REPORTING-CLOSURE-M2-FREE-NATIVE-PDF-PACKAGE-PROBE`
Final remote SHA: record from branch head after this document commit.

## M2 decision

**Accepted for Codex/Sol local verification.**

`PDFsharp-MigraDoc` version `6.2.4` is the first package candidate for Salryn's free native PDF/reporting engine path.

This branch adds only a dependency and a compile/type-load probe. It does not implement the native renderer and does not create PDFs.

## Source facts recorded

Reviewed source facts:

1. Official PDFsharp/MigraDoc documentation
   - URL: `https://docs.pdfsharp.net/`
   - Lists PDFsharp and MigraDoc availability for `.NET 8`, `.NET 9`, `.NET 10`, `.NET Framework 4.6.2`, and `.NET Standard 2.0`.
   - Describes MigraDoc as a document generator using a high-level document object model.
   - Describes PDFsharp as a .NET PDF library.
   - Shows release `6.2.4` dated `2026-01-06` and `7.0.0 Preview 1` as preview.

2. NuGet package page
   - URL: `https://www.nuget.org/packages/PDFsharp-MigraDoc/6.2.4`
   - Package: `PDFsharp-MigraDoc`
   - Version: `6.2.4`
   - PackageReference: `<PackageReference Include="PDFsharp-MigraDoc" Version="6.2.4" />`
   - License: MIT
   - Shows `net10.0` compatible target framework information.
   - States core package does not depend on Windows and can be used on any .NET-compatible platform including Linux and macOS.
   - Shows `7.0.0-preview-1` exists as a newer prerelease, but Salryn M2 intentionally avoids preview dependency.

## Files changed

```text
src/Salryn.Infrastructure/Salryn.Infrastructure.csproj
src/Salryn.Infrastructure/Reports/NativePdf/NativePdfPackageProbe.cs
tests/Salryn.Tests/NativePdfM2PackageProbeChecks.cs
tools/Salryn.Tools.CoreSmoke/Program.cs
00-HANDOFF/REPORTING_CLOSURE_M2_FREE_NATIVE_PDF_PACKAGE_PROBE_REPORT.md
```

## Exact package/version

```xml
<PackageReference Include="PDFsharp-MigraDoc" Version="6.2.4" />
```

Added to:

```text
src/Salryn.Infrastructure/Salryn.Infrastructure.csproj
```

## Probe contents

Created:

```text
src/Salryn.Infrastructure/Reports/NativePdf/NativePdfPackageProbe.cs
```

The probe references these package types:

```text
PdfSharp.Pdf.PdfDocument
MigraDoc.DocumentObjectModel.Document
```

The probe returns:

```text
PackageId
RequestedPackageVersion
PdfSharpTypeName
PdfSharpAssemblyName
PdfSharpAssemblyVersion
MigraDocTypeName
MigraDocAssemblyName
MigraDocAssemblyVersion
RequiresFileOutput
IsRenderer
ClaimsPdfProof
Notes
```

Safety flags returned by the probe:

```text
RequiresFileOutput = false
IsRenderer = false
ClaimsPdfProof = false
```

## Smoke/check coverage

Created:

```text
tests/Salryn.Tests/NativePdfM2PackageProbeChecks.cs
```

The check asserts:

- package reference exists and pins version `6.2.4`
- PDFsharp type is loadable
- MigraDoc type is loadable
- assembly names are non-empty
- assembly versions are non-empty
- probe requires no file output
- probe is not a renderer
- probe does not claim PDF proof
- probe source does not save PDFs
- probe source does not open file streams
- probe source does not implement `NativePdfReportRenderer`
- probe source does not consume `ProductionReportDatasetEnvelope`
- focused CoreSmoke gate is wired
- full CoreSmoke invokes the M2 check

## CoreSmoke gate

Focused gate added:

```text
reports-native-pdf-m2-package-probe
```

Required local command:

```powershell
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only reports-native-pdf-m2-package-probe
```

Full CoreSmoke now also calls:

```text
NativePdfM2PackageProbeChecks.Run()
```

## Compatibility note

Package selection is currently favorable because:

- the official docs include `.NET 10`
- NuGet lists `net10.0` compatibility
- the selected core package is not Windows-only
- Salryn's Infrastructure project targets `net10.0`
- this does not require a Crystal runtime, SAP package, `.rpt` file, or Windows-only primary engine dependency

Final acceptance still requires Codex/Sol to run restore/build/CoreSmoke locally.

## License note

The NuGet package page lists MIT license. This is compatible with the intended free/native primary reporting direction at the package-probe stage.

No broader legal/commercial release approval is claimed by this branch.

## Gates

Required gates for Codex/Sol:

```powershell
dotnet restore Salryn.sln
dotnet build Salryn.sln
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only reports-native-pdf-m2-package-probe
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only reports-crystal-m1-runtime-decision-minimal-proof
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only reports-precrystal-m0-full-reporting-spine
dotnet run --project tools/Salryn.Tools.CoreSmoke
npm --prefix src/Salryn.App.Web run build
```

Freehand did not run these gates.

## What was not claimed

This branch does not claim:

- renderer implementation
- PDF proof
- Salryn report PDF generation
- artifact creation
- output storage policy implementation
- endpoint/export behavior
- UI behavior
- installer readiness
- printer readiness
- final reporting-engine closure
- legal/commercial release approval

## Forbidden scope preserved

- No Crystal code.
- No SAP packages.
- No Crystal binaries.
- No `.rpt` files.
- No report templates.
- No native renderer implementation.
- No Salryn report PDFs generated.
- No files written by the probe.
- No artifact storage.
- No export endpoint.
- No UI touch.
- No installer packaging touch.
- No printer logic.
- No RMO/M5 accounting or projection change.
- No existing report dataset semantic change.

## M3 readiness

Move to M3 only after Codex/Sol verifies:

- restore succeeds
- build succeeds
- focused M2 smoke passes
- Crystal M1 and PRE-CRYSTAL M0 focused gates still pass
- full CoreSmoke passes
- web build remains green
- no unexpected runtime/platform/license blocker appears

M3 next phase:

```text
REPORTING-CLOSURE-M3 — Native Renderer Core
```

Expected M3 scope:

- introduce native renderer abstraction/implementation shell
- keep server-owned datasets
- keep permission/range/row caps
- keep output kind PDF-only
- still avoid broad layout expansion until controlled first proof
