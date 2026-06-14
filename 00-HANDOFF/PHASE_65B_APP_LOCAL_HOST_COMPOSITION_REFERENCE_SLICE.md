# Phase 65B - App Local Host Composition Reference Slice

Status: Controlled App.WinForms to Salryn.Host.Local reference slice.

Purpose:
Allow the WinForms app composition root to reference Salryn.Host.Local and expose a controlled ProductLookup Local Host composition method without changing Program, Shell, UI behavior, or app startup behavior.

Implemented:
- Added Salryn.Host.Local project reference to Salryn.App.WinForms
- Added Salryn.Core.Deployment and Salryn.Host.Local.Composition imports to SalrynAppServices
- Added SalrynAppServices.CreateLocalHostProductLookupComposition(StoreDeploymentProfile profile)
- The new method uses LocalHostBootstrapComposer.ComposeWithProductLookup
- The new method passes the existing ProductReadRepository into Host.Local
- Updated Phase65A planning check to allow only the planned 65B composition-root reference
- Added Phase65BAppLocalHostCompositionReferenceSliceChecks
- Added Phase 65B check to Salryn.Tools.CoreSmoke

Architecture rule preserved:
- Host.Local still references Core only
- Host.Local still does not reference Infrastructure
- Host.Local still does not reference App.WinForms
- Infrastructure wiring remains inside App.WinForms Composition
- Host.Local wiring remains inside App.WinForms Composition
- Program does not create Local Host composition yet
- Shell does not create Local Host composition yet
- No Local Host runtime is started by the app
- No ProductLookup UI flow was changed
- No HTTP transport was implemented
- No socket transport was implemented
- No LAN discovery was implemented
- No two-machine behavior was implemented

Important:
Phase 65B adds the legal project reference and controlled composition method only. It does not call the method from app startup, shell routing, checkout, product management, or any user-facing flow.

Result:
App.WinForms may now legally compose the explicit Host.Local ProductLookup path through SalrynAppServices, but no runtime behavior is activated yet.

Next:
Phase 65C - App Local Host Composition Creation Guard Slice
