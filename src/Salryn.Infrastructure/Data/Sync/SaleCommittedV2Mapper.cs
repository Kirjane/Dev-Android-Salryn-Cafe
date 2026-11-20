using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

public static class SaleCommittedV2Mapper
{
    public static SaleCommittedV2 FromCore(
        CompleteCheckoutSaleRequest request,
        CafeSaleFinancialDecision decision)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(decision);

        SaleCommittedV1 legacy = SaleCommittedV1Mapper.FromCore(request);

        return new SaleCommittedV2(
            legacy,
            decision.OperationId,
            decision.FinancialDecisionId,
            decision.OrderId,
            decision.OrderRevisionId,
            decision.ExpectedOrderVersion,
            decision.CreatedAtUtc,
            decision.ContentHash,
            decision.DiscountReason,
            decision.FeeTotalCents,
            decision.LineDecisions.Select(line =>
                new SaleCommittedV2LineDiscount(
                    line.SaleItemId,
                    line.LineDiscountCents,
                    line.OrderDiscountAllocatedCents,
                    line.TotalDiscountCents)).ToArray(),
            decision.Fees.Select(fee =>
                new SaleCommittedV2Fee(
                    fee.FeeId,
                    fee.FeeType,
                    fee.DisplayLabelSnapshot,
                    fee.AmountCents,
                    fee.ActorUserId,
                    fee.Reason,
                    fee.CreatedAtUtc)).ToArray());
    }


    public static Result<CafeSaleFinancialDecision> ToFinancialDecision(
        SaleCommittedV2 payload,
        Guid storeId)
    {
        SaleCommittedV2ValidationResult validation =
            SaleCommittedV2Validator.Validate(payload);
        if (!validation.IsValid)
        {
            return Result<CafeSaleFinancialDecision>.Fail(
                Error.Validation(
                    validation.ErrorCode ?? "SaleCommittedV2.Invalid",
                    validation.ErrorMessage ?? "SaleCommittedV2 payload is invalid."));
        }

        if (storeId == Guid.Empty)
        {
            return Result<CafeSaleFinancialDecision>.Fail(
                Error.Validation(
                    "SaleCommittedV2.StoreRequired",
                    "Financial decision reconstruction requires the authoritative store ID."));
        }

        try
        {
            SaleCommittedV1Sale sale = payload.SaleEvidence.Sale;
            Dictionary<Guid, SaleCommittedV1Item> items =
                payload.SaleEvidence.Items.ToDictionary(x => x.Id);

            CafeSaleFinancialLineDecision[] lines =
                payload.LineDiscounts
                    .Select(x => new CafeSaleFinancialLineDecision(
                        x.SaleItemId,
                        x.LineDiscountCents,
                        x.OrderDiscountAllocatedCents))
                    .ToArray();

            CafeSaleFeeDecision[] fees =
                payload.Fees
                    .Select(x => new CafeSaleFeeDecision(
                        x.FeeId,
                        x.FeeType,
                        x.DisplayLabelSnapshot,
                        x.AmountCents,
                        x.ActorUserId,
                        x.Reason,
                        x.CreatedAtUtc))
                    .ToArray();

            long lineDiscountTotal =
                lines.Aggregate(
                    0L,
                    (sum, line) =>
                        checked(sum + line.LineDiscountCents));
            long orderDiscountTotal =
                lines.Aggregate(
                    0L,
                    (sum, line) =>
                        checked(sum + line.OrderDiscountAllocatedCents));

            long cashTender = 0;
            long arTender = 0;
            foreach (SaleCommittedV1Payment payment in payload.SaleEvidence.Payments)
            {
                if (string.Equals(
                        payment.Method,
                        SaleCommittedV1Contract.PaymentCash,
                        StringComparison.Ordinal))
                    cashTender = checked(cashTender + payment.AmountCents);
                else
                    arTender = checked(arTender + payment.AmountCents);
            }

            CafeSaleFinancialDecision decision = new(
                payload.FinancialDecisionId,
                payload.FinancialOperationId,
                storeId,
                payload.OrderId,
                payload.OrderRevisionId,
                payload.ExpectedOrderVersion,
                sale.Id,
                sale.CashierSessionId,
                sale.CashierUserId,
                sale.SubtotalCents,
                lineDiscountTotal,
                orderDiscountTotal,
                payload.FeeTotalCents,
                sale.TaxCents,
                sale.TotalCents,
                cashTender,
                arTender,
                sale.AmountPaidCents,
                sale.BalanceDueCents,
                sale.ChangeCents,
                payload.DiscountReason,
                payload.FinancialDecisionHash,
                payload.FinancialDecisionCreatedAtUtc,
                lines,
                fees);

            string recomputed =
                CafeSaleFinancialCalculator.ComputeContentHash(decision);
            if (!string.Equals(
                    recomputed,
                    payload.FinancialDecisionHash,
                    StringComparison.Ordinal))
            {
                return Result<CafeSaleFinancialDecision>.Fail(
                    Error.Conflict(
                        "SaleCommittedV2.FinancialDecisionHashMismatch",
                        "SaleCommittedV2 financial evidence does not reproduce its immutable decision hash."));
            }

            return Result<CafeSaleFinancialDecision>.Success(decision);
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or ArgumentException or OverflowException)
        {
            return Result<CafeSaleFinancialDecision>.Fail(
                Error.Validation(
                    "SaleCommittedV2.FinancialDecisionMappingFailed",
                    ex.Message));
        }
    }

    public static Result<CompleteCheckoutSaleRequest> ToCore(SaleCommittedV2 payload)
    {
        SaleCommittedV2ValidationResult validation = SaleCommittedV2Validator.Validate(payload);
        if (!validation.IsValid)
            return Result<CompleteCheckoutSaleRequest>.Fail(Error.Validation(
                validation.ErrorCode ?? "SaleCommittedV2.Invalid",
                validation.ErrorMessage ?? "SaleCommittedV2 payload is invalid."));

        try
        {
            SaleCommittedV1 legacy = payload.SaleEvidence;
            List<SaleItem> items = new(legacy.Items.Count);
            foreach (SaleCommittedV1Item item in legacy.Items)
            {
                Result<Quantity> quantity = Quantity.FromX1000(item.QuantityX1000);
                if (quantity.IsFailure)
                    return Result<CompleteCheckoutSaleRequest>.Fail(Error.Validation("SaleCommittedV2.InvalidItemQuantity", quantity.Error));

                items.Add(new SaleItem
                {
                    Id = item.Id, SaleId = item.SaleId, ProductId = item.ProductId,
                    ProductNameSnapshot = item.ProductNameSnapshot, BarcodeSnapshot = item.BarcodeSnapshot,
                    UnitNameSnapshot = item.UnitNameSnapshot, Quantity = quantity.Value,
                    UnitPrice = Money.FromCents(item.UnitPriceCents),
                    CostPrice = item.CostPriceCents.HasValue ? Money.FromCents(item.CostPriceCents.Value) : null,
                    Discount = Money.FromCents(item.DiscountCents),
                    LineTotal = Money.FromCents(item.LineTotalCents)
                });
            }

            List<Payment> payments = legacy.Payments.Select(payment => new Payment
            {
                Id = payment.Id, SaleId = payment.SaleId,
                Method = payment.Method switch
                {
                    SaleCommittedV1Contract.PaymentCash => PaymentMethod.Cash,
                    SaleCommittedV1Contract.PaymentAccountsReceivable => PaymentMethod.AccountsReceivable,
                    _ => throw new InvalidOperationException("Unsupported SaleCommittedV2 payment token.")
                },
                Amount = Money.FromCents(payment.AmountCents), PaidAt = payment.PaidAt
            }).ToList();

            List<StockMovement> movements = new(legacy.StockMovements.Count);
            foreach (SaleCommittedV1StockMovement movement in legacy.StockMovements)
            {
                Result<Quantity> quantity = Quantity.FromX1000(movement.QuantityChangeX1000);
                if (quantity.IsFailure)
                    return Result<CompleteCheckoutSaleRequest>.Fail(Error.Validation("SaleCommittedV2.InvalidStockQuantity", quantity.Error));

                movements.Add(new StockMovement
                {
                    Id = movement.Id, ProductId = movement.ProductId,
                    MovementType = StockMovementType.Sale, QuantityChange = quantity.Value,
                    CostPrice = movement.CostPriceCents.HasValue ? Money.FromCents(movement.CostPriceCents.Value) : null,
                    ReferenceType = movement.ReferenceType, ReferenceId = movement.ReferenceId,
                    Reason = movement.Reason, UserId = movement.UserId, CreatedAt = movement.CreatedAt
                });
            }

            SaleCommittedV1Sale source = legacy.Sale;
            Sale sale = new()
            {
                Id = source.Id, ReceiptNumber = source.ReceiptNumber, SaleDate = source.SaleDate,
                CashierUserId = source.CashierUserId, CashierSessionId = source.CashierSessionId,
                Subtotal = Money.FromCents(source.SubtotalCents), Discount = Money.FromCents(source.DiscountCents),
                Tax = Money.FromCents(source.TaxCents), FeeTotal = Money.FromCents(payload.FeeTotalCents),
                Total = Money.FromCents(source.TotalCents), AmountPaid = Money.FromCents(source.AmountPaidCents),
                Change = Money.FromCents(source.ChangeCents), CustomerId = source.CustomerId,
                PaymentStatus = source.PaymentStatus switch
                {
                    SaleCommittedV1Contract.PaymentStatusPaid => SalePaymentStatus.Paid,
                    SaleCommittedV1Contract.PaymentStatusCredit => SalePaymentStatus.Credit,
                    SaleCommittedV1Contract.PaymentStatusPartial => SalePaymentStatus.Partial,
                    _ => throw new InvalidOperationException("Unsupported SaleCommittedV2 payment status.")
                },
                BalanceDue = Money.FromCents(source.BalanceDueCents),
                ReceivableDueDate = source.ReceivableDueDate, ReceivableNote = source.ReceivableNote,
                CustomerFullNameSnapshot = null, CustomerPhoneSnapshot = null,
                Status = SaleStatus.Completed, VoidedByUserId = null, VoidReason = null, VoidedAt = null,
                ReceiptSnapshot = source.ReceiptSnapshot, Items = items, Payments = payments, CreatedAt = source.CreatedAt
            };

            return Result<CompleteCheckoutSaleRequest>.Success(
                new CompleteCheckoutSaleRequest(sale, items, payments, movements));
        }
        catch (Exception ex) when (ex is InvalidOperationException or OverflowException)
        {
            return Result<CompleteCheckoutSaleRequest>.Fail(Error.Validation("SaleCommittedV2.MappingFailed", ex.Message));
        }
    }
}
