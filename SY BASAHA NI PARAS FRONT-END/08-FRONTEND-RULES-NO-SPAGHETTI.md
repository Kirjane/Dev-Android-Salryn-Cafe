# 08 - Frontend Rules: No Spaghetti

## Rule 1: Forms should render, not decide business rules

Bad:

    if (stock < qty)
    {
        MessageBox.Show("No stock");
    }

Better:

    CheckoutPresenterOperationResult result = _presenter.AddPreparedLine(input);
    Render(result.State);

Let Core/Presenter handle rules.

## Rule 2: No direct SQLite in forms

Forbidden in UI files:

    SqliteConnection
    SqliteCommand
    SqliteDataReader

## Rule 3: No sale completion yet

Forbidden for now:

    CompleteSaleAsync
    AddCompletedSaleAsync

Sale completion requires cashier session and full validation. Later phase pa na.

## Rule 4: No stock movement from UI

Forbidden for now:

    StockMovement
    StockMovementType.Sale
    StockMovementType.VoidSale

Inventory movement is backend territory.

## Rule 5: UI should not compute final truth

Use:

    state.Totals.TotalText

Do not recompute final sale totals inside the form.

## Rule 6: Keep controls named clearly

Good names:

    _productInputTextBox
    _cartGrid
    _subtotalValueLabel
    _totalValueLabel
    _cashAmountTextBox
    _statusLabel

Bad names:

    textBox1
    button2
    label9

## Rule 7: Keep sections separated

Good:

    CreateHeader()
    CreateProductInputSection()
    CreateCartSection()
    CreateTotalsSection()
    CreatePaymentSection()
    CreateActionsSection()
    Render(...)

Bad:

    One giant constructor with everything mixed.

## Rule 8: Button click should be tiny

Good:

    private void clearButton_Click(object? sender, EventArgs e)
    {
        Render(_presenter.ClearDraft().State);
    }

Bad:

    private void clearButton_Click(...)
    {
        // 200 lines of logic
    }

## Rule 9: Every phase must pass smoke

Before commit:

    dotnet build "Salryn.sln"

    dotnet run --project "tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj" -- pos-checkout-ui-presenter-viewmodel

    dotnet run --project "tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj" -- full

## Rule 10: Ask before touching backend

If a UI task needs repository, sale write, stock write, receipt print, or schema change, stop first.
