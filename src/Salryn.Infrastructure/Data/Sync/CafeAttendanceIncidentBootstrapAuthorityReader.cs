using Microsoft.Data.Sqlite;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

public static class CafeAttendanceIncidentBootstrapAuthorityReader
{
    public static CafeAttendanceIncidentBootstrapAuthorityV1 Read(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        string store = storeId.ToString("D");

        return new(
            ReadAttendanceEvents(connection, transaction, store),
            ReadAttendanceCorrections(connection, transaction, store),
            ReadIncidents(connection, transaction, store),
            ReadIncidentRevisions(connection, transaction, store),
            ReadIncidentOperations(connection, transaction, store),
            ReadIncidentResolutions(connection, transaction, store));
    }

    private static IReadOnlyList<CafeAttendanceBootstrapEventV1>
        ReadAttendanceEvents(
            SqliteConnection c,
            SqliteTransaction t,
            string store)
    {
        List<CafeAttendanceBootstrapEventV1> rows = new();
        using SqliteCommand q = Command(c, t, """
            SELECT attendance_event_id,operation_id,store_id,
                   subject_user_id,actor_user_id,device_id,event_type,
                   occurred_at_utc,recorded_at_utc,payload_hash
            FROM cafe_attendance_events
            WHERE store_id=$store
            ORDER BY occurred_at_utc,attendance_event_id;
            """, store);
        using SqliteDataReader r = q.ExecuteReader();
        while (r.Read())
            rows.Add(new(
                Guid.Parse(r.GetString(0)),
                Guid.Parse(r.GetString(1)),
                Guid.Parse(r.GetString(2)),
                Guid.Parse(r.GetString(3)),
                Guid.Parse(r.GetString(4)),
                Guid.Parse(r.GetString(5)),
                r.GetString(6),
                r.GetString(7),
                r.GetString(8),
                r.GetString(9)));
        return rows;
    }

    private static IReadOnlyList<CafeAttendanceBootstrapCorrectionV1>
        ReadAttendanceCorrections(
            SqliteConnection c,
            SqliteTransaction t,
            string store)
    {
        List<CafeAttendanceBootstrapCorrectionV1> rows = new();
        using SqliteCommand q = Command(c, t, """
            SELECT attendance_correction_id,operation_id,store_id,
                   attendance_event_id,actor_user_id,device_id,
                   correction_kind,replacement_attendance_event_id,
                   reason,occurred_at_utc,payload_hash
            FROM cafe_attendance_corrections
            WHERE store_id=$store
            ORDER BY occurred_at_utc,attendance_correction_id;
            """, store);
        using SqliteDataReader r = q.ExecuteReader();
        while (r.Read())
            rows.Add(new(
                Guid.Parse(r.GetString(0)),
                Guid.Parse(r.GetString(1)),
                Guid.Parse(r.GetString(2)),
                Guid.Parse(r.GetString(3)),
                Guid.Parse(r.GetString(4)),
                Guid.Parse(r.GetString(5)),
                r.GetString(6),
                r.IsDBNull(7) ? null : Guid.Parse(r.GetString(7)),
                r.GetString(8),
                r.GetString(9),
                r.GetString(10)));
        return rows;
    }

    private static IReadOnlyList<CafeIncidentBootstrapRootV1>
        ReadIncidents(
            SqliteConnection c,
            SqliteTransaction t,
            string store)
    {
        List<CafeIncidentBootstrapRootV1> rows = new();
        using SqliteCommand q = Command(c, t, """
            SELECT incident_id,store_id,current_version,current_revision_id,
                   status,reporter_user_id,reporter_device_id,subject_user_id,
                   occurred_at_utc,created_at_utc,updated_at_utc,
                   resolved_by_user_id,resolved_at_utc,resolution_note
            FROM cafe_incidents
            WHERE store_id=$store
            ORDER BY incident_id;
            """, store);
        using SqliteDataReader r = q.ExecuteReader();
        while (r.Read())
            rows.Add(new(
                Guid.Parse(r.GetString(0)),
                Guid.Parse(r.GetString(1)),
                r.GetInt32(2),
                Guid.Parse(r.GetString(3)),
                r.GetString(4),
                Guid.Parse(r.GetString(5)),
                Guid.Parse(r.GetString(6)),
                r.IsDBNull(7) ? null : Guid.Parse(r.GetString(7)),
                r.GetString(8),
                r.GetString(9),
                r.GetString(10),
                r.IsDBNull(11) ? null : Guid.Parse(r.GetString(11)),
                r.IsDBNull(12) ? null : r.GetString(12),
                r.IsDBNull(13) ? null : r.GetString(13)));
        return rows;
    }

    private static IReadOnlyList<CafeIncidentBootstrapRevisionV1>
        ReadIncidentRevisions(
            SqliteConnection c,
            SqliteTransaction t,
            string store)
    {
        List<CafeIncidentBootstrapRevisionV1> rows = new();
        using SqliteCommand q = Command(c, t, """
            SELECT r.revision_id,r.incident_id,r.revision_number,
                   r.incident_version,r.previous_revision_id,r.title,
                   r.description,r.classification,r.severity,
                   r.actor_user_id,r.device_id,r.created_at_utc,r.content_hash
            FROM cafe_incident_revisions r
            INNER JOIN cafe_incidents i ON i.incident_id=r.incident_id
            WHERE i.store_id=$store
            ORDER BY r.incident_id,r.incident_version,r.revision_id;
            """, store);
        using SqliteDataReader r = q.ExecuteReader();
        while (r.Read())
            rows.Add(new(
                Guid.Parse(r.GetString(0)),
                Guid.Parse(r.GetString(1)),
                r.GetInt32(2),
                r.GetInt32(3),
                r.IsDBNull(4) ? null : Guid.Parse(r.GetString(4)),
                r.GetString(5),
                r.GetString(6),
                r.GetString(7),
                r.GetString(8),
                Guid.Parse(r.GetString(9)),
                Guid.Parse(r.GetString(10)),
                r.GetString(11),
                r.GetString(12)));
        return rows;
    }

    private static IReadOnlyList<CafeIncidentBootstrapOperationV1>
        ReadIncidentOperations(
            SqliteConnection c,
            SqliteTransaction t,
            string store)
    {
        List<CafeIncidentBootstrapOperationV1> rows = new();
        using SqliteCommand q = Command(c, t, """
            SELECT operation_id,incident_id,store_id,device_id,actor_user_id,
                   operation_type,payload_hash,expected_version,resulting_version,
                   result_revision_id,result_status,occurred_at_utc
            FROM cafe_incident_operations
            WHERE store_id=$store
            ORDER BY incident_id,resulting_version,operation_id;
            """, store);
        using SqliteDataReader r = q.ExecuteReader();
        while (r.Read())
            rows.Add(new(
                Guid.Parse(r.GetString(0)),
                Guid.Parse(r.GetString(1)),
                Guid.Parse(r.GetString(2)),
                Guid.Parse(r.GetString(3)),
                Guid.Parse(r.GetString(4)),
                r.GetString(5),
                r.GetString(6),
                r.GetInt32(7),
                r.GetInt32(8),
                Guid.Parse(r.GetString(9)),
                r.GetString(10),
                r.GetString(11)));
        return rows;
    }

    private static IReadOnlyList<CafeIncidentBootstrapResolutionV1>
        ReadIncidentResolutions(
            SqliteConnection c,
            SqliteTransaction t,
            string store)
    {
        List<CafeIncidentBootstrapResolutionV1> rows = new();
        using SqliteCommand q = Command(c, t, """
            SELECT x.resolution_id,x.operation_id,x.incident_id,
                   x.actor_user_id,x.device_id,x.resolution_note,
                   x.resolved_at_utc,x.content_hash
            FROM cafe_incident_resolutions x
            INNER JOIN cafe_incidents i ON i.incident_id=x.incident_id
            WHERE i.store_id=$store
            ORDER BY x.incident_id,x.resolution_id;
            """, store);
        using SqliteDataReader r = q.ExecuteReader();
        while (r.Read())
            rows.Add(new(
                Guid.Parse(r.GetString(0)),
                Guid.Parse(r.GetString(1)),
                Guid.Parse(r.GetString(2)),
                Guid.Parse(r.GetString(3)),
                Guid.Parse(r.GetString(4)),
                r.GetString(5),
                r.GetString(6),
                r.GetString(7)));
        return rows;
    }

    private static SqliteCommand Command(
        SqliteConnection c,
        SqliteTransaction t,
        string sql,
        string store)
    {
        SqliteCommand q = c.CreateCommand();
        q.Transaction = t;
        q.CommandText = sql;
        q.Parameters.AddWithValue("$store", store);
        return q;
    }
}
