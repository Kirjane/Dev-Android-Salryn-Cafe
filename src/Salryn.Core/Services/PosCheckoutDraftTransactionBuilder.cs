using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Core.Services;

public sealed record PosCheckoutDraftLineInput(
    Guid ProductId,
    string ProductNameSnapshot,
    string? BarcodeSnapshot,
    string UnitNameSnapshot,
    long QuantityX1000,
    long UnitPriceCents,
    long? CostPriceCents,
    bool TrackStock,
    long DiscountCents = 0);

public sealed record PosCheckoutDraftLine(
    Guid Id,
    Guid ProductId,
    string ProductNameSnapshot,
    string? BarcodeSnapshot,
    string UnitNameSnapshot,
    long QuantityX1000,
    long UnitPriceCents,
    long? CostPriceCents,
    long DiscountCents,
    bool TrackStock,
    long GrossCents,
    long LineTotalCents);

public sealed record PosCheckoutDraftPayment(
    Guid Id,
    PaymentMethod Method,
    long AmountCents);

public sealed record PosCheckoutDraftTotals(
    long SubtotalCents,
    long SaleDiscountCents,
    long TaxCents,
    long TotalCents,
    long AmountPaidCents,
    long ChangeCents,
    long BalanceDueCents);

public sealed record PosCheckoutDraftTransaction(
    Guid CashierUserId,
    Guid? CashierSessionId,
    string? ReceiptNumber,
    IReadOnlyList<PosCheckoutDraftLine> Lines,
    IReadOnlyList<PosCheckoutDraftPayment> Payments,
    PosCheckoutDraftTotals Totals,
    string? ReceiptSnapshot);

public sealed class PosCheckoutDraftTransactionBuilder
{
    private readonly List<PosCheckoutDraftLine> _lines = new();
    private readonly List<PosCheckoutDraftPayment> _payments = new();

    private Guid _cashierUserId;
    private Guid? _cashierSessionId;
    private string? _receiptNumber;
    private string? _receiptSnapshot;
    private long _saleDiscountCents;
    private long _taxCents;

    public Result SetCashierUserId(Guid cashierUserId)
    {
        if (cashierUserId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "CheckoutDraft.InvalidCashierUserId",
                "Checkout draft cashier user ID is required."));
        }

        _cashierUserId = cashierUserId;
        return Result.Success();
    }

    public Result SetCashierSessionId(Guid cashierSessionId)
    {
        if (cashierSessionId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "CheckoutDraft.InvalidCashierSessionId",
                "Checkout draft cashier session ID is required."));
        }

        _cashierSessionId = cashierSessionId;
        return Result.Success();
    }

    public Result SetReceiptNumber(string receiptNumber)
    {
        if (string.IsNullOrWhiteSpace(receiptNumber))
        {
            return Result.Fail(Error.Validation(
                "CheckoutDraft.ReceiptNumberRequired",
                "Checkout draft receipt number is required."));
        }

        _receiptNumber = receiptNumber.Trim();
        return Result.Success();
    }

    public Result SetReceiptSnapshot(string? receiptSnapshot)
    {
        _receiptSnapshot = string.IsNullOrWhiteSpace(receiptSnapshot)
            ? null
            : receiptSnapshot.Trim();

        return Result.Success();
    }

    public Result<PosCheckoutDraftLine> AddLine(PosCheckoutDraftLineInput input)
    {
        Result validation = ValidateLineInput(input);

        if (validation.IsFailure)
        {
            return Result<PosCheckoutDraftLine>.Fail(validation.ErrorDetail);
        }

        try
        {
            long grossCents = CalculateLineGrossCents(input.UnitPriceCents, input.QuantityX1000);

            if (input.DiscountCents > grossCents)
            {
                return Result<PosCheckoutDraftLine>.Fail(Error.Validation(
                    "CheckoutDraft.LineDiscountExceedsGross",
                    "Checkout draft line discount cannot exceed line gross amount."));
            }

            PosCheckoutDraftLine line = new(
                Guid.NewGuid(),
                input.ProductId,
                input.ProductNameSnapshot.Trim(),
                string.IsNullOrWhiteSpace(input.BarcodeSnapshot) ? null : input.BarcodeSnapshot.Trim(),
                input.UnitNameSnapshot.Trim(),
                input.QuantityX1000,
                input.UnitPriceCents,
                input.CostPriceCents,
                input.DiscountCents,
                input.TrackStock,
                grossCents,
                checked(grossCents - input.DiscountCents));

            _lines.Add(line);

            return Result<PosCheckoutDraftLine>.Success(line);
        }
        catch (OverflowException)
        {
            return Result<PosCheckoutDraftLine>.Fail(AmountOverflow());
        }
    }

    public Result RemoveLine(Guid lineId)
    {
        int index = FindLineIndex(lineId);

        if (index < 0)
        {
            return Result.Fail(LineNotFound());
        }

        _lines.RemoveAt(index);

        if (_saleDiscountCents > CalculateSubtotalCents())
        {
            _saleDiscountCents = CalculateSubtotalCents();
        }

        return Result.Success();
    }

    public Result<PosCheckoutDraftLine> SetLineQuantity(
        Guid lineId,
        long quantityX1000)
    {
        return UpdateLine(lineId, line =>
        {
            if (quantityX1000 <= 0)
            {
                return Result<PosCheckoutDraftLine>.Fail(Error.Validation(
                    "CheckoutDraft.InvalidQuantity",
                    "Checkout draft line quantity must be greater than zero."));
            }

            try
            {
                long grossCents = CalculateLineGrossCents(line.UnitPriceCents, quantityX1000);

                if (line.DiscountCents > grossCents)
                {
                    return Result<PosCheckoutDraftLine>.Fail(Error.Validation(
                        "CheckoutDraft.LineDiscountExceedsGross",
                        "Checkout draft line discount cannot exceed line gross amount."));
                }

                return Result<PosCheckoutDraftLine>.Success(line with
                {
                    QuantityX1000 = quantityX1000,
                    GrossCents = grossCents,
                    LineTotalCents = checked(grossCents - line.DiscountCents)
                });
            }
            catch (OverflowException)
            {
                return Result<PosCheckoutDraftLine>.Fail(AmountOverflow());
            }
        });
    }

    public Result<PosCheckoutDraftLine> SetLineDiscount(
        Guid lineId,
        long discountCents)
    {
        return UpdateLine(lineId, line =>
        {
            if (discountCents < 0)
            {
                return Result<PosCheckoutDraftLine>.Fail(Error.Validation(
                    "CheckoutDraft.InvalidMoneyAmount",
                    "Checkout draft money amounts cannot be negative."));
            }

            if (discountCents > line.GrossCents)
            {
                return Result<PosCheckoutDraftLine>.Fail(Error.Validation(
                    "CheckoutDraft.LineDiscountExceedsGross",
                    "Checkout draft line discount cannot exceed line gross amount."));
            }

            return Result<PosCheckoutDraftLine>.Success(line with
            {
                DiscountCents = discountCents,
                LineTotalCents = checked(line.GrossCents - discountCents)
            });
        });
    }

    public Result<PosCheckoutDraftTotals> SetSaleDiscount(long saleDiscountCents)
    {
        if (saleDiscountCents < 0)
        {
            return Result<PosCheckoutDraftTotals>.Fail(Error.Validation(
                "CheckoutDraft.InvalidMoneyAmount",
                "Checkout draft money amounts cannot be negative."));
        }

        long subtotalCents = CalculateSubtotalCents();

        if (saleDiscountCents > subtotalCents)
        {
            return Result<PosCheckoutDraftTotals>.Fail(Error.Validation(
                "CheckoutDraft.SaleDiscountExceedsSubtotal",
                "Checkout draft sale discount cannot exceed subtotal."));
        }

        _saleDiscountCents = saleDiscountCents;

        return GetTotals();
    }

    public Result<PosCheckoutDraftTotals> SetTax(long taxCents)
    {
        if (taxCents < 0)
        {
            return Result<PosCheckoutDraftTotals>.Fail(Error.Validation(
                "CheckoutDraft.InvalidMoneyAmount",
                "Checkout draft money amounts cannot be negative."));
        }

        _taxCents = taxCents;

        return GetTotals();
    }

    public Result<PosCheckoutDraftPayment> AddPayment(
        PaymentMethod method,
        long amountCents)
    {
        if (!Enum.IsDefined(typeof(PaymentMethod), method))
        {
            return Result<PosCheckoutDraftPayment>.Fail(Error.Validation(
                "CheckoutDraft.InvalidPaymentMethod",
                "Checkout draft payment method is invalid."));
        }

        if (amountCents <= 0)
        {
            return Result<PosCheckoutDraftPayment>.Fail(Error.Validation(
                "CheckoutDraft.PaymentAmountRequired",
                "Checkout draft payment amount must be greater than zero."));
        }

        PosCheckoutDraftPayment payment = new(
            Guid.NewGuid(),
            method,
            amountCents);

        _payments.Add(payment);

        return Result<PosCheckoutDraftPayment>.Success(payment);
    }

    public Result ClearPayments()
    {
        _payments.Clear();
        return Result.Success();
    }

    public Result<PosCheckoutDraftTotals> GetTotals()
    {
        try
        {
            return Result<PosCheckoutDraftTotals>.Success(CalculateTotals());
        }
        catch (OverflowException)
        {
            return Result<PosCheckoutDraftTotals>.Fail(AmountOverflow());
        }
    }

    public Result<PosCheckoutDraftTransaction> GetTransaction()
    {
        try
        {
            PosCheckoutDraftTransaction transaction = new(
                _cashierUserId,
                _cashierSessionId,
                _receiptNumber,
                _lines.ToArray(),
                _payments.ToArray(),
                CalculateTotals(),
                _receiptSnapshot);

            return Result<PosCheckoutDraftTransaction>.Success(transaction);
        }
        catch (OverflowException)
        {
            return Result<PosCheckoutDraftTransaction>.Fail(AmountOverflow());
        }
    }

    public Result<PosCheckoutApplicationRequest> BuildApplicationRequest()
    {
        if (_cashierUserId == Guid.Empty)
        {
            return Result<PosCheckoutApplicationRequest>.Fail(Error.Validation(
                "CheckoutDraft.InvalidCashierUserId",
                "Checkout draft cashier user ID is required."));
        }

        if (!_cashierSessionId.HasValue || _cashierSessionId.GetValueOrDefault() == Guid.Empty)
        {
            return Result<PosCheckoutApplicationRequest>.Fail(Error.Validation(
                "CheckoutDraft.InvalidCashierSessionId",
                "Checkout draft cashier session ID is required."));
        }

        if (string.IsNullOrWhiteSpace(_receiptNumber))
        {
            return Result<PosCheckoutApplicationRequest>.Fail(Error.Validation(
                "CheckoutDraft.ReceiptNumberRequired",
                "Checkout draft receipt number is required."));
        }

        if (_lines.Count == 0)
        {
            return Result<PosCheckoutApplicationRequest>.Fail(Error.Validation(
                "CheckoutDraft.LinesRequired",
                "At least one checkout draft line is required."));
        }

        if (_payments.Count == 0)
        {
            return Result<PosCheckoutApplicationRequest>.Fail(Error.Validation(
                "CheckoutDraft.PaymentsRequired",
                "At least one checkout draft payment is required."));
        }

        try
        {
            PosCheckoutDraftTotals totals = CalculateTotals();

            if (totals.TotalCents < 0)
            {
                return Result<PosCheckoutApplicationRequest>.Fail(Error.Validation(
                    "CheckoutDraft.TotalCannotBeNegative",
                    "Checkout draft total cannot be negative."));
            }

            if (totals.AmountPaidCents < totals.TotalCents)
            {
                return Result<PosCheckoutApplicationRequest>.Fail(Error.Validation(
                    "CheckoutDraft.InsufficientPayment",
                    "Checkout draft payment cannot be less than the sale total."));
            }

            PosCheckoutApplicationRequest request = new(
                _cashierUserId,
                _cashierSessionId.GetValueOrDefault(),
                _receiptNumber.Trim(),
                _lines.Select(line => new PosCheckoutLineInput(
                    line.ProductId,
                    line.ProductNameSnapshot,
                    line.BarcodeSnapshot,
                    line.UnitNameSnapshot,
                    line.QuantityX1000,
                    line.UnitPriceCents,
                    line.CostPriceCents,
                    line.DiscountCents,
                    line.TrackStock)).ToArray(),
                _payments.Select(payment => new PosCheckoutPaymentInput(
                    payment.Method,
                    payment.AmountCents)).ToArray(),
                _saleDiscountCents,
                _taxCents,
                _receiptSnapshot);

            return Result<PosCheckoutApplicationRequest>.Success(request);
        }
        catch (OverflowException)
        {
            return Result<PosCheckoutApplicationRequest>.Fail(AmountOverflow());
        }
    }

    private Result<PosCheckoutDraftLine> UpdateLine(
        Guid lineId,
        Func<PosCheckoutDraftLine, Result<PosCheckoutDraftLine>> update)
    {
        int index = FindLineIndex(lineId);

        if (index < 0)
        {
            return Result<PosCheckoutDraftLine>.Fail(LineNotFound());
        }

        Result<PosCheckoutDraftLine> result = update(_lines[index]);

        if (result.IsFailure)
        {
            return result;
        }

        _lines[index] = result.Value;

        if (_saleDiscountCents > CalculateSubtotalCents())
        {
            _saleDiscountCents = CalculateSubtotalCents();
        }

        return result;
    }

    private static Result ValidateLineInput(PosCheckoutDraftLineInput input)
    {
        if (input is null)
        {
            return Result.Fail(Error.Validation(
                "CheckoutDraft.LineRequired",
                "Checkout draft line is required."));
        }

        if (input.ProductId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "CheckoutDraft.InvalidProductId",
                "Checkout draft line product ID is required."));
        }

        if (string.IsNullOrWhiteSpace(input.ProductNameSnapshot))
        {
            return Result.Fail(Error.Validation(
                "CheckoutDraft.ProductNameRequired",
                "Checkout draft line product name is required."));
        }

        if (string.IsNullOrWhiteSpace(input.UnitNameSnapshot))
        {
            return Result.Fail(Error.Validation(
                "CheckoutDraft.UnitNameRequired",
                "Checkout draft line unit name is required."));
        }

        if (input.QuantityX1000 <= 0)
        {
            return Result.Fail(Error.Validation(
                "CheckoutDraft.InvalidQuantity",
                "Checkout draft line quantity must be greater than zero."));
        }

        if (input.UnitPriceCents < 0 ||
            input.DiscountCents < 0 ||
            input.CostPriceCents < 0)
        {
            return Result.Fail(Error.Validation(
                "CheckoutDraft.InvalidMoneyAmount",
                "Checkout draft money amounts cannot be negative."));
        }

        return Result.Success();
    }

    private int FindLineIndex(Guid lineId)
    {
        return _lines.FindIndex(line => line.Id == lineId);
    }

    private long CalculateSubtotalCents()
    {
        long subtotalCents = 0;

        foreach (PosCheckoutDraftLine line in _lines)
        {
            subtotalCents = checked(subtotalCents + line.LineTotalCents);
        }

        return subtotalCents;
    }

    private long CalculateAmountPaidCents()
    {
        long amountPaidCents = 0;

        foreach (PosCheckoutDraftPayment payment in _payments)
        {
            amountPaidCents = checked(amountPaidCents + payment.AmountCents);
        }

        return amountPaidCents;
    }

    private PosCheckoutDraftTotals CalculateTotals()
    {
        long subtotalCents = CalculateSubtotalCents();
        long amountPaidCents = CalculateAmountPaidCents();
        long totalCents = checked(subtotalCents - _saleDiscountCents + _taxCents);

        long changeCents = amountPaidCents >= totalCents
            ? checked(amountPaidCents - totalCents)
            : 0;

        long balanceDueCents = amountPaidCents < totalCents
            ? checked(totalCents - amountPaidCents)
            : 0;

        return new PosCheckoutDraftTotals(
            subtotalCents,
            _saleDiscountCents,
            _taxCents,
            totalCents,
            amountPaidCents,
            changeCents,
            balanceDueCents);
    }

    private static long CalculateLineGrossCents(
        long unitPriceCents,
        long quantityX1000)
    {
        decimal gross = unitPriceCents * (quantityX1000 / 1000m);

        return checked((long)Math.Round(
            gross,
            0,
            MidpointRounding.AwayFromZero));
    }

    private static Error LineNotFound()
    {
        return Error.NotFound(
            "CheckoutDraft.LineNotFound",
            "Checkout draft line was not found.");
    }

    private static Error AmountOverflow()
    {
        return Error.Validation(
            "CheckoutDraft.AmountOverflow",
            "Checkout draft amount calculation is too large.");
    }
}



