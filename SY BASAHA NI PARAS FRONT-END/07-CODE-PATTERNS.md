# 07 - Code Patterns

Namespace:

    namespace Salryn.App.WinForms.Features.Checkout;

This is the address of the file in the project.

Class:

    public sealed class CheckoutPresenter

This means CheckoutPresenter is a class, usable by other files, and not meant to be inherited.

Record:

    public sealed record CheckoutViewModel(...)

A record is good for data state. It keeps viewmodels clean.

Nullable:

    string? ErrorMessage

The question mark means it can be null.

Guid:

    Guid ProductId

Guid is a unique ID. Products, sales, users, and cart lines use IDs like this.

Object initializer:

    TextBox input = new()
    {
        Dock = DockStyle.Fill,
        Enabled = false
    };

This creates a TextBox and sets properties directly.

Mapping:

    transaction.Lines
        .Select(MapLine)
        .ToList();

This means each core draft line is converted into a UI line.

Result check:

    if (result.IsFailure)
    {
        return FailFrom(result);
    }

Never ignore Result. If it fails, return the error state.

Render pattern later:

    private void Render(CheckoutViewModel state)
    {
        _statusLabel.Text = state.StatusMessage;
        _totalLabel.Text = state.Totals.TotalText;
        _completeSaleButton.Enabled = state.CanCompleteSale;
    }

The UI should render what the state says.
