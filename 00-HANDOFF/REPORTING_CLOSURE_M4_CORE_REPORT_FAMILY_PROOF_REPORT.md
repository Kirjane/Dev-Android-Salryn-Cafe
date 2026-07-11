# REPORTING-CLOSURE-M4 — Core Report Family Proof Report

Prepared: 2026-07-11

Repository: `Kirch-Nairu/Salryn`

Base branch: `KIRCH-REPORTING-CLOSURE-M3-NATIVE-PDF-RENDERER-CORE`

Base SHA: `2ee5aa7725eedfc9cbf3e7f5903441e50ad26dc0`

Candidate branch: `KIRCH-REPORTING-CLOSURE-M4-CORE-REPORT-FAMILY-PROOF`

Final SHA: pending final remote compare after this report commit.

## Mode

Stacked remote authoring only.

M2 has not been Codex/Sol verified.
M3 has not been Codex/Sol verified.
M4 is therefore not accepted, not green, and not reporting closure.

This branch intentionally authors the M4 proof lane so Codex/Sol can later pull, restore, build, run focused gates, repair concrete failures, and decide whether M4 is acceptable.

## Decision preserved

Crystal Reports is not Salryn's primary reporting engine.

Salryn's primary reporting engine remains the free native PDF/reporting path started in M2/M3 with `PDFsharp-MigraDoc`.

Crystal remains optional isolated compatibility worker only, disabled by default, and was not modified by this branch.

## Mission

Prove that the native PDF renderer core can handle the major Salryn report families from server-owned canonical dataset envelopes.

M4 intentionally uses `PreCrystalReportDatasetService.BuildDatasetAsync` with a controlled fake `IOfficialReportingProjectionService` so the proof exercises the actual report-family dataset mapping path instead of rendering only hand-built synthetic envelopes.

## Files changed

Added:

- `tests/Salryn.Tests/NativePdfM4CoreReportFamilyProofChecks.cs`
- `00-HANDOFF/REPORTING_CLOSURE_M4_CORE_REPORT_FAMILY_PROOF_REPORT.md`

Modified:

- `tools/Salryn.Tools.CoreSmoke/Program.cs`

## Report families covered

M4 covers the required six report families:

1. `ProductionReportKinds.TraditionalXReading`
2. `ProductionReportKinds.TraditionalZReading`
3. `ProductionReportKinds.EjExport`
4. `ProductionReportKinds.SalesRefundVoidSummary`
5. `ProductionReportKinds.TaxBucketSummary`
6. `ProductionReportKinds.InternalESalesDraft`

## Proof method

`NativePdfM4CoreReportFamilyProofChecks.Run()` does the following:

1. Creates a controlled fake `IOfficialReportingProjectionService`.
2. Feeds it into `PreCrystalReportDatasetService`.
3. Builds each required dataset envelope through `BuildDatasetAsync`.
4. Uses real `ProductionReportKinds` constants.
5. Renders each envelope through `NativePdfReportRenderer`.
6. Calls `NativePdfDocumentBuilder.BuildPdfBytes` directly for each family to check in-memory PDF bytes.
7. Asserts the byte array starts with `%PDF`.
8. Asserts renderer artifact metadata is populated.
9. Asserts artifact content type is `application/pdf`.
10. Asserts artifact filename ends with `.pdf` and is only a filename, not a caller path.
11. Asserts byte length is greater than zero.
12. Asserts artifact SHA-256 is populated.
13. Asserts artifact `SourceDatasetSha256` equals dataset `DatasetSha256`.
14. Asserts dataset `ContentSha256` and `DatasetSha256` are populated.
15. Asserts the native PDF source does not use file output APIs, caller paths, Crystal, or `ReportDocument`.

## Controlled projection contents

The fake projection contains:

- two official document rows
- one voided document
- one refund event
- one reprint event
- one adjustment event
- one void event
- non-zero gross posted sales
- non-zero refund amount
- non-zero net sales
- VAT/tax bucket values
- populated `ProjectionContentSha256`
- `AllSnapshotHashesMatch = true`
- `ProductionActivationEnabled = false`
- `BirAuthoritativeESalesSchemaConfirmed = false`
- `TransmissionEnabled = false`

It does not use a real database, network, real filesystem output, templates, caller SQL, or caller paths.

## Per-family assertions

### Traditional X reading

Assertions:

- dataset kind is `traditional-x-reading`
- summary marks read-only / preview boundary
- summary includes no sequence mutation claim
- official document rows exist
- native PDF render succeeds if M2/M3 compile locally
- direct PDF bytes are non-empty and start with `%PDF`
- no Z finalization or mutation is claimed

### Traditional Z reading preview

Assertions:

- dataset kind is `traditional-z-reading`
- summary marks preview boundary
- summary says it does not finalize
- official document rows exist
- native PDF render succeeds if M2/M3 compile locally
- direct PDF bytes are non-empty and start with `%PDF`
- no Z sequence mutation is claimed

### EJ export

Assertions:

- dataset kind is `ej-export`
- dataset has `ej-document` rows
- dataset has `ej-event` rows
- summary states it does not submit to BIR
- native PDF render succeeds if M2/M3 compile locally
- direct PDF bytes are non-empty and start with `%PDF`

### Sales / refund / void summary

Assertions:

- dataset kind is `sales-refund-void-summary`
- dataset has `sales-refund-void-summary` row
- control totals are present
- gross sales, refunded value, and void count are represented
- native PDF render succeeds if M2/M3 compile locally
- direct PDF bytes are non-empty and start with `%PDF`

### Tax bucket summary

Assertions:

- dataset kind is `tax-bucket-summary`
- dataset has `tax-bucket-summary` row
- VAT/tax values are represented
- native PDF render succeeds if M2/M3 compile locally
- direct PDF bytes are non-empty and start with `%PDF`

### Internal eSales draft

Assertions:

- dataset kind is `internal-esales-draft`
- summary marks internal draft
- summary marks unconfirmed schema
- summary marks transmission disabled
- rows mark presentation-only boundary
- `BirESalesSchemaConfirmed = false`
- `ESalesTransmissionEnabled = false`
- `BirAccreditationClaimed = false`
- native PDF render succeeds if M2/M3 compile locally
- direct PDF bytes are non-empty and start with `%PDF`

## CoreSmoke gate

Focused gate added:

```powershell
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only reports-native-pdf-m4-core-report-family-proof
```

Full CoreSmoke now invokes:

```csharp
NativePdfM4CoreReportFamilyProofChecks.Run();
```

## Safety boundaries preserved

M4 does not add or modify:

- production export endpoint
- local API render replacement
- UI preview/download
- persistent artifact storage
- printer logic
- installer packaging
- Crystal code
- SAP packages
- Crystal binaries
- `.rpt` files
- external templates
- arbitrary SQL
- arbitrary template upload
- caller filesystem paths
- RMO/M5 accounting logic
- RMO/M5 projection logic
- report dataset semantics outside test-only proof code

## What was intentionally not implemented

M4 intentionally does not implement:

- export/download endpoint
- artifact persistence
- report browser preview UI
- print path
- installer packaging
- final reporting freeze
- licensing readiness
- BIR accreditation evidence
- production eSales transmission
- visual polish for every report
- every future analytics report

M5 owns artifact/export/preview hardening.
M6 owns final QA and reporting freeze.

## Known unverified items

Freehand did not run local commands.

Unverified:

- `dotnet restore Salryn.sln`
- `dotnet build Salryn.sln`
- M2 focused gate
- M3 focused gate
- M4 focused gate
- Crystal M1 focused gate
- PRE-CRYSTAL M0 focused gate
- full CoreSmoke
- web build
- real PDF byte generation on the local machine
- whether M2/M3 compile without API adjustments
- whether M4 fake projection constructor signatures need local repair

No green gates are claimed.
No final acceptance is claimed.
No reporting closure is claimed.

## Required Codex/Sol verification commands

```powershell
git fetch origin

git checkout KIRCH-REPORTING-CLOSURE-M4-CORE-REPORT-FAMILY-PROOF
git pull --ff-only origin KIRCH-REPORTING-CLOSURE-M4-CORE-REPORT-FAMILY-PROOF

git rev-parse HEAD

git diff --stat 2ee5aa7725eedfc9cbf3e7f5903441e50ad26dc0..HEAD

dotnet restore Salryn.sln
dotnet build Salryn.sln

dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only reports-native-pdf-m2-package-probe
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only reports-native-pdf-m3-renderer-core
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only reports-native-pdf-m4-core-report-family-proof
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only reports-crystal-m1-runtime-decision-minimal-proof
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only reports-precrystal-m0-full-reporting-spine
dotnet run --project tools/Salryn.Tools.CoreSmoke

npm --prefix src/Salryn.App.Web run build
```

## M5 readiness

M5 is not ready until Codex/Sol verifies M2, M3, and M4.

M5 may begin only if:

- restore succeeds
- build succeeds
- M2 package probe gate passes
- M3 native renderer core gate passes
- M4 report family proof gate passes
- Crystal M1 gate still passes
- PRE-CRYSTAL M0 gate still passes
- full CoreSmoke passes
- web build passes
- no unsafe file/output/API/template/storage requirement appears

If M4 fails because the fake projection or PDFsharp/MigraDoc API signature differs from remote-authored assumptions, Codex/Sol should repair only the concrete compile/gate failure and should not expand into M5 behavior.
