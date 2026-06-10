# Salryn Phase 39 POS Checkout UI Shell Preparation Read-Only Audit

Status:
PHASE 39 READ-ONLY AUDIT COMPLETED.

Target:
POS Checkout UI Shell Preparation.

Purpose:
- Inspect the current WinForms project structure.
- Inspect current application startup.
- Inspect current composition surface.
- Inspect checkout services already available from Core.
- Inspect product picker boundary, draft builder, and checkout application boundary.
- Inspect existing UI forms, controls, designer files, and navigation references.
- Confirm build and full smoke pass before touching UI shell.

Planned Phase 39 Scope:
- Prepare a WinForms POS checkout shell surface only.
- Add placeholder checkout UI container if the current app structure supports it.
- Add safe navigation or launch surface if the current app structure supports it.
- Add labels/placeholders for product scan/search, cart, totals, payment, and actions.
- Keep controls disabled or non-operational where backend wiring is not yet intended.
- Add no real checkout completion from UI.
- Add no product search execution from UI.
- Add no receipt printing from UI.
- Add no stock movement writing from UI.
- Add no schema migration.
- Add no database file.
- Add no SQL file.

Recommended Phase 39 Files Depending On Existing Shape:
- src/Salryn.App.WinForms\Features\Checkout\CheckoutShellForm.cs
- src/Salryn.App.WinForms\Features\Checkout\CheckoutShellForm.Designer.cs
- src/Salryn.App.WinForms\Features\Checkout\CheckoutShellForm.resx
- or a UserControl equivalent if existing app uses panels/user controls.
- 00-HANDOFF\PHASE39_POS_CHECKOUT_UI_SHELL_PREPARATION_AUDIT.md

Recommended UI Shell Sections:
- Header: SALRYN POS Checkout
- Product input placeholder: Barcode / product search
- Product result placeholder
- Cart grid placeholder
- Totals placeholder
- Payment placeholder
- Action buttons placeholder:
  - Add Item
  - Remove Item
  - Clear Cart
  - Pay Cash
  - Complete Sale
- Status area:
  - Engine ready
  - UI shell only
  - No sale completion wired yet

Recommended Rules:
- No UI business logic.
- No direct SQLite inside forms.
- No repository calls from form event handlers in this phase.
- No sale completion.
- No receipt printing.
- No inventory movement.
- No schema changes.
- No SQL files.
- Build must pass.
- Full smoke must pass.
- DB/SQL file check must be clean before commit.

Next After Phase 39:
- Phase 40 can wire a safe UI presenter/view-model boundary.
- Phase 41 can wire product search/read-only picker into UI.
- Phase 42 can wire draft cart behavior into UI.
- Sale completion should remain later and gated by cashier session and full smoke.
