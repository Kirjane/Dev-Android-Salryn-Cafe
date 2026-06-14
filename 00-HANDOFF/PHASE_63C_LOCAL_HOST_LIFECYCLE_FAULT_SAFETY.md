# Phase 63C - Local Host Lifecycle and Fault Safety

Status: Implementation checkpoint.

Purpose:
Harden the Local Host runtime lifecycle and fault behavior before any transport, LAN, or real POS handlers are introduced.

Implemented:
- Added RuntimeNotRunning dispatch error code
- Added RuntimeAlreadyFaulted dispatch error code
- Added HandlerFailure dispatch error code
- Added LocalHostDispatchResponses failure factory
- Made LocalHostHandlerRegistry access lock-protected
- Added RegisteredHandlerCount to LocalHostRuntimeSnapshot
- Added RegisterHandler to ILocalHostRuntime
- Restricted handler registration to stopped runtime state
- Blocked dispatch when LocalHostRuntime is stopped
- Blocked dispatch when LocalHostRuntime is faulted
- Wrapped handler exceptions into HostResponseEnvelope errors
- Moved LocalHostRuntime into Faulted state after handler failure
- Added boundary invalid-with-empty-issues fallback
- Updated Phase63B dispatch smoke check for stopped-only handler registration
- Added Phase63CLocalHostLifecycleFaultSafetyCompileChecks
- Added Phase 63C check to Salryn.Tools.CoreSmoke

Runtime behavior:
- Dispatch requires LocalHostRuntimeState.Running
- Handler registration requires LocalHostRuntimeState.Stopped
- Handler exceptions do not escape as raw runtime crashes
- Handler exceptions return LocalHost.Dispatch.HandlerFailure
- Handler failure moves runtime to Faulted
- Faulted runtime rejects future dispatch until Stop is called
- Stop resets runtime state to Stopped and clears start time
- Empty invalid boundary issue lists return BoundaryValidationFailed

Architecture rule preserved:
- Core still owns contracts, descriptors, and boundary validation
- Salryn.Host.Local owns only local in-process runtime composition and dispatch safety
- Infrastructure still owns SQLite
- WinForms UI was not changed
- No HTTP transport was implemented
- No socket transport was implemented
- No LAN discovery was implemented
- No real POS handlers were implemented
- No two-machine behavior was tested
- No mobile app was created
- No Product Edit UI work was resumed

Important:
This phase hardens Local Host safety only. It still does not expose a network endpoint or execute real business operations.

Next:
Phase 63D - Local Host Operation Handler Contract Mapping
