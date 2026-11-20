using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Salryn.Sync.Contracts;

public static class SaleCommittedV3Contract
{
    public const string OperationType = "SaleCommittedV3";
    public const int SchemaVersion = 3;
}

public sealed record SaleCommittedV3DepositAllocation(
    Guid ReceivedDepositFactId,
    long AmountCents);

public sealed record SaleCommittedV3(
    string OperationType,
    SaleCommittedV1 SaleEvidence,
    Guid FinancialOperationId,
    Guid FinancialDecisionId,
    Guid OrderId,
    Guid OrderRevisionId,
    int ExpectedOrderVersion,
    DateTimeOffset FinancialDecisionCreatedAtUtc,
    string FinancialDecisionHash,
    string? DiscountReason,
    long FeeTotalCents,
    IReadOnlyList<SaleCommittedV2LineDiscount> LineDiscounts,
    IReadOnlyList<SaleCommittedV2Fee> Fees,
    Guid BookingId,
    Guid BookingRevisionId,
    Guid BookingOrderLinkId,
    int ExpectedBookingVersion,
    int ResultingBookingVersion,
    Guid BookingSaleLinkId,
    long BookingDepositAppliedCents,
    Guid? DepositApplicationFactId,
    IReadOnlyList<SaleCommittedV3DepositAllocation> DepositAllocations,
    string FinalBookingStatus,
    string FinalFulfillmentState);

public sealed record SaleCommittedV3ParseResult(
    bool IsValid,
    SaleCommittedV3? Payload,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static SaleCommittedV3ParseResult Success(SaleCommittedV3 payload) =>
        new(true, payload, null, null);

    public static SaleCommittedV3ParseResult Fail(string code, string message) =>
        new(false, null, code, message);
}

public static class SaleCommittedV3CanonicalJson
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string Serialize(SaleCommittedV3 payload) =>
        JsonSerializer.Serialize(Normalize(payload), Json);

    public static string ComputeHash(string canonicalJson) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)))
        .ToLowerInvariant();

    public static SaleCommittedV3ParseResult Parse(string canonicalJson)
    {
        try
        {
            SaleCommittedV3? payload =
                JsonSerializer.Deserialize<SaleCommittedV3>(
                    canonicalJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (payload is null)
                return SaleCommittedV3ParseResult.Fail(
                    "SaleCommittedV3.PayloadMissing",
                    "SaleCommittedV3 payload is missing.");

            SaleCommittedV3 normalized = Normalize(payload);
            string? error = SaleCommittedV3Validator.Validate(normalized);
            if (error is not null)
                return SaleCommittedV3ParseResult.Fail(
                    "SaleCommittedV3.PayloadInvalid",
                    error);

            if (!string.Equals(Serialize(normalized), canonicalJson, StringComparison.Ordinal))
                return SaleCommittedV3ParseResult.Fail(
                    "SaleCommittedV3.PayloadNotCanonical",
                    "SaleCommittedV3 payload is not canonical JSON.");

            return SaleCommittedV3ParseResult.Success(normalized);
        }
        catch (Exception ex) when (
            ex is JsonException or FormatException or OverflowException or ArgumentException)
        {
            return SaleCommittedV3ParseResult.Fail(
                "SaleCommittedV3.PayloadInvalidJson",
                ex.Message);
        }
    }

    private static SaleCommittedV3 Normalize(SaleCommittedV3 p) =>
        p with
        {
            OperationType = (p.OperationType ?? string.Empty).Trim(),
            FinancialDecisionCreatedAtUtc =
                p.FinancialDecisionCreatedAtUtc.ToUniversalTime(),
            FinancialDecisionHash =
                (p.FinancialDecisionHash ?? string.Empty).Trim().ToLowerInvariant(),
            DiscountReason = Clean(p.DiscountReason),
            FinalBookingStatus = (p.FinalBookingStatus ?? string.Empty).Trim(),
            FinalFulfillmentState = (p.FinalFulfillmentState ?? string.Empty).Trim(),
            SaleEvidence = Normalize(p.SaleEvidence),
            LineDiscounts = (p.LineDiscounts ?? Array.Empty<SaleCommittedV2LineDiscount>())
                .OrderBy(x => x.SaleItemId)
                .ToArray(),
            Fees = (p.Fees ?? Array.Empty<SaleCommittedV2Fee>())
                .Select(x => x with
                {
                    FeeType = (x.FeeType ?? string.Empty).Trim(),
                    DisplayLabelSnapshot = (x.DisplayLabelSnapshot ?? string.Empty).Trim(),
                    Reason = Clean(x.Reason),
                    CreatedAtUtc = x.CreatedAtUtc.ToUniversalTime()
                })
                .OrderBy(x => x.FeeId)
                .ToArray(),
            DepositAllocations = (p.DepositAllocations ??
                Array.Empty<SaleCommittedV3DepositAllocation>())
                .OrderBy(x => x.ReceivedDepositFactId)
                .ToArray()
        };

    private static SaleCommittedV1 Normalize(SaleCommittedV1 p) =>
        new(
            p.Sale with
            {
                ReceiptNumber = (p.Sale.ReceiptNumber ?? string.Empty).Trim(),
                SaleDate = p.Sale.SaleDate.ToUniversalTime(),
                PaymentStatus = (p.Sale.PaymentStatus ?? string.Empty).Trim(),
                ReceivableNote = Clean(p.Sale.ReceivableNote),
                ReceiptSnapshot = Clean(p.Sale.ReceiptSnapshot),
                CreatedAt = p.Sale.CreatedAt.ToUniversalTime()
            },
            (p.Items ?? Array.Empty<SaleCommittedV1Item>())
                .Select(x => x with
                {
                    ProductNameSnapshot = (x.ProductNameSnapshot ?? string.Empty).Trim(),
                    BarcodeSnapshot = Clean(x.BarcodeSnapshot),
                    UnitNameSnapshot = (x.UnitNameSnapshot ?? string.Empty).Trim()
                })
                .OrderBy(x => x.Id)
                .ToArray(),
            (p.Payments ?? Array.Empty<SaleCommittedV1Payment>())
                .Select(x => x with
                {
                    Method = (x.Method ?? string.Empty).Trim(),
                    PaidAt = x.PaidAt.ToUniversalTime()
                })
                .OrderBy(x => x.Id)
                .ToArray(),
            (p.StockMovements ?? Array.Empty<SaleCommittedV1StockMovement>())
                .Select(x => x with
                {
                    MovementType = (x.MovementType ?? string.Empty).Trim(),
                    ReferenceType = (x.ReferenceType ?? string.Empty).Trim(),
                    Reason = Clean(x.Reason),
                    CreatedAt = x.CreatedAt.ToUniversalTime()
                })
                .OrderBy(x => x.Id)
                .ToArray());

    private static string? Clean(string? value)
    {
        string v = (value ?? string.Empty).Trim();
        return v.Length == 0 ? null : v;
    }
}

public static class SaleCommittedV3Validator
{
    public static string? Validate(SaleCommittedV3 p)
    {
        if (!string.Equals(p.OperationType, SaleCommittedV3Contract.OperationType, StringComparison.Ordinal))
            return "OperationType must be SaleCommittedV3.";

        if (p.SaleEvidence is null || p.SaleEvidence.Sale is null)
            return "Sale evidence is required.";

        SaleCommittedV1Sale sale = p.SaleEvidence.Sale;
        if (sale.Id == Guid.Empty ||
            sale.CashierUserId == Guid.Empty ||
            sale.CashierSessionId == Guid.Empty ||
            p.FinancialOperationId == Guid.Empty ||
            p.FinancialDecisionId == Guid.Empty ||
            p.OrderId == Guid.Empty ||
            p.OrderRevisionId == Guid.Empty ||
            p.ExpectedOrderVersion <= 0 ||
            p.BookingId == Guid.Empty ||
            p.BookingRevisionId == Guid.Empty ||
            p.BookingOrderLinkId == Guid.Empty ||
            p.BookingSaleLinkId == Guid.Empty ||
            p.ExpectedBookingVersion <= 0 ||
            p.ResultingBookingVersion != checked(p.ExpectedBookingVersion + 1))
            return "SaleCommittedV3 required identities/version authority is invalid.";

        if (!string.Equals(p.FinalBookingStatus, "Fulfilled", StringComparison.Ordinal) ||
            !string.Equals(p.FinalFulfillmentState, "SaleBound", StringComparison.Ordinal))
            return "SaleCommittedV3 final Booking state must be Fulfilled/SaleBound.";

        if (p.FinancialDecisionHash.Length != 64 ||
            !p.FinancialDecisionHash.All(Uri.IsHexDigit))
            return "Financial decision hash must be hexadecimal SHA-256.";

        if (sale.SubtotalCents < 0 || sale.DiscountCents < 0 ||
            sale.TaxCents < 0 || p.FeeTotalCents < 0 ||
            sale.TotalCents < 0 || sale.AmountPaidCents < 0 ||
            sale.ChangeCents < 0 || sale.BalanceDueCents < 0 ||
            p.BookingDepositAppliedCents < 0)
            return "SaleCommittedV3 money values cannot be negative.";

        long expectedTotal = checked(
            sale.SubtotalCents - sale.DiscountCents +
            sale.TaxCents + p.FeeTotalCents);
        if (expectedTotal < 0 || expectedTotal != sale.TotalCents)
            return "Sale total does not reconcile with V3 fee/discount evidence.";

        if (p.SaleEvidence.Items is null || p.SaleEvidence.Items.Count == 0)
            return "SaleCommittedV3 requires at least one SaleItem.";

        HashSet<Guid> itemIds = new();
        long grossSubtotal = 0;
        long discountTotal = 0;
        foreach (SaleCommittedV1Item item in p.SaleEvidence.Items)
        {
            if (item.Id == Guid.Empty || item.SaleId != sale.Id ||
                item.ProductId == Guid.Empty || !itemIds.Add(item.Id) ||
                item.QuantityX1000 <= 0 || item.UnitPriceCents < 0 ||
                item.DiscountCents < 0 || item.LineTotalCents < 0)
                return "SaleCommittedV3 SaleItem evidence is invalid.";

            long gross = checked((long)Math.Round(
                item.UnitPriceCents * (item.QuantityX1000 / 1000m),
                0,
                MidpointRounding.AwayFromZero));
            if (item.DiscountCents > gross ||
                item.LineTotalCents != gross - item.DiscountCents)
                return "SaleCommittedV3 SaleItem arithmetic is invalid.";

            grossSubtotal = checked(grossSubtotal + gross);
            discountTotal = checked(discountTotal + item.DiscountCents);
        }

        if (grossSubtotal != sale.SubtotalCents ||
            discountTotal != sale.DiscountCents)
            return "SaleCommittedV3 item totals do not reconcile.";

        Dictionary<Guid,SaleCommittedV2LineDiscount> lineDiscounts;
        try
        {
            lineDiscounts = p.LineDiscounts.ToDictionary(x => x.SaleItemId);
        }
        catch (ArgumentException)
        {
            return "SaleCommittedV3 line discounts require unique SaleItemId.";
        }

        if (lineDiscounts.Count != p.SaleEvidence.Items.Count)
            return "Every V3 SaleItem requires financial line discount evidence.";

        foreach (SaleCommittedV1Item item in p.SaleEvidence.Items)
        {
            if (!lineDiscounts.TryGetValue(item.Id, out SaleCommittedV2LineDiscount? d) ||
                d.LineDiscountCents < 0 ||
                d.OrderDiscountAllocatedCents < 0 ||
                d.TotalDiscountCents != checked(d.LineDiscountCents + d.OrderDiscountAllocatedCents) ||
                d.TotalDiscountCents != item.DiscountCents)
                return "SaleCommittedV3 line discount evidence is inconsistent.";
        }

        long fees = 0;
        HashSet<Guid> feeIds = new();
        foreach (SaleCommittedV2Fee fee in p.Fees)
        {
            if (fee.FeeId == Guid.Empty || !feeIds.Add(fee.FeeId) ||
                fee.ActorUserId == Guid.Empty || fee.AmountCents < 0 ||
                string.IsNullOrWhiteSpace(fee.DisplayLabelSnapshot) ||
                fee.FeeType is not ("Service" or "Delivery"))
                return "SaleCommittedV3 fee evidence is invalid.";
            fees = checked(fees + fee.AmountCents);
        }
        if (fees != p.FeeTotalCents)
            return "SaleCommittedV3 fee total mismatch.";

        long paymentTotal = 0;
        long arTotal = 0;
        HashSet<Guid> paymentIds = new();
        foreach (SaleCommittedV1Payment payment in p.SaleEvidence.Payments ??
            Array.Empty<SaleCommittedV1Payment>())
        {
            if (payment.Id == Guid.Empty || payment.SaleId != sale.Id ||
                !paymentIds.Add(payment.Id) || payment.AmountCents <= 0 ||
                payment.Method is not (
                    SaleCommittedV1Contract.PaymentCash or
                    SaleCommittedV1Contract.PaymentAccountsReceivable))
                return "SaleCommittedV3 payment evidence is invalid.";

            paymentTotal = checked(paymentTotal + payment.AmountCents);
            if (payment.Method == SaleCommittedV1Contract.PaymentAccountsReceivable)
                arTotal = checked(arTotal + payment.AmountCents);
        }

        if (checked(paymentTotal + p.BookingDepositAppliedCents) != sale.AmountPaidCents)
            return "Cash/AR payment rows plus Booking deposit must equal Sale.AmountPaid.";

        if (arTotal != sale.BalanceDueCents)
            return "AR payment total must equal Sale.BalanceDue.";

        if (checked(arTotal + p.BookingDepositAppliedCents) > sale.TotalCents)
            return "AR plus Booking deposit application cannot exceed Sale total.";

        if (sale.AmountPaidCents < sale.TotalCents ||
            sale.ChangeCents != sale.AmountPaidCents - sale.TotalCents)
            return "SaleCommittedV3 payment/change arithmetic is invalid.";

        if (p.BookingDepositAppliedCents == 0)
        {
            if (p.DepositApplicationFactId.HasValue ||
                p.DepositAllocations.Count != 0)
                return "Zero Booking deposit application requires null fact and empty allocations.";
        }
        else
        {
            if (!p.DepositApplicationFactId.HasValue ||
                p.DepositApplicationFactId.Value == Guid.Empty ||
                p.DepositAllocations.Count == 0 ||
                p.DepositAllocations.Any(x =>
                    x.ReceivedDepositFactId == Guid.Empty ||
                    x.AmountCents <= 0) ||
                p.DepositAllocations.Select(x => x.ReceivedDepositFactId).Distinct().Count() !=
                    p.DepositAllocations.Count ||
                p.DepositAllocations.Sum(x => x.AmountCents) !=
                    p.BookingDepositAppliedCents)
                return "Positive Booking deposit requires exact immutable allocation evidence.";
        }

        return null;
    }
}
