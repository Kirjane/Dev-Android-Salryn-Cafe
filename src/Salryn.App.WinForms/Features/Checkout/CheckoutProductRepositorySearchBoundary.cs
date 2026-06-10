using Salryn.Core.Common;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.ValueObjects;

namespace Salryn.App.WinForms.Features.Checkout;

public sealed class CheckoutProductRepositorySearchBoundary : ICheckoutProductSearchBoundary
{
    private readonly IProductReadRepository _products;

    public CheckoutProductRepositorySearchBoundary(IProductReadRepository products)
    {
        _products = products ?? throw new ArgumentNullException(nameof(products));
    }

    public async Task<Result<ProductLookupReadModel?>> FindProductAsync(
        string searchText,
        CancellationToken cancellationToken = default)
    {
        string trimmed = searchText.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Result<ProductLookupReadModel?>.Success(null);
        }

        Result<Barcode> barcodeResult = Barcode.Create(trimmed);

        if (barcodeResult.IsSuccess)
        {
            Result<ProductLookupReadModel?> barcodeLookup =
                await _products.FindByBarcodeAsync(
                    barcodeResult.Value,
                    cancellationToken).ConfigureAwait(false);

            if (barcodeLookup.IsFailure)
            {
                return Result<ProductLookupReadModel?>.Fail(barcodeLookup.ErrorDetail);
            }

            if (barcodeLookup.Value is not null)
            {
                return barcodeLookup;
            }
        }

        ProductSearchFilter filter = new(
            SearchText: trimmed,
            CategoryId: null,
            BrandId: null,
            IsActive: true,
            IncludeInactive: false,
            Page: new PageRequest(
                PageNumber: 1,
                PageSize: 10));

        Result<PagedResult<ProductListItemReadModel>> searchResult =
            await _products.SearchAsync(
                filter,
                cancellationToken).ConfigureAwait(false);

        if (searchResult.IsFailure)
        {
            return Result<ProductLookupReadModel?>.Fail(searchResult.ErrorDetail);
        }

        ProductListItemReadModel? firstMatch = searchResult.Value.Items.FirstOrDefault();

        if (firstMatch is null)
        {
            return Result<ProductLookupReadModel?>.Success(null);
        }

        Result<ProductLookupReadModel?> lookupResult =
            await _products.FindByIdAsync(
                firstMatch.Id,
                cancellationToken).ConfigureAwait(false);

        if (lookupResult.IsFailure)
        {
            return Result<ProductLookupReadModel?>.Fail(lookupResult.ErrorDetail);
        }

        return lookupResult;
    }
}
