using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;

internal static class PosCheckoutDraftTransactionBuilderSmokeChecks
{
    public static void Run(string projectRoot)
    {
        _ = projectRoot;

        Console.WriteLine("===== POS CHECKOUT DRAFT TRANSACTION BUILDER SMOKE CHECKS =====");

        PosCheckoutDraftTransactionBuilder builder = new();

        PosCheckoutDraftTransaction emptyDraft = RequireSuccess(
            builder.GetTransaction(),
            "PosCheckoutDraft.EmptyDraft");

        VerifyTotals(
            emptyDraft.Totals,
            subtotal: 0,
            discount: 0,
            tax: 0,
            total: 0,
            paid: 0,
            change: 0,
            balance: 0,
            "PosCheckoutDraft.EmptyTotals");

        Guid cashierId = Guid.NewGuid();
        Guid cashierSessionId = Guid.NewGuid();

        RequireSuccess(
            builder.SetCashierUserId(cashierId),
            "PosCheckoutDraft.SetCashier");

        RequireSuccess(
            builder.SetCashierSessionId(cashierSessionId),
            "PosCheckoutDraft.SetCashierSession");

        RequireSuccess(
            builder.SetReceiptNumber("DRFT-" + Guid.NewGuid().ToString("N")),
            "PosCheckoutDraft.SetReceipt");

        RequireSuccess(
            builder.SetReceiptSnapshot("Phase 36 draft transaction builder receipt snapshot."),
            "PosCheckoutDraft.SetReceiptSnapshot");

        Guid trackedProductId = Guid.NewGuid();

        PosCheckoutDraftLine trackedLine = RequireSuccess(
            builder.AddLine(new PosCheckoutDraftLineInput(
                trackedProductId,
                "Draft Tracked Product",
                "1234567890123",
                "pc",
                QuantityX1000: 1000,
                UnitPriceCents: 1500,
                CostPriceCents: 900,
                TrackStock: true)),
            "PosCheckoutDraft.AddTrackedLine");

        Guid untrackedProductId = Guid.NewGuid();

        PosCheckoutDraftLine untrackedLine = RequireSuccess(
            builder.AddLine(new PosCheckoutDraftLineInput(
                untrackedProductId,
                "Draft Untracked Product",
                null,
                "svc",
                QuantityX1000: 2000,
                UnitPriceCents: 500,
                CostPriceCents: null,
                TrackStock: false)),
            "PosCheckoutDraft.AddUntrackedLine");

        PosCheckoutDraftTransaction twoLineDraft = RequireSuccess(
            builder.GetTransaction(),
            "PosCheckoutDraft.TwoLineDraft");

        if (twoLineDraft.Lines.Count != 2 ||
            twoLineDraft.Lines[0].Id != trackedLine.Id ||
            twoLineDraft.Lines[1].Id != untrackedLine.Id)
        {
            throw new InvalidOperationException("Draft builder did not preserve explicit separate lines.");
        }

        VerifyTotals(
            twoLineDraft.Totals,
            subtotal: 2500,
            discount: 0,
            tax: 0,
            total: 2500,
            paid: 0,
            change: 0,
            balance: 2500,
            "PosCheckoutDraft.TwoLineTotals");

        PosCheckoutDraftLine updatedQuantityLine = RequireSuccess(
            builder.SetLineQuantity(trackedLine.Id, 3000),
            "PosCheckoutDraft.UpdateTrackedQuantity");

        if (updatedQuantityLine.QuantityX1000 != 3000 ||
            updatedQuantityLine.GrossCents != 4500 ||
            updatedQuantityLine.LineTotalCents != 4500)
        {
            throw new InvalidOperationException("Draft builder quantity update verification failed.");
        }

        PosCheckoutDraftLine discountedLine = RequireSuccess(
            builder.SetLineDiscount(trackedLine.Id, 200),
            "PosCheckoutDraft.SetLineDiscount");

        if (discountedLine.DiscountCents != 200 ||
            discountedLine.LineTotalCents != 4300)
        {
            throw new InvalidOperationException("Draft builder line discount verification failed.");
        }

        ExpectFailure(
            builder.SetLineDiscount(trackedLine.Id, 5000),
            "PosCheckoutDraft.LineDiscountExceedsGross",
            ErrorType.Validation,
            "CheckoutDraft.LineDiscountExceedsGross");

        RequireSuccess(
            builder.RemoveLine(untrackedLine.Id),
            "PosCheckoutDraft.RemoveUntrackedLine");

        PosCheckoutDraftTransaction oneLineDraft = RequireSuccess(
            builder.GetTransaction(),
            "PosCheckoutDraft.OneLineDraft");

        if (oneLineDraft.Lines.Count != 1 ||
            oneLineDraft.Lines[0].Id != trackedLine.Id)
        {
            throw new InvalidOperationException("Draft builder remove line verification failed.");
        }

        RequireSuccess(
            builder.SetSaleDiscount(300),
            "PosCheckoutDraft.SetSaleDiscount");

        RequireSuccess(
            builder.SetTax(100),
            "PosCheckoutDraft.SetTax");

        PosCheckoutDraftTotals discountedTotals = RequireSuccess(
            builder.GetTotals(),
            "PosCheckoutDraft.DiscountedTotals");

        VerifyTotals(
            discountedTotals,
            subtotal: 4300,
            discount: 300,
            tax: 100,
            total: 4100,
            paid: 0,
            change: 0,
            balance: 4100,
            "PosCheckoutDraft.DiscountedTotalsVerify");

        RequireSuccess(
            builder.AddPayment(PaymentMethod.Cash, 4000),
            "PosCheckoutDraft.AddInsufficientPayment");

        ExpectFailure(
            builder.BuildApplicationRequest(),
            "PosCheckoutDraft.InsufficientPayment",
            ErrorType.Validation,
            "CheckoutDraft.InsufficientPayment");

        RequireSuccess(
            builder.ClearPayments(),
            "PosCheckoutDraft.ClearPayments");

        RequireSuccess(
            builder.AddPayment(PaymentMethod.Cash, 5000),
            "PosCheckoutDraft.AddSufficientPayment");

        PosCheckoutDraftTotals paidTotals = RequireSuccess(
            builder.GetTotals(),
            "PosCheckoutDraft.PaidTotals");

        VerifyTotals(
            paidTotals,
            subtotal: 4300,
            discount: 300,
            tax: 100,
            total: 4100,
            paid: 5000,
            change: 900,
            balance: 0,
            "PosCheckoutDraft.PaidTotalsVerify");

        PosCheckoutApplicationRequest request = RequireSuccess(
            builder.BuildApplicationRequest(),
            "PosCheckoutDraft.BuildApplicationRequest");

        VerifyApplicationRequest(
            request,
            cashierId,
            cashierSessionId,
            trackedProductId);

        Console.WriteLine("POS checkout draft transaction builder smoke checks succeeded.");
        Console.WriteLine();
    }

    private static void VerifyApplicationRequest(
        PosCheckoutApplicationRequest request,
        Guid expectedCashierId,
        Guid expectedCashierSessionId,
        Guid expectedProductId)
    {
        if (request.CashierUserId != expectedCashierId ||
            request.CashierSessionId != expectedCashierSessionId ||
            string.IsNullOrWhiteSpace(request.ReceiptNumber) ||
            request.SaleDiscountCents != 300 ||
            request.TaxCents != 100 ||
            string.IsNullOrWhiteSpace(request.ReceiptSnapshot))
        {
            throw new InvalidOperationException("Draft builder request header verification failed.");
        }

        if (request.Lines.Count != 1)
        {
            throw new InvalidOperationException("Draft builder request line count verification failed.");
        }

        PosCheckoutLineInput line = request.Lines[0];

        if (line.ProductId != expectedProductId ||
            !string.Equals(line.ProductNameSnapshot, "Draft Tracked Product", StringComparison.Ordinal) ||
            !string.Equals(line.BarcodeSnapshot, "1234567890123", StringComparison.Ordinal) ||
            !string.Equals(line.UnitNameSnapshot, "pc", StringComparison.Ordinal) ||
            line.QuantityX1000 != 3000 ||
            line.UnitPriceCents != 1500 ||
            line.CostPriceCents != 900 ||
            line.DiscountCents != 200 ||
            !line.TrackStock)
        {
            throw new InvalidOperationException("Draft builder request line mapping verification failed.");
        }

        if (request.Payments.Count != 1 ||
            request.Payments[0].Method != PaymentMethod.Cash ||
            request.Payments[0].AmountCents != 5000)
        {
            throw new InvalidOperationException("Draft builder request payment mapping verification failed.");
        }
    }

    private static void VerifyTotals(
        PosCheckoutDraftTotals totals,
        long subtotal,
        long discount,
        long tax,
        long total,
        long paid,
        long change,
        long balance,
        string area)
    {
        if (totals.SubtotalCents != subtotal ||
            totals.SaleDiscountCents != discount ||
            totals.TaxCents != tax ||
            totals.TotalCents != total ||
            totals.AmountPaidCents != paid ||
            totals.ChangeCents != change ||
            totals.BalanceDueCents != balance)
        {
            throw new InvalidOperationException(
                $"{area} totals mismatch. " +
                $"Got subtotal={totals.SubtotalCents}, discount={totals.SaleDiscountCents}, tax={totals.TaxCents}, total={totals.TotalCents}, paid={totals.AmountPaidCents}, change={totals.ChangeCents}, balance={totals.BalanceDueCents}.");
        }
    }

    private static void ExpectFailure(
        Result result,
        string area,
        ErrorType expectedType,
        string expectedCode)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException($"{area} expected failure but succeeded.");
        }

        if (result.ErrorDetail.Type != expectedType ||
            !string.Equals(result.ErrorDetail.Code, expectedCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{area} expected {expectedType}/{expectedCode}, got {result.ErrorDetail.Type}/{result.ErrorDetail.Code}: {result.Error}");
        }
    }

    private static void ExpectFailure<T>(
        Result<T> result,
        string area,
        ErrorType expectedType,
        string expectedCode)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException($"{area} expected failure but succeeded.");
        }

        if (result.ErrorDetail.Type != expectedType ||
            !string.Equals(result.ErrorDetail.Code, expectedCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{area} expected {expectedType}/{expectedCode}, got {result.ErrorDetail.Type}/{result.ErrorDetail.Code}: {result.Error}");
        }
    }

    private static void RequireSuccess(Result result, string area)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{area} failed: {result.Error}");
        }
    }

    private static T RequireSuccess<T>(Result<T> result, string area)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{area} failed: {result.Error}");
        }

        return result.Value;
    }
}

