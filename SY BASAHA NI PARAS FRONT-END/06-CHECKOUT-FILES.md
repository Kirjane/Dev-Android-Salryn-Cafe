# 06 - Checkout Files

## CheckoutShellForm.cs

Path:

    src/Salryn.App.WinForms/Features/Checkout/CheckoutShellForm.cs

This is the screen.

It builds the UI using C# code.

Class:

    public sealed class CheckoutShellForm : Form

Meaning:

    public
        usable by other files

    sealed
        not meant to be inherited

    Form
        WinForms window

Main section methods:

    BuildShell
    CreateHeader
    CreateProductInputSection
    CreateCartSection
    CreateRightPanel
    CreateTotalsSection
    CreatePaymentSection
    CreateActionsSection
    CreateStatusSection

This structure is okay. Keep it sectioned. Do not turn it into one huge constructor.

Product input is disabled for now because real product lookup is not wired yet.

Cart grid is read-only for now.

Complete Sale is disabled for now.

## CheckoutViewModels.cs

Path:

    src/Salryn.App.WinForms/Features/Checkout/CheckoutViewModels.cs

This file defines the data that the screen can display.

Important records:

    CheckoutLineViewModel
    CheckoutTotalsViewModel
    CheckoutViewModel
    CheckoutPresenterOperationResult

CheckoutLineViewModel is one cart row.

Useful fields:

    ProductName
    Barcode
    UnitName
    QuantityText
    UnitPriceText
    DiscountText
    LineTotalText
    TrackStock

CheckoutTotalsViewModel is for totals area.

Useful fields:

    SubtotalText
    SaleDiscountText
    TaxText
    TotalText
    AmountPaidText
    ChangeText
    BalanceDueText

CheckoutViewModel is the whole screen state.

Important flags:

    CanAddPreparedLine
    CanRemoveSelectedLine
    CanAddCashPayment
    CanClearPayments
    CanCompleteSale

CanCompleteSale must stay false until the proper phase.

## CheckoutPresenter.cs

Path:

    src/Salryn.App.WinForms/Features/Checkout/CheckoutPresenter.cs

This file controls UI state.

It wraps:

    PosCheckoutDraftTransactionBuilder

That core builder handles draft lines, totals, and payments.

SetProductInputText trims input and stores it, but does not search the database yet.

AddPreparedLine adds an already-prepared line to the draft.

AddCashPayment adds cash payment to draft state only. It does not save a sale.

RefreshState converts core state into UI state.

Failure handling stores error into State instead of crashing UI.
