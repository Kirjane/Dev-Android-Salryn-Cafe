using Salryn.App.WinForms.Features.Checkout;
using Salryn.Core.Common;
using Salryn.Core.Services;

internal static class PosCheckoutUiPresenterViewModelSmokeChecks
{
    public static void Run(string projectRoot)
    {
        _ = projectRoot;

        Console.WriteLine("===== POS CHECKOUT UI PRESENTER / VIEWMODEL SMOKE CHECKS =====");

        ICheckoutReceiptNumberBoundary receiptBoundary =
            new SystemCheckoutReceiptNumberBoundary();

        Result<string> generatedReceiptA =
            receiptBoundary.CreateReceiptNumber(sessionContext: null);

        Result<string> generatedReceiptB =
            receiptBoundary.CreateReceiptNumber(sessionContext: null);

        if (generatedReceiptA.IsFailure ||
            generatedReceiptB.IsFailure ||
            string.IsNullOrWhiteSpace(generatedReceiptA.Value) ||
            string.IsNullOrWhiteSpace(generatedReceiptB.Value) ||
            string.Equals(generatedReceiptA.Value, generatedReceiptB.Value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("ReceiptBoundary.Generated receipt verification failed.");
        }

        CheckoutPresenter presenter = new();

        CheckoutPresenterOperationResult initial = RequireSuccess(
            presenter.Initialize(),
            "CheckoutUiPresenter.Initialize");

        if (initial.State.Lines.Count != 0 ||
            initial.State.Totals.TotalCents != 0 ||
            initial.State.PaymentCount != 0 ||
            string.IsNullOrWhiteSpace(initial.State.ReceiptNumber) ||
            initial.State.CanCompleteSale)
        {
            throw new InvalidOperationException("Checkout presenter initial state verification failed.");
        }

        Guid cashierId = Guid.NewGuid();

        RequireSuccess(
            presenter.SetCashierUserId(cashierId),
            "CheckoutUiPresenter.SetCashierUserId");

        if (presenter.State.CashierUserId != cashierId)
        {
            throw new InvalidOperationException("Checkout presenter cashier mapping failed.");
        }

        RequireSuccess(
            presenter.SetReceiptNumber("  P40-UI-0001  "),
            "CheckoutUiPresenter.SetReceiptNumber");

        if (!string.Equals(presenter.State.ReceiptNumber, "P40-UI-0001", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Checkout presenter receipt trimming failed.");
        }

        RequireSuccess(
            presenter.SetProductInputText("  4801234567890  "),
            "CheckoutUiPresenter.SetProductInputText");

        if (!string.Equals(presenter.State.ProductInputText, "4801234567890", StringComparison.Ordinal) ||
            !presenter.State.CanAddPreparedLine)
        {
            throw new InvalidOperationException("Checkout presenter product input state failed.");
        }

        Guid productId = Guid.NewGuid();

        PosCheckoutDraftLineInput lineInput = new(
            ProductId: productId,
            ProductNameSnapshot: "Presenter Product",
            BarcodeSnapshot: "4801234567890",
            UnitNameSnapshot: "pc",
            QuantityX1000: 2000,
            UnitPriceCents: 1500,
            CostPriceCents: 900,
            DiscountCents: 100,
            TrackStock: true);

        RequireSuccess(
            presenter.AddPreparedLine(lineInput),
            "CheckoutUiPresenter.AddPreparedLine");

        VerifyLineState(
            presenter.State,
            expectedLineCount: 1,
            expectedProductId: productId,
            expectedSubtotal: 2900,
            expectedTotal: 2900,
            expectedBalance: 2900,
            area: "CheckoutUiPresenter.LineAdded");

        Guid lineId = presenter.State.Lines[0].LineId;

        RequireSuccess(
            presenter.SetCashAmountCents(5000),
            "CheckoutUiPresenter.SetCashAmountCents");

        if (!presenter.State.CanAddCashPayment ||
            presenter.State.CashAmountCents != 5000 ||
            !string.Equals(presenter.State.CashAmountText, "PHP 50.00", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Checkout presenter cash amount state failed.");
        }

        RequireSuccess(
            presenter.AddCashPayment(),
            "CheckoutUiPresenter.AddCashPayment");

        if (presenter.State.PaymentCount != 1 ||
            presenter.State.Totals.AmountPaidCents != 5000 ||
            presenter.State.Totals.ChangeCents != 2100 ||
            presenter.State.Totals.BalanceDueCents != 0 ||
            !presenter.State.CanClearPayments ||
            presenter.State.CanCompleteSale)
        {
            throw new InvalidOperationException("Checkout presenter payment/totals state failed.");
        }

        RequireSuccess(
            presenter.ClearPayments(),
            "CheckoutUiPresenter.ClearPayments");

        if (presenter.State.PaymentCount != 0 ||
            presenter.State.Totals.AmountPaidCents != 0 ||
            presenter.State.Totals.BalanceDueCents != 2900 ||
            presenter.State.CanClearPayments)
        {
            throw new InvalidOperationException("Checkout presenter clear payments state failed.");
        }

        RequireSuccess(
            presenter.RemoveLine(lineId),
            "CheckoutUiPresenter.RemoveLine");

        if (presenter.State.Lines.Count != 0 ||
            presenter.State.Totals.TotalCents != 0 ||
            presenter.State.CanRemoveSelectedLine)
        {
            throw new InvalidOperationException("Checkout presenter remove line state failed.");
        }

        CheckoutPresenterOperationResult invalidCash = presenter.SetCashAmountCents(-1);

        if (invalidCash.IsSuccess ||
            !invalidCash.State.HasError ||
            !string.Equals(invalidCash.ErrorCode, "CheckoutUi.InvalidCashAmount", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Checkout presenter invalid cash failure state failed.");
        }

        CheckoutPresenterOperationResult invalidLine = presenter.AddPreparedLine(new PosCheckoutDraftLineInput(
            ProductId: Guid.Empty,
            ProductNameSnapshot: "Invalid Product",
            BarcodeSnapshot: null,
            UnitNameSnapshot: "pc",
            QuantityX1000: 1000,
            UnitPriceCents: 100,
            CostPriceCents: null,
            DiscountCents: 0,
            TrackStock: true));

        if (invalidLine.IsSuccess ||
            !invalidLine.State.HasError ||
            string.IsNullOrWhiteSpace(invalidLine.ErrorCode))
        {
            throw new InvalidOperationException("Checkout presenter invalid line failure state failed.");
        }

        RequireSuccess(
            presenter.ClearDraft(),
            "CheckoutUiPresenter.ClearDraft");

        if (presenter.State.Lines.Count != 0 ||
            presenter.State.Totals.TotalCents != 0 ||
            presenter.State.PaymentCount != 0 ||
            presenter.State.CashierUserId is not null ||
            string.IsNullOrWhiteSpace(presenter.State.ReceiptNumber) ||
            string.Equals(presenter.State.ReceiptNumber, "P40-UI-0001", StringComparison.Ordinal) ||
            presenter.State.HasError)
        {
            throw new InvalidOperationException("Checkout presenter clear draft reset state failed.");
        }

        Console.WriteLine("POS checkout UI presenter / viewmodel smoke checks succeeded.");
        Console.WriteLine();
    }

    private static void VerifyLineState(
        CheckoutViewModel state,
        int expectedLineCount,
        Guid expectedProductId,
        long expectedSubtotal,
        long expectedTotal,
        long expectedBalance,
        string area)
    {
        if (state.Lines.Count != expectedLineCount ||
            state.Lines[0].ProductId != expectedProductId ||
            !string.Equals(state.Lines[0].ProductName, "Presenter Product", StringComparison.Ordinal) ||
            !string.Equals(state.Lines[0].Barcode, "4801234567890", StringComparison.Ordinal) ||
            !string.Equals(state.Lines[0].UnitName, "pc", StringComparison.Ordinal) ||
            state.Lines[0].QuantityX1000 != 2000 ||
            state.Lines[0].UnitPriceCents != 1500 ||
            state.Lines[0].CostPriceCents != 900 ||
            state.Lines[0].DiscountCents != 100 ||
            state.Lines[0].GrossCents != 3000 ||
            state.Lines[0].LineTotalCents != 2900 ||
            !state.Lines[0].TrackStock)
        {
            throw new InvalidOperationException($"{area} line viewmodel verification failed.");
        }

        if (state.Totals.SubtotalCents != expectedSubtotal ||
            state.Totals.TotalCents != expectedTotal ||
            state.Totals.BalanceDueCents != expectedBalance ||
            !string.Equals(state.Totals.TotalText, "PHP 29.00", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{area} totals viewmodel verification failed.");
        }
    }

    private static CheckoutPresenterOperationResult RequireSuccess(
        CheckoutPresenterOperationResult result,
        string area)
    {
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"{area} failed: {result.ErrorCode} / {result.ErrorMessage}");
        }

        return result;
    }
}

