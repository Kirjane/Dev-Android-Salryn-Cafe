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

internal static class SalesWriteEdgeSmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== SALES WRITE EDGE SMOKE CHECKS =====");

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

        Product stockTracked = CreateProduct(
            seedIds,
            "SALES-EDGE-STOCK",
            "Sales Edge Stock Product",
            5000,
            trackStock: true);

        Product nonStockTracked = CreateProduct(
            seedIds,
            "SALES-EDGE-NONSTOCK",
            "Sales Edge Non Stock Product",
            0,
            trackStock: false);

        Product inactiveProduct = CreateProduct(
            seedIds,
            "SALES-EDGE-INACTIVE",
            "Sales Edge Inactive Product",
            1000,
            trackStock: true);

        RequireSuccess(
            productWriteRepository.AddAsync(stockTracked).GetAwaiter().GetResult(),
            "SalesEdge.AddStockTracked");

        RequireSuccess(
            productWriteRepository.AddAsync(nonStockTracked).GetAwaiter().GetResult(),
            "SalesEdge.AddNonStockTracked");

        RequireSuccess(
            productWriteRepository.AddAsync(inactiveProduct).GetAwaiter().GetResult(),
            "SalesEdge.AddInactiveCandidate");

        RequireSuccess(
            productWriteRepository.DeactivateAsync(
                inactiveProduct.Id,
                "Phase 24 sales edge inactive product",
                adminUserId).GetAwaiter().GetResult(),
            "SalesEdge.DeactivateInactiveCandidate");

        VerifyProductStock(projectRoot, stockTracked.Id, 5000);

        SalePackage baseline = CreatePackage(
            stockTracked,
            seedIds.UnitName,
            adminUserId);

        RequireSuccess(
            AddPackage(saleWriteRepository, baseline),
            "SalesEdge.AddBaselineSale");

        VerifyProductStock(projectRoot, stockTracked.Id, 4000);
        VerifySaleExists(projectRoot, baseline.Sale.Id, SaleStatus.Completed);
        VerifyStockMovementExists(projectRoot, baseline.Movement.Id, -1000);

        ExpectFailure(
            AddPackage(saleWriteRepository, CreatePackage(
                stockTracked,
                seedIds.UnitName,
                adminUserId,
                saleIdOverride: Guid.Empty)),
            "SalesEdge.InvalidSaleId",
            ErrorType.Validation,
            "Sales.InvalidSaleId");

        ExpectFailure(
            AddPackage(saleWriteRepository, CreatePackage(
                stockTracked,
                seedIds.UnitName,
                adminUserId,
                receiptNoOverride: "   ")),
            "SalesEdge.BlankReceipt",
            ErrorType.Validation,
            "Sales.ReceiptNumberRequired");

        ExpectFailure(
            AddPackage(saleWriteRepository, CreatePackage(
                stockTracked,
                seedIds.UnitName,
                Guid.Empty)),
            "SalesEdge.EmptyCashier",
            ErrorType.Validation,
            "Sales.InvalidCashierUserId");

        ExpectFailure(
            AddPackage(saleWriteRepository, CreatePackage(
                stockTracked,
                seedIds.UnitName,
                adminUserId,
                status: SaleStatus.Draft)),
            "SalesEdge.DraftStatus",
            ErrorType.Validation,
            "Sales.CompletedStatusRequired");

        ExpectFailure(
            AddPackage(saleWriteRepository, CreatePackage(
                stockTracked,
                seedIds.UnitName,
                adminUserId,
                withVoidMetadata: true)),
            "SalesEdge.CompletedWithVoidMetadata",
            ErrorType.Validation,
            "Sales.CompletedSaleCannotHaveVoidMetadata");

        SalePackage emptyItems = CreatePackage(stockTracked, seedIds.UnitName, adminUserId);

        ExpectFailure(
            saleWriteRepository.AddCompletedSaleAsync(
                emptyItems.Sale,
                Array.Empty<SaleItem>(),
                new[] { emptyItems.Payment },
                new[] { emptyItems.Movement }).GetAwaiter().GetResult(),
            "SalesEdge.EmptyItems",
            ErrorType.Validation,
            "Sales.EmptySaleItems");

        SalePackage emptyPayments = CreatePackage(stockTracked, seedIds.UnitName, adminUserId);

        ExpectFailure(
            saleWriteRepository.AddCompletedSaleAsync(
                emptyPayments.Sale,
                new[] { emptyPayments.Item },
                Array.Empty<Payment>(),
                new[] { emptyPayments.Movement }).GetAwaiter().GetResult(),
            "SalesEdge.EmptyPayments",
            ErrorType.Validation,
            "Sales.EmptyPayments");

        ExpectFailure(
            AddPackage(saleWriteRepository, CreatePackage(
                stockTracked,
                seedIds.UnitName,
                adminUserId,
                totalCentsOverride: 900,
                amountPaidCentsOverride: 900,
                changeCentsOverride: 0,
                paymentAmountCentsOverride: 900)),
            "SalesEdge.TotalMismatch",
            ErrorType.Validation,
            "Sales.TotalMismatch");

        ExpectFailure(
            AddPackage(saleWriteRepository, CreatePackage(
                stockTracked,
                seedIds.UnitName,
                adminUserId,
                amountPaidCentsOverride: 500,
                changeCentsOverride: 0,
                paymentAmountCentsOverride: 500)),
            "SalesEdge.InsufficientPayment",
            ErrorType.Validation,
            "Sales.InsufficientPayment");

        ExpectFailure(
            AddPackage(saleWriteRepository, CreatePackage(
                stockTracked,
                seedIds.UnitName,
                adminUserId,
                amountPaidCentsOverride: 1000,
                changeCentsOverride: 999,
                paymentAmountCentsOverride: 1000)),
            "SalesEdge.ChangeMismatch",
            ErrorType.Validation,
            "Sales.ChangeMismatch");

        ExpectFailure(
            AddPackage(saleWriteRepository, CreatePackage(
                stockTracked,
                seedIds.UnitName,
                adminUserId,
                itemSaleIdOverride: Guid.NewGuid())),
            "SalesEdge.ItemSaleMismatch",
            ErrorType.Validation,
            "Sales.SaleItemSaleIdMismatch");

        ExpectFailure(
            AddPackage(saleWriteRepository, CreatePackage(
                stockTracked,
                seedIds.UnitName,
                adminUserId,
                quantityX1000: 0,
                subtotalCentsOverride: 0,
                totalCentsOverride: 0,
                amountPaidCentsOverride: 1,
                changeCentsOverride: 1,
                paymentAmountCentsOverride: 1)),
            "SalesEdge.ZeroItemQuantity",
            ErrorType.Validation,
            "Sales.ItemQuantityRequired");

        ExpectFailure(
            AddPackage(saleWriteRepository, CreatePackage(
                stockTracked,
                seedIds.UnitName,
                adminUserId,
                lineTotalCentsOverride: 999)),
            "SalesEdge.LineTotalMismatch",
            ErrorType.Validation,
            "Sales.LineTotalMismatch");

        ExpectFailure(
            AddPackage(saleWriteRepository, CreatePackage(
                stockTracked,
                seedIds.UnitName,
                adminUserId,
                itemDiscountCents: 2000,
                lineTotalCentsOverride: 0,
                subtotalCentsOverride: 0,
                totalCentsOverride: 0,
                amountPaidCentsOverride: 1,
                changeCentsOverride: 1,
                paymentAmountCentsOverride: 1)),
            "SalesEdge.ItemDiscountExceedsGross",
            ErrorType.Validation,
            "Sales.ItemDiscountExceedsLineGross");

        ExpectFailure(
            AddPackage(saleWriteRepository, CreatePackage(
                stockTracked,
                seedIds.UnitName,
                adminUserId,
                subtotalCentsOverride: 2000,
                totalCentsOverride: 2000,
                amountPaidCentsOverride: 2000,
                changeCentsOverride: 0,
                paymentAmountCentsOverride: 2000)),
            "SalesEdge.SubtotalMismatch",
            ErrorType.Validation,
            "Sales.SubtotalMismatch");

        ExpectFailure(
            AddPackage(saleWriteRepository, CreatePackage(
                stockTracked,
                seedIds.UnitName,
                adminUserId,
                paymentSaleIdOverride: Guid.NewGuid())),
            "SalesEdge.PaymentSaleMismatch",
            ErrorType.Validation,
            "Sales.PaymentSaleIdMismatch");

        ExpectFailure(
            AddPackage(saleWriteRepository, CreatePackage(
                stockTracked,
                seedIds.UnitName,
                adminUserId,
                paymentMethod: (PaymentMethod)999)),
            "SalesEdge.InvalidPaymentMethod",
            ErrorType.Validation,
            "Sales.InvalidPaymentMethod");

        ExpectFailure(
            AddPackage(saleWriteRepository, CreatePackage(
                stockTracked,
                seedIds.UnitName,
                adminUserId,
                paymentAmountCentsOverride: 0)),
            "SalesEdge.ZeroPayment",
            ErrorType.Validation,
            "Sales.PaymentAmountRequired");

        ExpectFailure(
            AddPackage(saleWriteRepository, CreatePackage(
                stockTracked,
                seedIds.UnitName,
                adminUserId,
                paymentAmountCentsOverride: 500)),
            "SalesEdge.PaymentTotalMismatch",
            ErrorType.Validation,
            "Sales.PaymentTotalMismatch");

        SalePackage duplicateItemPackage = CreatePackage(
            stockTracked,
            seedIds.UnitName,
            adminUserId,
            subtotalCentsOverride: 2000,
            totalCentsOverride: 2000,
            amountPaidCentsOverride: 2000,
            changeCentsOverride: 0,
            paymentAmountCentsOverride: 2000);

        SaleItem duplicateItem = CreateSaleItem(
            duplicateItemPackage.Item.Id,
            duplicateItemPackage.Sale.Id,
            stockTracked,
            seedIds.UnitName,
            1000,
            1000,
            0,
            1000);

        ExpectFailure(
            saleWriteRepository.AddCompletedSaleAsync(
                duplicateItemPackage.Sale,
                new[] { duplicateItemPackage.Item, duplicateItem },
                new[] { duplicateItemPackage.Payment },
                Array.Empty<StockMovement>()).GetAwaiter().GetResult(),
            "SalesEdge.DuplicateSaleItemId",
            ErrorType.Validation,
            "Sales.DuplicateSaleItemId");

        SalePackage duplicatePaymentPackage = CreatePackage(stockTracked, seedIds.UnitName, adminUserId);

        Payment duplicatePayment = CreatePayment(
            duplicatePaymentPackage.Payment.Id,
            duplicatePaymentPackage.Sale.Id,
            1000,
            PaymentMethod.Cash);

        ExpectFailure(
            saleWriteRepository.AddCompletedSaleAsync(
                duplicatePaymentPackage.Sale,
                new[] { duplicatePaymentPackage.Item },
                new[] { duplicatePaymentPackage.Payment, duplicatePayment },
                Array.Empty<StockMovement>()).GetAwaiter().GetResult(),
            "SalesEdge.DuplicatePaymentId",
            ErrorType.Validation,
            "Sales.DuplicatePaymentId");

        SalePackage duplicateMovementPackage = CreatePackage(stockTracked, seedIds.UnitName, adminUserId);

        StockMovement duplicateMovement = CreateMovement(
            duplicateMovementPackage.Movement.Id,
            stockTracked.Id,
            StockMovementType.Sale,
            1000,
            adminUserId,
            duplicateMovementPackage.Sale.Id,
            "SALE");

        ExpectFailure(
            saleWriteRepository.AddCompletedSaleAsync(
                duplicateMovementPackage.Sale,
                new[] { duplicateMovementPackage.Item },
                new[] { duplicateMovementPackage.Payment },
                new[] { duplicateMovementPackage.Movement, duplicateMovement }).GetAwaiter().GetResult(),
            "SalesEdge.DuplicateStockMovementId",
            ErrorType.Validation,
            "Sales.DuplicateStockMovementId");

        ExpectFailure(
            AddPackage(saleWriteRepository, CreatePackage(
                stockTracked,
                seedIds.UnitName,
                adminUserId,
                movementType: StockMovementType.StockIn)),
            "SalesEdge.InvalidSaleMovementType",
            ErrorType.Validation,
            "Sales.InvalidSaleStockMovementType");

        ExpectFailure(
            AddPackage(saleWriteRepository, CreatePackage(
                stockTracked,
                seedIds.UnitName,
                adminUserId,
                movementReferenceIdOverride: Guid.NewGuid())),
            "SalesEdge.InvalidSaleMovementReference",
            ErrorType.Validation,
            "Sales.InvalidSaleStockMovementReference");

        ExpectFailure(
            AddPackage(saleWriteRepository, CreatePackage(
                stockTracked,
                seedIds.UnitName,
                adminUserId,
                movementUserIdOverride: Guid.NewGuid())),
            "SalesEdge.StockMovementUserMismatch",
            ErrorType.Validation,
            "Sales.StockMovementUserMismatch");

        SalePackage missingProductPackage = CreatePackage(
            stockTracked,
            seedIds.UnitName,
            adminUserId,
            stockProductIdOverride: Guid.NewGuid());

        ExpectAddFailureRollback(
            projectRoot,
            saleWriteRepository,
            missingProductPackage,
            stockTracked.Id,
            ErrorType.NotFound,
            "Sales.ProductNotFoundOrInactive");

        SalePackage inactiveProductPackage = CreatePackage(
            stockTracked,
            seedIds.UnitName,
            adminUserId,
            stockProductIdOverride: inactiveProduct.Id);

        ExpectAddFailureRollback(
            projectRoot,
            saleWriteRepository,
            inactiveProductPackage,
            stockTracked.Id,
            ErrorType.NotFound,
            "Sales.ProductNotFoundOrInactive");

        SalePackage nonStockPackage = CreatePackage(
            stockTracked,
            seedIds.UnitName,
            adminUserId,
            stockProductIdOverride: nonStockTracked.Id);

        ExpectAddFailureRollback(
            projectRoot,
            saleWriteRepository,
            nonStockPackage,
            stockTracked.Id,
            ErrorType.Validation,
            "Sales.ProductNotStockTracked");

        Guid invalidCashierUserId = Guid.NewGuid();

        SalePackage invalidCashierPackage = CreatePackage(
            stockTracked,
            seedIds.UnitName,
            invalidCashierUserId);

        ExpectAddFailureRollback(
            projectRoot,
            saleWriteRepository,
            invalidCashierPackage,
            stockTracked.Id,
            ErrorType.Validation,
            "Sales.InvalidReference");

        SalePackage duplicateReceiptPackage = CreatePackage(
            stockTracked,
            seedIds.UnitName,
            adminUserId,
            receiptNoOverride: baseline.Sale.ReceiptNumber);

        ExpectAddFailureRollback(
            projectRoot,
            saleWriteRepository,
            duplicateReceiptPackage,
            stockTracked.Id,
            ErrorType.Conflict,
            "Sales.DuplicateSaleOrReceipt");

        SalePackage duplicateMovementDbPackage = CreatePackage(
            stockTracked,
            seedIds.UnitName,
            adminUserId,
            movementIdOverride: baseline.Movement.Id);

        ExpectAddFailureRollback(
            projectRoot,
            saleWriteRepository,
            duplicateMovementDbPackage,
            stockTracked.Id,
            ErrorType.Conflict,
            "Sales.DuplicateStockMovement");

        ExpectFailure(
            saleWriteRepository.MarkVoidedAsync(
                Guid.Empty,
                adminUserId,
                "void",
                Array.Empty<StockMovement>()).GetAwaiter().GetResult(),
            "SalesEdge.VoidEmptySaleId",
            ErrorType.Validation,
            "Sales.InvalidSaleId");

        ExpectFailure(
            saleWriteRepository.MarkVoidedAsync(
                baseline.Sale.Id,
                Guid.Empty,
                "void",
                Array.Empty<StockMovement>()).GetAwaiter().GetResult(),
            "SalesEdge.VoidEmptyUserId",
            ErrorType.Validation,
            "Sales.InvalidUserId");

        ExpectFailure(
            saleWriteRepository.MarkVoidedAsync(
                baseline.Sale.Id,
                adminUserId,
                "   ",
                Array.Empty<StockMovement>()).GetAwaiter().GetResult(),
            "SalesEdge.VoidBlankReason",
            ErrorType.Validation,
            "Sales.VoidReasonRequired");

        ExpectFailure(
            saleWriteRepository.MarkVoidedAsync(
                baseline.Sale.Id,
                adminUserId,
                "invalid reversal type",
                new[] { CreateMovement(Guid.NewGuid(), stockTracked.Id, StockMovementType.Sale, 1000, adminUserId, baseline.Sale.Id, "SALE") }).GetAwaiter().GetResult(),
            "SalesEdge.InvalidVoidMovementType",
            ErrorType.Validation,
            "Sales.InvalidVoidStockMovementType");

        ExpectFailure(
            saleWriteRepository.MarkVoidedAsync(
                baseline.Sale.Id,
                adminUserId,
                "invalid reversal reference",
                new[] { CreateMovement(Guid.NewGuid(), stockTracked.Id, StockMovementType.VoidSale, 1000, adminUserId, Guid.NewGuid(), "SALE") }).GetAwaiter().GetResult(),
            "SalesEdge.InvalidVoidMovementReference",
            ErrorType.Validation,
            "Sales.InvalidVoidStockMovementReference");

        ExpectFailure(
            saleWriteRepository.MarkVoidedAsync(
                baseline.Sale.Id,
                adminUserId,
                "invalid reversal user",
                new[] { CreateMovement(Guid.NewGuid(), stockTracked.Id, StockMovementType.VoidSale, 1000, Guid.NewGuid(), baseline.Sale.Id, "SALE") }).GetAwaiter().GetResult(),
            "SalesEdge.InvalidVoidMovementUser",
            ErrorType.Validation,
            "Sales.StockMovementUserMismatch");

        ExpectFailure(
            saleWriteRepository.MarkVoidedAsync(
                Guid.NewGuid(),
                adminUserId,
                "missing sale",
                Array.Empty<StockMovement>()).GetAwaiter().GetResult(),
            "SalesEdge.VoidMissingSale",
            ErrorType.NotFound,
            "Sales.NotFoundOrNotVoidable");

        StockMovement baselineReversal = CreateMovement(
            Guid.NewGuid(),
            stockTracked.Id,
            StockMovementType.VoidSale,
            1000,
            adminUserId,
            baseline.Sale.Id,
            "SALE");

        RequireSuccess(
            saleWriteRepository.MarkVoidedAsync(
                baseline.Sale.Id,
                adminUserId,
                "Phase 24 void baseline sale",
                new[] { baselineReversal }).GetAwaiter().GetResult(),
            "SalesEdge.VoidBaselineSale");

        VerifySaleExists(projectRoot, baseline.Sale.Id, SaleStatus.Voided);
        VerifyStockMovementExists(projectRoot, baselineReversal.Id, 1000);
        VerifyProductStock(projectRoot, stockTracked.Id, 5000);

        long stockBeforeSecondVoid = GetProductStock(projectRoot, stockTracked.Id);
        int movementCountBeforeSecondVoid = GetMovementCount(projectRoot, stockTracked.Id);

        ExpectFailure(
            saleWriteRepository.MarkVoidedAsync(
                baseline.Sale.Id,
                adminUserId,
                "void already voided sale",
                new[] { CreateMovement(Guid.NewGuid(), stockTracked.Id, StockMovementType.VoidSale, 1000, adminUserId, baseline.Sale.Id, "SALE") }).GetAwaiter().GetResult(),
            "SalesEdge.VoidAlreadyVoidedSale",
            ErrorType.NotFound,
            "Sales.NotFoundOrNotVoidable");

        VerifyProductStock(projectRoot, stockTracked.Id, stockBeforeSecondVoid);
        VerifyMovementCount(projectRoot, stockTracked.Id, movementCountBeforeSecondVoid);

        Console.WriteLine("Sales write edge smoke checks succeeded.");
        Console.WriteLine();
    }

    private static Result AddPackage(
        ISaleWriteRepository saleWriteRepository,
        SalePackage package)
    {
        return saleWriteRepository.AddCompletedSaleAsync(
            package.Sale,
            new[] { package.Item },
            new[] { package.Payment },
            new[] { package.Movement }).GetAwaiter().GetResult();
    }

    private static void ExpectAddFailureRollback(
        string projectRoot,
        ISaleWriteRepository saleWriteRepository,
        SalePackage package,
        Guid stockProductId,
        ErrorType expectedType,
        string expectedCode)
    {
        long stockBefore = GetProductStock(projectRoot, stockProductId);
        int movementCountBefore = GetMovementCount(projectRoot, stockProductId);

        ExpectFailure(
            AddPackage(saleWriteRepository, package),
            "SalesEdge.Rollback." + expectedCode,
            expectedType,
            expectedCode);

        VerifyMissing(projectRoot, "sales", package.Sale.Id);
        VerifyMissing(projectRoot, "sale_items", package.Item.Id);
        VerifyMissing(projectRoot, "payments", package.Payment.Id);
        VerifyProductStock(projectRoot, stockProductId, stockBefore);
        VerifyMovementCount(projectRoot, stockProductId, movementCountBefore);
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
                "SalesEdge.CheckStatus");

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
            "SalesEdge.FindAdminSmoke");

        if (admin is not null)
        {
            return admin.Id;
        }

        IReadOnlyList<UserReadModel> users = RequireSuccess(
            userRepository.GetAllAsync().GetAwaiter().GetResult(),
            "SalesEdge.GetAllUsers");

        if (users.Count == 0)
        {
            throw new InvalidOperationException("Sales edge smoke expected at least one user.");
        }

        return users[0].Id;
    }

    private static SeedIds GetSeedIds(ICatalogReadRepository catalogRepository)
    {
        IReadOnlyList<CategoryReadModel> categories = RequireSuccess(
            catalogRepository.GetCategoriesAsync().GetAwaiter().GetResult(),
            "SalesEdge.GetCategories");

        IReadOnlyList<BrandReadModel> brands = RequireSuccess(
            catalogRepository.GetBrandsAsync().GetAwaiter().GetResult(),
            "SalesEdge.GetBrands");

        IReadOnlyList<UnitReadModel> units = RequireSuccess(
            catalogRepository.GetUnitsAsync().GetAwaiter().GetResult(),
            "SalesEdge.GetUnits");

        if (categories.Count == 0 || brands.Count == 0 || units.Count == 0)
        {
            throw new InvalidOperationException("Sales edge smoke seed lookup failed.");
        }

        return new SeedIds(
            categories[0].Id,
            brands[0].Id,
            units[0].Id,
            units[0].Name);
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
            "SalesEdge.Barcode");

        return new Product
        {
            Id = Guid.NewGuid(),
            Sku = sku + "-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "-" + Random.Shared.Next(100, 999),
            Barcode = barcode,
            Name = name,
            Description = "Temporary sales write edge smoke product.",
            CategoryId = seedIds.CategoryId,
            BrandId = seedIds.BrandId,
            UnitId = seedIds.UnitId,
            SupplierId = null,
            CostPrice = Money.FromCents(700),
            SellingPrice = Money.FromCents(1000),
            WholesalePrice = null,
            StockOnHand = QuantityX1000(stockX1000),
            ReorderLevel = QuantityX1000(1000),
            TrackStock = trackStock,
            AllowDiscount = true,
            IsActive = true,
            Notes = "Sales edge smoke product",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = null
        };
    }

    private static SalePackage CreatePackage(
        Product product,
        string unitName,
        Guid cashierUserId,
        Guid? saleIdOverride = null,
        string? receiptNoOverride = null,
        long quantityX1000 = 1000,
        long unitPriceCents = 1000,
        long itemDiscountCents = 0,
        long? lineTotalCentsOverride = null,
        long? subtotalCentsOverride = null,
        long saleDiscountCents = 0,
        long taxCents = 0,
        long? totalCentsOverride = null,
        long? amountPaidCentsOverride = null,
        long? changeCentsOverride = null,
        long? paymentAmountCentsOverride = null,
        SaleStatus status = SaleStatus.Completed,
        Guid? stockProductIdOverride = null,
        StockMovementType movementType = StockMovementType.Sale,
        Guid? movementReferenceIdOverride = null,
        string? movementReferenceTypeOverride = "SALE",
        Guid? movementUserIdOverride = null,
        Guid? movementIdOverride = null,
        Guid? itemIdOverride = null,
        Guid? itemSaleIdOverride = null,
        Guid? paymentIdOverride = null,
        Guid? paymentSaleIdOverride = null,
        PaymentMethod paymentMethod = PaymentMethod.Cash,
        bool withVoidMetadata = false)
    {
        Guid saleId = saleIdOverride ?? Guid.NewGuid();
        string receiptNo = receiptNoOverride ?? NewReceiptNo();

        long gross = CalculateLineGross(unitPriceCents, quantityX1000);
        long lineTotal = lineTotalCentsOverride ?? checked(gross - itemDiscountCents);
        long subtotal = subtotalCentsOverride ?? lineTotal;
        long total = totalCentsOverride ?? checked(subtotal - saleDiscountCents + taxCents);
        long amountPaid = amountPaidCentsOverride ?? total;
        long change = changeCentsOverride ?? checked(amountPaid - total);
        long paymentAmount = paymentAmountCentsOverride ?? amountPaid;

        Sale sale = new()
        {
            Id = saleId,
            ReceiptNumber = receiptNo,
            SaleDate = DateTimeOffset.UtcNow,
            CashierUserId = cashierUserId,
            CashierSessionId = null,
            Subtotal = Money.FromCents(subtotal),
            Discount = Money.FromCents(saleDiscountCents),
            Tax = Money.FromCents(taxCents),
            Total = Money.FromCents(total),
            AmountPaid = Money.FromCents(amountPaid),
            Change = Money.FromCents(change),
            Status = status,
            VoidedByUserId = withVoidMetadata ? cashierUserId : null,
            VoidReason = withVoidMetadata ? "should not exist yet" : null,
            VoidedAt = withVoidMetadata ? DateTimeOffset.UtcNow : null,
            ReceiptSnapshot = "Sales edge smoke receipt snapshot.",
            CreatedAt = DateTimeOffset.UtcNow
        };

        SaleItem item = CreateSaleItem(
            itemIdOverride ?? Guid.NewGuid(),
            itemSaleIdOverride ?? saleId,
            product,
            unitName,
            quantityX1000,
            unitPriceCents,
            itemDiscountCents,
            lineTotal);

        Payment payment = CreatePayment(
            paymentIdOverride ?? Guid.NewGuid(),
            paymentSaleIdOverride ?? saleId,
            paymentAmount,
            paymentMethod);

        StockMovement movement = CreateMovement(
            movementIdOverride ?? Guid.NewGuid(),
            stockProductIdOverride ?? product.Id,
            movementType,
            quantityX1000,
            movementUserIdOverride ?? cashierUserId,
            movementReferenceIdOverride ?? saleId,
            movementReferenceTypeOverride);

        return new SalePackage(sale, item, payment, movement);
    }

    private static SaleItem CreateSaleItem(
        Guid id,
        Guid saleId,
        Product product,
        string unitName,
        long quantityX1000,
        long unitPriceCents,
        long discountCents,
        long lineTotalCents)
    {
        return new SaleItem
        {
            Id = id,
            SaleId = saleId,
            ProductId = product.Id,
            ProductNameSnapshot = product.Name,
            BarcodeSnapshot = product.Barcode?.Value,
            UnitNameSnapshot = unitName,
            Quantity = QuantityX1000(quantityX1000),
            UnitPrice = Money.FromCents(unitPriceCents),
            CostPrice = product.CostPrice,
            Discount = Money.FromCents(discountCents),
            LineTotal = Money.FromCents(lineTotalCents)
        };
    }

    private static Payment CreatePayment(
        Guid id,
        Guid saleId,
        long amountCents,
        PaymentMethod method)
    {
        return new Payment
        {
            Id = id,
            SaleId = saleId,
            Method = method,
            Amount = Money.FromCents(amountCents),
            PaidAt = DateTimeOffset.UtcNow
        };
    }

    private static StockMovement CreateMovement(
        Guid id,
        Guid productId,
        StockMovementType movementType,
        long quantityX1000,
        Guid userId,
        Guid referenceId,
        string? referenceType)
    {
        return new StockMovement
        {
            Id = id,
            ProductId = productId,
            MovementType = movementType,
            QuantityChange = QuantityX1000(quantityX1000),
            CostPrice = Money.FromCents(700),
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Reason = "Sales edge smoke movement",
            UserId = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static Quantity QuantityX1000(long valueX1000)
    {
        return RequireValue(
            Quantity.FromX1000(valueX1000),
            $"SalesEdge.Quantity.{valueX1000}");
    }

    private static long CalculateLineGross(long unitPriceCents, long quantityX1000)
    {
        decimal gross = unitPriceCents * (quantityX1000 / 1000m);

        return checked((long)Math.Round(
            gross,
            0,
            MidpointRounding.AwayFromZero));
    }

    private static string NewBarcode()
    {
        return "94" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() + Random.Shared.Next(100, 999).ToString();
    }

    private static string NewReceiptNo()
    {
        return "EDGE-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() + "-" + Random.Shared.Next(100, 999).ToString();
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

    private static void VerifySaleExists(string projectRoot, Guid saleId, SaleStatus expectedStatus)
    {
        using SqliteConnection connection = OpenConnection(projectRoot);
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = "SELECT status FROM sales WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", saleId.ToString("D"));

        object? value = command.ExecuteScalar();

        if (value is null)
        {
            throw new InvalidOperationException("Sale row was not found.");
        }

        SaleStatus actual = (SaleStatus)Convert.ToInt32(value);

        if (actual != expectedStatus)
        {
            throw new InvalidOperationException(
                $"Sale status verification failed. Expected {expectedStatus}, got {actual}.");
        }
    }

    private static void VerifyStockMovementExists(
        string projectRoot,
        Guid movementId,
        long expectedQtyChangeX1000)
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
                $"Stock movement verification failed. Expected {expectedQtyChangeX1000}, got {actual}.");
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

    private static void VerifyMissing(string projectRoot, string tableName, Guid id)
    {
        using SqliteConnection connection = OpenConnection(projectRoot);
        using SqliteCommand command = connection.CreateCommand();

        command.CommandText = $"SELECT COUNT(*) FROM {tableName} WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));

        int count = Convert.ToInt32(command.ExecuteScalar() ?? 0);

        if (count != 0)
        {
            throw new InvalidOperationException($"{tableName} rollback verification failed for {id}.");
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
