using System;
using System.Threading;
using System.Threading.Tasks;
using Salryn.Core.Common;
using Salryn.Core.Contracts;
using Salryn.Host.Local.Contracts;

namespace Salryn.App.WinForms.Composition;

// Kirch: Production adapter-to-holder dispatch bridge; it may dispatch through the stopped in-process runtime but must not start runtime or add transport.
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

        string barcode = request.Barcode.Trim();

        SalrynLocalHostCompositionHolder? holder = localHostCompositionHolder;

        if (holder is null)
        {
            return Task.FromResult(
                AppLocalHostProductLookupResult.LocalHostUnavailable(
                    "Local Host ProductLookup adapter shell is present but not wired to the passive holder yet; not wired to holder dispatch yet."));
        }

        return TryDispatchThroughHolderAsync(
            holder,
            barcode,
            cancellationToken);
    }

    private static async Task<AppLocalHostProductLookupResult> TryDispatchThroughHolderAsync(
        SalrynLocalHostCompositionHolder holder,
        string barcode,
        CancellationToken cancellationToken)
    {
        HostRequestEnvelope<LocalHostProductLookupRequest> hostRequest = new(
            RequestId: Guid.NewGuid(),
            OperationKind: HostOperationKind.Query,
            OperationName: HostOperationNames.ProductLookup,
            Context: CreateStoppedRuntimeBridgeContext(),
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

    private static HostRequestContext CreateStoppedRuntimeBridgeContext()
    {
        // Kirch: 65T intentionally carries placeholder identity because runtime remains stopped; a later runtime-start phase must replace this with real terminal/user context.
        return new HostRequestContext(
            TenantId: Guid.Empty,
            StoreId: Guid.Empty,
            UserId: null,
            RoleId: null,
            CashierSessionId: null,
            TerminalId: null,
            DeviceKey: null,
            RequestedAt: DateTimeOffset.UtcNow);
    }
}