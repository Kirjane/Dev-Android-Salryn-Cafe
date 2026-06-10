# 12 - Ayaw Hilabti

Ayaw hilabti ang database migrations unless instructed:

    src/Salryn.Infrastructure/Data/Migrations/

Ayaw pag-add og SQL file:

    *.sql

Ayaw pag-commit og database file:

    *.db
    *.sqlite

Ayaw gamita sa UI:

    SqliteConnection
    SqliteCommand
    SqliteDataReader

Ayaw tawaga sa UI for now:

    AddCompletedSaleAsync
    CompleteSaleAsync
    MarkVoidedAsync

Ayaw pag-create og stock movement sa UI:

    new StockMovement
    StockMovementType.Sale
    StockMovementType.VoidSale

Avoid editing these unless assigned:

    src/Salryn.Core/Models/
    src/Salryn.Core/ValueObjects/
    src/Salryn.Core/Repositories/
    src/Salryn.Core/Services/PosCheckoutService.cs
    src/Salryn.Core/Services/PosCheckoutApplicationService.cs

Ayaw i-enable ang Complete Sale manually.

Wrong:

    completeSaleButton.Enabled = true;

Later, kung allowed na:

    completeSaleButton.Enabled = state.CanCompleteSale;

Pero karon, CanCompleteSale stays false.
