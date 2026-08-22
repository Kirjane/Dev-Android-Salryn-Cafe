using Salryn.Core.Contracts;
using Salryn.Core.Deployment;
using Salryn.Core.Hosting;
using Salryn.Core.Models;
using Salryn.Host.Local.Contracts;
using Salryn.Host.Local.Runtime;

namespace Salryn.Tests;

// Kirch: Verifies Phase 63D Local Host operation contract mapping without real POS handlers.
public static class Phase63DLocalHostOperationContractMappingCompileChecks
{
    // Kirch: This smoke check proves operation contract coverage and typed handler registration enforcement.
    public static async Task RunAsync()
    {
        HostOperationCatalog operationCatalog = HostOperationCatalog.CreateDefault();
        LocalHostOperationContractMap contractMap = LocalHostOperationContractMap.CreateDefault();

        if (contractMap.Contracts.Count != operationCatalog.Descriptors.Count)
        {
            throw new InvalidOperationException("Local Host contract map should cover every default Host operation.");
        }

        if (!LocalHostOperationContractRules.CoversCatalog(contractMap, operationCatalog))
        {
            throw new InvalidOperationException("Local Host contract map should match the Core operation catalog.");
        }

        LocalHostOperationContractDescriptor? lookupContract = contractMap.Find(HostOperationNames.ProductLookup);

        if (lookupContract is null
            || lookupContract.PayloadType != typeof(LocalHostProductLookupRequest)
            || lookupContract.ResponseType != typeof(LocalHostProductLookupResponse)
            || lookupContract.OperationKind != HostOperationKind.Query)
        {
            throw new InvalidOperationException("Product lookup operation contract should map to the expected payload and response types.");
        }

        StoreDeploymentProfile profile = CreateProfile();
        LocalHostRuntime runtime = new(profile, operationCatalog, contractMap);

        if (runtime.GetSnapshot().RegisteredContractCount != operationCatalog.Descriptors.Count)
        {
            throw new InvalidOperationException("Local Host runtime snapshot should expose registered contract count.");
        }

        bool rejectedWrongHandlerContract = false;

        try
        {
            runtime.RegisterHandler(
                HostOperationNames.ProductLookup,
                new WrongProductLookupHandler());
        }
        catch (InvalidOperationException)
        {
            rejectedWrongHandlerContract = true;
        }

        if (!rejectedWrongHandlerContract)
        {
            throw new InvalidOperationException("Local Host runtime should reject handlers with mismatched payload and response contracts.");
        }

        bool rejectedUnknownOperation = false;

        try
        {
            runtime.RegisterHandler(
                "unknown.operation",
                new ProductLookupHandler());
        }
        catch (InvalidOperationException)
        {
            rejectedUnknownOperation = true;
        }

        if (!rejectedUnknownOperation)
        {
            throw new InvalidOperationException("Local Host runtime should reject handlers for unknown operation contracts.");
        }

        runtime.RegisterHandler(
            HostOperationNames.ProductLookup,
            new ProductLookupHandler());

        if (runtime.GetSnapshot().RegisteredHandlerCount != 1)
        {
            throw new InvalidOperationException("Local Host runtime should count registered handlers after contract-safe registration.");
        }

        runtime.Start(DateTimeOffset.UtcNow);

        HostRequestEnvelope<LocalHostProductLookupRequest> request = CreateProductLookupRequest(
            tenantId: profile.Store.TenantId,
            storeId: profile.Store.StoreId);

        HostResponseEnvelope<LocalHostProductLookupResponse> response = await runtime.DispatchAsync<LocalHostProductLookupRequest, LocalHostProductLookupResponse>(request);

        if (!response.IsSuccess || response.Payload?.DisplayName != "Mapped sample-payload")
        {
            throw new InvalidOperationException("Local Host runtime should dispatch a contract-mapped operation handler.");
        }
    }

    // Kirch: Creates a valid local host profile for operation contract mapping smoke checks.
    private static StoreDeploymentProfile CreateProfile()
    {
        return new StoreDeploymentProfile(
            Store: new StoreIdentity(
                TenantId: Guid.NewGuid(),
                StoreId: Guid.NewGuid()),
            Host: new StoreHostIdentity(
                HostId: Guid.NewGuid(),
                MachineName: "field-node",
                Role: SalrynMachineRole.StandaloneHost),
            Mode: SalrynDeploymentMode.Standalone,
            Endpoint: null,
            Terminal: null);
    }

    // Kirch: Creates a product lookup request that matches the Local Host operation contract map.
    private static HostRequestEnvelope<LocalHostProductLookupRequest> CreateProductLookupRequest(Guid tenantId, Guid storeId)
    {
        return new HostRequestEnvelope<LocalHostProductLookupRequest>(
            RequestId: Guid.NewGuid(),
            OperationKind: HostOperationKind.Query,
            OperationName: HostOperationNames.ProductLookup,
            Context: new HostRequestContext(
                TenantId: tenantId,
                StoreId: storeId,
                UserId: Guid.NewGuid(),
                RoleId: Guid.NewGuid(),
                CashierSessionId: null,
                TerminalId: Guid.NewGuid(),
                DeviceKey: "field-node-terminal",
                RequestedAt: DateTimeOffset.UtcNow),
            Payload: new LocalHostProductLookupRequest("sample-payload"));
    }

    // Kirch: Wrong sample handler exists only to prove payload/response contract rejection.
    private sealed class WrongProductLookupHandler : IHostOperationHandler<string, string>
    {
        public Task<HostResponseEnvelope<string>> HandleAsync(
            HostRequestEnvelope<string> request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new HostResponseEnvelope<string>(
                RequestId: request.RequestId,
                IsSuccess: true,
                Payload: request.Payload,
                Error: null));
        }
    }

    // Kirch: Product lookup sample handler exists only to prove mapped in-process dispatch.
    private sealed class ProductLookupHandler : IHostOperationHandler<LocalHostProductLookupRequest, LocalHostProductLookupResponse>
    {
        public Task<HostResponseEnvelope<LocalHostProductLookupResponse>> HandleAsync(
            HostRequestEnvelope<LocalHostProductLookupRequest> request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new HostResponseEnvelope<LocalHostProductLookupResponse>(
                RequestId: request.RequestId,
                IsSuccess: true,
                Payload: new LocalHostProductLookupResponse(
                    ProductId: Guid.NewGuid(),
                    Sku: null,
                    Barcode: request.Payload.BarcodeOrSku,
                    DisplayName: $"Mapped {request.Payload.BarcodeOrSku}",
                    UnitName: "piece",
                    SellingPriceCents: 100,
                    CostPriceCents: null,
                    StockOnHandX1000: 1000,
                    TrackStock: true,
                    AllowDiscount: true,
                    TaxClassification: (int)ProductTaxClassification.Vatable,
                    IsActive: true),
                Error: null));
        }
    }
}
