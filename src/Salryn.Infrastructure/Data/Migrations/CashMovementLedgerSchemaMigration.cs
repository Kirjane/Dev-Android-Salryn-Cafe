using Microsoft.Data.Sqlite;
using Salryn.Core.Models;

namespace Salryn.Infrastructure.Data.Migrations;

public sealed class CashMovementLedgerSchemaMigration : IDatabaseMigration
{
    public int Version => 26;
    public string Name => "Cafe cash movement ledger foundation";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        EnsureLedgerActivationCanProceed(connection, transaction);

        Execute(connection, transaction, """
            CREATE TABLE cash_movements (
                id TEXT PRIMARY KEY,
                cashier_session_id TEXT NOT NULL,
                actor_user_id TEXT NOT NULL,
                movement_type INTEGER NOT NULL CHECK (movement_type BETWEEN 1 AND 8),
                delta_cents INTEGER NOT NULL CHECK (delta_cents <> 0),
                reference_type TEXT NOT NULL,
                reference_id TEXT NOT NULL,
                reversal_of_movement_id TEXT NULL,
                reason TEXT NULL,
                created_at_utc TEXT NOT NULL,
                FOREIGN KEY (cashier_session_id) REFERENCES cashier_sessions(id) ON DELETE RESTRICT,
                FOREIGN KEY (actor_user_id) REFERENCES users(id) ON DELETE RESTRICT,
                FOREIGN KEY (reversal_of_movement_id) REFERENCES cash_movements(id) ON DELETE RESTRICT
            );
            CREATE INDEX idx_cash_movements_session_created
                ON cash_movements(cashier_session_id, created_at_utc, id);
            CREATE INDEX idx_cash_movements_reference
                ON cash_movements(reference_type, reference_id);
            CREATE UNIQUE INDEX idx_cashier_sessions_one_open_per_user
                ON cashier_sessions(user_id)
                WHERE status = 1;
            """);

        Execute(connection, transaction, $"""
            CREATE UNIQUE INDEX idx_cash_movements_sale_cash_receipt
                ON cash_movements(reference_id)
                WHERE movement_type = {Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.SaleCashReceipt)};
            CREATE UNIQUE INDEX idx_cash_movements_receivable_cash_collection
                ON cash_movements(reference_id)
                WHERE movement_type = {Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.ReceivableCashCollection)};
            CREATE UNIQUE INDEX idx_cash_movements_single_reversal
                ON cash_movements(reversal_of_movement_id)
                WHERE reversal_of_movement_id IS NOT NULL;
            """);

        Execute(connection, transaction, """
            CREATE TRIGGER trg_cash_movements_no_update
            BEFORE UPDATE ON cash_movements
            BEGIN
                SELECT RAISE(ABORT, 'cash movements are append-only');
            END;

            CREATE TRIGGER trg_cash_movements_no_delete
            BEFORE DELETE ON cash_movements
            BEGIN
                SELECT RAISE(ABORT, 'cash movements are append-only');
            END;
            """);

        Execute(connection, transaction, $"""
            CREATE TRIGGER trg_cash_movements_sale_receipt_shape
            BEFORE INSERT ON cash_movements
            WHEN NEW.movement_type = {Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.SaleCashReceipt)}
            BEGIN
                SELECT CASE WHEN UPPER(TRIM(NEW.reference_type)) <> 'SALE'
                    THEN RAISE(ABORT, 'sale cash movement reference type must be SALE') END;
                SELECT CASE WHEN NEW.delta_cents <= 0
                    THEN RAISE(ABORT, 'sale cash receipt delta must be positive') END;
                SELECT CASE WHEN NOT EXISTS (
                    SELECT 1 FROM sales s
                    WHERE s.id = NEW.reference_id
                      AND s.status = {Convert.ToInt32(SaleStatus.Completed)}
                      AND s.cashier_session_id = NEW.cashier_session_id
                      AND s.cashier_user_id = NEW.actor_user_id
                ) THEN RAISE(ABORT, 'sale cash movement must match completed source sale cashier session and actor') END;
                SELECT CASE WHEN (
                    SELECT COALESCE(SUM(p.amount_cents), 0)
                    FROM payments p
                    WHERE p.sale_id = NEW.reference_id
                ) <> (
                    SELECT s.amount_paid_cents
                    FROM sales s
                    WHERE s.id = NEW.reference_id
                ) THEN RAISE(ABORT, 'sale cash receipt requires complete persisted payment set') END;
                SELECT CASE WHEN NEW.delta_cents <> (
                    SELECT COALESCE(SUM(
                        CASE WHEN p.method = {Convert.ToInt32(PaymentMethod.Cash)}
                             THEN p.amount_cents ELSE 0 END), 0)
                    FROM payments p
                    WHERE p.sale_id = NEW.reference_id
                ) - (
                    SELECT s.change_cents
                    FROM sales s
                    WHERE s.id = NEW.reference_id
                ) THEN RAISE(ABORT, 'sale cash receipt delta must equal persisted retained cash') END;
            END;

            CREATE TRIGGER trg_cash_movements_receivable_collection_shape
            BEFORE INSERT ON cash_movements
            WHEN NEW.movement_type = {Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.ReceivableCashCollection)}
            BEGIN
                SELECT CASE WHEN UPPER(TRIM(NEW.reference_type)) <> 'AR_COLLECTION'
                    THEN RAISE(ABORT, 'receivable cash movement reference type must be AR_COLLECTION') END;
                SELECT CASE WHEN NEW.delta_cents <= 0
                    THEN RAISE(ABORT, 'receivable cash collection delta must be positive') END;
                SELECT CASE WHEN NOT EXISTS (
                    SELECT 1
                    FROM accounts_receivable_collections c
                    JOIN cashier_sessions cs ON cs.id = NEW.cashier_session_id
                    WHERE c.id = NEW.reference_id
                      AND (
                          COALESCE(TRIM(c.method), '') = ''
                          OR UPPER(TRIM(COALESCE(c.method, ''))) = 'CASH'
                      )
                      AND c.voided_at IS NULL
                      AND c.collected_by_user_id = NEW.actor_user_id
                      AND cs.user_id = NEW.actor_user_id
                ) THEN RAISE(ABORT, 'receivable cash movement must match cash collection collector and session') END;
                SELECT CASE WHEN NEW.delta_cents <> (
                    SELECT c.amount_cents
                    FROM accounts_receivable_collections c
                    WHERE c.id = NEW.reference_id
                ) THEN RAISE(ABORT, 'receivable cash movement delta must equal source collection amount') END;
            END;

            CREATE TRIGGER trg_cash_movements_reversal_shape
            BEFORE INSERT ON cash_movements
            WHEN NEW.movement_type = {Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.Reversal)}
            BEGIN
                SELECT CASE WHEN NEW.reversal_of_movement_id IS NULL
                    THEN RAISE(ABORT, 'cash reversal requires original movement') END;
                SELECT CASE WHEN NOT EXISTS (
                    SELECT 1 FROM cash_movements original
                    WHERE original.id = NEW.reversal_of_movement_id
                      AND original.movement_type <> {Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.Reversal)}
                      AND original.cashier_session_id = NEW.cashier_session_id
                      AND NEW.delta_cents = -original.delta_cents
                ) THEN RAISE(ABORT, 'cash reversal must exactly compensate original movement in same session') END;
            END;

            CREATE TRIGGER trg_cash_movements_nonreversal_has_no_source
            BEFORE INSERT ON cash_movements
            WHEN NEW.movement_type <> {Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.Reversal)}
             AND NEW.reversal_of_movement_id IS NOT NULL
            BEGIN
                SELECT RAISE(ABORT, 'only reversal cash movements may reference an original movement');
            END;

            CREATE TRIGGER trg_cash_movements_explicit_sign_shape
            BEFORE INSERT ON cash_movements
            BEGIN
                SELECT CASE
                    WHEN NEW.movement_type = {Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.CashIn)}
                     AND NEW.delta_cents <= 0
                    THEN RAISE(ABORT, 'cash-in movement delta must be positive')
                END;
                SELECT CASE
                    WHEN NEW.movement_type IN (
                        {Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.InventoryPurchase)},
                        {Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.OperationalExpense)},
                        {Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.CashRefund)},
                        {Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.CashOut)})
                     AND NEW.delta_cents >= 0
                    THEN RAISE(ABORT, 'cash-out movement delta must be negative')
                END;
            END;
            """);

        EnsureHistoricalCashCanBeBackfilled(connection, transaction);

        Execute(connection, transaction, $"""
            INSERT INTO cash_movements (
                id, cashier_session_id, actor_user_id, movement_type, delta_cents,
                reference_type, reference_id, reversal_of_movement_id, reason, created_at_utc)
            SELECT
                s.id, s.cashier_session_id, s.cashier_user_id,
                {Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.SaleCashReceipt)},
                SUM(CASE WHEN p.method = {Convert.ToInt32(PaymentMethod.Cash)} THEN p.amount_cents ELSE 0 END) - s.change_cents,
                'SALE', s.id, NULL, 'Backfilled retained cash from completed sale', s.created_at
            FROM sales s
            INNER JOIN payments p ON p.sale_id = s.id
            WHERE s.status = {Convert.ToInt32(SaleStatus.Completed)}
              AND s.cashier_session_id IS NOT NULL
            GROUP BY s.id, s.cashier_session_id, s.cashier_user_id, s.amount_paid_cents, s.change_cents, s.created_at
            HAVING SUM(p.amount_cents) = s.amount_paid_cents
               AND SUM(CASE WHEN p.method = {Convert.ToInt32(PaymentMethod.Cash)} THEN p.amount_cents ELSE 0 END) - s.change_cents > 0;
            """);

        // Created after historical backfill: all later physical cash rows require an open drawer.
        Execute(connection, transaction, $"""
            CREATE TRIGGER trg_cash_movements_live_open_session
            BEFORE INSERT ON cash_movements
            BEGIN
                SELECT CASE WHEN NOT EXISTS (
                    SELECT 1 FROM cashier_sessions cs
                    WHERE cs.id = NEW.cashier_session_id
                      AND cs.status = {Convert.ToInt32(CashierSessionStatus.Open)}
                ) THEN RAISE(ABORT, 'live cash movement requires an open cashier session') END;
            END;
            """);

        // Cash AR collections are created inside the existing immediate AR transaction.
        // DB classification intentionally matches application semantics:
        // NULL/blank/whitespace and case-insensitive Cash are physical Cash.
        Execute(connection, transaction, $"""
            CREATE TRIGGER trg_receivable_cash_collection_requires_open_session
            BEFORE INSERT ON accounts_receivable_collections
            WHEN COALESCE(TRIM(NEW.method), '') = ''
              OR UPPER(TRIM(COALESCE(NEW.method, ''))) = 'CASH'
            BEGIN
                SELECT CASE WHEN NOT EXISTS (
                    SELECT 1 FROM cashier_sessions cs
                    WHERE cs.user_id = NEW.collected_by_user_id
                      AND cs.status = {Convert.ToInt32(CashierSessionStatus.Open)}
                ) THEN RAISE(ABORT, 'cash receivable collection requires collector open cashier session') END;
            END;

            CREATE TRIGGER trg_receivable_cash_collection_posts_movement
            AFTER INSERT ON accounts_receivable_collections
            WHEN COALESCE(TRIM(NEW.method), '') = ''
              OR UPPER(TRIM(COALESCE(NEW.method, ''))) = 'CASH'
            BEGIN
                INSERT INTO cash_movements (
                    id, cashier_session_id, actor_user_id, movement_type, delta_cents,
                    reference_type, reference_id, reversal_of_movement_id, reason, created_at_utc)
                SELECT
                    lower(hex(randomblob(16))), cs.id, NEW.collected_by_user_id,
                    {Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.ReceivableCashCollection)}, NEW.amount_cents,
                    'AR_COLLECTION', NEW.id, NULL, 'Accounts receivable cash collection', NEW.collected_at
                FROM cashier_sessions cs
                WHERE cs.user_id = NEW.collected_by_user_id
                  AND cs.status = {Convert.ToInt32(CashierSessionStatus.Open)};
            END;
            """);

        // A session-bound sale must still be open and owned by the cashier at the actual INSERT.
        Execute(connection, transaction, $"""
            CREATE TRIGGER trg_sales_session_must_be_open_on_insert
            BEFORE INSERT ON sales
            WHEN NEW.cashier_session_id IS NOT NULL
            BEGIN
                SELECT CASE WHEN NOT EXISTS (
                    SELECT 1 FROM cashier_sessions cs
                    WHERE cs.id = NEW.cashier_session_id
                      AND cs.user_id = NEW.cashier_user_id
                      AND cs.status = {Convert.ToInt32(CashierSessionStatus.Open)}
                ) THEN RAISE(ABORT, 'session-bound sale requires matching open cashier session') END;
            END;
            """);

        // The final positive payment row is the stable point where all persisted tender is known.
        Execute(connection, transaction, $"""
            CREATE TRIGGER trg_payments_complete_sale_posts_cash_movement
            AFTER INSERT ON payments
            BEGIN
                SELECT CASE WHEN EXISTS (
                    SELECT 1 FROM sales s
                    WHERE s.id = NEW.sale_id
                      AND s.cashier_session_id IS NOT NULL
                      AND (SELECT COALESCE(SUM(p.amount_cents),0) FROM payments p WHERE p.sale_id=s.id) = s.amount_paid_cents
                      AND (SELECT COALESCE(SUM(CASE WHEN p.method={Convert.ToInt32(PaymentMethod.Cash)} THEN p.amount_cents ELSE 0 END),0)
                           FROM payments p WHERE p.sale_id=s.id) - s.change_cents < 0
                ) THEN RAISE(ABORT, 'sale change cannot exceed cash tender') END;

                INSERT INTO cash_movements (
                    id, cashier_session_id, actor_user_id, movement_type, delta_cents,
                    reference_type, reference_id, reversal_of_movement_id, reason, created_at_utc)
                SELECT
                    lower(hex(randomblob(16))), s.cashier_session_id, s.cashier_user_id,
                    {Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.SaleCashReceipt)},
                    (SELECT COALESCE(SUM(CASE WHEN p.method={Convert.ToInt32(PaymentMethod.Cash)} THEN p.amount_cents ELSE 0 END),0)
                     FROM payments p WHERE p.sale_id=s.id) - s.change_cents,
                    'SALE', s.id, NULL, 'POS checkout retained cash', s.created_at
                FROM sales s
                WHERE s.id = NEW.sale_id
                  AND s.status = {Convert.ToInt32(SaleStatus.Completed)}
                  AND s.cashier_session_id IS NOT NULL
                  AND (SELECT COALESCE(SUM(p.amount_cents),0) FROM payments p WHERE p.sale_id=s.id) = s.amount_paid_cents
                  AND (SELECT COALESCE(SUM(CASE WHEN p.method={Convert.ToInt32(PaymentMethod.Cash)} THEN p.amount_cents ELSE 0 END),0)
                       FROM payments p WHERE p.sale_id=s.id) - s.change_cents > 0;
            END;
            """);
    }

    private static void EnsureLedgerActivationCanProceed(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using (SqliteCommand duplicates = connection.CreateCommand())
        {
            duplicates.Transaction = transaction;
            duplicates.CommandText = $"""
                SELECT COUNT(*) FROM (
                    SELECT user_id
                    FROM cashier_sessions
                    WHERE status = {Convert.ToInt32(CashierSessionStatus.Open)}
                    GROUP BY user_id
                    HAVING COUNT(*) > 1
                );
                """;
            long duplicateOpenUsers = Convert.ToInt64(duplicates.ExecuteScalar() ?? 0L);
            if (duplicateOpenUsers > 0)
            {
                throw new InvalidOperationException(
                    "Cash movement migration stopped because duplicate open cashier sessions exist for one or more users.");
            }
        }

        using SqliteCommand openSessions = connection.CreateCommand();
        openSessions.Transaction = transaction;
        openSessions.CommandText = $"""
            SELECT COUNT(*)
            FROM cashier_sessions
            WHERE status = {Convert.ToInt32(CashierSessionStatus.Open)};
            """;
        long openCount = Convert.ToInt64(openSessions.ExecuteScalar() ?? 0L);
        if (openCount > 0)
        {
            throw new InvalidOperationException(
                "Cash movement migration stopped because all cashier sessions must be closed before ledger activation.");
        }
    }

    private static void EnsureHistoricalCashCanBeBackfilled(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT COUNT(*) FROM (
                SELECT s.id
                FROM sales s
                INNER JOIN payments p ON p.sale_id = s.id
                WHERE s.status = {Convert.ToInt32(SaleStatus.Completed)}
                  AND s.cashier_session_id IS NOT NULL
                GROUP BY s.id, s.change_cents
                HAVING SUM(CASE WHEN p.method={Convert.ToInt32(PaymentMethod.Cash)} THEN p.amount_cents ELSE 0 END) - s.change_cents < 0
            );
            """;
        long invalidCount = Convert.ToInt64(command.ExecuteScalar() ?? 0L);
        if (invalidCount > 0)
            throw new InvalidOperationException(
                "Cash movement migration stopped because historical completed sales contain change greater than cash tender.");
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
