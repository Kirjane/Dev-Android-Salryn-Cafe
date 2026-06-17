# Phase 66G1 - Local Host Runtime Lifecycle Activation Boundary

Status: Controlled lifecycle activation boundary only.

## Scope

This phase gives the App.WinForms Local Host lifecycle holder controlled lifecycle methods for the already-composed in-process Local Host runtime.

No automatic runtime activation was added.

## Added / changed

- SalrynLocalHostRuntimeLifecycleResult was added.
- SalrynLocalHostRuntimeLifecycleHolder now exposes TryStart(DateTimeOffset startedAt).
- SalrynLocalHostRuntimeLifecycleHolder now exposes TryStop().
- TryStart fails closed when the runtime is faulted.
- TryStart verifies the runtime reaches Running after the start attempt.
- TryStop verifies the runtime reaches Stopped after the stop attempt.
- Runtime lifecycle exceptions are captured into lifecycle result state.
- Historical smoke guards were updated so lifecycle ownership is allowed only in the lifecycle holder.
- Added Phase66G1LocalHostRuntimeLifecycleActivationChecks.
- CoreSmoke now runs the 66G1 guard after 66G0.

## Preserved boundaries

- No automatic runtime activation was added.
- No Program behavior was changed.
- Program remains clean.
- No Shell routing behavior was changed.
- Shell still only publishes CurrentAppSession into the session identity provider.
- Adapters and providers still do not start or stop runtime.
- ProductLookup adapter remains dispatch-only.
- Request context provider remains HostRequestContext creation only.
- Session identity provider remains CurrentAppSession identity only.
- No transport was added.
- No LAN behavior was added.
- No HTTP behavior was added.
- No socket behavior was added.
- No two-machine behavior was added.
- No direct SQLite access was added outside Infrastructure.

## Current state after this phase

The Local Host runtime can now be started or stopped through the lifecycle holder only.

Nothing calls TryStart automatically yet.

Runtime remains stopped during normal app composition until an explicit startup activation phase chooses the startup point.