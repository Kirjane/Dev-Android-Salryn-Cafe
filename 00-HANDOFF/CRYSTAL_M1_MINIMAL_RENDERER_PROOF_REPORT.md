# Crystal M1 Minimal Renderer Proof Report

Prepared: 2026-07-11

Branch: `KIRCH-REPORTS-CRYSTAL-M1-RUNTIME-DECISION-AND-MINIMAL-PROOF`
Base: `KIRCH-REPORTS-PRECRYSTAL-M0-QA1-HARDENING`
Base SHA: `bcd6f576bde72db1111c1bc1a6a8f19b9b7d4dcd`

## Result

**Minimal proof implemented as worker-boundary protocol only.**

No actual SAP Crystal runtime adapter was implemented. No PDF artifact is produced. The existing unavailable renderer remains fail-closed.

## What changed

1. Renderer output allow-list
   - Added `ProductionReportOutputKinds`.
   - Only `pdf` is allow-listed.
   - Unsupported output kinds return `Reports.UnsupportedOutputKind`.

2. Renderer capability extension
   - Added `ProductionReportRendererCapability`.
   - Unavailable renderer reports no direct in-process support, no installed runtime, and no licensed runtime confirmation.

3. Worker protocol strengthening
   - Added `CrystalWorkerProtocol`.
   - Added `CrystalWorkerDatasetPackage`.
   - Added `CrystalWorkerArtifactPolicy`.
   - Added `CrystalWorkerLaunchPolicy`.
   - Added `CrystalWorkerRuntimeReadiness`.

4. Host endpoint hardening
   - `/api/production-reports/render` validates output kind before renderer invocation.
   - Renderer status endpoint exposes worker protocol and allowed output kinds.
   - Render path still rebuilds server-owned dataset from filters.
   - No caller-supplied dataset envelope is accepted.

5. Focused smoke gate
   - Added `CrystalM1RuntimeDecisionMinimalProofChecks`.
   - Added focused CoreSmoke gate:
     `reports-crystal-m1-runtime-decision-minimal-proof`.

## What did not change

- No SAP packages were added.
- No Crystal assemblies were added.
- No `.rpt` file was added.
- No runtime installer was added.
- No template path policy was activated.
- No printer logic was added.
- No PDF success path was added.
- No installer branch was touched.
- No Sandbox branch was touched.
- No production official posting activation was enabled.
- No eSales transmission was enabled.
- No M5 or QA1 accounting/reporting projection logic was rewritten.

## Future Codex/Sol conditions for real Crystal runtime

Before any actual renderer implementation:

1. Confirm a specific SAP Crystal Reports for Visual Studio support package and bitness.
2. Use a Windows-only isolated worker targeting a SAP-supported .NET Framework runtime.
3. Confirm the runtime is installed and licensed for the deployment scenario.
4. Add an allow-listed template key policy.
5. Do not load arbitrary `.rpt` uploads.
6. Do not permit arbitrary SQL or direct production DB access from templates.
7. Render only from Salryn server-owned dataset packages.
8. Write artifacts only under a controlled output root.
9. Capture artifact SHA-256, renderer version, source dataset hashes, and failure status.
10. Run a real render test on the licensed Windows machine before claiming PDF proof.

## Verification status

Not verified locally by Freehand.

Codex/Sol must run:

```powershell
dotnet build Salryn.sln

dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only reports-crystal-m1-runtime-decision-minimal-proof

dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only reports-precrystal-m0-full-reporting-spine

dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only rmo-bir-trad-m5-rc2-xz-ej-esales-completion

dotnet run --project tools/Salryn.Tools.CoreSmoke

npm --prefix src/Salryn.App.Web run build
```
