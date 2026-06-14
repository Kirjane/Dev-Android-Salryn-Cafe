using Salryn.Core.Common;
using Salryn.Core.Contracts;
using Salryn.Core.Deployment;
using Salryn.Core.Hosting;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.ValueObjects;
using Salryn.Host.Local.Bootstrap;
using Salryn.Host.Local.Composition;
using Salryn.Host.Local.Contracts;
using Salryn.Host.Local.Runtime;

namespace Salryn.Tests;

// Kirch: Verifies Phase 64C ProductLookup composition without changing the default placeholder composition path.
public static class Phase64CLocalHostProductLookupCompositionChecks
{
    // Kirch: Proves ProductLookup can be composed through an explicit composition method only.
    public static async Task RunAsync()
    {
        StoreDeploymentProfile profile = CreateProfile();
        HostOperationCatalog operationCatalog = HostOperationCatalog.CreateDefault();
        LocalHostOperationContractMap contractMap = LocalHostOperationContractMap.CreateDefault();
        FakeProductReadRepository products = new();
        LocalHostBootstrapComposer composer = new();

        LocalHostBootstrapCompositionResult productLookupComposition = composer.ComposeWithProductLookup(
            profile,
            products,
            operationCatalog,
            contractMap);

        if (!LocalHostBootstrapCompositionRules.IsCompositionValid(productLookupComposition))
        {
            throw new InvalidOperationException("ProductLookup composition should be valid.");
        }

        if (productLookupComposition.Snapshot.State != LocalHostRuntimeState.Stopped)
        {
            throw new InvalidOperationException("ProductLookup composition should remain stopped until explicitly started.");
        }

        if (productLookupComposition.Snapshot.RegisteredHandlerCount != operationCatalog.Descriptors.Count
            || productLookupComposition.BootstrapResult.RegisteredHandlerCount != operationCatalog.Descriptors.Count)
        {
            throw new InvalidOperationException("ProductLookup composition should keep full handler count alignment.");
        }

        productLookupComposition.Runtime.Start(DateTimeOffset.UtcNow);

        HostResponseEnvelope<LocalHostProductLookupResponse> lookupResponse = await productLookupComposition.Runtime.DispatchAsync<LocalHostProductLookupRequest, LocalHostProductLookupResponse>(
            CreateProductLookupRequest(profile.Store.TenantId, profile.Store.StoreId, "1234567890"));

        if (!lookupResponse.IsSuccess
            || lookupResponse.Payload is null
            || lookupResponse.Payload.DisplayName != "Composed Product")
        {
            throw new InvalidOperationException("ProductLookup composition should dispatch to the real ProductLookup handler.");
        }

        if (products.FindByBarcodeCallCount != 1
            || products.LastBarcodeValue != "1234567890")
        {
            throw new InvalidOperationException("ProductLookup composition should use the Core product repository contract.");
        }

        HostResponseEnvelope<LocalHostProductSearchResponse> searchResponse = await productLookupComposition.Runtime.DispatchAsync<LocalHostProductSearchRequest, LocalHostProductSearchResponse>(
            CreateProductSearchRequest(profile.Store.TenantId, profile.Store.StoreId));

        if (searchResponse.IsSuccess
            || searchResponse.Error?.Code != LocalHostHandlerBootstrapErrorCodes.HandlerNotImplemented)
        {
            throw new InvalidOperationException("Non-ProductLookup operations should remain placeholder-backed in Phase 64C.");
        }

        LocalHostBootstrapCompositionResult defaultComposition = composer.Compose(
            profile,
            operationCatalog,
            contractMap);

        defaultComposition.Runtime.Start(DateTimeOffset.UtcNow);

        HostResponseEnvelope<LocalHostProductLookupResponse> defaultLookupResponse = await defaultComposition.Runtime.DispatchAsync<LocalHostProductLookupRequest, LocalHostProductLookupResponse>(
            CreateProductLookupRequest(profile.Store.TenantId, profile.Store.StoreId, "5555555555"));

        if (defaultLookupResponse.IsSuccess
            || defaultLookupResponse.Error?.Code != LocalHostHandlerBootstrapErrorCodes.HandlerNotImplemented)
        {
            throw new InvalidOperationException("Default composition should still keep ProductLookup placeholder-backed.");
        }

        if (products.FindByBarcodeCallCount != 1)
        {
            throw new InvalidOperationException("Default composition should not use the ProductLookup repository dependency.");
        }
    }

    // Kirch: Creates a valid local host profile for composition checks.
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

    // Kirch: Creates a ProductLookup request for composition dispatch checks.
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

    // Kirch: Creates a ProductSearch request to prove other operations remain placeholder-backed.
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

    // Kirch: Creates valid Host request context for composition smoke checks.
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

    // Kirch: Fake Core repository lets composition prove ProductLookup dispatch without implementation coupling.
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
                Sku: "SKU-COMPOSED",
                Barcode: barcode.Value,
                Name: "Composed Product",
                UnitName: "piece",
                SellingPrice: Money.FromPesos(40m),
                CostPrice: Money.FromPesos(25m),
                StockOnHand: Quantity.FromUnits(6m).Value,
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
