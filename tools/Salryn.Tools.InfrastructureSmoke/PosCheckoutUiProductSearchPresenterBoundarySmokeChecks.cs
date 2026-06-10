using Salryn.App.WinForms.Features.Checkout;
using Salryn.Core.Common;
using Salryn.Core.ReadModels;
using Salryn.Core.ValueObjects;

internal static class PosCheckoutUiProductSearchPresenterBoundarySmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== POS CHECKOUT UI PRODUCT SEARCH PRESENTER BOUNDARY SMOKE CHECKS =====");

        FakeCheckoutProductSearchBoundary productSearch = new();

        ProductLookupReadModel activeProduct = CreateProduct(
            sku: "PHASE41-ACTIVE",
            barcode: "941000000001",
            name: "Phase 41 Active Product",
            unitName: "pc",
            sellingPriceCents: 1500,
            costPriceCents: 900,
            stockX1000: 5000,
            trackStock: true,
            isActive: true);

        ProductLookupReadModel inactiveProduct = CreateProduct(
            sku: "PHASE41-INACTIVE",
            barcode: "941000000002",
            name: "Phase 41 Inactive Product",
            unitName: "pc",
            sellingPriceCents: 2500,
            costPriceCents: 1200,
            stockX1000: 3000,
            trackStock: true,
            isActive: false);

        productSearch.Add(activeProduct);
        productSearch.Add(inactiveProduct);

        CheckoutPresenter presenter = new(productSearch);

        RequireSuccess(
            presenter.Initialize(),
            "Phase41.Initialize");

        ExpectFailure(
            presenter.SearchProductAsync("   ").GetAwaiter().GetResult(),
            "Phase41.BlankSearch",
            "CheckoutUi.ProductSearchTextRequired");

        CheckoutPresenterOperationResult searchResult = RequireSuccess(
            presenter.SearchProductAsync(" 941000000001 ").GetAwaiter().GetResult(),
            "Phase41.SearchActiveByBarcode");

        if (!string.Equals(searchResult.State.ProductInputText, "941000000001", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Phase 41 search input trim verification failed.");
        }

        if (searchResult.State.PreparedProduct is null)
        {
            throw new InvalidOperationException("Phase 41 expected prepared product.");
        }

        if (searchResult.State.PreparedProduct.ProductId != activeProduct.Id ||
            !string.Equals(searchResult.State.PreparedProduct.ProductName, activeProduct.Name, StringComparison.Ordinal) ||
            !string.Equals(searchResult.State.PreparedProduct.Barcode, activeProduct.Barcode, StringComparison.Ordinal) ||
            searchResult.State.PreparedProduct.UnitPriceCents != activeProduct.SellingPrice.Cents ||
            searchResult.State.PreparedProduct.StockOnHandX1000 != activeProduct.StockOnHand.ValueX1000 ||
            !searchResult.State.CanSearchProduct ||
            !searchResult.State.CanAddPreparedProductLine ||
            searchResult.State.CanCompleteSale)
        {
            throw new InvalidOperationException("Phase 41 prepared product viewmodel verification failed.");
        }

        CheckoutPresenterOperationResult addResult = RequireSuccess(
            presenter.AddPreparedProductLine(),
            "Phase41.AddPreparedProductLine");

        if (addResult.State.PreparedProduct is not null ||
            !string.IsNullOrWhiteSpace(addResult.State.ProductInputText) ||
            addResult.State.Lines.Count != 1 ||
            addResult.State.Totals.TotalCents != activeProduct.SellingPrice.Cents ||
            addResult.State.CanCompleteSale)
        {
            throw new InvalidOperationException("Phase 41 add prepared product line verification failed.");
        }

        CheckoutLineViewModel line = addResult.State.Lines[0];

        if (line.ProductId != activeProduct.Id ||
            !string.Equals(line.ProductName, activeProduct.Name, StringComparison.Ordinal) ||
            line.QuantityX1000 != 1000 ||
            line.UnitPriceCents != activeProduct.SellingPrice.Cents ||
            line.LineTotalCents != activeProduct.SellingPrice.Cents)
        {
            throw new InvalidOperationException("Phase 41 cart line verification failed.");
        }

        CheckoutPresenterOperationResult missingResult = RequireSuccess(
            presenter.SearchProductAsync("missing-product").GetAwaiter().GetResult(),
            "Phase41.SearchMissing");

        if (missingResult.State.PreparedProduct is not null ||
            missingResult.State.CanAddPreparedProductLine ||
            !missingResult.State.ProductResultText.Contains("No product found", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Phase 41 missing product verification failed.");
        }

        ExpectFailure(
            presenter.SearchProductAsync("941000000002").GetAwaiter().GetResult(),
            "Phase41.SearchInactive",
            "CheckoutProductPicker.InactiveProduct");

        CheckoutPresenter noBoundaryPresenter = new();

        ExpectFailure(
            noBoundaryPresenter.SearchProductAsync("941000000001").GetAwaiter().GetResult(),
            "Phase41.SearchWithoutBoundary",
            "CheckoutUi.ProductSearchBoundaryMissing");

        CheckoutPresenter failingPresenter = new(new FailingCheckoutProductSearchBoundary());

        ExpectFailure(
            failingPresenter.SearchProductAsync("941000000001").GetAwaiter().GetResult(),
            "Phase41.SearchBoundaryFailure",
            "Phase41.ProductLookupFailure");

        Console.WriteLine("POS checkout UI product search presenter boundary smoke checks succeeded.");
        Console.WriteLine();
    }

    private static ProductLookupReadModel CreateProduct(
        string sku,
        string barcode,
        string name,
        string unitName,
        long sellingPriceCents,
        long costPriceCents,
        long stockX1000,
        bool trackStock,
        bool isActive)
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
            TrackStock: trackStock,
            AllowDiscount: true,
            IsActive: isActive);
    }

    private static Quantity QuantityX1000(long valueX1000)
    {
        Result<Quantity> result = Quantity.FromX1000(valueX1000);

        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Phase 41 quantity failed: {result.Error}");
        }

        return result.Value;
    }

    private static CheckoutPresenterOperationResult RequireSuccess(
        CheckoutPresenterOperationResult result,
        string area)
    {
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"{area} failed: {result.ErrorCode} / {result.ErrorMessage}");
        }

        return result;
    }

    private static void ExpectFailure(
        CheckoutPresenterOperationResult result,
        string area,
        string expectedCode)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException($"{area} expected failure but succeeded.");
        }

        if (!string.Equals(result.ErrorCode, expectedCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{area} expected {expectedCode}, got {result.ErrorCode}: {result.ErrorMessage}");
        }
    }

    private sealed class FakeCheckoutProductSearchBoundary : ICheckoutProductSearchBoundary
    {
        private readonly List<ProductLookupReadModel> _products = new();

        public void Add(ProductLookupReadModel product)
        {
            _products.Add(product);
        }

        public Task<Result<ProductLookupReadModel?>> FindProductAsync(
            string searchText,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string normalized = searchText.Trim();

            ProductLookupReadModel? product = _products.FirstOrDefault(x =>
                string.Equals(x.Barcode, normalized, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Sku, normalized, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Name, normalized, StringComparison.OrdinalIgnoreCase));

            return Task.FromResult(Result<ProductLookupReadModel?>.Success(product));
        }
    }

    private sealed class FailingCheckoutProductSearchBoundary : ICheckoutProductSearchBoundary
    {
        public Task<Result<ProductLookupReadModel?>> FindProductAsync(
            string searchText,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<ProductLookupReadModel?>.Fail(Error.Validation(
                "Phase41.ProductLookupFailure",
                "Phase 41 fake lookup failure.")));
        }
    }
}
