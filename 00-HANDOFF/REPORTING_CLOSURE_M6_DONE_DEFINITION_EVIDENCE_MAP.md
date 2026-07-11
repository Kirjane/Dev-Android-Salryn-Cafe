# REPORTING-CLOSURE-M6 DONE-DEFINITION EVIDENCE MAP

Mode: stacked remote authoring only.

Base branch: `KIRCH-REPORTING-CLOSURE-M5-CONTROLLED-EXPORT-ARTIFACTS`  
Base SHA: `5eb11e40eae12c944fe1cddc162fdf28449f1a1f`  
Candidate branch: `KIRCH-REPORTING-CLOSURE-M6-INTEGRATION-FREEZE-CANDIDATE`

## Important status

M2, M3, M4, and M5 have not been Codex/Sol verified yet.

This document maps intended done-definition evidence from the stacked reporting closure lane. It does not claim local verification, green gates, final acceptance, or final reporting closure.

## Evidence map

| Done-definition item | Evidence branch / file / test | Coverage status |
|---|---|---|
| Primary free/native PDF engine selected | `00-HANDOFF/REPORTING_CLOSURE_M1_STRATEGY_AND_PHASE_LOCK.md` | Strategy locked; M1 Codex green per current project status. |
| PDFsharp/MigraDoc package probe | `KIRCH-REPORTING-CLOSURE-M2-FREE-NATIVE-PDF-PACKAGE-PROBE`; `src/Salryn.Infrastructure/Salryn.Infrastructure.csproj`; `src/Salryn.Infrastructure/Reports/NativePdf/NativePdfPackageProbe.cs`; `tests/Salryn.Tests/NativePdfM2PackageProbeChecks.cs` | Remote-authored, unverified. |
| Native PDF renderer core | `KIRCH-REPORTING-CLOSURE-M3-NATIVE-PDF-RENDERER-CORE`; `src/Salryn.Infrastructure/Reports/NativePdf/NativePdfReportRenderer.cs`; `NativePdfDocumentBuilder.cs`; `NativePdfArtifactFactory.cs`; `NativePdfRendererIdentity.cs`; `tests/Salryn.Tests/NativePdfM3RendererCoreChecks.cs` | Remote-authored, unverified. |
| All six core report families proved | `KIRCH-REPORTING-CLOSURE-M4-CORE-REPORT-FAMILY-PROOF`; `tests/Salryn.Tests/NativePdfM4CoreReportFamilyProofChecks.cs` | Remote-authored, unverified. |
| Traditional X reading | `PreCrystalReportDatasetService.BuildDatasetAsync`; `ProductionReportKinds.TraditionalXReading`; `NativePdfM4CoreReportFamilyProofChecks` | M4 proof path, unverified. |
| Traditional Z reading preview | `PreCrystalReportDatasetService.BuildDatasetAsync`; `ProductionReportKinds.TraditionalZReading`; `NativePdfM4CoreReportFamilyProofChecks` | M4 proof path, unverified. |
| EJ export package or PDF/text proof | `PreCrystalReportDatasetService.BuildDatasetAsync`; `ProductionReportKinds.EjExport`; `NativePdfM4CoreReportFamilyProofChecks` | M4 proof path, unverified. |
| Sales/refund/void summary | `PreCrystalReportDatasetService.BuildDatasetAsync`; `ProductionReportKinds.SalesRefundVoidSummary`; `NativePdfM4CoreReportFamilyProofChecks` | M4 proof path, unverified. |
| Tax bucket summary | `PreCrystalReportDatasetService.BuildDatasetAsync`; `ProductionReportKinds.TaxBucketSummary`; `NativePdfM4CoreReportFamilyProofChecks` | M4 proof path, unverified. |
| Internal eSales draft, marked internal/unconfirmed | `PreCrystalReportDatasetService.ESalesDataset`; `ProductionReportKinds.InternalESalesDraft`; `NativePdfM4CoreReportFamilyProofChecks`; `NativePdfM6IntegrationFreezeCandidateChecks` | Boundary mapped; unverified. |
| Controlled artifact/export path | `KIRCH-REPORTING-CLOSURE-M5-CONTROLLED-EXPORT-ARTIFACTS`; `NativePdfReportExportService.cs`; `NativePdfArtifactStore.cs`; `LocalProductionReportEndpoints.cs`; `NativePdfM5ArtifactExportHardeningChecks.cs` | Remote-authored, unverified. |
| Controlled root policy | `src/Salryn.Infrastructure/Reports/NativePdf/NativePdfArtifactRootPolicy.cs`; `NativePdfM5ArtifactExportHardeningChecks.cs`; `NativePdfM6IntegrationFreezeCandidateChecks.cs` | Remote-authored, unverified. |
| Manifest JSON | `ReportArtifactContracts.cs`; `NativePdfArtifactStore.cs`; `NativePdfM5ArtifactExportHardeningChecks.cs` | Remote-authored, unverified. |
| PDF SHA-256 | `NativePdfArtifactFactory.Sha256`; `NativePdfArtifactStore.SaveAsync`; `ProductionReportArtifactManifest.PdfSha256`; M5 checks | Remote-authored, unverified. |
| Dataset `ContentSha256` preserved | `ReportArtifactContracts.cs`; `NativePdfArtifactStore.cs`; M5 checks | Remote-authored, unverified. |
| Dataset `DatasetSha256` preserved | `ReportArtifactContracts.cs`; `NativePdfArtifactStore.cs`; M5 checks | Remote-authored, unverified. |
| Safe filenames | `NativePdfArtifactFactory.SanitizeFileToken`; `NativePdfArtifactRootPolicy.CreateRelativeArtifactPath`; M5 checks | Remote-authored, unverified. |
| No caller output path | `ProductionReportExportApiRequest`; `NativePdfReportExportRequest`; `NativePdfArtifactRootPolicy.RejectCallerPath`; M5/M6 checks | Remote-authored, unverified. |
| No arbitrary templates | Absence from endpoint/export contracts; M5/M6 source checks | Remote-authored, unverified. |
| No arbitrary SQL | Absence from endpoint/export contracts; server-owned `PreCrystalReportDatasetService`; M5/M6 source checks | Remote-authored, unverified. |
| Permission-gated export endpoint | `LocalProductionReportEndpoints.TryBuildDatasetRequest`; `Guard(httpContext, sessions, catalogItem.RequiredPermission)`; M6 checks | Remote-authored, unverified. |
| Range cap preserved | `LocalProductionReportEndpoints.TryParseRange`; `ProductionReportDatasetLimits.MaxRange`; `PreCrystalReportDatasetService.ValidateRange`; M6 checks | Remote-authored, unverified. |
| Row cap preserved | `PreCrystalReportDatasetService.ValidateProjectionSize`; `ProductionReportDatasetLimits.MaxRows`; PRE-CRYSTAL QA1 checks | Remote-authored, unverified. |
| Crystal parked optional only | `UnavailableCrystalReportRenderer`; `LocalProductionReportEndpoints` renderer status; `NativePdfM6IntegrationFreezeCandidateChecks` | Remote-authored, unverified. |
| No printer claim | `NativePdfReportRenderer.GetStatus().CanPrint == false`; `UnavailableCrystalReportRenderer.GetStatus().CanPrint == false`; M6 checks | Remote-authored, unverified. |
| No installer claim | No installer files changed in M2-M6; protected `KIRCH-INSTALLER-M1` unchanged; final remote compare required | Remote-authored, unverified. |
| No BIR accreditation claim | `PreCrystalReportDatasetService` safety flags; M6 checks | Remote-authored, unverified. |
| No production eSales transmission claim | `PreCrystalReportDatasetService` safety flags and internal eSales summary; M6 checks | Remote-authored, unverified. |

## Evidence gaps before M7

M7 cannot claim final reporting closure until Sol verifies:

1. `dotnet restore Salryn.sln`
2. `dotnet build Salryn.sln`
3. focused M2 gate
4. focused M3 gate
5. focused M4 gate
6. focused M5 gate
7. focused M6 gate
8. Crystal M1 regression gate
9. PRE-CRYSTAL M0 regression gate
10. full CoreSmoke
11. web build

## Non-goals before licensing

The following are not required for licensing:

- Crystal runtime proof
- printer integration
- installer packaging
- BIR accreditation
- production eSales transmission
- legal/licensing approval
- UI preview/download
- arbitrary download-by-path
- every future analytics report
- final visual polish for every report
