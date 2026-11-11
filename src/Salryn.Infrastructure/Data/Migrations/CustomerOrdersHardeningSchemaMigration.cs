using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

public sealed class CustomerOrdersHardeningSchemaMigration : IDatabaseMigration
{
    public int Version => 31;
    public string Name => "Customer orders sale link unique index hardening";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            CREATE UNIQUE INDEX IF NOT EXISTS uq_customer_orders_sale_id
            ON customer_orders(sale_id) WHERE sale_id IS NOT NULL;
            """;
        command.ExecuteNonQuery();
    }
}
