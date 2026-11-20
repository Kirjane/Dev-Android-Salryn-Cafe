namespace Salryn.Sync.Contracts;

public sealed record SaleCommittedV2ValidationResult(
    bool IsValid,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static SaleCommittedV2ValidationResult Success() => new(true, null, null);
    public static SaleCommittedV2ValidationResult Fail(string code, string message) => new(false, code, message);
}

public static class SaleCommittedV2Validator
{
    public static SaleCommittedV2ValidationResult Validate(SaleCommittedV2 payload)
    {
        if (payload is null || payload.SaleEvidence is null || payload.SaleEvidence.Sale is null)
            return Fail("SaleCommittedV2.SaleRequired", "Committed sale evidence is required.");

        SaleCommittedV1 salePayload = payload.SaleEvidence;
        SaleCommittedV1Sale sale = salePayload.Sale;

        if (sale.Id == Guid.Empty || sale.CashierUserId == Guid.Empty ||
            sale.CashierSessionId == Guid.Empty ||
            payload.FinancialOperationId == Guid.Empty ||
            payload.FinancialDecisionId == Guid.Empty)
            return Fail("SaleCommittedV2.RequiredIdentityMissing", "Sale, cashier, session, financial operation and financial decision identities are required.");

        bool hasOrder = payload.OrderId.HasValue;
        bool hasRevision = payload.OrderRevisionId.HasValue;
        bool hasVersion = payload.ExpectedOrderVersion.HasValue;
        if (!(hasOrder == hasRevision && hasRevision == hasVersion))
            return Fail("SaleCommittedV2.IncompleteOrderBinding", "Order ID, order revision ID and expected version must be supplied together.");
        if (payload.OrderId == Guid.Empty ||
            payload.OrderRevisionId == Guid.Empty ||
            payload.ExpectedOrderVersion < 0)
            return Fail("SaleCommittedV2.InvalidOrderBinding", "C5 order binding contains an invalid identity or version.");

        if (payload.FinancialDecisionHash is null || payload.FinancialDecisionHash.Length != 64 ||
            !payload.FinancialDecisionHash.All(Uri.IsHexDigit))
            return Fail("SaleCommittedV2.FinancialDecisionHashInvalid", "Financial decision hash must be a 64-character hexadecimal SHA-256 value.");

        if (sale.SubtotalCents < 0 || sale.DiscountCents < 0 || sale.TaxCents < 0 ||
            payload.FeeTotalCents < 0 || sale.TotalCents < 0 || sale.AmountPaidCents < 0 ||
            sale.ChangeCents < 0 || sale.BalanceDueCents < 0)
            return Fail("SaleCommittedV2.InvalidMoney", "Sale money values cannot be negative.");

        long expectedTotal;
        try { expectedTotal = checked(sale.SubtotalCents - sale.DiscountCents + sale.TaxCents + payload.FeeTotalCents); }
        catch (OverflowException) { return Fail("SaleCommittedV2.AmountOverflow", "Sale arithmetic exceeds supported integer range."); }

        if (expectedTotal < 0 || expectedTotal != sale.TotalCents)
            return Fail("SaleCommittedV2.TotalMismatch", "Sale total does not reconcile with C5 financial evidence.");

        if (sale.AmountPaidCents < sale.TotalCents || sale.ChangeCents != sale.AmountPaidCents - sale.TotalCents)
            return Fail("SaleCommittedV2.PaymentArithmeticMismatch", "Amount paid and change do not reconcile to the sale total.");

        if (salePayload.Items is null || salePayload.Items.Count == 0)
            return Fail("SaleCommittedV2.ItemsRequired", "At least one committed sale item is required.");

        Dictionary<Guid, SaleCommittedV2LineDiscount> discountByItem;
        try { discountByItem = payload.LineDiscounts.ToDictionary(x => x.SaleItemId); }
        catch (ArgumentException) { return Fail("SaleCommittedV2.DuplicateLineDiscount", "Line discount evidence must have unique SaleItem IDs."); }

        long grossSubtotal = 0;
        long discountTotal = 0;
        foreach (SaleCommittedV1Item item in salePayload.Items)
        {
            if (!discountByItem.TryGetValue(item.Id, out SaleCommittedV2LineDiscount? line))
                return Fail("SaleCommittedV2.LineDiscountMissing", "Every SaleItem requires C5 discount evidence.");

            if (line.LineDiscountCents < 0 || line.OrderDiscountAllocatedCents < 0 ||
                line.TotalDiscountCents != checked(line.LineDiscountCents + line.OrderDiscountAllocatedCents) ||
                line.TotalDiscountCents != item.DiscountCents)
                return Fail("SaleCommittedV2.LineDiscountMismatch", "SaleItem discount evidence is inconsistent.");

            try
            {
                grossSubtotal = checked(grossSubtotal + item.LineTotalCents + item.DiscountCents);
                discountTotal = checked(discountTotal + item.DiscountCents);
            }
            catch (OverflowException) { return Fail("SaleCommittedV2.AmountOverflow", "Sale item totals exceed supported integer range."); }
        }

        if (discountByItem.Count != salePayload.Items.Count ||
            grossSubtotal != sale.SubtotalCents || discountTotal != sale.DiscountCents)
            return Fail("SaleCommittedV2.DiscountTotalMismatch", "Committed line discounts do not reconcile to Sale totals.");

        long feeTotal = 0;
        HashSet<Guid> feeIds = new();
        foreach (SaleCommittedV2Fee fee in payload.Fees)
        {
            if (fee.FeeId == Guid.Empty || !feeIds.Add(fee.FeeId) ||
                !(string.Equals(fee.FeeType, "Service", StringComparison.Ordinal) || string.Equals(fee.FeeType, "Delivery", StringComparison.Ordinal)) ||
                string.IsNullOrWhiteSpace(fee.DisplayLabelSnapshot) ||
                fee.AmountCents < 0 || fee.ActorUserId == Guid.Empty)
                return Fail("SaleCommittedV2.FeeInvalid", "C5 fee evidence is invalid.");

            try { feeTotal = checked(feeTotal + fee.AmountCents); }
            catch (OverflowException) { return Fail("SaleCommittedV2.AmountOverflow", "Fee total exceeds supported integer range."); }
        }

        if (feeTotal != payload.FeeTotalCents)
            return Fail("SaleCommittedV2.FeeTotalMismatch", "Fee lines must equal the committed fee total.");

        if (salePayload.Payments is null || salePayload.Payments.Count == 0)
            return Fail("SaleCommittedV2.PaymentsRequired", "At least one committed payment is required.");

        long paymentTotal = 0;
        long receivableTotal = 0;
        HashSet<Guid> paymentIds = new();
        foreach (SaleCommittedV1Payment payment in salePayload.Payments)
        {
            if (payment.Id == Guid.Empty || payment.SaleId != sale.Id || !paymentIds.Add(payment.Id) ||
                payment.AmountCents <= 0 ||
                !(string.Equals(payment.Method, SaleCommittedV1Contract.PaymentCash, StringComparison.Ordinal) ||
                  string.Equals(payment.Method, SaleCommittedV1Contract.PaymentAccountsReceivable, StringComparison.Ordinal)))
                return Fail("SaleCommittedV2.PaymentInvalid", "Committed payment evidence is invalid.");

            try
            {
                paymentTotal = checked(paymentTotal + payment.AmountCents);
                if (string.Equals(payment.Method, SaleCommittedV1Contract.PaymentAccountsReceivable, StringComparison.Ordinal))
                    receivableTotal = checked(receivableTotal + payment.AmountCents);
            }
            catch (OverflowException) { return Fail("SaleCommittedV2.AmountOverflow", "Payment totals exceed supported integer range."); }
        }

        if (paymentTotal != sale.AmountPaidCents || receivableTotal != sale.BalanceDueCents)
            return Fail("SaleCommittedV2.PaymentTotalMismatch", "Payment evidence does not reconcile to the Sale.");

        if (receivableTotal > 0 && (!sale.CustomerId.HasValue || sale.CustomerId == Guid.Empty))
            return Fail("SaleCommittedV2.CustomerRequired", "AccountsReceivable requires a customer.");

        return SaleCommittedV2ValidationResult.Success();
    }

    private static SaleCommittedV2ValidationResult Fail(string code, string message) =>
        SaleCommittedV2ValidationResult.Fail(code, message);
}
