using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

/// <summary>
/// Cafe Yza R4: extends immutable cashier-session reports with ledger reconciliation
/// projection fields. Existing V1 rows are not rewritten or rehashed.
/// </summary>
public sealed class CashierSessionReportV2SchemaMigration : IDatabaseMigration
{
    public int Version => 27;

    public string Name => "Ledger-aware cashier session report V2";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, """
            ALTER TABLE cashier_session_reports
                ADD COLUMN reconciliation_basis TEXT NOT NULL DEFAULT 'PreLedgerCompatibility';

            ALTER TABLE cashier_session_reports
                ADD COLUMN ledger_net_delta_cents INTEGER NULL;

            ALTER TABLE cashier_session_reports
                ADD COLUMN sale_cash_receipt_cents INTEGER NULL;

            ALTER TABLE cashier_session_reports
                ADD COLUMN receivable_cash_collection_cents INTEGER NULL;

            ALTER TABLE cashier_session_reports
                ADD COLUMN cash_in_cents INTEGER NULL;

            ALTER TABLE cashier_session_reports
                ADD COLUMN inventory_purchase_cents INTEGER NULL;

            ALTER TABLE cashier_session_reports
                ADD COLUMN operational_expense_cents INTEGER NULL;

            ALTER TABLE cashier_session_reports
                ADD COLUMN cash_refund_cents INTEGER NULL;

            ALTER TABLE cashier_session_reports
                ADD COLUMN cash_out_cents INTEGER NULL;

            ALTER TABLE cashier_session_reports
                ADD COLUMN reversal_cents INTEGER NULL;
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
