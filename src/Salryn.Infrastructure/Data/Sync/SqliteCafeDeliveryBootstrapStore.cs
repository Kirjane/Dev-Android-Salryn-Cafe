using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

public sealed record CafeDeliveryBootstrapApplyResult(
    bool IsReplay,
    Guid SnapshotId,
    string SnapshotHash,
    long BaselineServerSequence,
    long DeliveryCount,
    long NonTerminalDeliveryCount,
    long RevisionCount,
    long OperationCount,
    long EventCount);

public sealed class SqliteCafeDeliveryBootstrapStore
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteCafeDeliveryBootstrapStore(
        SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory =
            connectionFactory ??
            throw new ArgumentNullException(
                nameof(connectionFactory));
    }

    public Result<CafeDeliveryBootstrapApplyResult> Apply(
        CafeDeliveryBootstrapV1 snapshot)
    {
        if (!CafeDeliveryBootstrapProtocolV1.Validate(snapshot))
        {
            return Result<CafeDeliveryBootstrapApplyResult>.Fail(
                Error.Conflict(
                    "CafeDeliveryBootstrap.SnapshotIntegrityMismatch",
                    "C8 delivery bootstrap identity/hash does not match canonical content."));
        }

        try
        {
            using SqliteConnection connection =
                _connectionFactory.CreateConnection();
            connection.Open();
            SqlitePragma.Apply(connection);

            using SqliteTransaction transaction =
                connection.BeginTransaction();

            Guid localStore =
                ReadStoreId(
                    connection,
                    transaction);

            if (localStore != snapshot.StoreId)
            {
                transaction.Rollback();

                return Result<CafeDeliveryBootstrapApplyResult>.Fail(
                    Error.Conflict(
                        "CafeDeliveryBootstrap.StoreMismatch",
                        "C8 delivery bootstrap StoreId does not match local deployment authority."));
            }

            string? inheritedError =
                ValidateInheritedAuthority(
                    connection,
                    transaction,
                    snapshot);

            if (inheritedError is not null)
            {
                transaction.Rollback();

                return Result<CafeDeliveryBootstrapApplyResult>.Fail(
                    Error.Conflict(
                        "CafeDeliveryBootstrap.InheritedAuthorityConflict",
                        inheritedError));
            }

            ExistingState? existing =
                ReadState(
                    connection,
                    transaction,
                    snapshot.StoreId);

            if (existing is not null)
            {
                bool exact =
                    existing.CentralInstanceId ==
                        snapshot.CentralInstanceId &&
                    existing.BaselineServerSequence ==
                        snapshot.BaselineServerSequence &&
                    existing.SnapshotId ==
                        snapshot.SnapshotId &&
                    string.Equals(
                        existing.SnapshotHash,
                        snapshot.SnapshotHash,
                        StringComparison.Ordinal);

                transaction.Commit();

                return exact
                    ? Result<CafeDeliveryBootstrapApplyResult>.Success(
                        CreateResult(
                            snapshot,
                            isReplay: true))
                    : Result<CafeDeliveryBootstrapApplyResult>.Fail(
                        Error.Conflict(
                            "CafeDeliveryBootstrap.EpochConflict",
                            "Different C8 delivery bootstrap evidence already exists; explicit reconciliation/rebootstrap is required."));
            }

            CafeDeliveryAuthorityDigest local =
                CafeDeliveryBootstrapAuthorityDigest.Read(
                    connection,
                    transaction,
                    snapshot.StoreId);

            bool authorityMatches =
                local.DeliveryCount ==
                    snapshot.DeliveryCount &&
                local.NonTerminalDeliveryCount ==
                    snapshot.NonTerminalDeliveryCount &&
                local.RevisionCount ==
                    snapshot.RevisionCount &&
                local.OperationCount ==
                    snapshot.OperationCount &&
                local.EventCount ==
                    snapshot.EventCount &&
                string.Equals(
                    local.Digest,
                    snapshot.AuthorityDigest,
                    StringComparison.Ordinal);

            if (!authorityMatches)
            {
                transaction.Rollback();

                return Result<CafeDeliveryBootstrapApplyResult>.Fail(
                    Error.Conflict(
                        "CafeDeliveryBootstrap.AuthorityMismatch",
                        "Local C8 delivery facts do not match central bootstrap authority; delivery history must be reconciled rather than guessed."));
            }

            using SqliteCommand insert =
                connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO cafe_delivery_bootstrap_state(
                    store_id,
                    central_instance_id,
                    baseline_server_sequence,
                    snapshot_id,
                    snapshot_hash,
                    applied_at_utc)
                VALUES(
                    $store,
                    $instance,
                    $baseline,
                    $snapshot,
                    $hash,
                    $at);
                """;
            insert.Parameters.AddWithValue(
                "$store",
                snapshot.StoreId.ToString("D"));
            insert.Parameters.AddWithValue(
                "$instance",
                snapshot.CentralInstanceId.ToString("D"));
            insert.Parameters.AddWithValue(
                "$baseline",
                snapshot.BaselineServerSequence);
            insert.Parameters.AddWithValue(
                "$snapshot",
                snapshot.SnapshotId.ToString("D"));
            insert.Parameters.AddWithValue(
                "$hash",
                snapshot.SnapshotHash);
            insert.Parameters.AddWithValue(
                "$at",
                DateTimeOffset.UtcNow
                    .ToUniversalTime()
                    .ToString("O"));

            insert.ExecuteNonQuery();

            transaction.Commit();

            return Result<CafeDeliveryBootstrapApplyResult>.Success(
                CreateResult(
                    snapshot,
                    isReplay: false));
        }
        catch (Exception ex) when (
            ex is SqliteException or
            InvalidOperationException or
            FormatException)
        {
            return Result<CafeDeliveryBootstrapApplyResult>.Fail(
                Error.Unexpected(
                    "CafeDeliveryBootstrap.ApplyFailed",
                    ex.Message));
        }
    }

    private static string? ValidateInheritedAuthority(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeDeliveryBootstrapV1 snapshot)
    {
        AuthorityRow? c4 =
            ReadAuthority(
                connection,
                transaction,
                "cafe_order_bootstrap_state",
                snapshot.StoreId);

        AuthorityRow? c5 =
            ReadAuthority(
                connection,
                transaction,
                "cafe_financial_bootstrap_state",
                snapshot.StoreId);

        if (c4 is null || c5 is null)
        {
            return
                "C8 delivery bootstrap requires accepted C4 Order and C5 Financial bootstrap authority first.";
        }

        if (c4.CentralInstanceId != c5.CentralInstanceId ||
            c4.BaselineServerSequence !=
                c5.BaselineServerSequence)
        {
            return
                "Accepted C4 Order and C5 Financial bootstrap authorities disagree on epoch/baseline.";
        }

        if (c4.CentralInstanceId !=
                snapshot.CentralInstanceId ||
            c5.CentralInstanceId !=
                snapshot.CentralInstanceId ||
            c4.BaselineServerSequence !=
                snapshot.BaselineServerSequence ||
            c5.BaselineServerSequence !=
                snapshot.BaselineServerSequence)
        {
            return
                "C8 delivery bootstrap must exactly extend accepted C4/C5 central instance and baseline authority.";
        }

        return null;
    }

    private static AuthorityRow? ReadAuthority(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        Guid storeId)
    {
        using SqliteCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT central_instance_id,baseline_server_sequence " +
            "FROM " + table +
            " WHERE store_id=$store LIMIT 1;";
        command.Parameters.AddWithValue(
            "$store",
            storeId.ToString("D"));

        using SqliteDataReader reader =
            command.ExecuteReader();

        return reader.Read()
            ? new AuthorityRow(
                Guid.Parse(reader.GetString(0)),
                reader.GetInt64(1))
            : null;
    }

    private static Guid ReadStoreId(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using SqliteCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT store_id
            FROM deployment_identity
            ORDER BY created_at
            LIMIT 1;
            """;

        object? value =
            command.ExecuteScalar();

        return
            value is string text &&
            Guid.TryParse(
                text,
                out Guid storeId) &&
            storeId != Guid.Empty
                ? storeId
                : throw new InvalidOperationException(
                    "Local deployment StoreId missing.");
    }

    private static ExistingState? ReadState(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId)
    {
        using SqliteCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                central_instance_id,
                baseline_server_sequence,
                snapshot_id,
                snapshot_hash
            FROM cafe_delivery_bootstrap_state
            WHERE store_id=$store
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$store",
            storeId.ToString("D"));

        using SqliteDataReader reader =
            command.ExecuteReader();

        return reader.Read()
            ? new ExistingState(
                Guid.Parse(reader.GetString(0)),
                reader.GetInt64(1),
                Guid.Parse(reader.GetString(2)),
                reader.GetString(3))
            : null;
    }

    private static CafeDeliveryBootstrapApplyResult CreateResult(
        CafeDeliveryBootstrapV1 snapshot,
        bool isReplay) =>
        new(
            isReplay,
            snapshot.SnapshotId,
            snapshot.SnapshotHash,
            snapshot.BaselineServerSequence,
            snapshot.DeliveryCount,
            snapshot.NonTerminalDeliveryCount,
            snapshot.RevisionCount,
            snapshot.OperationCount,
            snapshot.EventCount);

    private sealed record AuthorityRow(
        Guid CentralInstanceId,
        long BaselineServerSequence);

    private sealed record ExistingState(
        Guid CentralInstanceId,
        long BaselineServerSequence,
        Guid SnapshotId,
        string SnapshotHash);
}
