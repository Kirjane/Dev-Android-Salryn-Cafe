using Microsoft.Data.Sqlite;

namespace Salryn.Sync.Central.Server;

public sealed record CentralServerStateSnapshot(
    Guid CentralInstanceId,
    long ServerEpoch,
    long HeadSequence,
    string LeaseSigningPrivateKeyPem,
    string LeaseSigningPublicKeySpki,
    DateTimeOffset UpdatedAtUtc);

public sealed class CentralSyncDatabase
{
    private readonly CentralServerOptions _options;
    private readonly string _connectionString;

    public CentralSyncDatabase(CentralServerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        string? directory = Path.GetDirectoryName(_options.DatabasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = _options.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        };
        _connectionString = builder.ToString();
    }

    public string DatabasePath => _options.DatabasePath;

    public SqliteConnection OpenConnection()
    {
        SqliteConnection connection = new(_connectionString);
        connection.Open();
        ApplyPragmas(connection);
        return connection;
    }

    public static void ApplyPragmas(SqliteConnection connection)
    {
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = """
            PRAGMA foreign_keys = ON;
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA busy_timeout = 5000;
            """;
        cmd.ExecuteNonQuery();
    }

    public void Initialize()
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        using (SqliteCommand cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS sync_central_schema (
                    version INTEGER PRIMARY KEY,
                    product_id TEXT NOT NULL,
                    applied_at_utc TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS sync_server_state (
                    singleton_id INTEGER PRIMARY KEY CHECK (singleton_id = 1),
                    central_instance_id TEXT NOT NULL,
                    server_epoch INTEGER NOT NULL,
                    head_sequence INTEGER NOT NULL,
                    lease_signing_private_key_pem TEXT NOT NULL,
                    lease_signing_public_key_spki TEXT NOT NULL,
                    updated_at_utc TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS sync_inbox_operations (
                    operation_id TEXT NOT NULL,
                    tenant_id TEXT NOT NULL,
                    store_id TEXT NOT NULL,
                    device_id TEXT NOT NULL,
                    actor_user_id TEXT NOT NULL,
                    local_sequence INTEGER NOT NULL,
                    operation_type TEXT NOT NULL,
                    schema_version INTEGER NOT NULL,
                    aggregate_id TEXT NOT NULL,
                    payload_hash TEXT NOT NULL,
                    operation_hash TEXT NOT NULL,
                    envelope_json TEXT NOT NULL,
                    received_sequence INTEGER NOT NULL UNIQUE,
                    status TEXT NOT NULL,
                    result_code TEXT NOT NULL,
                    result_json TEXT NOT NULL,
                    received_at_utc TEXT NOT NULL,
                    resolved_at_utc TEXT NOT NULL,
                    PRIMARY KEY (tenant_id, store_id, operation_id)
                );

                CREATE INDEX IF NOT EXISTS ix_sync_inbox_store_device_seq
                    ON sync_inbox_operations (tenant_id, store_id, device_id, local_sequence);

                CREATE TABLE IF NOT EXISTS sync_change_feed (
                    server_sequence INTEGER PRIMARY KEY AUTOINCREMENT,
                    change_id TEXT NOT NULL UNIQUE,
                    tenant_id TEXT NOT NULL,
                    store_id TEXT NOT NULL,
                    source_operation_id TEXT NOT NULL,
                    origin_device_id TEXT NOT NULL,
                    origin_local_sequence INTEGER NOT NULL,
                    change_type TEXT NOT NULL,
                    schema_version INTEGER NOT NULL,
                    aggregate_id TEXT NOT NULL,
                    payload_json TEXT NOT NULL,
                    payload_hash TEXT NOT NULL,
                    change_hash TEXT NOT NULL,
                    created_at_utc TEXT NOT NULL,
                    UNIQUE (tenant_id, store_id, source_operation_id)
                );

                CREATE INDEX IF NOT EXISTS ix_sync_change_feed_tenant_store_seq
                    ON sync_change_feed (tenant_id, store_id, server_sequence);

                CREATE TABLE IF NOT EXISTS sync_revoked_quarantine (
                    quarantine_id TEXT PRIMARY KEY,
                    tenant_id TEXT NOT NULL,
                    store_id TEXT NOT NULL,
                    device_id TEXT NOT NULL,
                    operation_id TEXT NOT NULL,
                    operation_type TEXT NOT NULL,
                    operation_hash TEXT NOT NULL,
                    payload_hash TEXT NOT NULL,
                    envelope_json TEXT NOT NULL,
                    received_at_utc TEXT NOT NULL,
                    review_status TEXT NOT NULL DEFAULT 'AdminReviewRequired',
                    review_note TEXT NULL,
                    reviewed_by_admin_id TEXT NULL,
                    reviewed_at_utc TEXT NULL,
                    UNIQUE (device_id, operation_id)
                );

                CREATE INDEX IF NOT EXISTS ix_sync_revoked_quarantine_tenant_store
                    ON sync_revoked_quarantine (tenant_id, store_id, review_status, received_at_utc);

                CREATE TABLE IF NOT EXISTS sync_device_invites (
                    invite_id TEXT PRIMARY KEY,
                    tenant_id TEXT NOT NULL,
                    store_id TEXT NOT NULL,
                    product_id TEXT NOT NULL,
                    device_role TEXT NOT NULL,
                    join_code_hash TEXT NOT NULL UNIQUE,
                    expires_at_utc TEXT NOT NULL,
                    consumed_at_utc TEXT NULL,
                    consumed_by_device_id TEXT NULL,
                    created_by_admin_id TEXT NOT NULL,
                    created_at_utc TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS sync_device_registry (
                    device_id TEXT PRIMARY KEY,
                    tenant_id TEXT NOT NULL,
                    store_id TEXT NOT NULL,
                    product_id TEXT NOT NULL,
                    device_name TEXT NOT NULL,
                    device_role TEXT NOT NULL,
                    public_key_spki TEXT NOT NULL,
                    public_key_thumbprint TEXT NOT NULL,
                    status TEXT NOT NULL,
                    enrolled_at_utc TEXT NOT NULL,
                    approved_at_utc TEXT NULL,
                    approved_by_admin_id TEXT NULL,
                    revoked_at_utc TEXT NULL,
                    revoked_by_admin_id TEXT NULL,
                    revoke_reason TEXT NULL,
                    last_seen_at_utc TEXT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_sync_device_registry_tenant_store
                    ON sync_device_registry (tenant_id, store_id, status);

                CREATE TABLE IF NOT EXISTS sync_device_challenges (
                    challenge_id TEXT PRIMARY KEY,
                    device_id TEXT NOT NULL,
                    nonce_base64 TEXT NOT NULL,
                    expires_at_utc TEXT NOT NULL,
                    consumed_at_utc TEXT NULL
                );

                CREATE TABLE IF NOT EXISTS sync_device_tokens (
                    token_hash TEXT PRIMARY KEY,
                    device_id TEXT NOT NULL,
                    tenant_id TEXT NOT NULL,
                    store_id TEXT NOT NULL,
                    product_id TEXT NOT NULL,
                    issued_at_utc TEXT NOT NULL,
                    expires_at_utc TEXT NOT NULL,
                    revoked_at_utc TEXT NULL
                );

                CREATE TABLE IF NOT EXISTS central_admin_users (
                    admin_id TEXT PRIMARY KEY,
                    tenant_id TEXT NOT NULL,
                    store_id TEXT NOT NULL,
                    product_id TEXT NOT NULL,
                    username TEXT NOT NULL UNIQUE,
                    display_name TEXT NOT NULL,
                    role TEXT NOT NULL,
                    password_hash TEXT NOT NULL,
                    failed_attempts INTEGER NOT NULL DEFAULT 0,
                    locked_until_utc TEXT NULL,
                    is_active INTEGER NOT NULL DEFAULT 1,
                    created_at_utc TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS central_admin_sessions (
                    token_hash TEXT PRIMARY KEY,
                    admin_id TEXT NOT NULL,
                    tenant_id TEXT NOT NULL,
                    store_id TEXT NOT NULL,
                    product_id TEXT NOT NULL,
                    role TEXT NOT NULL,
                    issued_at_utc TEXT NOT NULL,
                    expires_at_utc TEXT NOT NULL,
                    revoked_at_utc TEXT NULL
                );

                CREATE TABLE IF NOT EXISTS central_admin_audit_logs (
                    audit_id TEXT PRIMARY KEY,
                    tenant_id TEXT NOT NULL,
                    store_id TEXT NOT NULL,
                    actor_id TEXT NOT NULL,
                    action TEXT NOT NULL,
                    target_id TEXT NULL,
                    details_json TEXT NOT NULL,
                    created_at_utc TEXT NOT NULL
                );
                """;
            cmd.ExecuteNonQuery();
        }

        // Verify or record schema product identity (fail-closed if another product's DB)
        using (SqliteCommand check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "SELECT product_id FROM sync_central_schema ORDER BY version DESC LIMIT 1;";
            object? existingProduct = check.ExecuteScalar();
            if (existingProduct is string product &&
                !string.Equals(product, CentralServerOptions.CanonicalProductId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"ERR_WRONG_PRODUCT_DATABASE: Central DB belongs to '{product}', expected '{CentralServerOptions.CanonicalProductId}'.");
            }
        }

        string nowIso = DateTimeOffset.UtcNow.ToString("O");
        using (SqliteCommand schemaInsert = connection.CreateCommand())
        {
            schemaInsert.Transaction = transaction;
            schemaInsert.CommandText = """
                INSERT OR IGNORE INTO sync_central_schema (version, product_id, applied_at_utc)
                VALUES ($version, $productId, $now);
                """;
            schemaInsert.Parameters.AddWithValue("$version", CentralServerOptions.CentralSchemaVersion);
            schemaInsert.Parameters.AddWithValue("$productId", CentralServerOptions.CanonicalProductId);
            schemaInsert.Parameters.AddWithValue("$now", nowIso);
            schemaInsert.ExecuteNonQuery();
        }

        // Ensure singleton server state with persistent ECDSA P-256 lease signing key
        using (SqliteCommand stateCheck = connection.CreateCommand())
        {
            stateCheck.Transaction = transaction;
            stateCheck.CommandText = "SELECT COUNT(*) FROM sync_server_state WHERE singleton_id = 1;";
            long count = Convert.ToInt64(stateCheck.ExecuteScalar());
            if (count == 0)
            {
                (string privatePem, string publicSpki) = CentralCrypto.CreateEcdsaP256KeyPair();
                using SqliteCommand insertState = connection.CreateCommand();
                insertState.Transaction = transaction;
                insertState.CommandText = """
                    INSERT INTO sync_server_state (
                        singleton_id, central_instance_id, server_epoch, head_sequence,
                        lease_signing_private_key_pem, lease_signing_public_key_spki, updated_at_utc)
                    VALUES (1, $instanceId, 1, 0, $privPem, $pubSpki, $now);
                    """;
                insertState.Parameters.AddWithValue("$instanceId", Guid.CreateVersion7().ToString("D"));
                insertState.Parameters.AddWithValue("$privPem", privatePem);
                insertState.Parameters.AddWithValue("$pubSpki", publicSpki);
                insertState.Parameters.AddWithValue("$now", nowIso);
                insertState.ExecuteNonQuery();
            }
        }

        // Seed bootstrap admin if configured and not already present
        if (_options.BootstrapAdmin is { } admin &&
            !string.IsNullOrWhiteSpace(admin.Username) &&
            !string.IsNullOrWhiteSpace(admin.Password) &&
            admin.TenantId != Guid.Empty &&
            admin.StoreId != Guid.Empty)
        {
            using SqliteCommand seedAdmin = connection.CreateCommand();
            seedAdmin.Transaction = transaction;
            seedAdmin.CommandText = """
                INSERT OR IGNORE INTO central_admin_users (
                    admin_id, tenant_id, store_id, product_id, username, display_name, role,
                    password_hash, failed_attempts, is_active, created_at_utc)
                VALUES (
                    $adminId, $tenantId, $storeId, $productId, $username, $displayName, $role,
                    $passwordHash, 0, 1, $now);
                """;
            seedAdmin.Parameters.AddWithValue("$adminId", Guid.CreateVersion7().ToString("D"));
            seedAdmin.Parameters.AddWithValue("$tenantId", admin.TenantId.ToString("D"));
            seedAdmin.Parameters.AddWithValue("$storeId", admin.StoreId.ToString("D"));
            seedAdmin.Parameters.AddWithValue("$productId", CentralServerOptions.CanonicalProductId);
            seedAdmin.Parameters.AddWithValue("$username", admin.Username.Trim().ToLowerInvariant());
            seedAdmin.Parameters.AddWithValue("$displayName", admin.DisplayName);
            seedAdmin.Parameters.AddWithValue("$role", string.IsNullOrWhiteSpace(admin.Role) ? "Owner" : admin.Role);
            seedAdmin.Parameters.AddWithValue("$passwordHash", CentralCrypto.HashPassword(admin.Password));
            seedAdmin.Parameters.AddWithValue("$now", nowIso);
            seedAdmin.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public CentralServerStateSnapshot ReadServerState(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            SELECT central_instance_id, server_epoch, head_sequence,
                   lease_signing_private_key_pem, lease_signing_public_key_spki, updated_at_utc
            FROM sync_server_state
            WHERE singleton_id = 1;
            """;
        using SqliteDataReader reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException("Central server state row missing.");
        }

        return new CentralServerStateSnapshot(
            Guid.Parse(reader.GetString(0)),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetString(3),
            reader.GetString(4),
            DateTimeOffset.Parse(reader.GetString(5)));
    }

    public void BackupTo(string targetBackupPath)
    {
        string? dir = Path.GetDirectoryName(targetBackupPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using SqliteConnection source = OpenConnection();
        using SqliteConnection destination = new(new SqliteConnectionStringBuilder
        {
            DataSource = targetBackupPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());
        destination.Open();
        source.BackupDatabase(destination);
    }

    // The former helper copied a potentially stale backup over an active SQLite database
    // and incremented the epoch *inside the restored data*. That can reuse an old epoch
    // and erase previously acknowledged financial operations. A safe restore requires
    // external high-watermark evidence, quiescence, staging, reconciliation and fencing.
    // Until that protocol is implemented and verified, fail before ANY destination write.
    public static void RestoreAndBumpEpoch(string backupFilePath, string targetDatabasePath, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDatabasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (!File.Exists(backupFilePath))
        {
            throw new FileNotFoundException("Backup file not found.", backupFilePath);
        }

        throw new InvalidOperationException(
            "ERR_RESTORE_RECONCILIATION_REQUIRED: Restore is disabled until acknowledged " +
            "operations, prior epoch/instance, high-watermark and staged backup integrity " +
            "can be reconciled under a quiesced, independently fenced recovery protocol.");
    }
}
