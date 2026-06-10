# 10 - UI Tasks For Paras

## Task Group A: Understand existing UI

Open:

    src/Salryn.App.WinForms/Features/Checkout/CheckoutShellForm.cs

Look for:

    CreateHeader()
    CreateProductInputSection()
    CreateCartSection()
    CreateRightPanel()
    CreateTotalsSection()
    CreatePaymentSection()
    CreateActionsSection()
    CreateStatusSection()

## Task Group B: Improve appearance only

Allowed improvements:

- Better spacing
- Better layout proportions
- Better label names
- Better placeholder text
- Better section grouping
- Better cart grid columns
- Better status area
- Better visual hierarchy
- Better form title/header
- Better action button arrangement

## Task Group C: Prepare render method later

Future pattern:

    private readonly CheckoutPresenter _presenter = new();

    private void Render(CheckoutViewModel state)
    {
        _statusLabel.Text = state.StatusMessage;
    }

Do not force this yet unless the current phase asks for it.

## Task Group D: Cart grid plan

Future cart columns:

    Product
    Barcode
    Qty
    Unit
    Price
    Discount
    Total

Use CheckoutLineViewModel fields:

    ProductName
    Barcode
    QuantityText
    UnitName
    UnitPriceText
    DiscountText
    LineTotalText

## Task Group E: Totals panel plan

Use CheckoutTotalsViewModel:

    SubtotalText
    SaleDiscountText
    TaxText
    TotalText
    AmountPaidText
    ChangeText
    BalanceDueText

## Task Group F: Buttons

Buttons should follow state flags:

    CanAddPreparedLine
    CanRemoveSelectedLine
    CanAddCashPayment
    CanClearPayments
    CanCompleteSale

Important:

    CanCompleteSale is false for now.

Do not manually enable Complete Sale.
