using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

public sealed class ReceivableCollectionRequestSchemaMigration : IDatabaseMigration
{
    public int Version => 24;
    public string Name => "Receivable collection durable request reconciliation";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            ALTER TABLE accounts_receivable_collections
                ADD COLUMN client_request_id TEXT NULL;
            ALTER TABLE accounts_receivable_collections
                ADD COLUMN request_hash TEXT NULL;

            CREATE UNIQUE INDEX idx_ar_collections_request
                ON accounts_receivable_collections(collected_by_user_id, client_request_id)
                WHERE client_request_id IS NOT NULL;
            """;
        command.ExecuteNonQuery();
    }
}
