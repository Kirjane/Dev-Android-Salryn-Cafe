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

internal static class PosCheckoutApplicationRepositorySmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== POS CHECKOUT APPLICATION REPOSITORY SMOKE CHECKS =====");

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

        PosCheckoutService checkoutCore = new(
            saleWriteRepository,
            cashierSessions);

        PosCheckoutApplicationService checkoutApplication = new(checkoutCore, productReadRepository);

        ForceCloseExistingOpenSession(
            cashierSessions,
            cashier.Id);

        Product trackedProduct = CreateProduct(
            seedIds,
            "Checkout Application Repository Tracked Product",
            trackStock: true,
            initialStockX1000: 5000);

        RequireSuccess(
            productWriteRepository.AddAsync(trackedProduct).GetAwaiter().GetResult(),
            "PosCheckoutAppRepo.AddTrackedProduct");

        VerifyProductReadStock(
            productReadRepository,
            trackedProduct.Id,
            expectedStockX1000: 5000,
            "PosCheckoutAppRepo.InitialTrackedProductStock");

        PosCheckoutApplicationRequest noOpenRequest = CreateRequest(
            cashier.Id,
            Guid.NewGuid(),
            trackedProduct,
            seedIds.UnitName,
            receiptPrefix: "APP-RI-NOOPEN",
            trackStockInput: true,
            quantityX1000: 1000,
            lineDiscountCents: 0,
            saleDiscountCents: 0,
            taxCents: 0,
            paymentBufferCents: 500);

        ExpectFailure(
            checkoutApplication.CompleteSaleAsync(noOpenRequest).GetAwaiter().GetResult(),
            "PosCheckoutAppRepo.OpenSessionRequired",
            ErrorType.Conflict,
            "Checkout.OpenCashierSessionRequired");

        VerifySaleMissingByReceipt(
            projectRoot,
            noOpenRequest.ReceiptNumber,
            "PosCheckoutAppRepo.NoOpenSaleMissing");

        VerifyProductReadStock(
            productReadRepository,
            trackedProduct.Id,
            expectedStockX1000: 5000,
            "PosCheckoutAppRepo.StockUnchangedAfterNoOpenFailure");

        CashierSessionReadModel openSession = RequireSuccess(
            cashierSessions.OpenAsync(new OpenCashierSessionRequest(
                cashier.Id,
                Money.FromCents(10000),
                "Phase 35 checkout application repository open")).GetAwaiter().GetResult(),
            "PosCheckoutAppRepo.OpenCashierSession");

        VerifyOpenSession(openSession, cashier.Id);

        PosCheckoutApplicationRequest trackedRequest = CreateRequest(
            cashier.Id,
            openSession.Id,
            trackedProduct,
            seedIds.UnitName,
            receiptPrefix: "APP-RI-TRACKED",
            trackStockInput: true,
            quantityX1000: 2000,
            lineDiscountCents: 100,
            saleDiscountCents: 200,
            taxCents: 50,
            paymentBufferCents: 250);

        RequireSuccess(
            checkoutApplication.CompleteSaleAsync(trackedRequest).GetAwaiter().GetResult(),
            "PosCheckoutAppRepo.CompleteTrackedCheckout");

        SaleDetailReadModel trackedSale = RequireSale(
            saleReadRepository.FindSaleByReceiptNoAsync(trackedRequest.ReceiptNumber).GetAwaiter().GetResult(),
            "PosCheckoutAppRepo.FindTrackedSale");

        VerifyApplicationSaleDetail(
            trackedSale,
            trackedRequest,
            cashier.DisplayName,
            "PosCheckoutAppRepo.TrackedSaleDetail");

        VerifySaleCashierSessionId(
            projectRoot,
            trackedSale.Id,
            openSession.Id,
            "PosCheckoutAppRepo.TrackedSaleSessionStored");

        VerifyStockMovementCount(
            projectRoot,
            trackedSale.Id,
            expectedCount: 1,
            "PosCheckoutAppRepo.TrackedStockMovementCount");

        VerifyProductReadStock(
            productReadRepository,
            trackedProduct.Id,
            expectedStockX1000: 3000,
            "PosCheckoutAppRepo.StockAfterTrackedCheckout");

        Product untrackedProduct = CreateProduct(
            seedIds,
            "Checkout Application Repository Untracked Product",
            trackStock: false,
            initialStockX1000: 5000);

        RequireSuccess(
            productWriteRepository.AddAsync(untrackedProduct).GetAwaiter().GetResult(),
            "PosCheckoutAppRepo.AddUntrackedProduct");

        VerifyProductReadStock(
            productReadRepository,
            untrackedProduct.Id,
            expectedStockX1000: 5000,
            "PosCheckoutAppRepo.InitialUntrackedProductStock");

        PosCheckoutApplicationRequest untrackedRequest = CreateRequest(
            cashier.Id,
            openSession.Id,
            untrackedProduct,
            seedIds.UnitName,
            receiptPrefix: "APP-RI-UNTRACKED",
            trackStockInput: false,
            quantityX1000: 1000,
            lineDiscountCents: 0,
            saleDiscountCents: 0,
            taxCents: 0,
            paymentBufferCents: 500);

        RequireSuccess(
            checkoutApplication.CompleteSaleAsync(untrackedRequest).GetAwaiter().GetResult(),
            "PosCheckoutAppRepo.CompleteUntrackedCheckout");

        SaleDetailReadModel untrackedSale = RequireSale(
            saleReadRepository.FindSaleByReceiptNoAsync(untrackedRequest.ReceiptNumber).GetAwaiter().GetResult(),
            "PosCheckoutAppRepo.FindUntrackedSale");

        VerifyApplicationSaleDetail(
            untrackedSale,
            untrackedRequest,
            cashier.DisplayName,
            "PosCheckoutAppRepo.UntrackedSaleDetail");

        VerifySaleCashierSessionId(
            projectRoot,
            untrackedSale.Id,
            openSession.Id,
            "PosCheckoutAppRepo.UntrackedSaleSessionStored");

        VerifyStockMovementCount(
            projectRoot,
            untrackedSale.Id,
            expectedCount: 0,
            "PosCheckoutAppRepo.UntrackedStockMovementCount");

        VerifyProductReadStock(
            productReadRepository,
            untrackedProduct.Id,
            expectedStockX1000: 5000,
            "PosCheckoutAppRepo.StockUnchangedAfterUntrackedCheckout");

        CashierSessionReadModel closedSession = RequireSuccess(
            cashierSessions.CloseAsync(new CloseCashierSessionRequest(
                openSession.Id,
                cashier.Id,
                Money.FromCents(10000),
                "Phase 35 checkout application repository close")).GetAwaiter().GetResult(),
            "PosCheckoutAppRepo.CloseCashierSession");

        if (closedSession.Id != openSession.Id ||
            closedSession.Status != CashierSessionStatus.Closed ||
            closedSession.ClosedAt is null)
        {
            throw new InvalidOperationException("Checkout application repository close session verification failed.");
        }

        CashierSessionReadModel? finalOpen = RequireSuccess(
            cashierSessions.FindOpenSessionByUserIdAsync(cashier.Id).GetAwaiter().GetResult(),
            "PosCheckoutAppRepo.FinalNoOpen");

        if (finalOpen is not null)
        {
            throw new InvalidOperationException("Checkout application repository smoke left an open cashier session.");
        }

        Console.WriteLine("POS checkout application repository smoke checks succeeded.");
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
                "PosCheckoutAppRepo.CheckStatus");

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
            "PosCheckoutAppRepo.PreExistingOpenLookup");

        if (existingOpen is null)
        {
            return;
        }

        RequireSuccess(
            cashierSessions.ForceCloseAsync(new ForceCloseCashierSessionRequest(
                existingOpen.Id,
                cashierId,
                null,
                "Phase 35 cleanup pre-existing open session")).GetAwaiter().GetResult(),
            "PosCheckoutAppRepo.ForceClosePreExistingOpen");
    }

    private static SeedIds GetSeedIds(ICatalogReadRepository catalogRepository)
    {
        IReadOnlyList<CategoryReadModel> categories = RequireSuccess(
            catalogRepository.GetCategoriesAsync().GetAwaiter().GetResult(),
            "PosCheckoutAppRepo.GetCategories");

        IReadOnlyList<BrandReadModel> brands = RequireSuccess(
            catalogRepository.GetBrandsAsync().GetAwaiter().GetResult(),
            "PosCheckoutAppRepo.GetBrands");

        IReadOnlyList<UnitReadModel> units = RequireSuccess(
            catalogRepository.GetUnitsAsync().GetAwaiter().GetResult(),
            "PosCheckoutAppRepo.GetUnits");

        if (categories.Count == 0 || brands.Count == 0 || units.Count == 0)
        {
            throw new InvalidOperationException("Checkout application repository seed lookup failed.");
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
            "PosCheckoutAppRepo.FindAdminSmoke");

        if (admin is not null)
        {
            return admin;
        }

        IReadOnlyList<UserReadModel> users = RequireSuccess(
            userRepository.GetAllAsync().GetAwaiter().GetResult(),
            "PosCheckoutAppRepo.GetAllUsers");

        if (users.Count == 0)
        {
            throw new InvalidOperationException("Checkout application repository expected at least one user.");
        }

        return users[0];
    }

    private static Product CreateProduct(
        SeedIds seedIds,
        string name,
        bool trackStock,
        long initialStockX1000)
    {
        Barcode barcode = RequireValue(
            Barcode.Create(NewBarcode()),
            "PosCheckoutAppRepo.Barcode");

        return new Product
        {
            Id = Guid.NewGuid(),
            Sku = "CHK-APP-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "-" + Random.Shared.Next(100, 999),
            Barcode = barcode,
            Name = name,
            Description = "Temporary checkout application repository smoke product.",
            CategoryId = seedIds.CategoryId,
            BrandId = seedIds.BrandId,
            UnitId = seedIds.UnitId,
            SupplierId = null,
            CostPrice = Money.FromCents(1000),
            SellingPrice = Money.FromCents(1500),
            WholesalePrice = null,
            StockOnHand = QuantityX1000(initialStockX1000),
            ReorderLevel = QuantityX1000(1000),
            TrackStock = trackStock,
            AllowDiscount = true,
            IsActive = true,
            Notes = "Checkout application repository smoke product",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = null
        };
    }

    private static PosCheckoutApplicationRequest CreateRequest(
        Guid cashierId,
        Guid cashierSessionId,
        Product product,
        string unitName,
        string receiptPrefix,
        bool trackStockInput,
        long quantityX1000,
        long lineDiscountCents,
        long saleDiscountCents,
        long taxCents,
        long paymentBufferCents)
    {
        Quantity quantity = QuantityX1000(quantityX1000);
        long grossLineTotalCents = CalculateLineGrossCents(product.SellingPrice, quantity);
        long lineTotalCents = checked(grossLineTotalCents - lineDiscountCents);
        long totalCents = checked(lineTotalCents - saleDiscountCents + taxCents);
        long amountPaidCents = checked(totalCents + paymentBufferCents);

        return new PosCheckoutApplicationRequest(
            cashierId,
            cashierSessionId,
            NewReceiptNo(receiptPrefix),
            new[]
            {
                new PosCheckoutLineInput(
                    product.Id,
                    product.Name,
                    product.Barcode?.Value,
                    unitName,
                    quantityX1000,
                    product.SellingPrice.Cents,
                    product.CostPrice?.Cents,
                    lineDiscountCents,
                    trackStockInput)
            },
            new[]
            {
                new PosCheckoutPaymentInput(
                    PaymentMethod.Cash,
                    amountPaidCents)
            },
            saleDiscountCents,
            taxCents,
            "Phase 35 checkout application repository receipt snapshot.");
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
            throw new InvalidOperationException("Checkout application repository open session verification failed.");
        }
    }

    private static void VerifyApplicationSaleDetail(
        SaleDetailReadModel detail,
        PosCheckoutApplicationRequest request,
        string expectedCashierDisplayName,
        string area)
    {
        PosCheckoutLineInput line = request.Lines[0];
        PosCheckoutPaymentInput paymentInput = request.Payments[0];

        Quantity quantity = QuantityX1000(line.QuantityX1000);
        long grossLineTotalCents = CalculateLineGrossCents(Money.FromCents(line.UnitPriceCents), quantity);
        long lineTotalCents = checked(grossLineTotalCents - line.DiscountCents);
        long totalCents = checked(lineTotalCents - request.SaleDiscountCents + request.TaxCents);
        long amountPaidCents = request.Payments.Sum(x => x.AmountCents);
        long changeCents = checked(amountPaidCents - totalCents);

        if (!string.Equals(detail.ReceiptNo, request.ReceiptNumber, StringComparison.Ordinal) ||
            !string.Equals(detail.CashierDisplayName, expectedCashierDisplayName, StringComparison.Ordinal) ||
            detail.Subtotal.Cents != lineTotalCents ||
            detail.Discount.Cents != request.SaleDiscountCents ||
            detail.Tax.Cents != request.TaxCents ||
            detail.Total.Cents != totalCents ||
            detail.AmountPaid.Cents != amountPaidCents ||
            detail.Change.Cents != changeCents ||
            detail.Status != SaleStatus.Completed ||
            detail.VoidReason is not null ||
            detail.VoidedAt is not null ||
            string.IsNullOrWhiteSpace(detail.ReceiptSnapshot))
        {
            throw new InvalidOperationException($"{area} sale header verification failed.");
        }

        if (detail.Items.Count != 1)
        {
            throw new InvalidOperationException($"{area} sale item count verification failed.");
        }

        SaleItemDetailReadModel item = detail.Items[0];

        if (item.ProductId != line.ProductId ||
            !string.Equals(item.ProductNameSnapshot, line.ProductNameSnapshot, StringComparison.Ordinal) ||
            !string.Equals(item.BarcodeSnapshot, line.BarcodeSnapshot, StringComparison.Ordinal) ||
            !string.Equals(item.UnitNameSnapshot, line.UnitNameSnapshot, StringComparison.Ordinal) ||
            item.Quantity.ValueX1000 != line.QuantityX1000 ||
            item.UnitPrice.Cents != line.UnitPriceCents ||
            item.CostPrice?.Cents != line.CostPriceCents ||
            item.Discount.Cents != line.DiscountCents ||
            item.LineTotal.Cents != lineTotalCents)
        {
            throw new InvalidOperationException($"{area} sale item verification failed.");
        }

        if (detail.Payments.Count != 1)
        {
            throw new InvalidOperationException($"{area} payment count verification failed.");
        }

        PaymentReadModel payment = detail.Payments[0];

        if (payment.Method != paymentInput.Method ||
            payment.Amount.Cents != paymentInput.AmountCents)
        {
            throw new InvalidOperationException($"{area} payment verification failed.");
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

    private static void VerifySaleMissingByReceipt(
        string projectRoot,
        string receiptNo,
        string area)
    {
        using SqliteConnection connection = OpenConnection(projectRoot);
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = "SELECT COUNT(*) FROM sales WHERE receipt_no = $receiptNo;";
        command.Parameters.AddWithValue("$receiptNo", receiptNo);

        int count = Convert.ToInt32(command.ExecuteScalar() ?? 0);

        if (count != 0)
        {
            throw new InvalidOperationException($"{area} expected sale to be missing.");
        }
    }

    private static void VerifyStockMovementCount(
        string projectRoot,
        Guid saleId,
        int expectedCount,
        string area)
    {
        using SqliteConnection connection = OpenConnection(projectRoot);
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = """
            SELECT COUNT(*)
            FROM stock_movements
            WHERE reference_type = 'SALE'
              AND reference_id = $saleId;
            """;

        command.Parameters.AddWithValue("$saleId", saleId.ToString("D"));

        int actualCount = Convert.ToInt32(command.ExecuteScalar() ?? 0);

        if (actualCount != expectedCount)
        {
            throw new InvalidOperationException(
                $"{area} expected {expectedCount} stock movements, got {actualCount}.");
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
            $"PosCheckoutAppRepo.Quantity.{valueX1000}");
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
        return "98" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() + Random.Shared.Next(100, 999).ToString();
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
}

