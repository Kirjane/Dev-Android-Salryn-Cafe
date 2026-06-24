using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
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
    private readonly IProductReadRepository? _productReadRepository;

    public PosCheckoutApplicationService(IPosCheckoutService checkoutService)
    {
        _checkoutService = checkoutService ?? throw new ArgumentNullException(nameof(checkoutService));
    }

    public PosCheckoutApplicationService(
        IPosCheckoutService checkoutService,
        IProductReadRepository productReadRepository)
        : this(checkoutService)
    {
        _productReadRepository = productReadRepository ?? throw new ArgumentNullException(nameof(productReadRepository));
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
                Result<ResolvedCheckoutLineSnapshot> snapshotResult =
                    await ResolveLineSnapshotAsync(line, cancellationToken);

                if (snapshotResult.IsFailure)
                {
                    return Result.Fail(snapshotResult.ErrorDetail);
                }

                ResolvedCheckoutLineSnapshot snapshot = snapshotResult.Value;
                Result<Quantity> quantityResult = Quantity.FromX1000(line.QuantityX1000);

                if (quantityResult.IsFailure)
                {
                    return Result.Fail(Error.Validation(
                        "CheckoutBoundary.InvalidQuantity",
                        "Checkout line quantity is invalid."));
                }

                Quantity quantity = quantityResult.Value;
                Money unitPrice = Money.FromCents(snapshot.UnitPriceCents);
                Money discount = Money.FromCents(line.DiscountCents);
                Money? costPrice = snapshot.CostPriceCents.HasValue
                    ? Money.FromCents(snapshot.CostPriceCents.Value)
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
                    ProductId = snapshot.ProductId,
                    ProductNameSnapshot = snapshot.ProductNameSnapshot.Trim(),
                    BarcodeSnapshot = string.IsNullOrWhiteSpace(snapshot.BarcodeSnapshot)
                        ? null
                        : snapshot.BarcodeSnapshot.Trim(),
                    UnitNameSnapshot = snapshot.UnitNameSnapshot.Trim(),
                    Quantity = quantity,
                    UnitPrice = unitPrice,
                    CostPrice = costPrice,
                    Discount = discount,
                    LineTotal = Money.FromCents(lineTotalCents)
                };

                items.Add(item);

                if (snapshot.TrackStock)
                {
                    stockMovements.Add(new StockMovement
                    {
                        Id = Guid.NewGuid(),
                        ProductId = snapshot.ProductId,
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

    private async Task<Result<ResolvedCheckoutLineSnapshot>> ResolveLineSnapshotAsync(
        PosCheckoutLineInput line,
        CancellationToken cancellationToken)
    {
        if (_productReadRepository is null)
        {
            return Result<ResolvedCheckoutLineSnapshot>.Success(
                ResolvedCheckoutLineSnapshot.FromInput(line));
        }

        Result<ProductLookupReadModel?> productResult =
            await _productReadRepository.FindByIdAsync(line.ProductId, cancellationToken);

        if (productResult.IsFailure)
        {
            return Result<ResolvedCheckoutLineSnapshot>.Fail(productResult.ErrorDetail);
        }

        ProductLookupReadModel? product = productResult.Value;

        if (product is null || !product.IsActive)
        {
            return Result<ResolvedCheckoutLineSnapshot>.Fail(Error.NotFound(
                "CheckoutBoundary.ProductNotFoundOrInactive",
                "Checkout product was not found or is inactive."));
        }

        if (line.DiscountCents > 0 && !product.AllowDiscount)
        {
            return Result<ResolvedCheckoutLineSnapshot>.Fail(Error.Validation(
                "CheckoutBoundary.ProductDiscountNotAllowed",
                "Checkout product does not allow line discounts."));
        }

        // Kirch: Completion re-reads product facts so a POS terminal cannot persist tampered price or stock flags.
        return Result<ResolvedCheckoutLineSnapshot>.Success(
            new ResolvedCheckoutLineSnapshot(
                product.Id,
                product.Name,
                product.Barcode,
                product.UnitName,
                product.SellingPrice.Cents,
                product.CostPrice?.Cents,
                product.TrackStock));
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
        if (payment is null)
        {
            return Result.Fail(Error.Validation(
                "CheckoutBoundary.PaymentRequired",
                "Checkout payment is required."));
        }

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

    private sealed record ResolvedCheckoutLineSnapshot(
        Guid ProductId,
        string ProductNameSnapshot,
        string? BarcodeSnapshot,
        string UnitNameSnapshot,
        long UnitPriceCents,
        long? CostPriceCents,
        bool TrackStock)
    {
        public static ResolvedCheckoutLineSnapshot FromInput(PosCheckoutLineInput line)
        {
            return new ResolvedCheckoutLineSnapshot(
                line.ProductId,
                line.ProductNameSnapshot,
                line.BarcodeSnapshot,
                line.UnitNameSnapshot,
                line.UnitPriceCents,
                line.CostPriceCents,
                line.TrackStock);
        }
    }
}

