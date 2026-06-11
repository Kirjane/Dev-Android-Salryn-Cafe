using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ValueObjects;

namespace Salryn.Core.Services;

public sealed record PosCheckoutLineInput(
    Guid ProductId,
    string ProductNameSnapshot,
    string? BarcodeSnapshot,
    string UnitNameSnapshot,
    long QuantityX1000,
    long UnitPriceCents,
    long? CostPriceCents,
    long DiscountCents,
    bool TrackStock);

public sealed record PosCheckoutPaymentInput(
    PaymentMethod Method,
    long AmountCents);

public sealed record PosCheckoutApplicationRequest(
    Guid CashierUserId,
    Guid CashierSessionId,
    string ReceiptNumber,
    IReadOnlyList<PosCheckoutLineInput> Lines,
    IReadOnlyList<PosCheckoutPaymentInput> Payments,
    long SaleDiscountCents,
    long TaxCents,
    string? ReceiptSnapshot,
    DateTimeOffset? SaleDate = null);

public interface IPosCheckoutApplicationService
{
    Task<Result> CompleteSaleAsync(
        PosCheckoutApplicationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class PosCheckoutApplicationService : IPosCheckoutApplicationService
{
    private readonly IPosCheckoutService _checkoutService;

    public PosCheckoutApplicationService(IPosCheckoutService checkoutService)
    {
        _checkoutService = checkoutService ?? throw new ArgumentNullException(nameof(checkoutService));
    }

    public async Task<Result> CompleteSaleAsync(
        PosCheckoutApplicationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result validation = ValidateRequest(request);

        if (validation.IsFailure)
        {
            return validation;
        }

        try
        {
            Guid saleId = Guid.NewGuid();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset saleDate = request.SaleDate ?? now;

            List<SaleItem> items = new();
            List<StockMovement> stockMovements = new();

            long subtotalCents = 0;

            foreach (PosCheckoutLineInput line in request.Lines)
            {
                Result<Quantity> quantityResult = Quantity.FromX1000(line.QuantityX1000);

                if (quantityResult.IsFailure)
                {
                    return Result.Fail(Error.Validation(
                        "CheckoutBoundary.InvalidQuantity",
                        "Checkout line quantity is invalid."));
                }

                Quantity quantity = quantityResult.Value;
                Money unitPrice = Money.FromCents(line.UnitPriceCents);
                Money discount = Money.FromCents(line.DiscountCents);
                Money? costPrice = line.CostPriceCents.HasValue
                    ? Money.FromCents(line.CostPriceCents.Value)
                    : null;

                long grossLineTotalCents = CalculateLineGrossCents(unitPrice, quantity);

                if (line.DiscountCents > grossLineTotalCents)
                {
                    return Result.Fail(Error.Validation(
                        "CheckoutBoundary.LineDiscountExceedsGross",
                        "Checkout line discount cannot exceed line gross amount."));
                }

                long lineTotalCents = checked(grossLineTotalCents - line.DiscountCents);
                subtotalCents = checked(subtotalCents + lineTotalCents);

                SaleItem item = new()
                {
                    Id = Guid.NewGuid(),
                    SaleId = saleId,
                    ProductId = line.ProductId,
                    ProductNameSnapshot = line.ProductNameSnapshot.Trim(),
                    BarcodeSnapshot = string.IsNullOrWhiteSpace(line.BarcodeSnapshot)
                        ? null
                        : line.BarcodeSnapshot.Trim(),
                    UnitNameSnapshot = line.UnitNameSnapshot.Trim(),
                    Quantity = quantity,
                    UnitPrice = unitPrice,
                    CostPrice = costPrice,
                    Discount = discount,
                    LineTotal = Money.FromCents(lineTotalCents)
                };

                items.Add(item);

                if (line.TrackStock)
                {
                    stockMovements.Add(new StockMovement
                    {
                        Id = Guid.NewGuid(),
                        ProductId = line.ProductId,
                        MovementType = StockMovementType.Sale,
                        QuantityChange = quantity,
                        CostPrice = costPrice,
                        ReferenceType = "SALE",
                        ReferenceId = saleId,
                        Reason = "POS checkout sale stock-out",
                        UserId = request.CashierUserId,
                        CreatedAt = now
                    });
                }
            }

            List<Payment> payments = new();
            long amountPaidCents = 0;

            foreach (PosCheckoutPaymentInput paymentInput in request.Payments)
            {
                amountPaidCents = checked(amountPaidCents + paymentInput.AmountCents);

                payments.Add(new Payment
                {
                    Id = Guid.NewGuid(),
                    SaleId = saleId,
                    Method = paymentInput.Method,
                    Amount = Money.FromCents(paymentInput.AmountCents),
                    PaidAt = now
                });
            }

            long totalCents = checked(subtotalCents - request.SaleDiscountCents + request.TaxCents);

            if (totalCents < 0)
            {
                return Result.Fail(Error.Validation(
                    "CheckoutBoundary.TotalCannotBeNegative",
                    "Checkout total cannot be negative."));
            }

            if (amountPaidCents < totalCents)
            {
                return Result.Fail(Error.Validation(
                    "CheckoutBoundary.InsufficientPayment",
                    "Checkout payment cannot be less than the sale total."));
            }

            long changeCents = checked(amountPaidCents - totalCents);

            Sale sale = new()
            {
                Id = saleId,
                ReceiptNumber = request.ReceiptNumber.Trim(),
                SaleDate = saleDate,
                CashierUserId = request.CashierUserId,
                CashierSessionId = request.CashierSessionId,
                Subtotal = Money.FromCents(subtotalCents),
                Discount = Money.FromCents(request.SaleDiscountCents),
                Tax = Money.FromCents(request.TaxCents),
                Total = Money.FromCents(totalCents),
                AmountPaid = Money.FromCents(amountPaidCents),
                Change = Money.FromCents(changeCents),
                Status = SaleStatus.Completed,
                VoidedByUserId = null,
                VoidReason = null,
                VoidedAt = null,
                ReceiptSnapshot = string.IsNullOrWhiteSpace(request.ReceiptSnapshot)
                    ? null
                    : request.ReceiptSnapshot.Trim(),
                CreatedAt = now
            };

            return await _checkoutService.CompleteSaleAsync(
                new CompleteCheckoutSaleRequest(
                    sale,
                    items,
                    payments,
                    stockMovements),
                cancellationToken);
        }
        catch (OverflowException)
        {
            return Result.Fail(Error.Validation(
                "CheckoutBoundary.AmountOverflow",
                "Checkout amount calculation is too large."));
        }
    }

    private static Result ValidateRequest(PosCheckoutApplicationRequest request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "CheckoutBoundary.RequestRequired",
                "Checkout request is required."));
        }

        if (request.CashierUserId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "CheckoutBoundary.InvalidCashierUserId",
                "Checkout cashier user ID is required."));
        }

        if (request.CashierSessionId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "CheckoutBoundary.InvalidCashierSessionId",
                "Checkout cashier session ID is required."));
        }

        if (string.IsNullOrWhiteSpace(request.ReceiptNumber))
        {
            return Result.Fail(Error.Validation(
                "CheckoutBoundary.ReceiptNumberRequired",
                "Checkout receipt number is required."));
        }

        if (request.Lines is null || request.Lines.Count == 0)
        {
            return Result.Fail(Error.Validation(
                "CheckoutBoundary.LinesRequired",
                "At least one checkout line is required."));
        }

        if (request.Payments is null || request.Payments.Count == 0)
        {
            return Result.Fail(Error.Validation(
                "CheckoutBoundary.PaymentsRequired",
                "At least one checkout payment is required."));
        }

        if (request.SaleDiscountCents < 0 || request.TaxCents < 0)
        {
            return Result.Fail(Error.Validation(
                "CheckoutBoundary.InvalidMoneyAmount",
                "Checkout discount and tax amounts cannot be negative."));
        }

        foreach (PosCheckoutLineInput line in request.Lines)
        {
            Result lineValidation = ValidateLine(line);

            if (lineValidation.IsFailure)
            {
                return lineValidation;
            }
        }

        foreach (PosCheckoutPaymentInput payment in request.Payments)
        {
            Result paymentValidation = ValidatePayment(payment);

            if (paymentValidation.IsFailure)
            {
                return paymentValidation;
            }
        }

        return Result.Success();
    }

    private static Result ValidateLine(PosCheckoutLineInput line)
    {
        if (line is null)
        {
            return Result.Fail(Error.Validation(
                "CheckoutBoundary.LineRequired",
                "Checkout line is required."));
        }

        if (line.ProductId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "CheckoutBoundary.InvalidProductId",
                "Checkout line product ID is required."));
        }

        if (string.IsNullOrWhiteSpace(line.ProductNameSnapshot))
        {
            return Result.Fail(Error.Validation(
                "CheckoutBoundary.ProductNameRequired",
                "Checkout line product name is required."));
        }

        if (string.IsNullOrWhiteSpace(line.UnitNameSnapshot))
        {
            return Result.Fail(Error.Validation(
                "CheckoutBoundary.UnitNameRequired",
                "Checkout line unit name is required."));
        }

        if (line.QuantityX1000 <= 0)
        {
            return Result.Fail(Error.Validation(
                "CheckoutBoundary.InvalidQuantity",
                "Checkout line quantity must be greater than zero."));
        }

        if (line.UnitPriceCents < 0 ||
            line.DiscountCents < 0 ||
            line.CostPriceCents < 0)
        {
            return Result.Fail(Error.Validation(
                "CheckoutBoundary.InvalidMoneyAmount",
                "Checkout line money amounts cannot be negative."));
        }

        return Result.Success();
    }

    private static Result ValidatePayment(PosCheckoutPaymentInput payment)
    {
        if (!Enum.IsDefined(typeof(PaymentMethod), payment.Method))
        {
            return Result.Fail(Error.Validation(
                "CheckoutBoundary.InvalidPaymentMethod",
                "Checkout payment method is invalid."));
        }

        if (payment.AmountCents <= 0)
        {
            return Result.Fail(Error.Validation(
                "CheckoutBoundary.PaymentAmountRequired",
                "Checkout payment amount must be greater than zero."));
        }

        return Result.Success();
    }

    private static long CalculateLineGrossCents(
        Money unitPrice,
        Quantity quantity)
    {
        decimal gross = unitPrice.Cents * (quantity.ValueX1000 / 1000m);

        return checked((long)Math.Round(
            gross,
            0,
            MidpointRounding.AwayFromZero));
    }
}

