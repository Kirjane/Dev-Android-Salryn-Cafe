using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

/// <summary>
/// Cafe Yza R2: immutable per-cashier-session close report.
/// The report is a final close-time snapshot. Corrections must be recorded
/// as separate operational/audit events; the snapshot itself cannot be edited.
/// </summary>
public sealed class CashierSessionReportSchemaMigration : IDatabaseMigration
{
    public int Version => 25;

    public string Name => "Immutable cashier session sales reports";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, """
            CREATE TABLE cashier_session_reports (
                session_id TEXT PRIMARY KEY,
                cashier_user_id TEXT NOT NULL,
                opened_at_utc TEXT NOT NULL,
                closed_at_utc TEXT NOT NULL,
                opening_cash_cents INTEGER NOT NULL,
                gross_sales_cents INTEGER NOT NULL,
                discount_total_cents INTEGER NOT NULL,
                net_sales_cents INTEGER NOT NULL,
                cash_sales_cents INTEGER NOT NULL,
                noncash_sales_cents INTEGER NOT NULL,
                transaction_count INTEGER NOT NULL,
                expected_closing_cash_cents INTEGER NOT NULL,
                declared_closing_cash_cents INTEGER NULL,
                variance_cents INTEGER NULL,
                close_status INTEGER NOT NULL,
                close_notes TEXT NULL,
                snapshot_version INTEGER NOT NULL DEFAULT 1,
                snapshot_hash TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                FOREIGN KEY (session_id) REFERENCES cashier_sessions(id) ON DELETE RESTRICT,
                FOREIGN KEY (cashier_user_id) REFERENCES users(id) ON DELETE RESTRICT
            );
            """);

        Execute(connection, transaction, """
            CREATE INDEX idx_cashier_session_reports_cashier_closed
            ON cashier_session_reports(cashier_user_id, closed_at_utc DESC);
            """);

        // Database-level immutability. Application code has no legitimate UPDATE/DELETE path.
        Execute(connection, transaction, """
            CREATE TRIGGER trg_cashier_session_reports_no_update
            BEFORE UPDATE ON cashier_session_reports
            BEGIN
                SELECT RAISE(ABORT, 'cashier session reports are immutable');
            END;
            """);

        Execute(connection, transaction, """
            CREATE TRIGGER trg_cashier_session_reports_no_delete
            BEFORE DELETE ON cashier_session_reports
            BEGIN
                SELECT RAISE(ABORT, 'cashier session reports are immutable');
            END;
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
