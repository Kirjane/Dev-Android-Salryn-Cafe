# 06 - Checkout UI Files Explained

## CheckoutShellForm.cs

Path:

    src/Salryn.App.WinForms/Features/Checkout/CheckoutShellForm.cs

Purpose:

This is the actual WinForms screen. It builds the UI using C# code instead of Designer.

Important class:

    public sealed class CheckoutShellForm : Form

Meaning:

- public = accessible
- sealed = cannot be inherited
- class = object type
- : Form = it is a WinForms window

Constructor sets:

- Window title
- Center screen start
- Minimum size
- Font
- Background color
- Calls BuildShell()

BuildShell creates the screen layout.

It uses TableLayoutPanel.

Bisaya:

TableLayoutPanel mura siyag grid layout. Naa siyay rows and columns para dili gubot ang positioning.

UI sections:

    CreateHeader()
    CreateProductInputSection()
    CreateCartSection()
    CreateRightPanel()
    CreateStatusSection()

Each method returns a Control.

Meaning:

Every part of the UI is separated. Mas limpyo kaysa usa ka giant method.

Product section currently has disabled input because product search is not wired yet.

Cart section uses DataGridView and should stay read-only for now.

Complete Sale is disabled. This is correct. Do not enable it yet.

## CheckoutViewModels.cs

Path:

    src/Salryn.App.WinForms/Features/Checkout/CheckoutViewModels.cs

Purpose:

Defines UI data shapes.

Records:

    CheckoutLineViewModel
    CheckoutTotalsViewModel
    CheckoutViewModel
    CheckoutPresenterOperationResult

CheckoutLineViewModel represents one cart row.

Contains:

    LineId
    ProductId
    ProductName
    Barcode
    UnitName
    QuantityText
    UnitPriceText
    DiscountText
    GrossText
    LineTotalText
    TrackStock

CheckoutTotalsViewModel represents totals area.

Contains:

    SubtotalText
    SaleDiscountText
    TaxText
    TotalText
    AmountPaidText
    ChangeText
    BalanceDueText

CheckoutViewModel is the whole screen state.

Contains:

    Product input
    Product result text
    Lines
    Totals
    Payment count
    Cash amount
    Button enabled flags
    Status message
    Error info

Important:

    CanCompleteSale

Currently this must stay false.

## CheckoutPresenter.cs

Path:

    src/Salryn.App.WinForms/Features/Checkout/CheckoutPresenter.cs

Purpose:

Presenter controls UI state and talks to Core draft builder.

It uses:

    private PosCheckoutDraftTransactionBuilder _builder = new();

Meaning:

The presenter wraps the draft/cart engine.

SetProductInputText trims input and stores it, but does not search database yet.

AddPreparedLine adds an already-prepared line to the draft.

AddCashPayment adds cash payment to draft state only.

RefreshState maps Core transaction state into UI viewmodel.

Failure handling stores error into State instead of crashing UI.
