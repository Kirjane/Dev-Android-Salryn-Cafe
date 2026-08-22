# Phase 65Q - Holder-Aware Gateway Wiring Planning

Status: Holder-aware gateway wiring planning only.

Purpose:
Plan the safe boundary for future holder-aware ProductLookup gateway wiring without implementing holder consumption, dispatch, runtime start, transport, Program changes, Shell changes, checkout behavior changes, or product management behavior changes.

No production source behavior was changed.

Current sealed state:
- Phase 65P already exposes the passive inert gateway through SalrynAppServices.
- The gateway still returns LocalHostUnavailable for valid barcode lookup.
- The passive Local Host ProductLookup holder remains exposed through SalrynAppServices.
- The adapter does not consume the holder.
- The holder does not dispatch.
- The runtime is not started.

Allowed in this phase:
- Add planning documentation for holder-aware gateway wiring.
- Add verification checks for the future holder-aware wiring boundary.
- Register Phase65QHolderAwareGatewayWiringPlanningChecks in CoreSmoke.

Rejected for this phase:
- No production source behavior was changed
- No holder was consumed
- No holder was passed into the adapter
- No holder-aware adapter was implemented
- No LocalHostBootstrapCompositionResult was exposed through the gateway adapter
- No ProductLookup dispatch was performed
- No runtime was started
- No dispatch was performed
- No HTTP transport was implemented
- No socket transport was implemented
- No transport was implemented
- No LAN discovery was implemented
- No two-machine behavior was implemented
- No checkout UI behavior was changed
- No product management UI behavior was changed
- No Program startup behavior was changed
- No Shell routing behavior was changed
- No Infrastructure reference was added to Host.Local
- No App.WinForms reference was added to Host.Local

Future holder-aware wiring rule:
- Holder-aware ProductLookup wiring may only happen inside App.WinForms Composition.
- Program must remain boot only.
- Shell must remain UI routing only.
- Host.Local must remain Core-only.
- Infrastructure must remain outside Host.Local.
- The adapter may not start runtime.
- The adapter may not add transport.
- The adapter may not touch SQLite directly.
- Checkout and product management must not consume the gateway until a separate approved UI/application phase.
- Any future dispatch path must be introduced separately and verified before UI consumption.

Architecture rule preserved:
- App.WinForms Composition remains the only allowed wiring boundary.
- Program remains application boot only.
- Shell remains UI routing only.
- Host.Local remains Core-only.
- Infrastructure remains outside Host.Local.
- Local Host holder remains passive.
- Runtime remains stopped.
- ProductLookup dispatch remains unconsumed.
- Gateway remains inert until holder-aware wiring is explicitly implemented in a later phase.

Planning result:
Phase 65Q freezes the design contract for future holder-aware ProductLookup wiring. The next implementation phase can safely target the App.WinForms Composition boundary without touching Program, Shell, UI behavior, runtime start, LAN, transport, or direct SQLite access.

Next:
Phase 65R may introduce the holder-aware gateway adapter constructor boundary or adapter wiring shell, but it must remain separate and must not start runtime, perform dispatch, add transport, change Program, change Shell, or change checkout/product management behavior unless explicitly approved first.
