# Phase 66G2 - AppServices Local Host Startup Activation

Status: AppServices startup activation only.

## Scope

This phase starts the already-composed in-process Local Host runtime from the App.WinForms composition boundary.

Runtime now starts during AppServices composition through TryStart.

## Added / changed

- SalrynAppServices now stores LocalHostRuntimeStartupResult.
- SalrynAppServices now calls LocalHostRuntimeLifecycleHolder.TryStart(DateTimeOffset.UtcNow).
- Startup activation fails closed if the lifecycle holder reports failure.
- ProductLookup adapter wiring remains unchanged.
- Added Phase66G2AppServicesLocalHostStartupActivationChecks.
- CoreSmoke now runs the 66G2 guard after 66G1.

## Preserved boundaries

- Program remains clean.
- Shell routing remains clean.
- Shell still only publishes CurrentAppSession into the session identity provider.
- Adapters and providers still do not start or stop runtime.
- ProductLookup adapter remains dispatch-only.
- Lifecycle holder remains the only App.WinForms boundary that directly calls runtime Start/Stop.
- No transport was added.
- No LAN behavior was added.
- No HTTP behavior was added.
- No socket behavior was added.
- No two-machine behavior was added.
- No direct SQLite access was added outside Infrastructure.

## Current state after this phase

AppServices composition now starts the local in-process runtime.

ProductLookup can now reach dispatch when an authenticated session with UserId and RoleId exists.

This is still single-machine local runtime behavior only.