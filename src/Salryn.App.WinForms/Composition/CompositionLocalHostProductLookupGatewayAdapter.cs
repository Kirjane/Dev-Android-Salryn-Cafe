using System;
using System.Threading;
using System.Threading.Tasks;
using Salryn.Core.Common;
using Salryn.Core.Contracts;
using Salryn.Host.Local.Contracts;

namespace Salryn.App.WinForms.Composition;

// Kirch: Production adapter-to-holder dispatch bridge; it consumes the passive request context provider but still must not start runtime or add transport.
public sealed class CompositionLocalHostProductLookupGatewayAdapter : IAppLocalHostProductLookupGateway
{
    private readonly SalrynLocalHostCompositionHolder? localHostCompositionHolder;
    private readonly SalrynLocalHostRequestContextProvider? requestContextProvider;

    public CompositionLocalHostProductLookupGatewayAdapter()
    {
    }

    // Kirch: Holder-aware compatibility constructor; provider consumption remains unavailable unless the provider-aware constructor is used.
    public CompositionLocalHostProductLookupGatewayAdapter(
        SalrynLocalHostCompositionHolder localHostCompositionHolder)
    {
        ArgumentNullException.ThrowIfNull(localHostCompositionHolder);

        this.localHostCompositionHolder = localHostCompositionHolder;
    }

    // Kirch: Provider-aware constructor used by AppServices only; it still does not start runtime or touch transport.
    public CompositionLocalHostProductLookupGatewayAdapter(
        SalrynLocalHostCompositionHolder localHostCompositionHolder,
        SalrynLocalHostRequestContextProvider requestContextProvider)
    {
        ArgumentNullException.ThrowIfNull(localHostCompositionHolder);
        ArgumentNullException.ThrowIfNull(requestContextProvider);

        this.localHostCompositionHolder = localHostCompositionHolder;
        this.requestContextProvider = requestContextProvider;
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

        string barcode = request.Barcode.Trim();

        SalrynLocalHostCompositionHolder? holder = localHostCompositionHolder;

        if (holder is null)
        {
            return Task.FromResult(
                AppLocalHostProductLookupResult.LocalHostUnavailable(
                    "Local Host ProductLookup adapter shell is present but not wired to the passive holder yet; not wired to holder dispatch yet."));
        }

        SalrynLocalHostRequestContextProvider? provider = requestContextProvider;

        if (provider is null)
        {
            return Task.FromResult(
                AppLocalHostProductLookupResult.LocalHostUnavailable(
                    "Local Host ProductLookup adapter is wired to the holder but not to the passive request context provider yet."));
        }

        return TryDispatchThroughHolderAsync(
            holder,
            provider,
            barcode,
            cancellationToken);
    }

    private static async Task<AppLocalHostProductLookupResult> TryDispatchThroughHolderAsync(
        SalrynLocalHostCompositionHolder holder,
        SalrynLocalHostRequestContextProvider provider,
        string barcode,
        CancellationToken cancellationToken)
    {
        if (!provider.TryCreateAuthenticatedContext(
                userId: null,
                roleId: null,
                cashierSessionId: null,
                requestedAt: DateTimeOffset.UtcNow,
                out HostRequestContext? context) ||
            context is null)
        {
            return AppLocalHostProductLookupResult.LocalHostUnavailable(
                "Local Host ProductLookup request context is not available from the passive provider yet.");
        }

        HostRequestEnvelope<LocalHostProductLookupRequest> hostRequest = new(
            RequestId: Guid.NewGuid(),
            OperationKind: HostOperationKind.Query,
            OperationName: HostOperationNames.ProductLookup,
            Context: context,
            Payload: new LocalHostProductLookupRequest(barcode));

        HostResponseEnvelope<LocalHostProductLookupResponse> response =
            await holder.Composition.Runtime.DispatchAsync<LocalHostProductLookupRequest, LocalHostProductLookupResponse>(
                hostRequest,
                cancellationToken);

        if (response.IsSuccess && response.Payload is not null)
        {
            return AppLocalHostProductLookupResult.FoundProduct(
                barcode,
                sku: null,
                response.Payload.DisplayName);
        }

        if (response.Error?.Type == ErrorType.NotFound)
        {
            return AppLocalHostProductLookupResult.NotFound(barcode);
        }

        return AppLocalHostProductLookupResult.LocalHostUnavailable(
            response.Error?.Message ?? "Local Host ProductLookup dispatch did not return a product.");
    }
}