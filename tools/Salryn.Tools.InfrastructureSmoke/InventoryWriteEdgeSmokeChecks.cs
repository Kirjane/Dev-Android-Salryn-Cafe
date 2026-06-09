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

internal static class InventoryWriteEdgeSmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== INVENTORY WRITE EDGE SMOKE CHECKS =====");

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

        Product stockTracked = CreateProduct(
            seedIds,
            sku: "INV-EDGE-STOCK",
            name: "Inventory Edge Stock Product",
            stockX1000: 3000,
            trackStock: true);

        Product nonStockTracked = CreateProduct(
            seedIds,
            sku: "INV-EDGE-NONSTOCK",
            name: "Inventory Edge Non Stock Product",
            stockX1000: 0,
            trackStock: false);

        Product willBeInactive = CreateProduct(
            seedIds,
            sku: "INV-EDGE-INACTIVE",
            name: "Inventory Edge Inactive Product",
            stockX1000: 1000,
            trackStock: true);

        RequireSuccess(
            productWriteRepository.AddAsync(stockTracked).GetAwaiter().GetResult(),
            "InventoryEdge.AddStockTracked");

        RequireSuccess(
            productWriteRepository.AddAsync(nonStockTracked).GetAwaiter().GetResult(),
            "InventoryEdge.AddNonStockTracked");

        RequireSuccess(
            productWriteRepository.AddAsync(willBeInactive).GetAwaiter().GetResult(),
            "InventoryEdge.AddWillBeInactive");

        RequireSuccess(
            productWriteRepository.DeactivateAsync(
                willBeInactive.Id,
                "Phase 21 inventory inactive edge",
                adminUserId).GetAwaiter().GetResult(),
            "InventoryEdge.DeactivateProduct");

        VerifyProductStock(projectRoot, stockTracked.Id, expectedStockX1000: 3000);

        ExpectFailure(
            inventoryWriteRepository.AddStockMovementsAsync(Array.Empty<StockMovement>()).GetAwaiter().GetResult(),
            "InventoryEdge.EmptyBatch",
            ErrorType.Validation,
            "Inventory.EmptyMovementBatch");

        StockMovement duplicateA = CreateMovement(
            id: Guid.NewGuid(),
            productId: stockTracked.Id,
            movementType: StockMovementType.StockIn,
            quantity: QuantityX1000(1000),
            userId: adminUserId,
            reason: "duplicate A");

        StockMovement duplicateB = CreateMovement(
            id: duplicateA.Id,
            productId: stockTracked.Id,
            movementType: StockMovementType.StockIn,
            quantity: QuantityX1000(1000),
            userId: adminUserId,
            reason: "duplicate B");

        ExpectFailure(
            inventoryWriteRepository.AddStockMovementsAsync(new[] { duplicateA, duplicateB }).GetAwaiter().GetResult(),
            "InventoryEdge.DuplicateMovementIdInBatch",
            ErrorType.Validation,
            "Inventory.DuplicateMovementId");

        ExpectFailure(
            inventoryWriteRepository.AddStockMovementAsync(CreateMovement(
                id: Guid.Empty,
                productId: stockTracked.Id,
                movementType: StockMovementType.StockIn,
                quantity: QuantityX1000(1000),
                userId: adminUserId,
                reason: "empty movement id")).GetAwaiter().GetResult(),
            "InventoryEdge.EmptyMovementId",
            ErrorType.Validation,
            "Inventory.InvalidMovementId");

        ExpectFailure(
            inventoryWriteRepository.AddStockMovementAsync(CreateMovement(
                id: Guid.NewGuid(),
                productId: Guid.Empty,
                movementType: StockMovementType.StockIn,
                quantity: QuantityX1000(1000),
                userId: adminUserId,
                reason: "empty product id")).GetAwaiter().GetResult(),
            "InventoryEdge.EmptyProductId",
            ErrorType.Validation,
            "Inventory.InvalidProductId");

        ExpectFailure(
            inventoryWriteRepository.AddStockMovementAsync(CreateMovement(
                id: Guid.NewGuid(),
                productId: stockTracked.Id,
                movementType: StockMovementType.StockIn,
                quantity: QuantityX1000(1000),
                userId: Guid.Empty,
                reason: "empty user id")).GetAwaiter().GetResult(),
            "InventoryEdge.EmptyUserId",
            ErrorType.Validation,
            "Inventory.InvalidUserId");

        ExpectFailure(
            inventoryWriteRepository.AddStockMovementAsync(CreateMovement(
                id: Guid.NewGuid(),
                productId: stockTracked.Id,
                movementType: (StockMovementType)999,
                quantity: QuantityX1000(1000),
                userId: adminUserId,
                reason: "invalid movement type")).GetAwaiter().GetResult(),
            "InventoryEdge.InvalidMovementType",
            ErrorType.Validation,
            "Inventory.InvalidMovementType");

        ExpectFailure(
            inventoryWriteRepository.AddStockMovementAsync(CreateMovement(
                id: Guid.NewGuid(),
                productId: stockTracked.Id,
                movementType: StockMovementType.StockIn,
                quantity: Quantity.Zero,
                userId: adminUserId,
                reason: "zero quantity")).GetAwaiter().GetResult(),
            "InventoryEdge.ZeroQuantity",
            ErrorType.Validation,
            "Inventory.QuantityRequired");

        ExpectFailure(
            inventoryWriteRepository.AddStockMovementAsync(CreateMovement(
                id: Guid.NewGuid(),
                productId: Guid.NewGuid(),
                movementType: StockMovementType.StockIn,
                quantity: QuantityX1000(1000),
                userId: adminUserId,
                reason: "missing product")).GetAwaiter().GetResult(),
            "InventoryEdge.MissingProduct",
            ErrorType.NotFound,
            "Inventory.ProductNotFoundOrInactive");

        ExpectFailure(
            inventoryWriteRepository.AddStockMovementAsync(CreateMovement(
                id: Guid.NewGuid(),
                productId: willBeInactive.Id,
                movementType: StockMovementType.StockIn,
                quantity: QuantityX1000(1000),
                userId: adminUserId,
                reason: "inactive product")).GetAwaiter().GetResult(),
            "InventoryEdge.InactiveProduct",
            ErrorType.NotFound,
            "Inventory.ProductNotFoundOrInactive");

        ExpectFailure(
            inventoryWriteRepository.AddStockMovementAsync(CreateMovement(
                id: Guid.NewGuid(),
                productId: nonStockTracked.Id,
                movementType: StockMovementType.StockIn,
                quantity: QuantityX1000(1000),
                userId: adminUserId,
                reason: "non stock tracked product")).GetAwaiter().GetResult(),
            "InventoryEdge.NonStockTrackedProduct",
            ErrorType.Validation,
            "Inventory.ProductNotStockTracked");

        long stockBeforeInvalidUser = GetProductStock(projectRoot, stockTracked.Id);

        ExpectFailure(
            inventoryWriteRepository.AddStockMovementAsync(CreateMovement(
                id: Guid.NewGuid(),
                productId: stockTracked.Id,
                movementType: StockMovementType.StockIn,
                quantity: QuantityX1000(1000),
                userId: Guid.NewGuid(),
                reason: "invalid user reference")).GetAwaiter().GetResult(),
            "InventoryEdge.InvalidUserReference",
            ErrorType.Validation,
            "Inventory.InvalidReference");

        VerifyProductStock(projectRoot, stockTracked.Id, stockBeforeInvalidUser);

        StockMovement firstValid = CreateMovement(
            id: Guid.NewGuid(),
            productId: stockTracked.Id,
            movementType: StockMovementType.StockIn,
            quantity: QuantityX1000(1000),
            userId: adminUserId,
            reason: "first valid movement");

        RequireSuccess(
            inventoryWriteRepository.AddStockMovementAsync(firstValid).GetAwaiter().GetResult(),
            "InventoryEdge.FirstValidMovement");

        VerifyProductStock(projectRoot, stockTracked.Id, expectedStockX1000: 4000);
        VerifyMovementExists(projectRoot, firstValid.Id, expectedQtyChangeX1000: 1000);

        long stockBeforeDuplicateDbId = GetProductStock(projectRoot, stockTracked.Id);
        int movementCountBeforeDuplicateDbId = GetMovementCount(projectRoot, stockTracked.Id);

        ExpectFailure(
            inventoryWriteRepository.AddStockMovementAsync(firstValid).GetAwaiter().GetResult(),
            "InventoryEdge.DuplicateMovementIdAlreadyInDb",
            ErrorType.Conflict,
            "Inventory.DuplicateMovementId");

        VerifyProductStock(projectRoot, stockTracked.Id, stockBeforeDuplicateDbId);
        VerifyMovementCount(projectRoot, stockTracked.Id, movementCountBeforeDuplicateDbId);

        long stockBeforeFailedSecondBatch = GetProductStock(projectRoot, stockTracked.Id);
        int countBeforeFailedSecondBatch = GetMovementCount(projectRoot, stockTracked.Id);

        StockMovement batchFirstValid = CreateMovement(
            id: Guid.NewGuid(),
            productId: stockTracked.Id,
            movementType: StockMovementType.StockIn,
            quantity: QuantityX1000(500),
            userId: adminUserId,
            reason: "batch first valid");

        StockMovement batchSecondInvalid = CreateMovement(
            id: Guid.NewGuid(),
            productId: stockTracked.Id,
            movementType: StockMovementType.AdjustmentRemove,
            quantity: QuantityX1000(999999),
            userId: adminUserId,
            reason: "batch second invalid negative");

        ExpectFailure(
            inventoryWriteRepository.AddStockMovementsAsync(new[] { batchFirstValid, batchSecondInvalid }).GetAwaiter().GetResult(),
            "InventoryEdge.BatchSecondFailsRollback",
            ErrorType.Conflict,
            "Inventory.NegativeStockNotAllowed");

        VerifyProductStock(projectRoot, stockTracked.Id, stockBeforeFailedSecondBatch);
        VerifyMovementCount(projectRoot, stockTracked.Id, countBeforeFailedSecondBatch);

        StockMovement countCorrection = CreateMovement(
            id: Guid.NewGuid(),
            productId: stockTracked.Id,
            movementType: StockMovementType.CountCorrection,
            quantity: QuantityX1000(250),
            userId: adminUserId,
            reason: "count correction edge");

        RequireSuccess(
            inventoryWriteRepository.AddStockMovementAsync(countCorrection).GetAwaiter().GetResult(),
            "InventoryEdge.CountCorrection");

        VerifyProductStock(projectRoot, stockTracked.Id, expectedStockX1000: stockBeforeFailedSecondBatch + 250);
        VerifyMovementExists(projectRoot, countCorrection.Id, expectedQtyChangeX1000: 250);

        Console.WriteLine("Inventory write edge smoke checks succeeded.");
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
                "InventoryEdge.CheckStatus");

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
            "InventoryEdge.FindAdminSmoke");

        if (admin is not null)
        {
            return admin.Id;
        }

        IReadOnlyList<UserReadModel> users = RequireSuccess(
            userRepository.GetAllAsync().GetAwaiter().GetResult(),
            "InventoryEdge.GetAllUsers");

        if (users.Count == 0)
        {
            throw new InvalidOperationException("Inventory edge smoke expected at least one user.");
        }

        return users[0].Id;
    }

    private static SeedIds GetSeedIds(ICatalogReadRepository catalogRepository)
    {
        IReadOnlyList<CategoryReadModel> categories = RequireSuccess(
            catalogRepository.GetCategoriesAsync().GetAwaiter().GetResult(),
            "InventoryEdge.GetCategories");

        IReadOnlyList<BrandReadModel> brands = RequireSuccess(
            catalogRepository.GetBrandsAsync().GetAwaiter().GetResult(),
            "InventoryEdge.GetBrands");

        IReadOnlyList<UnitReadModel> units = RequireSuccess(
            catalogRepository.GetUnitsAsync().GetAwaiter().GetResult(),
            "InventoryEdge.GetUnits");

        if (categories.Count == 0 || brands.Count == 0 || units.Count == 0)
        {
            throw new InvalidOperationException("Inventory edge smoke seed lookup failed.");
        }

        return new SeedIds(categories[0].Id, brands[0].Id, units[0].Id);
    }

    private static Product CreateProduct(
        SeedIds seedIds,
        string sku,
        string name,
        long stockX1000,
        bool trackStock)
    {
        Barcode barcode = RequireValue(
            Barcode.Create(NewBarcode()),
            "InventoryEdge.Barcode");

        return new Product
        {
            Id = Guid.NewGuid(),
            Sku = sku + "-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Barcode = barcode,
            Name = name,
            Description = "Phase 21 inventory edge smoke product.",
            CategoryId = seedIds.CategoryId,
            BrandId = seedIds.BrandId,
            UnitId = seedIds.UnitId,
            SupplierId = null,
            CostPrice = Money.FromCents(1000),
            SellingPrice = Money.FromCents(1500),
            WholesalePrice = null,
            StockOnHand = QuantityX1000(stockX1000),
            ReorderLevel = QuantityX1000(1000),
            TrackStock = trackStock,
            AllowDiscount = true,
            IsActive = true,
            Notes = "Inventory edge smoke product",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = null
        };
    }

    private static StockMovement CreateMovement(
        Guid id,
        Guid productId,
        StockMovementType movementType,
        Quantity quantity,
        Guid userId,
        string reason)
    {
        return new StockMovement
        {
            Id = id,
            ProductId = productId,
            MovementType = movementType,
            QuantityChange = quantity,
            CostPrice = Money.FromCents(1000),
            ReferenceType = "INV-EDGE",
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
            $"InventoryEdge.Quantity.{valueX1000}");
    }

    private static string NewBarcode()
    {
        return "96" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() + Random.Shared.Next(100, 999).ToString();
    }

    private static void VerifyProductStock(string projectRoot, Guid productId, long expectedStockX1000)
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

    private static void VerifyMovementExists(string projectRoot, Guid movementId, long expectedQtyChangeX1000)
    {
        using SqliteConnection connection = OpenConnection(projectRoot);
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = "SELECT qty_change_x1000 FROM stock_movements WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", movementId.ToString("D"));

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

    private static void ExpectFailure(Result result, string area, ErrorType expectedType, string expectedCode)
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
