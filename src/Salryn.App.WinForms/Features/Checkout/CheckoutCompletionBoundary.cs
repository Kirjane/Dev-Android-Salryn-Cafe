using Salryn.Core.Common;
using Salryn.Core.Services;

namespace Salryn.App.WinForms.Features.Checkout;

// Kirch file guide: boundary for final sale completion readiness and submit.
// UI location: used by CheckoutPresenter before enabling/performing Complete Sale.
// Frontend note: do not move sale completion rules into button click handlers.

public sealed record CheckoutCompletionReadiness(
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

public interface ICheckoutCompletionBoundary
{
    Result<CheckoutCompletionReadiness> Evaluate(PosCheckoutDraftTransaction transaction);

    Task<Result> CompleteSaleAsync(
        PosCheckoutApplicationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class CheckoutCompletionBoundary : ICheckoutCompletionBoundary
{
    private readonly IPosCheckoutApplicationService? _applicationService;

    public CheckoutCompletionBoundary()
        : this(applicationService: null)
    {
    }

    public CheckoutCompletionBoundary(IPosCheckoutApplicationService? applicationService)
    {
        _applicationService = applicationService;
    }

    public Result<CheckoutCompletionReadiness> Evaluate(PosCheckoutDraftTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        bool hasCashierUser = transaction.CashierUserId != Guid.Empty;
        bool hasCashierSession =
            transaction.CashierSessionId.HasValue &&
            transaction.CashierSessionId.Value != Guid.Empty;
        bool hasReceiptNumber = !string.IsNullOrWhiteSpace(transaction.ReceiptNumber);
        bool hasLines = transaction.Lines.Count > 0;
        bool hasPayments = transaction.Payments.Count > 0;
        bool isPaidInFull =
            hasPayments &&
            transaction.Totals.TotalCents >= 0 &&
            transaction.Totals.AmountPaidCents >= transaction.Totals.TotalCents;

        List<string> missing = new();

        if (!hasCashierUser)
        {
            missing.Add("cashier user");
        }

        if (!hasCashierSession)
        {
            missing.Add("cashier shift");
        }

        if (!hasReceiptNumber)
        {
            missing.Add("receipt number");
        }

        if (!hasLines)
        {
            missing.Add("draft items");
        }

        if (!hasPayments)
        {
            missing.Add("payment");
        }

        if (hasPayments && !isPaidInFull)
        {
            missing.Add("full payment");
        }

        bool canBuildCompletionRequest = missing.Count == 0;

        string summaryText = canBuildCompletionRequest
            ? "Completion request ready · submit locked"
            : "Completion not ready";

        string detailText = canBuildCompletionRequest
            ? "Draft can build a completion request. Sale submit remains locked by UI."
            : "Missing: " + string.Join(", ", missing) + ".";

        return Result<CheckoutCompletionReadiness>.Success(new CheckoutCompletionReadiness(
            HasCashierUser: hasCashierUser,
            HasCashierSession: hasCashierSession,
            HasReceiptNumber: hasReceiptNumber,
            HasLines: hasLines,
            HasPayments: hasPayments,
            IsPaidInFull: isPaidInFull,
            CanBuildCompletionRequest: canBuildCompletionRequest,
            CanSubmitSale: false,
            SummaryText: summaryText,
            DetailText: detailText));
    }

    public Task<Result> CompleteSaleAsync(
        PosCheckoutApplicationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_applicationService is null)
        {
            return Task.FromResult(Result.Fail(Error.Failure(
                "CheckoutCompletion.ApplicationServiceMissing",
                "Checkout completion application service is not wired.")));
        }

        return _applicationService.CompleteSaleAsync(request, cancellationToken);
    }
}
