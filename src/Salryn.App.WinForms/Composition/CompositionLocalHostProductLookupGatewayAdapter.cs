using System;
using System.Threading;
using System.Threading.Tasks;
using Salryn.Core.Common;
using Salryn.Core.Contracts;
using Salryn.Host.Local.Contracts;

namespace Salryn.App.WinForms.Composition;

// Kirch file guide: adapter that lets the app ask the Local Host runtime for product lookup.
// UI location: powers barcode/name lookup in Checkout.
// Frontend note: keep forms consuming presenter/boundary APIs; do not call this directly from controls.

// Kirch: Production adapter-to-holder dispatch bridge; it consumes passive request/session identity providers but still must not start runtime or add transport.
public sealed class CompositionLocalHostProductLookupGatewayAdapter : IAppLocalHostProductLookupGateway
{
    private readonly SalrynLocalHostCompositionHolder? localHostCompositionHolder;
    private readonly SalrynLocalHostRequestContextProvider? requestContextProvider;
    private readonly SalrynLocalHostSessionIdentityProvider? sessionIdentityProvider;

    public CompositionLocalHostProductLookupGatewayAdapter()
    {
    }

    // Kirch: Holder-aware compatibility constructor; provider consumption remains unavailable unless provider-aware constructors are used.
    public CompositionLocalHostProductLookupGatewayAdapter(
        SalrynLocalHostCompositionHolder localHostCompositionHolder)
    {
        ArgumentNullException.ThrowIfNull(localHostCompositionHolder);

        this.localHostCompositionHolder = localHostCompositionHolder;
    }

    // Kirch: Request-context-aware compatibility constructor; session identity still fails closed unless the session provider is wired.
    public CompositionLocalHostProductLookupGatewayAdapter(
        SalrynLocalHostCompositionHolder localHostCompositionHolder,
        SalrynLocalHostRequestContextProvider requestContextProvider)
    {
        ArgumentNullException.ThrowIfNull(localHostCompositionHolder);
        ArgumentNullException.ThrowIfNull(requestContextProvider);

        this.localHostCompositionHolder = localHostCompositionHolder;
        this.requestContextProvider = requestContextProvider;
    }

    // Kirch: Full passive provider constructor used by AppServices only; it still does not start runtime or touch transport.
    public CompositionLocalHostProductLookupGatewayAdapter(
        SalrynLocalHostCompositionHolder localHostCompositionHolder,
        SalrynLocalHostRequestContextProvider requestContextProvider,
        SalrynLocalHostSessionIdentityProvider sessionIdentityProvider)
    {
        ArgumentNullException.ThrowIfNull(localHostCompositionHolder);
        ArgumentNullException.ThrowIfNull(requestContextProvider);
        ArgumentNullException.ThrowIfNull(sessionIdentityProvider);

        this.localHostCompositionHolder = localHostCompositionHolder;
        this.requestContextProvider = requestContextProvider;
        this.sessionIdentityProvider = sessionIdentityProvider;
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

        SalrynLocalHostRequestContextProvider? requestProvider = requestContextProvider;

        if (requestProvider is null)
        {
            return Task.FromResult(
                AppLocalHostProductLookupResult.LocalHostUnavailable(
                    "Local Host ProductLookup adapter is wired to the holder but not to the passive request context provider yet."));
        }

        SalrynLocalHostSessionIdentityProvider? sessionProvider = sessionIdentityProvider;

        if (sessionProvider is null)
        {
            return Task.FromResult(
                AppLocalHostProductLookupResult.LocalHostUnavailable(
                    "Local Host ProductLookup adapter is wired to request context but not to the passive app session identity provider yet."));
        }

        return TryDispatchThroughHolderAsync(
            holder,
            requestProvider,
            sessionProvider,
            barcode,
            cancellationToken);
    }

    private static async Task<AppLocalHostProductLookupResult> TryDispatchThroughHolderAsync(
        SalrynLocalHostCompositionHolder holder,
        SalrynLocalHostRequestContextProvider requestProvider,
        SalrynLocalHostSessionIdentityProvider sessionProvider,
        string barcode,
        CancellationToken cancellationToken)
    {
        if (!sessionProvider.TryGetAuthenticatedIdentity(
                out Guid userId,
                out Guid roleId))
        {
            return AppLocalHostProductLookupResult.LocalHostUnavailable(
                "Local Host ProductLookup authenticated app session identity is not available yet.");
        }

        if (!requestProvider.TryCreateAuthenticatedContext(
                userId: userId,
                roleId: roleId,
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
                productId: response.Payload.ProductId,
                sku: response.Payload.Sku,
                barcode: response.Payload.Barcode,
                displayName: response.Payload.DisplayName,
                unitName: response.Payload.UnitName,
                sellingPriceCents: response.Payload.SellingPriceCents,
                costPriceCents: response.Payload.CostPriceCents,
                stockOnHandX1000: response.Payload.StockOnHandX1000,
                trackStock: response.Payload.TrackStock,
                allowDiscount: response.Payload.AllowDiscount,
                isActive: response.Payload.IsActive);
        }

        if (response.Error?.Type == ErrorType.NotFound)
        {
            return AppLocalHostProductLookupResult.NotFound(barcode);
        }

        return AppLocalHostProductLookupResult.LocalHostUnavailable(
            response.Error?.Message ?? "Local Host ProductLookup dispatch did not return a product.");
    }
}
