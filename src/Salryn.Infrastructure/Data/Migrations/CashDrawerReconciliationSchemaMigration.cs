using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

public sealed class CashDrawerReconciliationSchemaMigration : IDatabaseMigration
{
    public int Version => 25;
    public string Name => "W4 cash drawer accountability candidate";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        using SqliteCommand command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="""
            ALTER TABLE cashier_sessions ADD COLUMN cash_coverage_started_at TEXT NULL;
            ALTER TABLE accounts_receivable_collections ADD COLUMN cashier_session_id TEXT NULL REFERENCES cashier_sessions(id);

            CREATE TABLE cash_drawer_events(
              id TEXT PRIMARY KEY, cashier_session_id TEXT NOT NULL, event_type TEXT NOT NULL,
              direction INTEGER NOT NULL CHECK(direction IN(-1,1)), amount_cents INTEGER NOT NULL CHECK(amount_cents>0),
              source_type TEXT NOT NULL, source_id TEXT NOT NULL, actor_user_id TEXT NOT NULL,
              method TEXT NOT NULL, occurred_at TEXT NOT NULL, request_id TEXT NOT NULL, note TEXT NULL,
              FOREIGN KEY(cashier_session_id) REFERENCES cashier_sessions(id), FOREIGN KEY(actor_user_id) REFERENCES users(id),
              UNIQUE(source_type,source_id,event_type), UNIQUE(cashier_session_id,request_id));
            CREATE INDEX idx_cash_drawer_events_session_time ON cash_drawer_events(cashier_session_id,occurred_at,id);

            CREATE TABLE cash_drawer_reconciliations(
              cashier_session_id TEXT PRIMARY KEY, expected_cash_cents INTEGER NULL, counted_cash_cents INTEGER NOT NULL CHECK(counted_cash_cents>=0),
              variance_cents INTEGER NULL, coverage_status TEXT NOT NULL, review_status TEXT NOT NULL,
              close_request_id TEXT NOT NULL UNIQUE, submitted_by_user_id TEXT NOT NULL, submitted_at TEXT NOT NULL,
              confirmation_text TEXT NOT NULL,
              FOREIGN KEY(cashier_session_id) REFERENCES cashier_sessions(id), FOREIGN KEY(submitted_by_user_id) REFERENCES users(id));

            CREATE TABLE cash_drawer_reviews(
              id TEXT PRIMARY KEY, cashier_session_id TEXT NOT NULL, reviewer_user_id TEXT NOT NULL,
              decision TEXT NOT NULL, reason TEXT NOT NULL, request_id TEXT NOT NULL UNIQUE, created_at TEXT NOT NULL,
              FOREIGN KEY(cashier_session_id) REFERENCES cashier_sessions(id), FOREIGN KEY(reviewer_user_id) REFERENCES users(id));
            CREATE INDEX idx_cash_drawer_reviews_session ON cash_drawer_reviews(cashier_session_id,created_at,id);
            """;command.ExecuteNonQuery();
    }
}
