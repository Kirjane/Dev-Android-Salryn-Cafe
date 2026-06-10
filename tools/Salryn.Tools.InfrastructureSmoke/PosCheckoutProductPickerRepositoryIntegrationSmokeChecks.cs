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

internal static class PosCheckoutProductPickerRepositoryIntegrationSmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== POS CHECKOUT PRODUCT PICKER REPOSITORY INTEGRATION SMOKE CHECKS =====");

        DatabaseInitializationOrchestrator orchestrator =
            DefaultDatabaseInitializationFactory.CreateDevelopment(projectRoot);

        RequireSuccess(
            orchestrator.InitializeDatabase(),
            "ProductPickerRepository.InitializeDatabase");

        DbPaths dbPaths = DbPaths.CreateDevelopment(projectRoot);
        SqliteConnectionFactory connectionFactory = new(dbPaths);
        SqliteRepositoryFactory repositories = new(connectionFactory);

        ICatalogReadRepository catalogRead = repositories.CreateCatalogReadRepository();
        IProductWriteRepository productWrite = repositories.CreateProductWriteRepository();
        IProductReadRepository productRead = repositories.CreateProductReadRepository();
        ISaleReadRepository saleRead = repositories.CreateSaleReadRepository();
        IInventoryReadRepository inventoryRead = repositories.CreateInventoryReadRepository();

        int initialSaleCount = CountSales(saleRead);
        int initialStockMovementCount = CountStockMovements(inventoryRead);

        UnitReadModel unit = RequireSuccess(
                catalogRead.GetUnitsAsync().GetAwaiter().GetResult(),
                "ProductPickerRepository.GetUnits")
            .FirstOrDefault(item => string.Equals(item.Abbreviation, "pc", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Default pc unit was not found.");

        string suffix = Guid.NewGuid().ToString("N")[..10];

        Guid trackedProductId = Guid.NewGuid();
        string trackedBarcodeText = "938" + suffix[..9];
        string trackedProductName = "Phase38 Tracked Product " + suffix;

        Product trackedProduct = new()
        {
            Id = trackedProductId,
            Sku = "P38-TRACK-" + suffix,
            Barcode = BarcodeValue(trackedBarcodeText),
            Name = trackedProductName,
            Description = "Phase 38 tracked picker repository integration product.",
            UnitId = unit.Id,
            CostPrice = Money.FromCents(800),
            SellingPrice = Money.FromCents(1750),
            WholesalePrice = null,
            StockOnHand = QuantityX1000(10000),
            ReorderLevel = QuantityX1000(1000),
            TrackStock = true,
            AllowDiscount = true,
            IsActive = true,
            Notes = "Created by Phase 38 smoke.",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = null
        };

        RequireSuccess(
            productWrite.AddAsync(trackedProduct).GetAwaiter().GetResult(),
            "ProductPickerRepository.AddTrackedProduct");

        Guid untrackedProductId = Guid.NewGuid();
        string untrackedProductName = "Phase38 Service Product " + suffix;

        Product untrackedProduct = new()
        {
            Id = untrackedProductId,
            Sku = "P38-SVC-" + suffix,
            Barcode = null,
            Name = untrackedProductName,
            Description = "Phase 38 untracked picker repository integration product.",
            UnitId = unit.Id,
            CostPrice = null,
            SellingPrice = Money.FromCents(650),
            WholesalePrice = null,
            StockOnHand = Quantity.Zero,
            ReorderLevel = Quantity.Zero,
            TrackStock = false,
            AllowDiscount = true,
            IsActive = true,
            Notes = "Created by Phase 38 smoke.",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = null
        };

        RequireSuccess(
            productWrite.AddAsync(untrackedProduct).GetAwaiter().GetResult(),
            "ProductPickerRepository.AddUntrackedProduct");

        ProductLookupReadModel trackedLookup = RequireFound(
            productRead.FindByIdAsync(trackedProductId).GetAwaiter().GetResult(),
            "ProductPickerRepository.FindTrackedById");

        VerifyLookup(
            trackedLookup,
            trackedProductId,
            trackedBarcodeText,
            trackedProductName,
            unit.Name,
            sellingPriceCents: 1750,
            costPriceCents: 800,
            stockOnHandX1000: 10000,
            trackStock: true,
            isActive: true,
            "ProductPickerRepository.TrackedLookupById.Verify");

        ProductLookupReadModel trackedBarcodeLookup = RequireFound(
            productRead.FindByBarcodeAsync(BarcodeValue(trackedBarcodeText)).GetAwaiter().GetResult(),
            "ProductPickerRepository.FindTrackedByBarcode");

        if (trackedBarcodeLookup.Id != trackedProductId)
        {
            throw new InvalidOperationException("Product picker repository barcode lookup returned the wrong product.");
        }

        ProductLookupReadModel untrackedLookup = RequireFound(
            productRead.FindByIdAsync(untrackedProductId).GetAwaiter().GetResult(),
            "ProductPickerRepository.FindUntrackedById");

        VerifyLookup(
            untrackedLookup,
            untrackedProductId,
            null,
            untrackedProductName,
            unit.Name,
            sellingPriceCents: 650,
            costPriceCents: null,
            stockOnHandX1000: 0,
            trackStock: false,
            isActive: true,
            "ProductPickerRepository.UntrackedLookupById.Verify");

        PagedResult<ProductListItemReadModel> trackedSearch = RequireSuccess(
            productRead.SearchAsync(new ProductSearchFilter(
                trackedBarcodeText,
                CategoryId: null,
                BrandId: null,
                IsActive: true,
                IncludeInactive: false,
                Page: PageRequest.FirstPage(20))).GetAwaiter().GetResult(),
            "ProductPickerRepository.SearchTrackedByBarcode");

        ProductListItemReadModel trackedListItem = trackedSearch.Items
            .FirstOrDefault(item => item.Id == trackedProductId)
            ?? throw new InvalidOperationException("Tracked product was not returned by product search.");

        PagedResult<ProductListItemReadModel> untrackedSearch = RequireSuccess(
            productRead.SearchAsync(new ProductSearchFilter(
                untrackedProductName,
                CategoryId: null,
                BrandId: null,
                IsActive: true,
                IncludeInactive: false,
                Page: PageRequest.FirstPage(20))).GetAwaiter().GetResult(),
            "ProductPickerRepository.SearchUntrackedByName");

        ProductListItemReadModel untrackedListItem = untrackedSearch.Items
            .FirstOrDefault(item => item.Id == untrackedProductId)
            ?? throw new InvalidOperationException("Untracked product was not returned by product search.");

        PosCheckoutProductPickerBoundary picker = new();
        PosCheckoutDraftTransactionBuilder draftBuilder = new();

        Guid cashierId = Guid.NewGuid();

        RequireSuccess(
            draftBuilder.SetCashierUserId(cashierId),
            "ProductPickerRepository.SetDraftCashier");

        RequireSuccess(
            draftBuilder.SetReceiptNumber("P38-" + Guid.NewGuid().ToString("N")),
            "ProductPickerRepository.SetDraftReceipt");

        PosCheckoutDraftLineInput trackedLineInput = RequireSuccess(
            picker.CreateLineInput(
                trackedLookup,
                quantityX1000: 2000,
                discountCents: 100),
            "ProductPickerRepository.PickTrackedLookup");

        VerifyLineInput(
            trackedLineInput,
            trackedProductId,
            trackedProductName,
            trackedBarcodeText,
            unit.Name,
            quantityX1000: 2000,
            unitPriceCents: 1750,
            costPriceCents: 800,
            discountCents: 100,
            trackStock: true,
            "ProductPickerRepository.TrackedLineInput.Verify");

        PosCheckoutDraftLine trackedDraftLine = RequireSuccess(
            draftBuilder.AddLine(trackedLineInput),
            "ProductPickerRepository.AddTrackedLineToDraft");

        if (trackedDraftLine.GrossCents != 3500 ||
            trackedDraftLine.LineTotalCents != 3400)
        {
            throw new InvalidOperationException("Tracked draft line total verification failed.");
        }

        VerifyTotals(
            RequireSuccess(draftBuilder.GetTotals(), "ProductPickerRepository.TrackedDraftTotals"),
            subtotal: 3400,
            discount: 0,
            tax: 0,
            total: 3400,
            paid: 0,
            change: 0,
            balance: 3400,
            "ProductPickerRepository.TrackedDraftTotals.Verify");

        PosCheckoutDraftLineInput untrackedLineInput = RequireSuccess(
            picker.CreateLineInput(
                untrackedListItem,
                quantityX1000: 3000,
                discountCents: 50),
            "ProductPickerRepository.PickUntrackedListItem");

        VerifyLineInput(
            untrackedLineInput,
            untrackedProductId,
            untrackedProductName,
            null,
            unit.Name,
            quantityX1000: 3000,
            unitPriceCents: 650,
            costPriceCents: null,
            discountCents: 50,
            trackStock: false,
            "ProductPickerRepository.UntrackedLineInput.Verify");

        PosCheckoutDraftLine untrackedDraftLine = RequireSuccess(
            draftBuilder.AddLine(untrackedLineInput),
            "ProductPickerRepository.AddUntrackedLineToDraft");

        if (untrackedDraftLine.GrossCents != 1950 ||
            untrackedDraftLine.LineTotalCents != 1900)
        {
            throw new InvalidOperationException("Untracked draft line total verification failed.");
        }

        VerifyTotals(
            RequireSuccess(draftBuilder.GetTotals(), "ProductPickerRepository.TwoLineDraftTotals"),
            subtotal: 5300,
            discount: 0,
            tax: 0,
            total: 5300,
            paid: 0,
            change: 0,
            balance: 5300,
            "ProductPickerRepository.TwoLineDraftTotals.Verify");

        PosCheckoutDraftTransaction draft = RequireSuccess(
            draftBuilder.GetTransaction(),
            "ProductPickerRepository.GetDraftTransaction");

        if (draft.Lines.Count != 2 ||
            draft.Lines[0].ProductId != trackedProductId ||
            draft.Lines[1].ProductId != untrackedProductId ||
            !draft.Lines[0].TrackStock ||
            draft.Lines[1].TrackStock)
        {
            throw new InvalidOperationException("Repository-fed picker draft line ordering or track-stock mapping failed.");
        }

        int finalSaleCount = CountSales(saleRead);
        int finalStockMovementCount = CountStockMovements(inventoryRead);

        if (finalSaleCount != initialSaleCount)
        {
            throw new InvalidOperationException(
                $"Product picker repository integration must not create sales. Before={initialSaleCount}, After={finalSaleCount}.");
        }

        if (finalStockMovementCount != initialStockMovementCount)
        {
            throw new InvalidOperationException(
                $"Product picker repository integration must not create stock movements. Before={initialStockMovementCount}, After={finalStockMovementCount}.");
        }

        Console.WriteLine("POS checkout product picker repository integration smoke checks succeeded.");
        Console.WriteLine();
    }

    private static void VerifyLookup(
        ProductLookupReadModel product,
        Guid expectedId,
        string? expectedBarcode,
        string expectedName,
        string expectedUnitName,
        long sellingPriceCents,
        long? costPriceCents,
        long stockOnHandX1000,
        bool trackStock,
        bool isActive,
        string area)
    {
        if (product.Id != expectedId ||
            !string.Equals(product.Barcode, expectedBarcode, StringComparison.Ordinal) ||
            !string.Equals(product.Name, expectedName, StringComparison.Ordinal) ||
            !string.Equals(product.UnitName, expectedUnitName, StringComparison.Ordinal) ||
            product.SellingPrice.Cents != sellingPriceCents ||
            product.CostPrice?.Cents != costPriceCents ||
            product.StockOnHand.ValueX1000 != stockOnHandX1000 ||
            product.TrackStock != trackStock ||
            product.IsActive != isActive)
        {
            throw new InvalidOperationException($"{area} product lookup verification failed.");
        }
    }

    private static void VerifyLineInput(
        PosCheckoutDraftLineInput line,
        Guid expectedProductId,
        string expectedName,
        string? expectedBarcode,
        string expectedUnitName,
        long quantityX1000,
        long unitPriceCents,
        long? costPriceCents,
        long discountCents,
        bool trackStock,
        string area)
    {
        if (line.ProductId != expectedProductId ||
            !string.Equals(line.ProductNameSnapshot, expectedName, StringComparison.Ordinal) ||
            !string.Equals(line.BarcodeSnapshot, expectedBarcode, StringComparison.Ordinal) ||
            !string.Equals(line.UnitNameSnapshot, expectedUnitName, StringComparison.Ordinal) ||
            line.QuantityX1000 != quantityX1000 ||
            line.UnitPriceCents != unitPriceCents ||
            line.CostPriceCents != costPriceCents ||
            line.DiscountCents != discountCents ||
            line.TrackStock != trackStock)
        {
            throw new InvalidOperationException($"{area} line input verification failed.");
        }
    }

    private static void VerifyTotals(
        PosCheckoutDraftTotals totals,
        long subtotal,
        long discount,
        long tax,
        long total,
        long paid,
        long change,
        long balance,
        string area)
    {
        if (totals.SubtotalCents != subtotal ||
            totals.SaleDiscountCents != discount ||
            totals.TaxCents != tax ||
            totals.TotalCents != total ||
            totals.AmountPaidCents != paid ||
            totals.ChangeCents != change ||
            totals.BalanceDueCents != balance)
        {
            throw new InvalidOperationException(
                $"{area} totals mismatch. " +
                $"Got subtotal={totals.SubtotalCents}, discount={totals.SaleDiscountCents}, tax={totals.TaxCents}, total={totals.TotalCents}, paid={totals.AmountPaidCents}, change={totals.ChangeCents}, balance={totals.BalanceDueCents}.");
        }
    }

    private static int CountSales(ISaleReadRepository saleRead)
    {
        PagedResult<SaleHistoryItemReadModel> result = RequireSuccess(
            saleRead.SearchSalesAsync(new SaleHistoryFilter(
                From: null,
                To: null,
                CashierUserId: null,
                Status: null,
                ReceiptNo: null,
                Page: PageRequest.FirstPage(1))).GetAwaiter().GetResult(),
            "ProductPickerRepository.CountSales");

        return result.TotalCount;
    }

    private static int CountStockMovements(IInventoryReadRepository inventoryRead)
    {
        PagedResult<StockMovementReadModel> result = RequireSuccess(
            inventoryRead.SearchStockMovementsAsync(new StockMovementFilter(
                ProductId: null,
                MovementType: null,
                From: null,
                To: null,
                Page: PageRequest.FirstPage(1))).GetAwaiter().GetResult(),
            "ProductPickerRepository.CountStockMovements");

        return result.TotalCount;
    }

    private static ProductLookupReadModel RequireFound(
        Result<ProductLookupReadModel?> result,
        string area)
    {
        ProductLookupReadModel? product = RequireSuccess(result, area);

        if (product is null)
        {
            throw new InvalidOperationException($"{area} expected product but found null.");
        }

        return product;
    }

    private static Barcode BarcodeValue(string value)
    {
        Result<Barcode> result = Barcode.Create(value);

        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Barcode helper failed: {result.Error}");
        }

        return result.Value;
    }

    private static Quantity QuantityX1000(long valueX1000)
    {
        Result<Quantity> result = Quantity.FromX1000(valueX1000);

        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Quantity helper failed: {result.Error}");
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

    private static T RequireSuccess<T>(Result<T> result, string area)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{area} failed: {result.Error}");
        }

        return result.Value;
    }
}
