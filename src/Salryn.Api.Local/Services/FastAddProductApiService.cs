using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Contracts;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data.Repositories.Products;

namespace Salryn.Api.Local.Services;

public sealed class FastAddProductApiService
{
    private readonly SalrynLocalComposition _composition;
    private readonly SqliteFastAddProductWriteRepository _fastAddWriter;

    public FastAddProductApiService(SalrynLocalComposition composition)
    {
        _composition = composition ?? throw new ArgumentNullException(nameof(composition));
        _fastAddWriter = new SqliteFastAddProductWriteRepository(_composition.ConnectionFactory);
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

        // Resolve display metadata BEFORE the atomic write. A post-commit catalog
        // or product readback must never turn a committed stock change into a false failure.
        Result<IReadOnlyList<UnitReadModel>> unitsResult = await _composition.CatalogReadRepository
            .GetUnitsAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (unitsResult.IsFailure)
        {
            return Result<FastAddProductResponse>.Fail(unitsResult.ErrorDetail);
        }

        UnitReadModel? unit = unitsResult.Value.FirstOrDefault(value => value.Id == input.UnitId);
        if (unit is null)
        {
            return Result<FastAddProductResponse>.Fail(Error.Validation(
                "FastAdd.UnitUnavailable",
                "The selected unit is unavailable. Refresh the unit list before submitting."));
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

        // One repository transaction owns product, MAIN-STORE bucket, stock-in and creation audit.
        // A failed stock or audit stage rolls back the product, including for zero-stock creates.
        Result writeResult = await _fastAddWriter
            .CreateAsync(product, input.InitialStockQuantity, session.UserId, cancellationToken)
            .ConfigureAwait(false);
        if (writeResult.IsFailure)
        {
            return Result<FastAddProductResponse>.Fail(writeResult.ErrorDetail);
        }

        bool stockMovementCreated = input.InitialStockQuantity.ValueX1000 > 0;

        // The atomic repository has committed before returning Success. Build the cart
        // response from those exact committed values and unit data validated above,
        // without another fallible read after commit.
        ProductListItemResponse productResponse = new(
            productId,
            product.Sku,
            product.Barcode.Value.Value,
            product.Name,
            unit.Name,
            null,
            null,
            product.SellingPrice.ToPesos(),
            null,
            input.InitialStockQuantity.ToUnits(),
            product.ReorderLevel.ToUnits(),
            product.TrackStock,
            product.IsActive);

        string message = stockMovementCreated
            ? "Product created, initial stock recorded, and product is ready for cart add."
            : "Product created with zero initial stock. Add stock before checkout if stock tracking blocks sale.";

        return Result<FastAddProductResponse>.Success(new FastAddProductResponse(
            "POS_M4_FAST_ADD_CREATED",
            message,
            productResponse,
            stockMovementCreated));
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

        if (request.SellingPrice <= 0m ||
            request.SellingPrice > long.MaxValue / 100m ||
            request.SellingPrice * 100m != decimal.Truncate(request.SellingPrice * 100m))
        {
            return Result<FastAddInput>.Fail(Error.Validation(
                "FastAdd.SellingPriceRequired",
                "Selling price must be positive and contain no fractional cent."));
        }

        if (request.InitialStockQuantity < 0m ||
            request.InitialStockQuantity > long.MaxValue / 1000m ||
            request.InitialStockQuantity * 1000m != decimal.Truncate(request.InitialStockQuantity * 1000m))
        {
            return Result<FastAddInput>.Fail(Error.Validation(
                "FastAdd.InitialStockQuantityInvalid",
                "Initial stock must be nonnegative with at most three decimal places."));
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
