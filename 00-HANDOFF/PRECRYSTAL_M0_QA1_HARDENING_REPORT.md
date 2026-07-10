# PRE-CRYSTAL M0 QA1 Hardening Report

**Branch:** `KIRCH-REPORTS-PRECRYSTAL-M0-QA1-HARDENING`  
**Base branch:** `KIRCH-REPORTS-PRECRYSTAL-M0-FULL-REPORTING-SPINE`  
**Base SHA:** `707df1c7ed651415e3e857eb4a2d8045a0c534e8`  
**Mode:** Code-quality hardening only. No Crystal runtime work.

## Decision

GO — QA1 hardening only.

This pass preserves the PRE-CRYSTAL architecture and hardens it before any Crystal runtime/worker integration.

## Changes

1. Render endpoint no longer accepts a caller-supplied `ProductionReportDatasetEnvelope`.
   - The endpoint now accepts the same allow-listed filter request used by dataset generation.
   - The server rebuilds the dataset through `PreCrystalReportDatasetService`.
   - The unavailable Crystal adapter still fails closed.

2. Invalid machine filters now fail closed.
   - Non-empty malformed `BirMachineProfileId` returns `Reports.InvalidMachineProfileId`.
   - Empty/omitted machine filter remains allowed.

3. Unknown document kinds now fail closed.
   - Allowed values: `OFFICIAL_INVOICE`, `OFFICIAL_RECEIPT`.
   - Empty/omitted value defaults to `OFFICIAL_INVOICE`.
   - Any other non-empty value returns `Reports.InvalidDocumentKind`.

4. Range and row caps were added.
   - Max range: 31 days.
   - Max rows: 5000 rows.
   - Oversized projections return `Reports.RowLimitExceeded`.

5. Dataset hash semantics were split.
   - `ContentSha256`: stable content/source hash that excludes runtime `GeneratedAt`.
   - `DatasetSha256`: runtime envelope hash that includes generated-at metadata.
   - Future renderer/worker should use the exact required hash for the artifact evidence role.

6. Internal eSales rows were clarified.
   - Official totals continue to come from M5 canonical projection control totals.
   - Per-document eSales rows are marked presentation-only.
   - Per-document VAT net recomputation was removed from the PRE-CRYSTAL dataset service.

7. Tests were expanded.
   - Existing focused gate remains `reports-precrystal-m0-full-reporting-spine`.
   - The gate now checks QA1 hardening behavior and source boundaries.

## Boundaries preserved

- No SAP packages.
- No Crystal binaries.
- No `.rpt` files.
- No printer logic.
- No installer changes.
- No PDF/rendering success claim.
- No production official posting activation.
- No eSales transmission enablement.
- No frozen RMO accounting rewrite.
- No Sandbox or installer branch modification.

## Required Codex/Sol verification

Run:

```powershell
dotnet build Salryn.sln
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only reports-precrystal-m0-full-reporting-spine
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only rmo-bir-trad-m5-rc2-xz-ej-esales-completion
dotnet run --project tools/Salryn.Tools.CoreSmoke
npm --prefix src/Salryn.App.Web run build
```

Do not claim green until local logs exist.
