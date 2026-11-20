using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

public sealed record CafeBookingBootstrapApplyResult(
    bool IsReplay,
    bool Reconstructed,
    Guid SnapshotId,
    string SnapshotHash,
    string AuthorityDigest,
    long BaselineServerSequence,
    long BookingCount,
    long RevisionCount,
    long DepositFactCount,
    long OrderLinkCount,
    long SaleLinkCount);

public sealed partial class SqliteCafeBookingBootstrapStore
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteCafeBookingBootstrapStore(
        SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory =
            connectionFactory ??
            throw new ArgumentNullException(nameof(connectionFactory));
    }

    public Result<CafeBookingBootstrapApplyResult> Apply(
        CafeBookingBootstrapV1 snapshot)
    {
        if (!CafeBookingBootstrapProtocolV1.Validate(snapshot))
        {
            return Result<CafeBookingBootstrapApplyResult>.Fail(
                Error.Conflict(
                    "CafeBookingBootstrap.SnapshotIntegrityMismatch",
                    "C9 Booking bootstrap identity/hash/digest does not match canonical retained authority."));
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
                ReadStoreId(connection, transaction);

            if (localStore != snapshot.StoreId)
            {
                transaction.Rollback();
                return Conflict(
                    "CafeBookingBootstrap.StoreMismatch",
                    "C9 Booking bootstrap StoreId does not match local deployment authority.");
            }

            string? inheritedError =
                ValidateInheritedAuthority(connection, transaction, snapshot);
            if (inheritedError is not null)
            {
                transaction.Rollback();
                return Conflict(
                    "CafeBookingBootstrap.InheritedAuthorityConflict",
                    inheritedError);
            }

            ExistingState? existing =
                ReadState(connection, transaction, snapshot.StoreId);

            if (existing is not null)
            {
                bool exact =
                    existing.CentralInstanceId == snapshot.CentralInstanceId &&
                    existing.BaselineServerSequence == snapshot.BaselineServerSequence &&
                    existing.SnapshotId == snapshot.SnapshotId &&
                    existing.SnapshotHash == snapshot.SnapshotHash &&
                    existing.AuthorityDigest == snapshot.AuthorityDigest;

                if (!exact)
                {
                    transaction.Rollback();
                    return Conflict(
                        "CafeBookingBootstrap.EpochConflict",
                        "Different C9 Booking bootstrap evidence already exists; retained history requires explicit reconciliation.");
                }

                CafeBookingBootstrapAuthorityV1 replayAuthority =
                    CafeBookingBootstrapAuthorityReader.Read(
                        connection,
                        transaction,
                        snapshot.StoreId);
                CafeBookingBootstrapV1 replaySnapshot =
                    CafeBookingBootstrapProtocolV1.Create(
                        snapshot.CentralInstanceId,
                        snapshot.StoreId,
                        snapshot.BaselineServerSequence,
                        replayAuthority);

                if (!SameAuthority(replaySnapshot, snapshot))
                {
                    transaction.Rollback();
                    return Conflict(
                        "CafeBookingBootstrap.ReplayAuthorityMismatch",
                        "Persisted C9 Booking facts no longer reproduce the accepted bootstrap authority; reconciliation is required.");
                }

                AssertForeignKeysClean(connection, transaction);
                transaction.Commit();
                return Result<CafeBookingBootstrapApplyResult>.Success(
                    CreateResult(snapshot, isReplay: true, reconstructed: false));
            }

            string? prerequisiteError =
                ValidatePrerequisites(connection, transaction, snapshot);
            if (prerequisiteError is not null)
            {
                transaction.Rollback();
                return Conflict(
                    "CafeBookingBootstrap.PrerequisiteMissing",
                    prerequisiteError);
            }

            CafeBookingBootstrapAuthorityV1 localAuthority =
                CafeBookingBootstrapAuthorityReader.Read(
                    connection,
                    transaction,
                    snapshot.StoreId);

            bool hasLocalAuthority =
                HasAuthority(localAuthority);

            bool reconstructed = false;

            if (hasLocalAuthority)
            {
                CafeBookingBootstrapV1 local =
                    CafeBookingBootstrapProtocolV1.Create(
                        snapshot.CentralInstanceId,
                        snapshot.StoreId,
                        snapshot.BaselineServerSequence,
                        localAuthority);

                if (!SameAuthority(local, snapshot))
                {
                    transaction.Rollback();
                    return Conflict(
                        "CafeBookingBootstrap.NonEmptyDomainConflict",
                        "Existing C9 Booking history differs from supplied bootstrap authority; history is preserved for reconciliation.");
                }
            }
            else
            {
                InsertAuthority(
                    connection,
                    transaction,
                    snapshot.Authority);
                reconstructed = true;

                CafeBookingBootstrapAuthorityV1 appliedAuthority =
                    CafeBookingBootstrapAuthorityReader.Read(
                        connection,
                        transaction,
                        snapshot.StoreId);

                CafeBookingBootstrapV1 applied =
                    CafeBookingBootstrapProtocolV1.Create(
                        snapshot.CentralInstanceId,
                        snapshot.StoreId,
                        snapshot.BaselineServerSequence,
                        appliedAuthority);

                if (!SameAuthority(applied, snapshot))
                {
                    transaction.Rollback();
                    return Conflict(
                        "CafeBookingBootstrap.AuthorityMismatch",
                        "Reconstructed C9 Booking history does not exactly match supplied bootstrap authority.");
                }
            }

            InsertState(connection, transaction, snapshot);
            AssertForeignKeysClean(connection, transaction);

            transaction.Commit();

            return Result<CafeBookingBootstrapApplyResult>.Success(
                CreateResult(
                    snapshot,
                    isReplay: false,
                    reconstructed: reconstructed));
        }
        catch (Exception ex) when (
            ex is SqliteException or
            InvalidOperationException or
            FormatException)
        {
            return Result<CafeBookingBootstrapApplyResult>.Fail(
                Error.Unexpected(
                    "CafeBookingBootstrap.ApplyFailed",
                    ex.Message));
        }
    }

    private static string? ValidateInheritedAuthority(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeBookingBootstrapV1 snapshot)
    {
        SyncRemoteState? f1e =
            SqliteSyncPullStore.ReadRemoteState(
                connection,
                transaction,
                snapshot.StoreId);
        SyncBootstrapEvidence? f1eEvidence =
            SqliteSyncBootstrapStore.ReadBootstrapEvidence(
                connection,
                transaction,
                snapshot.StoreId);

        if (f1e is null ||
            !f1e.CentralInstanceId.HasValue ||
            f1eEvidence is null)
        {
            return "C9 Booking bootstrap requires accepted F1E bootstrap authority first.";
        }

        if (f1e.CentralInstanceId.Value != snapshot.CentralInstanceId ||
            f1eEvidence.CentralInstanceId != snapshot.CentralInstanceId ||
            f1eEvidence.StoreId != snapshot.StoreId ||
            f1eEvidence.BaselineServerSequence != snapshot.BaselineServerSequence ||
            f1e.LastReceivedServerSequence != snapshot.BaselineServerSequence ||
            f1e.LastAppliedServerSequence != snapshot.BaselineServerSequence ||
            !string.Equals(
                f1e.State,
                AppliedBusinessChangeContract.Ready,
                StringComparison.Ordinal))
        {
            return "C9 Booking bootstrap must exactly extend accepted F1E StoreId, epoch and baseline authority.";
        }

        foreach (string table in new[]
        {
            "cafe_order_bootstrap_state",
            "cafe_financial_bootstrap_state",
            "cafe_cash_bootstrap_state"
        })
        {
            AuthorityRow? authority =
                ReadAuthority(
                    connection,
                    transaction,
                    table,
                    snapshot.StoreId);

            if (authority is null)
            {
                return "C9 Booking bootstrap requires accepted C4 Order, C5 Financial and C6 Cash bootstrap authority first.";
            }

            if (authority.CentralInstanceId != snapshot.CentralInstanceId ||
                authority.BaselineServerSequence != snapshot.BaselineServerSequence)
            {
                return "C9 Booking bootstrap must exactly extend accepted C4/C5/C6 epoch and baseline authority.";
            }
        }

        AuthorityRow? c8 =
            ReadAuthority(
                connection,
                transaction,
                "cafe_delivery_bootstrap_state",
                snapshot.StoreId);

        if (c8 is not null &&
            (c8.CentralInstanceId != snapshot.CentralInstanceId ||
             c8.BaselineServerSequence != snapshot.BaselineServerSequence))
        {
            return "Existing C8 Delivery bootstrap authority conflicts with the C9 epoch/baseline.";
        }

        return null;
    }

    private static AuthorityRow? ReadAuthority(
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
        command.Parameters.AddWithValue(
            "$store",
            storeId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? new AuthorityRow(
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
                   snapshot_id,snapshot_hash,authority_digest
            FROM cafe_booking_bootstrap_state
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
                reader.GetString(3),
                reader.GetString(4))
            : null;
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

    private static bool HasAuthority(
        CafeBookingBootstrapAuthorityV1 authority) =>
        authority.Bookings.Count != 0 ||
        authority.Revisions.Count != 0 ||
        authority.Lines.Count != 0 ||
        authority.ModifierSelections.Count != 0 ||
        authority.Operations.Count != 0 ||
        authority.Events.Count != 0 ||
        authority.DepositFacts.Count != 0 ||
        authority.DepositAllocations.Count != 0 ||
        authority.OrderLinks.Count != 0 ||
        authority.SaleLinks.Count != 0;

    private static bool SameAuthority(
        CafeBookingBootstrapV1 actual,
        CafeBookingBootstrapV1 expected) =>
        actual.BookingCount == expected.BookingCount &&
        actual.RevisionCount == expected.RevisionCount &&
        actual.LineCount == expected.LineCount &&
        actual.ModifierSelectionCount == expected.ModifierSelectionCount &&
        actual.OperationCount == expected.OperationCount &&
        actual.EventCount == expected.EventCount &&
        actual.DepositFactCount == expected.DepositFactCount &&
        actual.DepositAllocationCount == expected.DepositAllocationCount &&
        actual.OrderLinkCount == expected.OrderLinkCount &&
        actual.SaleLinkCount == expected.SaleLinkCount &&
        actual.AuthorityDigest == expected.AuthorityDigest &&
        actual.SnapshotHash == expected.SnapshotHash &&
        actual.SnapshotId == expected.SnapshotId;

    private static void InsertState(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeBookingBootstrapV1 snapshot)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cafe_booking_bootstrap_state(
                store_id,central_instance_id,baseline_server_sequence,
                snapshot_id,snapshot_hash,authority_digest,applied_at_utc)
            VALUES(
                $store,$instance,$baseline,$snapshot,$hash,$digest,$at);
            """;
        command.Parameters.AddWithValue("$store", snapshot.StoreId.ToString("D"));
        command.Parameters.AddWithValue("$instance", snapshot.CentralInstanceId.ToString("D"));
        command.Parameters.AddWithValue("$baseline", snapshot.BaselineServerSequence);
        command.Parameters.AddWithValue("$snapshot", snapshot.SnapshotId.ToString("D"));
        command.Parameters.AddWithValue("$hash", snapshot.SnapshotHash);
        command.Parameters.AddWithValue("$digest", snapshot.AuthorityDigest);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void AssertForeignKeysClean(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA foreign_key_check;";
        using SqliteDataReader reader = command.ExecuteReader();

        if (reader.Read())
            throw new InvalidOperationException(
                "C9 Booking bootstrap completed with foreign-key violations.");
    }

    private static Result<CafeBookingBootstrapApplyResult> Conflict(
        string code,
        string message) =>
        Result<CafeBookingBootstrapApplyResult>.Fail(
            Error.Conflict(code, message));

    private static CafeBookingBootstrapApplyResult CreateResult(
        CafeBookingBootstrapV1 snapshot,
        bool isReplay,
        bool reconstructed) =>
        new(
            isReplay,
            reconstructed,
            snapshot.SnapshotId,
            snapshot.SnapshotHash,
            snapshot.AuthorityDigest,
            snapshot.BaselineServerSequence,
            snapshot.BookingCount,
            snapshot.RevisionCount,
            snapshot.DepositFactCount,
            snapshot.OrderLinkCount,
            snapshot.SaleLinkCount);

    private sealed record AuthorityRow(
        Guid CentralInstanceId,
        long BaselineServerSequence);

    private sealed record ExistingState(
        Guid CentralInstanceId,
        long BaselineServerSequence,
        Guid SnapshotId,
        string SnapshotHash,
        string AuthorityDigest);
}
