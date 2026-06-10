# 05 - Frontend Architecture

Frontend is now in controlled UI territory.

Current frontend flow:

    Program.cs
        -> CheckoutShellForm
            -> CheckoutPresenter
                -> CheckoutViewModel
                    -> Core Draft Builder

## Program.cs

Current app launches:

    Application.Run(new Salryn.App.WinForms.Features.Checkout.CheckoutShellForm());

Meaning the app opens the checkout UI shell first.

## CheckoutShellForm

This is the form/window.

It creates visual controls:

- Header
- Product input area
- Cart grid
- Totals area
- Payment area
- Action buttons
- Status area

It is currently mostly shell-only.

## CheckoutPresenter

Presenter is the frontend controller.

It owns UI state operations like:

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

## CheckoutViewModel

ViewModel is the shape sent to UI.

It contains:

    CashierUserId
    ReceiptNumber
    ProductInputText
    ProductResultText
    Lines
    Totals
    PaymentCount
    CashAmountText
    Button enable flags
    StatusMessage
    Error fields

## Why Presenter/ViewModel?

So the form does not become spaghetti.

Bad:

    button.Click += (_, _) =>
    {
        // compute totals
        // validate
        // call DB
        // update grid
        // print receipt
    };

Good:

    button.Click += (_, _) =>
    {
        CheckoutPresenterOperationResult result = _presenter.AddCashPayment();
        Render(result.State);
    };

The form should mostly call presenter and render state.

## UI rendering principle

Later, form should have a method like:

    private void Render(CheckoutViewModel state)
    {
        subtotalLabel.Text = state.Totals.SubtotalText;
        totalLabel.Text = state.Totals.TotalText;
        statusLabel.Text = state.StatusMessage;
    }

Bisaya:

Ang UI dapat mo-display ra sa state. Dili siya ang magbuot sa business rules.
