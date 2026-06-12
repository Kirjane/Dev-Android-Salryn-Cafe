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
            initial.State.CompletionReadiness.HasLines ||
            !initial.State.CompletionReadiness.HasReceiptNumber ||
            initial.State.CompletionReadiness.CanBuildCompletionRequest ||
            initial.State.CompletionReadiness.CanSubmitSale ||
            initial.State.SaleSubmitGuard.IsPrepared ||
            initial.State.SaleSubmitGuard.CanExecuteSubmit ||
            initial.State.SaleSubmitResult.HasRun ||
            initial.State.SaleSubmitResult.IsCompleted ||
            initial.State.CanCompleteSale)
        {
            throw new InvalidOperationException("CompletionReadiness.Initial presenter state verification failed.");
        }

        CheckoutPresenterOperationResult initialDryRun =
            presenter.DryRunCompletionRequest();

        if (initialDryRun.IsSuccess ||
            !initialDryRun.State.HasError ||
            !initialDryRun.State.CompletionDryRun.HasRun ||
            initialDryRun.State.CompletionDryRun.IsSuccess ||
            initialDryRun.State.CompletionDryRun.BuiltRequest ||
            !string.Equals(initialDryRun.ErrorCode, "CheckoutDraft.InvalidCashierUserId", StringComparison.Ordinal) ||
            initialDryRun.State.SaleSubmitGuard.IsPrepared ||
            initialDryRun.State.SaleSubmitGuard.CanExecuteSubmit)
        {
            throw new InvalidOperationException("CompletionDryRun.InitialMissingCashier validation failed.");
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
            presenter.State.CompletionReadiness.HasCashierSession ||
            presenter.State.CompletionReadiness.CanBuildCompletionRequest ||
            presenter.State.CompletionReadiness.CanSubmitSale ||
            presenter.State.CompletionDryRun.HasRun ||
            presenter.State.SaleSubmitGuard.IsPrepared ||
            presenter.State.SaleSubmitGuard.CanExecuteSubmit ||
            presenter.State.CanCompleteSale)
        {
            throw new InvalidOperationException("CompletionReadiness.MissingSession payment/totals state failed.");
        }

        CheckoutPresenterOperationResult missingSessionDryRun =
            presenter.DryRunCompletionRequest();

        if (missingSessionDryRun.IsSuccess ||
            !missingSessionDryRun.State.HasError ||
            !missingSessionDryRun.State.CompletionDryRun.HasRun ||
            missingSessionDryRun.State.CompletionDryRun.IsSuccess ||
            missingSessionDryRun.State.CompletionDryRun.BuiltRequest ||
            !string.Equals(missingSessionDryRun.ErrorCode, "CheckoutDraft.InvalidCashierSessionId", StringComparison.Ordinal) ||
            missingSessionDryRun.State.SaleSubmitGuard.IsPrepared ||
            missingSessionDryRun.State.SaleSubmitGuard.CanExecuteSubmit)
        {
            throw new InvalidOperationException("CompletionDryRun.MissingSession validation failed.");
        }

        CheckoutSessionContext sessionContext = new(
            CashierUserId: cashierId,
            CashierDisplayName: "Smoke Cashier",
            CashierRoleName: "Cashier",
            CashierSessionId: Guid.NewGuid(),
            ShiftOpenedAt: DateTimeOffset.UtcNow,
            OpeningCashText: "PHP 0.00");

        RecordingCheckoutCompletionBoundary completionBoundary = new();

        CheckoutPresenter sessionPresenter = new(
            productSearchBoundary: null,
            sessionContext: sessionContext,
            receiptNumberBoundary: null,
            completionBoundary: completionBoundary);

        RequireSuccess(
            sessionPresenter.Initialize(),
            "CheckoutUiPresenter.SessionInitialize");

        RequireSuccess(
            sessionPresenter.AddPreparedLine(lineInput),
            "CheckoutUiPresenter.SessionAddPreparedLine");

        RequireSuccess(
            sessionPresenter.SetCashAmountCents(5000),
            "CheckoutUiPresenter.SessionSetCashAmountCents");

        RequireSuccess(
            sessionPresenter.AddCashPayment(),
            "CheckoutUiPresenter.SessionAddCashPayment");

        if (!sessionPresenter.State.CompletionReadiness.HasCashierUser ||
            !sessionPresenter.State.CompletionReadiness.HasCashierSession ||
            !sessionPresenter.State.CompletionReadiness.HasReceiptNumber ||
            !sessionPresenter.State.CompletionReadiness.HasLines ||
            !sessionPresenter.State.CompletionReadiness.HasPayments ||
            !sessionPresenter.State.CompletionReadiness.IsPaidInFull ||
            !sessionPresenter.State.CompletionReadiness.CanBuildCompletionRequest ||
            sessionPresenter.State.CompletionReadiness.CanSubmitSale ||
            sessionPresenter.State.CompletionDryRun.HasRun ||
            sessionPresenter.State.CanCompleteSale)
        {
            throw new InvalidOperationException("CompletionReadiness.ReadyButLocked state failed.");
        }

        CheckoutPresenterOperationResult guardBeforeDryRun =
            sessionPresenter.PrepareSaleSubmitGuard();

        if (guardBeforeDryRun.IsSuccess ||
            !guardBeforeDryRun.State.HasError ||
            !string.Equals(
                guardBeforeDryRun.State.SaleSubmitGuard.ErrorCode,
                "CheckoutSubmitGuard.DryRunRequired",
                StringComparison.Ordinal) ||
            guardBeforeDryRun.State.SaleSubmitGuard.IsPrepared ||
            guardBeforeDryRun.State.SaleSubmitGuard.CanExecuteSubmit ||
            guardBeforeDryRun.State.CanCompleteSale)
        {
            throw new InvalidOperationException("SaleSubmitGuard.DryRunRequired validation failed.");
        }

        CheckoutPresenterOperationResult readyDryRun = RequireSuccess(
            sessionPresenter.DryRunCompletionRequest(),
            "CheckoutUiPresenter.ReadyDryRun");

        if (!readyDryRun.State.CompletionDryRun.HasRun ||
            !readyDryRun.State.CompletionDryRun.IsSuccess ||
            !readyDryRun.State.CompletionDryRun.BuiltRequest ||
            string.IsNullOrWhiteSpace(readyDryRun.State.CompletionDryRun.DetailText) ||
            readyDryRun.State.CompletionReadiness.CanSubmitSale ||
            readyDryRun.State.SaleSubmitGuard.IsPrepared ||
            readyDryRun.State.SaleSubmitGuard.CanExecuteSubmit ||
            readyDryRun.State.CanCompleteSale)
        {
            throw new InvalidOperationException("CompletionDryRun.ReadyButLocked validation failed.");
        }

        CheckoutPresenterOperationResult submitGuard = RequireSuccess(
            sessionPresenter.PrepareSaleSubmitGuard(),
            "CheckoutUiPresenter.PrepareSaleSubmitGuard");

        if (!submitGuard.State.SaleSubmitGuard.IsPrepared ||
            submitGuard.State.SaleSubmitGuard.IsInProgress ||
            !submitGuard.State.SaleSubmitGuard.HasAttemptToken ||
            string.IsNullOrWhiteSpace(submitGuard.State.SaleSubmitGuard.AttemptTokenText) ||
            !submitGuard.State.SaleSubmitGuard.CanExecuteSubmit ||
            submitGuard.State.SaleSubmitResult.HasRun ||
            submitGuard.State.CompletionReadiness.CanSubmitSale ||
            !submitGuard.State.CanCompleteSale)
        {
            throw new InvalidOperationException("SaleSubmitGuard.PreparedAndUiUnlocked validation failed.");
        }

        CheckoutPresenterOperationResult saleSubmit = RequireSuccess(
            sessionPresenter.ExecutePreparedSaleSubmitAsync().GetAwaiter().GetResult(),
            "CheckoutUiPresenter.ExecutePreparedSaleSubmit");

        if (!saleSubmit.State.SaleSubmitResult.HasRun ||
            !saleSubmit.State.SaleSubmitResult.IsSuccess ||
            !saleSubmit.State.SaleSubmitResult.IsCompleted ||
            !saleSubmit.State.SaleSubmitGuard.IsPrepared ||
            saleSubmit.State.SaleSubmitGuard.CanExecuteSubmit ||
            saleSubmit.State.CanCompleteSale ||
            saleSubmit.State.CanAddCashPayment ||
            saleSubmit.State.CanRemoveSelectedLine ||
            saleSubmit.State.CanClearPayments ||
            completionBoundary.CompleteSaleCallCount != 1 ||
            completionBoundary.LastRequest is null)
        {
            throw new InvalidOperationException("SaleSubmitExecution.CompletedUiLocked validation failed.");
        }

        CheckoutPresenterOperationResult duplicateSubmit =
            sessionPresenter.ExecutePreparedSaleSubmitAsync().GetAwaiter().GetResult();

        if (duplicateSubmit.IsSuccess ||
            !string.Equals(duplicateSubmit.ErrorCode, "CheckoutSubmit.AlreadyCompleted", StringComparison.Ordinal) ||
            completionBoundary.CompleteSaleCallCount != 1 ||
            duplicateSubmit.State.CanCompleteSale)
        {
            throw new InvalidOperationException("SaleSubmitExecution.DuplicateBlocked validation failed.");
        }

        RequireSuccess(
            sessionPresenter.ClearDraft(),
            "CheckoutUiPresenter.SessionClearDraft");

        if (sessionPresenter.State.SaleSubmitGuard.IsPrepared ||
            sessionPresenter.State.SaleSubmitGuard.HasAttemptToken ||
            sessionPresenter.State.SaleSubmitGuard.CanExecuteSubmit ||
            sessionPresenter.State.SaleSubmitResult.HasRun ||
            sessionPresenter.State.SaleSubmitResult.IsCompleted ||
            sessionPresenter.State.CompletionDryRun.HasRun ||
            sessionPresenter.State.CanCompleteSale)
        {
            throw new InvalidOperationException("SaleSubmitGuard.ClearDraftReset validation failed.");
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
            presenter.State.CompletionDryRun.HasRun ||
            presenter.State.SaleSubmitGuard.IsPrepared ||
            presenter.State.SaleSubmitGuard.CanExecuteSubmit ||
            presenter.State.SaleSubmitResult.HasRun ||
            presenter.State.SaleSubmitResult.IsCompleted ||
            presenter.State.HasError)
        {
            throw new InvalidOperationException("Checkout presenter clear draft reset state failed.");
        }

        Console.WriteLine("POS checkout UI presenter / viewmodel smoke checks succeeded.");
        Console.WriteLine();
    }

    private sealed class RecordingCheckoutCompletionBoundary : ICheckoutCompletionBoundary
    {
        public int CompleteSaleCallCount { get; private set; }

        public PosCheckoutApplicationRequest? LastRequest { get; private set; }

        public Result<CheckoutCompletionReadiness> Evaluate(PosCheckoutDraftTransaction transaction)
        {
            return new CheckoutCompletionBoundary().Evaluate(transaction);
        }

        public Task<Result> CompleteSaleAsync(
            PosCheckoutApplicationRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;

            CompleteSaleCallCount++;
            LastRequest = request;

            return Task.FromResult(Result.Success());
        }
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








