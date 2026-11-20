using Microsoft.Data.Sqlite;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

public sealed record SyncEnqueueRequest(
    Guid DeviceId,
    Guid StoreId,
    Guid ActorUserId,
    string OperationType,
    int SchemaVersion,
    Guid AggregateId,
    long? BaseVersion,
    DateTimeOffset OccurredAtLocal,
    string PayloadJson,
    Guid? OperationId = null);

public sealed record SyncOutboxItem(
    SyncOperationEnvelopeV1 Envelope,
    string DeliveryStatus,
    int AttemptCount,
    string? LastError,
    long? CentralReceivedSequence,
    DateTimeOffset? CentralReceivedAtUtc,
    string? ResolutionStatus,
    string? ResolutionCode,
    string? ResolutionJson,
    DateTimeOffset? ResolvedAtUtc,
    DateTimeOffset CreatedAtUtc);

public sealed class SqliteSyncOutboxStore
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteSyncOutboxStore(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public SyncOperationEnvelopeV1 Enqueue(SyncEnqueueRequest request)
    {
        using SqliteConnection connection = _connectionFactory.CreateConnection();
        connection.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();

        SyncOperationEnvelopeV1 envelope = Enqueue(connection, transaction, request);
        transaction.Commit();
        return envelope;
    }

    public SyncOperationEnvelopeV1 Enqueue(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncEnqueueRequest request)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(request);

        if (request.DeviceId == Guid.Empty ||
            request.StoreId == Guid.Empty ||
            request.ActorUserId == Guid.Empty ||
            request.AggregateId == Guid.Empty)
        {
            throw new ArgumentException("Sync enqueue identities must be non-empty.", nameof(request));
        }

        if (request.SchemaVersion <= 0 || string.IsNullOrWhiteSpace(request.OperationType))
        {
            throw new ArgumentException("Sync operation type and positive schema version are required.", nameof(request));
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        string deviceId = request.DeviceId.ToString("D");

        using (SqliteCommand ensure = connection.CreateCommand())
        {
            ensure.Transaction = transaction;
            ensure.CommandText = """
                INSERT OR IGNORE INTO sync_device_state(
                    device_id, next_local_sequence, created_at_utc, updated_at_utc)
                VALUES($device, 1, $now, $now);
                """;
            ensure.Parameters.AddWithValue("$device", deviceId);
            ensure.Parameters.AddWithValue("$now", now.ToString("O"));
            ensure.ExecuteNonQuery();
        }

        long localSequence;
        using (SqliteCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = """
                SELECT next_local_sequence
                FROM sync_device_state
                WHERE device_id = $device;
                """;
            read.Parameters.AddWithValue("$device", deviceId);
            localSequence = Convert.ToInt64(
                read.ExecuteScalar()
                ?? throw new InvalidOperationException("sync_device_state row was not created."));
        }

        using (SqliteCommand advance = connection.CreateCommand())
        {
            advance.Transaction = transaction;
            advance.CommandText = """
                UPDATE sync_device_state
                SET next_local_sequence = next_local_sequence + 1,
                    updated_at_utc = $now
                WHERE device_id = $device
                  AND next_local_sequence = $allocated;
                """;
            advance.Parameters.AddWithValue("$now", now.ToString("O"));
            advance.Parameters.AddWithValue("$device", deviceId);
            advance.Parameters.AddWithValue("$allocated", localSequence);

            if (advance.ExecuteNonQuery() != 1)
            {
                throw new InvalidOperationException("Local sync sequence allocation lost transactional ownership.");
            }
        }

        SyncOperationEnvelopeV1 envelope = SyncProtocolV1.CreateEnvelope(
            request.DeviceId,
            request.StoreId,
            request.ActorUserId,
            localSequence,
            request.OperationType.Trim(),
            request.SchemaVersion,
            request.AggregateId,
            request.BaseVersion,
            request.OccurredAtLocal,
            request.PayloadJson,
            request.OperationId);

        SyncEnvelopeValidationResult validation = SyncProtocolV1.Validate(envelope);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                validation.ErrorCode + ": " + validation.ErrorMessage);
        }

        using SqliteCommand insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO sync_outbox(
                operation_id, device_id, store_id, actor_user_id, local_sequence,
                protocol_version, operation_type, schema_version, aggregate_id, base_version,
                occurred_at_local, payload_json, payload_hash, operation_hash,
                delivery_status, attempt_count, last_error,
                central_received_sequence, central_received_at_utc,
                resolution_status, resolution_code, resolution_json, resolved_at_utc,
                created_at_utc)
            VALUES(
                $operation, $device, $store, $actor, $sequence,
                $protocol, $type, $schema, $aggregate, $base,
                $occurred, $payload, $payloadHash, $operationHash,
                'PendingUpload', 0, NULL,
                NULL, NULL,
                NULL, NULL, NULL, NULL,
                $created);
            """;
        BindEnvelope(insert, envelope);
        insert.Parameters.AddWithValue("$created", now.ToString("O"));
        insert.ExecuteNonQuery();

        return envelope;
    }

    public IReadOnlyList<SyncOutboxItem> ReadPending(Guid deviceId)
    {
        using SqliteConnection connection = _connectionFactory.CreateConnection();
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT *
            FROM sync_outbox
            WHERE device_id = $device
              AND delivery_status = 'PendingUpload'
            ORDER BY local_sequence ASC;
            """;
        command.Parameters.AddWithValue("$device", deviceId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        List<SyncOutboxItem> items = new();
        while (reader.Read())
        {
            items.Add(Map(reader));
        }

        return items;
    }

    public SyncOutboxItem? Find(Guid operationId)
    {
        using SqliteConnection connection = _connectionFactory.CreateConnection();
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM sync_outbox WHERE operation_id = $operation LIMIT 1;";
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public SyncOutboxItem? Find(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT * FROM sync_outbox WHERE operation_id = $operation LIMIT 1;";
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public void RecordAttemptFailure(Guid operationId, string error)
    {
        using SqliteConnection connection = _connectionFactory.CreateConnection();
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE sync_outbox
            SET attempt_count = attempt_count + 1,
                last_error = $error
            WHERE operation_id = $operation
              AND delivery_status = 'PendingUpload';
            """;
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));
        command.Parameters.AddWithValue("$error", string.IsNullOrWhiteSpace(error) ? "Upload failed." : error.Trim());

        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException("Pending sync operation was not available for failure recording.");
        }
    }

    public void MarkReceivedByCentral(
        Guid operationId,
        long receivedSequence,
        DateTimeOffset receivedAtUtc)
    {
        using SqliteConnection connection = _connectionFactory.CreateConnection();
        connection.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();

        MarkReceivedByCentral(
            connection,
            transaction,
            operationId,
            receivedSequence,
            receivedAtUtc);

        transaction.Commit();
    }

    public void MarkReceivedByCentral(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        long receivedSequence,
        DateTimeOffset receivedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        if (receivedSequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(receivedSequence));
        }

        SyncOutboxItem existing = Find(connection, transaction, operationId)
            ?? throw new InvalidOperationException(
                "Sync.OutboxOperationMissing: local outbox operation was not found.");

        DateTimeOffset normalizedReceivedAt = receivedAtUtc.ToUniversalTime();

        if (string.Equals(
                existing.DeliveryStatus,
                SyncProtocolV1.ReceivedByCentral,
                StringComparison.Ordinal))
        {
            if (existing.CentralReceivedSequence == receivedSequence &&
                existing.CentralReceivedAtUtc.HasValue &&
                existing.CentralReceivedAtUtc.Value.ToUniversalTime() == normalizedReceivedAt)
            {
                return;
            }

            throw new InvalidOperationException(
                "Sync.CentralReceiptConflict: operation already carries different central receipt evidence.");
        }

        if (!string.Equals(
                existing.DeliveryStatus,
                SyncProtocolV1.PendingUpload,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Sync.OutboxDeliveryStateInvalid: operation is not pending upload.");
        }

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE sync_outbox
            SET delivery_status = 'ReceivedByCentral',
                central_received_sequence = $sequence,
                central_received_at_utc = $received,
                last_error = NULL
            WHERE operation_id = $operation
              AND delivery_status = 'PendingUpload';
            """;
        command.Parameters.AddWithValue("$sequence", receivedSequence);
        command.Parameters.AddWithValue("$received", normalizedReceivedAt.ToString("O"));
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));

        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException(
                "Sync.CentralReceiptTransitionLost: pending sync operation lost receipt transition ownership.");
        }
    }

    public void RecordFinalResolution(SyncOperationFinalResolutionV1 resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        using SqliteConnection connection = _connectionFactory.CreateConnection();
        connection.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();

        RecordFinalResolution(connection, transaction, resolution);
        transaction.Commit();
    }

    public void RecordFinalResolution(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncOperationFinalResolutionV1 resolution)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(resolution);

        if (!SaleCommittedV1Contract.IsFinalResolution(resolution.Status) ||
            resolution.ReceivedSequence <= 0 ||
            string.IsNullOrWhiteSpace(resolution.ResultCode) ||
            string.IsNullOrWhiteSpace(resolution.ResultJson))
        {
            throw new ArgumentException(
                "Final sync resolution status, receipt, code and result JSON are required.",
                nameof(resolution));
        }

        SyncOutboxItem existing = Find(connection, transaction, resolution.OperationId)
            ?? throw new InvalidOperationException(
                "Sync.OutboxOperationMissing: local outbox operation was not found.");

        if (!string.Equals(
                existing.DeliveryStatus,
                SyncProtocolV1.ReceivedByCentral,
                StringComparison.Ordinal) ||
            existing.CentralReceivedSequence != resolution.ReceivedSequence ||
            !existing.CentralReceivedAtUtc.HasValue ||
            existing.CentralReceivedAtUtc.Value.ToUniversalTime() !=
                resolution.ReceivedAtUtc.ToUniversalTime())
        {
            throw new InvalidOperationException(
                "Sync.FinalResolutionReceiptMismatch: final result does not match retained central receipt evidence.");
        }

        if (existing.ResolutionStatus is not null)
        {
            bool same =
                string.Equals(existing.ResolutionStatus, resolution.Status, StringComparison.Ordinal) &&
                string.Equals(existing.ResolutionCode, resolution.ResultCode, StringComparison.Ordinal) &&
                string.Equals(existing.ResolutionJson, resolution.ResultJson, StringComparison.Ordinal) &&
                existing.ResolvedAtUtc.HasValue &&
                existing.ResolvedAtUtc.Value.ToUniversalTime() ==
                    resolution.ResolvedAtUtc.ToUniversalTime();

            if (same)
            {
                return;
            }

            throw new InvalidOperationException(
                "Sync.FinalResolutionConflict: operation already carries different final resolution evidence.");
        }

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE sync_outbox
            SET resolution_status = $status,
                resolution_code = $code,
                resolution_json = $json,
                resolved_at_utc = $resolved
            WHERE operation_id = $operation
              AND delivery_status = 'ReceivedByCentral'
              AND resolution_status IS NULL;
            """;
        command.Parameters.AddWithValue("$status", resolution.Status);
        command.Parameters.AddWithValue("$code", resolution.ResultCode);
        command.Parameters.AddWithValue("$json", resolution.ResultJson);
        command.Parameters.AddWithValue(
            "$resolved",
            resolution.ResolvedAtUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$operation", resolution.OperationId.ToString("D"));

        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException(
                "Sync.FinalResolutionTransitionLost: final result transition lost transactional ownership.");
        }
    }

    public void RecordCentralResult(SyncOperationFinalResolutionV1 resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        using SqliteConnection connection = _connectionFactory.CreateConnection();
        connection.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();

        MarkReceivedByCentral(
            connection,
            transaction,
            resolution.OperationId,
            resolution.ReceivedSequence,
            resolution.ReceivedAtUtc);

        RecordFinalResolution(connection, transaction, resolution);
        transaction.Commit();
    }

    public long ReadNextLocalSequence(Guid deviceId)
    {
        using SqliteConnection connection = _connectionFactory.CreateConnection();
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT next_local_sequence
            FROM sync_device_state
            WHERE device_id = $device;
            """;
        command.Parameters.AddWithValue("$device", deviceId.ToString("D"));
        object? value = command.ExecuteScalar();
        return value is null or DBNull ? 1L : Convert.ToInt64(value);
    }

    private static void BindEnvelope(SqliteCommand command, SyncOperationEnvelopeV1 envelope)
    {
        command.Parameters.AddWithValue("$operation", envelope.OperationId.ToString("D"));
        command.Parameters.AddWithValue("$device", envelope.DeviceId.ToString("D"));
        command.Parameters.AddWithValue("$store", envelope.StoreId.ToString("D"));
        command.Parameters.AddWithValue("$actor", envelope.ActorUserId.ToString("D"));
        command.Parameters.AddWithValue("$sequence", envelope.LocalSequence);
        command.Parameters.AddWithValue("$protocol", envelope.ProtocolVersion);
        command.Parameters.AddWithValue("$type", envelope.OperationType);
        command.Parameters.AddWithValue("$schema", envelope.SchemaVersion);
        command.Parameters.AddWithValue("$aggregate", envelope.AggregateId.ToString("D"));
        command.Parameters.AddWithValue("$base", (object?)envelope.BaseVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("$occurred", envelope.OccurredAtLocal.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$payload", envelope.PayloadJson);
        command.Parameters.AddWithValue("$payloadHash", envelope.PayloadHash);
        command.Parameters.AddWithValue("$operationHash", envelope.OperationHash);
    }

    private static SyncOutboxItem Map(SqliteDataReader reader)
    {
        SyncOperationEnvelopeV1 envelope = new(
            reader.GetInt32(reader.GetOrdinal("protocol_version")),
            Guid.Parse(reader.GetString(reader.GetOrdinal("operation_id"))),
            Guid.Parse(reader.GetString(reader.GetOrdinal("device_id"))),
            Guid.Parse(reader.GetString(reader.GetOrdinal("store_id"))),
            Guid.Parse(reader.GetString(reader.GetOrdinal("actor_user_id"))),
            reader.GetInt64(reader.GetOrdinal("local_sequence")),
            reader.GetString(reader.GetOrdinal("operation_type")),
            reader.GetInt32(reader.GetOrdinal("schema_version")),
            Guid.Parse(reader.GetString(reader.GetOrdinal("aggregate_id"))),
            reader.IsDBNull(reader.GetOrdinal("base_version"))
                ? null
                : reader.GetInt64(reader.GetOrdinal("base_version")),
            DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("occurred_at_local"))),
            reader.GetString(reader.GetOrdinal("payload_json")),
            reader.GetString(reader.GetOrdinal("payload_hash")),
            reader.GetString(reader.GetOrdinal("operation_hash")));

        return new SyncOutboxItem(
            envelope,
            reader.GetString(reader.GetOrdinal("delivery_status")),
            reader.GetInt32(reader.GetOrdinal("attempt_count")),
            reader.IsDBNull(reader.GetOrdinal("last_error")) ? null : reader.GetString(reader.GetOrdinal("last_error")),
            reader.IsDBNull(reader.GetOrdinal("central_received_sequence"))
                ? null
                : reader.GetInt64(reader.GetOrdinal("central_received_sequence")),
            reader.IsDBNull(reader.GetOrdinal("central_received_at_utc"))
                ? null
                : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("central_received_at_utc"))),
            reader.IsDBNull(reader.GetOrdinal("resolution_status")) ? null : reader.GetString(reader.GetOrdinal("resolution_status")),
            reader.IsDBNull(reader.GetOrdinal("resolution_code")) ? null : reader.GetString(reader.GetOrdinal("resolution_code")),
            reader.IsDBNull(reader.GetOrdinal("resolution_json")) ? null : reader.GetString(reader.GetOrdinal("resolution_json")),
            reader.IsDBNull(reader.GetOrdinal("resolved_at_utc"))
                ? null
                : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("resolved_at_utc"))),
            DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_at_utc"))));
    }
}
