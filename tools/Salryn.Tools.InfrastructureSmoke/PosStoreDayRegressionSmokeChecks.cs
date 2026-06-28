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

internal static class PosStoreDayRegressionSmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== POS STORE-DAY REGRESSION SMOKE CHECKS =====");

        RunSourceGuard(projectRoot);
        EnsureDatabaseReady(projectRoot);

        DbPaths paths = DbPaths.CreateDevelopment(projectRoot);
        SqliteConnectionFactory connectionFactory = new(paths);
        SqliteRepositoryFactory repositoryFactory = new(connectionFactory);

        IProductWriteRepository productWriteRepository =
            repositoryFactory.CreateProductWriteRepository();

        IProductReadRepository productReadRepository =
            repositoryFactory.CreateProductReadRepository();

        IInventoryWriteRepository inventoryWriteRepository =
            repositoryFactory.CreateInventoryWriteRepository();

        ISaleWriteRepository saleWriteRepository =
            repositoryFactory.CreateSaleWriteRepository();

        ISaleReadRepository saleReadRepository =
            repositoryFactory.CreateSaleReadRepository();

        ICatalogReadRepository catalogReadRepository =
            repositoryFactory.CreateCatalogReadRepository();

        IUserReadRepository userReadRepository =
            repositoryFactory.CreateUserReadRepository();

        ICashierSessionWriteRepository cashierSessionWriteRepository =
            repositoryFactory.CreateCashierSessionWriteRepository();

        ICashierSessionReadRepository cashierSessionReadRepository =
            repositoryFactory.CreateCashierSessionReadRepository();

        SeedIds seedIds = GetSeedIds(catalogReadRepository);
        UserReadModel cashier = GetSmokeCashier(userReadRepository);

        CashierSessionService cashierSessions = new(
            cashierSessionWriteRepository,
            cashierSessionReadRepository);

        PosCheckoutService checkoutCore = new(
            saleWriteRepository,
            cashierSessions);

        PosCheckoutApplicationService checkoutApplication = new(
            checkoutCore,
            productReadRepository);

        ForceCloseExistingOpenSession(cashierSessions, cashier.Id);

        Product product = CreateTrackedProduct(seedIds);

        RequireSuccess(
            productWriteRepository.AddAsync(product).GetAwaiter().GetResult(),
            "PosStoreDay.AddProduct");

        VerifyProductStock(
            productReadRepository,
            product.Id,
            expectedStockX1000: 0,
            "PosStoreDay.InitialProductStock");

        StockMovement stockIn = CreateMovement(
            product.Id,
            StockMovementType.StockIn,
            QuantityX1000(5000),
            cashier.Id,
            Guid.NewGuid(),
            "POS-STORE-DAY",
            "POS store-day regression opening stock");

        RequireSuccess(
            inventoryWriteRepository.AddStockMovementAsync(stockIn).GetAwaiter().GetResult(),
            "PosStoreDay.StockIn");

        VerifyProductStock(
            productReadRepository,
            product.Id,
            expectedStockX1000: 5000,
            "PosStoreDay.StockAfterStockIn");

        CashierSessionReadModel openSession = RequireSuccess(
            cashierSessions.OpenAsync(new OpenCashierSessionRequest(
                cashier.Id,
                Money.FromCents(10000),
                "POS store-day regression open")).GetAwaiter().GetResult(),
            "PosStoreDay.OpenCashierSession");

        VerifyOpenSession(openSession, cashier.Id);

        PosCheckoutApplicationRequest checkoutRequest = CreateCheckoutRequest(
            cashier.Id,
            openSession.Id,
            product,
            seedIds.UnitName);

        RequireSuccess(
            checkoutApplication.CompleteSaleAsync(checkoutRequest).GetAwaiter().GetResult(),
            "PosStoreDay.CompleteSale");

        SaleDetailReadModel saleByReceipt = RequireSale(
            saleReadRepository.FindSaleByReceiptNoAsync(checkoutRequest.ReceiptNumber).GetAwaiter().GetResult(),
            "PosStoreDay.FindSaleByReceipt");

        VerifyCompletedSale(
            saleByReceipt,
            checkoutRequest,
            cashier.DisplayName,
            "PosStoreDay.CompletedSaleByReceipt");

        SaleDetailReadModel saleById = RequireSale(
            saleReadRepository.FindSaleByIdAsync(saleByReceipt.Id).GetAwaiter().GetResult(),
            "PosStoreDay.FindSaleById");

        VerifyCompletedSale(
            saleById,
            checkoutRequest,
            cashier.DisplayName,
            "PosStoreDay.CompletedSaleById");

        VerifySaleHistoryContains(
            saleReadRepository,
            saleByReceipt.Id,
            checkoutRequest,
            cashier.DisplayName,
            SaleStatus.Completed,
            "PosStoreDay.CompletedSaleHistory");

        VerifyProductStock(
            productReadRepository,
            product.Id,
            expectedStockX1000: 3000,
            "PosStoreDay.StockAfterSale");

        StockMovement reversal = CreateMovement(
            product.Id,
            StockMovementType.VoidSale,
            QuantityX1000(2000),
            cashier.Id,
            saleByReceipt.Id,
            "SALE",
            "POS store-day regression void reversal");

        RequireSuccess(
            saleWriteRepository.MarkVoidedAsync(
                saleByReceipt.Id,
                cashier.Id,
                "POS store-day regression void",
                new[] { reversal }).GetAwaiter().GetResult(),
            "PosStoreDay.VoidSale");

        SaleDetailReadModel voidedSale = RequireSale(
            saleReadRepository.FindSaleByIdAsync(saleByReceipt.Id).GetAwaiter().GetResult(),
            "PosStoreDay.FindVoidedSaleById");

        VerifyVoidedSale(
            voidedSale,
            checkoutRequest,
            cashier.DisplayName,
            "POS store-day regression void",
            "PosStoreDay.VoidedSaleById");

        VerifySaleHistoryContains(
            saleReadRepository,
            saleByReceipt.Id,
            checkoutRequest,
            cashier.DisplayName,
            SaleStatus.Voided,
            "PosStoreDay.VoidedSaleHistory");

        VerifyProductStock(
            productReadRepository,
            product.Id,
            expectedStockX1000: 5000,
            "PosStoreDay.StockAfterVoid");

        CashierSessionReadModel closedSession = RequireSuccess(
            cashierSessions.CloseAsync(new CloseCashierSessionRequest(
                openSession.Id,
                cashier.Id,
                Money.FromCents(10000),
                "POS store-day regression close")).GetAwaiter().GetResult(),
            "PosStoreDay.CloseCashierSession");

        VerifyClosedSession(closedSession, openSession.Id, cashier.Id);

        CashierSessionReadModel? finalOpen = RequireSuccess(
            cashierSessions.FindOpenSessionByUserIdAsync(cashier.Id).GetAwaiter().GetResult(),
            "PosStoreDay.FinalOpenSessionLookup");

        if (finalOpen is not null)
        {
            throw new InvalidOperationException("POS store-day regression left an open cashier session.");
        }

        Console.WriteLine("POS store-day regression smoke checks succeeded.");
        Console.WriteLine();
    }

    private static void RunSourceGuard(string projectRoot)
    {
        string smokePath = Path.Combine(
            projectRoot,
            "tools",
            "Salryn.Tools.InfrastructureSmoke",
            "PosStoreDayRegressionSmokeChecks.cs");

        string smokeText = File.ReadAllText(smokePath);

        RequireDoesNotContain(smokeText, "Application" + ".Run", "POS store-day command must not launch a WinForms application.");
        RequireDoesNotContain(smokeText, "Main" + "Form", "POS store-day command must not reference the WinForms startup form.");
        RequireDoesNotContain(smokeText, "Checkout" + "Presenter", "POS store-day command must not use checkout presenter state.");

        string uiRoot = Path.Combine(projectRoot, "src", "Salryn.App.WinForms");

        if (!Directory.Exists(uiRoot))
        {
            throw new InvalidOperationException("WinForms source root was not found for POS store-day source guard.");
        }

        foreach (string path in Directory.EnumerateFiles(uiRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (IsBuildOutputPath(path))
            {
                continue;
            }

            string text = File.ReadAllText(path);
            string relativePath = Path.GetRelativePath(projectRoot, path);

            RequireDoesNotContain(text, "Microsoft.Data.Sqlite", $"WinForms UI source must not reference SQLite directly: {relativePath}");
            RequireDoesNotContain(text, "SqliteConnection", $"WinForms UI source must not open SQLite connections directly: {relativePath}");
            RequireDoesNotContain(text, "SqliteCommand", $"WinForms UI source must not create SQLite commands directly: {relativePath}");
        }
    }

    private static bool IsBuildOutputPath(string path)
    {
        string normalized = path.Replace(Path.DirectorySeparatorChar, '/');

        return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
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
                "PosStoreDay.CheckStatus");

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
            "PosStoreDay.PreExistingOpenLookup");

        if (existingOpen is null)
        {
            return;
        }

        RequireSuccess(
            cashierSessions.ForceCloseAsync(new ForceCloseCashierSessionRequest(
                existingOpen.Id,
                cashierId,
                null,
                "POS store-day regression cleanup pre-existing open session")).GetAwaiter().GetResult(),
            "PosStoreDay.ForceClosePreExistingOpen");
    }

    private static SeedIds GetSeedIds(ICatalogReadRepository catalogReadRepository)
    {
        IReadOnlyList<CategoryReadModel> categories = RequireSuccess(
            catalogReadRepository.GetCategoriesAsync().GetAwaiter().GetResult(),
            "PosStoreDay.GetCategories");

        IReadOnlyList<BrandReadModel> brands = RequireSuccess(
            catalogReadRepository.GetBrandsAsync().GetAwaiter().GetResult(),
            "PosStoreDay.GetBrands");

        IReadOnlyList<UnitReadModel> units = RequireSuccess(
            catalogReadRepository.GetUnitsAsync().GetAwaiter().GetResult(),
            "PosStoreDay.GetUnits");

        if (categories.Count == 0 || brands.Count == 0 || units.Count == 0)
        {
            throw new InvalidOperationException("POS store-day regression seed lookup failed.");
        }

        return new SeedIds(
            categories[0].Id,
            brands[0].Id,
            units[0].Id,
            units[0].Name);
    }

    private static UserReadModel GetSmokeCashier(IUserReadRepository userReadRepository)
    {
        UserReadModel? admin = RequireSuccess(
            userReadRepository.FindByUsernameAsync("admin-smoke").GetAwaiter().GetResult(),
            "PosStoreDay.FindAdminSmoke");

        if (admin is not null)
        {
            return admin;
        }

        IReadOnlyList<UserReadModel> users = RequireSuccess(
            userReadRepository.GetAllAsync().GetAwaiter().GetResult(),
            "PosStoreDay.GetAllUsers");

        if (users.Count == 0)
        {
            throw new InvalidOperationException("POS store-day regression expected at least one smoke user.");
        }

        return users[0];
    }

    private static Product CreateTrackedProduct(SeedIds seedIds)
    {
        Barcode barcode = RequireValue(
            Barcode.Create(NewBarcode()),
            "PosStoreDay.Barcode");

        return new Product
        {
            Id = Guid.NewGuid(),
            Sku = "POS-DAY-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "-" + Random.Shared.Next(100, 999),
            Barcode = barcode,
            Name = "POS Store-Day Regression Product",
            Description = "Temporary POS store-day regression product.",
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
            Notes = "POS store-day regression smoke product",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = null
        };
    }

    private static PosCheckoutApplicationRequest CreateCheckoutRequest(
        Guid cashierId,
        Guid cashierSessionId,
        Product product,
        string unitName)
    {
        return new PosCheckoutApplicationRequest(
            cashierId,
            cashierSessionId,
            NewReceiptNo(),
            new[]
            {
                new PosCheckoutLineInput(
                    product.Id,
                    product.Name,
                    product.Barcode?.Value,
                    unitName,
                    QuantityX1000: 2000,
                    UnitPriceCents: product.SellingPrice.Cents,
                    CostPriceCents: product.CostPrice?.Cents,
                    DiscountCents: 0,
                    TrackStock: true)
            },
            new[]
            {
                new PosCheckoutPaymentInput(
                    PaymentMethod.Cash,
                    AmountCents: 5000)
            },
            SaleDiscountCents: 0,
            TaxCents: 0,
            ReceiptSnapshot: "POS store-day regression receipt snapshot.");
    }

    private static StockMovement CreateMovement(
        Guid productId,
        StockMovementType movementType,
        Quantity quantity,
        Guid userId,
        Guid referenceId,
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
            ReferenceId = referenceId,
            Reason = reason,
            UserId = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };
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
            throw new InvalidOperationException("POS store-day regression open session verification failed.");
        }
    }

    private static void VerifyClosedSession(
        CashierSessionReadModel closedSession,
        Guid expectedSessionId,
        Guid expectedCashierId)
    {
        if (closedSession.Id != expectedSessionId ||
            closedSession.UserId != expectedCashierId ||
            closedSession.Status != CashierSessionStatus.Closed ||
            closedSession.ClosedAt is null)
        {
            throw new InvalidOperationException("POS store-day regression close session verification failed.");
        }
    }

    private static void VerifyCompletedSale(
        SaleDetailReadModel sale,
        PosCheckoutApplicationRequest request,
        string expectedCashierDisplayName,
        string area)
    {
        VerifySaleCommon(
            sale,
            request,
            expectedCashierDisplayName,
            SaleStatus.Completed,
            expectedVoidReason: null,
            expectVoidedAt: false,
            area);
    }

    private static void VerifyVoidedSale(
        SaleDetailReadModel sale,
        PosCheckoutApplicationRequest request,
        string expectedCashierDisplayName,
        string expectedVoidReason,
        string area)
    {
        VerifySaleCommon(
            sale,
            request,
            expectedCashierDisplayName,
            SaleStatus.Voided,
            expectedVoidReason,
            expectVoidedAt: true,
            area);
    }

    private static void VerifySaleCommon(
        SaleDetailReadModel sale,
        PosCheckoutApplicationRequest request,
        string expectedCashierDisplayName,
        SaleStatus expectedStatus,
        string? expectedVoidReason,
        bool expectVoidedAt,
        string area)
    {
        PosCheckoutLineInput line = request.Lines[0];
        long expectedLineTotalCents = checked(line.UnitPriceCents * line.QuantityX1000 / 1000);
        long expectedTotalCents = checked(expectedLineTotalCents - request.SaleDiscountCents + request.TaxCents);
        long expectedAmountPaidCents = request.Payments.Sum(x => x.AmountCents);
        long expectedChangeCents = checked(expectedAmountPaidCents - expectedTotalCents);

        if (!string.Equals(sale.ReceiptNo, request.ReceiptNumber, StringComparison.Ordinal) ||
            !string.Equals(sale.CashierDisplayName, expectedCashierDisplayName, StringComparison.Ordinal) ||
            sale.Subtotal.Cents != expectedLineTotalCents ||
            sale.Discount.Cents != request.SaleDiscountCents ||
            sale.Tax.Cents != request.TaxCents ||
            sale.Total.Cents != expectedTotalCents ||
            sale.AmountPaid.Cents != expectedAmountPaidCents ||
            sale.Change.Cents != expectedChangeCents ||
            sale.Status != expectedStatus ||
            !string.Equals(sale.VoidReason, expectedVoidReason, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(sale.ReceiptSnapshot))
        {
            throw new InvalidOperationException($"{area} sale header verification failed.");
        }

        if (expectVoidedAt && sale.VoidedAt is null)
        {
            throw new InvalidOperationException($"{area} expected a void timestamp.");
        }

        if (!expectVoidedAt && sale.VoidedAt is not null)
        {
            throw new InvalidOperationException($"{area} had an unexpected void timestamp.");
        }

        if (sale.Items.Count != 1)
        {
            throw new InvalidOperationException($"{area} sale item count verification failed.");
        }

        SaleItemDetailReadModel item = sale.Items[0];

        if (item.ProductId != line.ProductId ||
            !string.Equals(item.ProductNameSnapshot, line.ProductNameSnapshot, StringComparison.Ordinal) ||
            !string.Equals(item.BarcodeSnapshot, line.BarcodeSnapshot, StringComparison.Ordinal) ||
            !string.Equals(item.UnitNameSnapshot, line.UnitNameSnapshot, StringComparison.Ordinal) ||
            item.Quantity.ValueX1000 != line.QuantityX1000 ||
            item.UnitPrice.Cents != line.UnitPriceCents ||
            item.CostPrice?.Cents != line.CostPriceCents ||
            item.Discount.Cents != line.DiscountCents ||
            item.LineTotal.Cents != expectedLineTotalCents)
        {
            throw new InvalidOperationException($"{area} sale item verification failed.");
        }

        if (sale.Payments.Count != 1)
        {
            throw new InvalidOperationException($"{area} payment count verification failed.");
        }

        PaymentReadModel payment = sale.Payments[0];
        PosCheckoutPaymentInput paymentInput = request.Payments[0];

        if (payment.Method != paymentInput.Method ||
            payment.Amount.Cents != paymentInput.AmountCents)
        {
            throw new InvalidOperationException($"{area} payment verification failed.");
        }
    }

    private static void VerifySaleHistoryContains(
        ISaleReadRepository saleReadRepository,
        Guid saleId,
        PosCheckoutApplicationRequest request,
        string expectedCashierDisplayName,
        SaleStatus expectedStatus,
        string area)
    {
        SaleHistoryFilter filter = new(
            From: DateTimeOffset.UtcNow.AddMinutes(-10),
            To: DateTimeOffset.UtcNow.AddMinutes(10),
            CashierUserId: request.CashierUserId,
            Status: expectedStatus,
            ReceiptNo: request.ReceiptNumber,
            Page: PageRequest.FirstPage());

        PagedResult<SaleHistoryItemReadModel> page = RequireSuccess(
            saleReadRepository.SearchSalesAsync(filter).GetAwaiter().GetResult(),
            area);

        SaleHistoryItemReadModel? item = page.Items.FirstOrDefault(x => x.Id == saleId);

        if (item is null)
        {
            throw new InvalidOperationException($"{area} did not return the sale.");
        }

        if (!string.Equals(item.ReceiptNo, request.ReceiptNumber, StringComparison.Ordinal) ||
            !string.Equals(item.CashierDisplayName, expectedCashierDisplayName, StringComparison.Ordinal) ||
            item.Status != expectedStatus)
        {
            throw new InvalidOperationException($"{area} sale history verification failed.");
        }
    }

    private static void VerifyProductStock(
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

    private static Quantity QuantityX1000(long valueX1000)
    {
        return RequireValue(
            Quantity.FromX1000(valueX1000),
            $"PosStoreDay.Quantity.{valueX1000}");
    }

    private static string NewBarcode()
    {
        return "91" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() + Random.Shared.Next(100, 999).ToString();
    }

    private static string NewReceiptNo()
    {
        return "DAY-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() + "-" + Random.Shared.Next(100, 999).ToString();
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

    private static void RequireDoesNotContain(
        string text,
        string value,
        string message)
    {
        if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record SeedIds(
        Guid CategoryId,
        Guid BrandId,
        Guid UnitId,
        string UnitName);
}
