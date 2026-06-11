# Phase 44 - Full Checkout UI Layout Overhaul

Branch:
KIRCH-FRONT-ATTEMPT

Scope:
Polish the visible checkout UI while preserving the existing Phase 43 architecture.

Implemented:
- Rebuilt CheckoutShellForm visual layout.
- Added cleaner top header.
- Added stronger product lookup card.
- Added prepared product display area.
- Improved draft cart grid styling.
- Improved totals card.
- Improved payment card.
- Improved actions card.
- Improved status bar.
- Preserved presenter-driven rendering.
- Preserved repository-backed read-only product search.

Rules preserved:
- No SQLite in CheckoutShellForm.
- No SQLite in CheckoutPresenter.
- No final sale save.
- No inventory write.
- No receipt printing.
- No product write from checkout UI.
- No schema migration.
- No SQL file.
- Complete Sale button remains locked.

Current allowed behavior:
- Search existing products from the local product repository.
- Prepare a product from search result.
- Add prepared product to draft cart.
- Render draft cart lines.
- Render totals.
- Add draft cash payment.
- Remove selected draft item.
- Clear draft cart.
