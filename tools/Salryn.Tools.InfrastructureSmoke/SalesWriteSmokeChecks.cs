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

internal static class SalesWriteSmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== SALES WRITE SMOKE CHECKS =====");

        EnsureDatabaseReady(projectRoot);

        DbPaths paths = DbPaths.CreateDevelopment(projectRoot);
        SqliteConnectionFactory connectionFactory = new(paths);
        SqliteRepositoryFactory repositoryFactory = new(connectionFactory);

        IProductWriteRepository productWriteRepository =
            repositoryFactory.CreateProductWriteRepository();

        ISaleWriteRepository saleWriteRepository =
            repositoryFactory.CreateSaleWriteRepository();

        ICatalogReadRepository catalogRepository =
            repositoryFactory.CreateCatalogReadRepository();

        IUserReadRepository userRepository =
            repositoryFactory.CreateUserReadRepository();

        SeedIds seedIds = GetSeedIds(catalogRepository);
        Guid adminUserId = GetSmokeAdminUserId(userRepository);

        Product product = CreateSmokeProduct(seedIds);

        RequireSuccess(
            productWriteRepository.AddAsync(product).GetAwaiter().GetResult(),
            "SalesSmoke.AddProduct");

        VerifyProductStock(projectRoot, product.Id, expectedStockX1000: 5000);

        Guid saleId = Guid.NewGuid();
        string receiptNo = NewReceiptNo();

        Sale sale = CreateCompletedSale(
            saleId,
            receiptNo,
            adminUserId,
            subtotalCents: 3000,
            amountPaidCents: 5000);

        SaleItem item = CreateSaleItem(
            saleId,
            product,
            seedIds.UnitName,
            QuantityX1000(2000),
            unitPriceCents: 1500,
            lineTotalCents: 3000);

        Payment payment = CreatePayment(
            saleId,
            amountCents: 5000);

        StockMovement saleMovement = CreateMovement(
            Guid.NewGuid(),
            product.Id,
            StockMovementType.Sale,
            QuantityX1000(2000),
            adminUserId,
            saleId,
            "Smoke completed sale stock-out");

        RequireSuccess(
            saleWriteRepository.AddCompletedSaleAsync(
                sale,
                new[] { item },
                new[] { payment },
                new[] { saleMovement }).GetAwaiter().GetResult(),
            "SalesSmoke.AddCompletedSale");

        VerifySaleHeader(
            projectRoot,
            saleId,
            receiptNo,
            SaleStatus.Completed,
            totalCents: 3000,
            amountPaidCents: 5000,
            changeCents: 2000);

        VerifySaleItemExists(
            projectRoot,
            item.Id,
            saleId,
            product.Id,
            expectedQtyX1000: 2000,
            expectedLineTotalCents: 3000);

        VerifyPaymentExists(
            projectRoot,
            payment.Id,
            saleId,
            expectedAmountCents: 5000);

        VerifyStockMovementExists(
            projectRoot,
            saleMovement.Id,
            product.Id,
            saleId,
            expectedQtyChangeX1000: -2000);

        VerifyProductStock(projectRoot, product.Id, expectedStockX1000: 3000);

        StockMovement voidMovement = CreateMovement(
            Guid.NewGuid(),
            product.Id,
            StockMovementType.VoidSale,
            QuantityX1000(2000),
            adminUserId,
            saleId,
            "Smoke void sale stock reversal");

        RequireSuccess(
            saleWriteRepository.MarkVoidedAsync(
                saleId,
                adminUserId,
                "Smoke void completed sale",
                new[] { voidMovement }).GetAwaiter().GetResult(),
            "SalesSmoke.VoidSale");

        VerifySaleVoided(
            projectRoot,
            saleId,
            adminUserId,
            "Smoke void completed sale");

        VerifyStockMovementExists(
            projectRoot,
            voidMovement.Id,
            product.Id,
            saleId,
            expectedQtyChangeX1000: 2000);

        VerifyProductStock(projectRoot, product.Id, expectedStockX1000: 5000);

        long stockBeforeFailedSale = GetProductStock(projectRoot, product.Id);
        int movementCountBeforeFailedSale = GetMovementCount(projectRoot, product.Id);

        Guid failedSaleId = Guid.NewGuid();
        string failedReceiptNo = NewReceiptNo();

        Sale failedSale = CreateCompletedSale(
            failedSaleId,
            failedReceiptNo,
            adminUserId,
            subtotalCents: 9000,
            amountPaidCents: 9000);

        SaleItem failedItem = CreateSaleItem(
            failedSaleId,
            product,
            seedIds.UnitName,
            QuantityX1000(6000),
            unitPriceCents: 1500,
            lineTotalCents: 9000);

        Payment failedPayment = CreatePayment(
            failedSaleId,
            amountCents: 9000);

        StockMovement failedMovement = CreateMovement(
            Guid.NewGuid(),
            product.Id,
            StockMovementType.Sale,
            QuantityX1000(6000),
            adminUserId,
            failedSaleId,
            "Smoke sale should fail due negative stock");

        ExpectFailure(
            saleWriteRepository.AddCompletedSaleAsync(
                failedSale,
                new[] { failedItem },
                new[] { failedPayment },
                new[] { failedMovement }).GetAwaiter().GetResult(),
            "SalesSmoke.NegativeStockRollback",
            ErrorType.Conflict,
            "Sales.NegativeStockNotAllowed");

        VerifySaleMissing(projectRoot, failedSaleId);
        VerifySaleItemMissing(projectRoot, failedItem.Id);
        VerifyPaymentMissing(projectRoot, failedPayment.Id);
        VerifyStockMovementMissing(projectRoot, failedMovement.Id);
        VerifyProductStock(projectRoot, product.Id, stockBeforeFailedSale);
        VerifyMovementCount(projectRoot, product.Id, movementCountBeforeFailedSale);

        Console.WriteLine("Sales write smoke checks succeeded.");
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
                "SalesSmoke.CheckStatus");

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
            "SalesSmoke.FindAdminSmoke");

        if (admin is not null)
        {
            return admin.Id;
        }

        IReadOnlyList<UserReadModel> users = RequireSuccess(
            userRepository.GetAllAsync().GetAwaiter().GetResult(),
            "SalesSmoke.GetAllUsers");

        if (users.Count == 0)
        {
            throw new InvalidOperationException("Sales smoke expected at least one user.");
        }

        return users[0].Id;
    }

    private static SeedIds GetSeedIds(ICatalogReadRepository catalogRepository)
    {
        IReadOnlyList<CategoryReadModel> categories = RequireSuccess(
            catalogRepository.GetCategoriesAsync().GetAwaiter().GetResult(),
            "SalesSmoke.GetCategories");

        IReadOnlyList<BrandReadModel> brands = RequireSuccess(
            catalogRepository.GetBrandsAsync().GetAwaiter().GetResult(),
            "SalesSmoke.GetBrands");

        IReadOnlyList<UnitReadModel> units = RequireSuccess(
            catalogRepository.GetUnitsAsync().GetAwaiter().GetResult(),
            "SalesSmoke.GetUnits");

        if (categories.Count == 0 || brands.Count == 0 || units.Count == 0)
        {
            throw new InvalidOperationException("Sales smoke seed lookup failed.");
        }

        return new SeedIds(
            categories[0].Id,
            brands[0].Id,
            units[0].Id,
            units[0].Name);
    }

    private static Product CreateSmokeProduct(SeedIds seedIds)
    {
        Barcode barcode = RequireValue(
            Barcode.Create(NewBarcode()),
            "SalesSmoke.Barcode");

        return new Product
        {
            Id = Guid.NewGuid(),
            Sku = "SALES-SMOKE-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "-" + Random.Shared.Next(100, 999),
            Barcode = barcode,
            Name = "Sales Smoke Product",
            Description = "Temporary sales write smoke test product.",
            CategoryId = seedIds.CategoryId,
            BrandId = seedIds.BrandId,
            UnitId = seedIds.UnitId,
            SupplierId = null,
            CostPrice = Money.FromCents(1000),
            SellingPrice = Money.FromCents(1500),
            WholesalePrice = null,
            StockOnHand = QuantityX1000(5000),
            ReorderLevel = QuantityX1000(1000),
            TrackStock = true,
            AllowDiscount = true,
            IsActive = true,
            Notes = "Sales smoke product",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = null
        };
    }

    private static Sale CreateCompletedSale(
        Guid saleId,
        string receiptNo,
        Guid cashierUserId,
        long subtotalCents,
        long amountPaidCents)
    {
        long totalCents = subtotalCents;
        long changeCents = checked(amountPaidCents - totalCents);

        if (changeCents < 0)
        {
            throw new InvalidOperationException("Smoke sale amount paid cannot be less than total.");
        }

        return new Sale
        {
            Id = saleId,
            ReceiptNumber = receiptNo,
            SaleDate = DateTimeOffset.UtcNow,
            CashierUserId = cashierUserId,
            CashierSessionId = null,
            Subtotal = Money.FromCents(subtotalCents),
            Discount = Money.Zero,
            Tax = Money.Zero,
            Total = Money.FromCents(totalCents),
            AmountPaid = Money.FromCents(amountPaidCents),
            Change = Money.FromCents(changeCents),
            Status = SaleStatus.Completed,
            VoidedByUserId = null,
            VoidReason = null,
            VoidedAt = null,
            ReceiptSnapshot = "Sales smoke receipt snapshot.",
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static SaleItem CreateSaleItem(
        Guid saleId,
        Product product,
        string unitName,
        Quantity quantity,
        long unitPriceCents,
        long lineTotalCents)
    {
        return new SaleItem
        {
            Id = Guid.NewGuid(),
            SaleId = saleId,
            ProductId = product.Id,
            ProductNameSnapshot = product.Name,
            BarcodeSnapshot = product.Barcode?.Value,
            UnitNameSnapshot = unitName,
            Quantity = quantity,
            UnitPrice = Money.FromCents(unitPriceCents),
            CostPrice = product.CostPrice,
            Discount = Money.Zero,
            LineTotal = Money.FromCents(lineTotalCents)
        };
    }

    private static Payment CreatePayment(Guid saleId, long amountCents)
    {
        return new Payment
        {
            Id = Guid.NewGuid(),
            SaleId = saleId,
            Method = PaymentMethod.Cash,
            Amount = Money.FromCents(amountCents),
            PaidAt = DateTimeOffset.UtcNow
        };
    }

    private static StockMovement CreateMovement(
        Guid id,
        Guid productId,
        StockMovementType movementType,
        Quantity quantity,
        Guid userId,
        Guid saleId,
        string reason)
    {
        return new StockMovement
        {
            Id = id,
            ProductId = productId,
            MovementType = movementType,
            QuantityChange = quantity,
            CostPrice = Money.FromCents(1000),
            ReferenceType = "SALE",
            ReferenceId = saleId,
            Reason = reason,
            UserId = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static Quantity QuantityX1000(long valueX1000)
    {
        return RequireValue(
            Quantity.FromX1000(valueX1000),
            $"SalesSmoke.Quantity.{valueX1000}");
    }

    private static string NewBarcode()
    {
        return "95" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() + Random.Shared.Next(100, 999).ToString();
    }

    private static string NewReceiptNo()
    {
        return "SMOKE-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() + "-" + Random.Shared.Next(100, 999).ToString();
    }

    private static void VerifySaleHeader(
        string projectRoot,
        Guid saleId,
        string expectedReceiptNo,
        SaleStatus expectedStatus,
        long totalCents,
        long amountPaidCents,
        long changeCents)
    {
        using SqliteConnection connection = OpenConnection(projectRoot);
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                receipt_no,
                status,
                total_cents,
                amount_paid_cents,
                change_cents
            FROM sales
            WHERE id = $id
            LIMIT 1;
            """;

        command.Parameters.AddWithValue("$id", saleId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            throw new InvalidOperationException("Sale row was not found.");
        }

        string actualReceiptNo = reader.GetString(0);
        SaleStatus actualStatus = (SaleStatus)reader.GetInt32(1);
        long actualTotal = reader.GetInt64(2);
        long actualAmountPaid = reader.GetInt64(3);
        long actualChange = reader.GetInt64(4);

        if (!string.Equals(actualReceiptNo, expectedReceiptNo, StringComparison.Ordinal) ||
            actualStatus != expectedStatus ||
            actualTotal != totalCents ||
            actualAmountPaid != amountPaidCents ||
            actualChange != changeCents)
        {
            throw new InvalidOperationException("Sale header verification failed.");
        }
    }

    private static void VerifySaleVoided(
        string projectRoot,
        Guid saleId,
        Guid expectedVoidedByUserId,
        string expectedReason)
    {
        using SqliteConnection connection = OpenConnection(projectRoot);
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                status,
                voided_by_user_id,
                void_reason,
                voided_at
            FROM sales
            WHERE id = $id
            LIMIT 1;
            """;

        command.Parameters.AddWithValue("$id", saleId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            throw new InvalidOperationException("Voided sale row was not found.");
        }

        SaleStatus actualStatus = (SaleStatus)reader.GetInt32(0);
        string actualVoidedByUserId = reader.GetString(1);
        string actualReason = reader.GetString(2);
        string actualVoidedAt = reader.GetString(3);

        if (actualStatus != SaleStatus.Voided ||
            !string.Equals(actualVoidedByUserId, expectedVoidedByUserId.ToString("D"), StringComparison.Ordinal) ||
            !string.Equals(actualReason, expectedReason, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(actualVoidedAt))
        {
            throw new InvalidOperationException("Sale void verification failed.");
        }
    }

    private static void VerifySaleItemExists(
        string projectRoot,
        Guid itemId,
        Guid saleId,
        Guid productId,
        long expectedQtyX1000,
        long expectedLineTotalCents)
    {
        using SqliteConnection connection = OpenConnection(projectRoot);
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                product_id,
                qty_x1000,
                line_total_cents
            FROM sale_items
            WHERE id = $id
              AND sale_id = $saleId
            LIMIT 1;
            """;

        command.Parameters.AddWithValue("$id", itemId.ToString("D"));
        command.Parameters.AddWithValue("$saleId", saleId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            throw new InvalidOperationException("Sale item row was not found.");
        }

        string actualProductId = reader.GetString(0);
        long actualQty = reader.GetInt64(1);
        long actualLineTotal = reader.GetInt64(2);

        if (!string.Equals(actualProductId, productId.ToString("D"), StringComparison.Ordinal) ||
            actualQty != expectedQtyX1000 ||
            actualLineTotal != expectedLineTotalCents)
        {
            throw new InvalidOperationException("Sale item verification failed.");
        }
    }

    private static void VerifyPaymentExists(
        string projectRoot,
        Guid paymentId,
        Guid saleId,
        long expectedAmountCents)
    {
        using SqliteConnection connection = OpenConnection(projectRoot);
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = """
            SELECT amount_cents, method
            FROM payments
            WHERE id = $id
              AND sale_id = $saleId
            LIMIT 1;
            """;

        command.Parameters.AddWithValue("$id", paymentId.ToString("D"));
        command.Parameters.AddWithValue("$saleId", saleId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            throw new InvalidOperationException("Payment row was not found.");
        }

        long actualAmount = reader.GetInt64(0);
        PaymentMethod actualMethod = (PaymentMethod)reader.GetInt32(1);

        if (actualAmount != expectedAmountCents || actualMethod != PaymentMethod.Cash)
        {
            throw new InvalidOperationException("Payment verification failed.");
        }
    }

    private static void VerifyStockMovementExists(
        string projectRoot,
        Guid movementId,
        Guid productId,
        Guid saleId,
        long expectedQtyChangeX1000)
    {
        using SqliteConnection connection = OpenConnection(projectRoot);
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                qty_change_x1000,
                reference_type,
                reference_id
            FROM stock_movements
            WHERE id = $id
              AND product_id = $productId
            LIMIT 1;
            """;

        command.Parameters.AddWithValue("$id", movementId.ToString("D"));
        command.Parameters.AddWithValue("$productId", productId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            throw new InvalidOperationException("Stock movement row was not found.");
        }

        long actualQty = reader.GetInt64(0);
        string actualReferenceType = reader.GetString(1);
        string actualReferenceId = reader.GetString(2);

        if (actualQty != expectedQtyChangeX1000 ||
            !string.Equals(actualReferenceType, "SALE", StringComparison.Ordinal) ||
            !string.Equals(actualReferenceId, saleId.ToString("D"), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Stock movement verification failed.");
        }
    }

    private static void VerifySaleMissing(string projectRoot, Guid saleId)
    {
        if (GetCount(projectRoot, "sales", saleId) != 0)
        {
            throw new InvalidOperationException("Failed sale rollback verification failed. Sale row still exists.");
        }
    }

    private static void VerifySaleItemMissing(string projectRoot, Guid itemId)
    {
        if (GetCount(projectRoot, "sale_items", itemId) != 0)
        {
            throw new InvalidOperationException("Failed sale rollback verification failed. Sale item row still exists.");
        }
    }

    private static void VerifyPaymentMissing(string projectRoot, Guid paymentId)
    {
        if (GetCount(projectRoot, "payments", paymentId) != 0)
        {
            throw new InvalidOperationException("Failed sale rollback verification failed. Payment row still exists.");
        }
    }

    private static void VerifyStockMovementMissing(string projectRoot, Guid movementId)
    {
        if (GetCount(projectRoot, "stock_movements", movementId) != 0)
        {
            throw new InvalidOperationException("Failed sale rollback verification failed. Stock movement row still exists.");
        }
    }

    private static int GetCount(string projectRoot, string tableName, Guid id)
    {
        using SqliteConnection connection = OpenConnection(projectRoot);
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = $"SELECT COUNT(*) FROM {tableName} WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));

        return Convert.ToInt32(command.ExecuteScalar() ?? 0);
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

    private sealed record SeedIds(Guid CategoryId, Guid BrandId, Guid UnitId, string UnitName);
}
