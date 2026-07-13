using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Contracts;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.ValueObjects;

namespace Salryn.Api.Local.Services;

public sealed class InventoryManagementApiService
{
    private const string StockInReferenceType = "POS_M6_STOCK_IN";
    private const string AdjustmentReferenceType = "POS_M6_STOCK_ADJUSTMENT";

    private readonly SalrynLocalComposition _composition;

    public InventoryManagementApiService(SalrynLocalComposition composition)
    {
        _composition = composition ?? throw new ArgumentNullException(nameof(composition));
    }

    public async Task<Result<StockWriteResponse>> StockInAsync(
        StockInRequest request,
        LocalApiSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        Result<InventoryWriteInput> inputResult = await ValidateInputAsync(
                request.ProductId,
                request.Quantity,
                request.Reason,
                requireReason: true,
                cancellationToken)
            .ConfigureAwait(false);
        if (inputResult.IsFailure)
        {
            return Result<StockWriteResponse>.Fail(inputResult.ErrorDetail);
        }

        InventoryWriteInput input = inputResult.Value;
        StockMovement movement = new()
        {
            Id = Guid.NewGuid(),
            ProductId = input.Product.Id,
            MovementType = StockMovementType.StockIn,
            QuantityChange = input.Quantity,
            CostPrice = null,
            ReferenceType = StockInReferenceType,
            ReferenceId = null,
            Reason = BuildReason(input.Reason, request.Reference),
            UserId = session.UserId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        Result result = await _composition.InventoryWriteRepository
            .AddStockMovementAsync(movement, cancellationToken)
            .ConfigureAwait(false);
        if (result.IsFailure)
        {
            return Result<StockWriteResponse>.Fail(result.ErrorDetail);
        }

        return await ReadStockWriteResponseAsync(
                input.Product.Id,
                "POS_M6_STOCK_IN_RECORDED",
                "Stock-In recorded. No sale, payment, or receipt was created.",
                movement.MovementType.ToString(),
                input.Quantity.ToUnits(),
                StockInReferenceType,
                request.Reference,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<StockWriteResponse>> AdjustAsync(
        StockAdjustmentRequest request,
        LocalApiSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        string direction = (request.Direction ?? string.Empty).Trim();
        StockMovementType movementType;
        if (string.Equals(direction, "add", StringComparison.OrdinalIgnoreCase))
        {
            movementType = StockMovementType.AdjustmentAdd;
        }
        else if (string.Equals(direction, "remove", StringComparison.OrdinalIgnoreCase))
        {
            movementType = StockMovementType.AdjustmentRemove;
        }
        else
        {
            return Result<StockWriteResponse>.Fail(Error.Validation(
                "Inventory.AdjustmentDirectionInvalid",
                "Adjustment direction must be add or remove."));
        }

        Result<InventoryWriteInput> inputResult = await ValidateInputAsync(
                request.ProductId,
                request.Quantity,
                request.Reason,
                requireReason: true,
                cancellationToken)
            .ConfigureAwait(false);
        if (inputResult.IsFailure)
        {
            return Result<StockWriteResponse>.Fail(inputResult.ErrorDetail);
        }

        InventoryWriteInput input = inputResult.Value;
        if (movementType == StockMovementType.AdjustmentRemove && input.Quantity > input.Product.StockOnHand)
        {
            return Result<StockWriteResponse>.Fail(Error.Conflict(
                "Inventory.NegativeStockNotAllowed",
                "Stock adjustment would make product stock negative."));
        }

        StockMovement movement = new()
        {
            Id = Guid.NewGuid(),
            ProductId = input.Product.Id,
            MovementType = movementType,
            QuantityChange = input.Quantity,
            CostPrice = null,
            ReferenceType = AdjustmentReferenceType,
            ReferenceId = null,
            Reason = BuildReason(input.Reason, request.Reference),
            UserId = session.UserId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        Result result = await _composition.InventoryWriteRepository
            .AddStockMovementAsync(movement, cancellationToken)
            .ConfigureAwait(false);
        if (result.IsFailure)
        {
            return Result<StockWriteResponse>.Fail(result.ErrorDetail);
        }

        decimal signedQuantity = movementType == StockMovementType.AdjustmentRemove
            ? -input.Quantity.ToUnits()
            : input.Quantity.ToUnits();

        return await ReadStockWriteResponseAsync(
                input.Product.Id,
                "POS_M6_STOCK_ADJUSTMENT_RECORDED",
                "Stock adjustment recorded. No sale, payment, or receipt was created.",
                movement.MovementType.ToString(),
                signedQuantity,
                AdjustmentReferenceType,
                request.Reference,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<InventoryWriteInput>> ValidateInputAsync(
        Guid productId,
        decimal quantity,
        string? reason,
        bool requireReason,
        CancellationToken cancellationToken)
    {
        if (productId == Guid.Empty)
        {
            return Result<InventoryWriteInput>.Fail(Error.Validation(
                "Inventory.ProductRequired",
                "Product is required."));
        }

        if (quantity <= 0m)
        {
            return Result<InventoryWriteInput>.Fail(Error.Validation(
                "Inventory.QuantityRequired",
                "Quantity must be greater than zero."));
        }

        string normalizedReason = (reason ?? string.Empty).Trim();
        if (requireReason && string.IsNullOrWhiteSpace(normalizedReason))
        {
            return Result<InventoryWriteInput>.Fail(Error.Validation(
                "Inventory.ReasonRequired",
                "Reason is required for stock writes."));
        }

        Result<Quantity> quantityResult = Quantity.FromUnits(quantity);
        if (quantityResult.IsFailure)
        {
            return Result<InventoryWriteInput>.Fail(Error.Validation(
                "Inventory.QuantityInvalid",
                quantityResult.Error));
        }

        Result<ProductDetailReadModel?> productResult = await _composition.ProductReadRepository
            .FindDetailByIdAsync(productId, cancellationToken)
            .ConfigureAwait(false);
        if (productResult.IsFailure)
        {
            return Result<InventoryWriteInput>.Fail(productResult.ErrorDetail);
        }

        if (productResult.Value is null)
        {
            return Result<InventoryWriteInput>.Fail(Error.NotFound(
                "Inventory.ProductNotFoundOrInactive",
                "Product was not found or is inactive."));
        }

        ProductDetailReadModel product = productResult.Value;
        if (!product.IsActive)
        {
            return Result<InventoryWriteInput>.Fail(Error.NotFound(
                "Inventory.ProductNotFoundOrInactive",
                "Product was not found or is inactive."));
        }

        if (!product.TrackStock)
        {
            return Result<InventoryWriteInput>.Fail(Error.Validation(
                "Inventory.ProductNotStockTracked",
                "Product is not configured for stock tracking."));
        }

        return Result<InventoryWriteInput>.Success(new InventoryWriteInput(
            product,
            quantityResult.Value,
            normalizedReason));
    }

    private async Task<Result<StockWriteResponse>> ReadStockWriteResponseAsync(
        Guid productId,
        string code,
        string message,
        string movementType,
        decimal quantityChange,
        string referenceType,
        string? reference,
        CancellationToken cancellationToken)
    {
        Result<ProductDetailReadModel?> detailResult = await _composition.ProductReadRepository
            .FindDetailByIdAsync(productId, cancellationToken)
            .ConfigureAwait(false);
        if (detailResult.IsFailure)
        {
            return Result<StockWriteResponse>.Fail(detailResult.ErrorDetail);
        }

        if (detailResult.Value is null)
        {
            return Result<StockWriteResponse>.Fail(Error.Unexpected(
                "Inventory.ProductReadbackMissing",
                "Stock write succeeded but product readback failed."));
        }

        return Result<StockWriteResponse>.Success(new StockWriteResponse(
            code,
            message,
            ReadModelMapper.ToProductListItem(detailResult.Value),
            movementType,
            quantityChange,
            referenceType,
            NormalizeReference(reference)));
    }

    private static string BuildReason(string reason, string? reference)
    {
        string normalizedReference = NormalizeReference(reference) ?? string.Empty;
        return string.IsNullOrWhiteSpace(normalizedReference)
            ? reason.Trim()
            : $"{reason.Trim()} Reference: {normalizedReference}";
    }

    private static string? NormalizeReference(string? reference)
    {
        return string.IsNullOrWhiteSpace(reference)
            ? null
            : reference.Trim();
    }

    private sealed record InventoryWriteInput(
        ProductDetailReadModel Product,
        Quantity Quantity,
        string Reason);
}
