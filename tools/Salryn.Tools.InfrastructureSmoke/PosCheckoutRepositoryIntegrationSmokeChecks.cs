using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Paths;

internal static class PosCheckoutRepositoryIntegrationSmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== POS CHECKOUT REPOSITORY INTEGRATION SMOKE CHECKS =====");

        EnsureDatabaseReady(projectRoot);

        DbPaths paths = DbPaths.CreateDevelopment(projectRoot);
        SqliteConnectionFactory connectionFactory = new(paths);
        SqliteRepositoryFactory repositoryFactory = new(connectionFactory);

        IProductWriteRepository productWriteRepository =
            repositoryFactory.CreateProductWriteRepository();

        IProductReadRepository productReadRepository =
            repositoryFactory.CreateProductReadRepository();

        ISaleWriteRepository saleWriteRepository =
            repositoryFactory.CreateSaleWriteRepository();

        ISaleReadRepository saleReadRepository =
            repositoryFactory.CreateSaleReadRepository();

        ICatalogReadRepository catalogRepository =
            repositoryFactory.CreateCatalogReadRepository();

        IUserReadRepository userRepository =
            repositoryFactory.CreateUserReadRepository();

        ICashierSessionWriteRepository cashierSessionWriteRepository =
            repositoryFactory.CreateCashierSessionWriteRepository();

        ICashierSessionReadRepository cashierSessionReadRepository =
            repositoryFactory.CreateCashierSessionReadRepository();

        SeedIds seedIds = GetSeedIds(catalogRepository);
        UserReadModel cashier = GetSmokeCashier(userRepository);

        CashierSessionService cashierSessions = new(
            cashierSessionWriteRepository,
            cashierSessionReadRepository);

        PosCheckoutService checkout = new(
            saleWriteRepository,
            cashierSessions);

        ForceCloseExistingOpenSession(
            cashierSessions,
            cashier.Id);

        Product product = CreateProduct(seedIds);

        RequireSuccess(
            productWriteRepository.AddAsync(product).GetAwaiter().GetResult(),
            "PosCheckoutRepo.AddProduct");

        VerifyProductReadStock(
            productReadRepository,
            product.Id,
            expectedStockX1000: 5000,
            "PosCheckoutRepo.InitialProductStock");

        SalePackage noOpenPackage = CreatePackage(
            product,
            seedIds.UnitName,
            cashier.Id,
            cashierSessionId: null,
            receiptPrefix: "CHK-RI-NOOPEN",
            quantityX1000: 1000);

        ExpectFailure(
            checkout.CompleteSaleAsync(new CompleteCheckoutSaleRequest(
                noOpenPackage.Sale,
                new[] { noOpenPackage.Item },
                new[] { noOpenPackage.Payment },
                new[] { noOpenPackage.Movement })).GetAwaiter().GetResult(),
            "PosCheckoutRepo.OpenSessionRequired",
            ErrorType.Conflict,
            "Checkout.OpenCashierSessionRequired");

        VerifySaleMissing(projectRoot, noOpenPackage.Sale.Id, "PosCheckoutRepo.NoOpenSaleMissing");

        VerifyProductReadStock(
            productReadRepository,
            product.Id,
            expectedStockX1000: 5000,
            "PosCheckoutRepo.StockUnchangedAfterNoOpenFailure");

        CashierSessionReadModel openSession = RequireSuccess(
            cashierSessions.OpenAsync(new OpenCashierSessionRequest(
                cashier.Id,
                Money.FromCents(10000),
                "Phase 33 checkout integration open")).GetAwaiter().GetResult(),
            "PosCheckoutRepo.OpenCashierSession");

        VerifyOpenSession(openSession, cashier.Id);

        SalePackage enforcedPackage = CreatePackage(
            product,
            seedIds.UnitName,
            cashier.Id,
            cashierSessionId: null,
            receiptPrefix: "CHK-RI-ENFORCED",
            quantityX1000: 2000);

        RequireSuccess(
            checkout.CompleteSaleAsync(new CompleteCheckoutSaleRequest(
                enforcedPackage.Sale,
                new[] { enforcedPackage.Item },
                new[] { enforcedPackage.Payment },
                new[] { enforcedPackage.Movement })).GetAwaiter().GetResult(),
            "PosCheckoutRepo.CompleteSaleWithInjectedSession");

        VerifySaleDetail(
            RequireSale(
                saleReadRepository.FindSaleByIdAsync(enforcedPackage.Sale.Id).GetAwaiter().GetResult(),
                "PosCheckoutRepo.FindInjectedSale"),
            enforcedPackage,
            cashier.DisplayName);

        VerifySaleCashierSessionId(
            projectRoot,
            enforcedPackage.Sale.Id,
            openSession.Id,
            "PosCheckoutRepo.InjectedSessionStored");

        VerifyProductReadStock(
            productReadRepository,
            product.Id,
            expectedStockX1000: 3000,
            "PosCheckoutRepo.StockAfterInjectedSale");

        SalePackage mismatchPackage = CreatePackage(
            product,
            seedIds.UnitName,
            cashier.Id,
            cashierSessionId: Guid.NewGuid(),
            receiptPrefix: "CHK-RI-MISMATCH",
            quantityX1000: 1000);

        ExpectFailure(
            checkout.CompleteSaleAsync(new CompleteCheckoutSaleRequest(
                mismatchPackage.Sale,
                new[] { mismatchPackage.Item },
                new[] { mismatchPackage.Payment },
                new[] { mismatchPackage.Movement })).GetAwaiter().GetResult(),
            "PosCheckoutRepo.SessionMismatch",
            ErrorType.Validation,
            "Checkout.CashierSessionMismatch");

        VerifySaleMissing(projectRoot, mismatchPackage.Sale.Id, "PosCheckoutRepo.MismatchSaleMissing");

        VerifyProductReadStock(
            productReadRepository,
            product.Id,
            expectedStockX1000: 3000,
            "PosCheckoutRepo.StockUnchangedAfterMismatchFailure");

        SalePackage matchingPackage = CreatePackage(
            product,
            seedIds.UnitName,
            cashier.Id,
            cashierSessionId: openSession.Id,
            receiptPrefix: "CHK-RI-MATCHING",
            quantityX1000: 1000);

        RequireSuccess(
            checkout.CompleteSaleAsync(new CompleteCheckoutSaleRequest(
                matchingPackage.Sale,
                new[] { matchingPackage.Item },
                new[] { matchingPackage.Payment },
                new[] { matchingPackage.Movement })).GetAwaiter().GetResult(),
            "PosCheckoutRepo.CompleteSaleWithMatchingSession");

        VerifySaleDetail(
            RequireSale(
                saleReadRepository.FindSaleByIdAsync(matchingPackage.Sale.Id).GetAwaiter().GetResult(),
                "PosCheckoutRepo.FindMatchingSale"),
            matchingPackage,
            cashier.DisplayName);

        VerifySaleCashierSessionId(
            projectRoot,
            matchingPackage.Sale.Id,
            openSession.Id,
            "PosCheckoutRepo.MatchingSessionStored");

        VerifyProductReadStock(
            productReadRepository,
            product.Id,
            expectedStockX1000: 2000,
            "PosCheckoutRepo.StockAfterMatchingSale");

        CashierSessionReadModel closedSession = RequireSuccess(
            cashierSessions.CloseAsync(new CloseCashierSessionRequest(
                openSession.Id,
                cashier.Id,
                Money.FromCents(10000),
                "Phase 33 checkout integration close")).GetAwaiter().GetResult(),
            "PosCheckoutRepo.CloseCashierSession");

        if (closedSession.Id != openSession.Id ||
            closedSession.Status != CashierSessionStatus.Closed ||
            closedSession.ClosedAt is null)
        {
            throw new InvalidOperationException("Checkout repository integration close session verification failed.");
        }

        CashierSessionReadModel? finalOpen = RequireSuccess(
            cashierSessions.FindOpenSessionByUserIdAsync(cashier.Id).GetAwaiter().GetResult(),
            "PosCheckoutRepo.FinalNoOpen");

        if (finalOpen is not null)
        {
            throw new InvalidOperationException("Checkout repository integration left an open cashier session.");
        }

        Console.WriteLine("POS checkout repository integration smoke checks succeeded.");
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
                "PosCheckoutRepo.CheckStatus");

            needsRepositoryBootstrap =
                !status.FirstRunStatus.IsDatabaseInitialized ||
                status.FirstRunStatus.IsFirstRunRequired;
        }

        if (needsRepositoryBootstrap)
        {
            RepositorySmokeChecks.Run(projectRoot);
        }
    }

    private static void ForceCloseExistingOpenSession(
        ICashierSessionService cashierSessions,
        Guid cashierId)
    {
        CashierSessionReadModel? existingOpen = RequireSuccess(
            cashierSessions.FindOpenSessionByUserIdAsync(cashierId).GetAwaiter().GetResult(),
            "PosCheckoutRepo.PreExistingOpenLookup");

        if (existingOpen is null)
        {
            return;
        }

        RequireSuccess(
            cashierSessions.ForceCloseAsync(new ForceCloseCashierSessionRequest(
                existingOpen.Id,
                cashierId,
                null,
                "Phase 33 cleanup pre-existing open session")).GetAwaiter().GetResult(),
            "PosCheckoutRepo.ForceClosePreExistingOpen");
    }

    private static SeedIds GetSeedIds(ICatalogReadRepository catalogRepository)
    {
        IReadOnlyList<CategoryReadModel> categories = RequireSuccess(
            catalogRepository.GetCategoriesAsync().GetAwaiter().GetResult(),
            "PosCheckoutRepo.GetCategories");

        IReadOnlyList<BrandReadModel> brands = RequireSuccess(
            catalogRepository.GetBrandsAsync().GetAwaiter().GetResult(),
            "PosCheckoutRepo.GetBrands");

        IReadOnlyList<UnitReadModel> units = RequireSuccess(
            catalogRepository.GetUnitsAsync().GetAwaiter().GetResult(),
            "PosCheckoutRepo.GetUnits");

        if (categories.Count == 0 || brands.Count == 0 || units.Count == 0)
        {
            throw new InvalidOperationException("Checkout repository integration seed lookup failed.");
        }

        return new SeedIds(
            categories[0].Id,
            brands[0].Id,
            units[0].Id,
            units[0].Name);
    }

    private static UserReadModel GetSmokeCashier(IUserReadRepository userRepository)
    {
        UserReadModel? admin = RequireSuccess(
            userRepository.FindByUsernameAsync("admin-smoke").GetAwaiter().GetResult(),
            "PosCheckoutRepo.FindAdminSmoke");

        if (admin is not null)
        {
            return admin;
        }

        IReadOnlyList<UserReadModel> users = RequireSuccess(
            userRepository.GetAllAsync().GetAwaiter().GetResult(),
            "PosCheckoutRepo.GetAllUsers");

        if (users.Count == 0)
        {
            throw new InvalidOperationException("Checkout repository integration expected at least one user.");
        }

        return users[0];
    }

    private static Product CreateProduct(SeedIds seedIds)
    {
        Barcode barcode = RequireValue(
            Barcode.Create(NewBarcode()),
            "PosCheckoutRepo.Barcode");

        return new Product
        {
            Id = Guid.NewGuid(),
            Sku = "CHK-REPO-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "-" + Random.Shared.Next(100, 999),
            Barcode = barcode,
            Name = "Checkout Repository Integration Product",
            Description = "Temporary checkout repository integration product.",
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
            Notes = "Checkout repository integration smoke product",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = null
        };
    }

    private static SalePackage CreatePackage(
        Product product,
        string unitName,
        Guid cashierUserId,
        Guid? cashierSessionId,
        string receiptPrefix,
        long quantityX1000)
    {
        Guid saleId = Guid.NewGuid();
        Quantity quantity = QuantityX1000(quantityX1000);
        long lineTotalCents = CalculateLineGrossCents(product.SellingPrice, quantity);
        long amountPaidCents = checked(lineTotalCents + 500);

        Sale sale = new()
        {
            Id = saleId,
            ReceiptNumber = NewReceiptNo(receiptPrefix),
            SaleDate = DateTimeOffset.UtcNow,
            CashierUserId = cashierUserId,
            CashierSessionId = cashierSessionId,
            Subtotal = Money.FromCents(lineTotalCents),
            Discount = Money.Zero,
            Tax = Money.Zero,
            Total = Money.FromCents(lineTotalCents),
            AmountPaid = Money.FromCents(amountPaidCents),
            Change = Money.FromCents(500),
            Status = SaleStatus.Completed,
            VoidedByUserId = null,
            VoidReason = null,
            VoidedAt = null,
            ReceiptSnapshot = "Phase 33 checkout repository integration receipt snapshot.",
            CreatedAt = DateTimeOffset.UtcNow
        };

        SaleItem item = new()
        {
            Id = Guid.NewGuid(),
            SaleId = saleId,
            ProductId = product.Id,
            ProductNameSnapshot = product.Name,
            BarcodeSnapshot = product.Barcode?.Value,
            UnitNameSnapshot = unitName,
            Quantity = quantity,
            UnitPrice = product.SellingPrice,
            CostPrice = product.CostPrice,
            Discount = Money.Zero,
            LineTotal = Money.FromCents(lineTotalCents)
        };

        Payment payment = new()
        {
            Id = Guid.NewGuid(),
            SaleId = saleId,
            Method = PaymentMethod.Cash,
            Amount = Money.FromCents(amountPaidCents),
            PaidAt = DateTimeOffset.UtcNow
        };

        StockMovement movement = new()
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            MovementType = StockMovementType.Sale,
            QuantityChange = quantity,
            CostPrice = product.CostPrice,
            ReferenceType = "SALE",
            ReferenceId = saleId,
            Reason = "Phase 33 checkout repository integration stock-out",
            UserId = cashierUserId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        return new SalePackage(sale, item, payment, movement);
    }

    private static void VerifyOpenSession(
        CashierSessionReadModel openSession,
        Guid expectedCashierId)
    {
        if (openSession.Id == Guid.Empty ||
            openSession.UserId != expectedCashierId ||
            openSession.Status != CashierSessionStatus.Open ||
            openSession.ClosedAt is not null)
        {
            throw new InvalidOperationException("Checkout repository integration open session verification failed.");
        }
    }

    private static void VerifySaleDetail(
        SaleDetailReadModel detail,
        SalePackage package,
        string expectedCashierDisplayName)
    {
        if (detail.Id != package.Sale.Id ||
            !string.Equals(detail.ReceiptNo, package.Sale.ReceiptNumber, StringComparison.Ordinal) ||
            !string.Equals(detail.CashierDisplayName, expectedCashierDisplayName, StringComparison.Ordinal) ||
            detail.Subtotal.Cents != package.Sale.Subtotal.Cents ||
            detail.Discount.Cents != package.Sale.Discount.Cents ||
            detail.Tax.Cents != package.Sale.Tax.Cents ||
            detail.Total.Cents != package.Sale.Total.Cents ||
            detail.AmountPaid.Cents != package.Sale.AmountPaid.Cents ||
            detail.Change.Cents != package.Sale.Change.Cents ||
            detail.Status != SaleStatus.Completed ||
            detail.VoidReason is not null ||
            detail.VoidedAt is not null ||
            string.IsNullOrWhiteSpace(detail.ReceiptSnapshot))
        {
            throw new InvalidOperationException("Checkout repository integration sale detail header verification failed.");
        }

        if (detail.Items.Count != 1)
        {
            throw new InvalidOperationException("Checkout repository integration sale detail item count failed.");
        }

        SaleItemDetailReadModel item = detail.Items[0];

        if (item.Id != package.Item.Id ||
            item.ProductId != package.Item.ProductId ||
            !string.Equals(item.ProductNameSnapshot, package.Item.ProductNameSnapshot, StringComparison.Ordinal) ||
            !string.Equals(item.BarcodeSnapshot, package.Item.BarcodeSnapshot, StringComparison.Ordinal) ||
            !string.Equals(item.UnitNameSnapshot, package.Item.UnitNameSnapshot, StringComparison.Ordinal) ||
            item.Quantity.ValueX1000 != package.Item.Quantity.ValueX1000 ||
            item.UnitPrice.Cents != package.Item.UnitPrice.Cents ||
            item.CostPrice?.Cents != package.Item.CostPrice?.Cents ||
            item.Discount.Cents != package.Item.Discount.Cents ||
            item.LineTotal.Cents != package.Item.LineTotal.Cents)
        {
            throw new InvalidOperationException("Checkout repository integration sale item verification failed.");
        }

        if (detail.Payments.Count != 1)
        {
            throw new InvalidOperationException("Checkout repository integration payment count failed.");
        }

        PaymentReadModel payment = detail.Payments[0];

        if (payment.Id != package.Payment.Id ||
            payment.Method != package.Payment.Method ||
            payment.Amount.Cents != package.Payment.Amount.Cents)
        {
            throw new InvalidOperationException("Checkout repository integration payment verification failed.");
        }
    }

    private static void VerifySaleCashierSessionId(
        string projectRoot,
        Guid saleId,
        Guid expectedSessionId,
        string area)
    {
        using SqliteConnection connection = OpenConnection(projectRoot);
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = """
            SELECT cashier_session_id
            FROM sales
            WHERE id = $id
            LIMIT 1;
            """;

        command.Parameters.AddWithValue("$id", saleId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            throw new InvalidOperationException($"{area} sale row was not found.");
        }

        if (reader.IsDBNull(0))
        {
            throw new InvalidOperationException($"{area} expected cashier session ID but found null.");
        }

        Guid actualSessionId = Guid.Parse(reader.GetString(0));

        if (actualSessionId != expectedSessionId)
        {
            throw new InvalidOperationException(
                $"{area} expected session {expectedSessionId}, got {actualSessionId}.");
        }
    }

    private static void VerifySaleMissing(
        string projectRoot,
        Guid saleId,
        string area)
    {
        using SqliteConnection connection = OpenConnection(projectRoot);
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = "SELECT COUNT(*) FROM sales WHERE id = $id;";
        command.Parameters.AddWithValue("$id", saleId.ToString("D"));

        int count = Convert.ToInt32(command.ExecuteScalar() ?? 0);

        if (count != 0)
        {
            throw new InvalidOperationException($"{area} expected sale to be missing.");
        }
    }

    private static void VerifyProductReadStock(
        IProductReadRepository productReadRepository,
        Guid productId,
        long expectedStockX1000,
        string area)
    {
        ProductLookupReadModel? product = RequireSuccess(
            productReadRepository.FindByIdAsync(productId).GetAwaiter().GetResult(),
            area);

        if (product is null)
        {
            throw new InvalidOperationException($"{area} product was not found.");
        }

        if (product.StockOnHand.ValueX1000 != expectedStockX1000)
        {
            throw new InvalidOperationException(
                $"{area} expected stock {expectedStockX1000}, got {product.StockOnHand.ValueX1000}.");
        }
    }

    private static SaleDetailReadModel RequireSale(
        Result<SaleDetailReadModel?> result,
        string area)
    {
        SaleDetailReadModel? sale = RequireSuccess(result, area);

        if (sale is null)
        {
            throw new InvalidOperationException($"{area} returned no sale.");
        }

        return sale;
    }

    private static SqliteConnection OpenConnection(string projectRoot)
    {
        DatabaseInitializationOrchestrator orchestrator =
            DefaultDatabaseInitializationFactory.CreateDevelopment(projectRoot);

        SqliteConnection connection = new($"Data Source={orchestrator.DatabasePath}");
        connection.Open();

        return connection;
    }

    private static Quantity QuantityX1000(long valueX1000)
    {
        return RequireValue(
            Quantity.FromX1000(valueX1000),
            $"PosCheckoutRepo.Quantity.{valueX1000}");
    }

    private static long CalculateLineGrossCents(
        Money unitPrice,
        Quantity quantity)
    {
        decimal gross = unitPrice.Cents * (quantity.ValueX1000 / 1000m);

        return checked((long)Math.Round(
            gross,
            0,
            MidpointRounding.AwayFromZero));
    }

    private static string NewBarcode()
    {
        return "97" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() + Random.Shared.Next(100, 999).ToString();
    }

    private static string NewReceiptNo(string prefix)
    {
        return prefix + "-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() + "-" + Random.Shared.Next(100, 999).ToString();
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

    private sealed record SeedIds(
        Guid CategoryId,
        Guid BrandId,
        Guid UnitId,
        string UnitName);

    private sealed record SalePackage(
        Sale Sale,
        SaleItem Item,
        Payment Payment,
        StockMovement Movement);
}
