# J20-4B - UI Workflow Readiness Guards

Status: UI workflow readiness guards only.

## Scope

Added CoreSmoke guards that inspect the WinForms workflow wiring without launching the app.

The guards verify:

- Shell routes POS Checkout through the cashier shift gate.
- Checkout receives AppServices checkout product search.
- Checkout receives PosCheckoutApplication through CheckoutCompletionBoundary.
- Checkout search, add item, cash payment, dry run, submit guard, and complete sale buttons call CheckoutPresenter.
- Complete Sale requires confirmation and renders completion result.
- Complete Sale button stays controlled by CheckoutViewModel.CanCompleteSale.
- Completed sales expose Start New Sale behavior.
- Product Management opens through ProductManagementPresenter.
- Add Product opens ProductCreateForm through the same presenter.
- Product create loads catalog options, builds ProductCreateRequest, and saves through CreateProductAsync.
- Product presenter writes through ProductWriteRepository.

## Boundary

- No app run.
- No visible UI redesign.
- No database schema changes.
- No business logic changes.
- No LAN, transport, or two-machine behavior.
- This guards the UI wiring before final manual app launch.