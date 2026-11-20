using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

public sealed record CafeCashBootstrapApplyResult(
    bool IsReplay,
    Guid SnapshotId,
    string SnapshotHash,
    long BaselineServerSequence,
    long ManualMovementCount,
    long ReversalCount,
    long NetDeltaCents);

public sealed class SqliteCafeCashBootstrapStore
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteCafeCashBootstrapStore(
        SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory =
            connectionFactory ??
            throw new ArgumentNullException(nameof(connectionFactory));
    }

    public Result<CafeCashBootstrapApplyResult> Apply(
        CafeCashBootstrapV1 snapshot)
    {
        if (!CafeCashBootstrapProtocolV1.Validate(snapshot))
        {
            return Result<CafeCashBootstrapApplyResult>.Fail(
                Error.Conflict(
                    "CafeCashBootstrap.SnapshotIntegrityMismatch",
                    "C6 cash bootstrap identity/hash does not match canonical content."));
        }

        try
        {
            using SqliteConnection connection =
                _connectionFactory.CreateConnection();
            connection.Open();
            SqlitePragma.Apply(connection);

            using SqliteTransaction transaction =
                connection.BeginTransaction();

            Guid localStore = ReadStoreId(connection, transaction);
            if (localStore != snapshot.StoreId)
            {
                transaction.Rollback();
                return Result<CafeCashBootstrapApplyResult>.Fail(
                    Error.Conflict(
                        "CafeCashBootstrap.StoreMismatch",
                        "C6 cash bootstrap StoreId does not match local deployment authority."));
            }

            string? inheritedError =
                ValidateInheritedAuthority(
                    connection,
                    transaction,
                    snapshot);

            if (inheritedError is not null)
            {
                transaction.Rollback();
                return Result<CafeCashBootstrapApplyResult>.Fail(
                    Error.Conflict(
                        "CafeCashBootstrap.InheritedAuthorityConflict",
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
                    existing.CentralInstanceId == snapshot.CentralInstanceId &&
                    existing.BaselineServerSequence ==
                        snapshot.BaselineServerSequence &&
                    existing.SnapshotId == snapshot.SnapshotId &&
                    string.Equals(
                        existing.SnapshotHash,
                        snapshot.SnapshotHash,
                        StringComparison.Ordinal);

                transaction.Commit();

                return exact
                    ? Result<CafeCashBootstrapApplyResult>.Success(
                        CreateResult(snapshot, isReplay: true))
                    : Result<CafeCashBootstrapApplyResult>.Fail(
                        Error.Conflict(
                            "CafeCashBootstrap.EpochConflict",
                            "Different C6 cash bootstrap evidence already exists; explicit reconciliation/rebootstrap is required."));
            }

            CafeCashAuthorityDigest local =
                CafeCashBootstrapAuthorityDigest.Read(
                    connection,
                    transaction);

            bool authorityMatches =
                local.ManualMovementCount == snapshot.ManualMovementCount &&
                local.ReversalCount == snapshot.ReversalCount &&
                local.NetDeltaCents == snapshot.NetDeltaCents &&
                string.Equals(
                    local.Digest,
                    snapshot.AuthorityDigest,
                    StringComparison.Ordinal);

            if (!authorityMatches)
            {
                transaction.Rollback();

                return Result<CafeCashBootstrapApplyResult>.Fail(
                    Error.Conflict(
                        "CafeCashBootstrap.AuthorityMismatch",
                        "Local C6 cash movement facts do not match central bootstrap authority; cash history must be reconciled rather than guessed."));
            }

            using SqliteCommand insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO cafe_cash_bootstrap_state(
                    store_id,central_instance_id,baseline_server_sequence,
                    snapshot_id,snapshot_hash,applied_at_utc)
                VALUES(
                    $store,$instance,$baseline,$snapshot,$hash,$at);
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
                DateTimeOffset.UtcNow.ToUniversalTime().ToString("O"));

            insert.ExecuteNonQuery();

            transaction.Commit();

            return Result<CafeCashBootstrapApplyResult>.Success(
                CreateResult(snapshot, isReplay: false));
        }
        catch (Exception ex) when (
            ex is SqliteException or
            InvalidOperationException or
            FormatException)
        {
            return Result<CafeCashBootstrapApplyResult>.Fail(
                Error.Unexpected(
                    "CafeCashBootstrap.ApplyFailed",
                    ex.Message));
        }
    }

    private static string? ValidateInheritedAuthority(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeCashBootstrapV1 snapshot)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT central_instance_id,baseline_server_sequence
            FROM cafe_financial_bootstrap_state
            WHERE store_id=$store
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$store",
            snapshot.StoreId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return "C6 cash bootstrap requires accepted C5 financial bootstrap authority first.";
        }

        Guid centralInstanceId = Guid.Parse(reader.GetString(0));
        long baseline = reader.GetInt64(1);

        return centralInstanceId == snapshot.CentralInstanceId &&
               baseline == snapshot.BaselineServerSequence
            ? null
            : "C6 cash bootstrap must exactly extend accepted C5 epoch and baseline authority.";
    }

    private static Guid ReadStoreId(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT store_id FROM deployment_identity ORDER BY created_at LIMIT 1;";

        object? value = command.ExecuteScalar();

        return value is string text &&
               Guid.TryParse(text, out Guid id) &&
               id != Guid.Empty
            ? id
            : throw new InvalidOperationException(
                "Local deployment StoreId missing.");
    }

    private static ExistingState? ReadState(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT central_instance_id,baseline_server_sequence,
                   snapshot_id,snapshot_hash
            FROM cafe_cash_bootstrap_state
            WHERE store_id=$store
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$store",
            storeId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? new ExistingState(
                Guid.Parse(reader.GetString(0)),
                reader.GetInt64(1),
                Guid.Parse(reader.GetString(2)),
                reader.GetString(3))
            : null;
    }

    private static CafeCashBootstrapApplyResult CreateResult(
        CafeCashBootstrapV1 snapshot,
        bool isReplay) =>
        new(
            isReplay,
            snapshot.SnapshotId,
            snapshot.SnapshotHash,
            snapshot.BaselineServerSequence,
            snapshot.ManualMovementCount,
            snapshot.ReversalCount,
            snapshot.NetDeltaCents);

    private sealed record ExistingState(
        Guid CentralInstanceId,
        long BaselineServerSequence,
        Guid SnapshotId,
        string SnapshotHash);
}
