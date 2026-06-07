using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Data.Repositories.Inventory;
using Salryn.Infrastructure.Data.Repositories.Sales;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class Phase13SalesInventoryReadRepositoryCompileChecks
{
    public static void Run()
    {
        DbPaths paths = DbPaths.CreateDevelopment("C:\\SalrynDevCheck");

        SqliteConnectionFactory connectionFactory = new(paths);

        SqliteRepositoryFactory repositoryFactory = new(connectionFactory);

        ISaleReadRepository saleRepository =
            repositoryFactory.CreateSaleReadRepository();

        IInventoryReadRepository inventoryRepository =
            repositoryFactory.CreateInventoryReadRepository();

        SqliteSaleReadRepository concreteSaleRepository = new(connectionFactory);
        SqliteInventoryReadRepository concreteInventoryRepository = new(connectionFactory);

        StockMovementReadModel movement = new(
            Id: Guid.NewGuid(),
            ProductId: Guid.NewGuid(),
            ProductName: "Sample Product",
            MovementType: StockMovementType.Sale,
            QuantityChangeX1000: -1000,
            QuantityChangeUnits: -1m,
            CostPrice: null,
            ReferenceType: "SALE",
            ReferenceId: "sample",
            Reason: null,
            UserDisplayName: "Cashier",
            CreatedAt: DateTimeOffset.UtcNow);

        if (saleRepository is null)
        {
            throw new InvalidOperationException("Sale repository factory check failed.");
        }

        if (inventoryRepository is null)
        {
            throw new InvalidOperationException("Inventory repository factory check failed.");
        }

        if (movement.QuantityChangeX1000 >= 0)
        {
            throw new InvalidOperationException("Signed stock movement read model check failed.");
        }

        _ = concreteSaleRepository;
        _ = concreteInventoryRepository;
    }
}
