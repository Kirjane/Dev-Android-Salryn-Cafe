using System.Globalization;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Infrastructure.Data.Sync;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Services;

public sealed partial class SqliteCafeAttendanceIncidentService
{
    public Result<CafeIncidentMutationResult> CreateIncident(
        CafeIncidentCreateMutation mutation)
    {
        string? validation = ValidateIncidentCreate(mutation);
        if (validation is not null)
            return Result<CafeIncidentMutationResult>.Fail(
                Error.Validation("CafeIncident.CreateInvalid", validation));

        string title = mutation.Title.Trim();
        string description = mutation.Description.Trim();
        DateTimeOffset incidentOccurred =
            mutation.IncidentOccurredAtUtc.ToUniversalTime();
        DateTimeOffset occurred =
            mutation.OccurredAtUtc.ToUniversalTime();

        string payloadHash = Hash(
            "IncidentRevisionCommittedV1",
            mutation.OperationId,
            mutation.IncidentId,
            mutation.RevisionId,
            mutation.StoreId,
            mutation.ActorUserId,
            mutation.DeviceId,
            mutation.SubjectUserId,
            incidentOccurred.ToString("O", CultureInfo.InvariantCulture),
            title,
            description,
            mutation.Classification,
            mutation.Severity,
            occurred.ToString("O", CultureInfo.InvariantCulture));

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Result authority = ValidateAuthority(
                connection,
                transaction,
                mutation.StoreId,
                mutation.ActorUserId,
                SalrynPermissions.IncidentRecord);
            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<CafeIncidentMutationResult>.Fail(
                    authority.ErrorDetail);
            }

            if (mutation.SubjectUserId.HasValue &&
                !ActiveUserExists(
                    connection,
                    transaction,
                    mutation.SubjectUserId.Value))
            {
                transaction.Rollback();
                return Result<CafeIncidentMutationResult>.Fail(
                    Error.Validation(
                        "CafeIncident.SubjectMissing",
                        "Incident subject must be an active existing user."));
            }

            Result<CafeIncidentMutationResult>? replay =
                TryReplayIncident(
                    connection,
                    transaction,
                    mutation.OperationId,
                    mutation.IncidentId,
                    mutation.StoreId,
                    mutation.DeviceId,
                    mutation.ActorUserId,
                    payloadHash);
            if (replay is not null)
            {
                transaction.Commit();
                return replay;
            }

            EnsureOperationUnused(
                connection,
                transaction,
                mutation.OperationId);

            if (IncidentExists(
                    connection,
                    transaction,
                    mutation.IncidentId))
                throw new C10ValidationException(
                    "CafeIncident.Duplicate",
                    "IncidentId already exists.");

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO cafe_incidents(
                        incident_id,store_id,current_version,current_revision_id,
                        status,reporter_user_id,reporter_device_id,subject_user_id,
                        occurred_at_utc,created_at_utc,updated_at_utc,
                        resolved_by_user_id,resolved_at_utc,resolution_note)
                    VALUES(
                        $incident,$store,1,$revision,'Open',$reporter,$device,$subject,
                        $incidentAt,$created,$created,NULL,NULL,NULL);
                    """;
                command.Parameters.AddWithValue("$incident", mutation.IncidentId.ToString("D"));
                command.Parameters.AddWithValue("$store", mutation.StoreId.ToString("D"));
                command.Parameters.AddWithValue("$revision", mutation.RevisionId.ToString("D"));
                command.Parameters.AddWithValue("$reporter", mutation.ActorUserId.ToString("D"));
                command.Parameters.AddWithValue("$device", mutation.DeviceId.ToString("D"));
                command.Parameters.AddWithValue(
                    "$subject",
                    mutation.SubjectUserId.HasValue
                        ? mutation.SubjectUserId.Value.ToString("D")
                        : DBNull.Value);
                command.Parameters.AddWithValue("$incidentAt", incidentOccurred.ToString("O"));
                command.Parameters.AddWithValue("$created", occurred.ToString("O"));
                command.ExecuteNonQuery();
            }

            InsertIncidentRevision(
                connection,
                transaction,
                mutation.RevisionId,
                mutation.IncidentId,
                1,
                1,
                null,
                title,
                description,
                mutation.Classification,
                mutation.Severity,
                mutation.ActorUserId,
                mutation.DeviceId,
                occurred,
                payloadHash);

            InsertIncidentOperation(
                connection,
                transaction,
                mutation.OperationId,
                mutation.IncidentId,
                mutation.StoreId,
                mutation.DeviceId,
                mutation.ActorUserId,
                CafeAttendanceIncidentSyncContract.IncidentRevisionCommittedV1,
                payloadHash,
                0,
                1,
                mutation.RevisionId,
                CafeIncidentStatuses.Open,
                occurred);

            IncidentRevisionCommittedV1 sync = new(
                mutation.OperationId,
                mutation.IncidentId,
                mutation.StoreId,
                0,
                1,
                mutation.ActorUserId,
                mutation.DeviceId,
                mutation.SubjectUserId,
                incidentOccurred,
                new(
                    mutation.RevisionId,
                    mutation.IncidentId,
                    1,
                    1,
                    null,
                    title,
                    description,
                    mutation.Classification,
                    mutation.Severity,
                    mutation.ActorUserId,
                    mutation.DeviceId,
                    occurred,
                    payloadHash),
                mutation.ActorUserId,
                mutation.DeviceId,
                occurred,
                payloadHash);

            _outbox.Enqueue(
                connection,
                transaction,
                new SyncEnqueueRequest(
                    mutation.DeviceId,
                    mutation.StoreId,
                    mutation.ActorUserId,
                    CafeAttendanceIncidentSyncContract.IncidentRevisionCommittedV1,
                    CafeAttendanceIncidentSyncContract.SchemaVersion,
                    mutation.IncidentId,
                    0,
                    occurred,
                    CafeAttendanceIncidentCanonicalJson.Serialize(sync),
                    mutation.OperationId));

            transaction.Commit();

            return Result<CafeIncidentMutationResult>.Success(
                new(
                    mutation.IncidentId,
                    1,
                    mutation.RevisionId,
                    CafeIncidentStatuses.Open,
                    false));
        }
        catch (C10ValidationException ex)
        {
            return Result<CafeIncidentMutationResult>.Fail(
                Error.Conflict(ex.Code, ex.Message));
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return Result<CafeIncidentMutationResult>.Fail(
                Error.Conflict(
                    "CafeIncident.ConstraintConflict",
                    ex.Message));
        }
        catch (Exception ex) when (
            ex is SqliteException or
            InvalidOperationException or
            FormatException or
            OverflowException or
            ArgumentException)
        {
            return Result<CafeIncidentMutationResult>.Fail(
                Error.Unexpected(
                    "CafeIncident.CreateFailed",
                    ex.Message));
        }
    }

    public Result<CafeIncidentMutationResult> ReviseIncident(
        CafeIncidentReviseMutation mutation)
    {
        string? validation = ValidateIncidentRevision(mutation);
        if (validation is not null)
            return Result<CafeIncidentMutationResult>.Fail(
                Error.Validation("CafeIncident.ReviseInvalid", validation));

        string title = mutation.Title.Trim();
        string description = mutation.Description.Trim();
        DateTimeOffset occurred =
            mutation.OccurredAtUtc.ToUniversalTime();

        string payloadHash = Hash(
            "IncidentRevisionCommittedV1",
            mutation.OperationId,
            mutation.IncidentId,
            mutation.RevisionId,
            mutation.StoreId,
            mutation.ExpectedVersion,
            mutation.ActorUserId,
            mutation.DeviceId,
            title,
            description,
            mutation.Classification,
            mutation.Severity,
            occurred.ToString("O", CultureInfo.InvariantCulture));

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Result authority = ValidateAuthority(
                connection,
                transaction,
                mutation.StoreId,
                mutation.ActorUserId,
                SalrynPermissions.IncidentManage);
            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<CafeIncidentMutationResult>.Fail(
                    authority.ErrorDetail);
            }

            Result<CafeIncidentMutationResult>? replay =
                TryReplayIncident(
                    connection,
                    transaction,
                    mutation.OperationId,
                    mutation.IncidentId,
                    mutation.StoreId,
                    mutation.DeviceId,
                    mutation.ActorUserId,
                    payloadHash);
            if (replay is not null)
            {
                transaction.Commit();
                return replay;
            }

            EnsureOperationUnused(
                connection,
                transaction,
                mutation.OperationId);

            IncidentBaseRow incident =
                ReadIncidentBase(
                    connection,
                    transaction,
                    mutation.IncidentId)
                ?? throw new C10ValidationException(
                    "CafeIncident.Missing",
                    "Incident was not found.");

            if (incident.StoreId != mutation.StoreId ||
                incident.Version != mutation.ExpectedVersion)
                throw new C10ValidationException(
                    "CafeIncident.ExpectedVersionConflict",
                    "Incident version/store authority changed; last-write-wins is not allowed.");

            if (!string.Equals(
                    incident.Status,
                    CafeIncidentStatuses.Open,
                    StringComparison.Ordinal))
                throw new C10ValidationException(
                    "CafeIncident.ResolvedTerminal",
                    "Resolved incident cannot be revised.");

            int version = checked(mutation.ExpectedVersion + 1);
            int revisionNumber =
                checked(
                    ReadRevisionNumber(
                        connection,
                        transaction,
                        incident.CurrentRevisionId) + 1);

            InsertIncidentRevision(
                connection,
                transaction,
                mutation.RevisionId,
                mutation.IncidentId,
                revisionNumber,
                version,
                incident.CurrentRevisionId,
                title,
                description,
                mutation.Classification,
                mutation.Severity,
                mutation.ActorUserId,
                mutation.DeviceId,
                occurred,
                payloadHash);

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE cafe_incidents
                    SET current_version=$version,
                        current_revision_id=$revision,
                        updated_at_utc=$at
                    WHERE incident_id=$incident
                      AND store_id=$store
                      AND current_version=$expected
                      AND status='Open';
                    """;
                command.Parameters.AddWithValue("$version", version);
                command.Parameters.AddWithValue("$revision", mutation.RevisionId.ToString("D"));
                command.Parameters.AddWithValue("$at", occurred.ToString("O"));
                command.Parameters.AddWithValue("$incident", mutation.IncidentId.ToString("D"));
                command.Parameters.AddWithValue("$store", mutation.StoreId.ToString("D"));
                command.Parameters.AddWithValue("$expected", mutation.ExpectedVersion);
                if (command.ExecuteNonQuery() != 1)
                    throw new C10ValidationException(
                        "CafeIncident.ConcurrentRevisionConflict",
                        "Incident lost revision ownership.");
            }

            InsertIncidentOperation(
                connection,
                transaction,
                mutation.OperationId,
                mutation.IncidentId,
                mutation.StoreId,
                mutation.DeviceId,
                mutation.ActorUserId,
                CafeAttendanceIncidentSyncContract.IncidentRevisionCommittedV1,
                payloadHash,
                mutation.ExpectedVersion,
                version,
                mutation.RevisionId,
                CafeIncidentStatuses.Open,
                occurred);

            IncidentRevisionCommittedV1 sync = new(
                mutation.OperationId,
                mutation.IncidentId,
                mutation.StoreId,
                mutation.ExpectedVersion,
                version,
                incident.ReporterUserId,
                incident.ReporterDeviceId,
                incident.SubjectUserId,
                incident.IncidentOccurredAtUtc,
                new(
                    mutation.RevisionId,
                    mutation.IncidentId,
                    revisionNumber,
                    version,
                    incident.CurrentRevisionId,
                    title,
                    description,
                    mutation.Classification,
                    mutation.Severity,
                    mutation.ActorUserId,
                    mutation.DeviceId,
                    occurred,
                    payloadHash),
                mutation.ActorUserId,
                mutation.DeviceId,
                occurred,
                payloadHash);

            _outbox.Enqueue(
                connection,
                transaction,
                new SyncEnqueueRequest(
                    mutation.DeviceId,
                    mutation.StoreId,
                    mutation.ActorUserId,
                    CafeAttendanceIncidentSyncContract.IncidentRevisionCommittedV1,
                    CafeAttendanceIncidentSyncContract.SchemaVersion,
                    mutation.IncidentId,
                    mutation.ExpectedVersion,
                    occurred,
                    CafeAttendanceIncidentCanonicalJson.Serialize(sync),
                    mutation.OperationId));

            transaction.Commit();
            return Result<CafeIncidentMutationResult>.Success(
                new(
                    mutation.IncidentId,
                    version,
                    mutation.RevisionId,
                    CafeIncidentStatuses.Open,
                    false));
        }
        catch (C10ValidationException ex)
        {
            return Result<CafeIncidentMutationResult>.Fail(
                Error.Conflict(ex.Code, ex.Message));
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return Result<CafeIncidentMutationResult>.Fail(
                Error.Conflict(
                    "CafeIncident.RevisionConstraintConflict",
                    ex.Message));
        }
        catch (Exception ex) when (
            ex is SqliteException or
            InvalidOperationException or
            FormatException or
            OverflowException or
            ArgumentException)
        {
            return Result<CafeIncidentMutationResult>.Fail(
                Error.Unexpected(
                    "CafeIncident.ReviseFailed",
                    ex.Message));
        }
    }

    public Result<CafeIncidentMutationResult> ResolveIncident(
        CafeIncidentResolveMutation mutation)
    {
        if (mutation.OperationId == Guid.Empty ||
            mutation.ResolutionId == Guid.Empty ||
            mutation.IncidentId == Guid.Empty ||
            mutation.StoreId == Guid.Empty ||
            mutation.ActorUserId == Guid.Empty ||
            mutation.DeviceId == Guid.Empty ||
            mutation.ExpectedVersion <= 0 ||
            string.IsNullOrWhiteSpace(mutation.ResolutionNote))
            return Result<CafeIncidentMutationResult>.Fail(
                Error.Validation(
                    "CafeIncident.ResolveInvalid",
                    "Operation, resolution, incident, store, actor, device, expected version and resolution note are required."));

        string note = mutation.ResolutionNote.Trim();
        DateTimeOffset occurred =
            mutation.OccurredAtUtc.ToUniversalTime();

        string payloadHash = Hash(
            "IncidentResolvedV1",
            mutation.OperationId,
            mutation.ResolutionId,
            mutation.IncidentId,
            mutation.StoreId,
            mutation.ExpectedVersion,
            mutation.ActorUserId,
            mutation.DeviceId,
            note,
            occurred.ToString("O", CultureInfo.InvariantCulture));

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Result authority = ValidateAuthority(
                connection,
                transaction,
                mutation.StoreId,
                mutation.ActorUserId,
                SalrynPermissions.IncidentManage);
            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<CafeIncidentMutationResult>.Fail(
                    authority.ErrorDetail);
            }

            Result<CafeIncidentMutationResult>? replay =
                TryReplayIncident(
                    connection,
                    transaction,
                    mutation.OperationId,
                    mutation.IncidentId,
                    mutation.StoreId,
                    mutation.DeviceId,
                    mutation.ActorUserId,
                    payloadHash);
            if (replay is not null)
            {
                transaction.Commit();
                return replay;
            }

            EnsureOperationUnused(
                connection,
                transaction,
                mutation.OperationId);

            IncidentBaseRow incident =
                ReadIncidentBase(
                    connection,
                    transaction,
                    mutation.IncidentId)
                ?? throw new C10ValidationException(
                    "CafeIncident.Missing",
                    "Incident was not found.");

            if (incident.StoreId != mutation.StoreId ||
                incident.Version != mutation.ExpectedVersion)
                throw new C10ValidationException(
                    "CafeIncident.ExpectedVersionConflict",
                    "Incident version/store authority changed; last-write-wins is not allowed.");

            if (!string.Equals(
                    incident.Status,
                    CafeIncidentStatuses.Open,
                    StringComparison.Ordinal))
                throw new C10ValidationException(
                    "CafeIncident.AlreadyResolved",
                    "Incident is already resolved.");

            int version = checked(mutation.ExpectedVersion + 1);

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE cafe_incidents
                    SET current_version=$version,
                        status='Resolved',
                        updated_at_utc=$at,
                        resolved_by_user_id=$actor,
                        resolved_at_utc=$at,
                        resolution_note=$note
                    WHERE incident_id=$incident
                      AND store_id=$store
                      AND current_version=$expected
                      AND status='Open';
                    """;
                command.Parameters.AddWithValue("$version", version);
                command.Parameters.AddWithValue("$at", occurred.ToString("O"));
                command.Parameters.AddWithValue("$actor", mutation.ActorUserId.ToString("D"));
                command.Parameters.AddWithValue("$note", note);
                command.Parameters.AddWithValue("$incident", mutation.IncidentId.ToString("D"));
                command.Parameters.AddWithValue("$store", mutation.StoreId.ToString("D"));
                command.Parameters.AddWithValue("$expected", mutation.ExpectedVersion);
                if (command.ExecuteNonQuery() != 1)
                    throw new C10ValidationException(
                        "CafeIncident.ConcurrentResolutionConflict",
                        "Incident lost resolution ownership.");
            }

            InsertIncidentOperation(
                connection,
                transaction,
                mutation.OperationId,
                mutation.IncidentId,
                mutation.StoreId,
                mutation.DeviceId,
                mutation.ActorUserId,
                CafeAttendanceIncidentSyncContract.IncidentResolvedV1,
                payloadHash,
                mutation.ExpectedVersion,
                version,
                incident.CurrentRevisionId,
                CafeIncidentStatuses.Resolved,
                occurred);

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO cafe_incident_resolutions(
                        resolution_id,operation_id,incident_id,actor_user_id,
                        device_id,resolution_note,resolved_at_utc,content_hash)
                    VALUES(
                        $resolution,$operation,$incident,$actor,$device,
                        $note,$at,$hash);
                    """;
                command.Parameters.AddWithValue("$resolution", mutation.ResolutionId.ToString("D"));
                command.Parameters.AddWithValue("$operation", mutation.OperationId.ToString("D"));
                command.Parameters.AddWithValue("$incident", mutation.IncidentId.ToString("D"));
                command.Parameters.AddWithValue("$actor", mutation.ActorUserId.ToString("D"));
                command.Parameters.AddWithValue("$device", mutation.DeviceId.ToString("D"));
                command.Parameters.AddWithValue("$note", note);
                command.Parameters.AddWithValue("$at", occurred.ToString("O"));
                command.Parameters.AddWithValue("$hash", payloadHash);
                command.ExecuteNonQuery();
            }

            IncidentResolvedV1 sync = new(
                mutation.OperationId,
                mutation.ResolutionId,
                mutation.IncidentId,
                mutation.StoreId,
                mutation.ExpectedVersion,
                version,
                incident.CurrentRevisionId,
                mutation.ActorUserId,
                mutation.DeviceId,
                note,
                occurred,
                payloadHash);

            _outbox.Enqueue(
                connection,
                transaction,
                new SyncEnqueueRequest(
                    mutation.DeviceId,
                    mutation.StoreId,
                    mutation.ActorUserId,
                    CafeAttendanceIncidentSyncContract.IncidentResolvedV1,
                    CafeAttendanceIncidentSyncContract.SchemaVersion,
                    mutation.IncidentId,
                    mutation.ExpectedVersion,
                    occurred,
                    CafeAttendanceIncidentCanonicalJson.Serialize(sync),
                    mutation.OperationId));

            transaction.Commit();
            return Result<CafeIncidentMutationResult>.Success(
                new(
                    mutation.IncidentId,
                    version,
                    incident.CurrentRevisionId,
                    CafeIncidentStatuses.Resolved,
                    false));
        }
        catch (C10ValidationException ex)
        {
            return Result<CafeIncidentMutationResult>.Fail(
                Error.Conflict(ex.Code, ex.Message));
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return Result<CafeIncidentMutationResult>.Fail(
                Error.Conflict(
                    "CafeIncident.ResolutionConstraintConflict",
                    ex.Message));
        }
        catch (Exception ex) when (
            ex is SqliteException or
            InvalidOperationException or
            FormatException or
            OverflowException or
            ArgumentException)
        {
            return Result<CafeIncidentMutationResult>.Fail(
                Error.Unexpected(
                    "CafeIncident.ResolveFailed",
                    ex.Message));
        }
    }

    public Result<IReadOnlyList<CafeIncidentSummary>> ListIncidents(
        Guid storeId,
        Guid actorUserId,
        string? status = null)
    {
        if (storeId == Guid.Empty || actorUserId == Guid.Empty)
            return Result<IReadOnlyList<CafeIncidentSummary>>.Fail(
                Error.Validation(
                    "CafeIncident.ListIdentityInvalid",
                    "Store and actor identity are required."));

        string? normalizedStatus =
            string.IsNullOrWhiteSpace(status)
                ? null
                : status.Trim();

        if (normalizedStatus is not null &&
            !CafeIncidentStatuses.IsKnown(normalizedStatus))
            return Result<IReadOnlyList<CafeIncidentSummary>>.Fail(
                Error.Validation(
                    "CafeIncident.StatusInvalid",
                    "Incident status filter is invalid."));

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Result authority = ValidateAuthority(
                connection,
                transaction,
                storeId,
                actorUserId,
                SalrynPermissions.IncidentView);
            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<IReadOnlyList<CafeIncidentSummary>>.Fail(
                    authority.ErrorDetail);
            }

            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT
                    i.incident_id,i.store_id,i.current_version,
                    i.current_revision_id,i.status,i.reporter_user_id,
                    i.reporter_device_id,i.subject_user_id,i.occurred_at_utc,
                    i.created_at_utc,i.updated_at_utc,i.resolved_by_user_id,
                    i.resolved_at_utc,i.resolution_note,
                    r.title,r.classification,r.severity
                FROM cafe_incidents i
                INNER JOIN cafe_incident_revisions r
                  ON r.revision_id=i.current_revision_id
                WHERE i.store_id=$store
                  AND ($status IS NULL OR i.status=$status)
                ORDER BY i.occurred_at_utc DESC,i.incident_id ASC;
                """;
            command.Parameters.AddWithValue("$store", storeId.ToString("D"));
            command.Parameters.AddWithValue(
                "$status",
                normalizedStatus is null ? DBNull.Value : normalizedStatus);

            List<CafeIncidentSummary> incidents = new();
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                incidents.Add(MapIncidentSummary(reader));

            transaction.Commit();
            return Result<IReadOnlyList<CafeIncidentSummary>>.Success(incidents);
        }
        catch (Exception ex) when (
            ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Result<IReadOnlyList<CafeIncidentSummary>>.Fail(
                Error.Unexpected("CafeIncident.ListFailed", ex.Message));
        }
    }

    public Result<CafeIncidentDetail?> GetIncident(
        Guid storeId,
        Guid actorUserId,
        Guid incidentId)
    {
        if (storeId == Guid.Empty ||
            actorUserId == Guid.Empty ||
            incidentId == Guid.Empty)
            return Result<CafeIncidentDetail?>.Fail(
                Error.Validation(
                    "CafeIncident.ReadIdentityInvalid",
                    "Store, actor and incident identity are required."));

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Result authority = ValidateAuthority(
                connection,
                transaction,
                storeId,
                actorUserId,
                SalrynPermissions.IncidentView);
            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<CafeIncidentDetail?>.Fail(
                    authority.ErrorDetail);
            }

            using SqliteCommand summaryCommand = connection.CreateCommand();
            summaryCommand.Transaction = transaction;
            summaryCommand.CommandText = """
                SELECT
                    i.incident_id,i.store_id,i.current_version,
                    i.current_revision_id,i.status,i.reporter_user_id,
                    i.reporter_device_id,i.subject_user_id,i.occurred_at_utc,
                    i.created_at_utc,i.updated_at_utc,i.resolved_by_user_id,
                    i.resolved_at_utc,i.resolution_note,
                    r.title,r.classification,r.severity
                FROM cafe_incidents i
                INNER JOIN cafe_incident_revisions r
                  ON r.revision_id=i.current_revision_id
                WHERE i.store_id=$store AND i.incident_id=$incident
                LIMIT 1;
                """;
            summaryCommand.Parameters.AddWithValue("$store", storeId.ToString("D"));
            summaryCommand.Parameters.AddWithValue("$incident", incidentId.ToString("D"));

            CafeIncidentSummary? summary;
            using (SqliteDataReader reader = summaryCommand.ExecuteReader())
                summary = reader.Read() ? MapIncidentSummary(reader) : null;

            if (summary is null)
            {
                transaction.Commit();
                return Result<CafeIncidentDetail?>.Success(null);
            }

            using SqliteCommand revisionCommand = connection.CreateCommand();
            revisionCommand.Transaction = transaction;
            revisionCommand.CommandText = """
                SELECT
                    revision_id,incident_id,revision_number,incident_version,
                    previous_revision_id,title,description,classification,severity,
                    actor_user_id,device_id,created_at_utc,content_hash
                FROM cafe_incident_revisions
                WHERE incident_id=$incident
                ORDER BY revision_number ASC,revision_id ASC;
                """;
            revisionCommand.Parameters.AddWithValue("$incident", incidentId.ToString("D"));

            List<CafeIncidentRevision> history = new();
            using (SqliteDataReader reader = revisionCommand.ExecuteReader())
                while (reader.Read())
                    history.Add(MapIncidentRevision(reader));

            CafeIncidentRevision current =
                history.First(x => x.RevisionId == summary.CurrentRevisionId);

            transaction.Commit();
            return Result<CafeIncidentDetail?>.Success(
                new(summary, current, history));
        }
        catch (Exception ex) when (
            ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Result<CafeIncidentDetail?>.Fail(
                Error.Unexpected("CafeIncident.ReadFailed", ex.Message));
        }
    }

    private static Result<CafeIncidentMutationResult>? TryReplayIncident(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        Guid incidentId,
        Guid storeId,
        Guid deviceId,
        Guid actorUserId,
        string payloadHash)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT incident_id,store_id,device_id,actor_user_id,payload_hash,
                   resulting_version,result_revision_id,result_status
            FROM cafe_incident_operations
            WHERE operation_id=$operation
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        bool same =
            Guid.Parse(reader.GetString(0)) == incidentId &&
            Guid.Parse(reader.GetString(1)) == storeId &&
            Guid.Parse(reader.GetString(2)) == deviceId &&
            Guid.Parse(reader.GetString(3)) == actorUserId &&
            string.Equals(reader.GetString(4), payloadHash, StringComparison.Ordinal);

        if (!same)
            return Result<CafeIncidentMutationResult>.Fail(
                Error.Conflict(
                    "CafeIncident.OperationCollision",
                    "OperationId already exists with different incident evidence."));

        int version = reader.GetInt32(5);
        Guid revisionId = Guid.Parse(reader.GetString(6));
        string status = reader.GetString(7);

        return Result<CafeIncidentMutationResult>.Success(
            new(incidentId, version, revisionId, status, true));
    }

    private static IncidentBaseRow? ReadIncidentBase(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid incidentId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT store_id,current_version,current_revision_id,status,
                   reporter_user_id,reporter_device_id,subject_user_id,
                   occurred_at_utc
            FROM cafe_incidents
            WHERE incident_id=$incident
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$incident", incidentId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? new(
                Guid.Parse(reader.GetString(0)),
                reader.GetInt32(1),
                Guid.Parse(reader.GetString(2)),
                reader.GetString(3),
                Guid.Parse(reader.GetString(4)),
                Guid.Parse(reader.GetString(5)),
                reader.IsDBNull(6) ? null : Guid.Parse(reader.GetString(6)),
                ParseDate(reader.GetString(7)))
            : null;
    }

    private static bool IncidentExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid incidentId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT COUNT(*) FROM cafe_incidents WHERE incident_id=$incident;";
        command.Parameters.AddWithValue("$incident", incidentId.ToString("D"));
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) != 0;
    }

    private static int ReadRevisionNumber(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid revisionId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT revision_number
            FROM cafe_incident_revisions
            WHERE revision_id=$revision;
            """;
        command.Parameters.AddWithValue("$revision", revisionId.ToString("D"));
        return Convert.ToInt32(
            command.ExecuteScalar()
            ?? throw new C10ValidationException(
                "CafeIncident.RevisionMissing",
                "Current incident revision is missing."));
    }

    private static void InsertIncidentRevision(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid revisionId,
        Guid incidentId,
        int revisionNumber,
        int incidentVersion,
        Guid? previousRevisionId,
        string title,
        string description,
        string classification,
        string severity,
        Guid actorUserId,
        Guid deviceId,
        DateTimeOffset createdAtUtc,
        string contentHash)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cafe_incident_revisions(
                revision_id,incident_id,revision_number,incident_version,
                previous_revision_id,title,description,classification,severity,
                actor_user_id,device_id,created_at_utc,content_hash)
            VALUES(
                $revision,$incident,$number,$version,$previous,$title,$description,
                $classification,$severity,$actor,$device,$created,$hash);
            """;
        command.Parameters.AddWithValue("$revision", revisionId.ToString("D"));
        command.Parameters.AddWithValue("$incident", incidentId.ToString("D"));
        command.Parameters.AddWithValue("$number", revisionNumber);
        command.Parameters.AddWithValue("$version", incidentVersion);
        command.Parameters.AddWithValue(
            "$previous",
            previousRevisionId.HasValue
                ? previousRevisionId.Value.ToString("D")
                : DBNull.Value);
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$description", description);
        command.Parameters.AddWithValue("$classification", classification);
        command.Parameters.AddWithValue("$severity", severity);
        command.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
        command.Parameters.AddWithValue("$device", deviceId.ToString("D"));
        command.Parameters.AddWithValue("$created", createdAtUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$hash", contentHash);
        command.ExecuteNonQuery();
    }

    private static void InsertIncidentOperation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        Guid incidentId,
        Guid storeId,
        Guid deviceId,
        Guid actorUserId,
        string operationType,
        string payloadHash,
        int expectedVersion,
        int resultingVersion,
        Guid resultRevisionId,
        string resultStatus,
        DateTimeOffset occurredAtUtc)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cafe_incident_operations(
                operation_id,incident_id,store_id,device_id,actor_user_id,
                operation_type,payload_hash,expected_version,resulting_version,
                result_revision_id,result_status,occurred_at_utc)
            VALUES(
                $operation,$incident,$store,$device,$actor,
                $type,$hash,$expected,$resulting,$revision,$status,$at);
            """;
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));
        command.Parameters.AddWithValue("$incident", incidentId.ToString("D"));
        command.Parameters.AddWithValue("$store", storeId.ToString("D"));
        command.Parameters.AddWithValue("$device", deviceId.ToString("D"));
        command.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
        command.Parameters.AddWithValue("$type", operationType);
        command.Parameters.AddWithValue("$hash", payloadHash);
        command.Parameters.AddWithValue("$expected", expectedVersion);
        command.Parameters.AddWithValue("$resulting", resultingVersion);
        command.Parameters.AddWithValue("$revision", resultRevisionId.ToString("D"));
        command.Parameters.AddWithValue("$status", resultStatus);
        command.Parameters.AddWithValue("$at", occurredAtUtc.ToUniversalTime().ToString("O"));
        command.ExecuteNonQuery();
    }

    private static CafeIncidentSummary MapIncidentSummary(
        SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            reader.GetInt32(2),
            Guid.Parse(reader.GetString(3)),
            reader.GetString(4),
            Guid.Parse(reader.GetString(5)),
            Guid.Parse(reader.GetString(6)),
            reader.IsDBNull(7) ? null : Guid.Parse(reader.GetString(7)),
            ParseDate(reader.GetString(8)),
            ParseDate(reader.GetString(9)),
            ParseDate(reader.GetString(10)),
            reader.IsDBNull(11) ? null : Guid.Parse(reader.GetString(11)),
            reader.IsDBNull(12) ? null : ParseDate(reader.GetString(12)),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.GetString(14),
            reader.GetString(15),
            reader.GetString(16));

    private static CafeIncidentRevision MapIncidentRevision(
        SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4)),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            Guid.Parse(reader.GetString(9)),
            Guid.Parse(reader.GetString(10)),
            ParseDate(reader.GetString(11)),
            reader.GetString(12));

    private static string? ValidateIncidentCreate(
        CafeIncidentCreateMutation mutation)
    {
        if (mutation.OperationId == Guid.Empty ||
            mutation.IncidentId == Guid.Empty ||
            mutation.RevisionId == Guid.Empty ||
            mutation.StoreId == Guid.Empty ||
            mutation.ActorUserId == Guid.Empty ||
            mutation.DeviceId == Guid.Empty)
            return "Operation, incident, revision, store, actor and device IDs are required.";

        return ValidateIncidentContent(
            mutation.Title,
            mutation.Description,
            mutation.Classification,
            mutation.Severity);
    }

    private static string? ValidateIncidentRevision(
        CafeIncidentReviseMutation mutation)
    {
        if (mutation.OperationId == Guid.Empty ||
            mutation.IncidentId == Guid.Empty ||
            mutation.RevisionId == Guid.Empty ||
            mutation.StoreId == Guid.Empty ||
            mutation.ExpectedVersion <= 0 ||
            mutation.ActorUserId == Guid.Empty ||
            mutation.DeviceId == Guid.Empty)
            return "Operation, incident, revision, store, expected version, actor and device IDs are required.";

        return ValidateIncidentContent(
            mutation.Title,
            mutation.Description,
            mutation.Classification,
            mutation.Severity);
    }

    private static string? ValidateIncidentContent(
        string title,
        string description,
        string classification,
        string severity)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "Incident title is required.";
        if (string.IsNullOrWhiteSpace(description))
            return "Incident description is required.";
        if (!CafeIncidentClassifications.IsKnown(classification))
            return "Incident classification is invalid.";
        if (!CafeIncidentSeverities.IsKnown(severity))
            return "Incident severity is invalid.";
        return null;
    }

    private sealed record IncidentBaseRow(
        Guid StoreId,
        int Version,
        Guid CurrentRevisionId,
        string Status,
        Guid ReporterUserId,
        Guid ReporterDeviceId,
        Guid? SubjectUserId,
        DateTimeOffset IncidentOccurredAtUtc);
}
