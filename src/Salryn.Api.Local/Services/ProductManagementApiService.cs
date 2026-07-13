using Salryn.Api.Local.Contracts;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.ValueObjects;

namespace Salryn.Api.Local.Services;

public sealed class ProductManagementApiService
{
    private readonly SalrynLocalComposition _composition;

    public ProductManagementApiService(SalrynLocalComposition composition)
    {
        _composition = composition ?? throw new ArgumentNullException(nameof(composition));
    }

    public async Task<Result<ProductManagementWriteResponse>> CreateAsync(
        ProductManagementWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        Result<ProductManagementInput> inputResult = ValidateRequest(request);
        if (inputResult.IsFailure)
        {
            return Result<ProductManagementWriteResponse>.Fail(inputResult.ErrorDetail);
        }

        ProductManagementInput input = inputResult.Value;

        Result duplicateCheck = await EnsureBarcodeIsNotActiveDuplicateAsync(
                input.Barcode,
                excludeProductId: null,
                cancellationToken)
            .ConfigureAwait(false);
        if (duplicateCheck.IsFailure)
        {
            return Result<ProductManagementWriteResponse>.Fail(duplicateCheck.ErrorDetail);
        }

        Guid productId = Guid.NewGuid();
        Product product = new()
        {
            Id = productId,
            Sku = null,
            Barcode = input.Barcode,
            Name = input.ProductName,
            Description = "Created by POS-M5 Product Catalog write management.",
            CategoryId = input.CategoryId,
            BrandId = input.BrandId,
            UnitId = input.UnitId,
            SupplierId = null,
            CostPrice = input.CostPrice,
            SellingPrice = input.SellingPrice,
            WholesalePrice = null,
            StockOnHand = Quantity.Zero,
            ReorderLevel = input.ReorderLevel,
            TrackStock = true,
            AllowDiscount = true,
            TaxClassification = ProductTaxClassification.Vatable,
            IsActive = true,
            Notes = "POS-M5 product write management. Inventory quantity changes remain parked for POS-M6.",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = null
        };

        Result addResult = await _composition.ProductWriteRepository
            .AddAsync(product, cancellationToken)
            .ConfigureAwait(false);
        if (addResult.IsFailure)
        {
            return Result<ProductManagementWriteResponse>.Fail(addResult.ErrorDetail);
        }

        return await ReadProductWriteResponseAsync(
                productId,
                "POS_M5_PRODUCT_CREATED",
                "Product created. Inventory quantity was not changed by Product Catalog write management.",
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<ProductManagementWriteResponse>> UpdateAsync(
        Guid productId,
        ProductManagementWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (productId == Guid.Empty)
        {
            return Result<ProductManagementWriteResponse>.Fail(Error.Validation(
                "Products.InvalidProductId",
                "Product ID is required."));
        }

        Result<ProductManagementInput> inputResult = ValidateRequest(request);
        if (inputResult.IsFailure)
        {
            return Result<ProductManagementWriteResponse>.Fail(inputResult.ErrorDetail);
        }

        Result<ProductDetailReadModel?> existingResult = await _composition.ProductReadRepository
            .FindDetailByIdAsync(productId, cancellationToken)
            .ConfigureAwait(false);
        if (existingResult.IsFailure)
        {
            return Result<ProductManagementWriteResponse>.Fail(existingResult.ErrorDetail);
        }

        if (existingResult.Value is null)
        {
            return Result<ProductManagementWriteResponse>.Fail(Error.NotFound(
                "Product.NotFound",
                "Product was not found."));
        }

        ProductDetailReadModel existing = existingResult.Value;
        if (!existing.IsActive)
        {
            return Result<ProductManagementWriteResponse>.Fail(Error.Validation(
                "Products.InactiveEditNotAllowed",
                "Inactive products cannot be edited in POS-M5. Reactivation is parked until a clean repository method exists."));
        }

        ProductManagementInput input = inputResult.Value;
        Result duplicateCheck = await EnsureBarcodeIsNotActiveDuplicateAsync(
                input.Barcode,
                productId,
                cancellationToken)
            .ConfigureAwait(false);
        if (duplicateCheck.IsFailure)
        {
            return Result<ProductManagementWriteResponse>.Fail(duplicateCheck.ErrorDetail);
        }

        Product product = new()
        {
            Id = productId,
            Sku = existing.Sku,
            Barcode = input.Barcode,
            Name = input.ProductName,
            Description = existing.Description,
            CategoryId = input.CategoryId,
            BrandId = input.BrandId,
            UnitId = input.UnitId,
            SupplierId = existing.SupplierId,
            CostPrice = input.CostPrice,
            SellingPrice = input.SellingPrice,
            WholesalePrice = existing.WholesalePrice,
            StockOnHand = existing.StockOnHand,
            ReorderLevel = input.ReorderLevel,
            TrackStock = existing.TrackStock,
            AllowDiscount = existing.AllowDiscount,
            TaxClassification = existing.TaxClassification,
            IsActive = true,
            Notes = existing.Notes,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        Result updateResult = await _composition.ProductWriteRepository
            .UpdateAsync(product, cancellationToken)
            .ConfigureAwait(false);
        if (updateResult.IsFailure)
        {
            return Result<ProductManagementWriteResponse>.Fail(updateResult.ErrorDetail);
        }

        return await ReadProductWriteResponseAsync(
                productId,
                "POS_M5_PRODUCT_UPDATED",
                "Product updated. Historical sale and receipt snapshots remain unchanged.",
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<ProductDeactivateResponse>> DeactivateAsync(
        Guid productId,
        ProductDeactivateRequest request,
        LocalApiSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        if (productId == Guid.Empty)
        {
            return Result<ProductDeactivateResponse>.Fail(Error.Validation(
                "Products.InvalidProductId",
                "Product ID is required."));
        }

        string reason = (request.Reason ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result<ProductDeactivateResponse>.Fail(Error.Validation(
                "Products.DeactivateReasonRequired",
                "A reason is required when deactivating a product."));
        }

        Result result = await _composition.ProductWriteRepository
            .DeactivateAsync(productId, reason, session.UserId, cancellationToken)
            .ConfigureAwait(false);
        if (result.IsFailure)
        {
            return Result<ProductDeactivateResponse>.Fail(result.ErrorDetail);
        }

        return Result<ProductDeactivateResponse>.Success(new ProductDeactivateResponse(
            "POS_M5_PRODUCT_DEACTIVATED",
            "Product deactivated. Inactive products are excluded from checkout barcode lookup.",
            productId));
    }

    private async Task<Result<ProductManagementWriteResponse>> ReadProductWriteResponseAsync(
        Guid productId,
        string code,
        string message,
        CancellationToken cancellationToken)
    {
        Result<ProductDetailReadModel?> detailResult = await _composition.ProductReadRepository
            .FindDetailByIdAsync(productId, cancellationToken)
            .ConfigureAwait(false);
        if (detailResult.IsFailure)
        {
            return Result<ProductManagementWriteResponse>.Fail(detailResult.ErrorDetail);
        }

        if (detailResult.Value is null)
        {
            return Result<ProductManagementWriteResponse>.Fail(Error.Unexpected(
                "ProductManagement.ProductReadbackMissing",
                "Product write succeeded but product readback failed."));
        }

        return Result<ProductManagementWriteResponse>.Success(new ProductManagementWriteResponse(
            code,
            message,
            ReadModelMapper.ToProductListItem(detailResult.Value)));
    }

    private async Task<Result> EnsureBarcodeIsNotActiveDuplicateAsync(
        Barcode barcode,
        Guid? excludeProductId,
        CancellationToken cancellationToken)
    {
        Result<PagedResult<ProductListItemReadModel>> searchResult = await _composition.ProductReadRepository
            .SearchAsync(
                new ProductSearchFilter(
                    barcode.Value,
                    CategoryId: null,
                    BrandId: null,
                    IsActive: true,
                    IncludeInactive: false,
                    PageRequest.FirstPage(50)),
                cancellationToken)
            .ConfigureAwait(false);

        if (searchResult.IsFailure)
        {
            return Result.Fail(searchResult.ErrorDetail);
        }

        bool duplicate = searchResult.Value.Items.Any(product =>
            product.IsActive &&
            (!excludeProductId.HasValue || product.Id != excludeProductId.Value) &&
            string.Equals(product.Barcode, barcode.Value, StringComparison.Ordinal));

        return duplicate
            ? Result.Fail(Error.Conflict(
                "Products.DuplicateBarcode",
                "An active product with the same barcode already exists."))
            : Result.Success();
    }

    private static Result<ProductManagementInput> ValidateRequest(ProductManagementWriteRequest request)
    {
        string productName = (request.ProductName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(productName))
        {
            return Result<ProductManagementInput>.Fail(Error.Validation(
                "Products.NameRequired",
                "Product name is required."));
        }

        Result<Barcode> barcodeResult = Barcode.Create(request.Barcode);
        if (barcodeResult.IsFailure)
        {
            return Result<ProductManagementInput>.Fail(Error.Validation(
                "Products.BarcodeRequired",
                barcodeResult.Error));
        }

        if (request.UnitId == Guid.Empty)
        {
            return Result<ProductManagementInput>.Fail(Error.Validation(
                "Products.UnitRequired",
                "Product unit is required."));
        }

        if (request.SellingPrice <= 0m)
        {
            return Result<ProductManagementInput>.Fail(Error.Validation(
                "Products.SellingPriceRequired",
                "Selling price must be greater than zero."));
        }

        Money? costPrice = null;
        if (request.CostPrice.HasValue)
        {
            if (request.CostPrice.Value < 0m)
            {
                return Result<ProductManagementInput>.Fail(Error.Validation(
                    "Products.CostPriceInvalid",
                    "Cost price cannot be negative."));
            }

            costPrice = Money.FromPesos(request.CostPrice.Value);
        }

        if (request.ReorderLevel < 0m)
        {
            return Result<ProductManagementInput>.Fail(Error.Validation(
                "Products.ReorderLevelInvalid",
                "Reorder level cannot be negative."));
        }

        Result<Quantity> reorderResult = Quantity.FromUnits(request.ReorderLevel);
        if (reorderResult.IsFailure)
        {
            return Result<ProductManagementInput>.Fail(Error.Validation(
                "Products.ReorderLevelInvalid",
                reorderResult.Error));
        }

        return Result<ProductManagementInput>.Success(new ProductManagementInput(
            productName,
            barcodeResult.Value,
            request.UnitId,
            NormalizeOptionalGuid(request.CategoryId),
            NormalizeOptionalGuid(request.BrandId),
            Money.FromPesos(request.SellingPrice),
            costPrice,
            reorderResult.Value));
    }

    private static Guid? NormalizeOptionalGuid(Guid? value)
    {
        return value.HasValue && value.Value != Guid.Empty ? value.Value : null;
    }

    private sealed record ProductManagementInput(
        string ProductName,
        Barcode Barcode,
        Guid UnitId,
        Guid? CategoryId,
        Guid? BrandId,
        Money SellingPrice,
        Money? CostPrice,
        Quantity ReorderLevel);
}
