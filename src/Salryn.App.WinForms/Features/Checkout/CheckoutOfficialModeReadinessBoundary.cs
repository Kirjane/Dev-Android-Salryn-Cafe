using Salryn.Core.Common;
using Salryn.Core.Services;

namespace Salryn.App.WinForms.Features.Checkout;

public sealed record CheckoutOfficialModeReadiness(
    bool HasCheckedReadiness,
    bool IsReady,
    bool CanSelectOfficialMode,
    string SummaryText,
    string DetailText,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static CheckoutOfficialModeReadiness NotChecked()
    {
        return new CheckoutOfficialModeReadiness(
            HasCheckedReadiness: false,
            IsReady: false,
            CanSelectOfficialMode: false,
            SummaryText: "Official output not checked",
            DetailText: "Official output readiness has not been checked.",
            ErrorCode: null,
            ErrorMessage: null);
    }
}

public interface ICheckoutOfficialModeReadinessBoundary
{
    Task<Result<CheckoutOfficialModeReadiness>> CheckAsync(
        CancellationToken cancellationToken = default);
}

public sealed class CheckoutOfficialModeReadinessBoundary : ICheckoutOfficialModeReadinessBoundary
{
    internal const string LocalSingleStoreScopeCode = "LOCAL_SINGLE_STORE";
    internal const string OfficialReceiptDocumentKind = "OFFICIAL_RECEIPT";

    private readonly IOfficialCheckoutReadinessService _readinessService;
    private readonly Func<DateTimeOffset> _clock;

    public CheckoutOfficialModeReadinessBoundary(
        IOfficialCheckoutReadinessService readinessService,
        Func<DateTimeOffset>? clock = null)
    {
        _readinessService = readinessService ?? throw new ArgumentNullException(nameof(readinessService));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<Result<CheckoutOfficialModeReadiness>> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        Result<OfficialCheckoutReadinessResult> result =
            await _readinessService.CheckActiveScopeAsync(
                new OfficialCheckoutActiveScopeReadinessRequest(
                    LocalSingleStoreScopeCode,
                    OfficialReceiptDocumentKind,
                    LocalSingleStoreScopeCode,
                    _clock().Year),
                cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result<CheckoutOfficialModeReadiness>.Fail(result.ErrorDetail);
        }

        OfficialCheckoutReadinessResult readiness = result.Value;

        if (readiness.IsReady)
        {
            return Result<CheckoutOfficialModeReadiness>.Success(
                new CheckoutOfficialModeReadiness(
                    HasCheckedReadiness: true,
                    IsReady: true,
                    CanSelectOfficialMode: true,
                    SummaryText: "Official output ready",
                    DetailText: "Active accredited BIR machine profile and official receipt sequence are ready.",
                    ErrorCode: null,
                    ErrorMessage: null));
        }

        string detailText = readiness.Blocks.Count == 0
            ? "Official output is locked because readiness evidence is incomplete."
            : string.Join(" ", readiness.Blocks.Select(block => $"{block.Code}: {block.Message}"));

        string? firstCode = readiness.Blocks.Count == 0
            ? "OfficialCheckoutReadiness.Blocked"
            : readiness.Blocks[0].Code;

        // Kirch: why - checkout UI may display official readiness, but locked mode must stay non-selectable.
        return Result<CheckoutOfficialModeReadiness>.Success(
            new CheckoutOfficialModeReadiness(
                HasCheckedReadiness: true,
                IsReady: false,
                CanSelectOfficialMode: false,
                SummaryText: "Official output locked",
                DetailText: detailText,
                ErrorCode: firstCode,
                ErrorMessage: detailText));
    }
}
