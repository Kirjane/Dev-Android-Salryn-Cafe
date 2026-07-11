# LICENSING M1 — Local License Foundation Report

## Remote truth

Repo: `Kirch-Nairu/Salryn`

Base branch: `KIRCH-REPORTING-CLOSURE-M7-FREEHAND-QUALITY-PASS`

Base SHA: `90d14d71b1b8ae6cd7f2c01dc633a944ce154d3a`

Candidate branch: `KIRCH-LICENSING-M1-LOCAL-LICENSE-FOUNDATION`

Protected refs verified before authoring:

- `KIRCH-SANDBOX = 1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
- `KIRCH-INSTALLER-M1 = 6b9c46676390b8a86a989eca600cb02e406ce865`

## Scope

This branch implements the local/offline-first licensing foundation only.

It does not implement Salryn Cloud licensing, Vercel licensing, payment sync, subscription sync, online activation, installer packaging, or hardware lockout.

## Changed files

Added:

- `src/Salryn.Core/Services/Licensing/LicenseContracts.cs`
- `src/Salryn.Core/Services/Licensing/ILocalLicenseService.cs`
- `src/Salryn.Infrastructure/Licensing/LocalLicenseFileStore.cs`
- `src/Salryn.Infrastructure/Licensing/LocalLicenseValidator.cs`
- `src/Salryn.Infrastructure/Licensing/LocalLicenseService.cs`
- `src/Salryn.Api.Local/LocalLicenseEndpoints.cs`
- `tests/Salryn.Tests/LocalLicensingM1FoundationChecks.cs`
- `00-HANDOFF/LICENSING_M1_LOCAL_LICENSE_FOUNDATION_REPORT.md`

Modified:

- `src/Salryn.Api.Local/LocalApiApplicationFactory.cs`

Pending Sol repair:

- `tools/Salryn.Tools.CoreSmoke/Program.cs` still needs the focused `licensing-m1-local-license-foundation` route wired. Freehand did not complete that whole-file edit.

## License architecture summary

M1 introduces a local file-based license model:

- controlled local license JSON file
- local file reader
- local validator
- local service facade
- read-only local API status endpoint
- smoke checks for missing/malformed/expired/unsigned/development-mode behavior

All behavior is offline-first. The M1 implementation does not call a network, cloud service, payment provider, subscription service, Vercel, or registry.

## Status model

`LicenseStatus` includes:

- `Unlicensed`
- `Licensed`
- `Expired`
- `Invalid`
- `Grace`
- `Development`

`LicenseEdition` includes:

- `Trial`
- `Local`
- `Professional`
- `Enterprise`

`LicenseFeatures` includes:

- `Pos`
- `Rmo`
- `Reports`
- `Users`
- `Backup`
- `Licensing`

## Controlled path policy

`LocalLicenseFileStore` owns the path.

Default root:

`LocalApplicationData/Salryn/Licensing`

Default file:

`license.json`

Rules:

- no API body accepts a license file path
- no caller-supplied arbitrary path
- file name validation rejects rooted paths
- file name validation rejects traversal
- M1 does not use registry storage
- M1 does not mutate license files

## Signature policy

M1 does not store private keys in the repo.

Production-looking licenses fail closed unless a signature verifier is explicitly configured.

Unsigned licenses do not become `Licensed`.

If no signature verifier is configured, signed-license validation fails closed as `Invalid`.

This is intentional. Real key management belongs to a later licensing phase.

## Development-mode boundary

Development mode is explicit only.

Default missing-file behavior is `Unlicensed`.

`Development` status is returned only when `LocalLicenseValidatorOptions.AllowDevelopmentMode` is set explicitly.

No production default silently enters development mode.

## API endpoint summary

Added endpoint:

`GET /api/license/status`

Boundary:

- loopback/local access guard
- authenticated operator session required
- returns `LocalLicenseValidationResult`
- no private keys exposed
- no machine secret exposed
- no caller path accepted
- no license mutation in M1

Startup mapping added:

`app.MapLocalLicenseEndpoints();`

## What was not implemented

Not implemented:

- cloud licensing
- Vercel licensing
- payment sync
- subscription sync
- online activation
- license upload/update endpoint
- installer integration
- hardware lockout
- production signing key management
- legal/licensing approval
- production licensing completion

## Known unverified items

Freehand did not run:

- `dotnet restore Salryn.sln`
- `dotnet build Salryn.sln`
- focused CoreSmoke gates
- full CoreSmoke
- web build
- API runtime checks

Known Sol repair item:

- wire `tools/Salryn.Tools.CoreSmoke/Program.cs` for `licensing-m1-local-license-foundation`

Additional risk:

- `LocalApiApplicationFactory.cs` was whole-file rewritten to add endpoint mapping. Sol must verify no local API helper behavior was lost.

## Sol/Codex verification commands

```powershell
git fetch origin

git checkout KIRCH-LICENSING-M1-LOCAL-LICENSE-FOUNDATION
git pull --ff-only origin KIRCH-LICENSING-M1-LOCAL-LICENSE-FOUNDATION

git rev-parse HEAD

# compare against base
git diff --stat 90d14d71b1b8ae6cd7f2c01dc633a944ce154d3a..HEAD

dotnet restore Salryn.sln
dotnet build Salryn.sln

dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only licensing-m1-local-license-foundation
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only reports-native-pdf-m6-integration-freeze-candidate
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only reports-crystal-m1-runtime-decision-minimal-proof
dotnet run --project tools/Salryn.Tools.CoreSmoke

npm --prefix src/Salryn.App.Web run build
```

Sol should repair only concrete compile/gate failures and should not expand licensing scope.
