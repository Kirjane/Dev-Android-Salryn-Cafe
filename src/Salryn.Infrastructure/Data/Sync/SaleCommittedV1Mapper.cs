using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

public static class SaleCommittedV1Mapper
{
    public static SaleCommittedV1 FromCore(CompleteCheckoutSaleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Sale);

        Sale sale = request.Sale;
        if (!sale.CashierSessionId.HasValue || sale.CashierSessionId.Value == Guid.Empty)
        {
            throw new InvalidOperationException(
                "SaleCommittedV1 requires the committed sale to carry its cashier session ID.");
        }

        return new SaleCommittedV1(
            new SaleCommittedV1Sale(
                sale.Id,
                sale.ReceiptNumber,
                sale.SaleDate,
                sale.CashierUserId,
                sale.CashierSessionId.Value,
                sale.Subtotal.Cents,
                sale.Discount.Cents,
                sale.Tax.Cents,
                sale.Total.Cents,
                sale.AmountPaid.Cents,
                sale.Change.Cents,
                sale.CustomerId,
                sale.PaymentStatus switch
                {
                    SalePaymentStatus.Paid => SaleCommittedV1Contract.PaymentStatusPaid,
                    SalePaymentStatus.Credit => SaleCommittedV1Contract.PaymentStatusCredit,
                    SalePaymentStatus.Partial => SaleCommittedV1Contract.PaymentStatusPartial,
                    _ => throw new InvalidOperationException("Unsupported sale payment status.")
                },
                sale.BalanceDue.Cents,
                sale.ReceivableDueDate,
                sale.ReceivableNote,
                sale.ReceiptSnapshot,
                sale.CreatedAt),
            request.Items.Select(item => new SaleCommittedV1Item(
                item.Id,
                item.SaleId,
                item.ProductId,
                item.ProductNameSnapshot,
                item.BarcodeSnapshot,
                item.UnitNameSnapshot,
                item.Quantity.ValueX1000,
                item.UnitPrice.Cents,
                item.CostPrice?.Cents,
                item.Discount.Cents,
                item.LineTotal.Cents)).ToArray(),
            request.Payments.Select(payment => new SaleCommittedV1Payment(
                payment.Id,
                payment.SaleId,
                payment.Method switch
                {
                    PaymentMethod.Cash => SaleCommittedV1Contract.PaymentCash,
                    PaymentMethod.AccountsReceivable => SaleCommittedV1Contract.PaymentAccountsReceivable,
                    _ => throw new InvalidOperationException("Unsupported payment method.")
                },
                payment.Amount.Cents,
                payment.PaidAt)).ToArray(),
            request.StockMovements.Select(movement => new SaleCommittedV1StockMovement(
                movement.Id,
                movement.ProductId,
                movement.MovementType switch
                {
                    StockMovementType.Sale => SaleCommittedV1Contract.StockMovementSale,
                    _ => throw new InvalidOperationException(
                        "SaleCommittedV1 only serializes committed sale stock movements.")
                },
                movement.QuantityChange.ValueX1000,
                movement.CostPrice?.Cents,
                movement.ReferenceType ?? string.Empty,
                movement.ReferenceId
                    ?? throw new InvalidOperationException("Sale stock movement reference ID is required."),
                movement.Reason,
                movement.UserId,
                movement.CreatedAt)).ToArray());
    }

    public static Result<CompleteCheckoutSaleRequest> ToCore(SaleCommittedV1 payload)
    {
        SaleCommittedV1ValidationResult validation = SaleCommittedV1Validator.Validate(payload);
        if (!validation.IsValid)
        {
            return Result<CompleteCheckoutSaleRequest>.Fail(Error.Validation(
                validation.ErrorCode ?? "SaleCommittedV1.Invalid",
                validation.ErrorMessage ?? "SaleCommittedV1 payload is invalid."));
        }

        try
        {
            List<SaleItem> items = new(payload.Items.Count);
            foreach (SaleCommittedV1Item item in payload.Items)
            {
                Result<Quantity> quantity = Quantity.FromX1000(item.QuantityX1000);
                if (quantity.IsFailure)
                {
                    return Result<CompleteCheckoutSaleRequest>.Fail(Error.Validation(
                        "SaleCommittedV1.InvalidItemQuantity",
                        quantity.Error));
                }

                items.Add(new SaleItem
                {
                    Id = item.Id,
                    SaleId = item.SaleId,
                    ProductId = item.ProductId,
                    ProductNameSnapshot = item.ProductNameSnapshot,
                    BarcodeSnapshot = item.BarcodeSnapshot,
                    UnitNameSnapshot = item.UnitNameSnapshot,
                    Quantity = quantity.Value,
                    UnitPrice = Money.FromCents(item.UnitPriceCents),
                    CostPrice = item.CostPriceCents.HasValue
                        ? Money.FromCents(item.CostPriceCents.Value)
                        : null,
                    Discount = Money.FromCents(item.DiscountCents),
                    LineTotal = Money.FromCents(item.LineTotalCents)
                });
            }

            List<Payment> payments = payload.Payments.Select(payment => new Payment
            {
                Id = payment.Id,
                SaleId = payment.SaleId,
                Method = payment.Method switch
                {
                    SaleCommittedV1Contract.PaymentCash => PaymentMethod.Cash,
                    SaleCommittedV1Contract.PaymentAccountsReceivable => PaymentMethod.AccountsReceivable,
                    _ => throw new InvalidOperationException("Unsupported SaleCommittedV1 payment token.")
                },
                Amount = Money.FromCents(payment.AmountCents),
                PaidAt = payment.PaidAt
            }).ToList();

            List<StockMovement> movements = new(payload.StockMovements.Count);
            foreach (SaleCommittedV1StockMovement movement in payload.StockMovements)
            {
                Result<Quantity> quantity = Quantity.FromX1000(movement.QuantityChangeX1000);
                if (quantity.IsFailure)
                {
                    return Result<CompleteCheckoutSaleRequest>.Fail(Error.Validation(
                        "SaleCommittedV1.InvalidStockQuantity",
                        quantity.Error));
                }

                movements.Add(new StockMovement
                {
                    Id = movement.Id,
                    ProductId = movement.ProductId,
                    MovementType = StockMovementType.Sale,
                    QuantityChange = quantity.Value,
                    CostPrice = movement.CostPriceCents.HasValue
                        ? Money.FromCents(movement.CostPriceCents.Value)
                        : null,
                    ReferenceType = movement.ReferenceType,
                    ReferenceId = movement.ReferenceId,
                    Reason = movement.Reason,
                    UserId = movement.UserId,
                    CreatedAt = movement.CreatedAt
                });
            }

            SaleCommittedV1Sale source = payload.Sale;
            Sale sale = new()
            {
                Id = source.Id,
                ReceiptNumber = source.ReceiptNumber,
                SaleDate = source.SaleDate,
                CashierUserId = source.CashierUserId,
                CashierSessionId = source.CashierSessionId,
                Subtotal = Money.FromCents(source.SubtotalCents),
                Discount = Money.FromCents(source.DiscountCents),
                Tax = Money.FromCents(source.TaxCents),
                Total = Money.FromCents(source.TotalCents),
                AmountPaid = Money.FromCents(source.AmountPaidCents),
                Change = Money.FromCents(source.ChangeCents),
                CustomerId = source.CustomerId,
                PaymentStatus = source.PaymentStatus switch
                {
                    SaleCommittedV1Contract.PaymentStatusPaid => SalePaymentStatus.Paid,
                    SaleCommittedV1Contract.PaymentStatusCredit => SalePaymentStatus.Credit,
                    SaleCommittedV1Contract.PaymentStatusPartial => SalePaymentStatus.Partial,
                    _ => throw new InvalidOperationException("Unsupported SaleCommittedV1 payment status.")
                },
                BalanceDue = Money.FromCents(source.BalanceDueCents),
                ReceivableDueDate = source.ReceivableDueDate,
                ReceivableNote = source.ReceivableNote,
                CustomerFullNameSnapshot = null,
                CustomerPhoneSnapshot = null,
                Status = SaleStatus.Completed,
                VoidedByUserId = null,
                VoidReason = null,
                VoidedAt = null,
                ReceiptSnapshot = source.ReceiptSnapshot,
                Items = items,
                Payments = payments,
                CreatedAt = source.CreatedAt
            };

            return Result<CompleteCheckoutSaleRequest>.Success(
                new CompleteCheckoutSaleRequest(sale, items, payments, movements));
        }
        catch (Exception ex) when (ex is InvalidOperationException or OverflowException)
        {
            return Result<CompleteCheckoutSaleRequest>.Fail(Error.Validation(
                "SaleCommittedV1.MappingFailed",
                ex.Message));
        }
    }
}
