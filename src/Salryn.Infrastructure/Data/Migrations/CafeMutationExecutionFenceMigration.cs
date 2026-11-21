using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

/// <summary>Coordination fence, not a financial ledger or evidence of business application.</summary>
public sealed class CafeMutationExecutionFenceMigration : IDatabaseMigration
{
    public int Version => 43;
    public string Name => "Cafe durable mutation execution fence";
    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE cafe_mutation_execution_fences (
                operation_id TEXT PRIMARY KEY,
                endpoint_key TEXT NOT NULL,
                actor_user_id TEXT NOT NULL,
                request_hash TEXT NOT NULL,
                created_at_utc TEXT NOT NULL
            );
            CREATE TRIGGER cafe_mutation_execution_fences_no_update
            BEFORE UPDATE ON cafe_mutation_execution_fences
            BEGIN
                SELECT RAISE(ABORT, 'Mutation execution identity is immutable.');
            END;
            """;
        command.ExecuteNonQuery();
    }
}
