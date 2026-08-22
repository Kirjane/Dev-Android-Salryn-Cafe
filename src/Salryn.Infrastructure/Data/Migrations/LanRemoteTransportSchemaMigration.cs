using Microsoft.Data.Sqlite;
using Salryn.Core.Host;

namespace Salryn.Infrastructure.Data.Migrations;

public sealed class LanRemoteTransportSchemaMigration : IDatabaseMigration
{
    public int Version => 11;

    public string Name => "LAN remote transport gate configuration schema";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, """
            CREATE TABLE IF NOT EXISTS salryn_lan_transport_config (
                id TEXT PRIMARY KEY,
                remote_transport_enabled INTEGER NOT NULL,
                bind_mode INTEGER NOT NULL,
                bind_address TEXT NOT NULL,
                port INTEGER NOT NULL,
                require_paired_terminal INTEGER NOT NULL,
                require_active_terminal INTEGER NOT NULL,
                reject_unknown_terminals INTEGER NOT NULL,
                allow_official_commands_remotely INTEGER NOT NULL,
                allow_admin_commands_remotely INTEGER NOT NULL,
                updated_at_utc TEXT NOT NULL,
                updated_by_user_id TEXT NULL
            );
            """);

        LanRemoteTransportConfig defaults = LanRemoteTransportConfig.CreateDefault();

        Execute(connection, transaction, """
            INSERT OR IGNORE INTO salryn_lan_transport_config (
                id,
                remote_transport_enabled,
                bind_mode,
                bind_address,
                port,
                require_paired_terminal,
                require_active_terminal,
                reject_unknown_terminals,
                allow_official_commands_remotely,
                allow_admin_commands_remotely,
                updated_at_utc,
                updated_by_user_id)
            VALUES (
                $id,
                $remoteTransportEnabled,
                $bindMode,
                $bindAddress,
                $port,
                $requirePairedTerminal,
                $requireActiveTerminal,
                $rejectUnknownTerminals,
                $allowOfficialCommandsRemotely,
                $allowAdminCommandsRemotely,
                $updatedAtUtc,
                NULL);
            """,
            new Dictionary<string, object?>
            {
                ["$id"] = "default",
                ["$remoteTransportEnabled"] = defaults.RemoteTransportEnabled ? 1 : 0,
                ["$bindMode"] = (int)defaults.BindMode,
                ["$bindAddress"] = defaults.BindAddress,
                ["$port"] = defaults.Port,
                ["$requirePairedTerminal"] = defaults.RequirePairedTerminal ? 1 : 0,
                ["$requireActiveTerminal"] = defaults.RequireActiveTerminal ? 1 : 0,
                ["$rejectUnknownTerminals"] = defaults.RejectUnknownTerminals ? 1 : 0,
                ["$allowOfficialCommandsRemotely"] = defaults.AllowOfficialCommandsRemotely ? 1 : 0,
                ["$allowAdminCommandsRemotely"] = defaults.AllowAdminCommandsRemotely ? 1 : 0,
                ["$updatedAtUtc"] = defaults.UpdatedAtUtc.ToString("O")
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
