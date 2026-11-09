using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Contracts;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;

namespace Salryn.Api.Local.Services;

public sealed class SaleVoidApiService
{
    private const int MaxSaleMovements = 1000;
    private readonly SalrynLocalComposition _composition;

    public SaleVoidApiService(SalrynLocalComposition composition)
    {
        _composition = composition ?? throw new ArgumentNullException(nameof(composition));
    }

    public async Task<Result<SaleVoidResponse>> VoidAsync(
        Guid saleId,
        SaleVoidRequest request,
        LocalApiSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(session);

        string reason = (request.Reason ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result<SaleVoidResponse>.Fail(Error.Validation(
                "Sales.VoidReasonRequired", "Void reason is required."));
        }

        Result<SaleDetailReadModel?> saleResult = await _composition.SaleReadRepository
            .FindSaleByIdAsync(saleId, cancellationToken).ConfigureAwait(false);
        if (saleResult.IsFailure)
        {
            return Result<SaleVoidResponse>.Fail(saleResult.ErrorDetail);
        }

        SaleDetailReadModel? sale = saleResult.Value;
        if (sale is null)
        {
            return Result<SaleVoidResponse>.Fail(Error.NotFound("Sales.NotFound", "Sale was not found."));
        }

        if (sale.Status != SaleStatus.Completed)
        {
            return Result<SaleVoidResponse>.Fail(Error.Conflict(
                "Sales.NotVoidable", "Only a completed sale can be voided."));
        }

        Result<PagedResult<StockMovementReadModel>> movementResult = await _composition.InventoryReadRepository
            .SearchStockMovementsAsync(new StockMovementFilter(
                ProductId: null,
                MovementType: StockMovementType.Sale,
                From: null,
                To: null,
                Page: PageRequest.FirstPage(MaxSaleMovements),
                ReferenceType: "SALE",
                ReferenceId: sale.Id.ToString("D")), cancellationToken)
            .ConfigureAwait(false);
        if (movementResult.IsFailure)
        {
            return Result<SaleVoidResponse>.Fail(movementResult.ErrorDetail);
        }

        if (movementResult.Value.TotalCount > movementResult.Value.Items.Count)
        {
            return Result<SaleVoidResponse>.Fail(Error.Failure(
                "Sales.VoidMovementLimitExceeded", "Sale has more stock movements than can be safely reversed."));
        }

        List<StockMovement> reversals = new();
        foreach (StockMovementReadModel movement in movementResult.Value.Items.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id))
        {
            Result<Quantity> quantity = Quantity.FromX1000(Math.Abs(movement.QuantityChangeX1000));
            if (quantity.IsFailure || quantity.Value == Quantity.Zero)
            {
                return Result<SaleVoidResponse>.Fail(Error.Validation(
                    "Sales.InvalidVoidMovement", "Recorded sale stock movement cannot be reversed."));
            }

            reversals.Add(new StockMovement
            {
                Id = Guid.NewGuid(),
                ProductId = movement.ProductId,
                MovementType = StockMovementType.VoidSale,
                QuantityChange = quantity.Value,
                CostPrice = movement.CostPrice,
                ReferenceType = "SALE",
                ReferenceId = sale.Id,
                Reason = reason,
                UserId = session.UserId,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        Result voidResult = await _composition.SaleWriteRepository
            .MarkVoidedAsync(sale.Id, session.UserId, reason, reversals, cancellationToken)
            .ConfigureAwait(false);
        if (voidResult.IsFailure)
        {
            return Result<SaleVoidResponse>.Fail(voidResult.ErrorDetail);
        }

        Result<SaleDetailReadModel?> updatedResult = await _composition.SaleReadRepository
            .FindSaleByIdAsync(sale.Id, cancellationToken).ConfigureAwait(false);
        if (updatedResult.IsFailure || updatedResult.Value is null)
        {
            return Result<SaleVoidResponse>.Fail(updatedResult.IsFailure
                ? updatedResult.ErrorDetail
                : Error.Unexpected("Sales.VoidReadbackMissing", "Voided sale readback failed."));
        }

        await OperatorAuditTrail.TryWriteAsync(
                _composition.ConnectionFactory,
                session.UserId,
                OperatorAuditActions.SaleVoided,
                "Sale",
                sale.Id.ToString("D"),
                new
                {
                    saleId = sale.Id.ToString("D"),
                    sale.ReceiptNo,
                    status = SaleStatus.Voided.ToString(),
                    reversalMovementCount = reversals.Count
                },
                oldValue: new { status = SaleStatus.Completed.ToString() },
                reason: reason,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return Result<SaleVoidResponse>.Success(new SaleVoidResponse(
            "POS_M7_SALE_VOIDED",
            "Sale voided. Recorded stock movements were reversed; payment and receipt snapshot were preserved.",
            ReadModelMapper.ToSaleDetail(updatedResult.Value),
            reversals.Count));
    }
}
