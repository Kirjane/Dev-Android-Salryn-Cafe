using Salryn.App.WinForms.Composition;
using Salryn.App.WinForms.Features.Checkout;

Console.WriteLine("===== SALRYN CHECKOUT LOCAL HOST LOOKUP SMOKE =====");

await CheckoutLocalHostLookupSmokeChecks.RunAsync();

Console.WriteLine("SALRYN checkout Local Host lookup smoke passed.");

internal static class CheckoutLocalHostLookupSmokeChecks
{
    public static async Task RunAsync()
    {
        const string barcode = "480000000001";
        const string sku = "E2E-SKU-001";
        const string productName = "E2E Local Host Cola";
        const string unitName = "piece";
        const long sellingPriceCents = 1250;
        const long costPriceCents = 700;
        const long stockOnHandX1000 = 12000;

        Guid productId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        SmokeGateway gateway = SmokeGateway.Found(
            productId,
            barcode,
            sku,
            productName,
            unitName,
            sellingPriceCents,
            costPriceCents,
            stockOnHandX1000);

        ICheckoutProductSearchBoundary boundary =
            new CheckoutLocalHostProductSearchBoundary(gateway);

        CheckoutPresenter presenter = new CheckoutPresenter(boundary);

        CheckoutPresenterOperationResult inputResult =
            presenter.SetProductInputText($"  {barcode}  ");

        Require(inputResult.IsSuccess, "Product input should be accepted before search.");
        Require(inputResult.State.CanSearchProduct, "Presenter must allow search when the Local Host-backed boundary is wired.");

        CheckoutPresenterOperationResult searchResult =
            await presenter.SearchProductAsync($"  {barcode}  ").ConfigureAwait(false);

        Require(searchResult.IsSuccess, "Local checkout lookup should succeed.");
        Require(gateway.CallCount == 1, "Local Host gateway should be called exactly once.");
        Require(gateway.LastBarcode == barcode, "Checkout boundary must trim scanned barcode before gateway lookup.");
        Require(searchResult.State.ProductInputText == barcode, "Presenter must keep trimmed product input.");
        Require(searchResult.State.ProductResultText == $"{productName} is ready to add.", "Presenter must show product prepared result text.");
        Require(searchResult.State.StatusMessage == "Product found and prepared.", "Presenter must report prepared status.");
        Require(searchResult.State.PreparedProduct is not null, "Presenter must expose prepared product after Local Host lookup.");
        Require(searchResult.State.CanAddPreparedProductLine, "Presenter must allow adding the prepared Local Host product line.");

        CheckoutPreparedProductViewModel prepared = searchResult.State.PreparedProduct!;

        Require(prepared.ProductId == productId, "Prepared product id must come from Local Host payload.");
        Require(prepared.ProductName == productName, "Prepared product name must come from Local Host payload.");
        Require(prepared.Sku == sku, "Prepared product SKU must come from Local Host payload.");
        Require(prepared.Barcode == barcode, "Prepared product barcode must come from Local Host payload.");
        Require(prepared.UnitName == unitName, "Prepared product unit must come from Local Host payload.");
        Require(prepared.UnitPriceCents == sellingPriceCents, "Prepared product selling price must come from Local Host payload.");
        Require(prepared.CostPriceCents == costPriceCents, "Prepared product cost price must come from Local Host payload.");
        Require(prepared.StockOnHandX1000 == stockOnHandX1000, "Prepared product stock must come from Local Host payload.");
        Require(prepared.TrackStock, "Prepared product track-stock flag must come from Local Host payload.");
        Require(prepared.IsActive, "Prepared product active flag must come from Local Host payload.");

        CheckoutPresenterOperationResult addResult = presenter.AddPreparedProductLine();

        Require(addResult.IsSuccess, "Prepared Local Host product should be addable to the checkout draft.");
        Require(addResult.State.Lines.Count == 1, "Checkout draft must contain exactly one line after adding prepared product.");
        Require(!addResult.State.CanAddPreparedProductLine, "Prepared product should be cleared after adding it to the draft.");

        CheckoutLineViewModel line = addResult.State.Lines[0];

        Require(line.ProductId == productId, "Checkout line product id must match Local Host payload.");
        Require(line.ProductName == productName, "Checkout line product name must match Local Host payload.");
        Require(line.Barcode == barcode, "Checkout line barcode must match Local Host payload.");
        Require(line.UnitName == unitName, "Checkout line unit must match Local Host payload.");
        Require(line.QuantityX1000 == 1000, "Checkout line must default to one unit.");
        Require(line.UnitPriceCents == sellingPriceCents, "Checkout line price must match Local Host payload.");
        Require(line.CostPriceCents == costPriceCents, "Checkout line cost must match Local Host payload.");
        Require(line.GrossCents == sellingPriceCents, "Checkout line gross must equal one unit of selling price.");
        Require(line.LineTotalCents == sellingPriceCents, "Checkout line total must equal one unit of selling price.");
        Require(addResult.State.Totals.SubtotalCents == sellingPriceCents, "Checkout subtotal must reflect the Local Host product line.");
        Require(addResult.State.Totals.TotalCents == sellingPriceCents, "Checkout total must reflect the Local Host product line.");

        await VerifyNotFoundPathAsync().ConfigureAwait(false);
        await VerifyUnavailableFailurePathAsync().ConfigureAwait(false);
    }

    private static async Task VerifyNotFoundPathAsync()
    {
        const string barcode = "000000000000";

        SmokeGateway gateway = SmokeGateway.NotFound();
        CheckoutPresenter presenter = new(new CheckoutLocalHostProductSearchBoundary(gateway));

        CheckoutPresenterOperationResult result =
            await presenter.SearchProductAsync(barcode).ConfigureAwait(false);

        Require(result.IsSuccess, "Not-found lookup should be a successful empty checkout search result.");
        Require(gateway.CallCount == 1, "Not-found lookup should call the gateway once.");
        Require(result.State.PreparedProduct is null, "Not-found lookup must not prepare a product.");
        Require(!result.State.CanAddPreparedProductLine, "Not-found lookup must not allow adding a product line.");
        Require(result.State.ProductResultText == $"No product found for '{barcode}'.", "Not-found lookup should update product result text.");
    }

    private static async Task VerifyUnavailableFailurePathAsync()
    {
        const string barcode = "480000000999";

        SmokeGateway gateway = SmokeGateway.Unavailable("Local Host smoke unavailable.");
        CheckoutPresenter presenter = new(new CheckoutLocalHostProductSearchBoundary(gateway));

        CheckoutPresenterOperationResult result =
            await presenter.SearchProductAsync(barcode).ConfigureAwait(false);

        Require(!result.IsSuccess, "Unavailable Local Host lookup should fail the checkout search operation.");
        Require(gateway.CallCount == 1, "Unavailable lookup should call the gateway once.");
        Require(result.ErrorCode == "CheckoutLocalHostProductSearch.LocalHostUnavailable", "Unavailable lookup should surface Local Host failure code.");
        Require(result.ErrorMessage == "Local Host smoke unavailable.", "Unavailable lookup should surface Local Host failure message.");
        Require(result.State.PreparedProduct is null, "Unavailable lookup must not prepare a product.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class SmokeGateway : IAppLocalHostProductLookupGateway
    {
        private readonly Func<string, AppLocalHostProductLookupResult> _handler;

        private SmokeGateway(Func<string, AppLocalHostProductLookupResult> handler)
        {
            _handler = handler;
        }

        public int CallCount { get; private set; }

        public string? LastBarcode { get; private set; }

        public static SmokeGateway Found(
            Guid productId,
            string barcode,
            string sku,
            string productName,
            string unitName,
            long sellingPriceCents,
            long costPriceCents,
            long stockOnHandX1000)
        {
            return new SmokeGateway(_ => AppLocalHostProductLookupResult.FoundProduct(
                productId,
                sku,
                barcode,
                productName,
                unitName,
                sellingPriceCents,
                costPriceCents,
                stockOnHandX1000,
                trackStock: true,
                allowDiscount: true,
                isActive: true));
        }

        public static SmokeGateway NotFound()
        {
            return new SmokeGateway(AppLocalHostProductLookupResult.NotFound);
        }

        public static SmokeGateway Unavailable(string message)
        {
            return new SmokeGateway(_ => AppLocalHostProductLookupResult.LocalHostUnavailable(message));
        }

        public Task<AppLocalHostProductLookupResult> TryLookupByBarcodeAsync(
            AppLocalHostProductLookupRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CallCount++;
            LastBarcode = request.Barcode;

            return Task.FromResult(_handler(request.Barcode));
        }
    }
}