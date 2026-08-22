using Microsoft.Data.Sqlite;
using Salryn.Infrastructure.Data.Seeding;

namespace Salryn.Infrastructure.Data.Migrations;

public sealed class TerminalRegistrySchemaMigration : IDatabaseMigration
{
    public int Version => 10;

    public string Name => "Terminal registry and pairing preparation schema";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, """
            CREATE TABLE salryn_terminal_registrations (
                terminal_id TEXT PRIMARY KEY,
                store_id TEXT NOT NULL,
                host_id TEXT NOT NULL,
                terminal_name TEXT NOT NULL,
                terminal_role INTEGER NOT NULL,
                terminal_status INTEGER NOT NULL,
                registered_at_utc TEXT NOT NULL,
                last_seen_at_utc TEXT NULL,
                disabled_at_utc TEXT NULL,
                revoked_at_utc TEXT NULL,
                created_by_user_id TEXT NOT NULL,
                notes TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """);

        Execute(connection, transaction, """
            CREATE TABLE salryn_terminal_pairing_invites (
                pairing_id TEXT PRIMARY KEY,
                store_id TEXT NOT NULL,
                host_id TEXT NOT NULL,
                target_terminal_name TEXT NOT NULL,
                target_terminal_role INTEGER NOT NULL,
                pairing_status INTEGER NOT NULL,
                pairing_code_hash TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                expires_at_utc TEXT NOT NULL,
                consumed_at_utc TEXT NULL,
                revoked_at_utc TEXT NULL,
                created_by_user_id TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """);

        SeedLocalTerminalFromDeploymentIdentity(connection, transaction);
        SeedTerminalPermissionsIfRolesExist(connection, transaction);
    }

    private static void SeedLocalTerminalFromDeploymentIdentity(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using SqliteCommand readCommand = connection.CreateCommand();
        readCommand.Transaction = transaction;
        readCommand.CommandText = """
            SELECT
                store_id,
                host_id,
                terminal_id,
                terminal_name,
                terminal_role,
                terminal_registered_at
            FROM deployment_identity
            WHERE id = $id
            LIMIT 1;
            """;
        readCommand.Parameters.AddWithValue("$id", DeploymentIdentitySchemaMigration.DeploymentIdentityRowId);

        using SqliteDataReader reader = readCommand.ExecuteReader();
        if (!reader.Read())
        {
            return;
        }

        string terminalId = reader.GetString(2);

        using SqliteCommand existsCommand = connection.CreateCommand();
        existsCommand.Transaction = transaction;
        existsCommand.CommandText = """
            SELECT 1
            FROM salryn_terminal_registrations
            WHERE terminal_id = $terminalId
            LIMIT 1;
            """;
        existsCommand.Parameters.AddWithValue("$terminalId", terminalId);

        if (existsCommand.ExecuteScalar() is not null)
        {
            return;
        }

        string now = DateTimeOffset.UtcNow.ToString("O");
        string registeredAt = reader.GetString(5);

        Execute(connection, transaction, """
            INSERT INTO salryn_terminal_registrations (
                terminal_id,
                store_id,
                host_id,
                terminal_name,
                terminal_role,
                terminal_status,
                registered_at_utc,
                last_seen_at_utc,
                disabled_at_utc,
                revoked_at_utc,
                created_by_user_id,
                notes,
                created_at,
                updated_at)
            VALUES (
                $terminalId,
                $storeId,
                $hostId,
                $terminalName,
                $terminalRole,
                $terminalStatus,
                $registeredAtUtc,
                $lastSeenAtUtc,
                NULL,
                NULL,
                $createdByUserId,
                $notes,
                $createdAt,
                $updatedAt);
            """,
            new Dictionary<string, object?>
            {
                ["$terminalId"] = terminalId,
                ["$storeId"] = reader.GetString(0),
                ["$hostId"] = reader.GetString(1),
                ["$terminalName"] = reader.GetString(3),
                ["$terminalRole"] = reader.GetInt32(4),
                ["$terminalStatus"] = 2,
                ["$registeredAtUtc"] = registeredAt,
                ["$lastSeenAtUtc"] = registeredAt,
                ["$createdByUserId"] = "00000000-0000-0000-0000-000000000001",
                ["$notes"] = "Seeded from deployment identity during LAN-M4 migration.",
                ["$createdAt"] = now,
                ["$updatedAt"] = now
            });
    }

    private static void SeedTerminalPermissionsIfRolesExist(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        SeedRolePermission(connection, transaction, DefaultSeedData.AdminRoleId, "StoreTerminal.View");
        SeedRolePermission(connection, transaction, DefaultSeedData.AdminRoleId, "StoreTerminal.Manage");
        SeedRolePermission(connection, transaction, DefaultSeedData.AdminRoleId, "StoreTerminal.PairingIssue");
        SeedRolePermission(connection, transaction, DefaultSeedData.AdminRoleId, "StoreTerminal.PairingRevoke");
        SeedRolePermission(connection, transaction, DefaultSeedData.ManagerRoleId, "StoreTerminal.View");
    }

    private static void SeedRolePermission(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string roleId,
        string permission)
    {
        using SqliteCommand existsCommand = connection.CreateCommand();
        existsCommand.Transaction = transaction;
        existsCommand.CommandText = """
            SELECT 1
            FROM roles
            WHERE id = $roleId
            LIMIT 1;
            """;
        existsCommand.Parameters.AddWithValue("$roleId", roleId);

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
                ["$roleId"] = roleId,
                ["$permission"] = permission,
                ["$createdAt"] = now
            });
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
