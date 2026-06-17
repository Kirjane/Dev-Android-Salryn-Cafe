using System;
using System.Threading;
using System.Threading.Tasks;
using Salryn.App.WinForms.Composition;
using Salryn.Core.Common;
using Salryn.Core.ReadModels;
using Salryn.Core.ValueObjects;

namespace Salryn.App.WinForms.Features.Checkout;

// Kirch file guide: checkout product lookup via Local Host-backed gateway.
// UI location: supports the Checkout search input.
// Frontend note: scanner/search UI can change, but lookup still flows through this boundary.

// Kirch: Checkout-facing Local Host ProductLookup adapter only; it does not start runtime, dispatch directly, touch transport, or wire UI.
public sealed class CheckoutLocalHostProductSearchBoundary : ICheckoutProductSearchBoundary
{
    private readonly IAppLocalHostProductLookupGateway _localHostProductLookupGateway;

    public CheckoutLocalHostProductSearchBoundary(IAppLocalHostProductLookupGateway localHostProductLookupGateway)
    {
        _localHostProductLookupGateway =
            localHostProductLookupGateway ?? throw new ArgumentNullException(nameof(localHostProductLookupGateway));
    }

    public async Task<Result<ProductLookupReadModel?>> FindProductAsync(
        string searchText,
        CancellationToken cancellationToken = default)
    {
        string trimmed = searchText?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Result<ProductLookupReadModel?>.Success(null);
        }

        AppLocalHostProductLookupResult lookupResult =
            await _localHostProductLookupGateway.TryLookupByBarcodeAsync(
                new AppLocalHostProductLookupRequest(trimmed),
                cancellationToken).ConfigureAwait(false);

        if (!lookupResult.Succeeded)
        {
            return Result<ProductLookupReadModel?>.Fail(MapFailure(lookupResult));
        }

        if (!lookupResult.Found)
        {
            return Result<ProductLookupReadModel?>.Success(null);
        }

        if (lookupResult.Product is null)
        {
            return Result<ProductLookupReadModel?>.Fail(Error.Unexpected(
                "CheckoutLocalHostProductSearch.ProductPayloadMissing",
                "Local Host ProductLookup reported a found product but did not return product data."));
        }

        return MapProduct(lookupResult.Product);
    }

    private static Result<ProductLookupReadModel?> MapProduct(AppLocalHostProductLookupProduct product)
    {
        Result<Quantity> stockOnHandResult = Quantity.FromX1000(product.StockOnHandX1000);

        if (stockOnHandResult.IsFailure)
        {
            return Result<ProductLookupReadModel?>.Fail(Error.Validation(
                "CheckoutLocalHostProductSearch.InvalidStockOnHand",
                stockOnHandResult.Error));
        }

        Money? costPrice = product.CostPriceCents.HasValue
            ? Money.FromCents(product.CostPriceCents.Value)
            : null;

        ProductLookupReadModel readModel = new ProductLookupReadModel(
            Id: product.ProductId,
            Sku: product.Sku,
            Barcode: product.Barcode,
            Name: product.DisplayName,
            UnitName: product.UnitName,
            SellingPrice: Money.FromCents(product.SellingPriceCents),
            CostPrice: costPrice,
            StockOnHand: stockOnHandResult.Value,
            TrackStock: product.TrackStock,
            AllowDiscount: product.AllowDiscount,
            IsActive: product.IsActive);

        return Result<ProductLookupReadModel?>.Success(readModel);
    }

    private static Error MapFailure(AppLocalHostProductLookupResult lookupResult)
    {
        string code = string.IsNullOrWhiteSpace(lookupResult.FailureCode)
            ? "CheckoutLocalHostProductSearch.LookupFailed"
            : $"CheckoutLocalHostProductSearch.{lookupResult.FailureCode}";

        string message = string.IsNullOrWhiteSpace(lookupResult.FailureMessage)
            ? "Local Host product lookup failed."
            : lookupResult.FailureMessage;

        return string.Equals(lookupResult.FailureCode, "InvalidBarcode", StringComparison.Ordinal)
            ? Error.Validation(code, message)
            : Error.Failure(code, message);
    }
}
