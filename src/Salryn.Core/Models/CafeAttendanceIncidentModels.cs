namespace Salryn.Core.Models;

public static class CafeAttendanceEventTypes
{
    public const string ClockIn = "ClockIn";
    public const string ClockOut = "ClockOut";

    public static bool IsKnown(string? value) =>
        string.Equals(value, ClockIn, StringComparison.Ordinal) ||
        string.Equals(value, ClockOut, StringComparison.Ordinal);
}

public static class CafeAttendanceCorrectionKinds
{
    public const string Void = "Void";
    public const string Replace = "Replace";

    public static bool IsKnown(string? value) =>
        string.Equals(value, Void, StringComparison.Ordinal) ||
        string.Equals(value, Replace, StringComparison.Ordinal);
}

public static class CafeIncidentStatuses
{
    public const string Open = "Open";
    public const string Resolved = "Resolved";

    public static bool IsKnown(string? value) =>
        string.Equals(value, Open, StringComparison.Ordinal) ||
        string.Equals(value, Resolved, StringComparison.Ordinal);
}

public static class CafeIncidentClassifications
{
    public const string Operational = "Operational";
    public const string Safety = "Safety";
    public const string Conduct = "Conduct";
    public const string Equipment = "Equipment";
    public const string Other = "Other";

    public static bool IsKnown(string? value) =>
        value is Operational or Safety or Conduct or Equipment or Other;
}

public static class CafeIncidentSeverities
{
    public const string Low = "Low";
    public const string Medium = "Medium";
    public const string High = "High";
    public const string Critical = "Critical";

    public static bool IsKnown(string? value) =>
        value is Low or Medium or High or Critical;
}

public sealed record CafeAttendanceEvent(
    Guid AttendanceEventId,
    Guid OperationId,
    Guid StoreId,
    Guid SubjectUserId,
    Guid ActorUserId,
    Guid DeviceId,
    string EventType,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset RecordedAtUtc,
    string PayloadHash,
    bool IsEffective,
    Guid? CorrectionId,
    string? CorrectionKind,
    Guid? ReplacementAttendanceEventId,
    string? CorrectionReason);

public sealed record CafeAttendanceMutationResult(
    Guid AttendanceEventId,
    Guid OperationId,
    Guid SubjectUserId,
    string EventType,
    bool Idempotent);

public sealed record CafeAttendanceCorrectionResult(
    Guid AttendanceCorrectionId,
    Guid AttendanceEventId,
    string CorrectionKind,
    Guid? ReplacementAttendanceEventId,
    bool Idempotent);

public sealed record CafeAttendanceRecordMutation(
    Guid OperationId,
    Guid AttendanceEventId,
    Guid StoreId,
    Guid SubjectUserId,
    Guid ActorUserId,
    Guid DeviceId,
    string EventType,
    DateTimeOffset OccurredAtUtc);

public sealed record CafeAttendanceCorrectMutation(
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
    string? ReplacementEventType,
    DateTimeOffset? ReplacementOccurredAtUtc);

public sealed record CafeIncidentSummary(
    Guid IncidentId,
    Guid StoreId,
    int Version,
    Guid CurrentRevisionId,
    string Status,
    Guid ReporterUserId,
    Guid ReporterDeviceId,
    Guid? SubjectUserId,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    Guid? ResolvedByUserId,
    DateTimeOffset? ResolvedAtUtc,
    string? ResolutionNote,
    string Title,
    string Classification,
    string Severity);

public sealed record CafeIncidentRevision(
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

public sealed record CafeIncidentDetail(
    CafeIncidentSummary Incident,
    CafeIncidentRevision CurrentRevision,
    IReadOnlyList<CafeIncidentRevision> RevisionHistory);

public sealed record CafeIncidentMutationResult(
    Guid IncidentId,
    int Version,
    Guid CurrentRevisionId,
    string Status,
    bool Idempotent);

public sealed record CafeIncidentCreateMutation(
    Guid OperationId,
    Guid IncidentId,
    Guid RevisionId,
    Guid StoreId,
    Guid ActorUserId,
    Guid DeviceId,
    Guid? SubjectUserId,
    DateTimeOffset IncidentOccurredAtUtc,
    string Title,
    string Description,
    string Classification,
    string Severity,
    DateTimeOffset OccurredAtUtc);

public sealed record CafeIncidentReviseMutation(
    Guid OperationId,
    Guid IncidentId,
    Guid RevisionId,
    Guid StoreId,
    int ExpectedVersion,
    Guid ActorUserId,
    Guid DeviceId,
    string Title,
    string Description,
    string Classification,
    string Severity,
    DateTimeOffset OccurredAtUtc);

public sealed record CafeIncidentResolveMutation(
    Guid OperationId,
    Guid ResolutionId,
    Guid IncidentId,
    Guid StoreId,
    int ExpectedVersion,
    Guid ActorUserId,
    Guid DeviceId,
    string ResolutionNote,
    DateTimeOffset OccurredAtUtc);
