# LICENSING M2 — Signed License Install/Import Report

**Date:** 2026-07-11  
**Branch:** `KIRCH-LICENSING-M2-SIGNED-LICENSE-INSTALL-IMPORT`  
**Base branch:** `KIRCH-LICENSING-M1-LOCAL-LICENSE-FOUNDATION`  
**Base SHA:** `66ba7c22bf505f6f19e5db622a7a3893b7b321c4`

> Note on final SHA: a committed report cannot accurately contain the SHA of the same commit that contains it, because the file contents affect the commit hash. The exact final branch SHA is therefore recorded in the Freehand final output after this report commit is created.

---

## Scope implemented

M2 adds a controlled local license install path on top of M1. It does not change the M1 read-only status semantics.

Implemented:

1. Narrow install/import contracts:
   - `LocalLicenseInstallRequest`
   - `LocalLicenseInstallResponse`
2. Expanded local license service boundary:
   - `ILocalLicenseService.InstallAsync(...)`
   - `LocalLicenseService.InstallAsync(...)`
3. Controlled document parsing and normalization:
   - accepts license document JSON content
   - canonicalizes trim-able fields and feature entries before write
   - rejects malformed JSON
   - rejects caller filesystem/path-like fields
   - rejects unsupported fields outside the narrow document contract
4. Existing verifier policy reused:
   - unsigned production licenses remain invalid
   - signed production licenses fail closed when no verifier is configured
   - invalid verifier result remains invalid
   - expired license remains rejected before replacement
   - unsupported schema remains rejected
   - machine mismatch remains handled by the existing validator policy
5. Controlled local replacement under the existing local license file store:
   - writes only under `LocalLicenseFileStore.RootDirectory`
   - writes a temp file under the controlled root
   - backs up existing `license.json` before replacement
   - uses system-generated backup file names only
   - does not read or write caller-supplied paths
   - cleans up temp files when practical
6. Guarded local API endpoint:
   - `POST /api/license/install`
   - preserves loopback/local operator guard
   - requires authenticated permission guard
   - uses the existing `Settings.Manage` permission instead of inventing a new broad security model
   - accepts document content only, not filesystem paths
7. Focused smoke coverage added:
   - missing license remains `Unlicensed`
   - malformed install rejected
   - unsigned production license rejected
   - signed license rejected when verifier is unavailable
   - expired signed license rejected
   - unsupported schema rejected
   - valid signed license accepted only when verifier accepts
   - old license backed up before replacement
   - failed install does not corrupt existing license
   - caller path-like fields rejected
   - endpoint guard/source checks
   - no network/cloud/payment/subscription dependency source checks
   - M1 status endpoint remains mapped and read-only by behavior boundary

---

## Files changed

- `src/Salryn.Core/Services/Licensing/LicenseContracts.cs`
- `src/Salryn.Core/Services/Licensing/ILocalLicenseService.cs`
- `src/Salryn.Infrastructure/Licensing/LocalLicenseFileStore.cs`
- `src/Salryn.Infrastructure/Licensing/LocalLicenseService.cs`
- `src/Salryn.Api.Local/LocalLicenseEndpoints.cs`
- `tests/Salryn.Tests/LocalLicensingM1FoundationChecks.cs`
- `tests/Salryn.Tests/LocalLicensingM2SignedInstallImportChecks.cs`
- `tools/Salryn.Tools.CoreSmoke/Program.cs`
- `00-HANDOFF/LICENSING_M2_SIGNED_LICENSE_INSTALL_IMPORT_REPORT.md`

---

## Intentionally excluded

Not implemented in M2:

- CLI license import
- Salryn Cloud
- Vercel licensing
- payment/subscription sync
- online activation
- installer integration
- hardware lockout
- private signing keys
- public-key production verification unless safely configured later
- arbitrary SQL
- arbitrary caller filesystem paths
- RMO/reporting changes except the existing M1 smoke adjustment required for M2 endpoint coexistence
- production eSales transmission
- BIR/legal/licensing approval claims

Protected lanes were not intentionally edited:

- `KIRCH-SANDBOX`
- `KIRCH-INSTALLER-M1`
- installer lane
- reporting lane
- RMO lane

---

## Verification performed by Freehand

Local verification was **not** run by Freehand in this ChatGPT connector session.

Performed here:

- GitHub branch creation from `66ba7c22bf505f6f19e5db622a7a3893b7b321c4`
- connector-backed file writes to candidate branch only
- connector-backed file fetch inspection for key modified files
- connector-backed compare inspection against M1 base

Not performed here:

- `dotnet build`
- `dotnet test`
- `dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only licensing-m2-signed-license-install-import`
- full CoreSmoke
- runtime HTTP endpoint exercise
- Windows file replacement validation

---

## Required Sol/Codex verification before GREEN

Sol/Codex must verify before M2 can be called GREEN:

1. Branch head and diff are exactly as expected.
2. No protected refs moved.
3. `dotnet build` passes.
4. Focused M2 smoke passes:
   - `dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only licensing-m2-signed-license-install-import`
5. M1 licensing smoke still passes:
   - `dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only licensing-m1-local-license-foundation`
6. Full CoreSmoke impact is acceptable or explicitly deferred by Kirch.
7. Runtime endpoint is exercised locally with:
   - unauthenticated request rejected
   - non-loopback/remote bridge request rejected
   - authenticated `Settings.Manage` operator accepted for install attempts
   - non-admin/non-settings operator rejected if such a role is available
8. Replacement behavior is validated on the target Windows runtime.
9. No private signing keys or production signing material are present.
10. No cloud, Vercel, payment, subscription, online activation, installer, or RMO/reporting behavior was introduced.

---

## Status

M2 is a **candidate implementation only**.

Do **not** call this GREEN until Sol/Codex or a real local verification log confirms the gates above.

No production licensing/legal approval is claimed. No cloud licensing, Vercel licensing, payment/subscription flow, online activation, installer integration, BIR approval, or eSales transmission is implemented.
