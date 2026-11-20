using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Salryn.Sync.Contracts;

public static class CafeAttendanceIncidentSyncContract
{
    public const int SchemaVersion = 1;

    public const string AttendanceEventRecordedV1 = "AttendanceEventRecordedV1";
    public const string AttendanceEventCorrectedV1 = "AttendanceEventCorrectedV1";
    public const string IncidentRevisionCommittedV1 = "IncidentRevisionCommittedV1";
    public const string IncidentResolvedV1 = "IncidentResolvedV1";

    public static bool IsSupported(string? value) =>
        value is AttendanceEventRecordedV1
            or AttendanceEventCorrectedV1
            or IncidentRevisionCommittedV1
            or IncidentResolvedV1;
}

public sealed record CafeAttendanceEventEvidenceV1(
    Guid AttendanceEventId,
    Guid OperationId,
    Guid StoreId,
    Guid SubjectUserId,
    Guid ActorUserId,
    Guid DeviceId,
    string EventType,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset RecordedAtUtc,
    string PayloadHash);

public sealed record AttendanceEventRecordedV1(
    CafeAttendanceEventEvidenceV1 Event);

public sealed record AttendanceEventCorrectedV1(
    Guid OperationId,
    Guid AttendanceCorrectionId,
    Guid StoreId,
    Guid AttendanceEventId,
    Guid ActorUserId,
    Guid DeviceId,
    string CorrectionKind,
    string Reason,
    DateTimeOffset OccurredAtUtc,
    Guid? ReplacementAttendanceEventId,
    CafeAttendanceEventEvidenceV1? ReplacementEvent,
    string PayloadHash);

public sealed record CafeIncidentRevisionEvidenceV1(
    Guid RevisionId,
    Guid IncidentId,
    int RevisionNumber,
    int IncidentVersion,
    Guid? PreviousRevisionId,
    string Title,
    string Description,
    string Classification,
    string Severity,
    Guid ActorUserId,
    Guid DeviceId,
    DateTimeOffset CreatedAtUtc,
    string ContentHash);

public sealed record IncidentRevisionCommittedV1(
    Guid OperationId,
    Guid IncidentId,
    Guid StoreId,
    int ExpectedVersion,
    int ResultingVersion,
    Guid ReporterUserId,
    Guid ReporterDeviceId,
    Guid? SubjectUserId,
    DateTimeOffset IncidentOccurredAtUtc,
    CafeIncidentRevisionEvidenceV1 Revision,
    Guid ActorUserId,
    Guid DeviceId,
    DateTimeOffset OccurredAtUtc,
    string PayloadHash);

public sealed record IncidentResolvedV1(
    Guid OperationId,
    Guid ResolutionId,
    Guid IncidentId,
    Guid StoreId,
    int ExpectedVersion,
    int ResultingVersion,
    Guid CurrentRevisionId,
    Guid ActorUserId,
    Guid DeviceId,
    string ResolutionNote,
    DateTimeOffset ResolvedAtUtc,
    string PayloadHash);

public sealed record CafeAttendanceIncidentParseResult<T>(
    bool IsValid,
    T? Payload,
    string? ErrorCode,
    string? ErrorMessage);

public static class CafeAttendanceIncidentCanonicalJson
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string Serialize<T>(T payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return JsonSerializer.Serialize(payload, Json);
    }

    public static CafeAttendanceIncidentParseResult<T> Parse<T>(string json)
    {
        try
        {
            T? payload = JsonSerializer.Deserialize<T>(json, Json);
            return payload is null
                ? new(false, default, "CafeAttendanceIncidentSync.PayloadMissing", "C10 semantic payload is empty.")
                : new(true, payload, null, null);
        }
        catch (JsonException ex)
        {
            return new(false, default, "CafeAttendanceIncidentSync.PayloadInvalid", ex.Message);
        }
    }

    public static string Sha256(string canonicalText) =>
        Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(canonicalText)))
            .ToLowerInvariant();
}
