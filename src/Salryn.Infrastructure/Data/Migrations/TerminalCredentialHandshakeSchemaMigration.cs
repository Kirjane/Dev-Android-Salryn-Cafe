using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

public sealed class TerminalCredentialHandshakeSchemaMigration : IDatabaseMigration
{
    public int Version => 12;

    public string Name => "Terminal credential and remote handshake schema";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, """
            CREATE TABLE salryn_terminal_credentials (
                terminal_id TEXT PRIMARY KEY,
                store_id TEXT NOT NULL,
                host_id TEXT NOT NULL,
                credential_hash TEXT NOT NULL,
                issued_at_utc TEXT NOT NULL,
                last_used_at_utc TEXT NULL,
                revoked_at_utc TEXT NULL,
                created_from_pairing_id TEXT NULL,
                created_by_user_id TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """);

        Execute(connection, transaction, """
            CREATE UNIQUE INDEX ix_salryn_terminal_credentials_hash
            ON salryn_terminal_credentials(credential_hash);
            """);

        Execute(connection, transaction, """
            CREATE TABLE salryn_remote_client_handshakes (
                handshake_id TEXT PRIMARY KEY,
                terminal_id TEXT NOT NULL,
                store_id TEXT NOT NULL,
                host_id TEXT NOT NULL,
                origin_address TEXT NOT NULL,
                client_machine_name TEXT NULL,
                client_fingerprint TEXT NULL,
                created_at_utc TEXT NOT NULL,
                expires_at_utc TEXT NOT NULL,
                handshake_status INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """);

        Execute(connection, transaction, """
            CREATE INDEX ix_salryn_remote_client_handshakes_terminal_status
            ON salryn_remote_client_handshakes(terminal_id, handshake_status, expires_at_utc);
            """);
    }

    private static void Execute(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
