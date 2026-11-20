using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

public sealed record CafeAttendanceIncidentBootstrapApplyResult(
    bool IsReplay,
    bool Reconstructed,
    Guid SnapshotId,
    string SnapshotHash,
    string AuthorityDigest,
    long BaselineServerSequence);

public sealed class SqliteCafeAttendanceIncidentBootstrapStore
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteCafeAttendanceIncidentBootstrapStore(
        SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory =
            connectionFactory ??
            throw new ArgumentNullException(nameof(connectionFactory));
    }

    public Result<CafeAttendanceIncidentBootstrapApplyResult> Apply(
        CafeAttendanceIncidentBootstrapV1 snapshot)
    {
        if (!CafeAttendanceIncidentBootstrapProtocolV1.Validate(snapshot))
            return Fail(
                "CafeAttendanceIncidentBootstrap.SnapshotIntegrityMismatch",
                "C10 bootstrap snapshot/digest does not match canonical retained authority.");

        try
        {
            using SqliteConnection connection =
                _connectionFactory.CreateConnection();
            connection.Open();
            SqlitePragma.Apply(connection);
            using SqliteTransaction transaction =
                connection.BeginTransaction();

            Guid localStore =
                ReadStoreId(connection, transaction);

            if (localStore != snapshot.StoreId)
            {
                transaction.Rollback();
                return Fail(
                    "CafeAttendanceIncidentBootstrap.StoreMismatch",
                    "C10 bootstrap StoreId does not match local deployment authority.");
            }

            string? inherited =
                ValidateInheritedF1E(
                    connection,
                    transaction,
                    snapshot);

            if (inherited is not null)
            {
                transaction.Rollback();
                return Fail(
                    "CafeAttendanceIncidentBootstrap.InheritedAuthorityConflict",
                    inherited);
            }

            ExistingState? existing =
                ReadState(
                    connection,
                    transaction,
                    snapshot.StoreId);

            CafeAttendanceIncidentBootstrapAuthorityV1 localAuthority =
                CafeAttendanceIncidentBootstrapAuthorityReader.Read(
                    connection,
                    transaction,
                    snapshot.StoreId);

            bool hasLocal =
                HasAuthority(localAuthority);

            if (existing is not null)
            {
                CafeAttendanceIncidentBootstrapV1 local =
                    CafeAttendanceIncidentBootstrapProtocolV1.Create(
                        snapshot.CentralInstanceId,
                        snapshot.StoreId,
                        snapshot.BaselineServerSequence,
                        localAuthority);

                bool exact =
                    existing.CentralInstanceId == snapshot.CentralInstanceId &&
                    existing.BaselineServerSequence == snapshot.BaselineServerSequence &&
                    existing.SnapshotId == snapshot.SnapshotId &&
                    existing.SnapshotHash == snapshot.SnapshotHash &&
                    existing.AuthorityDigest == snapshot.AuthorityDigest &&
                    SameAuthority(local, snapshot);

                transaction.Commit();

                return exact
                    ? Result<CafeAttendanceIncidentBootstrapApplyResult>.Success(
                        CreateResult(snapshot, true, false))
                    : Fail(
                        "CafeAttendanceIncidentBootstrap.EpochConflict",
                        "Existing C10 bootstrap/domain evidence differs and requires reconciliation.");
            }

            string? prerequisite =
                ValidateUserPrerequisites(
                    connection,
                    transaction,
                    snapshot.Authority);

            if (prerequisite is not null)
            {
                transaction.Rollback();
                return Fail(
                    "CafeAttendanceIncidentBootstrap.PrerequisiteMissing",
                    prerequisite);
            }

            bool reconstructed = false;

            if (hasLocal)
            {
                CafeAttendanceIncidentBootstrapV1 local =
                    CafeAttendanceIncidentBootstrapProtocolV1.Create(
                        snapshot.CentralInstanceId,
                        snapshot.StoreId,
                        snapshot.BaselineServerSequence,
                        localAuthority);

                if (!SameAuthority(local, snapshot))
                {
                    transaction.Rollback();
                    return Fail(
                        "CafeAttendanceIncidentBootstrap.NonEmptyDomainConflict",
                        "Existing C10 history differs from supplied bootstrap authority.");
                }
            }
            else
            {
                InsertAuthority(
                    connection,
                    transaction,
                    snapshot.Authority);

                reconstructed = true;

                CafeAttendanceIncidentBootstrapAuthorityV1 appliedAuthority =
                    CafeAttendanceIncidentBootstrapAuthorityReader.Read(
                        connection,
                        transaction,
                        snapshot.StoreId);

                CafeAttendanceIncidentBootstrapV1 applied =
                    CafeAttendanceIncidentBootstrapProtocolV1.Create(
                        snapshot.CentralInstanceId,
                        snapshot.StoreId,
                        snapshot.BaselineServerSequence,
                        appliedAuthority);

                if (!SameAuthority(applied, snapshot))
                {
                    transaction.Rollback();
                    return Fail(
                        "CafeAttendanceIncidentBootstrap.AuthorityMismatch",
                        "Reconstructed C10 authority does not match the supplied snapshot.");
                }
            }

            InsertState(
                connection,
                transaction,
                snapshot);

            AssertForeignKeysClean(
                connection,
                transaction);

            transaction.Commit();

            return Result<CafeAttendanceIncidentBootstrapApplyResult>.Success(
                CreateResult(
                    snapshot,
                    false,
                    reconstructed));
        }
        catch (Exception ex) when (
            ex is SqliteException or
            InvalidOperationException or
            FormatException or
            ArgumentException)
        {
            return Fail(
                "CafeAttendanceIncidentBootstrap.ApplyFailed",
                ex.Message);
        }
    }

    private static string? ValidateInheritedF1E(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeAttendanceIncidentBootstrapV1 snapshot)
    {
        SyncRemoteState? state =
            SqliteSyncPullStore.ReadRemoteState(
                connection,
                transaction,
                snapshot.StoreId);

        SyncBootstrapEvidence? evidence =
            SqliteSyncBootstrapStore.ReadBootstrapEvidence(
                connection,
                transaction,
                snapshot.StoreId);

        if (state is null ||
            !state.CentralInstanceId.HasValue ||
            evidence is null)
            return "C10 bootstrap requires accepted F1E bootstrap authority.";

        if (state.CentralInstanceId.Value != snapshot.CentralInstanceId ||
            evidence.CentralInstanceId != snapshot.CentralInstanceId ||
            evidence.StoreId != snapshot.StoreId ||
            state.LastReceivedServerSequence != snapshot.BaselineServerSequence ||
            state.LastAppliedServerSequence != snapshot.BaselineServerSequence ||
            evidence.BaselineServerSequence != snapshot.BaselineServerSequence ||
            state.State != AppliedBusinessChangeContract.Ready)
            return "C10 bootstrap must exactly extend F1E StoreId, epoch and baseline authority.";

        return null;
    }

    private static string? ValidateUserPrerequisites(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeAttendanceIncidentBootstrapAuthorityV1 a)
    {
        IEnumerable<Guid> users =
            a.AttendanceEvents.SelectMany(x =>
                new[] { x.SubjectUserId, x.ActorUserId })
            .Concat(a.AttendanceCorrections.Select(x => x.ActorUserId))
            .Concat(a.Incidents.SelectMany(x =>
                new Guid?[]
                {
                    x.ReporterUserId,
                    x.SubjectUserId,
                    x.ResolvedByUserId
                }.Where(y => y.HasValue).Select(y => y!.Value)))
            .Concat(a.IncidentRevisions.Select(x => x.ActorUserId))
            .Concat(a.IncidentOperations.Select(x => x.ActorUserId))
            .Concat(a.IncidentResolutions.Select(x => x.ActorUserId))
            .Distinct();

        foreach (Guid user in users)
        {
            using SqliteCommand command =
                connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "SELECT COUNT(*) FROM users WHERE id=$user;";
            command.Parameters.AddWithValue(
                "$user",
                user.ToString("D"));

            if (Convert.ToInt64(
                    command.ExecuteScalar() ?? 0L) != 1)
                return "C10 user prerequisite is missing: " +
                    user.ToString("D") + ".";
        }

        return null;
    }

    private static void InsertAuthority(
        SqliteConnection c,
        SqliteTransaction t,
        CafeAttendanceIncidentBootstrapAuthorityV1 a)
    {
        foreach (CafeAttendanceBootstrapEventV1 x in a.AttendanceEvents)
            Exec(c, t, """
                INSERT INTO cafe_attendance_events(
                    attendance_event_id,operation_id,store_id,subject_user_id,
                    actor_user_id,device_id,event_type,occurred_at_utc,
                    recorded_at_utc,payload_hash)
                VALUES(
                    $id,$operation,$store,$subject,$actor,$device,$type,
                    $occurred,$recorded,$hash);
                """,
                ("$id", x.AttendanceEventId.ToString("D")),
                ("$operation", x.OperationId.ToString("D")),
                ("$store", x.StoreId.ToString("D")),
                ("$subject", x.SubjectUserId.ToString("D")),
                ("$actor", x.ActorUserId.ToString("D")),
                ("$device", x.DeviceId.ToString("D")),
                ("$type", x.EventType),
                ("$occurred", x.OccurredAtUtc),
                ("$recorded", x.RecordedAtUtc),
                ("$hash", x.PayloadHash));

        foreach (CafeAttendanceBootstrapCorrectionV1 x in a.AttendanceCorrections)
            Exec(c, t, """
                INSERT INTO cafe_attendance_corrections(
                    attendance_correction_id,operation_id,store_id,
                    attendance_event_id,actor_user_id,device_id,
                    correction_kind,replacement_attendance_event_id,
                    reason,occurred_at_utc,payload_hash)
                VALUES(
                    $id,$operation,$store,$event,$actor,$device,
                    $kind,$replacement,$reason,$occurred,$hash);
                """,
                ("$id", x.AttendanceCorrectionId.ToString("D")),
                ("$operation", x.OperationId.ToString("D")),
                ("$store", x.StoreId.ToString("D")),
                ("$event", x.AttendanceEventId.ToString("D")),
                ("$actor", x.ActorUserId.ToString("D")),
                ("$device", x.DeviceId.ToString("D")),
                ("$kind", x.CorrectionKind),
                ("$replacement", DbGuid(x.ReplacementAttendanceEventId)),
                ("$reason", x.Reason),
                ("$occurred", x.OccurredAtUtc),
                ("$hash", x.PayloadHash));

        foreach (CafeIncidentBootstrapRootV1 x in a.Incidents)
            Exec(c, t, """
                INSERT INTO cafe_incidents(
                    incident_id,store_id,current_version,current_revision_id,status,
                    reporter_user_id,reporter_device_id,subject_user_id,
                    occurred_at_utc,created_at_utc,updated_at_utc,
                    resolved_by_user_id,resolved_at_utc,resolution_note)
                VALUES(
                    $id,$store,$version,$revision,$status,$reporter,$device,$subject,
                    $occurred,$created,$updated,$resolvedBy,$resolvedAt,$note);
                """,
                ("$id", x.IncidentId.ToString("D")),
                ("$store", x.StoreId.ToString("D")),
                ("$version", x.CurrentVersion),
                ("$revision", x.CurrentRevisionId.ToString("D")),
                ("$status", x.Status),
                ("$reporter", x.ReporterUserId.ToString("D")),
                ("$device", x.ReporterDeviceId.ToString("D")),
                ("$subject", DbGuid(x.SubjectUserId)),
                ("$occurred", x.OccurredAtUtc),
                ("$created", x.CreatedAtUtc),
                ("$updated", x.UpdatedAtUtc),
                ("$resolvedBy", DbGuid(x.ResolvedByUserId)),
                ("$resolvedAt", Db(x.ResolvedAtUtc)),
                ("$note", Db(x.ResolutionNote)));

        foreach (CafeIncidentBootstrapRevisionV1 x in a.IncidentRevisions)
            Exec(c, t, """
                INSERT INTO cafe_incident_revisions(
                    revision_id,incident_id,revision_number,incident_version,
                    previous_revision_id,title,description,classification,severity,
                    actor_user_id,device_id,created_at_utc,content_hash)
                VALUES(
                    $id,$incident,$number,$version,$previous,$title,$description,
                    $classification,$severity,$actor,$device,$created,$hash);
                """,
                ("$id", x.RevisionId.ToString("D")),
                ("$incident", x.IncidentId.ToString("D")),
                ("$number", x.RevisionNumber),
                ("$version", x.IncidentVersion),
                ("$previous", DbGuid(x.PreviousRevisionId)),
                ("$title", x.Title),
                ("$description", x.Description),
                ("$classification", x.Classification),
                ("$severity", x.Severity),
                ("$actor", x.ActorUserId.ToString("D")),
                ("$device", x.DeviceId.ToString("D")),
                ("$created", x.CreatedAtUtc),
                ("$hash", x.ContentHash));

        foreach (CafeIncidentBootstrapOperationV1 x in a.IncidentOperations)
            Exec(c, t, """
                INSERT INTO cafe_incident_operations(
                    operation_id,incident_id,store_id,device_id,actor_user_id,
                    operation_type,payload_hash,expected_version,resulting_version,
                    result_revision_id,result_status,occurred_at_utc)
                VALUES(
                    $operation,$incident,$store,$device,$actor,$type,$hash,
                    $expected,$resulting,$revision,$status,$occurred);
                """,
                ("$operation", x.OperationId.ToString("D")),
                ("$incident", x.IncidentId.ToString("D")),
                ("$store", x.StoreId.ToString("D")),
                ("$device", x.DeviceId.ToString("D")),
                ("$actor", x.ActorUserId.ToString("D")),
                ("$type", x.OperationType),
                ("$hash", x.PayloadHash),
                ("$expected", x.ExpectedVersion),
                ("$resulting", x.ResultingVersion),
                ("$revision", x.ResultRevisionId.ToString("D")),
                ("$status", x.ResultStatus),
                ("$occurred", x.OccurredAtUtc));

        foreach (CafeIncidentBootstrapResolutionV1 x in a.IncidentResolutions)
            Exec(c, t, """
                INSERT INTO cafe_incident_resolutions(
                    resolution_id,operation_id,incident_id,actor_user_id,
                    device_id,resolution_note,resolved_at_utc,content_hash)
                VALUES(
                    $id,$operation,$incident,$actor,$device,$note,$resolved,$hash);
                """,
                ("$id", x.ResolutionId.ToString("D")),
                ("$operation", x.OperationId.ToString("D")),
                ("$incident", x.IncidentId.ToString("D")),
                ("$actor", x.ActorUserId.ToString("D")),
                ("$device", x.DeviceId.ToString("D")),
                ("$note", x.ResolutionNote),
                ("$resolved", x.ResolvedAtUtc),
                ("$hash", x.ContentHash));
    }

    private static bool SameAuthority(
        CafeAttendanceIncidentBootstrapV1 a,
        CafeAttendanceIncidentBootstrapV1 b) =>
        a.SnapshotId == b.SnapshotId &&
        a.SnapshotHash == b.SnapshotHash &&
        a.AuthorityDigest == b.AuthorityDigest;

    private static bool HasAuthority(
        CafeAttendanceIncidentBootstrapAuthorityV1 a) =>
        a.AttendanceEvents.Count != 0 ||
        a.AttendanceCorrections.Count != 0 ||
        a.Incidents.Count != 0 ||
        a.IncidentRevisions.Count != 0 ||
        a.IncidentOperations.Count != 0 ||
        a.IncidentResolutions.Count != 0;

    private static Guid ReadStoreId(
        SqliteConnection c,
        SqliteTransaction t)
    {
        using SqliteCommand q = c.CreateCommand();
        q.Transaction = t;
        q.CommandText =
            "SELECT store_id FROM deployment_identity ORDER BY created_at LIMIT 1;";
        object? value = q.ExecuteScalar();

        return value is string text &&
            Guid.TryParse(text, out Guid id) &&
            id != Guid.Empty
                ? id
                : throw new InvalidOperationException(
                    "Local deployment StoreId missing.");
    }

    private static ExistingState? ReadState(
        SqliteConnection c,
        SqliteTransaction t,
        Guid storeId)
    {
        using SqliteCommand q = c.CreateCommand();
        q.Transaction = t;
        q.CommandText = """
            SELECT central_instance_id,baseline_server_sequence,
                   snapshot_id,snapshot_hash,authority_digest
            FROM cafe_attendance_incident_bootstrap_state
            WHERE store_id=$store
            LIMIT 1;
            """;
        q.Parameters.AddWithValue("$store", storeId.ToString("D"));
        using SqliteDataReader r = q.ExecuteReader();
        return r.Read()
            ? new(
                Guid.Parse(r.GetString(0)),
                r.GetInt64(1),
                Guid.Parse(r.GetString(2)),
                r.GetString(3),
                r.GetString(4))
            : null;
    }

    private static void InsertState(
        SqliteConnection c,
        SqliteTransaction t,
        CafeAttendanceIncidentBootstrapV1 snapshot)
    {
        Exec(c, t, """
            INSERT INTO cafe_attendance_incident_bootstrap_state(
                store_id,central_instance_id,baseline_server_sequence,
                snapshot_id,snapshot_hash,authority_digest,applied_at_utc)
            VALUES(
                $store,$instance,$baseline,$snapshot,$hash,$digest,$at);
            """,
            ("$store", snapshot.StoreId.ToString("D")),
            ("$instance", snapshot.CentralInstanceId.ToString("D")),
            ("$baseline", snapshot.BaselineServerSequence),
            ("$snapshot", snapshot.SnapshotId.ToString("D")),
            ("$hash", snapshot.SnapshotHash),
            ("$digest", snapshot.AuthorityDigest),
            ("$at", DateTimeOffset.UtcNow.ToString("O")));
    }

    private static void AssertForeignKeysClean(
        SqliteConnection c,
        SqliteTransaction t)
    {
        using SqliteCommand q = c.CreateCommand();
        q.Transaction = t;
        q.CommandText = "PRAGMA foreign_key_check;";
        using SqliteDataReader r = q.ExecuteReader();
        if (r.Read())
            throw new InvalidOperationException(
                "C10 bootstrap completed with foreign-key violations.");
    }

    private static void Exec(
        SqliteConnection c,
        SqliteTransaction t,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        using SqliteCommand q = c.CreateCommand();
        q.Transaction = t;
        q.CommandText = sql;
        foreach ((string name, object value) in parameters)
            q.Parameters.AddWithValue(name, value);
        q.ExecuteNonQuery();
    }

    private static object Db(string? value) =>
        value is null ? DBNull.Value : value;

    private static object DbGuid(Guid? value) =>
        value.HasValue
            ? value.Value.ToString("D")
            : DBNull.Value;

    private static Result<CafeAttendanceIncidentBootstrapApplyResult> Fail(
        string code,
        string message) =>
        Result<CafeAttendanceIncidentBootstrapApplyResult>.Fail(
            Error.Conflict(code, message));

    private static CafeAttendanceIncidentBootstrapApplyResult CreateResult(
        CafeAttendanceIncidentBootstrapV1 snapshot,
        bool replay,
        bool reconstructed) =>
        new(
            replay,
            reconstructed,
            snapshot.SnapshotId,
            snapshot.SnapshotHash,
            snapshot.AuthorityDigest,
            snapshot.BaselineServerSequence);

    private sealed record ExistingState(
        Guid CentralInstanceId,
        long BaselineServerSequence,
        Guid SnapshotId,
        string SnapshotHash,
        string AuthorityDigest);
}
