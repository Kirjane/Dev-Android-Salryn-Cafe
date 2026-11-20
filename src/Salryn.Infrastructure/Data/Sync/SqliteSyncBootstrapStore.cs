using System.Text.Json;
using Microsoft.Data.Sqlite;
using Salryn.Infrastructure.Data.Migrations;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

public sealed class SqliteSyncBootstrapStore
{
    private const string BootstrapPasswordPlaceholder = "!SYNC-BOOTSTRAP-NONLOGIN!";
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteSyncBootstrapStore(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public SyncBootstrapApplyResult Apply(
        SyncBootstrapSnapshotV1 snapshot,
        DateTimeOffset? appliedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        SyncBootstrapValidationResult validation = SyncBootstrapSnapshotProtocolV1.Validate(snapshot);
        if (!validation.IsValid)
        {
            return SyncBootstrapApplyResult.Fail(
                null,
                validation.ErrorCode ?? "Sync.BootstrapInvalid",
                validation.ErrorMessage ?? "Bootstrap snapshot is invalid.");
        }

        SyncBootstrapApplyResult? financialBaselineFailure =
            ValidateFinancialBaseline(snapshot);
        if (financialBaselineFailure is not null)
        {
            return financialBaselineFailure;
        }

        using SqliteConnection connection = _connectionFactory.CreateConnection();
        connection.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();

        try
        {
            SyncRemoteState? existing =
                SqliteSyncPullStore.ReadRemoteState(connection, transaction, snapshot.StoreId);
            SyncBootstrapEvidence? existingEvidence =
                ReadBootstrapEvidence(connection, transaction, snapshot.StoreId);

            if (existing is not null)
            {
                bool exactReplay =
                    existing.CentralInstanceId == snapshot.CentralInstanceId &&
                    existing.LastReceivedServerSequence == snapshot.BaselineServerSequence &&
                    existing.LastAppliedServerSequence == snapshot.BaselineServerSequence &&
                    existingEvidence is not null &&
                    string.Equals(existingEvidence.SnapshotId, snapshot.SnapshotId, StringComparison.Ordinal) &&
                    string.Equals(existingEvidence.SnapshotHash, snapshot.SnapshotHash, StringComparison.Ordinal);

                if (exactReplay)
                {
                    transaction.Commit();
                    return SyncBootstrapApplyResult.Success(
                        existing,
                        existingEvidence!,
                        isReplay: true);
                }

                MarkExistingRemoteStateReconciliation(
                    connection,
                    transaction,
                    snapshot.StoreId,
                    "Sync.BootstrapAuthorityConflict: existing remote/bootstrap authority differs from the supplied snapshot.");

                SyncRemoteState conflictState =
                    SqliteSyncPullStore.ReadRemoteState(connection, transaction, snapshot.StoreId)
                    ?? throw new InvalidOperationException("Remote state disappeared during bootstrap conflict handling.");
                transaction.Commit();

                return SyncBootstrapApplyResult.Fail(
                    conflictState,
                    "Sync.BootstrapAuthorityConflict",
                    "Existing remote/bootstrap authority conflicts with the supplied snapshot; retained evidence requires reconciliation.");
            }

            if (!IsFreshOperationalClient(connection, transaction))
            {
                transaction.Rollback();
                return SyncBootstrapApplyResult.Fail(
                    null,
                    "Sync.BootstrapNonEmptyClientConflict",
                    "Bootstrap refused to overwrite a non-empty operational client. Existing business/sync evidence is preserved.");
            }

            EnsureDeploymentIdentityCanBeBound(connection, transaction);
            BindDeploymentStore(connection, transaction, snapshot.StoreId);

            InsertRoles(connection, transaction, snapshot.Roles);
            InsertUnits(connection, transaction, snapshot.Units);
            InsertUsers(connection, transaction, snapshot.Users);
            InsertProducts(connection, transaction, snapshot.Products);
            InsertCashierSessions(connection, transaction, snapshot.CashierSessions);
            InsertCustomers(connection, transaction, snapshot.Customers);

            DateTimeOffset appliedAt = (appliedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
            InsertRemoteState(connection, transaction, snapshot, appliedAt);

            SyncBootstrapEvidence evidence = new(
                snapshot.StoreId,
                snapshot.CentralInstanceId,
                snapshot.BaselineServerSequence,
                snapshot.SnapshotId,
                snapshot.SnapshotHash,
                appliedAt);

            InsertBootstrapAudit(connection, transaction, evidence);

            SyncRemoteState finalState =
                SqliteSyncPullStore.ReadRemoteState(connection, transaction, snapshot.StoreId)
                ?? throw new InvalidOperationException("Bootstrap remote state was not persisted.");

            transaction.Commit();
            return SyncBootstrapApplyResult.Success(finalState, evidence, isReplay: false);
        }
        catch (Exception ex) when (
            ex is SqliteException or InvalidOperationException or FormatException or JsonException)
        {
            TryRollback(transaction);
            return SyncBootstrapApplyResult.Fail(
                null,
                "Sync.BootstrapApplyFailed",
                ex.Message);
        }
    }

    public SyncBootstrapEvidence? ReadEvidence(Guid storeId)
    {
        using SqliteConnection connection = _connectionFactory.CreateConnection();
        connection.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        SyncBootstrapEvidence? evidence = ReadBootstrapEvidence(connection, transaction, storeId);
        transaction.Commit();
        return evidence;
    }

    private static SyncBootstrapApplyResult? ValidateFinancialBaseline(
        SyncBootstrapSnapshotV1 snapshot)
    {
        SyncBootstrapCashierSessionV1? nonZeroCash =
            snapshot.CashierSessions.FirstOrDefault(x => x.LedgerNetDeltaCents != 0);

        if (nonZeroCash is not null)
        {
            return SyncBootstrapApplyResult.Fail(
                null,
                "Sync.BootstrapFinancialBaselineUnsupported",
                "Bootstrap requires a clean financial baseline: open cashier session " +
                nonZeroCash.Id.ToString("D") +
                " has non-zero ledger net delta " +
                nonZeroCash.LedgerNetDeltaCents.ToString() +
                " cents.");
        }

        SyncBootstrapCustomerV1? nonZeroReceivable =
            snapshot.Customers.FirstOrDefault(x => x.OutstandingReceivableCents != 0);

        if (nonZeroReceivable is not null)
        {
            return SyncBootstrapApplyResult.Fail(
                null,
                "Sync.BootstrapFinancialBaselineUnsupported",
                "Bootstrap requires a clean financial baseline: customer " +
                nonZeroReceivable.Id.ToString("D") +
                " has non-zero outstanding receivable " +
                nonZeroReceivable.OutstandingReceivableCents.ToString() +
                " cents.");
        }

        return null;
    }

    private static bool IsFreshOperationalClient(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        string[] evidenceTables =
        [
            "users",
            "products",
            "customers",
            "cashier_sessions",
            "sales",
            "sale_items",
            "payments",
            "stock_movements",
            "accounts_receivable_entries",
            "accounts_receivable_collections",
            "cash_movements",
            "sync_outbox",
            "sync_change_inbox",
            "sync_remote_state"
        ];

        foreach (string table in evidenceTables)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT COUNT(*) FROM " + table + ";";
            if (Convert.ToInt64(command.ExecuteScalar() ?? 0L) != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static void EnsureDeploymentIdentityCanBeBound(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM deployment_identity
            WHERE id=$id;
            """;
        command.Parameters.AddWithValue("$id", DeploymentIdentitySchemaMigration.DeploymentIdentityRowId);

        if (Convert.ToInt64(command.ExecuteScalar() ?? 0L) != 1)
        {
            throw new InvalidOperationException(
                "Bootstrap requires the single seeded production deployment identity.");
        }
    }

    private static void BindDeploymentStore(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE deployment_identity
            SET store_id=$store,
                updated_at=$updated
            WHERE id=$id;
            """;
        command.Parameters.AddWithValue("$store", storeId.ToString("D"));
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", DeploymentIdentitySchemaMigration.DeploymentIdentityRowId);

        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException("Bootstrap could not bind the deployment store identity.");
        }
    }

    private static void InsertRoles(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<SyncBootstrapRoleV1> roles)
    {
        foreach (SyncBootstrapRoleV1 role in roles)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO roles(id,name,is_system_role,created_at)
                VALUES($id,$name,$system,$at);
                """;
            command.Parameters.AddWithValue("$id", role.Id.ToString("D"));
            command.Parameters.AddWithValue("$name", role.Name);
            command.Parameters.AddWithValue("$system", role.IsSystemRole ? 1 : 0);
            command.Parameters.AddWithValue("$at", DateTimeOffset.UnixEpoch.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    private static void InsertUnits(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<SyncBootstrapUnitV1> units)
    {
        foreach (SyncBootstrapUnitV1 unit in units)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO units(id,name,abbreviation,allows_decimal_quantity,is_active)
                VALUES($id,$name,$abbr,$decimal,$active);
                """;
            command.Parameters.AddWithValue("$id", unit.Id.ToString("D"));
            command.Parameters.AddWithValue("$name", unit.Name);
            command.Parameters.AddWithValue("$abbr", unit.Abbreviation ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$decimal", unit.AllowsDecimalQuantity ? 1 : 0);
            command.Parameters.AddWithValue("$active", unit.IsActive ? 1 : 0);
            command.ExecuteNonQuery();
        }
    }

    private static void InsertUsers(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<SyncBootstrapUserV1> users)
    {
        foreach (SyncBootstrapUserV1 user in users)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO users(
                    id,username,display_name,password_hash,role_id,status,created_at,updated_at)
                VALUES($id,$username,$display,$password,$role,$status,$at,NULL);
                """;
            command.Parameters.AddWithValue("$id", user.Id.ToString("D"));
            command.Parameters.AddWithValue("$username", user.Username);
            command.Parameters.AddWithValue("$display", user.DisplayName);
            command.Parameters.AddWithValue("$password", BootstrapPasswordPlaceholder);
            command.Parameters.AddWithValue("$role", user.RoleId.ToString("D"));
            command.Parameters.AddWithValue("$status", user.Status);
            command.Parameters.AddWithValue("$at", DateTimeOffset.UnixEpoch.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    private static void InsertProducts(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<SyncBootstrapProductV1> products)
    {
        foreach (SyncBootstrapProductV1 product in products)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO products(
                    id,sku,barcode,name,description,category_id,brand_id,unit_id,supplier_id,
                    cost_price_cents,selling_price_cents,wholesale_price_cents,
                    stock_on_hand_x1000,reorder_level_x1000,track_stock,allow_discount,
                    is_active,notes,created_at,updated_at,tax_classification)
                VALUES(
                    $id,$sku,$barcode,$name,$description,NULL,NULL,$unit,NULL,
                    $cost,$selling,$wholesale,$stock,$reorder,$track,$discount,
                    $active,NULL,$at,NULL,$tax);
                """;
            command.Parameters.AddWithValue("$id", product.Id.ToString("D"));
            command.Parameters.AddWithValue("$sku", product.Sku ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$barcode", product.Barcode ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$name", product.Name);
            command.Parameters.AddWithValue("$description", product.Description ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$unit", product.UnitId.ToString("D"));
            command.Parameters.AddWithValue("$cost", product.CostPriceCents ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$selling", product.SellingPriceCents);
            command.Parameters.AddWithValue("$wholesale", product.WholesalePriceCents ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$stock", product.StockOnHandX1000);
            command.Parameters.AddWithValue("$reorder", product.ReorderLevelX1000);
            command.Parameters.AddWithValue("$track", product.TrackStock ? 1 : 0);
            command.Parameters.AddWithValue("$discount", product.AllowDiscount ? 1 : 0);
            command.Parameters.AddWithValue("$active", product.IsActive ? 1 : 0);
            command.Parameters.AddWithValue("$at", DateTimeOffset.UnixEpoch.ToString("O"));
            command.Parameters.AddWithValue("$tax", product.TaxClassification);
            command.ExecuteNonQuery();
        }
    }

    private static void InsertCashierSessions(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<SyncBootstrapCashierSessionV1> sessions)
    {
        foreach (SyncBootstrapCashierSessionV1 session in sessions)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO cashier_sessions(
                    id,user_id,opened_at,closed_at,opening_cash_cents,expected_cash_cents,
                    counted_cash_cents,variance_cents,status,notes)
                VALUES($id,$user,$opened,NULL,$opening,$expected,NULL,0,$status,$notes);
                """;
            command.Parameters.AddWithValue("$id", session.Id.ToString("D"));
            command.Parameters.AddWithValue("$user", session.UserId.ToString("D"));
            command.Parameters.AddWithValue("$opened", session.OpenedAt.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$opening", session.OpeningCashCents);
            command.Parameters.AddWithValue("$expected", session.ExpectedCashCents);
            command.Parameters.AddWithValue("$status", session.Status);
            command.Parameters.AddWithValue("$notes", session.Notes ?? (object)DBNull.Value);
            command.ExecuteNonQuery();
        }
    }

    private static void InsertCustomers(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<SyncBootstrapCustomerV1> customers)
    {
        foreach (SyncBootstrapCustomerV1 customer in customers)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO customers(
                    id,full_name,phone,address,note,credit_limit_cents,is_active,
                    created_by_user_id,created_at,updated_at)
                VALUES($id,$name,$phone,$address,$note,$limit,$active,$user,$created,$updated);
                """;
            command.Parameters.AddWithValue("$id", customer.Id.ToString("D"));
            command.Parameters.AddWithValue("$name", customer.FullName);
            command.Parameters.AddWithValue("$phone", customer.Phone ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$address", customer.Address ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$note", customer.Note ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$limit", customer.CreditLimitCents ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$active", customer.IsActive ? 1 : 0);
            command.Parameters.AddWithValue("$user", customer.CreatedByUserId.ToString("D"));
            command.Parameters.AddWithValue("$created", customer.CreatedAt.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$updated", customer.UpdatedAt.ToUniversalTime().ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    private static void InsertRemoteState(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncBootstrapSnapshotV1 snapshot,
        DateTimeOffset at)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO sync_remote_state(
                store_id,central_instance_id,
                last_received_server_sequence,last_applied_server_sequence,
                state,last_error,created_at_utc,updated_at_utc)
            VALUES($store,$instance,$baseline,$baseline,$state,NULL,$at,$at);
            """;
        command.Parameters.AddWithValue("$store", snapshot.StoreId.ToString("D"));
        command.Parameters.AddWithValue("$instance", snapshot.CentralInstanceId.ToString("D"));
        command.Parameters.AddWithValue("$baseline", snapshot.BaselineServerSequence);
        command.Parameters.AddWithValue("$state", AppliedBusinessChangeContract.Ready);
        command.Parameters.AddWithValue("$at", at.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void InsertBootstrapAudit(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncBootstrapEvidence evidence)
    {
        BootstrapAuditPayload payload = new(
            evidence.CentralInstanceId,
            evidence.BaselineServerSequence,
            evidence.SnapshotId,
            evidence.SnapshotHash);

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO audit_logs(
                id,user_id,action,entity_type,entity_id,
                old_value_json,new_value_json,reason,created_at)
            VALUES($id,NULL,$action,$type,$entity,NULL,$json,$reason,$at);
            """;
        command.Parameters.AddWithValue("$id", Guid.CreateVersion7().ToString("D"));
        command.Parameters.AddWithValue("$action", SyncProjectionContract.BootstrapAuditAction);
        command.Parameters.AddWithValue("$type", SyncProjectionContract.BootstrapAuditEntityType);
        command.Parameters.AddWithValue("$entity", evidence.StoreId.ToString("D"));
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(payload));
        command.Parameters.AddWithValue(
            "$reason",
            "Explicit F1E bootstrap authority through server sequence " +
            evidence.BaselineServerSequence.ToString() + ".");
        command.Parameters.AddWithValue("$at", evidence.AppliedAtUtc.ToString("O"));
        command.ExecuteNonQuery();
    }

    internal static SyncBootstrapEvidence? ReadBootstrapEvidence(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT new_value_json, created_at
            FROM audit_logs
            WHERE action=$action
              AND entity_type=$type
              AND entity_id=$entity
            ORDER BY created_at DESC, id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$action", SyncProjectionContract.BootstrapAuditAction);
        command.Parameters.AddWithValue("$type", SyncProjectionContract.BootstrapAuditEntityType);
        command.Parameters.AddWithValue("$entity", storeId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        BootstrapAuditPayload? payload =
            JsonSerializer.Deserialize<BootstrapAuditPayload>(reader.GetString(0));
        if (payload is null)
        {
            return null;
        }

        return new SyncBootstrapEvidence(
            storeId,
            payload.CentralInstanceId,
            payload.BaselineServerSequence,
            payload.SnapshotId,
            payload.SnapshotHash,
            DateTimeOffset.Parse(reader.GetString(1)));
    }

    private static void MarkExistingRemoteStateReconciliation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId,
        string error)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE sync_remote_state
            SET state=$state,
                last_error=$error,
                updated_at_utc=$updated
            WHERE store_id=$store;
            """;
        command.Parameters.AddWithValue("$state", AppliedBusinessChangeContract.ReconciliationRequired);
        command.Parameters.AddWithValue("$error", error);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$store", storeId.ToString("D"));
        command.ExecuteNonQuery();
    }

    private static void TryRollback(SqliteTransaction transaction)
    {
        try
        {
            transaction.Rollback();
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
        }
    }

    private sealed record BootstrapAuditPayload(
        Guid CentralInstanceId,
        long BaselineServerSequence,
        string SnapshotId,
        string SnapshotHash);
}
