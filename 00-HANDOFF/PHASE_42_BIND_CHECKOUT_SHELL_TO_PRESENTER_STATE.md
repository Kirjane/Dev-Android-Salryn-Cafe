# Phase 42 - Bind CheckoutShellForm to Presenter State

Branch:
KIRCH-FRONT-ATTEMPT

Scope:
Binds the visible WinForms checkout shell to the Phase 41 checkout presenter state.

Implemented:
- CheckoutShellForm now owns a CheckoutPresenter.
- Product textbox updates presenter input state.
- Enter key and Scan/Search button call SearchProductAsync.
- Add Item calls AddPreparedProductLine.
- Cart grid renders CheckoutLineViewModel rows.
- Totals panel renders CheckoutTotalsViewModel.
- Cash textbox and Pay Cash button update draft payment state.
- Remove Item and Clear Cart use presenter actions.
- Complete Sale remains disabled.
- Temporary CheckoutDemoProductSearchBoundary added for front-end attempt/demo visibility only.

Rules preserved:
- No SQLite in CheckoutShellForm.
- No sale completion.
- No stock movement writing.
- No receipt printing.
- No schema migration.
- No SQL file.
- Product lookup is read-only demo boundary for this front-end attempt.

Demo search values:
- 480000000001
- COFFEE
- SARDINES
- RICE
