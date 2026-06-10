# 15 - Current Code Map

## CheckoutShellForm.cs

Role:

    Actual WinForms screen.

Important methods:

    BuildShell
    CreateHeader
    CreateProductInputSection
    CreateCartSection
    CreateRightPanel
    CreateTotalsSection
    CreatePaymentSection
    CreateActionsSection
    CreateStatusSection

## CheckoutViewModels.cs

Role:

    UI state shapes.

Records:

    CheckoutLineViewModel
    CheckoutTotalsViewModel
    CheckoutViewModel
    CheckoutPresenterOperationResult

## CheckoutPresenter.cs

Role:

    UI state controller.

Important methods:

    Initialize
    SetCashierUserId
    SetReceiptNumber
    SetProductInputText
    AddPreparedLine
    SelectLine
    RemoveLine
    SetCashAmountCents
    SetCashAmountText
    AddCashPayment
    ClearPayments
    ClearDraft
    RefreshState

## PosCheckoutDraftTransactionBuilder.cs

Role:

    Core draft/cart engine.

Used by presenter.

It handles:

    Lines
    Totals
    Payments
    Draft transaction state
    Build application request later

## PosCheckoutProductPickerBoundary.cs

Role:

    Converts product read models into checkout draft line input.

Used later by Phase 41 product search flow.

## PosCheckoutApplicationService.cs

Role:

    Builds real sale/items/payments/stock movements for completion.

Not wired into UI yet.

## PosCheckoutService.cs

Role:

    Requires open cashier session before sale completion.

Not wired into UI yet.
