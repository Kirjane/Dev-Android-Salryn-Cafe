using Microsoft.Data.Sqlite;
using Salryn.Infrastructure.Data.Seeding;

namespace Salryn.Infrastructure.Data.Migrations;

public sealed class DeploymentIdentitySchemaMigration : IDatabaseMigration
{
    public const string DeploymentIdentityRowId = "00000000-0000-0000-0000-000000000901";

    public int Version => 9;

    public string Name => "Deployment identity schema";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, """
            CREATE TABLE deployment_identity (
                id TEXT PRIMARY KEY,
                store_id TEXT NOT NULL,
                store_name TEXT NOT NULL,
                host_id TEXT NOT NULL,
                installation_role INTEGER NOT NULL,
                host_display_name TEXT NOT NULL,
                is_database_authority INTEGER NOT NULL,
                is_official_sequence_authority INTEGER NOT NULL,
                is_journal_authority INTEGER NOT NULL,
                is_backup_authority INTEGER NOT NULL,
                is_client_only INTEGER NOT NULL,
                lan_transport_enabled INTEGER NOT NULL,
                terminal_id TEXT NOT NULL,
                terminal_name TEXT NOT NULL,
                terminal_role INTEGER NOT NULL,
                is_local_terminal INTEGER NOT NULL,
                terminal_registered_at TEXT NOT NULL,
                host_address TEXT NULL,
                host_port INTEGER NULL,
                pairing_credential_label TEXT NULL,
                host_fingerprint_placeholder TEXT NULL,
                connection_status INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """);

        SeedDefaultAllInOneIdentity(connection, transaction);
        SeedAdminDeploymentPermissionIfRoleExists(connection, transaction);
    }

    private static void SeedDefaultAllInOneIdentity(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using SqliteCommand countCommand = connection.CreateCommand();
        countCommand.Transaction = transaction;
        countCommand.CommandText = "SELECT COUNT(1) FROM deployment_identity;";
        long existingCount = (long)(countCommand.ExecuteScalar() ?? 0L);

        if (existingCount > 0)
        {
            return;
        }

        string now = DateTimeOffset.UtcNow.ToString("O");
        string storeName = ReadCurrentStoreName(connection, transaction) ?? "Salryn Store";
        Guid storeId = Guid.NewGuid();
        Guid hostId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();
        string machineName = Environment.MachineName;

        Execute(connection, transaction, """
            INSERT INTO deployment_identity (
                id,
                store_id,
                store_name,
                host_id,
                installation_role,
                host_display_name,
                is_database_authority,
                is_official_sequence_authority,
                is_journal_authority,
                is_backup_authority,
                is_client_only,
                lan_transport_enabled,
                terminal_id,
                terminal_name,
                terminal_role,
                is_local_terminal,
                terminal_registered_at,
                host_address,
                host_port,
                pairing_credential_label,
                host_fingerprint_placeholder,
                connection_status,
                created_at,
                updated_at)
            VALUES (
                $id,
                $storeId,
                $storeName,
                $hostId,
                $installationRole,
                $hostDisplayName,
                1,
                1,
                1,
                1,
                0,
                0,
                $terminalId,
                $terminalName,
                $terminalRole,
                1,
                $terminalRegisteredAt,
                NULL,
                NULL,
                NULL,
                NULL,
                $connectionStatus,
                $createdAt,
                $updatedAt);
            """,
            new Dictionary<string, object?>
            {
                ["$id"] = DeploymentIdentityRowId,
                ["$storeId"] = storeId.ToString("D"),
                ["$storeName"] = storeName,
                ["$hostId"] = hostId.ToString("D"),
                ["$installationRole"] = 2,
                ["$hostDisplayName"] = machineName,
                ["$terminalId"] = terminalId.ToString("D"),
                ["$terminalName"] = machineName,
                ["$terminalRole"] = 2,
                ["$terminalRegisteredAt"] = now,
                ["$connectionStatus"] = 1,
                ["$createdAt"] = now,
                ["$updatedAt"] = now
            });
    }

    private static void SeedAdminDeploymentPermissionIfRoleExists(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using SqliteCommand existsCommand = connection.CreateCommand();
        existsCommand.Transaction = transaction;
        existsCommand.CommandText = """
            SELECT 1
            FROM roles
            WHERE id = $roleId
            LIMIT 1;
            """;
        existsCommand.Parameters.AddWithValue("$roleId", DefaultSeedData.AdminRoleId);

        if (existsCommand.ExecuteScalar() is null)
        {
            return;
        }

        string now = DateTimeOffset.UtcNow.ToString("O");

        Execute(connection, transaction, """
            INSERT OR IGNORE INTO role_permissions (id, role_id, permission, created_at)
            VALUES ($id, $roleId, $permission, $createdAt);
            """,
            new Dictionary<string, object?>
            {
                ["$id"] = Guid.NewGuid().ToString("D"),
                ["$roleId"] = DefaultSeedData.AdminRoleId,
                ["$permission"] = "StoreDeployment.Manage",
                ["$createdAt"] = now
            });
    }

    private static string? ReadCurrentStoreName(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT store_name
            FROM store_settings
            ORDER BY created_at DESC
            LIMIT 1;
            """;

        object? value = command.ExecuteScalar();
        return value is string storeName && !string.IsNullOrWhiteSpace(storeName)
            ? storeName
            : null;
    }

    private static void Execute(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        if (parameters is not null)
        {
            foreach (KeyValuePair<string, object?> parameter in parameters)
            {
                command.Parameters.AddWithValue(parameter.Key, parameter.Value ?? DBNull.Value);
            }
        }

        command.ExecuteNonQuery();
    }
}
