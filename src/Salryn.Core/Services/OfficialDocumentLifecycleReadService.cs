using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Core.Services;

public enum OfficialDocumentLifecycleState
{
    Active = 1,
    Voided = 2,
    Cancelled = 3,
    Invalid = 4,
    Refunded = 5
}

public sealed record OfficialDocumentLifecycleReadRequest(
    Guid? JournalEntryId,
    string? OfficialDocumentNumber,
    ElectronicJournalDocumentKind? DocumentKind,
    Guid? BirMachineProfileId,
    string? RegistrationScopeCode);

public sealed record OfficialDocumentLifecycleSummary(
    Guid JournalEntryId,
    Guid SaleId,
    Guid BirMachineProfileId,
    string? RegistrationScopeCode,
    ElectronicJournalDocumentKind DocumentKind,
    string OfficialDocumentNumber,
    DateTimeOffset TransactionTimestamp,
    Guid CashierUserId,
    string? TerminalId,
    OfficialDocumentLifecycleState State,
    int CorrectionCount,
    bool IsCoveredByFinalizedZReading,
    Guid? CoveringZReadingId,
    DateTimeOffset? CoveringZReadingFinalizedAt);

public sealed record OfficialDocumentLifecycleEventReadModel(
    Guid EventId,
    string EventType,
    bool OriginalMarker,
    bool ReprintMarker,
    bool VoidMarker,
    bool RefundMarker,
    bool CancelMarker,
    Guid CashierUserId,
    string? Reason,
    DateTimeOffset CreatedAt,
    string? TerminalId,
    string? MetadataJson);

public sealed record OfficialDocumentLifecycleCorrectionReadModel(
    Guid EventId,
    string? Reason,
    Guid CashierUserId,
    DateTimeOffset CreatedAt,
    string? TerminalId);

public sealed record OfficialDocumentLifecycleIssue(
    string Code,
    string Message);

public sealed record OfficialDocumentLifecycleResult(
    OfficialDocumentLifecycleSummary Summary,
    IReadOnlyList<OfficialDocumentLifecycleEventReadModel> Events,
    IReadOnlyList<OfficialDocumentLifecycleCorrectionReadModel> Corrections,
    IReadOnlyList<OfficialDocumentLifecycleIssue> Issues);

public sealed record OfficialDocumentLifecycleSource(
    Guid JournalEntryId,
    Guid SaleId,
    Guid BirMachineProfileId,
    string? RegistrationScopeCode,
    ElectronicJournalDocumentKind DocumentKind,
    string OfficialDocumentNumber,
    DateTimeOffset TransactionTimestamp,
    Guid CashierUserId,
    string? TerminalId);

public sealed record OfficialDocumentLifecycleZReadingCoverage(
    bool IsCovered,
    Guid? ZReadingId,
    DateTimeOffset? FinalizedAt);

public interface IOfficialDocumentLifecycleReadService
{
    Task<Result<OfficialDocumentLifecycleResult>> ReadAsync(
        OfficialDocumentLifecycleReadRequest request,
        CancellationToken cancellationToken = default);
}

public static class OfficialDocumentLifecycleReadService
{
    // Kirch: why - UI, reports, and renderers should consume this derived lifecycle state instead of interpreting raw journal event rows.
    public static Result ValidateRequest(OfficialDocumentLifecycleReadRequest? request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentLifecycle.RequestRequired",
                "Official document lifecycle read request is required."));
        }

        bool hasJournalEntryId = request.JournalEntryId.HasValue;
        bool hasOfficialDocumentNumber = !string.IsNullOrWhiteSpace(request.OfficialDocumentNumber);

        if (!hasJournalEntryId && !hasOfficialDocumentNumber)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentLifecycle.DocumentReferenceRequired",
                "Official document lifecycle read requires a journal entry id or official document number."));
        }

        if (hasJournalEntryId && request.JournalEntryId!.Value == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentLifecycle.InvalidJournalEntryId",
                "Official document lifecycle journal entry id is invalid."));
        }

        if (hasOfficialDocumentNumber)
        {
            if (!request.DocumentKind.HasValue)
            {
                return Result.Fail(Error.Validation(
                    "OfficialDocumentLifecycle.DocumentKindRequired",
                    "Official document lifecycle read requires document kind when using an official document number."));
            }

            if (!request.BirMachineProfileId.HasValue &&
                string.IsNullOrWhiteSpace(request.RegistrationScopeCode))
            {
                return Result.Fail(Error.Validation(
                    "OfficialDocumentLifecycle.MachineProfileOrScopeRequired",
                    "Official document lifecycle read requires a machine profile id or registration scope when using an official document number."));
            }
        }

        if (request.DocumentKind.HasValue &&
            !Enum.IsDefined(typeof(ElectronicJournalDocumentKind), request.DocumentKind.Value))
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentLifecycle.UnsupportedDocumentKind",
                "Official document lifecycle document kind is unsupported."));
        }

        if (request.BirMachineProfileId.HasValue &&
            request.BirMachineProfileId.Value == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentLifecycle.InvalidMachineProfileId",
                "Official document lifecycle machine profile id is invalid."));
        }

        return Result.Success();
    }

    public static OfficialDocumentLifecycleReadRequest NormalizeRequest(
        OfficialDocumentLifecycleReadRequest request)
    {
        return request with
        {
            OfficialDocumentNumber = NormalizeOptional(request.OfficialDocumentNumber),
            RegistrationScopeCode = NormalizeOptional(request.RegistrationScopeCode)
        };
    }

    public static OfficialDocumentLifecycleResult BuildResult(
        OfficialDocumentLifecycleSource source,
        IReadOnlyList<OfficialDocumentLifecycleEventReadModel> events,
        OfficialDocumentLifecycleZReadingCoverage zReadingCoverage)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(zReadingCoverage);

        List<OfficialDocumentLifecycleIssue> issues = new();

        int originalCount = events.Count(IsOriginalEvent);
        int voidCount = events.Count(IsVoidEvent);
        int cancelCount = events.Count(IsCancelEvent);
        int refundCount = events.Count(IsRefundEvent);

        if (originalCount == 0)
        {
            issues.Add(new OfficialDocumentLifecycleIssue(
                "OfficialDocumentLifecycle.OriginalEventMissing",
                "Official document lifecycle has no ORIGINAL event."));
        }
        else if (originalCount > 1)
        {
            issues.Add(new OfficialDocumentLifecycleIssue(
                "OfficialDocumentLifecycle.MultipleOriginalEvents",
                "Official document lifecycle has more than one ORIGINAL event."));
        }

        if (voidCount > 1)
        {
            issues.Add(new OfficialDocumentLifecycleIssue(
                "OfficialDocumentLifecycle.DuplicateVoidEvents",
                "Official document lifecycle has more than one VOID event."));
        }

        if (cancelCount > 1)
        {
            issues.Add(new OfficialDocumentLifecycleIssue(
                "OfficialDocumentLifecycle.DuplicateCancelEvents",
                "Official document lifecycle has more than one CANCEL event."));
        }

        if (refundCount > 1)
        {
            issues.Add(new OfficialDocumentLifecycleIssue(
                "OfficialDocumentLifecycle.DuplicateRefundEvents",
                "Official document lifecycle has more than one REFUND event."));
        }

        if (voidCount > 0 && cancelCount > 0)
        {
            issues.Add(new OfficialDocumentLifecycleIssue(
                "OfficialDocumentLifecycle.ContradictoryTerminalEvents",
                "Official document lifecycle has both VOID and CANCEL terminal events."));
        }

        if (refundCount > 0 && (voidCount > 0 || cancelCount > 0))
        {
            issues.Add(new OfficialDocumentLifecycleIssue(
                "OfficialDocumentLifecycle.ContradictoryTerminalEvents",
                "Official document lifecycle has REFUND with VOID or CANCEL terminal events."));
        }

        foreach (OfficialDocumentLifecycleEventReadModel eventRow in events)
        {
            string? markerIssueCode = GetMarkerMismatchCode(eventRow);

            if (markerIssueCode is not null)
            {
                issues.Add(new OfficialDocumentLifecycleIssue(
                    markerIssueCode,
                    "Official document lifecycle event type and marker columns are contradictory."));
            }
        }

        OfficialDocumentLifecycleState state = issues.Count > 0
            ? OfficialDocumentLifecycleState.Invalid
            : voidCount == 1
                ? OfficialDocumentLifecycleState.Voided
                : cancelCount == 1
                    ? OfficialDocumentLifecycleState.Cancelled
                    : refundCount == 1
                        ? OfficialDocumentLifecycleState.Refunded
                        : OfficialDocumentLifecycleState.Active;

        IReadOnlyList<OfficialDocumentLifecycleCorrectionReadModel> corrections =
            events
                .Where(static eventRow => string.Equals(eventRow.EventType, "CORRECTION", StringComparison.Ordinal))
                .Select(static eventRow => new OfficialDocumentLifecycleCorrectionReadModel(
                    eventRow.EventId,
                    eventRow.Reason,
                    eventRow.CashierUserId,
                    eventRow.CreatedAt,
                    eventRow.TerminalId))
                .ToArray();

        OfficialDocumentLifecycleSummary summary = new(
            source.JournalEntryId,
            source.SaleId,
            source.BirMachineProfileId,
            source.RegistrationScopeCode,
            source.DocumentKind,
            source.OfficialDocumentNumber,
            source.TransactionTimestamp,
            source.CashierUserId,
            source.TerminalId,
            state,
            corrections.Count,
            zReadingCoverage.IsCovered,
            zReadingCoverage.ZReadingId,
            zReadingCoverage.FinalizedAt);

        return new OfficialDocumentLifecycleResult(
            summary,
            events,
            corrections,
            issues);
    }

    private static bool IsOriginalEvent(OfficialDocumentLifecycleEventReadModel eventRow)
    {
        return string.Equals(eventRow.EventType, "ORIGINAL", StringComparison.Ordinal);
    }

    private static bool IsVoidEvent(OfficialDocumentLifecycleEventReadModel eventRow)
    {
        return string.Equals(eventRow.EventType, "VOID", StringComparison.Ordinal) ||
               eventRow.VoidMarker;
    }

    private static bool IsCancelEvent(OfficialDocumentLifecycleEventReadModel eventRow)
    {
        return string.Equals(eventRow.EventType, "CANCEL", StringComparison.Ordinal) ||
               eventRow.CancelMarker;
    }

    private static bool IsRefundEvent(OfficialDocumentLifecycleEventReadModel eventRow)
    {
        return string.Equals(eventRow.EventType, "REFUND", StringComparison.Ordinal) ||
               eventRow.RefundMarker;
    }

    private static string? GetMarkerMismatchCode(OfficialDocumentLifecycleEventReadModel eventRow)
    {
        return eventRow.EventType switch
        {
            "ORIGINAL" when !eventRow.OriginalMarker => "OfficialDocumentLifecycle.MarkerTypeMismatch",
            "REPRINT" when !eventRow.ReprintMarker => "OfficialDocumentLifecycle.MarkerTypeMismatch",
            "VOID" when !eventRow.VoidMarker => "OfficialDocumentLifecycle.MarkerTypeMismatch",
            "REFUND" when !eventRow.RefundMarker => "OfficialDocumentLifecycle.MarkerTypeMismatch",
            "CANCEL" when !eventRow.CancelMarker => "OfficialDocumentLifecycle.MarkerTypeMismatch",
            "CORRECTION" when eventRow.OriginalMarker ||
                              eventRow.ReprintMarker ||
                              eventRow.VoidMarker ||
                              eventRow.RefundMarker ||
                              eventRow.CancelMarker => "OfficialDocumentLifecycle.MarkerTypeMismatch",
            "EXPORT" or "PRESERVATION" or "BACKUP_RECOVERY" when eventRow.OriginalMarker ||
                                                                 eventRow.ReprintMarker ||
                                                                 eventRow.VoidMarker ||
                                                                 eventRow.RefundMarker ||
                                                                 eventRow.CancelMarker => "OfficialDocumentLifecycle.MarkerTypeMismatch",
            _ => null
        };
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
