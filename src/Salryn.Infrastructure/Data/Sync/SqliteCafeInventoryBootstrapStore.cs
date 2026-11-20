using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Infrastructure.Data.Repositories.Inventory;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

public sealed record CafeInventoryBootstrapApplyResult(
    bool IsReplay,
    Guid SnapshotId,
    string SnapshotHash,
    long BaselineServerSequence);

public sealed class SqliteCafeInventoryBootstrapStore
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteCafeInventoryBootstrapStore(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public Result<CafeInventoryBootstrapApplyResult> Apply(
        CafeInventoryBootstrapV1 snapshot)
    {
        if (!CafeInventoryBootstrapProtocolV1.Validate(snapshot))
        {
            return Result<CafeInventoryBootstrapApplyResult>.Fail(
                Error.Conflict(
                    "CafeInventoryBootstrap.SnapshotIntegrityMismatch",
                    "Cafe inventory bootstrap identity/hash does not match canonical content."));
        }

        string? supplierError = ValidateSupplierPublication(snapshot.SupplierDirectory);
        if (supplierError is not null)
        {
            return Result<CafeInventoryBootstrapApplyResult>.Fail(
                Error.Conflict(
                    "CafeInventoryBootstrap.SupplierPublicationInvalid",
                    supplierError));
        }

        try
        {
            using SqliteConnection connection = _connectionFactory.CreateConnection();
            connection.Open();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Guid localStore = ReadStoreId(connection, transaction);
            if (localStore != snapshot.StoreId)
            {
                transaction.Rollback();
                return Result<CafeInventoryBootstrapApplyResult>.Fail(
                    Error.Conflict(
                        "CafeInventoryBootstrap.StoreMismatch",
                        "Cafe inventory bootstrap StoreId does not match local deployment authority."));
            }

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
            C2Authority? c2 = ReadC2Authority(
                connection,
                transaction,
                snapshot.StoreId);

            if (f1eState is null ||
                !f1eState.CentralInstanceId.HasValue ||
                f1eEvidence is null ||
                c2 is null)
            {
                transaction.Rollback();
                return Result<CafeInventoryBootstrapApplyResult>.Fail(
                    Error.Conflict(
                        "CafeInventoryBootstrap.AuthorityMissing",
                        "C3 bootstrap requires accepted F1E and C2 bootstrap authority first."));
            }

            bool authorityMatches =
                f1eState.CentralInstanceId.Value == snapshot.CentralInstanceId &&
                f1eEvidence.CentralInstanceId == snapshot.CentralInstanceId &&
                f1eEvidence.StoreId == snapshot.StoreId &&
                f1eEvidence.BaselineServerSequence == snapshot.BaselineServerSequence &&
                f1eState.LastReceivedServerSequence == snapshot.BaselineServerSequence &&
                f1eState.LastAppliedServerSequence == snapshot.BaselineServerSequence &&
                string.Equals(
                    f1eState.State,
                    AppliedBusinessChangeContract.Ready,
                    StringComparison.Ordinal) &&
                c2.CentralInstanceId == snapshot.CentralInstanceId &&
                c2.BaselineServerSequence == snapshot.BaselineServerSequence;

            if (!authorityMatches)
            {
                transaction.Rollback();
                return Result<CafeInventoryBootstrapApplyResult>.Fail(
                    Error.Conflict(
                        "CafeInventoryBootstrap.AuthorityConflict",
                        "C3 bootstrap must exactly extend accepted F1E/C2 StoreId, epoch and baseline authority."));
            }

            ExistingState? existing = ReadState(
                connection,
                transaction,
                snapshot.StoreId);

            if (existing is not null)
            {
                bool exact =
                    existing.CentralInstanceId == snapshot.CentralInstanceId &&
                    existing.BaselineServerSequence == snapshot.BaselineServerSequence &&
                    existing.SnapshotId == snapshot.SnapshotId &&
                    string.Equals(existing.SnapshotHash, snapshot.SnapshotHash, StringComparison.Ordinal);

                transaction.Commit();
                return exact
                    ? Result<CafeInventoryBootstrapApplyResult>.Success(
                        new CafeInventoryBootstrapApplyResult(
                            true,
                            snapshot.SnapshotId,
                            snapshot.SnapshotHash,
                            snapshot.BaselineServerSequence))
                    : Result<CafeInventoryBootstrapApplyResult>.Fail(
                        Error.Conflict(
                            "CafeInventoryBootstrap.EpochConflict",
                            "Different C3 bootstrap evidence already exists; explicit reconciliation/rebootstrap is required."));
            }

            if (ScalarLong(connection, transaction,
                    "SELECT COUNT(*) FROM supplier_directory_revisions;") != 0 ||
                ScalarLong(connection, transaction,
                    "SELECT COUNT(*) FROM purchase_receipts;") != 0 ||
                ScalarLong(connection, transaction,
                    "SELECT COUNT(*) FROM inventory_waste_events;") != 0 ||
                ScalarLong(connection, transaction,
                    "SELECT COUNT(*) FROM inventory_adjustment_events;") != 0 ||
                ScalarLong(connection, transaction,
                    "SELECT COUNT(*) FROM inventory_count_sessions WHERE state='Finalized';") != 0)
            {
                transaction.Rollback();
                return Result<CafeInventoryBootstrapApplyResult>.Fail(
                    Error.Conflict(
                        "CafeInventoryBootstrap.NonEmptyDomainConflict",
                        "C3 bootstrap requires an empty C3 publication/factual domain."));
            }

            if (snapshot.SupplierDirectory is not null)
            {
                ApplySupplierPublication(
                    connection,
                    transaction,
                    snapshot.SupplierDirectory);
            }

            using SqliteCommand state = connection.CreateCommand();
            state.Transaction = transaction;
            state.CommandText = """
                INSERT INTO cafe_inventory_bootstrap_state(
                    store_id,central_instance_id,baseline_server_sequence,
                    snapshot_id,snapshot_hash,supplier_revision_id,applied_at_utc)
                VALUES($store,$instance,$baseline,$snapshot,$hash,$supplierRevision,$at);
                """;
            state.Parameters.AddWithValue("$store", snapshot.StoreId.ToString("D"));
            state.Parameters.AddWithValue("$instance", snapshot.CentralInstanceId.ToString("D"));
            state.Parameters.AddWithValue("$baseline", snapshot.BaselineServerSequence);
            state.Parameters.AddWithValue("$snapshot", snapshot.SnapshotId.ToString("D"));
            state.Parameters.AddWithValue("$hash", snapshot.SnapshotHash);
            state.Parameters.AddWithValue(
                "$supplierRevision",
                snapshot.SupplierDirectory is null
                    ? DBNull.Value
                    : snapshot.SupplierDirectory.RevisionId.ToString("D"));
            state.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            state.ExecuteNonQuery();

            transaction.Commit();
            return Result<CafeInventoryBootstrapApplyResult>.Success(
                new CafeInventoryBootstrapApplyResult(
                    false,
                    snapshot.SnapshotId,
                    snapshot.SnapshotHash,
                    snapshot.BaselineServerSequence));
        }
        catch (Exception ex) when (
            ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Result<CafeInventoryBootstrapApplyResult>.Fail(
                Error.Unexpected(
                    "CafeInventoryBootstrap.ApplyFailed",
                    ex.Message));
        }
    }

    private static string? ValidateSupplierPublication(
        CafeInventorySupplierPublicationV1? publication)
    {
        if (publication is null)
        {
            return null;
        }

        SupplierSnapshot[] suppliers = publication.Suppliers
            .Select(x => new SupplierSnapshot(
                x.SupplierId,
                x.Name,
                x.ContactNumber,
                x.Notes,
                x.IsActive,
                x.DisplayOrder))
            .ToArray();

        string contentHash = SqliteSupplierDirectoryStore.ComputeContentHash(suppliers);
        if (!string.Equals(contentHash, publication.ContentHash, StringComparison.Ordinal))
        {
            return "Supplier directory content hash is invalid.";
        }

        string publicationHash = SqliteSupplierDirectoryStore.ComputePublicationHash(
            publication.RevisionId,
            publication.StoreId,
            publication.RevisionNumber,
            publication.ContentHash,
            publication.PublishedAtUtc);

        if (!string.Equals(publicationHash, publication.PublicationHash, StringComparison.Ordinal))
        {
            return "Supplier directory publication hash is invalid.";
        }

        return null;
    }

    private static void ApplySupplierPublication(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeInventorySupplierPublicationV1 publication)
    {
        using (SqliteCommand revision = connection.CreateCommand())
        {
            revision.Transaction = transaction;
            revision.CommandText = """
                INSERT INTO supplier_directory_revisions(
                    revision_id,store_id,revision_number,content_hash,publication_hash,published_at_utc)
                VALUES($revision,$store,$number,$content,$publication,$at);
                """;
            revision.Parameters.AddWithValue("$revision", publication.RevisionId.ToString("D"));
            revision.Parameters.AddWithValue("$store", publication.StoreId.ToString("D"));
            revision.Parameters.AddWithValue("$number", publication.RevisionNumber);
            revision.Parameters.AddWithValue("$content", publication.ContentHash);
            revision.Parameters.AddWithValue("$publication", publication.PublicationHash);
            revision.Parameters.AddWithValue("$at", publication.PublishedAtUtc.ToUniversalTime().ToString("O"));
            revision.ExecuteNonQuery();
        }

        foreach (CafeInventoryBootstrapSupplierV1 supplier in
            publication.Suppliers.OrderBy(x => x.DisplayOrder).ThenBy(x => x.SupplierId))
        {
            using (SqliteCommand item = connection.CreateCommand())
            {
                item.Transaction = transaction;
                item.CommandText = """
                    INSERT INTO supplier_directory_revision_items(
                        revision_id,supplier_id,name,contact_number,notes,is_active,display_order)
                    VALUES($revision,$supplier,$name,$contact,$notes,$active,$order);
                    """;
                item.Parameters.AddWithValue("$revision", publication.RevisionId.ToString("D"));
                item.Parameters.AddWithValue("$supplier", supplier.SupplierId.ToString("D"));
                item.Parameters.AddWithValue("$name", supplier.Name);
                item.Parameters.AddWithValue("$contact", (object?)supplier.ContactNumber ?? DBNull.Value);
                item.Parameters.AddWithValue("$notes", (object?)supplier.Notes ?? DBNull.Value);
                item.Parameters.AddWithValue("$active", supplier.IsActive ? 1 : 0);
                item.Parameters.AddWithValue("$order", supplier.DisplayOrder);
                item.ExecuteNonQuery();
            }

            using SqliteCommand live = connection.CreateCommand();
            live.Transaction = transaction;
            live.CommandText = """
                INSERT INTO suppliers(id,name,contact_number,notes,is_active)
                VALUES($id,$name,$contact,$notes,$active)
                ON CONFLICT(id) DO UPDATE SET
                    name=excluded.name,
                    contact_number=excluded.contact_number,
                    notes=excluded.notes,
                    is_active=excluded.is_active;
                """;
            live.Parameters.AddWithValue("$id", supplier.SupplierId.ToString("D"));
            live.Parameters.AddWithValue("$name", supplier.Name);
            live.Parameters.AddWithValue("$contact", (object?)supplier.ContactNumber ?? DBNull.Value);
            live.Parameters.AddWithValue("$notes", (object?)supplier.Notes ?? DBNull.Value);
            live.Parameters.AddWithValue("$active", supplier.IsActive ? 1 : 0);
            live.ExecuteNonQuery();
        }

        using SqliteCommand current = connection.CreateCommand();
        current.Transaction = transaction;
        current.CommandText = """
            INSERT INTO supplier_directory_current_publication(
                store_id,revision_id,content_hash,publication_hash,applied_at_utc)
            VALUES($store,$revision,$content,$publication,$at);
            """;
        current.Parameters.AddWithValue("$store", publication.StoreId.ToString("D"));
        current.Parameters.AddWithValue("$revision", publication.RevisionId.ToString("D"));
        current.Parameters.AddWithValue("$content", publication.ContentHash);
        current.Parameters.AddWithValue("$publication", publication.PublicationHash);
        current.Parameters.AddWithValue("$at", publication.PublishedAtUtc.ToUniversalTime().ToString("O"));
        current.ExecuteNonQuery();
    }

    private static Guid ReadStoreId(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT store_id FROM deployment_identity ORDER BY created_at LIMIT 1;";
        object? value = command.ExecuteScalar();

        return value is string text && Guid.TryParse(text, out Guid id)
            ? id
            : throw new InvalidOperationException("Local deployment StoreId missing.");
    }

    private static C2Authority? ReadC2Authority(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT central_instance_id,baseline_server_sequence
            FROM cafe_ingredient_bootstrap_state
            WHERE store_id=$store
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$store", storeId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? new C2Authority(
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
            SELECT central_instance_id,baseline_server_sequence,snapshot_id,snapshot_hash
            FROM cafe_inventory_bootstrap_state
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

    private static long ScalarLong(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L);
    }

    private sealed record C2Authority(
        Guid CentralInstanceId,
        long BaselineServerSequence);

    private sealed record ExistingState(
        Guid CentralInstanceId,
        long BaselineServerSequence,
        Guid SnapshotId,
        string SnapshotHash);
}
