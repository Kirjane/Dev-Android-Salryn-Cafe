# LICENSING M3 — Local Enforcement Boundaries Report

## Scope

Milestone: `LICENSING M3 — Local Enforcement Boundaries`

Candidate branch: `KIRCH-LICENSING-M3-LOCAL-ENFORCEMENT-BOUNDARIES`

Base branch: `KIRCH-LICENSING-M2-SIGNED-LICENSE-INSTALL-IMPORT`

Base SHA: `657b3d830567771c2d53b35ab1c9ca5d25cfcfde`

Last implementation commit before this final report update: `8040778f71242b04c46ee59c0b1779f68588f9d1`

Final SHA note: the final candidate SHA must be taken from the Freehand final output or repository head after this report update. A commit cannot include its own resulting SHA inside its content.

## What was added

M3 adds a passive local licensing enforcement boundary on top of M1/M2. It does not lock existing product flows.

Added local enforcement contracts:

- `LicenseEnforcementReason`
- `LicenseEnforcementDecision`
- `LocalLicenseEntitlementsResponse`
- `ILocalLicenseEnforcementService`

Added enforcement implementation:

- `LocalLicenseEnforcementService`
- `RequireFeatureAsync(string feature, CancellationToken cancellationToken = default)`
- `IsFeatureAllowedAsync(string feature, CancellationToken cancellationToken = default)`

Enforcement behavior:

- missing license denies feature access
- malformed/invalid license denies feature access
- expired license denies feature access
- unsigned production license denies feature access through existing validator status
- signed license with no verifier denies feature access and is mapped to `VerifierUnavailable`
- valid signed license with verifier allows only included features
- unknown features deny access
- explicit development mode allows only explicitly configured development features
- machine-binding mismatch is mapped to `MachineMismatch` when the validator exposes that message/status path
- enforcement reads current license status through `ILocalLicenseService`; it does not independently re-parse the license store for decisions

Added local API helper:

- `ApiLicenseGuard.RequireLicensedFeatureAsync(...)`
- returns deterministic `License.FeatureDenied` JSON on denial

Added read-only API exposure:

- `GET /api/license/entitlements`
- guarded by loopback/local operator request
- guarded by authenticated local API session
- returns current license status, known features, enabled features, and per-feature enforcement decisions

Preserved existing licensing endpoints:

- `GET /api/license/status`
- `POST /api/license/install`

Added focused smoke check:

- `LocalLicensingM3LocalEnforcementBoundaryChecks`
- CoreSmoke `--only` hook: `licensing-m3-local-enforcement-boundaries`
- full CoreSmoke path invokes the M3 check after M1 and M2 licensing checks

## What was intentionally not enforced yet

M3 does not apply licensing gates to existing runtime product surfaces.

No broad lockout was added to:

- checkout
- shift operations
- users
- reporting
- RMO/BIR flows
- installer lane
- frontend UI
- local app startup

The API helper is a boundary for future endpoints. It is not mass-applied in M3.

## Files changed

- `00-HANDOFF/LICENSING_M3_LOCAL_ENFORCEMENT_BOUNDARIES_REPORT.md`
- `src/Salryn.Api.Local/Auth/ApiLicenseGuard.cs`
- `src/Salryn.Api.Local/LocalLicenseEndpoints.cs`
- `src/Salryn.Core/Services/Licensing/ILocalLicenseEnforcementService.cs`
- `src/Salryn.Core/Services/Licensing/LicenseContracts.cs`
- `src/Salryn.Infrastructure/Licensing/LocalLicenseEnforcementService.cs`
- `tests/Salryn.Tests/LocalLicensingM3LocalEnforcementBoundaryChecks.cs`
- `tools/Salryn.Tools.CoreSmoke/Program.cs`

## Focused smoke coverage added

The M3 smoke check covers:

- missing license denies POS feature
- malformed/invalid license denies feature
- expired license denies feature
- unsigned production license denies feature
- signed license with no verifier denies feature
- valid signed license with verifier allows included feature
- valid signed license denies feature not included
- unknown feature denies
- development mode allows only explicitly configured development features
- enforcement service does not mutate the license file
- API guard/helper source remains present
- entitlements endpoint remains loopback/auth guarded
- M1 status endpoint remains present
- M2 install endpoint remains present
- no `HttpClient`, Vercel, payment, or subscription sync dependency is added to the M3 implementation slice
- reporting/RMO source files are not given local licensing behavior in this milestone

## Freehand verification actually run

No local build, local test, or local CoreSmoke command was run by Freehand in this chat.

Connector-level checks performed only:

- base commit lookup
- candidate branch creation
- file fetch inspection
- connector file writes
- connector source inspection of CoreSmoke hook registration
- connector compare against M2 base
- connector combined status lookup
- source-level cleanup of named-argument compile-risk patterns before final report update

Do not treat those connector checks as local verification.

## Required Sol/Codex verification before GREEN

Before M3 can be called GREEN, Sol/Codex or a real local verification log must verify at minimum:

```text
dotnet build
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only licensing-m1-local-license-foundation
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only licensing-m2-signed-license-install-import
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only licensing-m3-local-enforcement-boundaries
full CoreSmoke if required by maintainer policy
```

Also verify:

- `GET /api/license/status` still behaves as M1 intended
- `POST /api/license/install` still behaves as M2 intended
- `GET /api/license/entitlements` is loopback/auth guarded
- `ApiLicenseGuard.RequireLicensedFeatureAsync` returns deterministic denial payloads
- no checkout/RMO/reporting/installer runtime behavior changed
- protected refs remain unchanged from local git truth

## Explicit exclusions

M3 does not implement:

- Salryn Cloud
- Vercel licensing
- payment/subscription sync
- online activation
- installer integration
- hardware lockout
- private signing keys
- arbitrary SQL
- arbitrary caller filesystem paths
- mass endpoint lockout
- RMO/reporting behavior changes
- production eSales transmission
- BIR/legal/licensing approval

## Status

M3 is a Freehand candidate only.

M3 is not GREEN until Sol/Codex or a real local verification log promotes it.
