using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

public sealed class ProductTaxClassificationSchemaMigration : IDatabaseMigration
{
    public int Version => 8;

    public string Name => "Product tax classification schema";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, """
            ALTER TABLE products
            ADD COLUMN tax_classification INTEGER NOT NULL DEFAULT 1;
            """);

        Execute(connection, transaction, """
            UPDATE products
            SET tax_classification = 1
            WHERE tax_classification IS NULL OR tax_classification NOT IN (1, 2, 3, 4);
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
