# 07 - Code Syntax Explained

## Namespace

    namespace Salryn.App.WinForms.Features.Checkout;

Meaning:

This file belongs to the checkout feature area of the WinForms app.

Bisaya:

Murag address sa class.

## Using

    using Salryn.Core.Services;

Meaning:

This file can use classes from Salryn.Core.Services.

## Class

    public sealed class CheckoutPresenter

Meaning:

- public = other files can use it
- sealed = cannot be inherited
- class = object blueprint
- CheckoutPresenter = name

## Record

    public sealed record CheckoutViewModel(...)

Record is a data container.

Good for immutable state.

Example:

    State = State with
    {
        HasError = true
    };

with creates a copy with changed values.

## Nullable

    string? ErrorMessage

The ? means it can be null.

## Guid

    Guid ProductId

Guid is a unique ID.

Used for products, lines, sales, users.

## Long cents

    long TotalCents

Money is stored as cents.

Examples:

    100 = PHP 1.00
    1500 = PHP 15.00
    5000 = PHP 50.00

## Quantity x1000

    long QuantityX1000

Quantity is stored as thousandths.

Examples:

    1000 = 1
    2000 = 2
    500 = 0.5
    1250 = 1.25

## Method

    public CheckoutPresenterOperationResult ClearDraft()
    {
        return Initialize();
    }

A method is a function inside a class.

## Object initializer

    TextBox input = new()
    {
        Dock = DockStyle.Fill,
        Enabled = false
    };

Create a new TextBox and set properties.

## Select mapping

    transaction.Lines
        .Select(MapLine)
        .ToList();

For each core line, convert it to UI line viewmodel.

Bisaya:

Tagsa-tagsa nga line sa draft, himuon og line nga ready ipakita sa UI.

## Result pattern

    if (result.IsFailure)
    {
        return FailFrom(result);
    }

If operation fails, convert it to UI error.

Never ignore Result.
