# 00 - Basaha First

Sy, ayaw ni tan-awa nga murag ordinary WinForms app nga pwede ra ibutang tanan logic sa button click.

Layered ni ang SALRYN.

Main layers:

    Salryn.App.WinForms
        UI side. Forms, buttons, labels, grids, presenter, viewmodel.

    Salryn.Core
        Business rules, models, value objects, repository contracts, services.

    Salryn.Infrastructure
        SQLite, migrations, repository implementations, database setup.

    Salryn.Tools.InfrastructureSmoke
        Safety checks. Mao ni gamiton para mahibaw-an kung naa bay naguba.

Pinaka-importante nga front-end files karon:

    src/Salryn.App.WinForms/Features/Checkout/CheckoutShellForm.cs
    src/Salryn.App.WinForms/Features/Checkout/CheckoutPresenter.cs
    src/Salryn.App.WinForms/Features/Checkout/CheckoutViewModels.cs

Pasabot ana:

    CheckoutShellForm
        actual screen

    CheckoutPresenter
        tig-handle sa UI actions ug state

    CheckoutViewModel
        data nga ready ipakita sa screen

Sakto nga flow:

    Form -> Presenter -> ViewModel -> Core

Sayop nga flow:

    Form -> SQLite directly

Ayaw ani:

    private void completeSaleButton_Click(object sender, EventArgs e)
    {
        using SqliteConnection connection = new(...);
    }

Mas tarong ani:

    private void clearButton_Click(object sender, EventArgs e)
    {
        CheckoutPresenterOperationResult result = _presenter.ClearDraft();
        Render(result.State);
    }

Front-end work karon:

- tarungon ang layout
- tarungon ang labels
- limpyo nga grid
- klaro nga totals panel
- klaro nga payment area
- andam nga product search area
- ayaw pa i-complete ang sale
- ayaw pa i-print receipt
- ayaw pa i-write ang stock movement
