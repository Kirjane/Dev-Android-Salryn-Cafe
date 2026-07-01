using Salryn.App.WinForms.Features.OfficialOutput;
using Salryn.App.WinForms.Features.Shell;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Core.Services;

namespace Salryn.App.WinForms.Features.OfficialDocuments;

public sealed record OfficialDocumentLifecycleSearchInput(
    string? OfficialDocumentNumber,
    ElectronicJournalDocumentKind? DocumentKind,
    Guid? BirMachineProfileId,
    string? RegistrationScopeCode,
    Guid? JournalEntryId);

public sealed record OfficialDocumentLifecycleEventRow(
    string EventType,
    string Reason,
    string ActorUserId,
    DateTimeOffset CreatedAt,
    string? TerminalId);

public sealed record OfficialDocumentLifecycleViewModel(
    bool IsLoaded,
    string? OfficialDocumentNumber,
    string? DocumentKind,
    string? SaleId,
    string? JournalEntryId,
    string? MachineProfileId,
    string? RegistrationScopeCode,
    string? TerminalId,
    string? LifecycleState,
    string? SnapshotPayloadHash,
    string? SnapshotHashMatches,
    string? ZCoverageStatus,
    IReadOnlyList<OfficialDocumentLifecycleEventRow> Events,
    IReadOnlyList<string> Issues,
    bool CanReprint,
    bool CanVoid,
    bool CanCancel,
    bool CanCorrection,
    bool CanRefund,
    string StatusMessage,
    bool HasError,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static OfficialDocumentLifecycleViewModel Empty(string statusMessage) =>
        new(
            IsLoaded: false,
            OfficialDocumentNumber: null,
            DocumentKind: null,
            SaleId: null,
            JournalEntryId: null,
            MachineProfileId: null,
            RegistrationScopeCode: null,
            TerminalId: null,
            LifecycleState: null,
            SnapshotPayloadHash: null,
            SnapshotHashMatches: null,
            ZCoverageStatus: null,
            Events: Array.Empty<OfficialDocumentLifecycleEventRow>(),
            Issues: Array.Empty<string>(),
            CanReprint: false,
            CanVoid: false,
            CanCancel: false,
            CanCorrection: false,
            CanRefund: false,
            StatusMessage: statusMessage,
            HasError: false,
            ErrorCode: null,
            ErrorMessage: null);
}

public sealed record OfficialDocumentLifecycleOperationResult(
    bool IsSuccess,
    OfficialDocumentLifecycleViewModel State,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public static OfficialDocumentLifecycleOperationResult Success(OfficialDocumentLifecycleViewModel state) =>
        new(true, state);

    public static OfficialDocumentLifecycleOperationResult Failure(
        OfficialDocumentLifecycleViewModel state,
        string errorCode,
        string errorMessage) =>
        new(false, state, errorCode, errorMessage);
}

public sealed class OfficialDocumentLifecyclePresenter
{
    private readonly IOfficialDocumentLifecycleReadService _lifecycleRead;
    private readonly IOfficialDocumentLifecycleOperationBoundary _lifecycleOperations;
    private readonly IOfficialDocumentReprintService _reprints;
    private readonly IOfficialOutputSourceReadService _outputSourceRead;
    private readonly CurrentAppSession _session;

    private OfficialDocumentLifecycleSearchInput? _lastSearch;
    private OfficialDocumentLifecycleViewModel _state =
        OfficialDocumentLifecycleViewModel.Empty("Search for an official document to review lifecycle state.");

    public OfficialDocumentLifecyclePresenter(
        IOfficialDocumentLifecycleReadService lifecycleRead,
        IOfficialDocumentLifecycleOperationBoundary lifecycleOperations,
        IOfficialDocumentReprintService reprints,
        IOfficialOutputSourceReadService outputSourceRead,
        CurrentAppSession session)
    {
        _lifecycleRead = lifecycleRead ?? throw new ArgumentNullException(nameof(lifecycleRead));
        _lifecycleOperations = lifecycleOperations ?? throw new ArgumentNullException(nameof(lifecycleOperations));
        _reprints = reprints ?? throw new ArgumentNullException(nameof(reprints));
        _outputSourceRead = outputSourceRead ?? throw new ArgumentNullException(nameof(outputSourceRead));
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public OfficialDocumentLifecycleViewModel State => _state;

    public async Task<OfficialDocumentLifecycleOperationResult> LoadAsync(
        OfficialDocumentLifecycleSearchInput input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result validation = ValidateSearchInput(input);

        if (validation.IsFailure)
        {
            _state = _state with
            {
                IsLoaded = false,
                HasError = true,
                ErrorCode = validation.ErrorDetail.Code,
                ErrorMessage = validation.ErrorDetail.Message,
                StatusMessage = validation.ErrorDetail.Message
            };

            return OfficialDocumentLifecycleOperationResult.Failure(
                _state,
                validation.ErrorDetail.Code,
                validation.ErrorDetail.Message);
        }

        _lastSearch = input;

        OfficialDocumentLifecycleReadRequest lifecycleRequest = new(
            input.JournalEntryId,
            input.OfficialDocumentNumber,
            input.DocumentKind,
            input.BirMachineProfileId,
            input.RegistrationScopeCode);

        Result<OfficialDocumentLifecycleResult> lifecycleResult =
            await _lifecycleRead.ReadAsync(lifecycleRequest, cancellationToken).ConfigureAwait(false);

        if (lifecycleResult.IsFailure)
        {
            _state = OfficialDocumentLifecycleViewModel.Empty(lifecycleResult.ErrorDetail.Message) with
            {
                HasError = true,
                ErrorCode = lifecycleResult.ErrorDetail.Code,
                ErrorMessage = lifecycleResult.ErrorDetail.Message
            };

            return OfficialDocumentLifecycleOperationResult.Failure(
                _state,
                lifecycleResult.ErrorDetail.Code,
                lifecycleResult.ErrorDetail.Message);
        }

        OfficialOutputSourceReadRequest sourceRequest = new(
            lifecycleResult.Value.Summary.JournalEntryId,
            lifecycleResult.Value.Summary.OfficialDocumentNumber,
            lifecycleResult.Value.Summary.DocumentKind,
            lifecycleResult.Value.Summary.BirMachineProfileId,
            lifecycleResult.Value.Summary.RegistrationScopeCode);

        Result<OfficialOutputSourceResult> sourceResult =
            await _outputSourceRead.ReadAsync(sourceRequest, cancellationToken).ConfigureAwait(false);

        string? snapshotHash = sourceResult.IsSuccess
            ? sourceResult.Value.Evidence.SnapshotPayloadHash
            : null;

        string? snapshotHashMatches = sourceResult.IsSuccess
            ? sourceResult.Value.Evidence.SnapshotHashMatches.ToString()
            : null;

        _state = MapViewModel(lifecycleResult.Value, snapshotHash, snapshotHashMatches, "Official document loaded.");

        return OfficialDocumentLifecycleOperationResult.Success(_state);
    }

    public async Task<OfficialDocumentLifecycleOperationResult> ReprintAsync(
        string? reason,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteReprintAsync(reason, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OfficialDocumentLifecycleOperationResult> VoidAsync(
        string? reason,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteLifecycleEventAsync(OfficialDocumentAuditEventKind.Void, reason, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OfficialDocumentLifecycleOperationResult> CancelAsync(
        string? reason,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteLifecycleEventAsync(OfficialDocumentAuditEventKind.Cancel, reason, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OfficialDocumentLifecycleOperationResult> CorrectionAsync(
        string? reason,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteLifecycleEventAsync(OfficialDocumentAuditEventKind.Correction, reason, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OfficialDocumentLifecycleOperationResult> RefundAsync(
        string? reason,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteLifecycleEventAsync(OfficialDocumentAuditEventKind.Refund, reason, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<OfficialDocumentLifecycleOperationResult> ExecuteReprintAsync(
        string? reason,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Failure(
                "OfficialDocumentReprint.ReasonRequired",
                "Official document reprint reason is required.");
        }

        if (_lastSearch is null || !_state.IsLoaded)
        {
            return Failure("OfficialDocumentLifecycle.DocumentNotLoaded", "Load an official document before reprint.");
        }

        if (!_session.Can(SalrynPermissions.OfficialDocumentReprint))
        {
            return Failure(
                "OfficialDocumentLifecycle.PermissionDenied",
                "Official document reprint requires OfficialDocument.Reprint permission.");
        }

        OfficialDocumentReprintRequest request = new(
            _lastSearch.JournalEntryId,
            _lastSearch.OfficialDocumentNumber,
            _lastSearch.DocumentKind,
            _lastSearch.BirMachineProfileId,
            _lastSearch.RegistrationScopeCode,
            reason,
            _session.UserId,
            ResolveTerminalId());

        Result<OfficialDocumentReprintResult> result =
            await _reprints.AppendAsync(request, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Failure(result.ErrorDetail.Code, result.ErrorDetail.Message);
        }

        return await ReloadAfterActionAsync("Reprint event recorded.", cancellationToken).ConfigureAwait(false);
    }

    private async Task<OfficialDocumentLifecycleOperationResult> ExecuteLifecycleEventAsync(
        OfficialDocumentAuditEventKind eventKind,
        string? reason,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Failure(
                "OfficialDocumentAuditEvent.ReasonRequired",
                "Official document lifecycle action reason is required.");
        }

        if (_lastSearch is null || !_state.IsLoaded)
        {
            return Failure("OfficialDocumentLifecycle.DocumentNotLoaded", "Load an official document before lifecycle action.");
        }

        OfficialDocumentAuditEventRequest auditRequest = new(
            _lastSearch.JournalEntryId,
            _lastSearch.OfficialDocumentNumber,
            _lastSearch.DocumentKind,
            _lastSearch.BirMachineProfileId,
            _lastSearch.RegistrationScopeCode,
            eventKind,
            reason,
            _session.UserId,
            ResolveTerminalId());

        Result<OfficialDocumentAuditEventResult> result =
            await _lifecycleOperations.AppendLifecycleEventAsync(
                new OfficialDocumentLifecycleOperationRequest(
                    auditRequest,
                    () => _session.Permissions),
                cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Failure(result.ErrorDetail.Code, result.ErrorDetail.Message);
        }

        return await ReloadAfterActionAsync($"{eventKind} event recorded.", cancellationToken).ConfigureAwait(false);
    }

    private async Task<OfficialDocumentLifecycleOperationResult> ReloadAfterActionAsync(
        string successMessage,
        CancellationToken cancellationToken)
    {
        if (_lastSearch is null)
        {
            _state = _state with { StatusMessage = successMessage };
            return OfficialDocumentLifecycleOperationResult.Success(_state);
        }

        OfficialDocumentLifecycleOperationResult reload =
            await LoadAsync(_lastSearch, cancellationToken).ConfigureAwait(false);

        if (reload.IsSuccess)
        {
            _state = reload.State with { StatusMessage = successMessage };
            return OfficialDocumentLifecycleOperationResult.Success(_state);
        }

        return reload;
    }

    private OfficialDocumentLifecycleViewModel MapViewModel(
        OfficialDocumentLifecycleResult lifecycle,
        string? snapshotHash,
        string? snapshotHashMatches,
        string statusMessage)
    {
        OfficialDocumentLifecycleSummary summary = lifecycle.Summary;

        IReadOnlyList<OfficialDocumentLifecycleEventRow> events = lifecycle.Events
            .Select(static eventRow => new OfficialDocumentLifecycleEventRow(
                eventRow.EventType,
                eventRow.Reason ?? string.Empty,
                eventRow.CashierUserId.ToString("D"),
                eventRow.CreatedAt,
                eventRow.TerminalId))
            .ToArray();

        string zCoverage = summary.IsCoveredByFinalizedZReading
            ? $"Finalized Z {summary.CoveringZReadingId:D} at {summary.CoveringZReadingFinalizedAt:yyyy-MM-dd HH:mm} UTC"
            : "Not covered by finalized Z-reading";

        bool terminalBlocked = summary.IsCoveredByFinalizedZReading &&
            summary.State == OfficialDocumentLifecycleState.Active;

        return new OfficialDocumentLifecycleViewModel(
            IsLoaded: true,
            OfficialDocumentNumber: summary.OfficialDocumentNumber,
            DocumentKind: summary.DocumentKind.ToString(),
            SaleId: summary.SaleId.ToString("D"),
            JournalEntryId: summary.JournalEntryId.ToString("D"),
            MachineProfileId: summary.BirMachineProfileId.ToString("D"),
            RegistrationScopeCode: summary.RegistrationScopeCode ?? string.Empty,
            TerminalId: summary.TerminalId ?? string.Empty,
            LifecycleState: summary.State.ToString(),
            SnapshotPayloadHash: snapshotHash ?? "Unavailable",
            SnapshotHashMatches: snapshotHashMatches ?? "Unavailable",
            ZCoverageStatus: zCoverage,
            Events: events,
            Issues: lifecycle.Issues.Select(static issue => issue.Code + ": " + issue.Message).ToArray(),
            CanReprint: _session.Can(SalrynPermissions.OfficialDocumentReprint) &&
                summary.State != OfficialDocumentLifecycleState.Invalid,
            CanVoid: _session.Can(SalrynPermissions.OfficialDocumentVoid) &&
                summary.State == OfficialDocumentLifecycleState.Active &&
                !terminalBlocked,
            CanCancel: _session.Can(SalrynPermissions.OfficialDocumentCancel) &&
                summary.State == OfficialDocumentLifecycleState.Active &&
                !terminalBlocked,
            CanCorrection: _session.Can(SalrynPermissions.OfficialDocumentCorrection) &&
                summary.State == OfficialDocumentLifecycleState.Active &&
                !terminalBlocked,
            CanRefund: _session.Can(SalrynPermissions.OfficialDocumentRefund) &&
                summary.State == OfficialDocumentLifecycleState.Active &&
                !terminalBlocked,
            StatusMessage: statusMessage,
            HasError: lifecycle.Issues.Count > 0,
            ErrorCode: lifecycle.Issues.Count > 0 ? lifecycle.Issues[0].Code : null,
            ErrorMessage: lifecycle.Issues.Count > 0 ? lifecycle.Issues[0].Message : null);
    }

    private static Result ValidateSearchInput(OfficialDocumentLifecycleSearchInput input)
    {
        bool hasJournalEntryId = input.JournalEntryId.HasValue && input.JournalEntryId.Value != Guid.Empty;
        bool hasOfficialDocumentNumber = !string.IsNullOrWhiteSpace(input.OfficialDocumentNumber);

        if (!hasJournalEntryId && !hasOfficialDocumentNumber)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentLifecycle.SearchReferenceRequired",
                "Enter an official document number or journal entry id."));
        }

        if (hasOfficialDocumentNumber && !input.DocumentKind.HasValue)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentLifecycle.DocumentKindRequired",
                "Document kind is required when searching by official document number."));
        }

        if (hasOfficialDocumentNumber &&
            !input.BirMachineProfileId.HasValue &&
            string.IsNullOrWhiteSpace(input.RegistrationScopeCode))
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentLifecycle.MachineProfileOrScopeRequired",
                "Machine profile id or registration scope is required when searching by official document number."));
        }

        return Result.Success();
    }

    private static string? ResolveTerminalId()
    {
        return Environment.MachineName;
    }

    private OfficialDocumentLifecycleOperationResult Failure(string errorCode, string errorMessage)
    {
        _state = _state with
        {
            HasError = true,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            StatusMessage = errorMessage
        };

        return OfficialDocumentLifecycleOperationResult.Failure(_state, errorCode, errorMessage);
    }
}
