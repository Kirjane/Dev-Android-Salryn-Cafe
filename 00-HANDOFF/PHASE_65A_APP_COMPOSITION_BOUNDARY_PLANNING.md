# Phase 65A - App Composition Boundary Planning

Status: Planning seal for future WinForms-to-LocalHost composition wiring.

Purpose:
Plan the boundary between Salryn.App.WinForms composition root and Salryn.Host.Local explicit ProductLookup composition without adding runtime behavior, app shell behavior, or Host.Local reference to App.WinForms yet.

Implemented:
- Added Phase65AAppCompositionBoundaryPlanningChecks
- Verified App.WinForms remains the app composition root for Infrastructure and services
- Verified SalrynAppServices exposes IProductReadRepository for future Local Host ProductLookup composition
- Verified SalrynAppServices does not call LocalHostBootstrapComposer yet
- Verified App.WinForms project does not reference Salryn.Host.Local yet
- Verified Host.Local ComposeWithProductLookup path exists for a future wiring slice
- Verified Program still creates SalrynAppServices through the composition root
- Verified Shell receives dependencies from SalrynAppServices
- Verified WinForms feature code does not directly touch Infrastructure outside Composition
- Added Phase 65A check to Salryn.Tools.CoreSmoke

Architecture rule preserved:
- No runtime behavior was added
- No Host.Local project reference was added to App.WinForms
- No Local Host runtime was created by the app shell
- No ProductLookup app shell flow was changed
- No database wiring was moved into Host.Local
- No HTTP transport was implemented
- No socket transport was implemented
- No LAN discovery was implemented
- No two-machine behavior was implemented

Important:
Phase 65A is planning-only. It proves the future app composition path has the required dependency boundary, but it does not wire Local Host into the WinForms app yet.

Planned future wiring:
- App.WinForms composition root may later reference Salryn.Host.Local explicitly
- SalrynAppServices may later compose LocalHostBootstrapComposer.ComposeWithProductLookup using ProductReadRepository
- The shell should receive a composed host boundary from SalrynAppServices, not construct it directly

Result:
The app composition boundary is planned and guarded before introducing any WinForms-to-LocalHost wiring.

Next:
Phase 65B - App Local Host Composition Reference Slice
