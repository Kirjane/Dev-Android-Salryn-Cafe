using System.Threading;
using System.Threading.Tasks;

namespace Salryn.App.WinForms.Composition;

// Kirch: Production adapter implementation shell only.
// This intentionally does not consume the holder, start runtime, dispatch, register, or add transport.
public sealed class CompositionLocalHostProductLookupGatewayAdapter : IAppLocalHostProductLookupGateway
{
    public Task<AppLocalHostProductLookupResult> TryLookupByBarcodeAsync(
        AppLocalHostProductLookupRequest request,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<AppLocalHostProductLookupResult>(cancellationToken);
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Barcode))
        {
            return Task.FromResult(
                AppLocalHostProductLookupResult.InvalidBarcode("Barcode is required."));
        }

        return Task.FromResult(
            AppLocalHostProductLookupResult.LocalHostUnavailable(
                "Local Host ProductLookup adapter shell is present but not wired to the passive holder yet."));
    }
}
