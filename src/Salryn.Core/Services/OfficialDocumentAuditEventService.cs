using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Core.Services;

public enum OfficialDocumentAuditEventKind
{
    Void = 1,
    Cancel = 2,
    Correction = 3
}

public sealed record OfficialDocumentAuditEventRequest(
    Guid? JournalEntryId,
    string? OfficialDocumentNumber,
    ElectronicJournalDocumentKind? DocumentKind,
    Guid? BirMachineProfileId,
    string? RegistrationScopeCode,
    OfficialDocumentAuditEventKind EventKind,
    string? Reason,
    Guid ActorUserId,
    string? TerminalId);

public sealed record OfficialDocumentAuditEventResult(
    Guid EventId,
    Guid JournalEntryId,
    string OfficialDocumentNumber,
    OfficialDocumentAuditEventKind EventKind,
    string Reason,
    DateTimeOffset CreatedAt);

public sealed record OfficialDocumentAuditEventPersistenceRequest(
    Guid EventId,
    DateTimeOffset CreatedAt,
    OfficialDocumentAuditEventRequest Request);

public interface IOfficialDocumentAuditEventService
{
    Task<Result<OfficialDocumentAuditEventResult>> AppendAsync(
        OfficialDocumentAuditEventRequest request,
        CancellationToken cancellationToken = default);
}

public interface IOfficialDocumentAuditEventPersistence
{
    Task<Result<OfficialDocumentAuditEventResult>> AppendAsync(
        OfficialDocumentAuditEventPersistenceRequest request,
        CancellationToken cancellationToken = default);
}

// Kirch: why - official void/cancel/correction actions must append audit events instead of rewriting official journal evidence.
public sealed class OfficialDocumentAuditEventService : IOfficialDocumentAuditEventService
{
    public const int MaxReasonLength = 500;

    private readonly IOfficialDocumentAuditEventPersistence _persistence;
    private readonly Func<Guid> _idFactory;
    private readonly Func<DateTimeOffset> _clock;

    public OfficialDocumentAuditEventService(
        IOfficialDocumentAuditEventPersistence persistence,
        Func<Guid>? idFactory = null,
        Func<DateTimeOffset>? clock = null)
    {
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _idFactory = idFactory ?? Guid.NewGuid;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public Task<Result<OfficialDocumentAuditEventResult>> AppendAsync(
        OfficialDocumentAuditEventRequest request,
        CancellationToken cancellationToken = default)
    {
        Result validation = ValidateRequest(request);

        if (validation.IsFailure)
        {
            return Task.FromResult(Result<OfficialDocumentAuditEventResult>.Fail(validation.ErrorDetail));
        }

        OfficialDocumentAuditEventRequest normalizedRequest = request with
        {
            OfficialDocumentNumber = NormalizeOptional(request.OfficialDocumentNumber),
            RegistrationScopeCode = NormalizeOptional(request.RegistrationScopeCode),
            Reason = request.Reason!.Trim(),
            TerminalId = NormalizeOptional(request.TerminalId)
        };

        OfficialDocumentAuditEventPersistenceRequest persistenceRequest = new(
            EventId: _idFactory(),
            CreatedAt: _clock(),
            Request: normalizedRequest);

        return _persistence.AppendAsync(persistenceRequest, cancellationToken);
    }

    public static Result ValidateRequest(OfficialDocumentAuditEventRequest? request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentAuditEvent.RequestRequired",
                "Official document audit event request is required."));
        }

        bool hasJournalEntryId = request.JournalEntryId.HasValue;
        bool hasOfficialDocumentNumber = !string.IsNullOrWhiteSpace(request.OfficialDocumentNumber);

        if (!hasJournalEntryId && !hasOfficialDocumentNumber)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentAuditEvent.DocumentReferenceRequired",
                "Official document audit event requires a journal entry id or official document number."));
        }

        if (hasJournalEntryId && request.JournalEntryId!.Value == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentAuditEvent.InvalidJournalEntryId",
                "Official document audit event journal entry id is invalid."));
        }

        if (hasOfficialDocumentNumber)
        {
            if (!request.DocumentKind.HasValue)
            {
                return Result.Fail(Error.Validation(
                    "OfficialDocumentAuditEvent.DocumentKindRequired",
                    "Official document audit event requires document kind when using an official document number."));
            }

            if (!request.BirMachineProfileId.HasValue &&
                string.IsNullOrWhiteSpace(request.RegistrationScopeCode))
            {
                return Result.Fail(Error.Validation(
                    "OfficialDocumentAuditEvent.MachineProfileOrScopeRequired",
                    "Official document audit event requires a machine profile id or registration scope when using an official document number."));
            }
        }

        if (request.DocumentKind.HasValue &&
            !Enum.IsDefined(typeof(ElectronicJournalDocumentKind), request.DocumentKind.Value))
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentAuditEvent.UnsupportedDocumentKind",
                "Official document audit event document kind is unsupported."));
        }

        if (request.BirMachineProfileId.HasValue &&
            request.BirMachineProfileId.Value == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentAuditEvent.InvalidMachineProfileId",
                "Official document audit event machine profile id is invalid."));
        }

        if (!Enum.IsDefined(typeof(OfficialDocumentAuditEventKind), request.EventKind))
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentAuditEvent.UnsupportedEventKind",
                "Official document audit event kind is unsupported."));
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentAuditEvent.ReasonRequired",
                "Official document audit event reason is required."));
        }

        if (request.Reason.Trim().Length > MaxReasonLength)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentAuditEvent.ReasonTooLong",
                $"Official document audit event reason must be {MaxReasonLength} characters or fewer."));
        }

        if (request.ActorUserId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentAuditEvent.ActorUserRequired",
                "Official document audit event actor user id is required."));
        }

        return Result.Success();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
