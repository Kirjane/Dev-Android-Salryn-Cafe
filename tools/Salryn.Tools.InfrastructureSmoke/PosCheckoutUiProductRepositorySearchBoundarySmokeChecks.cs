using Salryn.App.WinForms.Features.Checkout;
using Salryn.Core.Common;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.ValueObjects;

internal static class PosCheckoutUiProductRepositorySearchBoundarySmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== POS CHECKOUT UI PRODUCT REPOSITORY SEARCH BOUNDARY SMOKE CHECKS =====");

        FakeProductReadRepository products = new();

        ProductLookupReadModel coffee = CreateProduct(
            sku: "REAL-COFFEE",
            barcode: "490000000001",
            name: "Repository Coffee",
            unitName: "pc",
            sellingPriceCents: 1500,
            costPriceCents: 1000,
            stockX1000: 9000);

        ProductLookupReadModel rice = CreateProduct(
            sku: "REAL-RICE",
            barcode: "490000000002",
            name: "Repository Rice",
            unitName: "kg",
            sellingPriceCents: 5800,
            costPriceCents: 5100,
            stockX1000: 100000);

        products.Add(coffee);
        products.Add(rice);

        CheckoutProductRepositorySearchBoundary boundary = new(products);

        Result<ProductLookupReadModel?> barcodeResult =
            boundary.FindProductAsync("490000000001").GetAwaiter().GetResult();

        RequireSuccess(barcodeResult, "Phase43.BarcodeLookup");

        if (barcodeResult.Value is null ||
            barcodeResult.Value.Id != coffee.Id)
        {
            throw new InvalidOperationException("Phase 43 barcode lookup returned wrong product.");
        }

        if (products.BarcodeLookupCount < 1)
        {
            throw new InvalidOperationException("Phase 43 did not use barcode lookup path.");
        }

        Result<ProductLookupReadModel?> textResult =
            boundary.FindProductAsync("rice").GetAwaiter().GetResult();

        RequireSuccess(textResult, "Phase43.TextSearch");

        if (textResult.Value is null ||
            textResult.Value.Id != rice.Id)
        {
            throw new InvalidOperationException("Phase 43 text search returned wrong product.");
        }

        if (products.SearchCount < 1 ||
            products.FindByIdCount < 1)
        {
            throw new InvalidOperationException("Phase 43 did not use search + find-by-id path.");
        }

        Result<ProductLookupReadModel?> missingResult =
            boundary.FindProductAsync("missing").GetAwaiter().GetResult();

        RequireSuccess(missingResult, "Phase43.MissingSearch");

        if (missingResult.Value is not null)
        {
            throw new InvalidOperationException("Phase 43 missing search should return null product.");
        }

        CheckoutPresenter presenter = new(boundary);

        CheckoutPresenterOperationResult presenterResult =
            presenter.SearchProductAsync("490000000002").GetAwaiter().GetResult();

        if (!presenterResult.IsSuccess ||
            presenterResult.State.PreparedProduct is null ||
            presenterResult.State.PreparedProduct.ProductId != rice.Id ||
            !presenterResult.State.CanAddPreparedProductLine ||
            presenterResult.State.CanCompleteSale)
        {
            throw new InvalidOperationException("Phase 43 presenter integration verification failed.");
        }

        Console.WriteLine("POS checkout UI product repository search boundary smoke checks succeeded.");
        Console.WriteLine();
    }

    private static void RequireSuccess<T>(Result<T> result, string area)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{area} failed: {result.ErrorDetail.Code} / {result.Error}");
        }
    }

    private static ProductLookupReadModel CreateProduct(
        string sku,
        string barcode,
        string name,
        string unitName,
        long sellingPriceCents,
        long costPriceCents,
        long stockX1000)
    {
        return new ProductLookupReadModel(
            Id: Guid.NewGuid(),
            Sku: sku,
            Barcode: barcode,
            Name: name,
            UnitName: unitName,
            SellingPrice: Money.FromCents(sellingPriceCents),
            CostPrice: Money.FromCents(costPriceCents),
            StockOnHand: QuantityX1000(stockX1000),
            TrackStock: true,
            AllowDiscount: true,
            IsActive: true);
    }

    private static Quantity QuantityX1000(long valueX1000)
    {
        Result<Quantity> result = Quantity.FromX1000(valueX1000);

        if (result.IsFailure)
        {
            throw new InvalidOperationException(result.Error);
        }

        return result.Value;
    }

    private sealed class FakeProductReadRepository : IProductReadRepository
    {
        private readonly List<ProductLookupReadModel> _products = new();

        public int BarcodeLookupCount { get; private set; }

        public int SearchCount { get; private set; }

        public int FindByIdCount { get; private set; }

        public void Add(ProductLookupReadModel product)
        {
            _products.Add(product);
        }

        public Task<Result<ProductLookupReadModel?>> FindByIdAsync(
            Guid productId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FindByIdCount++;

            ProductLookupReadModel? product = _products.FirstOrDefault(x => x.Id == productId);

            return Task.FromResult(Result<ProductLookupReadModel?>.Success(product));
        }

        // Kirch: Checkout smoke tests do not need edit-detail reads, so this fake returns no detail product.
        public Task<Result<ProductDetailReadModel?>> FindDetailByIdAsync(
            Guid productId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<ProductDetailReadModel?>.Success(null));
        }
        public Task<Result<ProductLookupReadModel?>> FindByBarcodeAsync(
            Barcode barcode,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BarcodeLookupCount++;

            ProductLookupReadModel? product = _products.FirstOrDefault(x =>
                string.Equals(x.Barcode, barcode.Value, StringComparison.OrdinalIgnoreCase) &&
                x.IsActive);

            return Task.FromResult(Result<ProductLookupReadModel?>.Success(product));
        }

        public Task<Result<PagedResult<ProductListItemReadModel>>> SearchAsync(
            ProductSearchFilter filter,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SearchCount++;

            string? search = string.IsNullOrWhiteSpace(filter.SearchText)
                ? null
                : filter.SearchText.Trim();

            IReadOnlyList<ProductListItemReadModel> items = _products
                .Where(x => x.IsActive)
                .Where(x =>
                    search is null ||
                    x.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (x.Barcode?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (x.Sku?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
                .Select(ToListItem)
                .ToList();

            PagedResult<ProductListItemReadModel> page = new(
                items,
                items.Count,
                filter.Page.PageNumber,
                filter.Page.PageSize);

            return Task.FromResult(Result<PagedResult<ProductListItemReadModel>>.Success(page));
        }

        public Task<Result<IReadOnlyList<LowStockProductReadModel>>> GetLowStockAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<LowStockProductReadModel> items =
                Array.Empty<LowStockProductReadModel>();

            return Task.FromResult(Result<IReadOnlyList<LowStockProductReadModel>>.Success(items));
        }

        private static ProductListItemReadModel ToListItem(ProductLookupReadModel product)
        {
            return new ProductListItemReadModel(
                Id: product.Id,
                Sku: product.Sku,
                Barcode: product.Barcode,
                Name: product.Name,
                UnitName: product.UnitName,
                CategoryName: null,
                BrandName: null,
                SellingPrice: product.SellingPrice,
                CostPrice: product.CostPrice,
                StockOnHand: product.StockOnHand,
                ReorderLevel: QuantityX1000(0),
                TrackStock: product.TrackStock,
                IsActive: product.IsActive);
        }
    }
}

