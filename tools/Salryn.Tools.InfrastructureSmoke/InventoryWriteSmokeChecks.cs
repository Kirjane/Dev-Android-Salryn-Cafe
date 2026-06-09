using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Paths;

internal static class InventoryWriteSmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== INVENTORY WRITE SMOKE CHECKS =====");

        EnsureDatabaseReady(projectRoot);

        DbPaths paths = DbPaths.CreateDevelopment(projectRoot);
        SqliteConnectionFactory connectionFactory = new(paths);
        SqliteRepositoryFactory repositoryFactory = new(connectionFactory);

        IProductWriteRepository productWriteRepository =
            repositoryFactory.CreateProductWriteRepository();

        IInventoryWriteRepository inventoryWriteRepository =
            repositoryFactory.CreateInventoryWriteRepository();

        ICatalogReadRepository catalogRepository =
            repositoryFactory.CreateCatalogReadRepository();

        IUserReadRepository userRepository =
            repositoryFactory.CreateUserReadRepository();

        SeedIds seedIds = GetSeedIds(catalogRepository);
        Guid adminUserId = GetSmokeAdminUserId(userRepository);

        Product product = CreateSmokeProduct(seedIds);

        RequireSuccess(
            productWriteRepository.AddAsync(product).GetAwaiter().GetResult(),
            "InventorySmoke.AddProduct");

        VerifyProductStock(projectRoot, product.Id, expectedStockX1000: 0);

        StockMovement stockIn = CreateMovement(
            product.Id,
            StockMovementType.StockIn,
            QuantityX1000(5000),
            adminUserId,
            "INV-SMOKE",
            "Initial stock-in");

        RequireSuccess(
            inventoryWriteRepository.AddStockMovementAsync(stockIn).GetAwaiter().GetResult(),
            "InventorySmoke.StockIn");

        VerifyProductStock(projectRoot, product.Id, expectedStockX1000: 5000);
        VerifyMovementExists(projectRoot, stockIn.Id, product.Id, expectedQtyChangeX1000: 5000);

        StockMovement sale = CreateMovement(
            product.Id,
            StockMovementType.Sale,
            QuantityX1000(2000),
            adminUserId,
            "INV-SMOKE",
            "Sale stock-out");

        RequireSuccess(
            inventoryWriteRepository.AddStockMovementAsync(sale).GetAwaiter().GetResult(),
            "InventorySmoke.Sale");

        VerifyProductStock(projectRoot, product.Id, expectedStockX1000: 3000);
        VerifyMovementExists(projectRoot, sale.Id, product.Id, expectedQtyChangeX1000: -2000);

        Result negativeStockResult =
            inventoryWriteRepository.AddStockMovementAsync(
                CreateMovement(
                    product.Id,
                    StockMovementType.AdjustmentRemove,
                    QuantityX1000(999999),
                    adminUserId,
                    "INV-SMOKE",
                    "Should fail negative stock")).GetAwaiter().GetResult();

        ExpectFailure(
            negativeStockResult,
            "InventorySmoke.NegativeStock",
            ErrorType.Conflict,
            "Inventory.NegativeStockNotAllowed");

        VerifyProductStock(projectRoot, product.Id, expectedStockX1000: 3000);

        long stockBeforeBatch = GetProductStock(projectRoot, product.Id);
        int movementCountBeforeBatch = GetMovementCount(projectRoot, product.Id);

        StockMovement batchOne = CreateMovement(
            product.Id,
            StockMovementType.StockIn,
            QuantityX1000(1000),
            adminUserId,
            "INV-BATCH",
            "Batch first valid movement");

        StockMovement batchTwoFails = CreateMovement(
            product.Id,
            StockMovementType.AdjustmentRemove,
            QuantityX1000(999999),
            adminUserId,
            "INV-BATCH",
            "Batch second movement should fail");

        Result failedBatchResult =
            inventoryWriteRepository.AddStockMovementsAsync(
                new[] { batchOne, batchTwoFails }).GetAwaiter().GetResult();

        ExpectFailure(
            failedBatchResult,
            "InventorySmoke.BatchRollback",
            ErrorType.Conflict,
            "Inventory.NegativeStockNotAllowed");

        VerifyProductStock(projectRoot, product.Id, stockBeforeBatch);
        VerifyMovementCount(projectRoot, product.Id, movementCountBeforeBatch);

        StockMovement batchAdd = CreateMovement(
            product.Id,
            StockMovementType.AdjustmentAdd,
            QuantityX1000(1500),
            adminUserId,
            "INV-BATCH",
            "Batch adjustment add");

        StockMovement batchDamage = CreateMovement(
            product.Id,
            StockMovementType.Damage,
            QuantityX1000(500),
            adminUserId,
            "INV-BATCH",
            "Batch damage remove");

        RequireSuccess(
            inventoryWriteRepository.AddStockMovementsAsync(
                new[] { batchAdd, batchDamage }).GetAwaiter().GetResult(),
            "InventorySmoke.ValidBatch");

        VerifyProductStock(projectRoot, product.Id, expectedStockX1000: 4000);
        VerifyMovementExists(projectRoot, batchAdd.Id, product.Id, expectedQtyChangeX1000: 1500);
        VerifyMovementExists(projectRoot, batchDamage.Id, product.Id, expectedQtyChangeX1000: -500);

        Console.WriteLine("Inventory write smoke checks succeeded.");
        Console.WriteLine();
    }

    private static void EnsureDatabaseReady(string projectRoot)
    {
        DatabaseInitializationOrchestrator orchestrator =
            DefaultDatabaseInitializationFactory.CreateDevelopment(projectRoot);

        bool needsRepositoryBootstrap = !File.Exists(orchestrator.DatabasePath);

        if (!needsRepositoryBootstrap)
        {
            DatabaseInitializationStatus status = RequireSuccess(
                orchestrator.CheckStatus(),
                "InventorySmoke.CheckStatus");

            needsRepositoryBootstrap =
                !status.FirstRunStatus.IsDatabaseInitialized ||
                status.FirstRunStatus.IsFirstRunRequired;
        }

        if (needsRepositoryBootstrap)
        {
            RepositorySmokeChecks.Run(projectRoot);
        }
    }

    private static Guid GetSmokeAdminUserId(IUserReadRepository userRepository)
    {
        UserReadModel? admin = RequireSuccess(
            userRepository.FindByUsernameAsync("admin-smoke").GetAwaiter().GetResult(),
            "InventorySmoke.FindAdminSmoke");

        if (admin is not null)
        {
            return admin.Id;
        }

        IReadOnlyList<UserReadModel> users = RequireSuccess(
            userRepository.GetAllAsync().GetAwaiter().GetResult(),
            "InventorySmoke.GetAllUsers");

        if (users.Count == 0)
        {
            throw new InvalidOperationException("Inventory smoke expected at least one user.");
        }

        return users[0].Id;
    }

    private static SeedIds GetSeedIds(ICatalogReadRepository catalogRepository)
    {
        IReadOnlyList<CategoryReadModel> categories = RequireSuccess(
            catalogRepository.GetCategoriesAsync().GetAwaiter().GetResult(),
            "InventorySmoke.GetCategories");

        IReadOnlyList<BrandReadModel> brands = RequireSuccess(
            catalogRepository.GetBrandsAsync().GetAwaiter().GetResult(),
            "InventorySmoke.GetBrands");

        IReadOnlyList<UnitReadModel> units = RequireSuccess(
            catalogRepository.GetUnitsAsync().GetAwaiter().GetResult(),
            "InventorySmoke.GetUnits");

        if (categories.Count == 0 || brands.Count == 0 || units.Count == 0)
        {
            throw new InvalidOperationException("Inventory smoke seed lookup failed.");
        }

        return new SeedIds(categories[0].Id, brands[0].Id, units[0].Id);
    }

    private static Product CreateSmokeProduct(SeedIds seedIds)
    {
        Barcode barcode = RequireValue(
            Barcode.Create(NewBarcode()),
            "InventorySmoke.Barcode");

        return new Product
        {
            Id = Guid.NewGuid(),
            Sku = "INV-SMOKE-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Barcode = barcode,
            Name = "Inventory Smoke Product",
            Description = "Temporary inventory write smoke test product.",
            CategoryId = seedIds.CategoryId,
            BrandId = seedIds.BrandId,
            UnitId = seedIds.UnitId,
            SupplierId = null,
            CostPrice = Money.FromCents(1000),
            SellingPrice = Money.FromCents(1500),
            WholesalePrice = null,
            StockOnHand = QuantityX1000(0),
            ReorderLevel = QuantityX1000(1000),
            TrackStock = true,
            AllowDiscount = true,
            IsActive = true,
            Notes = "Inventory smoke product",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = null
        };
    }

    private static StockMovement CreateMovement(
        Guid productId,
        StockMovementType movementType,
        Quantity quantity,
        Guid userId,
        string referenceType,
        string reason)
    {
        return new StockMovement
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            MovementType = movementType,
            QuantityChange = quantity,
            CostPrice = Money.FromCents(1000),
            ReferenceType = referenceType,
            ReferenceId = Guid.NewGuid(),
            Reason = reason,
            UserId = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static Quantity QuantityX1000(long valueX1000)
    {
        return RequireValue(
            Quantity.FromX1000(valueX1000),
            $"InventorySmoke.Quantity.{valueX1000}");
    }

    private static string NewBarcode()
    {
        return "97" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() + Random.Shared.Next(100, 999).ToString();
    }

    private static void VerifyProductStock(
        string projectRoot,
        Guid productId,
        long expectedStockX1000)
    {
        long actual = GetProductStock(projectRoot, productId);

        if (actual != expectedStockX1000)
        {
            throw new InvalidOperationException(
                $"Product stock verification failed. Expected {expectedStockX1000}, got {actual}.");
        }
    }

    private static long GetProductStock(string projectRoot, Guid productId)
    {
        using SqliteConnection connection = OpenConnection(projectRoot);
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = "SELECT stock_on_hand_x1000 FROM products WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", productId.ToString("D"));

        object? value = command.ExecuteScalar();

        if (value is null)
        {
            throw new InvalidOperationException("Product stock row was not found.");
        }

        return Convert.ToInt64(value);
    }

    private static void VerifyMovementExists(
        string projectRoot,
        Guid movementId,
        Guid productId,
        long expectedQtyChangeX1000)
    {
        using SqliteConnection connection = OpenConnection(projectRoot);
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = """
            SELECT qty_change_x1000
            FROM stock_movements
            WHERE id = $id
              AND product_id = $productId
            LIMIT 1;
            """;

        command.Parameters.AddWithValue("$id", movementId.ToString("D"));
        command.Parameters.AddWithValue("$productId", productId.ToString("D"));

        object? value = command.ExecuteScalar();

        if (value is null)
        {
            throw new InvalidOperationException("Stock movement row was not found.");
        }

        long actual = Convert.ToInt64(value);

        if (actual != expectedQtyChangeX1000)
        {
            throw new InvalidOperationException(
                $"Stock movement quantity verification failed. Expected {expectedQtyChangeX1000}, got {actual}.");
        }
    }

    private static int GetMovementCount(string projectRoot, Guid productId)
    {
        using SqliteConnection connection = OpenConnection(projectRoot);
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = "SELECT COUNT(*) FROM stock_movements WHERE product_id = $productId;";
        command.Parameters.AddWithValue("$productId", productId.ToString("D"));

        return Convert.ToInt32(command.ExecuteScalar() ?? 0);
    }

    private static void VerifyMovementCount(string projectRoot, Guid productId, int expectedCount)
    {
        int actual = GetMovementCount(projectRoot, productId);

        if (actual != expectedCount)
        {
            throw new InvalidOperationException(
                $"Stock movement count verification failed. Expected {expectedCount}, got {actual}.");
        }
    }

    private static SqliteConnection OpenConnection(string projectRoot)
    {
        DatabaseInitializationOrchestrator orchestrator =
            DefaultDatabaseInitializationFactory.CreateDevelopment(projectRoot);

        SqliteConnection connection = new($"Data Source={orchestrator.DatabasePath}");
        connection.Open();

        return connection;
    }

    private static void ExpectFailure(
        Result result,
        string area,
        ErrorType expectedType,
        string expectedCode)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException($"{area} expected failure but succeeded.");
        }

        if (result.ErrorDetail.Type != expectedType ||
            !string.Equals(result.ErrorDetail.Code, expectedCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{area} expected {expectedType}/{expectedCode}, got {result.ErrorDetail.Type}/{result.ErrorDetail.Code}: {result.Error}");
        }
    }

    private static T RequireValue<T>(Result<T> result, string area)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{area} failed: {result.Error}");
        }

        return result.Value;
    }

    private static T RequireSuccess<T>(Result<T> result, string area)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{area} failed: {result.Error}");
        }

        return result.Value;
    }

    private static void RequireSuccess(Result result, string area)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{area} failed: {result.Error}");
        }
    }

    private sealed record SeedIds(Guid CategoryId, Guid BrandId, Guid UnitId);
}
