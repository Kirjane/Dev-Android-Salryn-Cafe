using Salryn.Core.Models;
using Salryn.Core.Repositories;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Data.Repositories.Sales;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class Phase22SaleWriteRepositoryCompileChecks
{
    public static void Compile()
    {
        DbPaths paths = DbPaths.CreateDevelopment("compile-only");
        SqliteConnectionFactory connectionFactory = new(paths);
        SqliteRepositoryFactory factory = new(connectionFactory);

        ISaleWriteRepository repository = factory.CreateSaleWriteRepository();

        Guid saleId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();
        Guid cashierUserId = Guid.NewGuid();

        Sale sale = new()
        {
            Id = saleId,
            ReceiptNumber = "SLRN-2026-000001",
            SaleDate = DateTimeOffset.UtcNow,
            CashierUserId = cashierUserId,
            CashierSessionId = null,
            Subtotal = Money.FromCents(10000),
            Discount = Money.Zero,
            Tax = Money.Zero,
            Total = Money.FromCents(10000),
            AmountPaid = Money.FromCents(10000),
            Change = Money.Zero,
            Status = SaleStatus.Completed,
            ReceiptSnapshot = "Compile check receipt.",
            CreatedAt = DateTimeOffset.UtcNow
        };

        SaleItem item = new()
        {
            Id = Guid.NewGuid(),
            SaleId = saleId,
            ProductId = productId,
            ProductNameSnapshot = "Compile Product",
            BarcodeSnapshot = "4800000000000",
            UnitNameSnapshot = "Piece",
            Quantity = Quantity.FromX1000(1000).Value,
            UnitPrice = Money.FromCents(10000),
            CostPrice = Money.FromCents(7000),
            Discount = Money.Zero,
            LineTotal = Money.FromCents(10000)
        };

        Payment payment = new()
        {
            Id = Guid.NewGuid(),
            SaleId = saleId,
            Method = PaymentMethod.Cash,
            Amount = Money.FromCents(10000),
            PaidAt = DateTimeOffset.UtcNow
        };

        StockMovement stockMovement = new()
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            MovementType = StockMovementType.Sale,
            QuantityChange = Quantity.FromX1000(1000).Value,
            CostPrice = Money.FromCents(7000),
            ReferenceType = "SALE",
            ReferenceId = saleId,
            Reason = "Compile check sale movement",
            UserId = cashierUserId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        SqliteSaleWriteRepository concreteRepository = new(connectionFactory);

        _ = repository;
        _ = concreteRepository;
        _ = sale;
        _ = item;
        _ = payment;
        _ = stockMovement;
    }
}
