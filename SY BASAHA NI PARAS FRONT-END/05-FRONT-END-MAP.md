# 05 - Front-end Map

Current front-end flow:

    Program.cs
        -> CheckoutShellForm
            -> CheckoutPresenter
                -> CheckoutViewModel
                    -> Core draft builder

CheckoutShellForm is the screen.

It has sections for:

- header
- product input
- cart grid
- totals
- payment
- action buttons
- status

CheckoutPresenter is the UI state controller.

Current methods include:

    Initialize()
    SetCashierUserId(...)
    SetReceiptNumber(...)
    SetProductInputText(...)
    AddPreparedLine(...)
    RemoveLine(...)
    SetCashAmountCents(...)
    SetCashAmountText(...)
    AddCashPayment()
    ClearPayments()
    ClearDraft()
    RefreshState()

CheckoutViewModel is the full screen state.

It has:

- cashier ID
- receipt number
- product input text
- product result text
- cart lines
- totals
- payment count
- cash amount
- button flags
- status message
- error info

Clean form behavior:

    call presenter
    get state
    render state

Avoid:

    compute final sale totals inside form
    write database inside form
    create stock movements inside form
    complete sale inside form
