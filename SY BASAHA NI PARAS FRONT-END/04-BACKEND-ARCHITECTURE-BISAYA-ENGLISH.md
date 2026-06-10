# 04 - Backend Architecture Bisaya-English

Big picture:

    Core = rules
    Infrastructure = database
    WinForms = UI
    Smoke Tool = verification

## Core

Core does not save to database.

Core defines rules and shapes.

Example:

    public sealed record PosCheckoutDraftLineInput(...)

This is a data shape. It says: Mao ni ang needed input para makadugang og line sa draft cart.

## Result/Error pattern

Most services return Result or Result<T>.

Example:

    Result result = _builder.SetReceiptNumber(trimmed);

    if (result.IsFailure)
    {
        return FailFrom(result);
    }

Meaning:

- If success, continue.
- If failure, do not crash UI.
- Convert backend error into UI-safe error state.

Bisaya:

Dili ta basta mag-exception for normal validation. Mogamit ta og Result para klaro ang failure reason.

## Value objects

Examples:

    Money
    Quantity
    Barcode

Purpose:

- Avoid raw messy numbers everywhere.
- Enforce valid values.
- Make business meaning clear.

Money example:

    Money.FromCents(1500)

Means PHP 15.00.

Quantity example:

    Quantity.FromX1000(2000)

Means quantity 2.000.

Why x1000?

Because POS can have decimal quantities:

    1 pc
    0.5 kg
    2.25 liters

Stored as integer to avoid decimal bugs.

## Repository interfaces

Core defines interfaces like:

    IProductReadRepository
    IProductWriteRepository
    ISaleWriteRepository

Interface means contract.

Bisaya:

Murag promise: kinsa man ang mo-implement ani, dapat naa ni nga methods.

## Infrastructure repositories

Infrastructure implements contracts using SQLite.

Example:

    IProductReadRepository
        implemented by
    SqliteProductReadRepository

## Sale completion path later

    CheckoutPresenter
        -> PosCheckoutApplicationService
            -> PosCheckoutService
                -> ISaleWriteRepository
                    -> SQLite

But this is not wired yet in UI.

## Current frontend safe path

    CheckoutShellForm
        -> CheckoutPresenter
            -> PosCheckoutDraftTransactionBuilder

This only prepares draft/cart/totals state.

It does not complete sale.
