using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Salryn.Sync.Contracts;

public sealed record CafeAttendanceBootstrapEventV1(
    Guid AttendanceEventId,
    Guid OperationId,
    Guid StoreId,
    Guid SubjectUserId,
    Guid ActorUserId,
    Guid DeviceId,
    string EventType,
    string OccurredAtUtc,
    string RecordedAtUtc,
    string PayloadHash);

public sealed record CafeAttendanceBootstrapCorrectionV1(
    Guid AttendanceCorrectionId,
    Guid OperationId,
    Guid StoreId,
    Guid AttendanceEventId,
    Guid ActorUserId,
    Guid DeviceId,
    string CorrectionKind,
    Guid? ReplacementAttendanceEventId,
    string Reason,
    string OccurredAtUtc,
    string PayloadHash);

public sealed record CafeIncidentBootstrapRootV1(
    Guid IncidentId,
    Guid StoreId,
    int CurrentVersion,
    Guid CurrentRevisionId,
    string Status,
    Guid ReporterUserId,
    Guid ReporterDeviceId,
    Guid? SubjectUserId,
    string OccurredAtUtc,
    string CreatedAtUtc,
    string UpdatedAtUtc,
    Guid? ResolvedByUserId,
    string? ResolvedAtUtc,
    string? ResolutionNote);

public sealed record CafeIncidentBootstrapRevisionV1(
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
    string CreatedAtUtc,
    string ContentHash);

public sealed record CafeIncidentBootstrapOperationV1(
    Guid OperationId,
    Guid IncidentId,
    Guid StoreId,
    Guid DeviceId,
    Guid ActorUserId,
    string OperationType,
    string PayloadHash,
    int ExpectedVersion,
    int ResultingVersion,
    Guid ResultRevisionId,
    string ResultStatus,
    string OccurredAtUtc);

public sealed record CafeIncidentBootstrapResolutionV1(
    Guid ResolutionId,
    Guid OperationId,
    Guid IncidentId,
    Guid ActorUserId,
    Guid DeviceId,
    string ResolutionNote,
    string ResolvedAtUtc,
    string ContentHash);

public sealed record CafeAttendanceIncidentBootstrapAuthorityV1(
    IReadOnlyList<CafeAttendanceBootstrapEventV1> AttendanceEvents,
    IReadOnlyList<CafeAttendanceBootstrapCorrectionV1> AttendanceCorrections,
    IReadOnlyList<CafeIncidentBootstrapRootV1> Incidents,
    IReadOnlyList<CafeIncidentBootstrapRevisionV1> IncidentRevisions,
    IReadOnlyList<CafeIncidentBootstrapOperationV1> IncidentOperations,
    IReadOnlyList<CafeIncidentBootstrapResolutionV1> IncidentResolutions);

public sealed record CafeAttendanceIncidentBootstrapV1(
    Guid CentralInstanceId,
    Guid StoreId,
    long BaselineServerSequence,
    Guid SnapshotId,
    string SnapshotHash,
    long AttendanceEventCount,
    long AttendanceCorrectionCount,
    long IncidentCount,
    long IncidentRevisionCount,
    long IncidentOperationCount,
    long IncidentResolutionCount,
    string AuthorityDigest,
    CafeAttendanceIncidentBootstrapAuthorityV1 Authority);

public static class CafeAttendanceIncidentBootstrapProtocolV1
{
    private static readonly Regex LowerHexSha256 =
        new("^[0-9a-f]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static CafeAttendanceIncidentBootstrapV1 Create(
        Guid centralInstanceId,
        Guid storeId,
        long baselineServerSequence,
        CafeAttendanceIncidentBootstrapAuthorityV1 authority)
    {
        if (centralInstanceId == Guid.Empty ||
            storeId == Guid.Empty ||
            baselineServerSequence < 0)
            throw new ArgumentException(
                "C10 bootstrap requires central/store identity and non-negative baseline.");

        CafeAttendanceIncidentBootstrapAuthorityV1 normalized =
            Normalize(
                authority ??
                throw new ArgumentNullException(nameof(authority)));

        ValidateAuthority(normalized, storeId);

        string digest =
            Sha256(JsonSerializer.Serialize(normalized, Json));

        string snapshotHash =
            Sha256(JsonSerializer.Serialize(
                new
                {
                    centralInstanceId = centralInstanceId.ToString("D"),
                    storeId = storeId.ToString("D"),
                    baselineServerSequence,
                    attendanceEventCount = normalized.AttendanceEvents.Count,
                    attendanceCorrectionCount = normalized.AttendanceCorrections.Count,
                    incidentCount = normalized.Incidents.Count,
                    incidentRevisionCount = normalized.IncidentRevisions.Count,
                    incidentOperationCount = normalized.IncidentOperations.Count,
                    incidentResolutionCount = normalized.IncidentResolutions.Count,
                    authorityDigest = digest
                },
                Json));

        byte[] hash = Convert.FromHexString(snapshotHash);
        byte[] idBytes = hash[..16];
        idBytes[6] = (byte)((idBytes[6] & 0x0f) | 0x50);
        idBytes[8] = (byte)((idBytes[8] & 0x3f) | 0x80);

        return new(
            centralInstanceId,
            storeId,
            baselineServerSequence,
            new Guid(idBytes),
            snapshotHash,
            normalized.AttendanceEvents.Count,
            normalized.AttendanceCorrections.Count,
            normalized.Incidents.Count,
            normalized.IncidentRevisions.Count,
            normalized.IncidentOperations.Count,
            normalized.IncidentResolutions.Count,
            digest,
            normalized);
    }

    public static bool Validate(
        CafeAttendanceIncidentBootstrapV1 snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        try
        {
            if (!LowerHexSha256.IsMatch(snapshot.SnapshotHash) ||
                !LowerHexSha256.IsMatch(snapshot.AuthorityDigest))
                return false;

            CafeAttendanceIncidentBootstrapV1 expected =
                Create(
                    snapshot.CentralInstanceId,
                    snapshot.StoreId,
                    snapshot.BaselineServerSequence,
                    snapshot.Authority);

            return
                expected.SnapshotId == snapshot.SnapshotId &&
                expected.SnapshotHash == snapshot.SnapshotHash &&
                expected.AuthorityDigest == snapshot.AuthorityDigest &&
                expected.AttendanceEventCount == snapshot.AttendanceEventCount &&
                expected.AttendanceCorrectionCount == snapshot.AttendanceCorrectionCount &&
                expected.IncidentCount == snapshot.IncidentCount &&
                expected.IncidentRevisionCount == snapshot.IncidentRevisionCount &&
                expected.IncidentOperationCount == snapshot.IncidentOperationCount &&
                expected.IncidentResolutionCount == snapshot.IncidentResolutionCount;
        }
        catch (Exception ex) when (
            ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static CafeAttendanceIncidentBootstrapAuthorityV1 Normalize(
        CafeAttendanceIncidentBootstrapAuthorityV1 a) =>
        new(
            (a.AttendanceEvents ?? Array.Empty<CafeAttendanceBootstrapEventV1>())
                .OrderBy(x => x.OccurredAtUtc, StringComparer.Ordinal)
                .ThenBy(x => x.AttendanceEventId)
                .ToArray(),
            (a.AttendanceCorrections ?? Array.Empty<CafeAttendanceBootstrapCorrectionV1>())
                .OrderBy(x => x.OccurredAtUtc, StringComparer.Ordinal)
                .ThenBy(x => x.AttendanceCorrectionId)
                .ToArray(),
            (a.Incidents ?? Array.Empty<CafeIncidentBootstrapRootV1>())
                .OrderBy(x => x.IncidentId)
                .ToArray(),
            (a.IncidentRevisions ?? Array.Empty<CafeIncidentBootstrapRevisionV1>())
                .OrderBy(x => x.IncidentId)
                .ThenBy(x => x.IncidentVersion)
                .ThenBy(x => x.RevisionId)
                .ToArray(),
            (a.IncidentOperations ?? Array.Empty<CafeIncidentBootstrapOperationV1>())
                .OrderBy(x => x.IncidentId)
                .ThenBy(x => x.ResultingVersion)
                .ThenBy(x => x.OperationId)
                .ToArray(),
            (a.IncidentResolutions ?? Array.Empty<CafeIncidentBootstrapResolutionV1>())
                .OrderBy(x => x.IncidentId)
                .ThenBy(x => x.ResolutionId)
                .ToArray());

    private static void ValidateAuthority(
        CafeAttendanceIncidentBootstrapAuthorityV1 a,
        Guid storeId)
    {
        if (a.AttendanceEvents.Any(x =>
                x.AttendanceEventId == Guid.Empty ||
                x.OperationId == Guid.Empty ||
                x.StoreId != storeId ||
                x.SubjectUserId == Guid.Empty ||
                x.ActorUserId == Guid.Empty ||
                x.DeviceId == Guid.Empty ||
                !LowerHexSha256.IsMatch(x.PayloadHash)) ||
            a.AttendanceCorrections.Any(x =>
                x.AttendanceCorrectionId == Guid.Empty ||
                x.OperationId == Guid.Empty ||
                x.StoreId != storeId ||
                x.AttendanceEventId == Guid.Empty ||
                x.ActorUserId == Guid.Empty ||
                x.DeviceId == Guid.Empty ||
                !LowerHexSha256.IsMatch(x.PayloadHash)) ||
            a.Incidents.Any(x =>
                x.IncidentId == Guid.Empty ||
                x.StoreId != storeId ||
                x.CurrentVersion <= 0 ||
                x.CurrentRevisionId == Guid.Empty) ||
            a.IncidentRevisions.Any(x =>
                x.RevisionId == Guid.Empty ||
                x.IncidentId == Guid.Empty ||
                x.RevisionNumber <= 0 ||
                x.IncidentVersion <= 0 ||
                !LowerHexSha256.IsMatch(x.ContentHash)) ||
            a.IncidentOperations.Any(x =>
                x.OperationId == Guid.Empty ||
                x.IncidentId == Guid.Empty ||
                x.StoreId != storeId ||
                x.ResultingVersion <= 0 ||
                !LowerHexSha256.IsMatch(x.PayloadHash)) ||
            a.IncidentResolutions.Any(x =>
                x.ResolutionId == Guid.Empty ||
                x.OperationId == Guid.Empty ||
                x.IncidentId == Guid.Empty ||
                !LowerHexSha256.IsMatch(x.ContentHash)))
            throw new ArgumentException(
                "C10 bootstrap contains invalid retained authority.");
    }

    private static string Sha256(string text) =>
        Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(text)))
            .ToLowerInvariant();
}
