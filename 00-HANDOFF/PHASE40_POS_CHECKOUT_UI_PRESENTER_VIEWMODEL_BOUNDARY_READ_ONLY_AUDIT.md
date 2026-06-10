# Salryn Phase 40 POS Checkout UI Presenter/ViewModel Boundary Read-Only Audit

Status:
PHASE 40 READ-ONLY AUDIT COMPLETED.

Target:
POS Checkout UI Presenter/ViewModel Boundary.

Purpose:
- Inspect Phase 39 WinForms checkout UI shell.
- Inspect current Program.cs launch surface.
- Inspect app composition surface.
- Inspect existing Core checkout draft transaction builder.
- Inspect existing product picker boundary.
- Inspect existing checkout application boundary.
- Confirm build and full smoke pass before adding UI presenter/viewmodel boundary.

Planned Phase 40 Scope:
- Add a UI-safe presenter/viewmodel boundary between CheckoutShellForm and Core checkout draft engine.
- Presenter/viewmodel should hold UI state for:
  - Cashier ID placeholder
  - Receipt number placeholder
  - Product input text
  - Product result/status text
  - Draft cart lines
  - Totals
  - Payment input
  - Button enabled/disabled states
  - Status message
- Presenter/viewmodel may use PosCheckoutDraftTransactionBuilder.
- Presenter/viewmodel may expose simple methods for future UI:
  - Initialize
  - SetCashierUserId
  - SetReceiptNumber
  - SetProductInputText
  - AddPreparedLine
  - RemoveLine
  - ClearDraft
  - SetCashAmount
  - AddCashPayment
  - ClearPayments
  - RefreshState
- Presenter/viewmodel must not call SQLite directly.
- Presenter/viewmodel must not call repositories directly in Phase 40.
- Presenter/viewmodel must not complete sales in Phase 40.
- Presenter/viewmodel must not print receipts.
- Presenter/viewmodel must not write stock movements.
- CheckoutShellForm wiring should remain minimal or shell-only unless explicitly limited to state rendering.

Recommended Phase 40 Files:
- src/Salryn.App.WinForms/Features/Checkout/CheckoutViewModels.cs
- src/Salryn.App.WinForms/Features/Checkout/CheckoutPresenter.cs
- tools/Salryn.Tools.InfrastructureSmoke/PosCheckoutUiPresenterViewModelSmokeChecks.cs
- 00-HANDOFF/PHASE40_POS_CHECKOUT_UI_PRESENTER_VIEWMODEL_BOUNDARY_AUDIT.md

Recommended Smoke Command:
- pos-checkout-ui-presenter-viewmodel

Recommended Smoke Coverage:
- Presenter initializes empty state.
- Cashier ID can be set.
- Receipt number can be set.
- Product input text can be set and trimmed.
- Prepared draft line can be added.
- Draft line appears in UI viewmodel.
- Totals map correctly from draft builder to UI viewmodel.
- Cash payment can be added.
- Balance/change mapping works.
- Clear payments works.
- Remove line works.
- Clear draft resets state.
- Invalid line/input returns safe UI error state.
- No database touched.
- No sale completed.
- No receipt printing.

Rules:
- No production database touched.
- No database schema changes.
- No SQL files created.
- No direct SQLite inside UI presenter/viewmodel.
- No repository calls from presenter/viewmodel in Phase 40.
- No sale completion.
- No receipt printer logic.
- No reports logic.
- This audit is read-only except this handoff file.

Next After Phase 40:
- Phase 41 can wire read-only product search/picker into presenter.
- Phase 42 can wire checkout shell controls to presenter state.
- Sale completion stays later and must remain cashier-session gated.
