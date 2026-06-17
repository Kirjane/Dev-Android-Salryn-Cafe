using Salryn.Core.Common;
using Salryn.Core.Contracts;
using Salryn.Core.Hosting;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.ValueObjects;
using Salryn.Host.Local.Contracts;
using Salryn.Host.Local.Dispatch;

namespace Salryn.Host.Local.Handlers.Products;

// Kirch: Defines ProductLookup handler error codes before transport or terminal clients exist.
public static class LocalHostProductLookupHandlerErrorCodes
{
    public const string InvalidLookupCode = "LocalHost.Products.Lookup.InvalidCode";
    public const string ProductNotFound = "LocalHost.Products.Lookup.NotFound";
    public const string RepositoryFailure = "LocalHost.Products.Lookup.RepositoryFailure";
}

// Kirch: Handles the first real Local Host ProductLookup slice through the Core product read repository only.
public sealed class LocalHostProductLookupHandler : IHostOperationHandler<LocalHostProductLookupRequest, LocalHostProductLookupResponse>
{
    private readonly IProductReadRepository _products;

    // Kirch: Keeps Host.Local independent from persistence implementation by depending only on the Core repository contract.
    public LocalHostProductLookupHandler(IProductReadRepository products)
    {
        _products = products ?? throw new ArgumentNullException(nameof(products));
    }

    // Kirch: Resolves ProductLookup by barcode through the Core repository contract only.
    public async Task<HostResponseEnvelope<LocalHostProductLookupResponse>> HandleAsync(
        HostRequestEnvelope<LocalHostProductLookupRequest> request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return LocalHostDispatchResponses.Failure<LocalHostProductLookupResponse>(
                requestId: Guid.Empty,
                code: LocalHostDispatchErrorCodes.RequestEnvelopeMissing,
                message: "Local Host product lookup request envelope must be provided.",
                type: ErrorType.Validation);
        }

        Result<Barcode> barcodeResult = Barcode.Create(request.Payload.BarcodeOrSku);

        if (barcodeResult.IsFailure)
        {
            return LocalHostDispatchResponses.Failure<LocalHostProductLookupResponse>(
                requestId: request.RequestId,
                code: LocalHostProductLookupHandlerErrorCodes.InvalidLookupCode,
                message: barcodeResult.Error,
                type: ErrorType.Validation);
        }

        Result<ProductLookupReadModel?> productResult = await _products.FindByBarcodeAsync(
            barcodeResult.Value,
            cancellationToken);

        if (productResult.IsFailure)
        {
            return LocalHostDispatchResponses.Failure<LocalHostProductLookupResponse>(
                requestId: request.RequestId,
                code: LocalHostProductLookupHandlerErrorCodes.RepositoryFailure,
                message: productResult.Error,
                type: productResult.ErrorDetail.Type);
        }

        ProductLookupReadModel? product = productResult.Value;

        if (product is null)
        {
            return LocalHostDispatchResponses.Failure<LocalHostProductLookupResponse>(
                requestId: request.RequestId,
                code: LocalHostProductLookupHandlerErrorCodes.ProductNotFound,
                message: "Product was not found for the provided lookup code.",
                type: ErrorType.NotFound);
        }

        return new HostResponseEnvelope<LocalHostProductLookupResponse>(
            RequestId: request.RequestId,
            IsSuccess: true,
            Payload: new LocalHostProductLookupResponse(
                ProductId: product.Id,
                Sku: product.Sku,
                Barcode: product.Barcode,
                DisplayName: product.Name,
                UnitName: product.UnitName,
                SellingPriceCents: product.SellingPrice.Cents,
                CostPriceCents: product.CostPrice?.Cents,
                StockOnHandX1000: product.StockOnHand.ValueX1000,
                TrackStock: product.TrackStock,
                AllowDiscount: product.AllowDiscount,
                IsActive: product.IsActive),
            Error: null);
    }
}

