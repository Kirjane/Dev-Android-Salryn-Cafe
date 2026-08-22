# REPORTING-CLOSURE-M7 — Sol Verification Handoff

## Branch

Repository: `Kirch-Nairu/Salryn`

Branch: `KIRCH-REPORTING-CLOSURE-M7-FREEHAND-QUALITY-PASS`

Base SHA: `5560772ee4bea7397c41d3e024951c1543e33256`

Final SHA: use the exact final SHA from the final Freehand response after this handoff is committed.

## Status

This branch is stacked remote authoring only.

M1 is Codex green.

M2-M7 are not Sol verified yet.

Freehand did not run local restore, build, CoreSmoke, web build, PDF generation, endpoint runtime checks, or artifact writes.

## What this branch is

M7 is the final author-side quality pass before Sol.

It applies only small mechanical fixes and adds handoff documentation.

It does not add reporting features.

## What Sol must verify

Sol must verify the stacked reporting closure lane from M2 through M7:

- M2 package probe
- M3 native PDF renderer core
- M4 core report-family proof
- M5 controlled export/artifact hardening
- M6 integration/freeze candidate checks
- Crystal M1 parked compatibility regression
- PRE-CRYSTAL M0 dataset spine regression
- full CoreSmoke
- web build

## Required commands

```powershell
git fetch origin

git checkout KIRCH-REPORTING-CLOSURE-M7-FREEHAND-QUALITY-PASS
git pull --ff-only origin KIRCH-REPORTING-CLOSURE-M7-FREEHAND-QUALITY-PASS

git rev-parse HEAD
# expected: use the exact final M7 SHA from the final Freehand response

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

## Sol repair rules

Sol may repair only concrete restore/build/CoreSmoke/web-build failures.

Sol must not add:

- new reporting features
- UI preview/download
- printer logic
- installer packaging
- Crystal runtime activation
- SAP packages
- Crystal binaries
- `.rpt` files
- external templates
- arbitrary download-by-path endpoint
- caller-supplied output paths
- arbitrary SQL
- arbitrary templates
- RMO/M5 accounting/projection changes

## Required preservation rules

Preserve native PDF as the primary Salryn reporting engine.

Preserve Crystal as parked optional compatibility only.

Preserve server-owned dataset generation.

Preserve report catalog permissions.

Preserve document-kind allow-list.

Preserve range and row caps.

Preserve dataset `ContentSha256` and `DatasetSha256` handling.

Preserve controlled root policy and sidecar manifest JSON.

Preserve internal eSales draft boundary:

- BIR authoritative schema unconfirmed
- transmission disabled
- BIR accreditation not claimed

## Acceptance rule

If all required Sol gates pass and no forbidden scope entered, Sol may produce the final reporting closure acceptance report.

If any gate fails, Sol should repair only the concrete failure and rerun the affected focused gate plus full CoreSmoke and web build.
