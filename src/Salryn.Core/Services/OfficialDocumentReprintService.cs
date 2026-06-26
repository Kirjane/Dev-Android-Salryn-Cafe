using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Core.Services;

public sealed record OfficialDocumentReprintRequest(
    Guid? JournalEntryId,
    string? OfficialDocumentNumber,
    ElectronicJournalDocumentKind? DocumentKind,
    Guid? BirMachineProfileId,
    string? RegistrationScopeCode,
    string? Reason,
    Guid ActorUserId,
    string? TerminalId);

public sealed record OfficialDocumentReprintResult(
    Guid EventId,
    Guid JournalEntryId,
    string OfficialDocumentNumber,
    string Reason,
    Guid ActorUserId,
    DateTimeOffset CreatedAt);

public sealed record OfficialDocumentReprintPersistenceRequest(
    Guid EventId,
    DateTimeOffset CreatedAt,
    OfficialDocumentReprintRequest Request);

public interface IOfficialDocumentReprintService
{
    Task<Result<OfficialDocumentReprintResult>> AppendAsync(
        OfficialDocumentReprintRequest request,
        CancellationToken cancellationToken = default);
}

public interface IOfficialDocumentReprintPersistence
{
    Task<Result<OfficialDocumentReprintResult>> AppendAsync(
        OfficialDocumentReprintPersistenceRequest request,
        CancellationToken cancellationToken = default);
}

// Kirch: why - reprint/copy intent is audit evidence only; it never allocates a new official number or rewrites the original document.
public sealed class OfficialDocumentReprintService : IOfficialDocumentReprintService
{
    public const int MaxReasonLength = 500;

    private readonly IOfficialDocumentLifecycleReadService _lifecycleRead;
    private readonly IOfficialDocumentReprintPersistence _persistence;
    private readonly Func<Guid> _idFactory;
    private readonly Func<DateTimeOffset> _clock;

    public OfficialDocumentReprintService(
        IOfficialDocumentLifecycleReadService lifecycleRead,
        IOfficialDocumentReprintPersistence persistence,
        Func<Guid>? idFactory = null,
        Func<DateTimeOffset>? clock = null)
    {
        _lifecycleRead = lifecycleRead ?? throw new ArgumentNullException(nameof(lifecycleRead));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _idFactory = idFactory ?? Guid.NewGuid;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<Result<OfficialDocumentReprintResult>> AppendAsync(
        OfficialDocumentReprintRequest request,
        CancellationToken cancellationToken = default)
    {
        Result validation = ValidateRequest(request);

        if (validation.IsFailure)
        {
            return Result<OfficialDocumentReprintResult>.Fail(validation.ErrorDetail);
        }

        OfficialDocumentReprintRequest normalizedRequest = NormalizeRequest(request);

        Result<OfficialDocumentLifecycleResult> lifecycleResult =
            await _lifecycleRead.ReadAsync(new OfficialDocumentLifecycleReadRequest(
                normalizedRequest.JournalEntryId,
                normalizedRequest.OfficialDocumentNumber,
                normalizedRequest.DocumentKind,
                normalizedRequest.BirMachineProfileId,
                normalizedRequest.RegistrationScopeCode), cancellationToken).ConfigureAwait(false);

        if (lifecycleResult.IsFailure)
        {
            return Result<OfficialDocumentReprintResult>.Fail(MapLifecycleReadFailure(lifecycleResult.ErrorDetail));
        }

        Result lifecycleGuard = GuardLifecycle(lifecycleResult.Value);

        if (lifecycleGuard.IsFailure)
        {
            return Result<OfficialDocumentReprintResult>.Fail(lifecycleGuard.ErrorDetail);
        }

        OfficialDocumentReprintPersistenceRequest persistenceRequest = new(
            EventId: _idFactory(),
            CreatedAt: _clock(),
            Request: normalizedRequest);

        return await _persistence.AppendAsync(persistenceRequest, cancellationToken).ConfigureAwait(false);
    }

    public static Result ValidateRequest(OfficialDocumentReprintRequest? request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentReprint.RequestRequired",
                "Official document reprint request is required."));
        }

        bool hasJournalEntryId = request.JournalEntryId.HasValue;
        bool hasOfficialDocumentNumber = !string.IsNullOrWhiteSpace(request.OfficialDocumentNumber);

        if (!hasJournalEntryId && !hasOfficialDocumentNumber)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentReprint.DocumentReferenceRequired",
                "Official document reprint requires a journal entry id or official document number."));
        }

        if (hasJournalEntryId && request.JournalEntryId!.Value == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentReprint.InvalidJournalEntryId",
                "Official document reprint journal entry id is invalid."));
        }

        if (hasOfficialDocumentNumber)
        {
            if (!request.DocumentKind.HasValue)
            {
                return Result.Fail(Error.Validation(
                    "OfficialDocumentReprint.DocumentKindRequired",
                    "Official document reprint requires document kind when using an official document number."));
            }

            if (!request.BirMachineProfileId.HasValue &&
                string.IsNullOrWhiteSpace(request.RegistrationScopeCode))
            {
                return Result.Fail(Error.Validation(
                    "OfficialDocumentReprint.MachineProfileOrScopeRequired",
                    "Official document reprint requires a machine profile id or registration scope when using an official document number."));
            }
        }

        if (request.DocumentKind.HasValue &&
            !Enum.IsDefined(typeof(ElectronicJournalDocumentKind), request.DocumentKind.Value))
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentReprint.UnsupportedDocumentKind",
                "Official document reprint document kind is unsupported."));
        }

        if (request.BirMachineProfileId.HasValue &&
            request.BirMachineProfileId.Value == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentReprint.InvalidMachineProfileId",
                "Official document reprint machine profile id is invalid."));
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentReprint.ReasonRequired",
                "Official document reprint reason is required."));
        }

        if (request.Reason.Trim().Length > MaxReasonLength)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentReprint.ReasonTooLong",
                $"Official document reprint reason must be {MaxReasonLength} characters or fewer."));
        }

        if (request.ActorUserId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentReprint.ActorUserRequired",
                "Official document reprint actor user id is required."));
        }

        return Result.Success();
    }

    public static OfficialDocumentReprintRequest NormalizeRequest(
        OfficialDocumentReprintRequest request)
    {
        return request with
        {
            OfficialDocumentNumber = NormalizeOptional(request.OfficialDocumentNumber),
            RegistrationScopeCode = NormalizeOptional(request.RegistrationScopeCode),
            Reason = request.Reason!.Trim(),
            TerminalId = NormalizeOptional(request.TerminalId)
        };
    }

    public static Result GuardLifecycle(OfficialDocumentLifecycleResult lifecycle)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);

        if (lifecycle.Summary.State != OfficialDocumentLifecycleState.Invalid)
        {
            return Result.Success();
        }

        if (lifecycle.Issues.Any(static issue =>
                string.Equals(issue.Code, "OfficialDocumentLifecycle.OriginalEventMissing", StringComparison.Ordinal)))
        {
            return Result.Fail(Error.Failure(
                "OfficialDocumentReprint.OriginalEventMissing",
                "Official document reprint requires an ORIGINAL event."));
        }

        return Result.Fail(Error.Conflict(
            "OfficialDocumentReprint.InvalidLifecycleState",
            "Official document reprint is blocked because the lifecycle state is invalid."));
    }

    private static Error MapLifecycleReadFailure(Error error)
    {
        return error.Code switch
        {
            "OfficialDocumentLifecycle.JournalEntryNotFound" => Error.NotFound(
                "OfficialDocumentReprint.JournalEntryNotFound",
                "Official journal entry was not found for the requested reprint."),
            "OfficialDocumentLifecycle.AmbiguousDocumentReference" => Error.Conflict(
                "OfficialDocumentReprint.AmbiguousDocumentReference",
                "Official document reprint reference matched more than one journal entry."),
            _ => Error.Failure(
                "OfficialDocumentReprint.LifecycleReadFailed",
                error.Message)
        };
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
