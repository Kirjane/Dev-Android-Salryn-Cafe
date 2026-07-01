using Salryn.Core.Common;
using Salryn.Core.Contracts;
using Salryn.Core.Deployment;
using Salryn.Core.Hosting;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.ValueObjects;
using Salryn.Host.Local.Bootstrap;
using Salryn.Host.Local.Composition;
using Salryn.Host.Local.Contracts;
using Salryn.Host.Local.Readiness;
using Salryn.Host.Local.Runtime;

namespace Salryn.Tests;

// Kirch: Seals Phase 64D ProductLookup composition readiness without adding new runtime behavior.
public static class Phase64DLocalHostProductLookupCompositionReadinessSealChecks
{
    // Kirch: Proves explicit ProductLookup composition is readiness-sealable while default composition stays placeholder-safe.
    public static async Task RunAsync()
    {
        StoreDeploymentProfile profile = CreateProfile();
        HostOperationCatalog operationCatalog = HostOperationCatalog.CreateDefault();
        LocalHostOperationContractMap contractMap = LocalHostOperationContractMap.CreateDefault();
        LocalHostBootstrapComposer composer = new();
        LocalHostOperationSurfaceReadinessInspector inspector = new();
        FakeProductReadRepository products = new();

        LocalHostBootstrapCompositionResult productLookupComposition = composer.ComposeWithProductLookup(
            profile,
            products,
            operationCatalog,
            contractMap);

        LocalHostOperationSurfaceReadinessReport productLookupReadyReport = inspector.Inspect(
            productLookupComposition,
            operationCatalog,
            contractMap);

        if (!productLookupReadyReport.IsReady)
        {
            throw new InvalidOperationException("ProductLookup composition readiness seal should pass while runtime is stopped.");
        }

        if (productLookupReadyReport.ExpectedOperationCount != operationCatalog.Descriptors.Count
            || productLookupReadyReport.ReadyOperationCount != operationCatalog.Descriptors.Count)
        {
            throw new InvalidOperationException("ProductLookup composition readiness seal should align all operation counts.");
        }

        if (productLookupReadyReport.Issues.Count != 0)
        {
            throw new InvalidOperationException("ProductLookup composition readiness seal should not report issues.");
        }

        if (!productLookupReadyReport.ReadyOperationNames.Contains(HostOperationNames.ProductLookup)
            || !productLookupReadyReport.ReadyOperationNames.Contains(HostOperationNames.ProductSearch)
            || !productLookupReadyReport.ReadyOperationNames.Contains(HostOperationNames.CompleteSale)
            || !productLookupReadyReport.ReadyOperationNames.Contains(HostOperationNames.BackupHealth))
        {
            throw new InvalidOperationException("ProductLookup composition readiness seal should include important Local Host operation names.");
        }

        productLookupComposition.Runtime.Start(DateTimeOffset.UtcNow);

        LocalHostOperationSurfaceReadinessReport runningRuntimeReport = inspector.Inspect(
            productLookupComposition,
            operationCatalog,
            contractMap);

        if (runningRuntimeReport.IsReady
            || !runningRuntimeReport.Issues.Any(issue =>
                issue.Reason == LocalHostOperationSurfaceReadinessFailureReason.RuntimeNotStopped))
        {
            throw new InvalidOperationException("ProductLookup readiness seal should reject a started runtime.");
        }

        HostResponseEnvelope<LocalHostProductLookupResponse> lookupResponse = await productLookupComposition.Runtime.DispatchAsync<LocalHostProductLookupRequest, LocalHostProductLookupResponse>(
            CreateProductLookupRequest(profile.Store.TenantId, profile.Store.StoreId, "1234567890"));

        if (!lookupResponse.IsSuccess
            || lookupResponse.Payload is null
            || lookupResponse.Payload.DisplayName != "Sealed Product")
        {
            throw new InvalidOperationException("Sealed ProductLookup composition should dispatch through the real handler.");
        }

        if (products.FindByBarcodeCallCount != 1
            || products.LastBarcodeValue != "1234567890")
        {
            throw new InvalidOperationException("Sealed ProductLookup composition should use the Core product repository contract exactly once.");
        }

        HostResponseEnvelope<LocalHostProductSearchResponse> searchResponse = await productLookupComposition.Runtime.DispatchAsync<LocalHostProductSearchRequest, LocalHostProductSearchResponse>(
            CreateProductSearchRequest(profile.Store.TenantId, profile.Store.StoreId));

        if (searchResponse.IsSuccess
            || searchResponse.Error?.Code != LocalHostHandlerBootstrapErrorCodes.HandlerNotImplemented)
        {
            throw new InvalidOperationException("Sealed ProductLookup composition should keep non-ProductLookup operations placeholder-backed.");
        }

        productLookupComposition.Runtime.Stop();

        LocalHostOperationSurfaceReadinessReport stoppedAgainReport = inspector.Inspect(
            productLookupComposition,
            operationCatalog,
            contractMap);

        if (!stoppedAgainReport.IsReady)
        {
            throw new InvalidOperationException("ProductLookup composition readiness seal should pass again after runtime stop.");
        }

        LocalHostBootstrapCompositionResult defaultComposition = composer.Compose(
            profile,
            operationCatalog,
            contractMap);

        LocalHostOperationSurfaceReadinessReport defaultReadyReport = inspector.Inspect(
            defaultComposition,
            operationCatalog,
            contractMap);

        if (!defaultReadyReport.IsReady)
        {
            throw new InvalidOperationException("Default placeholder composition should remain readiness-sealable.");
        }

        defaultComposition.Runtime.Start(DateTimeOffset.UtcNow);

        HostResponseEnvelope<LocalHostProductLookupResponse> defaultLookupResponse = await defaultComposition.Runtime.DispatchAsync<LocalHostProductLookupRequest, LocalHostProductLookupResponse>(
            CreateProductLookupRequest(profile.Store.TenantId, profile.Store.StoreId, "5555555555"));

        if (defaultLookupResponse.IsSuccess
            || defaultLookupResponse.Error?.Code != LocalHostHandlerBootstrapErrorCodes.HandlerNotImplemented)
        {
            throw new InvalidOperationException("Default composition should still keep ProductLookup placeholder-backed after 64D.");
        }

        if (products.FindByBarcodeCallCount != 1)
        {
            throw new InvalidOperationException("Default composition should not touch the ProductLookup repository dependency.");
        }
    }

    // Kirch: Creates a valid local host profile for ProductLookup composition readiness seal checks.
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

    // Kirch: Creates a ProductLookup request for readiness seal dispatch checks.
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

    // Kirch: Creates a ProductSearch request to prove non-ProductLookup operations remain placeholder-backed.
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

    // Kirch: Creates valid Host request context for readiness seal checks.
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

    // Kirch: Fake Core repository lets readiness seal prove ProductLookup composition without implementation coupling.
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
                Sku: "SKU-SEALED",
                Barcode: barcode.Value,
                Name: "Sealed Product",
                UnitName: "piece",
                SellingPrice: Money.FromPesos(45m),
                CostPrice: Money.FromPesos(28m),
                StockOnHand: Quantity.FromUnits(10m).Value,
                TrackStock: true,
                AllowDiscount: true,
                TaxClassification: ProductTaxClassification.Vatable,
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
