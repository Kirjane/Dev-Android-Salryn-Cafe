# 03 - Repo Map

Main folders:

    src/
        Salryn.Core/
        Salryn.Infrastructure/
        Salryn.App.WinForms/

    tests/
        Salryn.Tests/

    tools/
        Salryn.Tools.InfrastructureSmoke/

    00-HANDOFF/

    SY BASAHA NI PARAS FRONT-END/

Meaning:

    Salryn.Core
        rules, models, value objects, repository contracts, services

    Salryn.Infrastructure
        SQLite implementation, migrations, repositories, database setup

    Salryn.App.WinForms
        Windows UI, forms, presenters, viewmodels

    Salryn.Tools.InfrastructureSmoke
        smoke checks, not the real app

    00-HANDOFF
        phase notes and audit proof

    SY BASAHA NI PARAS FRONT-END
        this guide

Front-end files to read first:

    src/Salryn.App.WinForms/Program.cs
    src/Salryn.App.WinForms/Features/Checkout/CheckoutShellForm.cs
    src/Salryn.App.WinForms/Features/Checkout/CheckoutPresenter.cs
    src/Salryn.App.WinForms/Features/Checkout/CheckoutViewModels.cs

Backend checkout files to understand, but not casually edit:

    src/Salryn.Core/Services/PosCheckoutDraftTransactionBuilder.cs
    src/Salryn.Core/Services/PosCheckoutProductPickerBoundary.cs
    src/Salryn.Core/Services/PosCheckoutApplicationService.cs
    src/Salryn.Core/Services/PosCheckoutService.cs
    src/Salryn.Core/Services/CashierSessionService.cs
