# 04 - Backend Map

The backend is already prepared enough for safe UI work.

Simple map:

    Core
        rules and contracts

    Infrastructure
        actual SQLite work

    App.WinForms
        UI and presentation state

    Smoke Tool
        verification

Money is stored in cents:

    100 = PHP 1.00
    1500 = PHP 15.00
    5000 = PHP 50.00

Quantity is stored in x1000:

    1000 = 1
    2000 = 2
    500 = 0.5
    1250 = 1.25

This avoids decimal bugs sa POS.

Most service calls use Result or Result<T>.

Example:

    Result result = _builder.SetReceiptNumber(receiptNumber);

    if (result.IsFailure)
    {
        return FailFrom(result);
    }

Meaning: kung naay validation error, dili mo-crash ang UI. Ibalik siya as clean error state.

Repositories are database contracts.

Examples:

    IProductReadRepository
    IProductWriteRepository
    ISaleWriteRepository

Actual SQLite code lives in Infrastructure.

Later sale completion path:

    CheckoutPresenter
        -> PosCheckoutApplicationService
            -> PosCheckoutService
                -> Sale write repository
                    -> SQLite

But dili pa na wired sa UI.

Current safe path:

    CheckoutShellForm
        -> CheckoutPresenter
            -> PosCheckoutDraftTransactionBuilder

For now, draft/cart/totals/payments state pa. Dili pa final sale save.
