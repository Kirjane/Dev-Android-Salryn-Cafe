using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Contracts;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.ValueObjects;

namespace Salryn.Api.Local.Services;

public sealed class FastAddProductApiService
{
    private const string ReferenceType = "POS_M4_FAST_ADD";

    private readonly SalrynLocalComposition _composition;

    public FastAddProductApiService(SalrynLocalComposition composition)
    {
        _composition = composition ?? throw new ArgumentNullException(nameof(composition));
    }

    public async Task<Result<FastAddProductResponse>> FastAddAsync(
        FastAddProductRequest request,
        LocalApiSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        Result<FastAddInput> inputResult = ValidateRequest(request);
        if (inputResult.IsFailure)
        {
            return Result<FastAddProductResponse>.Fail(inputResult.ErrorDetail);
        }

        FastAddInput input = inputResult.Value;

        Result duplicateCheck = await EnsureBarcodeIsNotActiveDuplicateAsync(input.Barcode, cancellationToken)
            .ConfigureAwait(false);
        if (duplicateCheck.IsFailure)
        {
            return Result<FastAddProductResponse>.Fail(duplicateCheck.ErrorDetail);
        }

        Guid productId = Guid.NewGuid();
        Product product = new()
        {
            Id = productId,
            Sku = null,
            Barcode = input.Barcode,
            Name = input.ProductName,
            Description = "Created by POS-M4 Fast Add from checkout unknown barcode.",
            CategoryId = null,
            BrandId = null,
            UnitId = input.UnitId,
            SupplierId = null,
            CostPrice = null,
            SellingPrice = input.SellingPrice,
            WholesalePrice = null,
            StockOnHand = Quantity.Zero,
            ReorderLevel = Quantity.Zero,
            TrackStock = true,
            AllowDiscount = true,
            TaxClassification = ProductTaxClassification.Vatable,
            IsActive = true,
            Notes = "POS-M4 Fast Add; stock still reduces only through checkout.",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = null
        };

        Result addProductResult = await _composition.ProductWriteRepository
            .AddAsync(product, cancellationToken)
            .ConfigureAwait(false);
        if (addProductResult.IsFailure)
        {
            return Result<FastAddProductResponse>.Fail(addProductResult.ErrorDetail);
        }

        bool stockMovementCreated = false;
        if (input.InitialStockQuantity.ValueX1000 > 0)
        {
            StockMovement initialStockMovement = new()
            {
                Id = Guid.NewGuid(),
                ProductId = productId,
                MovementType = StockMovementType.StockIn,
                QuantityChange = input.InitialStockQuantity,
                CostPrice = null,
                ReferenceType = ReferenceType,
                ReferenceId = productId,
                Reason = "POS-M4 Fast Add initial inventory quantity.",
                UserId = session.UserId,
                CreatedAt = DateTimeOffset.UtcNow
            };

            Result stockResult = await _composition.InventoryWriteRepository
                .AddStockMovementAsync(initialStockMovement, cancellationToken)
                .ConfigureAwait(false);
            if (stockResult.IsFailure)
            {
                return Result<FastAddProductResponse>.Fail(stockResult.ErrorDetail);
            }

            stockMovementCreated = true;
        }

        Result<ProductDetailReadModel?> detailResult = await _composition.ProductReadRepository
            .FindDetailByIdAsync(productId, cancellationToken)
            .ConfigureAwait(false);
        if (detailResult.IsFailure)
        {
            return Result<FastAddProductResponse>.Fail(detailResult.ErrorDetail);
        }

        if (detailResult.Value is null)
        {
            return Result<FastAddProductResponse>.Fail(Error.Unexpected(
                "FastAdd.ProductReadbackMissing",
                "Fast Add created the product but readback failed."));
        }

        ProductListItemResponse productResponse = ReadModelMapper.ToProductListItem(detailResult.Value);
        string message = stockMovementCreated
            ? "Product created, initial stock recorded, and product is ready for cart add."
            : "Product created with zero initial stock. Add stock before checkout if stock tracking blocks sale.";

        return Result<FastAddProductResponse>.Success(new FastAddProductResponse(
            "POS_M4_FAST_ADD_CREATED",
            message,
            productResponse,
            stockMovementCreated));
    }

    private async Task<Result> EnsureBarcodeIsNotActiveDuplicateAsync(
        Barcode barcode,
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
            string.Equals(product.Barcode, barcode.Value, StringComparison.Ordinal));

        return duplicate
            ? Result.Fail(Error.Conflict(
                "Products.DuplicateBarcode",
                "Barcode is already assigned to another active product."))
            : Result.Success();
    }

    private static Result<FastAddInput> ValidateRequest(FastAddProductRequest request)
    {
        string productName = (request.ProductName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(productName))
        {
            return Result<FastAddInput>.Fail(Error.Validation(
                "FastAdd.ProductNameRequired",
                "Product name is required."));
        }

        Result<Barcode> barcodeResult = Barcode.Create(request.Barcode);
        if (barcodeResult.IsFailure)
        {
            return Result<FastAddInput>.Fail(Error.Validation(
                "FastAdd.BarcodeRequired",
                barcodeResult.Error));
        }

        if (request.UnitId == Guid.Empty)
        {
            return Result<FastAddInput>.Fail(Error.Validation(
                "FastAdd.UnitRequired",
                "Unit is required."));
        }

        if (request.SellingPrice <= 0m)
        {
            return Result<FastAddInput>.Fail(Error.Validation(
                "FastAdd.SellingPriceRequired",
                "Selling price must be greater than zero."));
        }

        if (request.InitialStockQuantity < 0m)
        {
            return Result<FastAddInput>.Fail(Error.Validation(
                "FastAdd.InitialStockQuantityInvalid",
                "Initial stock quantity cannot be negative."));
        }

        Result<Quantity> initialStockResult = Quantity.FromUnits(request.InitialStockQuantity);
        if (initialStockResult.IsFailure)
        {
            return Result<FastAddInput>.Fail(Error.Validation(
                "FastAdd.InitialStockQuantityInvalid",
                initialStockResult.Error));
        }

        return Result<FastAddInput>.Success(new FastAddInput(
            productName,
            barcodeResult.Value,
            request.UnitId,
            Money.FromPesos(request.SellingPrice),
            initialStockResult.Value));
    }

    private sealed record FastAddInput(
        string ProductName,
        Barcode Barcode,
        Guid UnitId,
        Money SellingPrice,
        Quantity InitialStockQuantity);
}
