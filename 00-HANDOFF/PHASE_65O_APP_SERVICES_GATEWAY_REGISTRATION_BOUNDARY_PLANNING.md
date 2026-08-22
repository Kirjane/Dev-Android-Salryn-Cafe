# Phase 65O - AppServices Gateway Registration Boundary Planning

Status: AppServices gateway registration boundary planning only.

Purpose:
Plan the future registration boundary for the App.WinForms Local Host ProductLookup gateway without changing production app source, registering the adapter, consuming the holder, starting runtime, dispatching, adding transport, or changing UI behavior.

No production app source was changed.

Future registration boundary: App.WinForms Composition only.

Planned future rule:
- AppServices may become the owner of a passive gateway exposure
- The future gateway exposure must be read-only
- The future gateway exposure must be created inside App.WinForms Composition only
- The future gateway exposure must not be created in Program
- The future gateway exposure must not be created in Shell
- The future gateway exposure must not be passed into Checkout or Product Management yet
- The future gateway exposure must not consume the Local Host holder until a separate holder-aware wiring phase is approved

Allowed future shape:
- AppServices may expose IAppLocalHostProductLookupGateway as a passive property
- AppServices may instantiate CompositionLocalHostProductLookupGatewayAdapter
- The adapter must remain inert unless a separate holder-aware wiring phase is approved

Rejected for this phase:
- No production app source was changed
- No adapter was registered in SalrynAppServices
- No gateway was exposed by SalrynAppServices
- No holder was consumed
- No holder was passed into Program
- No holder was passed into Shell
- No Program startup behavior was changed
- No Shell routing behavior was changed
- No checkout UI behavior was changed
- No product management UI behavior was changed
- No Local Host runtime was started
- No dispatch was performed
- No HTTP transport was implemented
- No socket transport was implemented
- No transport was implemented
- No LAN discovery was implemented
- No two-machine behavior was implemented
- No direct SQLite access was added
- No Infrastructure reference was added to Host.Local
- No App.WinForms reference was added to Host.Local

Architecture rule preserved:
- App.WinForms Composition remains the only allowed wiring boundary for future gateway registration
- Program remains application boot only
- Shell remains UI routing only
- Host.Local remains Core-only
- Infrastructure remains outside Host.Local
- Local Host holder remains passive
- Runtime remains stopped
- ProductLookup dispatch remains unconsumed

Planning result:
Phase 65O defines the safe boundary for the next possible production source change. The next source change may expose the inert gateway through AppServices, but it must still avoid holder consumption, dispatch, runtime start, transport, Program changes, and Shell changes.

Next:
Phase 65P may add the passive AppServices gateway exposure, but it must remain separate and must not start runtime, perform dispatch, consume the holder, change Program, change Shell, or replace checkout/product management behavior unless that scope is explicitly approved first.
