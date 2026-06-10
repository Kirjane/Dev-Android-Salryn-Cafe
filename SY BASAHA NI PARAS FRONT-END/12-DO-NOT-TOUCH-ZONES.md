# 12 - Do Not Touch Zones

Frontend developer must respect these boundaries.

Do not touch database migrations:

    src/Salryn.Infrastructure/Data/Migrations/

Do not add SQL files:

    *.sql

Do not add database files:

    *.db
    *.sqlite

Do not directly use SQLite in UI:

    SqliteConnection
    SqliteCommand
    SqliteDataReader

Do not call sale write methods from UI:

    AddCompletedSaleAsync
    CompleteSaleAsync
    MarkVoidedAsync

Do not create stock movements from UI:

    new StockMovement
    StockMovementType.Sale
    StockMovementType.VoidSale

Avoid touching core rules casually:

    src/Salryn.Core/Models/
    src/Salryn.Core/ValueObjects/
    src/Salryn.Core/Repositories/
    src/Salryn.Core/Services/PosCheckoutService.cs
    src/Salryn.Core/Services/PosCheckoutApplicationService.cs

Do not bypass presenter.

Bad:

    Form directly computes everything and writes data.

Good:

    CheckoutPresenterOperationResult result = _presenter.SomeMethod();
    Render(result.State);

Do not enable Complete Sale manually.

Wrong:

    completeSaleButton.Enabled = true;

Correct later:

    completeSaleButton.Enabled = state.CanCompleteSale;

But currently CanCompleteSale must stay false.
