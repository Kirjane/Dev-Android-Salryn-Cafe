using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Core.Services;

public interface ICafeAttendanceIncidentService
{
    Result<CafeAttendanceMutationResult> RecordAttendance(
        CafeAttendanceRecordMutation mutation);

    Result<CafeAttendanceCorrectionResult> CorrectAttendance(
        CafeAttendanceCorrectMutation mutation);

    Result<IReadOnlyList<CafeAttendanceEvent>> ListAttendance(
        Guid storeId,
        Guid actorUserId,
        Guid? subjectUserId = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null);

    Result<CafeIncidentMutationResult> CreateIncident(
        CafeIncidentCreateMutation mutation);

    Result<CafeIncidentMutationResult> ReviseIncident(
        CafeIncidentReviseMutation mutation);

    Result<CafeIncidentMutationResult> ResolveIncident(
        CafeIncidentResolveMutation mutation);

    Result<IReadOnlyList<CafeIncidentSummary>> ListIncidents(
        Guid storeId,
        Guid actorUserId,
        string? status = null);

    Result<CafeIncidentDetail?> GetIncident(
        Guid storeId,
        Guid actorUserId,
        Guid incidentId);
}
