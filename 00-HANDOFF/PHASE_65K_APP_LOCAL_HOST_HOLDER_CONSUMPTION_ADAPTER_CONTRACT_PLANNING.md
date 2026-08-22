# Phase 65K - App Local Host Holder Consumption Adapter Contract Planning

Status: Adapter contract planning only.

Purpose:
Plan the future adapter contract shape for consuming the App.WinForms Local Host ProductLookup holder without adding the production adapter interface, implementation, runtime start, dispatch, transport, or UI consumption yet.

This phase narrows the Phase 65J approved boundary into a named future gateway concept while keeping the repository behavior unchanged.

Implemented:
- Added Phase65KAppLocalHostHolderConsumptionAdapterContractPlanningChecks
- Added Phase 65K after Phase 65J in CoreSmoke
- Verified Phase 65H startup holder wiring remains intact
- Verified Phase 65I readiness seal remains intact
- Verified Phase 65J consumption boundary planning remains intact
- Verified no production adapter interface exists yet
- Verified no production adapter implementation exists yet
- Verified Program remains free of holder consumption
- Verified Shell remains free of holder consumption
- Verified Host.Local remains Core-only
- Verified App.WinForms Composition remains the only Host.Local wiring boundary
- Documented the planned adapter contract shape for a future explicit implementation phase

Planned adapter contract shape:
The future gateway name may be:

IAppLocalHostProductLookupGateway

The future gateway may expose one read-only lookup method:

TryLookupByBarcodeAsync

The future method must be planned around:
- barcode input only unless SKU support is explicitly added later
- cancellation token support
- non-throwing lookup result for not-found products
- explicit failure result for invalid barcode input
- explicit failure result for unavailable local host composition
- no direct SQLite access
- no Infrastructure reference from Host.Local
- no App.WinForms reference from Host.Local
- no Program usage
- no Shell direct composition usage
- stopped-runtime by default unless a later phase explicitly approves runtime start
- no dispatch unless a later phase explicitly approves dispatch
- read-only ProductLookup only

Rejected for this phase:
- No production source behavior was changed
- No Program startup behavior was changed
- No Shell routing behavior was changed
- No adapter interface was added to production source
- No adapter implementation was added
- No adapter was registered in SalrynAppServices
- No holder was passed into Program
- No holder was passed into Shell
- No checkout UI behavior was changed
- No product management UI behavior was changed
- No Local Host runtime was started
- No dispatch was performed
- No HTTP transport was implemented
- No socket transport was implemented
- No transport was implemented
- No LAN discovery was implemented
- No two-machine behavior was implemented

Architecture rule preserved:
- App.WinForms Composition remains the only allowed wiring boundary for future holder consumption
- Program remains application boot only
- Shell remains UI routing only
- Host.Local remains Core-only
- Infrastructure remains outside Host.Local
- Local Host holder remains passive
- Runtime remains stopped
- ProductLookup dispatch remains unconsumed

Planning result:
Phase 65K defines the future adapter contract shape only.

A future implementation phase must create the adapter contract explicitly, prove that it remains inside App.WinForms Composition, and still avoid Program/Shell/runtime/dispatch/transport changes unless separately approved.

Next:
Phase 65L may introduce the production adapter contract shell, but it must remain separate and must not implement dispatch, start runtime, change Program, change Shell, or replace checkout/product management behavior unless that scope is explicitly approved first.
