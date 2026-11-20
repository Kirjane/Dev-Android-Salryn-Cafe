using Microsoft.Data.Sqlite;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

public sealed class SqliteSyncPullStore
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteSyncPullStore(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public SyncRemoteState? ReadRemoteState(Guid storeId)
    {
        using SqliteConnection connection = _connectionFactory.CreateConnection();
        connection.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        SyncRemoteState? state = ReadRemoteState(connection, transaction, storeId);
        transaction.Commit();
        return state;
    }

    public SyncRemoteBindingResult InitializeRemoteState(
        Guid storeId,
        Guid centralInstanceId,
        long baselineServerSequence = 0,
        DateTimeOffset? initializedAtUtc = null)
    {
        if (storeId == Guid.Empty || centralInstanceId == Guid.Empty)
        {
            return SyncRemoteBindingResult.Fail(
                "Sync.RemoteBindingIdentityRequired",
                "Store and central instance IDs are required.");
        }

        if (baselineServerSequence < 0)
        {
            return SyncRemoteBindingResult.Fail(
                "Sync.RemoteBindingBaselineInvalid",
                "Baseline server sequence cannot be negative.");
        }

        using SqliteConnection connection = _connectionFactory.CreateConnection();
        connection.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();

        Guid? localStoreId = ReadDeploymentStoreId(connection, transaction);
        if (!localStoreId.HasValue || localStoreId.Value != storeId)
        {
            transaction.Rollback();
            return SyncRemoteBindingResult.Fail(
                "Sync.RemoteBindingStoreMismatch",
                "Remote binding store must match the production deployment identity.");
        }

        SyncRemoteState? existing = ReadRemoteState(connection, transaction, storeId);
        if (existing is not null)
        {
            bool sameAuthorityWithoutCursorRegression =
                existing.CentralInstanceId == centralInstanceId &&
                baselineServerSequence <= existing.LastReceivedServerSequence;

            if (sameAuthorityWithoutCursorRegression)
            {
                transaction.Commit();
                return SyncRemoteBindingResult.Success(existing, isReplay: true);
            }

            transaction.Rollback();
            return SyncRemoteBindingResult.Fail(
                "Sync.RemoteBindingConflict",
                "Remote state is already bound to different epoch or baseline evidence.");
        }

        DateTimeOffset at = (initializedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();

        using SqliteCommand insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO sync_remote_state(
                store_id, central_instance_id,
                last_received_server_sequence, last_applied_server_sequence,
                state, last_error, created_at_utc, updated_at_utc)
            VALUES(
                $store, $instance,
                $received, 0,
                $state, NULL, $created, $updated);
            """;
        insert.Parameters.AddWithValue("$store", storeId.ToString("D"));
        insert.Parameters.AddWithValue("$instance", centralInstanceId.ToString("D"));
        insert.Parameters.AddWithValue("$received", baselineServerSequence);
        insert.Parameters.AddWithValue("$state", AppliedBusinessChangeContract.Ready);
        insert.Parameters.AddWithValue("$created", at.ToString("O"));
        insert.Parameters.AddWithValue("$updated", at.ToString("O"));
        insert.ExecuteNonQuery();

        SyncRemoteState bound = ReadRemoteState(connection, transaction, storeId)
            ?? throw new InvalidOperationException("Remote state disappeared after binding.");

        transaction.Commit();
        return SyncRemoteBindingResult.Success(bound, isReplay: false);
    }

    public SyncPullPageAcceptanceResult AcceptPage(
        Guid storeId,
        SyncChangeFeedPageV1 page,
        DateTimeOffset? receivedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(page);

        using SqliteConnection connection = _connectionFactory.CreateConnection();
        connection.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();

        SyncRemoteState? state = ReadRemoteState(connection, transaction, storeId);
        if (state is null || !state.CentralInstanceId.HasValue)
        {
            transaction.Rollback();
            return SyncPullPageAcceptanceResult.Fail(
                state,
                "Sync.RemoteStateNotBound",
                "Client remote state must be explicitly bound before accepting change pages.");
        }

        if (state.CentralInstanceId.Value != page.CentralInstanceId)
        {
            using SqliteCommand mismatch = connection.CreateCommand();
            mismatch.Transaction = transaction;
            mismatch.CommandText = """
                UPDATE sync_remote_state
                SET state = $state,
                    last_error = $error,
                    updated_at_utc = $updated
                WHERE store_id = $store;
                """;
            mismatch.Parameters.AddWithValue("$state", AppliedBusinessChangeContract.BootstrapRequired);
            mismatch.Parameters.AddWithValue(
                "$error",
                "Central instance changed; incremental pull requires bootstrap/reconciliation.");
            mismatch.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
            mismatch.Parameters.AddWithValue("$store", storeId.ToString("D"));
            mismatch.ExecuteNonQuery();

            SyncRemoteState mismatchState = ReadRemoteState(connection, transaction, storeId)
                ?? throw new InvalidOperationException("Remote state disappeared during epoch mismatch handling.");

            transaction.Commit();
            return SyncPullPageAcceptanceResult.Fail(
                mismatchState,
                "Sync.CentralInstanceMismatch",
                "Change page belongs to a different central epoch.");
        }

        SyncPullPageAcceptanceResult Fail(string code, string message)
        {
            transaction.Rollback();
            return SyncPullPageAcceptanceResult.Fail(state, code, message);
        }

        if (page.AfterServerSequence < 0 ||
            page.AfterServerSequence > state.LastReceivedServerSequence)
        {
            return Fail(
                "Sync.PullCursorInvalid",
                "Page cursor is ahead of the client's durable received cursor.");
        }

        if (page.HighWatermarkServerSequence < page.AfterServerSequence)
        {
            return Fail(
                "Sync.PullHighWatermarkInvalid",
                "Page high watermark cannot be lower than its requested cursor.");
        }

        long previousSequence = page.AfterServerSequence;
        long expectedNewSequence = state.LastReceivedServerSequence + 1;
        long acceptedCursor = state.LastReceivedServerSequence;
        int inserted = 0;
        int duplicates = 0;
        DateTimeOffset receivedAt = (receivedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();

        foreach (AppliedBusinessChangeV1 change in page.Changes)
        {
            if (change.ServerSequence <= page.AfterServerSequence ||
                change.ServerSequence <= previousSequence)
            {
                return Fail(
                    "Sync.PullSequenceOrderInvalid",
                    "Change page sequences must be strictly increasing and greater than the requested cursor.");
            }

            previousSequence = change.ServerSequence;

            if (change.ServerSequence > page.HighWatermarkServerSequence)
            {
                return Fail(
                    "Sync.PullHighWatermarkInvalid",
                    "Change sequence exceeds the advertised page high watermark.");
            }

            if (change.CentralInstanceId != page.CentralInstanceId)
            {
                return Fail(
                    "Sync.ChangeEpochMismatch",
                    "A change row does not belong to the page central instance.");
            }

            if (change.StoreId != storeId)
            {
                return Fail(
                    "Sync.ChangeStoreMismatch",
                    "A change row belongs to a different store.");
            }

            AppliedBusinessChangeValidationResult validation =
                AppliedBusinessChangeProtocolV1.Validate(change);
            if (!validation.IsValid)
            {
                return Fail(
                    validation.ErrorCode ?? "Sync.ChangeInvalid",
                    validation.ErrorMessage ?? "Applied change integrity validation failed.");
            }

            AppliedBusinessChangeV1? bySequence =
                ReadChangeBySequence(connection, transaction, change.ServerSequence);
            AppliedBusinessChangeV1? byChangeId =
                ReadChangeById(connection, transaction, change.ChangeId);

            if (byChangeId is not null &&
                byChangeId.ServerSequence != change.ServerSequence)
            {
                return Fail(
                    "Sync.ChangeIdCollision",
                    "Change ID is already retained under a different server sequence.");
            }

            if (change.ServerSequence <= state.LastReceivedServerSequence)
            {
                if (bySequence is null ||
                    !AppliedBusinessChangeProtocolV1.ImmutableContentEquals(bySequence, change))
                {
                    return Fail(
                        "Sync.ServerSequenceCollision",
                        "Already-received server sequence carries different immutable content.");
                }

                duplicates++;
                continue;
            }

            if (change.ServerSequence != expectedNewSequence)
            {
                return Fail(
                    "Sync.PullSequenceGap",
                    "Change page is not contiguous with the durable received cursor.");
            }

            if (bySequence is not null)
            {
                if (!AppliedBusinessChangeProtocolV1.ImmutableContentEquals(bySequence, change))
                {
                    return Fail(
                        "Sync.ServerSequenceCollision",
                        "Server sequence is already retained with different immutable content.");
                }

                duplicates++;
            }
            else
            {
                using SqliteCommand insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO sync_change_inbox(
                        server_sequence, change_id, central_instance_id,
                        source_operation_id, origin_device_id, origin_local_sequence,
                        store_id, change_type, schema_version, aggregate_id,
                        payload_json, payload_hash, change_hash,
                        change_created_at_utc, received_at_utc,
                        apply_status, apply_error, applied_at_utc)
                    VALUES(
                        $serverSequence, $changeId, $centralInstance,
                        $sourceOperation, $originDevice, $originLocalSequence,
                        $store, $changeType, $schemaVersion, $aggregate,
                        $payload, $payloadHash, $changeHash,
                        $changeCreated, $received,
                        $status, NULL, NULL);
                    """;
                BindChange(insert, change, receivedAt);
                insert.ExecuteNonQuery();
                inserted++;
            }

            acceptedCursor = change.ServerSequence;
            expectedNewSequence = change.ServerSequence + 1;
        }

        if (acceptedCursor > state.LastReceivedServerSequence)
        {
            using SqliteCommand advance = connection.CreateCommand();
            advance.Transaction = transaction;
            advance.CommandText = """
                UPDATE sync_remote_state
                SET last_received_server_sequence = $received,
                    state = $state,
                    last_error = NULL,
                    updated_at_utc = $updated
                WHERE store_id = $store
                  AND central_instance_id = $instance
                  AND last_received_server_sequence = $expected;
                """;
            advance.Parameters.AddWithValue("$received", acceptedCursor);
            advance.Parameters.AddWithValue("$state", AppliedBusinessChangeContract.Ready);
            advance.Parameters.AddWithValue("$updated", receivedAt.ToString("O"));
            advance.Parameters.AddWithValue("$store", storeId.ToString("D"));
            advance.Parameters.AddWithValue("$instance", page.CentralInstanceId.ToString("D"));
            advance.Parameters.AddWithValue("$expected", state.LastReceivedServerSequence);

            if (advance.ExecuteNonQuery() != 1)
            {
                return Fail(
                    "Sync.PullCursorAdvanceLost",
                    "Received cursor transition lost transactional ownership.");
            }
        }

        SyncRemoteState finalState = ReadRemoteState(connection, transaction, storeId)
            ?? throw new InvalidOperationException("Remote state disappeared after page receipt.");

        transaction.Commit();
        return SyncPullPageAcceptanceResult.Success(finalState, inserted, duplicates);
    }

    public AppliedBusinessChangeV1? ReadChange(long serverSequence)
    {
        using SqliteConnection connection = _connectionFactory.CreateConnection();
        connection.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        AppliedBusinessChangeV1? change = ReadChangeBySequence(
            connection,
            transaction,
            serverSequence);
        transaction.Commit();
        return change;
    }

    public long CountChanges()
    {
        using SqliteConnection connection = _connectionFactory.CreateConnection();
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sync_change_inbox;";
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L);
    }

    private static AppliedBusinessChangeV1? ReadChangeBySequence(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long serverSequence)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT server_sequence, change_id, central_instance_id,
                   source_operation_id, origin_device_id, origin_local_sequence,
                   store_id, change_type, schema_version, aggregate_id,
                   payload_json, payload_hash, change_hash, change_created_at_utc, received_at_utc
            FROM sync_change_inbox
            WHERE server_sequence = $sequence
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$sequence", serverSequence);

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? MapChange(reader) : null;
    }

    private static AppliedBusinessChangeV1? ReadChangeById(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid changeId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT server_sequence, change_id, central_instance_id,
                   source_operation_id, origin_device_id, origin_local_sequence,
                   store_id, change_type, schema_version, aggregate_id,
                   payload_json, payload_hash, change_hash, change_created_at_utc, received_at_utc
            FROM sync_change_inbox
            WHERE change_id = $change
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$change", changeId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? MapChange(reader) : null;
    }

    private static void BindChange(
        SqliteCommand command,
        AppliedBusinessChangeV1 change,
        DateTimeOffset receivedAtUtc)
    {
        command.Parameters.AddWithValue("$serverSequence", change.ServerSequence);
        command.Parameters.AddWithValue("$changeId", change.ChangeId.ToString("D"));
        command.Parameters.AddWithValue("$centralInstance", change.CentralInstanceId.ToString("D"));
        command.Parameters.AddWithValue("$sourceOperation", change.SourceOperationId.ToString("D"));
        command.Parameters.AddWithValue("$originDevice", change.OriginDeviceId.ToString("D"));
        command.Parameters.AddWithValue("$originLocalSequence", change.OriginLocalSequence);
        command.Parameters.AddWithValue("$store", change.StoreId.ToString("D"));
        command.Parameters.AddWithValue("$changeType", change.ChangeType);
        command.Parameters.AddWithValue("$schemaVersion", change.SchemaVersion);
        command.Parameters.AddWithValue("$aggregate", change.AggregateId.ToString("D"));
        command.Parameters.AddWithValue("$payload", change.PayloadJson);
        command.Parameters.AddWithValue("$payloadHash", change.PayloadHash);
        command.Parameters.AddWithValue("$changeHash", change.ChangeHash);
        command.Parameters.AddWithValue("$changeCreated", change.CreatedAtUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$received", receivedAtUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$status", AppliedBusinessChangeContract.Received);
    }

    private static AppliedBusinessChangeV1 MapChange(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(2)),
            reader.GetInt64(0),
            Guid.Parse(reader.GetString(1)),
            Guid.Parse(reader.GetString(3)),
            Guid.Parse(reader.GetString(4)),
            reader.GetInt64(5),
            Guid.Parse(reader.GetString(6)),
            reader.GetString(7),
            reader.GetInt32(8),
            Guid.Parse(reader.GetString(9)),
            reader.GetString(10),
            reader.GetString(11),
            reader.GetString(12),
            DateTimeOffset.Parse(reader.GetString(13)));

    internal static SyncRemoteState? ReadRemoteState(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT store_id, central_instance_id,
                   last_received_server_sequence, last_applied_server_sequence,
                   state, last_error, created_at_utc, updated_at_utc
            FROM sync_remote_state
            WHERE store_id = $store
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$store", storeId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new SyncRemoteState(
            Guid.Parse(reader.GetString(0)),
            reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            DateTimeOffset.Parse(reader.GetString(6)),
            DateTimeOffset.Parse(reader.GetString(7)));
    }

    private static Guid? ReadDeploymentStoreId(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT store_id
            FROM deployment_identity
            ORDER BY created_at ASC
            LIMIT 1;
            """;

        object? value = command.ExecuteScalar();
        return value is string text && Guid.TryParse(text, out Guid storeId)
            ? storeId
            : null;
    }
}
