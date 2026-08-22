using Salryn.Core.Common;
using Salryn.Core.Contracts;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.ValueObjects;
using Salryn.Host.Local.Contracts;
using Salryn.Host.Local.Handlers.Products;

namespace Salryn.Tests;

// Kirch: Verifies Phase 64A first real Local Host ProductLookup handler without SQLite, WinForms, HTTP, or sockets.
public static class Phase64ALocalHostProductLookupHandlerCompileChecks
{
    // Kirch: Proves ProductLookup can be handled through Core repository contracts only.
    public static async Task RunAsync()
    {
        LocalHostOperationContractDescriptor? productLookupContract = LocalHostOperationContractMap
            .CreateDefault()
            .Find(HostOperationNames.ProductLookup);

        if (!LocalHostOperationContractRules.HandlerMatchesContract<LocalHostProductLookupRequest, LocalHostProductLookupResponse>(productLookupContract))
        {
            throw new InvalidOperationException("ProductLookup handler must match the Local Host ProductLookup contract.");
        }

        FakeProductReadRepository repository = new();
        LocalHostProductLookupHandler handler = new(repository);

        HostRequestEnvelope<LocalHostProductLookupRequest> successRequest = CreateRequest("1234567890");
        HostResponseEnvelope<LocalHostProductLookupResponse> successResponse = await handler.HandleAsync(successRequest);

        if (!successResponse.IsSuccess
            || successResponse.Payload is null
            || successResponse.Payload.DisplayName != "Sample Product")
        {
            throw new InvalidOperationException("ProductLookup handler should return the product display name.");
        }

        if (repository.FindByBarcodeCallCount != 1
            || repository.LastBarcodeValue != "1234567890")
        {
            throw new InvalidOperationException("ProductLookup handler should query the repository by normalized barcode.");
        }

        HostResponseEnvelope<LocalHostProductLookupResponse> invalidResponse = await handler.HandleAsync(CreateRequest("   "));

        if (invalidResponse.IsSuccess
            || invalidResponse.Error?.Code != LocalHostProductLookupHandlerErrorCodes.InvalidLookupCode)
        {
            throw new InvalidOperationException("ProductLookup handler should reject blank lookup codes.");
        }

        repository.ReturnMissing = true;
        HostResponseEnvelope<LocalHostProductLookupResponse> missingResponse = await handler.HandleAsync(CreateRequest("0000000000"));

        if (missingResponse.IsSuccess
            || missingResponse.Error?.Code != LocalHostProductLookupHandlerErrorCodes.ProductNotFound)
        {
            throw new InvalidOperationException("ProductLookup handler should return not found for missing products.");
        }

        repository.ReturnMissing = false;
        repository.ForceFailure = true;
        HostResponseEnvelope<LocalHostProductLookupResponse> failureResponse = await handler.HandleAsync(CreateRequest("9999999999"));

        if (failureResponse.IsSuccess
            || failureResponse.Error?.Code != LocalHostProductLookupHandlerErrorCodes.RepositoryFailure)
        {
            throw new InvalidOperationException("ProductLookup handler should wrap repository failures.");
        }
    }

    // Kirch: Creates a valid ProductLookup Host request envelope for direct handler smoke checks.
    private static HostRequestEnvelope<LocalHostProductLookupRequest> CreateRequest(string lookupCode)
    {
        return new HostRequestEnvelope<LocalHostProductLookupRequest>(
            RequestId: Guid.NewGuid(),
            OperationKind: HostOperationKind.Query,
            OperationName: HostOperationNames.ProductLookup,
            Context: new HostRequestContext(
                TenantId: Guid.NewGuid(),
                StoreId: Guid.NewGuid(),
                UserId: null,
                RoleId: null,
                CashierSessionId: null,
                TerminalId: null,
                DeviceKey: null,
                RequestedAt: DateTimeOffset.UtcNow),
            Payload: new LocalHostProductLookupRequest(
                BarcodeOrSku: lookupCode));
    }

    // Kirch: Fake Core product read repository keeps Phase 64A independent from SQLite infrastructure.
    private sealed class FakeProductReadRepository : IProductReadRepository
    {
        public int FindByBarcodeCallCount { get; private set; }

        public string? LastBarcodeValue { get; private set; }

        public bool ReturnMissing { get; set; }

        public bool ForceFailure { get; set; }

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

            if (ForceFailure)
            {
                return Task.FromResult(Result<ProductLookupReadModel?>.Fail(
                    Error.Failure("FakeProductReadRepository.Failure", "Forced repository failure.")));
            }

            if (ReturnMissing)
            {
                return Task.FromResult(Result<ProductLookupReadModel?>.Success(null));
            }

            ProductLookupReadModel product = new(
                Id: Guid.NewGuid(),
                Sku: "SKU-001",
                Barcode: barcode.Value,
                Name: "Sample Product",
                UnitName: "piece",
                SellingPrice: Money.FromPesos(25m),
                CostPrice: Money.FromPesos(15m),
                StockOnHand: Quantity.FromUnits(12m).Value,
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
