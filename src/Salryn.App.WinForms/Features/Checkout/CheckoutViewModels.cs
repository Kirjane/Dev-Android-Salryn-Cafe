namespace Salryn.App.WinForms.Features.Checkout;

public sealed record CheckoutLineViewModel(
    Guid LineId,
    Guid ProductId,
    string ProductName,
    string? Barcode,
    string UnitName,
    long QuantityX1000,
    string QuantityText,
    long UnitPriceCents,
    string UnitPriceText,
    long? CostPriceCents,
    long DiscountCents,
    string DiscountText,
    long GrossCents,
    string GrossText,
    long LineTotalCents,
    string LineTotalText,
    bool TrackStock);

public sealed record CheckoutPreparedProductViewModel(
    Guid ProductId,
    string ProductName,
    string? Sku,
    string? Barcode,
    string UnitName,
    long UnitPriceCents,
    string UnitPriceText,
    long? CostPriceCents,
    string CostPriceText,
    long StockOnHandX1000,
    string StockOnHandText,
    bool TrackStock,
    bool IsActive);

public sealed record CheckoutTotalsViewModel(
    long SubtotalCents,
    string SubtotalText,
    long SaleDiscountCents,
    string SaleDiscountText,
    long TaxCents,
    string TaxText,
    long TotalCents,
    string TotalText,
    long AmountPaidCents,
    string AmountPaidText,
    long ChangeCents,
    string ChangeText,
    long BalanceDueCents,
    string BalanceDueText);

public sealed record CheckoutCompletionReadinessViewModel(
    bool HasCashierUser,
    bool HasCashierSession,
    bool HasReceiptNumber,
    bool HasLines,
    bool HasPayments,
    bool IsPaidInFull,
    bool CanBuildCompletionRequest,
    bool CanSubmitSale,
    string SummaryText,
    string DetailText);

public sealed record CheckoutCompletionDryRunViewModel(
    bool HasRun,
    bool IsSuccess,
    bool BuiltRequest,
    string SummaryText,
    string DetailText,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static CheckoutCompletionDryRunViewModel NotRun()
    {
        return new CheckoutCompletionDryRunViewModel(
            HasRun: false,
            IsSuccess: false,
            BuiltRequest: false,
            SummaryText: "not executed",
            DetailText: "Completion request dry run has not been executed.",
            ErrorCode: null,
            ErrorMessage: null);
    }
}

public sealed record CheckoutSaleSubmitGuardViewModel(
    bool IsPrepared,
    bool IsInProgress,
    bool HasAttemptToken,
    string AttemptTokenText,
    bool CanExecuteSubmit,
    string SummaryText,
    string DetailText,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static CheckoutSaleSubmitGuardViewModel NotPrepared()
    {
        return new CheckoutSaleSubmitGuardViewModel(
            IsPrepared: false,
            IsInProgress: false,
            HasAttemptToken: false,
            AttemptTokenText: string.Empty,
            CanExecuteSubmit: false,
            SummaryText: "not prepared",
            DetailText: "Sale submit guard has not been prepared.",
            ErrorCode: null,
            ErrorMessage: null);
    }
}

public sealed record CheckoutViewModel(
    Guid? CashierUserId,
    Guid? CashierSessionId,
    string CashierDisplayName,
    string CashierRoleName,
    string CashierSessionText,
    string CheckoutModeText,
    string ReceiptNumber,
    string ProductInputText,
    string ProductResultText,
    CheckoutPreparedProductViewModel? PreparedProduct,
    IReadOnlyList<CheckoutLineViewModel> Lines,
    CheckoutTotalsViewModel Totals,
    int PaymentCount,
    string CashAmountText,
    long CashAmountCents,
    bool CanSearchProduct,
    bool CanAddPreparedLine,
    bool CanAddPreparedProductLine,
    bool CanRemoveSelectedLine,
    bool CanAddCashPayment,
    bool CanClearPayments,
    bool CanCompleteSale,
    CheckoutCompletionReadinessViewModel CompletionReadiness,
    CheckoutCompletionDryRunViewModel CompletionDryRun,
    CheckoutSaleSubmitGuardViewModel SaleSubmitGuard,
    string StatusMessage,
    bool HasError,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static CheckoutViewModel Empty()
    {
        return new CheckoutViewModel(
            CashierUserId: null,
            CashierSessionId: null,
            CashierDisplayName: string.Empty,
            CashierRoleName: string.Empty,
            CashierSessionText: "No cashier shift context bound.",
            CheckoutModeText: "Repository-backed product search · Sale completion locked",
            ReceiptNumber: string.Empty,
            ProductInputText: string.Empty,
            ProductResultText: "Waiting for product input.",
            PreparedProduct: null,
            Lines: Array.Empty<CheckoutLineViewModel>(),
            Totals: new CheckoutTotalsViewModel(
                SubtotalCents: 0,
                SubtotalText: "PHP 0.00",
                SaleDiscountCents: 0,
                SaleDiscountText: "PHP 0.00",
                TaxCents: 0,
                TaxText: "PHP 0.00",
                TotalCents: 0,
                TotalText: "PHP 0.00",
                AmountPaidCents: 0,
                AmountPaidText: "PHP 0.00",
                ChangeCents: 0,
                ChangeText: "PHP 0.00",
                BalanceDueCents: 0,
                BalanceDueText: "PHP 0.00"),
            PaymentCount: 0,
            CashAmountText: "PHP 0.00",
            CashAmountCents: 0,
            CanSearchProduct: false,
            CanAddPreparedLine: false,
            CanAddPreparedProductLine: false,
            CanRemoveSelectedLine: false,
            CanAddCashPayment: false,
            CanClearPayments: false,
            CanCompleteSale: false,
            CompletionReadiness: new CheckoutCompletionReadinessViewModel(
                HasCashierUser: false,
                HasCashierSession: false,
                HasReceiptNumber: false,
                HasLines: false,
                HasPayments: false,
                IsPaidInFull: false,
                CanBuildCompletionRequest: false,
                CanSubmitSale: false,
                SummaryText: "Completion not ready",
                DetailText: "Checkout has not been initialized."),
            CompletionDryRun: CheckoutCompletionDryRunViewModel.NotRun(),
            SaleSubmitGuard: CheckoutSaleSubmitGuardViewModel.NotPrepared(),
            StatusMessage: "Checkout presenter ready.",
            HasError: false,
            ErrorCode: null,
            ErrorMessage: null);
    }
}

public sealed record CheckoutPresenterOperationResult(
    bool IsSuccess,
    string? ErrorCode,
    string? ErrorMessage,
    CheckoutViewModel State);



