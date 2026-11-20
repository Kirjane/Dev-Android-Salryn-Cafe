using Microsoft.Data.Sqlite;
using Salryn.Core.Models;

namespace Salryn.Infrastructure.Data.Migrations;

public sealed class CafeCashOperationsSchemaMigration : IDatabaseMigration
{
    public int Version => 35;

    public string Name => "Cafe cash and expense operation authority";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, $"""
            CREATE TABLE cafe_cash_operations (
                operation_id TEXT PRIMARY KEY,
                movement_id TEXT NOT NULL UNIQUE,
                device_id TEXT NOT NULL,
                store_id TEXT NOT NULL,
                actor_user_id TEXT NOT NULL,
                cashier_session_id TEXT NOT NULL,
                movement_type INTEGER NOT NULL,
                requested_amount_cents INTEGER NOT NULL,
                delta_cents INTEGER NOT NULL,
                reference_type TEXT NOT NULL,
                reference_id TEXT NOT NULL,
                reversal_of_movement_id TEXT NULL,
                reason TEXT NOT NULL,
                payload_hash TEXT NOT NULL,
                occurred_at_utc TEXT NOT NULL,
                FOREIGN KEY(actor_user_id) REFERENCES users(id),
                FOREIGN KEY(cashier_session_id) REFERENCES cashier_sessions(id),
                FOREIGN KEY(movement_id) REFERENCES cash_movements(id)
                    DEFERRABLE INITIALLY DEFERRED,
                FOREIGN KEY(reversal_of_movement_id) REFERENCES cash_movements(id),
                CHECK(requested_amount_cents > 0),
                CHECK(delta_cents <> 0),
                CHECK(LENGTH(TRIM(reason)) > 0),
                CHECK(LENGTH(payload_hash) = 64),
                CHECK(movement_type IN (
                    {Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.CashIn)},
                    {Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.InventoryPurchase)},
                    {Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.OperationalExpense)},
                    {Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.CashOut)},
                    {Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.Reversal)}
                )),
                CHECK(
                    (movement_type={Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.CashIn)}
                        AND delta_cents=requested_amount_cents
                        AND reversal_of_movement_id IS NULL)
                    OR
                    (movement_type IN (
                        {Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.InventoryPurchase)},
                        {Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.OperationalExpense)},
                        {Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.CashOut)})
                        AND delta_cents=-requested_amount_cents
                        AND reversal_of_movement_id IS NULL)
                    OR
                    (movement_type={Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.Reversal)}
                        AND reversal_of_movement_id IS NOT NULL)
                )
            );

            CREATE INDEX idx_cafe_cash_operations_session_time
                ON cafe_cash_operations(cashier_session_id, occurred_at_utc, operation_id);

            CREATE INDEX idx_cafe_cash_operations_reference
                ON cafe_cash_operations(reference_type, reference_id, movement_type);

            CREATE TABLE cafe_cash_bootstrap_state (
                store_id TEXT PRIMARY KEY,
                central_instance_id TEXT NOT NULL,
                baseline_server_sequence INTEGER NOT NULL,
                snapshot_id TEXT NOT NULL,
                snapshot_hash TEXT NOT NULL,
                applied_at_utc TEXT NOT NULL,
                CHECK(baseline_server_sequence >= 0)
            );

            CREATE TRIGGER trg_cafe_cash_operations_no_update
            BEFORE UPDATE ON cafe_cash_operations
            BEGIN
                SELECT RAISE(ABORT, 'cafe cash operations are immutable');
            END;

            CREATE TRIGGER trg_cafe_cash_operations_no_delete
            BEFORE DELETE ON cafe_cash_operations
            BEGIN
                SELECT RAISE(ABORT, 'cafe cash operations are immutable');
            END;

            CREATE TRIGGER trg_c6_cash_operation_movement_match
            BEFORE INSERT ON cafe_cash_operations
            BEGIN
                SELECT CASE WHEN NOT EXISTS (
                    SELECT 1
                    FROM cash_movements m
                    WHERE m.id=NEW.movement_id
                      AND m.cashier_session_id=NEW.cashier_session_id
                      AND m.actor_user_id=NEW.actor_user_id
                      AND m.movement_type=NEW.movement_type
                      AND m.delta_cents=NEW.delta_cents
                      AND UPPER(TRIM(m.reference_type))=UPPER(TRIM(NEW.reference_type))
                      AND m.reference_id=NEW.reference_id
                      AND (
                          (NEW.reversal_of_movement_id IS NULL AND m.reversal_of_movement_id IS NULL)
                          OR
                          m.reversal_of_movement_id=NEW.reversal_of_movement_id
                      )
                ) THEN RAISE(ABORT, 'c6 cash operation must match persisted cash movement') END;
            END;

            CREATE TRIGGER trg_c6_cash_operation_source_owned_guard
            BEFORE INSERT ON cafe_cash_operations
            WHEN NEW.movement_type IN (
                {Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.SaleCashReceipt)},
                {Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.ReceivableCashCollection)},
                {Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.CashRefund)}
            )
            BEGIN
                SELECT RAISE(ABORT, 'source-owned cash movement cannot be posted through C6');
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
