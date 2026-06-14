using Salryn.Core.Common;
using Salryn.Core.Contracts;
using Salryn.Core.Deployment;
using Salryn.Core.Hosting;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.ValueObjects;
using Salryn.Host.Local.Bootstrap;
using Salryn.Host.Local.Contracts;
using Salryn.Host.Local.Runtime;

namespace Salryn.Tests;

// Kirch: Verifies Phase 64B runtime registration for ProductLookup while other operations stay placeholder-backed.
public static class Phase64BLocalHostProductLookupRuntimeRegistrationChecks
{
    // Kirch: Proves the real ProductLookup handler can be registered through the runtime gate.
    public static async Task RunAsync()
    {
        StoreDeploymentProfile profile = CreateProfile();
        HostOperationCatalog operationCatalog = HostOperationCatalog.CreateDefault();
        LocalHostOperationContractMap contractMap = LocalHostOperationContractMap.CreateDefault();
        LocalHostRuntime runtime = new(profile, operationCatalog, contractMap);
        FakeProductReadRepository products = new();
        LocalHostHandlerBootstrapper bootstrapper = new();

        LocalHostHandlerBootstrapResult bootstrapResult = bootstrapper.RegisterDefaultHandlersWithProductLookup(
            runtime,
            products);

        LocalHostRuntimeSnapshot stoppedSnapshot = runtime.GetSnapshot();

        if (bootstrapResult.RegisteredHandlerCount != operationCatalog.Descriptors.Count
            || stoppedSnapshot.RegisteredHandlerCount != operationCatalog.Descriptors.Count)
        {
            throw new InvalidOperationException("ProductLookup runtime registration should keep full handler count alignment.");
        }

        if (!bootstrapResult.RegisteredOperationNames.Contains(HostOperationNames.ProductLookup))
        {
            throw new InvalidOperationException("ProductLookup runtime registration should include the ProductLookup operation.");
        }

        runtime.Start(DateTimeOffset.UtcNow);

        HostResponseEnvelope<LocalHostProductLookupResponse> lookupResponse = await runtime.DispatchAsync<LocalHostProductLookupRequest, LocalHostProductLookupResponse>(
            CreateProductLookupRequest(profile.Store.TenantId, profile.Store.StoreId, "1234567890"));

        if (!lookupResponse.IsSuccess
            || lookupResponse.Payload is null
            || lookupResponse.Payload.DisplayName != "Runtime Product")
        {
            throw new InvalidOperationException("ProductLookup runtime registration should dispatch to the real ProductLookup handler.");
        }

        if (products.FindByBarcodeCallCount != 1
            || products.LastBarcodeValue != "1234567890")
        {
            throw new InvalidOperationException("Runtime ProductLookup dispatch should use the Core product repository contract.");
        }

        HostResponseEnvelope<LocalHostProductSearchResponse> searchResponse = await runtime.DispatchAsync<LocalHostProductSearchRequest, LocalHostProductSearchResponse>(
            CreateProductSearchRequest(profile.Store.TenantId, profile.Store.StoreId));

        if (searchResponse.IsSuccess
            || searchResponse.Error?.Code != LocalHostHandlerBootstrapErrorCodes.HandlerNotImplemented)
        {
            throw new InvalidOperationException("Other operations should remain placeholder-backed in Phase 64B.");
        }

        if (runtime.GetSnapshot().State != LocalHostRuntimeState.Running)
        {
            throw new InvalidOperationException("ProductLookup runtime registration should not fault the runtime.");
        }
    }

    // Kirch: Creates a valid local host profile for runtime registration checks.
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

    // Kirch: Creates a ProductLookup request for the Phase 64B runtime dispatch path.
    private static HostRequestEnvelope<LocalHostProductLookupRequest> CreateProductLookupRequest(
        Guid tenantId,
        Guid storeId,
        string lookupCode)
    {
        return new HostRequestEnvelope<LocalHostProductLookupRequest>(
            RequestId: Guid.NewGuid(),
            OperationKind: HostOperationKind.Query,
            OperationName: HostOperationNames.ProductLookup,
            Context: CreateContext(tenantId, storeId),
            Payload: new LocalHostProductLookupRequest(
                BarcodeOrSku: lookupCode));
    }

    // Kirch: Creates a ProductSearch request to prove non-64B operations still use placeholder handlers.
    private static HostRequestEnvelope<LocalHostProductSearchRequest> CreateProductSearchRequest(
        Guid tenantId,
        Guid storeId)
    {
        return new HostRequestEnvelope<LocalHostProductSearchRequest>(
            RequestId: Guid.NewGuid(),
            OperationKind: HostOperationKind.Query,
            OperationName: HostOperationNames.ProductSearch,
            Context: CreateContext(tenantId, storeId),
            Payload: new LocalHostProductSearchRequest(
                SearchText: "sample",
                MaxResults: 10));
    }

    // Kirch: Creates valid Host request context for query dispatch smoke checks.
    private static HostRequestContext CreateContext(Guid tenantId, Guid storeId)
    {
        return new HostRequestContext(
            TenantId: tenantId,
            StoreId: storeId,
            UserId: Guid.NewGuid(),
            RoleId: Guid.NewGuid(),
            CashierSessionId: null,
            TerminalId: Guid.NewGuid(),
            DeviceKey: "field-node-terminal",
            RequestedAt: DateTimeOffset.UtcNow);
    }

    // Kirch: Fake Core repository lets runtime registration prove handler dispatch without implementation coupling.
    private sealed class FakeProductReadRepository : IProductReadRepository
    {
        public int FindByBarcodeCallCount { get; private set; }

        public string? LastBarcodeValue { get; private set; }

        public Task<Result<ProductLookupReadModel?>> FindByIdAsync(
            Guid productId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<ProductLookupReadModel?>.Success(null));
        }

        public Task<Result<ProductDetailReadModel?>> FindDetailByIdAsync(
            Guid productId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<ProductDetailReadModel?>.Success(null));
        }

        public Task<Result<ProductLookupReadModel?>> FindByBarcodeAsync(
            Barcode barcode,
            CancellationToken cancellationToken = default)
        {
            FindByBarcodeCallCount++;
            LastBarcodeValue = barcode.Value;

            ProductLookupReadModel product = new(
                Id: Guid.NewGuid(),
                Sku: "SKU-RUNTIME",
                Barcode: barcode.Value,
                Name: "Runtime Product",
                UnitName: "piece",
                SellingPrice: Money.FromPesos(30m),
                CostPrice: Money.FromPesos(20m),
                StockOnHand: Quantity.FromUnits(8m).Value,
                TrackStock: true,
                AllowDiscount: true,
                IsActive: true);

            return Task.FromResult(Result<ProductLookupReadModel?>.Success(product));
        }

        public Task<Result<PagedResult<ProductListItemReadModel>>> SearchAsync(
            ProductSearchFilter filter,
            CancellationToken cancellationToken = default)
        {
            PagedResult<ProductListItemReadModel> result = new(
                Items: Array.Empty<ProductListItemReadModel>(),
                TotalCount: 0,
                PageNumber: filter.Page.PageNumber,
                PageSize: filter.Page.PageSize);

            return Task.FromResult(Result<PagedResult<ProductListItemReadModel>>.Success(result));
        }

        public Task<Result<IReadOnlyList<LowStockProductReadModel>>> GetLowStockAsync(
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<LowStockProductReadModel> empty = Array.Empty<LowStockProductReadModel>();
            return Task.FromResult(Result<IReadOnlyList<LowStockProductReadModel>>.Success(empty));
        }
    }
}
