using System;
using System.Threading;
using System.Threading.Tasks;

namespace Salryn.App.WinForms.Composition;

// Kirch: Production adapter implementation shell only.
// This intentionally does not start runtime, dispatch, register, perform lookup through the holder, or add transport.
public sealed class CompositionLocalHostProductLookupGatewayAdapter : IAppLocalHostProductLookupGateway
{
    private readonly SalrynLocalHostCompositionHolder? localHostCompositionHolder;

    public CompositionLocalHostProductLookupGatewayAdapter()
    {
    }

    // Kirch: Holder-aware constructor boundary shell only; storing the holder is not lookup consumption, dispatch, runtime start, or transport.
    public CompositionLocalHostProductLookupGatewayAdapter(
        SalrynLocalHostCompositionHolder localHostCompositionHolder)
    {
        ArgumentNullException.ThrowIfNull(localHostCompositionHolder);

        this.localHostCompositionHolder = localHostCompositionHolder;
    }

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
                "Local Host ProductLookup adapter shell is present but not wired to the passive holder yet; not wired to holder dispatch yet."));
    }
}