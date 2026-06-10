# 10 - Tasks sa Front-end

First file to open:

    src/Salryn.App.WinForms/Features/Checkout/CheckoutShellForm.cs

Look at these methods:

    CreateHeader()
    CreateProductInputSection()
    CreateCartSection()
    CreateRightPanel()
    CreateTotalsSection()
    CreatePaymentSection()
    CreateActionsSection()
    CreateStatusSection()

Safe work right now:

- improve spacing
- improve alignment
- improve labels
- improve grid columns
- improve total panel
- improve payment panel
- improve button placement
- make status area clearer
- prepare product search result area
- keep Complete Sale disabled

Cart grid should eventually show:

    Product
    Barcode
    Qty
    Unit
    Price
    Discount
    Total

Use these fields later:

    ProductName
    Barcode
    QuantityText
    UnitName
    UnitPriceText
    DiscountText
    LineTotalText

Totals panel should use:

    SubtotalText
    SaleDiscountText
    TaxText
    TotalText
    AmountPaidText
    ChangeText
    BalanceDueText

Buttons should follow viewmodel flags:

    CanAddPreparedLine
    CanRemoveSelectedLine
    CanAddCashPayment
    CanClearPayments
    CanCompleteSale

Important:

    CanCompleteSale must stay false for now.
