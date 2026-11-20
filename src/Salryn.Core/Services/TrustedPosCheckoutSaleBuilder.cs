using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.ValueObjects;

namespace Salryn.Core.Services;

public sealed record TrustedPosCheckoutSaleBuildResult(
    CompleteCheckoutSaleRequest Request,
    IReadOnlyList<OfficialDocumentLineTaxClassification> LineTaxClassifications);

public interface ITrustedPosCheckoutSaleBuilder
{
    Task<Result<TrustedPosCheckoutSaleBuildResult>> BuildAsync(
        PosCheckoutApplicationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class TrustedPosCheckoutSaleBuilder : ITrustedPosCheckoutSaleBuilder
{
    private readonly IProductReadRepository? _productReadRepository;
    private readonly IStoreSettingsReadRepository? _storeSettingsReadRepository;

    public TrustedPosCheckoutSaleBuilder()
    {
    }

    public TrustedPosCheckoutSaleBuilder(IProductReadRepository productReadRepository)
    {
        _productReadRepository = productReadRepository ?? throw new ArgumentNullException(nameof(productReadRepository));
    }

    public TrustedPosCheckoutSaleBuilder(
        IProductReadRepository productReadRepository,
        IStoreSettingsReadRepository storeSettingsReadRepository)
    {
        _productReadRepository = productReadRepository ?? throw new ArgumentNullException(nameof(productReadRepository));
        _storeSettingsReadRepository = storeSettingsReadRepository ?? throw new ArgumentNullException(nameof(storeSettingsReadRepository));
    }

    public async Task<Result<TrustedPosCheckoutSaleBuildResult>> BuildAsync(
        PosCheckoutApplicationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result validation = ValidateRequest(request);

        if (validation.IsFailure)
        {
            return Result<TrustedPosCheckoutSaleBuildResult>.Fail(validation.ErrorDetail);
        }

        try
        {
            Guid saleId = Guid.NewGuid();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset saleDate = request.SaleDate ?? now;

            List<SaleItem> items = new();
            List<StockMovement> stockMovements = new();
            List<OfficialDocumentLineTaxClassification> lineTaxClassifications = new();

            long subtotalCents = 0;

            foreach (PosCheckoutLineInput line in request.Lines)
            {
                Result<ResolvedCheckoutLineSnapshot> snapshotResult =
                    await ResolveLineSnapshotAsync(line, cancellationToken).ConfigureAwait(false);

                if (snapshotResult.IsFailure)
                {
                    return Result<TrustedPosCheckoutSaleBuildResult>.Fail(snapshotResult.ErrorDetail);
                }

                ResolvedCheckoutLineSnapshot snapshot = snapshotResult.Value;
                Result<Quantity> quantityResult = Quantity.FromX1000(line.QuantityX1000);

                if (quantityResult.IsFailure)
                {
                    return Result<TrustedPosCheckoutSaleBuildResult>.Fail(Error.Validation(
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
                    return Result<TrustedPosCheckoutSaleBuildResult>.Fail(Error.Validation(
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
                lineTaxClassifications.Add(snapshot.TaxClassification);

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
                return Result<TrustedPosCheckoutSaleBuildResult>.Fail(Error.Validation(
                    "CheckoutBoundary.TotalCannotBeNegative",
                    "Checkout total cannot be negative."));
            }

            if (amountPaidCents < totalCents)
            {
                return Result<TrustedPosCheckoutSaleBuildResult>.Fail(Error.Validation(
                    "CheckoutBoundary.InsufficientPayment",
                    "Checkout payment cannot be less than the sale total."));
            }

            if (request.Payments.Any(payment =>
                    payment.Method != PaymentMethod.Cash &&
                    payment.Method != PaymentMethod.AccountsReceivable))
            {
                return Result<TrustedPosCheckoutSaleBuildResult>.Fail(Error.Validation(
                    "CheckoutBoundary.UnsupportedPaymentMethod",
                    "Only Cash and AccountsReceivable (or partial Cash + AccountsReceivable) tenders are supported."));
            }

            long receivableCents = request.Payments
                .Where(payment => payment.Method == PaymentMethod.AccountsReceivable)
                .Sum(payment => payment.AmountCents);
            long cashTenderCents = request.Payments
                .Where(payment => payment.Method == PaymentMethod.Cash)
                .Sum(payment => payment.AmountCents);

            if (receivableCents > 0 && (!request.CustomerId.HasValue || request.CustomerId.Value == Guid.Empty))
            {
                return Result<TrustedPosCheckoutSaleBuildResult>.Fail(Error.Validation(
                    "CheckoutBoundary.CustomerRequiredForUtang",
                    "A customer is required when an Utang balance is due."));
            }
            if (receivableCents > totalCents)
            {
                return Result<TrustedPosCheckoutSaleBuildResult>.Fail(Error.Validation(
                    "CheckoutBoundary.InvalidUtangAmount",
                    "Utang cannot exceed the sale total."));
            }

            long cashDueCents = Math.Max(0L, totalCents - receivableCents);

            if (receivableCents > 0 && cashTenderCents > cashDueCents)
            {
                return Result<TrustedPosCheckoutSaleBuildResult>.Fail(Error.Validation(
                    "CheckoutBoundary.UtangCannotProduceChange",
                    "Utang credit sales cannot produce cash change."));
            }

            long changeCents = receivableCents > 0
                ? 0
                : Math.Max(0L, cashTenderCents - cashDueCents);
            Money subtotal = Money.FromCents(subtotalCents);
            Money saleDiscount = Money.FromCents(request.SaleDiscountCents);
            Money tax = Money.FromCents(request.TaxCents);
            Money total = Money.FromCents(totalCents);
            Money amountPaid = Money.FromCents(amountPaidCents);
            Money change = Money.FromCents(changeCents);
            StoreSettingsReadModel? settings = null;
            if (_storeSettingsReadRepository is not null)
            {
                Result<StoreSettingsReadModel?> settingsResult = await _storeSettingsReadRepository.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
                if (settingsResult.IsFailure) return Result<TrustedPosCheckoutSaleBuildResult>.Fail(settingsResult.ErrorDetail);
                settings = settingsResult.Value;
            }

            string receiptSnapshot = string.IsNullOrWhiteSpace(request.ReceiptSnapshot)
                ? PosReceiptTextRenderer.Render(new PosReceiptSnapshotDraft(
                    request.ReceiptNumber.Trim(),
                    saleDate,
                    request.CashierUserId,
                    subtotal,
                    saleDiscount,
                    tax,
                    total,
                    amountPaid,
                    change,
                    SaleStatus.Completed,
                    items,
                    payments,
                    settings?.StoreName,
                    settings?.BusinessName,
                    settings?.Address,
                    settings?.ContactNumber,
                    settings?.ReceiptHeader,
                    settings?.ReceiptFooter,
                    request.CashierDisplayName,
                    request.CustomerFullName,
                    request.CustomerPhone,
                    Money.FromCents(cashTenderCents),
                    Money.FromCents(receivableCents),
                    Money.FromCents(receivableCents),
                    request.ReceivableDueDate,
                    request.ReceivableNote))
                : request.ReceiptSnapshot.Trim();

            Sale sale = new()
            {
                Id = saleId,
                ReceiptNumber = request.ReceiptNumber.Trim(),
                SaleDate = saleDate,
                CashierUserId = request.CashierUserId,
                CashierSessionId = request.CashierSessionId,
                Subtotal = subtotal,
                Discount = saleDiscount,
                Tax = tax,
                Total = total,
                AmountPaid = amountPaid,
                Change = change,
                CustomerId = request.CustomerId,
                PaymentStatus = receivableCents == 0
                    ? SalePaymentStatus.Paid
                    : cashTenderCents == 0 ? SalePaymentStatus.Credit : SalePaymentStatus.Partial,
                BalanceDue = Money.FromCents(receivableCents),
                ReceivableDueDate = request.ReceivableDueDate,
                ReceivableNote = string.IsNullOrWhiteSpace(request.ReceivableNote) ? null : request.ReceivableNote.Trim(),
                CustomerFullNameSnapshot = string.IsNullOrWhiteSpace(request.CustomerFullName) ? null : request.CustomerFullName.Trim(),
                CustomerPhoneSnapshot = string.IsNullOrWhiteSpace(request.CustomerPhone) ? null : request.CustomerPhone.Trim(),
                Status = SaleStatus.Completed,
                VoidedByUserId = null,
                VoidReason = null,
                VoidedAt = null,
                ReceiptSnapshot = receiptSnapshot,
                CreatedAt = now
            };

            return Result<TrustedPosCheckoutSaleBuildResult>.Success(
                new TrustedPosCheckoutSaleBuildResult(
                    new CompleteCheckoutSaleRequest(
                        sale,
                        items,
                        payments,
                        stockMovements),
                    lineTaxClassifications));
        }
        catch (OverflowException)
        {
            return Result<TrustedPosCheckoutSaleBuildResult>.Fail(Error.Validation(
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
            await _productReadRepository.FindByIdAsync(line.ProductId, cancellationToken).ConfigureAwait(false);

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

        // Kirch: why - official and standard completion both re-read persisted product facts before any sale can be built.
        return Result<ResolvedCheckoutLineSnapshot>.Success(
            new ResolvedCheckoutLineSnapshot(
                product.Id,
                product.Name,
                product.Barcode,
                product.UnitName,
                product.SellingPrice.Cents,
                product.CostPrice?.Cents,
                product.TrackStock,
                ProductTaxClassificationMapper.ToOfficialLineClassification(product.TaxClassification)));
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
        bool TrackStock,
        OfficialDocumentLineTaxClassification TaxClassification)
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
                line.TrackStock,
                ProductTaxClassificationMapper.ToOfficialLineClassification(line.TaxClassification));
        }
    }
}
