namespace Salryn.Sync.Contracts;

public sealed record SaleCommittedV1ValidationResult(
    bool IsValid,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static SaleCommittedV1ValidationResult Success() => new(true, null, null);

    public static SaleCommittedV1ValidationResult Fail(string code, string message) =>
        new(false, code, message);
}

public static class SaleCommittedV1Validator
{
    public static SaleCommittedV1ValidationResult Validate(SaleCommittedV1 payload)
    {
        if (payload is null || payload.Sale is null)
        {
            return Fail("SaleCommittedV1.SaleRequired", "Committed sale payload is required.");
        }

        SaleCommittedV1Sale sale = payload.Sale;
        if (sale.Id == Guid.Empty ||
            sale.CashierUserId == Guid.Empty ||
            sale.CashierSessionId == Guid.Empty)
        {
            return Fail(
                "SaleCommittedV1.RequiredSaleIdentityMissing",
                "Sale, cashier user and cashier session IDs are required.");
        }

        if (string.IsNullOrWhiteSpace(sale.ReceiptNumber))
        {
            return Fail("SaleCommittedV1.ReceiptNumberRequired", "Receipt number is required.");
        }

        if (sale.SubtotalCents < 0 ||
            sale.DiscountCents < 0 ||
            sale.TaxCents < 0 ||
            sale.TotalCents < 0 ||
            sale.AmountPaidCents < 0 ||
            sale.ChangeCents < 0 ||
            sale.BalanceDueCents < 0)
        {
            return Fail("SaleCommittedV1.InvalidMoney", "Sale money values cannot be negative.");
        }

        long expectedTotal;
        try
        {
            expectedTotal = checked(sale.SubtotalCents - sale.DiscountCents + sale.TaxCents);
        }
        catch (OverflowException)
        {
            return Fail("SaleCommittedV1.AmountOverflow", "Sale arithmetic exceeds supported integer range.");
        }

        if (expectedTotal < 0 || expectedTotal != sale.TotalCents)
        {
            return Fail(
                "SaleCommittedV1.TotalMismatch",
                "Sale total must equal subtotal minus discount plus tax.");
        }

        if (sale.AmountPaidCents < sale.TotalCents ||
            sale.ChangeCents != sale.AmountPaidCents - sale.TotalCents)
        {
            return Fail(
                "SaleCommittedV1.PaymentArithmeticMismatch",
                "Amount paid and change do not reconcile to the sale total.");
        }

        if (payload.Items is null || payload.Items.Count == 0)
        {
            return Fail("SaleCommittedV1.ItemsRequired", "At least one committed sale item is required.");
        }

        if (payload.Payments is null || payload.Payments.Count == 0)
        {
            return Fail("SaleCommittedV1.PaymentsRequired", "At least one committed payment is required.");
        }

        HashSet<Guid> itemIds = new();
        long subtotal = 0;
        foreach (SaleCommittedV1Item item in payload.Items)
        {
            SaleCommittedV1ValidationResult itemResult = ValidateItem(item, sale.Id);
            if (!itemResult.IsValid)
            {
                return itemResult;
            }

            if (!itemIds.Add(item.Id))
            {
                return Fail("SaleCommittedV1.DuplicateItemId", "Sale item IDs must be unique.");
            }

            try
            {
                subtotal = checked(subtotal + item.LineTotalCents);
            }
            catch (OverflowException)
            {
                return Fail("SaleCommittedV1.AmountOverflow", "Item subtotal exceeds supported integer range.");
            }
        }

        if (subtotal != sale.SubtotalCents)
        {
            return Fail("SaleCommittedV1.SubtotalMismatch", "Sale subtotal must equal item line totals.");
        }

        HashSet<Guid> paymentIds = new();
        long paymentTotal = 0;
        long cashTotal = 0;
        long receivableTotal = 0;

        foreach (SaleCommittedV1Payment payment in payload.Payments)
        {
            SaleCommittedV1ValidationResult paymentResult = ValidatePayment(payment, sale.Id);
            if (!paymentResult.IsValid)
            {
                return paymentResult;
            }

            if (!paymentIds.Add(payment.Id))
            {
                return Fail("SaleCommittedV1.DuplicatePaymentId", "Payment IDs must be unique.");
            }

            try
            {
                paymentTotal = checked(paymentTotal + payment.AmountCents);
                if (string.Equals(payment.Method, SaleCommittedV1Contract.PaymentCash, StringComparison.Ordinal))
                {
                    cashTotal = checked(cashTotal + payment.AmountCents);
                }
                else
                {
                    receivableTotal = checked(receivableTotal + payment.AmountCents);
                }
            }
            catch (OverflowException)
            {
                return Fail("SaleCommittedV1.AmountOverflow", "Payment totals exceed supported integer range.");
            }
        }

        if (paymentTotal != sale.AmountPaidCents)
        {
            return Fail(
                "SaleCommittedV1.PaymentTotalMismatch",
                "Committed payments must equal sale amount paid.");
        }

        if (receivableTotal != sale.BalanceDueCents)
        {
            return Fail(
                "SaleCommittedV1.ReceivableBalanceMismatch",
                "AccountsReceivable payment amount must equal the committed balance due.");
        }

        if (receivableTotal > 0 &&
            (!sale.CustomerId.HasValue || sale.CustomerId.Value == Guid.Empty))
        {
            return Fail(
                "SaleCommittedV1.CustomerRequiredForReceivable",
                "A customer is required for an accounts receivable balance.");
        }

        if (receivableTotal > 0 && sale.ChangeCents != 0)
        {
            return Fail(
                "SaleCommittedV1.ReceivableCannotCarryChange",
                "Cash change is not valid when the committed sale includes accounts receivable.");
        }

        long retainedCash = cashTotal - sale.ChangeCents;
        if (retainedCash < 0)
        {
            return Fail(
                "SaleCommittedV1.ChangeExceedsCash",
                "Committed change cannot exceed physical cash tender.");
        }

        string expectedStatus = receivableTotal == 0
            ? SaleCommittedV1Contract.PaymentStatusPaid
            : retainedCash == 0
                ? SaleCommittedV1Contract.PaymentStatusCredit
                : SaleCommittedV1Contract.PaymentStatusPartial;

        if (!string.Equals(sale.PaymentStatus, expectedStatus, StringComparison.Ordinal))
        {
            return Fail(
                "SaleCommittedV1.PaymentStatusMismatch",
                "Payment status does not match committed Cash/AR composition.");
        }

        HashSet<Guid> movementIds = new();
        HashSet<Guid> itemProductIds = payload.Items.Select(item => item.ProductId).ToHashSet();
        foreach (SaleCommittedV1StockMovement movement in payload.StockMovements)
        {
            if (movement.Id == Guid.Empty ||
                movement.ProductId == Guid.Empty ||
                movement.ReferenceId == Guid.Empty ||
                movement.UserId == Guid.Empty)
            {
                return Fail(
                    "SaleCommittedV1.InvalidStockIdentity",
                    "Stock movement identities must be non-empty.");
            }

            if (!movementIds.Add(movement.Id))
            {
                return Fail("SaleCommittedV1.DuplicateStockMovementId", "Stock movement IDs must be unique.");
            }

            if (!string.Equals(
                    movement.MovementType,
                    SaleCommittedV1Contract.StockMovementSale,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    movement.ReferenceType,
                    SaleCommittedV1Contract.StockReferenceSale,
                    StringComparison.Ordinal) ||
                movement.ReferenceId != sale.Id ||
                movement.UserId != sale.CashierUserId)
            {
                return Fail(
                    "SaleCommittedV1.InvalidStockRelation",
                    "Stock movement must be a SALE movement owned by the committed sale and cashier.");
            }

            if (!itemProductIds.Contains(movement.ProductId))
            {
                return Fail(
                    "SaleCommittedV1.StockProductNotInSale",
                    "Stock movement product must occur in the committed sale items.");
            }

            if (movement.QuantityChangeX1000 <= 0 ||
                movement.CostPriceCents is < 0)
            {
                return Fail(
                    "SaleCommittedV1.InvalidStockValue",
                    "Stock movement quantity must be positive and cost cannot be negative.");
            }
        }

        return SaleCommittedV1ValidationResult.Success();
    }

    private static SaleCommittedV1ValidationResult ValidateItem(
        SaleCommittedV1Item item,
        Guid saleId)
    {
        if (item.Id == Guid.Empty ||
            item.SaleId != saleId ||
            item.ProductId == Guid.Empty)
        {
            return Fail(
                "SaleCommittedV1.InvalidItemRelation",
                "Sale item identity and sale relation are invalid.");
        }

        if (string.IsNullOrWhiteSpace(item.ProductNameSnapshot) ||
            string.IsNullOrWhiteSpace(item.UnitNameSnapshot))
        {
            return Fail(
                "SaleCommittedV1.ItemSnapshotRequired",
                "Product and unit snapshots are required.");
        }

        if (item.QuantityX1000 <= 0 ||
            item.UnitPriceCents < 0 ||
            item.CostPriceCents is < 0 ||
            item.DiscountCents < 0 ||
            item.LineTotalCents < 0)
        {
            return Fail(
                "SaleCommittedV1.InvalidItemValue",
                "Sale item quantity and money values are invalid.");
        }

        try
        {
            decimal gross = item.UnitPriceCents * (item.QuantityX1000 / 1000m);
            long roundedGross = checked((long)Math.Round(gross, 0, MidpointRounding.AwayFromZero));
            if (item.DiscountCents > roundedGross ||
                item.LineTotalCents != roundedGross - item.DiscountCents)
            {
                return Fail(
                    "SaleCommittedV1.LineTotalMismatch",
                    "Sale item line total does not match quantity, price and discount.");
            }
        }
        catch (OverflowException)
        {
            return Fail("SaleCommittedV1.AmountOverflow", "Sale item arithmetic overflowed.");
        }

        return SaleCommittedV1ValidationResult.Success();
    }

    private static SaleCommittedV1ValidationResult ValidatePayment(
        SaleCommittedV1Payment payment,
        Guid saleId)
    {
        if (payment.Id == Guid.Empty || payment.SaleId != saleId)
        {
            return Fail(
                "SaleCommittedV1.InvalidPaymentRelation",
                "Payment identity and sale relation are invalid.");
        }

        if (!string.Equals(payment.Method, SaleCommittedV1Contract.PaymentCash, StringComparison.Ordinal) &&
            !string.Equals(payment.Method, SaleCommittedV1Contract.PaymentAccountsReceivable, StringComparison.Ordinal))
        {
            return Fail(
                "SaleCommittedV1.InvalidPaymentMethod",
                "Payment method token is not supported by SaleCommittedV1.");
        }

        if (payment.AmountCents <= 0)
        {
            return Fail(
                "SaleCommittedV1.InvalidPaymentAmount",
                "Payment amount must be greater than zero.");
        }

        return SaleCommittedV1ValidationResult.Success();
    }

    private static SaleCommittedV1ValidationResult Fail(string code, string message) =>
        SaleCommittedV1ValidationResult.Fail(code, message);
}
