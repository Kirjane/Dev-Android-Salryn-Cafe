# Phase 65R - Holder-Aware Adapter Constructor Boundary Shell

Status: Holder-aware adapter constructor boundary shell only.

Purpose:
Introduce the holder-aware constructor boundary inside CompositionLocalHostProductLookupGatewayAdapter without wiring it from AppServices, consuming the holder for lookup, dispatching, starting runtime, adding transport, changing Program, changing Shell, or changing checkout/product management behavior.

Production source change: adapter constructor boundary shell only.

Implemented:
- Added a passive SalrynLocalHostCompositionHolder field inside CompositionLocalHostProductLookupGatewayAdapter
- Preserved the parameterless adapter constructor used by AppServices
- Added a holder-aware constructor boundary with null guarding
- Kept TryLookupByBarcodeAsync inert and unavailable for valid barcode lookup
- Advanced prior checks so the approved 65R constructor boundary is no longer falsely blocked
- Added Phase65RHolderAwareAdapterConstructorBoundaryShellChecks
- Added Phase 65R after Phase 65Q in CoreSmoke

Allowed in this phase:
- Adapter may accept a SalrynLocalHostCompositionHolder through a constructor boundary
- Adapter may store the holder passively
- Adapter may keep returning LocalHostUnavailable until dispatch is approved
- AppServices may continue using the parameterless inert adapter constructor

Rejected for this phase:
- No AppServices holder wiring was changed
- No holder dispatch was performed
- No holder composition was accessed
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
- No checkout presenter behavior was changed
- No product management presenter behavior was changed
- No Program startup behavior was changed
- No Shell routing behavior was changed
- No direct SQLite access was added
- No Infrastructure reference was added to Host.Local
- No App.WinForms reference was added to Host.Local

Architecture rule preserved:
- App.WinForms Composition remains the only allowed wiring boundary
- Program remains application boot only
- Shell remains UI routing only
- Host.Local remains Core-only
- Infrastructure remains outside Host.Local
- Runtime remains stopped
- ProductLookup dispatch remains unconsumed
- Gateway remains unavailable until dispatch is separately approved

Planning result:
Phase 65R introduces only the safe constructor shape required for future holder-aware gateway wiring. The adapter can now be made holder-aware later without changing Program, Shell, UI behavior, transport, or direct database access.

Next:
Phase 65S may introduce AppServices holder-aware adapter wiring, but it must remain separate and must not start runtime, perform dispatch, add transport, change Program, change Shell, or change checkout/product management behavior unless explicitly approved first.
