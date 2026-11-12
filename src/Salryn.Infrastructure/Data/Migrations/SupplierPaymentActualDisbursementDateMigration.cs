using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

/// <summary>
/// Issue #71: keep the requested/recorded payment_date and confirmed approved_at
/// separate from the actual date funds moved. Legacy settled rows remain NULL:
/// no bank transfer date can be inferred from historical approval or request dates.
/// </summary>
public sealed class SupplierPaymentActualDisbursementDateMigration : IDatabaseMigration
{
    public int Version => 32;
    public string Name => "Supplier payment actual disbursement date custody";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        using SqliteCommand check = connection.CreateCommand();
        check.Transaction = transaction;
        check.CommandText = "PRAGMA table_info(supplier_payments);";
        using (SqliteDataReader reader = check.ExecuteReader())
        {
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), "actual_disbursement_date", StringComparison.OrdinalIgnoreCase))
                    return;
            }
        }

        using SqliteCommand alter = connection.CreateCommand();
        alter.Transaction = transaction;
        alter.CommandText = "ALTER TABLE supplier_payments ADD COLUMN actual_disbursement_date TEXT NULL;";
        alter.ExecuteNonQuery();
        // Deliberately no UPDATE: historical settled dates are unknown, not reconstructed.
    }
}
