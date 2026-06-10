# 08 - Rules sa Front-end

## 1. Keep the form thin

The form should not own business rules.

Bad:

    if (stock < qty)
    {
        MessageBox.Show("No stock");
    }

Better:

    CheckoutPresenterOperationResult result = _presenter.AddPreparedLine(input);
    Render(result.State);

## 2. No SQLite in the form

Do not put these in UI files:

    SqliteConnection
    SqliteCommand
    SqliteDataReader

## 3. No sale completion yet

Do not call these from UI:

    CompleteSaleAsync
    AddCompletedSaleAsync

## 4. No stock movement from UI

Do not create these from UI:

    StockMovement
    StockMovementType.Sale
    StockMovementType.VoidSale

## 5. Do not compute final totals in the form

Use the state:

    state.Totals.TotalText

Do not make the form become the calculator of final sale truth.

## 6. Name controls properly

Good names:

    _productInputTextBox
    _cartGrid
    _subtotalValueLabel
    _totalValueLabel
    _cashAmountTextBox
    _statusLabel

Avoid:

    textBox1
    button2
    label9

## 7. Keep UI sections separated

Good:

    CreateHeader()
    CreateProductInputSection()
    CreateCartSection()
    CreateTotalsSection()
    CreatePaymentSection()
    CreateActionsSection()
    Render(...)

Avoid one huge method with everything mixed.

## 8. Button click should be short

Good:

    private void clearButton_Click(object? sender, EventArgs e)
    {
        Render(_presenter.ClearDraft().State);
    }

If a button click becomes 100 lines, something is wrong.

## 9. Smoke checks are required

Before saying okay, run build and smoke.

## 10. Ask before touching backend

If the task needs database write, sale write, stock write, receipt print, or migration, stop first.
