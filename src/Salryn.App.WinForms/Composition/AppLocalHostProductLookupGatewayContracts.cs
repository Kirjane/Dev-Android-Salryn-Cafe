using System.Threading;
using System.Threading.Tasks;

namespace Salryn.App.WinForms.Composition;

// Kirch: Production contract shell only. No implementation, registration, runtime start, dispatch, or transport.
public interface IAppLocalHostProductLookupGateway
{
    Task<AppLocalHostProductLookupResult> TryLookupByBarcodeAsync(
        AppLocalHostProductLookupRequest request,
        CancellationToken cancellationToken);
}

public sealed record AppLocalHostProductLookupRequest(string Barcode);

public sealed record AppLocalHostProductLookupProduct(
    string Barcode,
    string? Sku,
    string DisplayName);

public sealed record AppLocalHostProductLookupResult(
    bool Succeeded,
    bool Found,
    AppLocalHostProductLookupProduct? Product,
    string? FailureCode,
    string? FailureMessage)
{
    public static AppLocalHostProductLookupResult FoundProduct(
        string barcode,
        string? sku,
        string displayName)
    {
        return new AppLocalHostProductLookupResult(
            Succeeded: true,
            Found: true,
            Product: new AppLocalHostProductLookupProduct(barcode, sku, displayName),
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
