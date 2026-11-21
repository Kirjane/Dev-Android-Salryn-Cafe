using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

/// <summary>Additive actor binding; historical unbound requests must never be attributed by inference.</summary>
public sealed class CafeRemoteCheckoutOperatorBindingMigration : IDatabaseMigration
{
    public int Version => 42;
    public string Name => "Cafe immutable remote checkout operator binding";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            ALTER TABLE remote_pos_checkout_requests ADD COLUMN operator_user_id TEXT NULL;
            CREATE TRIGGER remote_checkout_operator_binding_immutable
            BEFORE UPDATE OF operator_user_id ON remote_pos_checkout_requests
            WHEN OLD.operator_user_id IS NOT NEW.operator_user_id
            BEGIN
                SELECT RAISE(ABORT, 'Remote checkout operator binding is immutable.');
            END;
            """;
        command.ExecuteNonQuery();
    }
}
