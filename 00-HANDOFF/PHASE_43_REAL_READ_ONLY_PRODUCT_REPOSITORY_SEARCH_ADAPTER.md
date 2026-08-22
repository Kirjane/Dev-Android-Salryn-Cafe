# Phase 43 - Real Read-Only Product Repository Search Adapter

Branch:
KIRCH-FRONT-ATTEMPT

Scope:
Replace the temporary demo product search boundary with a real read-only product repository adapter.

Implemented:
- Added CheckoutProductRepositorySearchBoundary.
- Removed CheckoutDemoProductSearchBoundary from app code.
- Checkout product search now uses IProductReadRepository.
- Program now composes CheckoutPresenter with services.CheckoutProductSearch.
- SalrynAppServices now exposes ProductReadRepository and CheckoutProductSearch.
- Added smoke check for repository-backed checkout product search boundary.
- Full smoke includes the Phase 43 smoke.

Runtime behavior:
- Product search now reads actual products from the local database.
- Demo values no longer work unless those products exist in the database.
- If no products exist yet, search correctly returns "No product found."

Rules preserved:
- No SQLite inside CheckoutShellForm.
- No SQLite inside CheckoutPresenter.
- No sale completion.
- No stock movement writing.
- No receipt printing.
- No product write from checkout UI.
- No schema migration.
- No SQL file.
- Complete Sale remains disabled.
