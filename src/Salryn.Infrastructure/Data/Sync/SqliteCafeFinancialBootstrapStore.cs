using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

public sealed record CafeFinancialBootstrapApplyResult(
    bool IsReplay,
    Guid SnapshotId,
    string SnapshotHash,
    long BaselineServerSequence,
    long FinancialDecisionCount,
    long RefundCount,
    long NonTerminalRefundCount);

public sealed class SqliteCafeFinancialBootstrapStore
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteCafeFinancialBootstrapStore(
        SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory =
            connectionFactory ??
            throw new ArgumentNullException(nameof(connectionFactory));
    }

    public Result<CafeFinancialBootstrapApplyResult> Apply(
        CafeFinancialBootstrapV1 snapshot)
    {
        if (!CafeFinancialBootstrapProtocolV1.Validate(snapshot))
        {
            return Result<CafeFinancialBootstrapApplyResult>.Fail(
                Error.Conflict(
                    "CafeFinancialBootstrap.SnapshotIntegrityMismatch",
                    "C5 financial bootstrap identity/hash does not match canonical content."));
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
                return Result<CafeFinancialBootstrapApplyResult>.Fail(
                    Error.Conflict(
                        "CafeFinancialBootstrap.StoreMismatch",
                        "C5 financial bootstrap StoreId does not match local deployment authority."));
            }

            string? inheritedError =
                ValidateInheritedAuthority(
                    connection,
                    transaction,
                    snapshot);
            if (inheritedError is not null)
            {
                transaction.Rollback();
                return Result<CafeFinancialBootstrapApplyResult>.Fail(
                    Error.Conflict(
                        "CafeFinancialBootstrap.InheritedAuthorityConflict",
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
                    existing.BaselineServerSequence == snapshot.BaselineServerSequence &&
                    existing.SnapshotId == snapshot.SnapshotId &&
                    string.Equals(
                        existing.SnapshotHash,
                        snapshot.SnapshotHash,
                        StringComparison.Ordinal);

                transaction.Commit();
                return exact
                    ? Result<CafeFinancialBootstrapApplyResult>.Success(
                        CreateResult(snapshot, isReplay: true))
                    : Result<CafeFinancialBootstrapApplyResult>.Fail(
                        Error.Conflict(
                            "CafeFinancialBootstrap.EpochConflict",
                            "Different C5 financial bootstrap evidence already exists; explicit reconciliation/rebootstrap is required."));
            }

            CafeFinancialAuthorityDigest local =
                CafeFinancialBootstrapAuthorityDigest.Read(
                    connection,
                    transaction);

            bool authorityMatches =
                local.FinancialDecisionCount == snapshot.FinancialDecisionCount &&
                local.RefundCount == snapshot.RefundCount &&
                local.NonTerminalRefundCount == snapshot.NonTerminalRefundCount &&
                string.Equals(
                    local.Digest,
                    snapshot.AuthorityDigest,
                    StringComparison.Ordinal);

            if (!authorityMatches)
            {
                transaction.Rollback();
                return Result<CafeFinancialBootstrapApplyResult>.Fail(
                    Error.Conflict(
                        "CafeFinancialBootstrap.AuthorityMismatch",
                        "Local C5 financial/refund facts do not match central bootstrap authority; historical Sale/AR facts must be reconciled rather than guessed."));
            }

            using SqliteCommand insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO cafe_financial_bootstrap_state(
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
            return Result<CafeFinancialBootstrapApplyResult>.Success(
                CreateResult(snapshot, isReplay: false));
        }
        catch (Exception ex) when (
            ex is SqliteException or
            InvalidOperationException or
            FormatException)
        {
            return Result<CafeFinancialBootstrapApplyResult>.Fail(
                Error.Unexpected(
                    "CafeFinancialBootstrap.ApplyFailed",
                    ex.Message));
        }
    }

    private static string? ValidateInheritedAuthority(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeFinancialBootstrapV1 snapshot)
    {
        SyncRemoteState? f1eState =
            SqliteSyncPullStore.ReadRemoteState(
                connection,
                transaction,
                snapshot.StoreId);

        SyncBootstrapEvidence? f1eEvidence =
            SqliteSyncBootstrapStore.ReadBootstrapEvidence(
                connection,
                transaction,
                snapshot.StoreId);

        if (f1eState is null ||
            !f1eState.CentralInstanceId.HasValue ||
            f1eEvidence is null)
        {
            return "C5 financial bootstrap requires accepted F1E bootstrap authority first.";
        }

        bool sameF1E =
            f1eState.CentralInstanceId.Value == snapshot.CentralInstanceId &&
            f1eEvidence.CentralInstanceId == snapshot.CentralInstanceId &&
            f1eEvidence.StoreId == snapshot.StoreId &&
            f1eEvidence.BaselineServerSequence == snapshot.BaselineServerSequence &&
            f1eState.LastReceivedServerSequence == snapshot.BaselineServerSequence &&
            f1eState.LastAppliedServerSequence == snapshot.BaselineServerSequence &&
            string.Equals(
                f1eState.State,
                AppliedBusinessChangeContract.Ready,
                StringComparison.Ordinal);

        if (!sameF1E)
        {
            return "C5 financial bootstrap must exactly extend accepted F1E StoreId, epoch and baseline authority.";
        }

        foreach (string table in new[]
        {
            "cafe_ingredient_bootstrap_state",
            "cafe_inventory_bootstrap_state",
            "cafe_order_bootstrap_state"
        })
        {
            BootstrapAuthority? authority =
                ReadInheritedState(
                    connection,
                    transaction,
                    table,
                    snapshot.StoreId);

            if (authority is null)
            {
                return "C5 financial bootstrap requires accepted C2, C3 and C4 bootstrap authority first.";
            }

            if (authority.CentralInstanceId != snapshot.CentralInstanceId ||
                authority.BaselineServerSequence != snapshot.BaselineServerSequence)
            {
                return "C5 financial bootstrap must exactly extend C2/C3/C4 epoch and baseline authority.";
            }
        }

        return null;
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

    private static BootstrapAuthority? ReadInheritedState(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        Guid storeId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT central_instance_id,baseline_server_sequence FROM " +
            table +
            " WHERE store_id=$store LIMIT 1;";
        command.Parameters.AddWithValue("$store", storeId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? new BootstrapAuthority(
                Guid.Parse(reader.GetString(0)),
                reader.GetInt64(1))
            : null;
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
            FROM cafe_financial_bootstrap_state
            WHERE store_id=$store
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$store", storeId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? new ExistingState(
                Guid.Parse(reader.GetString(0)),
                reader.GetInt64(1),
                Guid.Parse(reader.GetString(2)),
                reader.GetString(3))
            : null;
    }

    private static CafeFinancialBootstrapApplyResult CreateResult(
        CafeFinancialBootstrapV1 snapshot,
        bool isReplay) =>
        new(
            isReplay,
            snapshot.SnapshotId,
            snapshot.SnapshotHash,
            snapshot.BaselineServerSequence,
            snapshot.FinancialDecisionCount,
            snapshot.RefundCount,
            snapshot.NonTerminalRefundCount);

    private sealed record BootstrapAuthority(
        Guid CentralInstanceId,
        long BaselineServerSequence);

    private sealed record ExistingState(
        Guid CentralInstanceId,
        long BaselineServerSequence,
        Guid SnapshotId,
        string SnapshotHash);
}
