using Salryn.App.WinForms.Features.Shell;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Core.Services;

namespace Salryn.App.WinForms.Features.OfficialReadings;

public sealed record OfficialReadingsSearchInput(
    DateTimeOffset RangeStartedAt,
    DateTimeOffset RangeEndedAt,
    ElectronicJournalDocumentKind DocumentKind,
    Guid? BirMachineProfileId,
    string? RegistrationScopeCode);

public sealed record OfficialReadingsViewModel(
    bool HasPreview,
    string PreviewText,
    bool HasFinalizeResult,
    string FinalizeResultText,
    bool CanPreview,
    bool CanFinalize,
    string StatusMessage,
    bool HasError,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static OfficialReadingsViewModel Initial() =>
        new(
            HasPreview: false,
            PreviewText: string.Empty,
            HasFinalizeResult: false,
            FinalizeResultText: string.Empty,
            CanPreview: false,
            CanFinalize: false,
            StatusMessage: "Set the reading range and preview X-reading before finalizing Z-reading.",
            HasError: false,
            ErrorCode: null,
            ErrorMessage: null);
}

public sealed record OfficialReadingsOperationResult(
    bool IsSuccess,
    OfficialReadingsViewModel State,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public static OfficialReadingsOperationResult Success(OfficialReadingsViewModel state) =>
        new(true, state);

    public static OfficialReadingsOperationResult Failure(
        OfficialReadingsViewModel state,
        string errorCode,
        string errorMessage) =>
        new(false, state, errorCode, errorMessage);
}

public sealed class OfficialReadingsPresenter
{
    private readonly IOfficialXReadingService _xReading;
    private readonly IOfficialZReadingService _zReading;
    private readonly CurrentAppSession _session;

    private OfficialReadingsSearchInput? _lastSearch;
    private OfficialXReadingResult? _lastPreview;
    private OfficialReadingsViewModel _state = OfficialReadingsViewModel.Initial();

    public OfficialReadingsPresenter(
        IOfficialXReadingService xReading,
        IOfficialZReadingService zReading,
        CurrentAppSession session)
    {
        _xReading = xReading ?? throw new ArgumentNullException(nameof(xReading));
        _zReading = zReading ?? throw new ArgumentNullException(nameof(zReading));
        _session = session ?? throw new ArgumentNullException(nameof(session));

        _state = _state with
        {
            CanPreview = _session.Can(SalrynPermissions.OfficialReadingView),
            CanFinalize = _session.Can(SalrynPermissions.OfficialReadingFinalize)
        };
    }

    public OfficialReadingsViewModel State => _state;

    public async Task<OfficialReadingsOperationResult> PreviewXReadingAsync(
        OfficialReadingsSearchInput input,
        CancellationToken cancellationToken = default)
    {
        if (!_session.Can(SalrynPermissions.OfficialReadingView))
        {
            return Failure(
                "OfficialReading.PermissionDenied",
                "X-reading preview requires OfficialReading.View permission.");
        }

        Result validation = ValidateSearchInput(input);

        if (validation.IsFailure)
        {
            return Failure(validation.ErrorDetail.Code, validation.ErrorDetail.Message);
        }

        _lastSearch = input;

        Result<OfficialXReadingResult> result = await _xReading.ReadAsync(
            new OfficialXReadingRequest(
                input.RangeStartedAt,
                input.RangeEndedAt,
                input.BirMachineProfileId,
                input.RegistrationScopeCode,
                input.DocumentKind),
            cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Failure(result.ErrorDetail.Code, result.ErrorDetail.Message);
        }

        _lastPreview = result.Value;

        _state = _state with
        {
            HasPreview = true,
            PreviewText = FormatXReading(result.Value),
            HasFinalizeResult = false,
            FinalizeResultText = string.Empty,
            StatusMessage = "X-reading preview loaded.",
            HasError = false,
            ErrorCode = null,
            ErrorMessage = null,
            CanPreview = _session.Can(SalrynPermissions.OfficialReadingView),
            CanFinalize = _session.Can(SalrynPermissions.OfficialReadingFinalize) && _lastPreview is not null
        };

        return OfficialReadingsOperationResult.Success(_state);
    }

    public async Task<OfficialReadingsOperationResult> FinalizeZReadingAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_session.Can(SalrynPermissions.OfficialReadingFinalize))
        {
            return Failure(
                "OfficialReading.PermissionDenied",
                "Z-reading finalization requires OfficialReading.Finalize permission.");
        }

        if (_lastSearch is null || _lastPreview is null)
        {
            return Failure(
                "OfficialZReading.PreviewRequired",
                "Preview X-reading before finalizing Z-reading.");
        }

        Result<OfficialZReadingResult> result = await _zReading.FinalizeAsync(
            new OfficialZReadingRequest(
                _lastSearch.RangeStartedAt,
                _lastSearch.RangeEndedAt,
                _lastSearch.BirMachineProfileId,
                _lastSearch.RegistrationScopeCode,
                _lastSearch.DocumentKind,
                _session.UserId,
                Environment.MachineName),
            cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Failure(result.ErrorDetail.Code, result.ErrorDetail.Message);
        }

        _state = _state with
        {
            HasFinalizeResult = true,
            FinalizeResultText = FormatZReading(result.Value),
            StatusMessage = "Z-reading finalized and persisted.",
            HasError = false,
            ErrorCode = null,
            ErrorMessage = null
        };

        return OfficialReadingsOperationResult.Success(_state);
    }

    private static string FormatXReading(OfficialXReadingResult reading)
    {
        OfficialXReadingSummary summary = reading.Summary;
        OfficialXReadingAdjustedSummary adjusted = reading.AdjustedSummary;
        OfficialXReadingLifecycleSummary lifecycle = reading.Lifecycle;

        return
            $"Range: {summary.RangeStartedAt:yyyy-MM-dd HH:mm} to {summary.RangeEndedAt:yyyy-MM-dd HH:mm} UTC{Environment.NewLine}" +
            $"Document kind: {summary.DocumentKind}{Environment.NewLine}" +
            $"Base entries: {summary.EntryCount} · Gross: {FormatMoney(summary.GrossSalesCents)} · Net: {FormatMoney(summary.NetSalesCents)}{Environment.NewLine}" +
            $"Adjusted entries: {adjusted.ActiveEntryCount} · Adjusted total: {FormatMoney(adjusted.TotalSalesCents)}{Environment.NewLine}" +
            $"Lifecycle counts — Reprint: {lifecycle.ReprintCount}, Void: {lifecycle.VoidCount}, Cancel: {lifecycle.CancelCount}, Correction: {lifecycle.CorrectionCount}, Refund: {lifecycle.RefundCount}{Environment.NewLine}" +
            $"Document range: {reading.DocumentRange?.FirstOfficialDocumentNumber ?? "n/a"} to {reading.DocumentRange?.LastOfficialDocumentNumber ?? "n/a"}{Environment.NewLine}" +
            $"Evidence manifest hash: {summary.EvidenceManifestHash}{Environment.NewLine}" +
            $"All snapshot hashes match: {summary.AllSnapshotHashesMatch}{Environment.NewLine}" +
            FormatVatBuckets(reading.VatBuckets) +
            FormatPayments(reading.Payments);
    }

    private static string FormatZReading(OfficialZReadingResult reading)
    {
        OfficialZReadingSummary summary = reading.Summary;
        OfficialZReadingEnrichmentPayload? enrichment = reading.Enrichment;

        string enrichmentText = enrichment is null
            ? "Enrichment: not persisted"
            : $"Enrichment schema: {enrichment.Schema}{Environment.NewLine}" +
              $"Enrichment lifecycle — Reprint: {enrichment.Lifecycle.ReprintCount}, Void: {enrichment.Lifecycle.VoidCount}, Cancel: {enrichment.Lifecycle.CancelCount}, Correction: {enrichment.Lifecycle.CorrectionCount}, Refund: {enrichment.Lifecycle.RefundCount}{Environment.NewLine}" +
              $"Enrichment adjusted total: {FormatMoney(enrichment.AdjustedSummary.TotalSalesCents)}";

        return
            $"Z id: {summary.ZReadingId:D}{Environment.NewLine}" +
            $"Finalized at: {summary.FinalizedAt:yyyy-MM-dd HH:mm} UTC{Environment.NewLine}" +
            $"Range: {summary.RangeStartedAt:yyyy-MM-dd HH:mm} to {summary.RangeEndedAt:yyyy-MM-dd HH:mm} UTC{Environment.NewLine}" +
            $"Entries: {summary.EntryCount} · Gross: {FormatMoney(summary.GrossSalesCents)} · Net: {FormatMoney(summary.NetSalesCents)}{Environment.NewLine}" +
            $"Document range: {summary.FirstOfficialDocumentNumber ?? "n/a"} to {summary.LastOfficialDocumentNumber ?? "n/a"}{Environment.NewLine}" +
            $"Evidence manifest hash: {summary.EvidenceManifestHash}{Environment.NewLine}" +
            enrichmentText;
    }

    private static string FormatVatBuckets(IReadOnlyList<OfficialXReadingVatBucketSummary> buckets)
    {
        if (buckets.Count == 0)
        {
            return "VAT buckets: none" + Environment.NewLine;
        }

        return "VAT buckets:" + Environment.NewLine +
            string.Join(Environment.NewLine, buckets.Select(static bucket =>
                $"  {bucket.BucketKind}: sales {FormatMoney(bucket.SalesAmountCents)}, tax {FormatMoney(bucket.TaxAmountCents)}, entries {bucket.EntryCount}")) +
            Environment.NewLine;
    }

    private static string FormatPayments(IReadOnlyList<OfficialXReadingPaymentSummary> payments)
    {
        if (payments.Count == 0)
        {
            return "Payments: none";
        }

        return "Payments:" + Environment.NewLine +
            string.Join(Environment.NewLine, payments.Select(static payment =>
                $"  {payment.Method}: {FormatMoney(payment.AmountCents)} ({payment.PaymentCount} payments)"));
    }

    private static string FormatMoney(long cents)
    {
        return (cents / 100m).ToString("0.00");
    }

    private static Result ValidateSearchInput(OfficialReadingsSearchInput input)
    {
        if (input.RangeEndedAt <= input.RangeStartedAt)
        {
            return Result.Fail(Error.Validation(
                "OfficialReading.InvalidRange",
                "Reading range end must be after range start."));
        }

        if (!input.BirMachineProfileId.HasValue &&
            string.IsNullOrWhiteSpace(input.RegistrationScopeCode))
        {
            return Result.Fail(Error.Validation(
                "OfficialReading.MachineProfileOrScopeRequired",
                "Machine profile id or registration scope is required."));
        }

        return Result.Success();
    }

    private OfficialReadingsOperationResult Failure(string errorCode, string errorMessage)
    {
        _state = _state with
        {
            HasError = true,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            StatusMessage = errorMessage
        };

        return OfficialReadingsOperationResult.Failure(_state, errorCode, errorMessage);
    }
}
