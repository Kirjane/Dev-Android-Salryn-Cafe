using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Paths;

internal static class SalesReadWriteIntegrationSmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== SALES READ/WRITE INTEGRATION SMOKE CHECKS =====");

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

        SeedIds seedIds = GetSeedIds(catalogRepository);
        UserReadModel cashier = GetSmokeCashier(userRepository);

        Product product = CreateProduct(seedIds);

        RequireSuccess(
            productWriteRepository.AddAsync(product).GetAwaiter().GetResult(),
            "SalesReadWrite.AddProduct");

        VerifyProductReadStock(
            productReadRepository,
            product.Id,
            expectedStockX1000: 5000,
            "SalesReadWrite.InitialProductStock");

        SalePackage package = CreatePackage(
            product,
            seedIds.UnitName,
            cashier.Id);

        RequireSuccess(
            saleWriteRepository.AddCompletedSaleAsync(
                package.Sale,
                new[] { package.Item },
                new[] { package.Payment },
                new[] { package.Movement }).GetAwaiter().GetResult(),
            "SalesReadWrite.AddCompletedSale");

        VerifyProductReadStock(
            productReadRepository,
            product.Id,
            expectedStockX1000: 3000,
            "SalesReadWrite.StockAfterSale");

        SaleDetailReadModel byId = RequireSale(
            saleReadRepository.FindSaleByIdAsync(package.Sale.Id).GetAwaiter().GetResult(),
            "SalesReadWrite.FindSaleById");

        VerifySaleDetail(
            byId,
            package,
            cashier.DisplayName,
            SaleStatus.Completed,
            expectedVoidReason: null,
            expectVoidedAt: false);

        SaleDetailReadModel byReceipt = RequireSale(
            saleReadRepository.FindSaleByReceiptNoAsync(package.Sale.ReceiptNumber).GetAwaiter().GetResult(),
            "SalesReadWrite.FindSaleByReceiptNo");

        VerifySaleDetail(
            byReceipt,
            package,
            cashier.DisplayName,
            SaleStatus.Completed,
            expectedVoidReason: null,
            expectVoidedAt: false);

        VerifySaleHistoryContains(
            saleReadRepository,
            package.Sale,
            cashier.DisplayName,
            SaleStatus.Completed,
            "SalesReadWrite.SearchCompletedSale");

        StockMovement reversal = CreateMovement(
            Guid.NewGuid(),
            product.Id,
            StockMovementType.VoidSale,
            QuantityX1000(2000),
            cashier.Id,
            package.Sale.Id,
            "SALE",
            "Phase 25 read/write void reversal");

        RequireSuccess(
            saleWriteRepository.MarkVoidedAsync(
                package.Sale.Id,
                cashier.Id,
                "Phase 25 integration void",
                new[] { reversal }).GetAwaiter().GetResult(),
            "SalesReadWrite.MarkVoided");

        VerifyProductReadStock(
            productReadRepository,
            product.Id,
            expectedStockX1000: 5000,
            "SalesReadWrite.StockAfterVoid");

        SaleDetailReadModel voidedById = RequireSale(
            saleReadRepository.FindSaleByIdAsync(package.Sale.Id).GetAwaiter().GetResult(),
            "SalesReadWrite.FindVoidedSaleById");

        VerifySaleDetail(
            voidedById,
            package,
            cashier.DisplayName,
            SaleStatus.Voided,
            expectedVoidReason: "Phase 25 integration void",
            expectVoidedAt: true);

        SaleDetailReadModel voidedByReceipt = RequireSale(
            saleReadRepository.FindSaleByReceiptNoAsync(package.Sale.ReceiptNumber).GetAwaiter().GetResult(),
            "SalesReadWrite.FindVoidedSaleByReceiptNo");

        VerifySaleDetail(
            voidedByReceipt,
            package,
            cashier.DisplayName,
            SaleStatus.Voided,
            expectedVoidReason: "Phase 25 integration void",
            expectVoidedAt: true);

        VerifySaleHistoryContains(
            saleReadRepository,
            package.Sale,
            cashier.DisplayName,
            SaleStatus.Voided,
            "SalesReadWrite.SearchVoidedSale");

        Console.WriteLine("Sales read/write integration smoke checks succeeded.");
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
                "SalesReadWrite.CheckStatus");

            needsRepositoryBootstrap =
                !status.FirstRunStatus.IsDatabaseInitialized ||
                status.FirstRunStatus.IsFirstRunRequired;
        }

        if (needsRepositoryBootstrap)
        {
            RepositorySmokeChecks.Run(projectRoot);
        }
    }

    private static SeedIds GetSeedIds(ICatalogReadRepository catalogRepository)
    {
        IReadOnlyList<CategoryReadModel> categories = RequireSuccess(
            catalogRepository.GetCategoriesAsync().GetAwaiter().GetResult(),
            "SalesReadWrite.GetCategories");

        IReadOnlyList<BrandReadModel> brands = RequireSuccess(
            catalogRepository.GetBrandsAsync().GetAwaiter().GetResult(),
            "SalesReadWrite.GetBrands");

        IReadOnlyList<UnitReadModel> units = RequireSuccess(
            catalogRepository.GetUnitsAsync().GetAwaiter().GetResult(),
            "SalesReadWrite.GetUnits");

        if (categories.Count == 0 || brands.Count == 0 || units.Count == 0)
        {
            throw new InvalidOperationException("Sales read/write integration seed lookup failed.");
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
            "SalesReadWrite.FindAdminSmoke");

        if (admin is not null)
        {
            return admin;
        }

        IReadOnlyList<UserReadModel> users = RequireSuccess(
            userRepository.GetAllAsync().GetAwaiter().GetResult(),
            "SalesReadWrite.GetAllUsers");

        if (users.Count == 0)
        {
            throw new InvalidOperationException("Sales read/write integration expected at least one user.");
        }

        return users[0];
    }

    private static Product CreateProduct(SeedIds seedIds)
    {
        Barcode barcode = RequireValue(
            Barcode.Create(NewBarcode()),
            "SalesReadWrite.Barcode");

        return new Product
        {
            Id = Guid.NewGuid(),
            Sku = "SALES-RW-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "-" + Random.Shared.Next(100, 999),
            Barcode = barcode,
            Name = "Sales Read Write Product",
            Description = "Temporary sales read/write integration product.",
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
            Notes = "Sales read/write integration smoke product",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = null
        };
    }

    private static SalePackage CreatePackage(
        Product product,
        string unitName,
        Guid cashierUserId)
    {
        Guid saleId = Guid.NewGuid();
        string receiptNo = NewReceiptNo();

        Sale sale = new()
        {
            Id = saleId,
            ReceiptNumber = receiptNo,
            SaleDate = DateTimeOffset.UtcNow,
            CashierUserId = cashierUserId,
            CashierSessionId = null,
            Subtotal = Money.FromCents(3000),
            Discount = Money.Zero,
            Tax = Money.Zero,
            Total = Money.FromCents(3000),
            AmountPaid = Money.FromCents(5000),
            Change = Money.FromCents(2000),
            Status = SaleStatus.Completed,
            VoidedByUserId = null,
            VoidReason = null,
            VoidedAt = null,
            ReceiptSnapshot = "Phase 25 read/write integration receipt snapshot.",
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
            Quantity = QuantityX1000(2000),
            UnitPrice = Money.FromCents(1500),
            CostPrice = product.CostPrice,
            Discount = Money.Zero,
            LineTotal = Money.FromCents(3000)
        };

        Payment payment = new()
        {
            Id = Guid.NewGuid(),
            SaleId = saleId,
            Method = PaymentMethod.Cash,
            Amount = Money.FromCents(5000),
            PaidAt = DateTimeOffset.UtcNow
        };

        StockMovement movement = CreateMovement(
            Guid.NewGuid(),
            product.Id,
            StockMovementType.Sale,
            QuantityX1000(2000),
            cashierUserId,
            saleId,
            "SALE",
            "Phase 25 read/write sale stock-out");

        return new SalePackage(sale, item, payment, movement);
    }

    private static StockMovement CreateMovement(
        Guid id,
        Guid productId,
        StockMovementType movementType,
        Quantity quantity,
        Guid userId,
        Guid saleId,
        string referenceType,
        string reason)
    {
        return new StockMovement
        {
            Id = id,
            ProductId = productId,
            MovementType = movementType,
            QuantityChange = quantity,
            CostPrice = Money.FromCents(1000),
            ReferenceType = referenceType,
            ReferenceId = saleId,
            Reason = reason,
            UserId = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static void VerifySaleDetail(
        SaleDetailReadModel detail,
        SalePackage package,
        string expectedCashierDisplayName,
        SaleStatus expectedStatus,
        string? expectedVoidReason,
        bool expectVoidedAt)
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
            detail.Status != expectedStatus ||
            !string.Equals(detail.VoidReason, expectedVoidReason, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(detail.ReceiptSnapshot))
        {
            throw new InvalidOperationException("Sale detail header verification failed.");
        }

        if (expectVoidedAt && detail.VoidedAt is null)
        {
            throw new InvalidOperationException("Sale detail voided_at verification failed.");
        }

        if (!expectVoidedAt && detail.VoidedAt is not null)
        {
            throw new InvalidOperationException("Sale detail unexpected voided_at verification failed.");
        }

        if (detail.Items.Count != 1)
        {
            throw new InvalidOperationException("Sale detail item count verification failed.");
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
            throw new InvalidOperationException("Sale detail item verification failed.");
        }

        if (detail.Payments.Count != 1)
        {
            throw new InvalidOperationException("Sale detail payment count verification failed.");
        }

        PaymentReadModel payment = detail.Payments[0];

        if (payment.Id != package.Payment.Id ||
            payment.Method != package.Payment.Method ||
            payment.Amount.Cents != package.Payment.Amount.Cents)
        {
            throw new InvalidOperationException("Sale detail payment verification failed.");
        }
    }

    private static void VerifySaleHistoryContains(
        ISaleReadRepository saleReadRepository,
        Sale sale,
        string expectedCashierDisplayName,
        SaleStatus expectedStatus,
        string area)
    {
        SaleHistoryFilter filter = new(
            From: sale.SaleDate.AddMinutes(-5),
            To: DateTimeOffset.UtcNow.AddMinutes(5),
            CashierUserId: sale.CashierUserId,
            Status: expectedStatus,
            ReceiptNo: sale.ReceiptNumber,
            Page: PageRequest.FirstPage());

        PagedResult<SaleHistoryItemReadModel> page = RequireSuccess(
            saleReadRepository.SearchSalesAsync(filter).GetAwaiter().GetResult(),
            area);

        SaleHistoryItemReadModel? item = page.Items.FirstOrDefault(x => x.Id == sale.Id);

        if (item is null)
        {
            throw new InvalidOperationException($"{area} did not return the sale.");
        }

        if (!string.Equals(item.ReceiptNo, sale.ReceiptNumber, StringComparison.Ordinal) ||
            !string.Equals(item.CashierDisplayName, expectedCashierDisplayName, StringComparison.Ordinal) ||
            item.Total.Cents != sale.Total.Cents ||
            item.AmountPaid.Cents != sale.AmountPaid.Cents ||
            item.Change.Cents != sale.Change.Cents ||
            item.Status != expectedStatus)
        {
            throw new InvalidOperationException($"{area} sale history verification failed.");
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

    private static Quantity QuantityX1000(long valueX1000)
    {
        return RequireValue(
            Quantity.FromX1000(valueX1000),
            $"SalesReadWrite.Quantity.{valueX1000}");
    }

    private static string NewBarcode()
    {
        return "93" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() + Random.Shared.Next(100, 999).ToString();
    }

    private static string NewReceiptNo()
    {
        return "RW-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() + "-" + Random.Shared.Next(100, 999).ToString();
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
