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

internal static class ProductWriteSmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== PRODUCT WRITE SMOKE CHECKS =====");

        EnsureDatabaseReady(projectRoot);

        DbPaths paths = DbPaths.CreateDevelopment(projectRoot);
        SqliteConnectionFactory connectionFactory = new(paths);
        SqliteRepositoryFactory repositoryFactory = new(connectionFactory);

        IProductWriteRepository productWriteRepository =
            repositoryFactory.CreateProductWriteRepository();

        ICatalogReadRepository catalogRepository =
            repositoryFactory.CreateCatalogReadRepository();

        IUserReadRepository userRepository =
            repositoryFactory.CreateUserReadRepository();

        Guid adminUserId = GetSmokeAdminUserId(userRepository);
        SeedIds seedIds = GetSeedIds(catalogRepository);

        string barcodeText = "99" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        Barcode barcode = RequireValue(Barcode.Create(barcodeText), "Barcode.Create");
        Money costPrice = Money.FromCents(1000);
        Money sellingPrice = Money.FromCents(1500);
        Quantity zeroStock = RequireValue(Quantity.FromX1000(0), "Quantity.FromX1000.Zero");
        Quantity reorderLevel = RequireValue(Quantity.FromX1000(1000), "Quantity.FromX1000.Reorder");

        Guid productId = Guid.NewGuid();

        Product product = CreateProduct(
            id: productId,
            sku: "SMOKE-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            barcode: barcode,
            name: "Smoke Test Product",
            description: "Temporary product write smoke test item.",
            categoryId: seedIds.CategoryId,
            brandId: seedIds.BrandId,
            unitId: seedIds.UnitId,
            supplierId: null,
            costPrice: costPrice,
            sellingPrice: sellingPrice,
            wholesalePrice: null,
            stockOnHand: zeroStock,
            reorderLevel: reorderLevel,
            trackStock: true,
            allowDiscount: true,
            isActive: true,
            createdAt: DateTimeOffset.UtcNow,
            updatedAt: null);

        RequireSuccess(
            productWriteRepository.AddAsync(product).GetAwaiter().GetResult(),
            "ProductWrite.Add");

        VerifyProductExists(projectRoot, productId, barcodeText, expectedName: "Smoke Test Product", expectedActive: true);

        Product duplicateProduct = CreateProduct(
            id: Guid.NewGuid(),
            sku: "SMOKE-DUP-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            barcode: barcode,
            name: "Smoke Duplicate Product",
            description: "This duplicate should fail while original is active.",
            categoryId: seedIds.CategoryId,
            brandId: seedIds.BrandId,
            unitId: seedIds.UnitId,
            supplierId: null,
            costPrice: costPrice,
            sellingPrice: sellingPrice,
            wholesalePrice: null,
            stockOnHand: zeroStock,
            reorderLevel: reorderLevel,
            trackStock: true,
            allowDiscount: true,
            isActive: true,
            createdAt: DateTimeOffset.UtcNow,
            updatedAt: null);

        Result duplicateResult =
            productWriteRepository.AddAsync(duplicateProduct).GetAwaiter().GetResult();

        if (duplicateResult.IsSuccess)
        {
            throw new InvalidOperationException("Product duplicate barcode check failed. Duplicate active barcode was accepted.");
        }

        Product updatedProduct = CreateProduct(
            id: productId,
            sku: product.Sku,
            barcode: barcode,
            name: "Smoke Test Product Updated",
            description: "Temporary product write smoke test item updated.",
            categoryId: seedIds.CategoryId,
            brandId: seedIds.BrandId,
            unitId: seedIds.UnitId,
            supplierId: null,
            costPrice: costPrice,
            sellingPrice: Money.FromCents(1750),
            wholesalePrice: null,
            stockOnHand: zeroStock,
            reorderLevel: reorderLevel,
            trackStock: true,
            allowDiscount: true,
            isActive: true,
            createdAt: product.CreatedAt,
            updatedAt: DateTimeOffset.UtcNow);

        RequireSuccess(
            productWriteRepository.UpdateAsync(updatedProduct).GetAwaiter().GetResult(),
            "ProductWrite.Update");

        VerifyProductExists(projectRoot, productId, barcodeText, expectedName: "Smoke Test Product Updated", expectedActive: true);

        RequireSuccess(
            productWriteRepository.DeactivateAsync(
                productId,
                "Phase 17 smoke test deactivate",
                adminUserId).GetAwaiter().GetResult(),
            "ProductWrite.Deactivate");

        VerifyProductExists(projectRoot, productId, barcodeText, expectedName: "Smoke Test Product Updated", expectedActive: false);
        VerifyDeactivateAuditLog(projectRoot, productId);

        Product replacementProduct = CreateProduct(
            id: Guid.NewGuid(),
            sku: "SMOKE-REUSE-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            barcode: barcode,
            name: "Smoke Replacement Product",
            description: "This should pass because the original was deactivated.",
            categoryId: seedIds.CategoryId,
            brandId: seedIds.BrandId,
            unitId: seedIds.UnitId,
            supplierId: null,
            costPrice: costPrice,
            sellingPrice: sellingPrice,
            wholesalePrice: null,
            stockOnHand: zeroStock,
            reorderLevel: reorderLevel,
            trackStock: true,
            allowDiscount: true,
            isActive: true,
            createdAt: DateTimeOffset.UtcNow,
            updatedAt: null);

        RequireSuccess(
            productWriteRepository.AddAsync(replacementProduct).GetAwaiter().GetResult(),
            "ProductWrite.AddAfterDeactivate");

        VerifyBarcodeCounts(projectRoot, barcodeText, expectedTotal: 2, expectedActive: 1);

        Console.WriteLine("Product write smoke checks succeeded.");
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
                "ProductWriteSmoke.CheckStatus");

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
            "ProductWriteSmoke.FindAdminSmoke");

        if (admin is not null)
        {
            return admin.Id;
        }

        IReadOnlyList<UserReadModel> users = RequireSuccess(
            userRepository.GetAllAsync().GetAwaiter().GetResult(),
            "ProductWriteSmoke.GetAllUsers");

        if (users.Count == 0)
        {
            throw new InvalidOperationException("Product write smoke expected at least one user.");
        }

        return users[0].Id;
    }

    private static SeedIds GetSeedIds(ICatalogReadRepository catalogRepository)
    {
        IReadOnlyList<CategoryReadModel> categories = RequireSuccess(
            catalogRepository.GetCategoriesAsync().GetAwaiter().GetResult(),
            "ProductWriteSmoke.GetCategories");

        IReadOnlyList<BrandReadModel> brands = RequireSuccess(
            catalogRepository.GetBrandsAsync().GetAwaiter().GetResult(),
            "ProductWriteSmoke.GetBrands");

        IReadOnlyList<UnitReadModel> units = RequireSuccess(
            catalogRepository.GetUnitsAsync().GetAwaiter().GetResult(),
            "ProductWriteSmoke.GetUnits");

        if (categories.Count == 0 || brands.Count == 0 || units.Count == 0)
        {
            throw new InvalidOperationException("Product write smoke seed lookup failed.");
        }

        return new SeedIds(
            categories[0].Id,
            brands[0].Id,
            units[0].Id);
    }

    private static Product CreateProduct(
        Guid id,
        string? sku,
        Barcode? barcode,
        string name,
        string? description,
        Guid? categoryId,
        Guid? brandId,
        Guid unitId,
        Guid? supplierId,
        Money? costPrice,
        Money sellingPrice,
        Money? wholesalePrice,
        Quantity stockOnHand,
        Quantity reorderLevel,
        bool trackStock,
        bool allowDiscount,
        bool isActive,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt)
    {
        return new Product
        {
            Id = id,
            Sku = sku,
            Barcode = barcode,
            Name = name,
            Description = description,
            CategoryId = categoryId,
            BrandId = brandId,
            UnitId = unitId,
            SupplierId = supplierId,
            CostPrice = costPrice,
            SellingPrice = sellingPrice,
            WholesalePrice = wholesalePrice,
            StockOnHand = stockOnHand,
            ReorderLevel = reorderLevel,
            TrackStock = trackStock,
            AllowDiscount = allowDiscount,
            IsActive = isActive,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };
    }
    private static void VerifyProductExists(
        string projectRoot,
        Guid productId,
        string barcode,
        string expectedName,
        bool expectedActive)
    {
        using SqliteConnection connection = OpenConnection(projectRoot);
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = """
            SELECT name, barcode, is_active
            FROM products
            WHERE id = $id
            LIMIT 1;
            """;

        command.Parameters.AddWithValue("$id", productId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            throw new InvalidOperationException("Product write smoke expected product row to exist.");
        }

        string actualName = reader.GetString(0);
        string actualBarcode = reader.GetString(1);
        bool actualActive = reader.GetInt32(2) != 0;

        if (!string.Equals(actualName, expectedName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Product name verification failed. Expected '{expectedName}', got '{actualName}'.");
        }

        if (!string.Equals(actualBarcode, barcode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Product barcode verification failed.");
        }

        if (actualActive != expectedActive)
        {
            throw new InvalidOperationException("Product active flag verification failed.");
        }
    }

    private static void VerifyDeactivateAuditLog(
        string projectRoot,
        Guid productId)
    {
        using SqliteConnection connection = OpenConnection(projectRoot);
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = """
            SELECT COUNT(*)
            FROM audit_logs
            WHERE action = 'PRODUCT_DEACTIVATED'
              AND entity_type = 'PRODUCT'
              AND entity_id = $productId;
            """;

        command.Parameters.AddWithValue("$productId", productId.ToString("D"));

        int count = Convert.ToInt32(command.ExecuteScalar() ?? 0);

        if (count < 1)
        {
            throw new InvalidOperationException("Product deactivate audit log verification failed.");
        }
    }

    private static void VerifyBarcodeCounts(
        string projectRoot,
        string barcode,
        int expectedTotal,
        int expectedActive)
    {
        using SqliteConnection connection = OpenConnection(projectRoot);

        using SqliteCommand totalCommand = connection.CreateCommand();
        totalCommand.CommandText = "SELECT COUNT(*) FROM products WHERE barcode = $barcode;";
        totalCommand.Parameters.AddWithValue("$barcode", barcode);

        int total = Convert.ToInt32(totalCommand.ExecuteScalar() ?? 0);

        using SqliteCommand activeCommand = connection.CreateCommand();
        activeCommand.CommandText = "SELECT COUNT(*) FROM products WHERE barcode = $barcode AND is_active = 1;";
        activeCommand.Parameters.AddWithValue("$barcode", barcode);

        int active = Convert.ToInt32(activeCommand.ExecuteScalar() ?? 0);

        if (total != expectedTotal || active != expectedActive)
        {
            throw new InvalidOperationException(
                $"Barcode reuse verification failed. Expected total={expectedTotal}, active={expectedActive}; got total={total}, active={active}.");
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

    private static T RequireValue<T>(
        Result<T> result,
        string area)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{area} failed: {result.Error}");
        }

        return result.Value;
    }

    private static T RequireSuccess<T>(
        Result<T> result,
        string area)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{area} failed: {result.Error}");
        }

        return result.Value;
    }

    private static void RequireSuccess(
        Result result,
        string area)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{area} failed: {result.Error}");
        }
    }

    private sealed record SeedIds(
        Guid CategoryId,
        Guid BrandId,
        Guid UnitId);
}

