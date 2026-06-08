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

internal static class ProductWriteEdgeSmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== PRODUCT WRITE EDGE SMOKE CHECKS =====");

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

        SeedIds seedIds = GetSeedIds(catalogRepository);
        Guid adminUserId = GetSmokeAdminUserId(userRepository);

        Money sellingPrice = Money.FromCents(2500);
        Money costPrice = Money.FromCents(1500);
        Quantity zeroStock = RequireValue(Quantity.FromX1000(0), "Edge.Quantity.Zero");
        Quantity reorderLevel = RequireValue(Quantity.FromX1000(1000), "Edge.Quantity.Reorder");

        Barcode barcodeA = RequireValue(Barcode.Create(NewBarcode()), "Edge.BarcodeA");
        Barcode barcodeB = RequireValue(Barcode.Create(NewBarcode()), "Edge.BarcodeB");
        Barcode barcodeC = RequireValue(Barcode.Create(NewBarcode()), "Edge.BarcodeC");

        Product validA = CreateProduct(
            Guid.NewGuid(),
            "EDGE-A",
            barcodeA,
            "Edge Product A",
            seedIds,
            costPrice,
            sellingPrice,
            zeroStock,
            reorderLevel,
            "Edge notes A");

        Product validB = CreateProduct(
            Guid.NewGuid(),
            "EDGE-B",
            barcodeB,
            "Edge Product B",
            seedIds,
            costPrice,
            sellingPrice,
            zeroStock,
            reorderLevel,
            "Edge notes B");

        ExpectFailure(
            productWriteRepository.AddAsync(CreateProduct(
                Guid.Empty,
                "EDGE-EMPTY-ID",
                barcodeA,
                "Edge Empty Id",
                seedIds,
                costPrice,
                sellingPrice,
                zeroStock,
                reorderLevel,
                "invalid")).GetAwaiter().GetResult(),
            "Edge.AddEmptyProductId",
            ErrorType.Validation,
            "Products.InvalidProductId");

        ExpectFailure(
            productWriteRepository.AddAsync(CreateProduct(
                Guid.NewGuid(),
                "EDGE-BLANK-NAME",
                barcodeA,
                "   ",
                seedIds,
                costPrice,
                sellingPrice,
                zeroStock,
                reorderLevel,
                "invalid")).GetAwaiter().GetResult(),
            "Edge.AddBlankName",
            ErrorType.Validation,
            "Products.NameRequired");

        ExpectFailure(
            productWriteRepository.AddAsync(CreateProduct(
                Guid.NewGuid(),
                "EDGE-MISSING-UNIT",
                barcodeA,
                "Edge Missing Unit",
                seedIds,
                costPrice,
                sellingPrice,
                zeroStock,
                reorderLevel,
                "invalid",
                unitIdOverride: Guid.Empty)).GetAwaiter().GetResult(),
            "Edge.AddMissingUnitId",
            ErrorType.Validation,
            "Products.UnitRequired");

        ExpectFailure(
            productWriteRepository.AddAsync(CreateProduct(
                Guid.NewGuid(),
                "EDGE-INACTIVE",
                barcodeA,
                "Edge Inactive",
                seedIds,
                costPrice,
                sellingPrice,
                zeroStock,
                reorderLevel,
                "invalid",
                isActive: false)).GetAwaiter().GetResult(),
            "Edge.AddInactiveProduct",
            ErrorType.Validation,
            "Products.InactiveWriteNotAllowed");

        ExpectFailure(
            productWriteRepository.AddAsync(CreateProduct(
                Guid.NewGuid(),
                "EDGE-BAD-UNIT",
                barcodeA,
                "Edge Bad Unit",
                seedIds,
                costPrice,
                sellingPrice,
                zeroStock,
                reorderLevel,
                "invalid",
                unitIdOverride: Guid.NewGuid())).GetAwaiter().GetResult(),
            "Edge.AddInvalidUnitReference",
            ErrorType.Validation,
            "Products.InvalidReference");

        RequireSuccess(
            productWriteRepository.AddAsync(validA).GetAwaiter().GetResult(),
            "Edge.AddValidA");

        VerifyProductNotes(projectRoot, validA.Id, "Edge notes A");

        RequireSuccess(
            productWriteRepository.AddAsync(validB).GetAwaiter().GetResult(),
            "Edge.AddValidB");

        Product updateDuplicateBarcode = CreateProduct(
            validB.Id,
            "EDGE-B-DUPLICATE-BARCODE",
            barcodeA,
            "Edge Product B Duplicate Barcode",
            seedIds,
            costPrice,
            sellingPrice,
            zeroStock,
            reorderLevel,
            "duplicate barcode update");

        ExpectFailure(
            productWriteRepository.UpdateAsync(updateDuplicateBarcode).GetAwaiter().GetResult(),
            "Edge.UpdateDuplicateBarcode",
            ErrorType.Conflict,
            "Products.DuplicateBarcode");

        Product updateInvalidUnit = CreateProduct(
            validB.Id,
            "EDGE-B-BAD-UNIT",
            barcodeB,
            "Edge Product B Bad Unit",
            seedIds,
            costPrice,
            sellingPrice,
            zeroStock,
            reorderLevel,
            "bad unit update",
            unitIdOverride: Guid.NewGuid());

        ExpectFailure(
            productWriteRepository.UpdateAsync(updateInvalidUnit).GetAwaiter().GetResult(),
            "Edge.UpdateInvalidUnitReference",
            ErrorType.Validation,
            "Products.InvalidReference");

        Product updateMissingProduct = CreateProduct(
            Guid.NewGuid(),
            "EDGE-MISSING-UPDATE",
            barcodeC,
            "Edge Missing Update",
            seedIds,
            costPrice,
            sellingPrice,
            zeroStock,
            reorderLevel,
            "missing update");

        ExpectFailure(
            productWriteRepository.UpdateAsync(updateMissingProduct).GetAwaiter().GetResult(),
            "Edge.UpdateMissingProduct",
            ErrorType.NotFound,
            "Products.NotFoundOrInactive");

        ExpectFailure(
            productWriteRepository.DeactivateAsync(Guid.NewGuid(), "missing product", adminUserId).GetAwaiter().GetResult(),
            "Edge.DeactivateMissingProduct",
            ErrorType.NotFound,
            "Products.NotFoundOrInactive");

        ExpectFailure(
            productWriteRepository.DeactivateAsync(validA.Id, "   ", adminUserId).GetAwaiter().GetResult(),
            "Edge.DeactivateMissingReason",
            ErrorType.Validation,
            "Products.DeactivateReasonRequired");

        ExpectFailure(
            productWriteRepository.DeactivateAsync(validA.Id, "empty user", Guid.Empty).GetAwaiter().GetResult(),
            "Edge.DeactivateEmptyUser",
            ErrorType.Validation,
            "Products.InvalidUserId");

        RequireSuccess(
            productWriteRepository.DeactivateAsync(validA.Id, "Phase 18 edge deactivate", adminUserId).GetAwaiter().GetResult(),
            "Edge.DeactivateValidA");

        Product updateInactiveProduct = CreateProduct(
            validA.Id,
            "EDGE-A-INACTIVE-UPDATE",
            barcodeA,
            "Should Not Update Inactive",
            seedIds,
            costPrice,
            sellingPrice,
            zeroStock,
            reorderLevel,
            "inactive update attempt");

        ExpectFailure(
            productWriteRepository.UpdateAsync(updateInactiveProduct).GetAwaiter().GetResult(),
            "Edge.UpdateInactiveProduct",
            ErrorType.NotFound,
            "Products.NotFoundOrInactive");

        VerifyProductNameAndActive(projectRoot, validA.Id, "Edge Product A", expectedActive: false);

        Product replacement = CreateProduct(
            Guid.NewGuid(),
            "EDGE-A-REUSE",
            barcodeA,
            "Edge Product A Replacement",
            seedIds,
            costPrice,
            sellingPrice,
            zeroStock,
            reorderLevel,
            "Replacement notes");

        RequireSuccess(
            productWriteRepository.AddAsync(replacement).GetAwaiter().GetResult(),
            "Edge.AddReplacementWithInactiveBarcode");

        VerifyBarcodeCounts(projectRoot, barcodeA.Value, expectedTotal: 2, expectedActive: 1);

        Console.WriteLine("Product write edge smoke checks succeeded.");
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
                "Edge.CheckStatus");

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
            "Edge.FindAdminSmoke");

        if (admin is not null)
        {
            return admin.Id;
        }

        IReadOnlyList<UserReadModel> users = RequireSuccess(
            userRepository.GetAllAsync().GetAwaiter().GetResult(),
            "Edge.GetAllUsers");

        if (users.Count == 0)
        {
            throw new InvalidOperationException("Edge smoke expected at least one user.");
        }

        return users[0].Id;
    }

    private static SeedIds GetSeedIds(ICatalogReadRepository catalogRepository)
    {
        IReadOnlyList<CategoryReadModel> categories = RequireSuccess(
            catalogRepository.GetCategoriesAsync().GetAwaiter().GetResult(),
            "Edge.GetCategories");

        IReadOnlyList<BrandReadModel> brands = RequireSuccess(
            catalogRepository.GetBrandsAsync().GetAwaiter().GetResult(),
            "Edge.GetBrands");

        IReadOnlyList<UnitReadModel> units = RequireSuccess(
            catalogRepository.GetUnitsAsync().GetAwaiter().GetResult(),
            "Edge.GetUnits");

        if (categories.Count == 0 || brands.Count == 0 || units.Count == 0)
        {
            throw new InvalidOperationException("Edge smoke seed lookup failed.");
        }

        return new SeedIds(categories[0].Id, brands[0].Id, units[0].Id);
    }

    private static Product CreateProduct(
        Guid id,
        string sku,
        Barcode barcode,
        string name,
        SeedIds seedIds,
        Money? costPrice,
        Money sellingPrice,
        Quantity stockOnHand,
        Quantity reorderLevel,
        string? notes,
        Guid? unitIdOverride = null,
        bool isActive = true)
    {
        return new Product
        {
            Id = id,
            Sku = sku,
            Barcode = barcode,
            Name = name,
            Description = "Phase 18 edge smoke product.",
            CategoryId = seedIds.CategoryId,
            BrandId = seedIds.BrandId,
            UnitId = unitIdOverride ?? seedIds.UnitId,
            SupplierId = null,
            CostPrice = costPrice,
            SellingPrice = sellingPrice,
            WholesalePrice = null,
            StockOnHand = stockOnHand,
            ReorderLevel = reorderLevel,
            TrackStock = true,
            AllowDiscount = true,
            IsActive = isActive,
            Notes = notes,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = null
        };
    }

    private static string NewBarcode()
    {
        return "98" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() + Random.Shared.Next(100, 999).ToString();
    }

    private static void VerifyProductNotes(string projectRoot, Guid productId, string expectedNotes)
    {
        using SqliteConnection connection = OpenConnection(projectRoot);
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = "SELECT notes FROM products WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", productId.ToString("D"));

        string? actual = command.ExecuteScalar() as string;

        if (!string.Equals(actual, expectedNotes, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Product notes verification failed. Expected '{expectedNotes}', got '{actual}'.");
        }
    }

    private static void VerifyProductNameAndActive(string projectRoot, Guid productId, string expectedName, bool expectedActive)
    {
        using SqliteConnection connection = OpenConnection(projectRoot);
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = "SELECT name, is_active FROM products WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", productId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            throw new InvalidOperationException("Product row was not found.");
        }

        string actualName = reader.GetString(0);
        bool actualActive = reader.GetInt32(1) != 0;

        if (!string.Equals(actualName, expectedName, StringComparison.Ordinal) ||
            actualActive != expectedActive)
        {
            throw new InvalidOperationException("Product inactive update verification failed.");
        }
    }

    private static void VerifyBarcodeCounts(string projectRoot, string barcode, int expectedTotal, int expectedActive)
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
                $"Barcode count verification failed. Expected total={expectedTotal}, active={expectedActive}; got total={total}, active={active}.");
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
