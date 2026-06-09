using Salryn.Core.Models;
using Salryn.Core.Repositories;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class Phase19InventoryWriteRepositoryCompileChecks
{
    public static void Compile()
    {
        DbPaths paths = DbPaths.CreateDevelopment("compile-only");
        SqliteConnectionFactory connectionFactory = new(paths);
        SqliteRepositoryFactory factory = new(connectionFactory);

        IInventoryWriteRepository repository = factory.CreateInventoryWriteRepository();

        Quantity quantity = Quantity.FromX1000(1000).Value;

        StockMovement movement = new()
        {
            Id = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            MovementType = StockMovementType.StockIn,
            QuantityChange = quantity,
            CostPrice = Money.FromCents(1000),
            ReferenceType = "COMPILE",
            ReferenceId = Guid.NewGuid(),
            Reason = "Compile check only",
            UserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        _ = repository;
        _ = movement;
    }
}
