using System.Threading;
using System.Threading.Tasks;
using Salryn.Core.Models;

namespace Salryn.App.WinForms.Composition;

// Kirch file guide: app-side contract for checkout product lookup through Local Host.
// UI location: used behind the Checkout search box; users do not see this directly.
// Frontend note: do not change payload shape for styling; this is a backend/UI boundary.

// Kirch: Production contract shell only. No implementation, registration, runtime start, dispatch, or transport.
public interface IAppLocalHostProductLookupGateway
{
    Task<AppLocalHostProductLookupResult> TryLookupByBarcodeAsync(
        AppLocalHostProductLookupRequest request,
        CancellationToken cancellationToken);
}

public sealed record AppLocalHostProductLookupRequest(string Barcode);

public sealed record AppLocalHostProductLookupProduct(
    Guid ProductId,
    string? Sku,
    string? Barcode,
    string DisplayName,
    string UnitName,
    long SellingPriceCents,
    long? CostPriceCents,
    long StockOnHandX1000,
    bool TrackStock,
    bool AllowDiscount,
    ProductTaxClassification TaxClassification,
    bool IsActive);

public sealed record AppLocalHostProductLookupResult(
    bool Succeeded,
    bool Found,
    AppLocalHostProductLookupProduct? Product,
    string? FailureCode,
    string? FailureMessage)
{
    public static AppLocalHostProductLookupResult FoundProduct(
        Guid productId,
        string? sku,
        string? barcode,
        string displayName,
        string unitName,
        long sellingPriceCents,
        long? costPriceCents,
        long stockOnHandX1000,
        bool trackStock,
        bool allowDiscount,
        ProductTaxClassification taxClassification,
        bool isActive)
    {
        return new AppLocalHostProductLookupResult(
            Succeeded: true,
            Found: true,
            Product: new AppLocalHostProductLookupProduct(
                productId,
                sku,
                barcode,
                displayName,
                unitName,
                sellingPriceCents,
                costPriceCents,
                stockOnHandX1000,
                trackStock,
                allowDiscount,
                taxClassification,
                isActive),
            FailureCode: null,
            FailureMessage: null);
    }

    public static AppLocalHostProductLookupResult NotFound(string barcode)
    {
        return new AppLocalHostProductLookupResult(
            Succeeded: true,
            Found: false,
            Product: null,
            FailureCode: null,
            FailureMessage: null);
    }

    public static AppLocalHostProductLookupResult InvalidBarcode(string failureMessage)
    {
        return new AppLocalHostProductLookupResult(
            Succeeded: false,
            Found: false,
            Product: null,
            FailureCode: "InvalidBarcode",
            FailureMessage: failureMessage);
    }

    public static AppLocalHostProductLookupResult LocalHostUnavailable(string failureMessage)
    {
        return new AppLocalHostProductLookupResult(
            Succeeded: false,
            Found: false,
            Product: null,
            FailureCode: "LocalHostUnavailable",
            FailureMessage: failureMessage);
    }
}
