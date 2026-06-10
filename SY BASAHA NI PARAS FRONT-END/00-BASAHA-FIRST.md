# 00 - BASAHA FIRST

Paras, basaha ni una before touching the frontend.

This is not a random WinForms app. This is a layered POS system.

SALRYN has these layers:

    Salryn.App.WinForms
        Frontend / UI

    Salryn.Core
        Business rules, models, value objects, service contracts

    Salryn.Infrastructure
        SQLite database, repositories, migrations, initialization

    Salryn.Tools.InfrastructureSmoke
        Smoke checks / safety verification

Current UI files:

    src/Salryn.App.WinForms/Features/Checkout/CheckoutShellForm.cs
    src/Salryn.App.WinForms/Features/Checkout/CheckoutPresenter.cs
    src/Salryn.App.WinForms/Features/Checkout/CheckoutViewModels.cs

Meaning ani:

- CheckoutShellForm is the screen.
- CheckoutPresenter is the bridge/controller for UI state.
- CheckoutViewModel is the data shape nga ipakita sa screen.

Good direction:

    Form -> Presenter -> ViewModel -> Core

Bad direction:

    Form -> SQLite directly

Ayaw ibutang ang business logic sa button click.

Bad pattern:

    private void completeSaleButton_Click(object sender, EventArgs e)
    {
        using SqliteConnection connection = new(...);
    }

Correct direction:

    private void addButton_Click(object sender, EventArgs e)
    {
        CheckoutPresenterOperationResult result =
            _presenter.AddPreparedLine(lineInput);

        Render(result.State);
    }

Current frontend mission:

- Make the interface clean.
- Prepare the UI for future wiring.
- Do not force sale completion.
- Do not force receipt printing.
- Do not force inventory movement.
