using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

public sealed class RemotePosCheckoutRequestSchemaMigration : IDatabaseMigration
{
    public int Version => 21;
    public string Name => "Remote POS durable checkout requests";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            CREATE TABLE remote_pos_checkout_requests (
                store_id TEXT NOT NULL,
                terminal_id TEXT NOT NULL,
                client_request_id TEXT NOT NULL,
                request_hash TEXT NOT NULL,
                state TEXT NOT NULL,
                receipt_no TEXT NOT NULL,
                sale_id TEXT NULL,
                total_cents INTEGER NULL,
                change_cents INTEGER NULL,
                message TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                PRIMARY KEY (store_id, terminal_id, client_request_id),
                UNIQUE (receipt_no)
            );

            CREATE INDEX idx_remote_pos_checkout_requests_state
                ON remote_pos_checkout_requests(state, updated_at_utc);
            """;
        command.ExecuteNonQuery();
    }
}
