using Microsoft.Data.Sqlite;
using Salryn.Core.Models;

namespace Salryn.Infrastructure.Data.Migrations;

public sealed class CafeFinancialSchemaMigration : IDatabaseMigration
{
    public int Version => 34;

    public string Name => "Cafe payments discounts fees and refund authority";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, """
            ALTER TABLE sales
                ADD COLUMN fee_total_cents INTEGER NOT NULL DEFAULT 0
                CHECK(fee_total_cents >= 0);

            CREATE TABLE cafe_sale_financial_decisions (
                financial_decision_id TEXT PRIMARY KEY,
                operation_id TEXT NOT NULL UNIQUE,
                store_id TEXT NOT NULL,
                order_id TEXT NULL,
                order_revision_id TEXT NULL,
                expected_order_version INTEGER NULL,
                sale_id TEXT NOT NULL UNIQUE,
                cashier_session_id TEXT NOT NULL,
                actor_user_id TEXT NOT NULL,
                gross_subtotal_cents INTEGER NOT NULL,
                line_discount_total_cents INTEGER NOT NULL,
                order_discount_total_cents INTEGER NOT NULL,
                fee_total_cents INTEGER NOT NULL,
                tax_cents INTEGER NOT NULL,
                net_sale_total_cents INTEGER NOT NULL,
                cash_tender_cents INTEGER NOT NULL,
                accounts_receivable_tender_cents INTEGER NOT NULL,
                amount_paid_cents INTEGER NOT NULL,
                balance_due_cents INTEGER NOT NULL,
                change_cents INTEGER NOT NULL,
                discount_reason TEXT NULL,
                content_hash TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                FOREIGN KEY(order_id) REFERENCES cafe_orders(order_id),
                FOREIGN KEY(order_revision_id) REFERENCES cafe_order_revisions(revision_id),
                FOREIGN KEY(sale_id) REFERENCES sales(id),
                FOREIGN KEY(cashier_session_id) REFERENCES cashier_sessions(id),
                FOREIGN KEY(actor_user_id) REFERENCES users(id),
                CHECK(expected_order_version IS NULL OR expected_order_version >= 0),
                CHECK(gross_subtotal_cents >= 0),
                CHECK(line_discount_total_cents >= 0),
                CHECK(order_discount_total_cents >= 0),
                CHECK(fee_total_cents >= 0),
                CHECK(tax_cents >= 0),
                CHECK(net_sale_total_cents >= 0),
                CHECK(cash_tender_cents >= 0),
                CHECK(accounts_receivable_tender_cents >= 0),
                CHECK(amount_paid_cents >= 0),
                CHECK(balance_due_cents >= 0),
                CHECK(change_cents >= 0),
                CHECK(line_discount_total_cents + order_discount_total_cents <= gross_subtotal_cents),
                CHECK(
                    net_sale_total_cents =
                    gross_subtotal_cents
                    - line_discount_total_cents
                    - order_discount_total_cents
                    + fee_total_cents
                    + tax_cents
                ),
                CHECK(amount_paid_cents = cash_tender_cents + accounts_receivable_tender_cents),
                CHECK(balance_due_cents = accounts_receivable_tender_cents),
                CHECK(change_cents = amount_paid_cents - net_sale_total_cents),
                CHECK(
                    (order_id IS NULL AND order_revision_id IS NULL AND expected_order_version IS NULL)
                    OR
                    (order_id IS NOT NULL AND order_revision_id IS NOT NULL AND expected_order_version IS NOT NULL)
                )
            );

            CREATE TABLE cafe_sale_financial_line_discounts (
                financial_decision_id TEXT NOT NULL,
                sale_item_id TEXT NOT NULL,
                line_discount_cents INTEGER NOT NULL,
                order_discount_allocated_cents INTEGER NOT NULL,
                total_discount_cents INTEGER NOT NULL,
                PRIMARY KEY(financial_decision_id, sale_item_id),
                FOREIGN KEY(financial_decision_id)
                    REFERENCES cafe_sale_financial_decisions(financial_decision_id),
                FOREIGN KEY(sale_item_id) REFERENCES sale_items(id),
                CHECK(line_discount_cents >= 0),
                CHECK(order_discount_allocated_cents >= 0),
                CHECK(total_discount_cents = line_discount_cents + order_discount_allocated_cents)
            );

            CREATE TABLE cafe_sale_fees (
                fee_id TEXT PRIMARY KEY,
                financial_decision_id TEXT NOT NULL,
                sale_id TEXT NOT NULL,
                fee_type TEXT NOT NULL,
                display_label_snapshot TEXT NOT NULL,
                amount_cents INTEGER NOT NULL,
                actor_user_id TEXT NOT NULL,
                reason TEXT NULL,
                created_at_utc TEXT NOT NULL,
                FOREIGN KEY(financial_decision_id)
                    REFERENCES cafe_sale_financial_decisions(financial_decision_id),
                FOREIGN KEY(sale_id) REFERENCES sales(id),
                FOREIGN KEY(actor_user_id) REFERENCES users(id),
                CHECK(fee_type='Service'),
                CHECK(amount_cents >= 0)
            );

            CREATE INDEX idx_cafe_sale_financial_order
                ON cafe_sale_financial_decisions(order_id, order_revision_id);
            CREATE INDEX idx_cafe_sale_fees_sale
                ON cafe_sale_fees(sale_id, fee_id);

            CREATE TABLE cafe_sale_refunds (
                refund_id TEXT PRIMARY KEY,
                sale_id TEXT NOT NULL,
                store_id TEXT NOT NULL,
                current_version INTEGER NOT NULL,
                state TEXT NOT NULL,
                reason TEXT NOT NULL,
                requested_by_user_id TEXT NOT NULL,
                requested_at_utc TEXT NOT NULL,
                request_hash TEXT NOT NULL,
                resolved_by_user_id TEXT NULL,
                resolved_at_utc TEXT NULL,
                resolution_reason TEXT NULL,
                committed_by_user_id TEXT NULL,
                committed_at_utc TEXT NULL,
                total_refund_cents INTEGER NULL,
                FOREIGN KEY(sale_id) REFERENCES sales(id),
                FOREIGN KEY(requested_by_user_id) REFERENCES users(id),
                FOREIGN KEY(resolved_by_user_id) REFERENCES users(id),
                FOREIGN KEY(committed_by_user_id) REFERENCES users(id),
                CHECK(current_version > 0),
                CHECK(state IN ('Requested','Approved','Rejected','Committed')),
                CHECK(total_refund_cents IS NULL OR total_refund_cents >= 0),
                CHECK(
                    (state='Requested'
                        AND resolved_by_user_id IS NULL
                        AND resolved_at_utc IS NULL
                        AND committed_by_user_id IS NULL
                        AND committed_at_utc IS NULL
                        AND total_refund_cents IS NULL)
                    OR
                    (state='Approved'
                        AND resolved_by_user_id IS NOT NULL
                        AND resolved_at_utc IS NOT NULL
                        AND committed_by_user_id IS NULL
                        AND committed_at_utc IS NULL
                        AND total_refund_cents IS NULL)
                    OR
                    (state='Rejected'
                        AND resolved_by_user_id IS NOT NULL
                        AND resolved_at_utc IS NOT NULL
                        AND committed_by_user_id IS NULL
                        AND committed_at_utc IS NULL
                        AND total_refund_cents IS NULL)
                    OR
                    (state='Committed'
                        AND resolved_by_user_id IS NOT NULL
                        AND resolved_at_utc IS NOT NULL
                        AND committed_by_user_id IS NOT NULL
                        AND committed_at_utc IS NOT NULL
                        AND total_refund_cents IS NOT NULL)
                )
            );

            CREATE TABLE cafe_sale_refund_lines (
                refund_line_id TEXT PRIMARY KEY,
                refund_id TEXT NOT NULL,
                sale_item_id TEXT NOT NULL,
                product_id TEXT NOT NULL,
                product_name_snapshot TEXT NOT NULL,
                original_quantity_x1000 INTEGER NOT NULL,
                refund_quantity_x1000 INTEGER NOT NULL,
                frozen_unit_price_cents INTEGER NOT NULL,
                frozen_discount_cents INTEGER NOT NULL,
                refund_amount_cents INTEGER NOT NULL,
                stock_disposition TEXT NOT NULL,
                operator_note TEXT NULL,
                FOREIGN KEY(refund_id) REFERENCES cafe_sale_refunds(refund_id),
                FOREIGN KEY(sale_item_id) REFERENCES sale_items(id),
                FOREIGN KEY(product_id) REFERENCES products(id),
                CHECK(original_quantity_x1000 > 0),
                CHECK(refund_quantity_x1000 > 0),
                CHECK(refund_quantity_x1000 <= original_quantity_x1000),
                CHECK(frozen_unit_price_cents >= 0),
                CHECK(frozen_discount_cents >= 0),
                CHECK(refund_amount_cents >= 0),
                CHECK(stock_disposition IN ('Restock','DamagedNonRestockable','ServiceNoStock')),
                UNIQUE(refund_id, sale_item_id)
            );

            CREATE TABLE cafe_sale_refund_operations (
                operation_id TEXT PRIMARY KEY,
                device_id TEXT NOT NULL,
                store_id TEXT NOT NULL,
                refund_id TEXT NOT NULL,
                sale_id TEXT NOT NULL,
                operation_type TEXT NOT NULL,
                payload_hash TEXT NOT NULL,
                expected_version INTEGER NOT NULL,
                resulting_version INTEGER NOT NULL,
                result_state TEXT NOT NULL,
                actor_user_id TEXT NOT NULL,
                occurred_at_utc TEXT NOT NULL,
                FOREIGN KEY(refund_id) REFERENCES cafe_sale_refunds(refund_id),
                FOREIGN KEY(sale_id) REFERENCES sales(id),
                FOREIGN KEY(actor_user_id) REFERENCES users(id),
                CHECK(expected_version >= 0),
                CHECK(resulting_version > 0),
                CHECK(operation_type IN (
                    'SaleRefundRequestedV1',
                    'SaleRefundResolvedV1',
                    'SaleRefundCommittedV1')),
                CHECK(result_state IN ('Requested','Approved','Rejected','Committed'))
            );

            CREATE TABLE cafe_sale_refund_events (
                event_id TEXT PRIMARY KEY,
                operation_id TEXT NOT NULL UNIQUE,
                refund_id TEXT NOT NULL,
                sale_id TEXT NOT NULL,
                expected_version INTEGER NOT NULL,
                resulting_version INTEGER NOT NULL,
                action TEXT NOT NULL,
                from_state TEXT NULL,
                to_state TEXT NOT NULL,
                actor_user_id TEXT NOT NULL,
                occurred_at_utc TEXT NOT NULL,
                reason TEXT NULL,
                content_hash TEXT NOT NULL,
                FOREIGN KEY(operation_id) REFERENCES cafe_sale_refund_operations(operation_id),
                FOREIGN KEY(refund_id) REFERENCES cafe_sale_refunds(refund_id),
                FOREIGN KEY(sale_id) REFERENCES sales(id),
                FOREIGN KEY(actor_user_id) REFERENCES users(id),
                CHECK(expected_version >= 0),
                CHECK(resulting_version > 0),
                CHECK(action IN ('Requested','Approved','Rejected','Committed')),
                CHECK(to_state IN ('Requested','Approved','Rejected','Committed'))
            );

            CREATE TABLE cafe_sale_refund_settlements (
                settlement_part_id TEXT PRIMARY KEY,
                refund_id TEXT NOT NULL,
                method TEXT NOT NULL,
                amount_cents INTEGER NOT NULL,
                cash_movement_id TEXT NULL,
                receivable_entry_id TEXT NULL,
                created_at_utc TEXT NOT NULL,
                FOREIGN KEY(refund_id) REFERENCES cafe_sale_refunds(refund_id),
                FOREIGN KEY(cash_movement_id) REFERENCES cash_movements(id),
                FOREIGN KEY(receivable_entry_id) REFERENCES accounts_receivable_entries(id),
                CHECK(method IN ('Cash','AccountsReceivable')),
                CHECK(amount_cents > 0),
                CHECK(
                    (method='Cash' AND cash_movement_id IS NOT NULL AND receivable_entry_id IS NULL)
                    OR
                    (method='AccountsReceivable' AND cash_movement_id IS NULL AND receivable_entry_id IS NOT NULL)
                ),
                UNIQUE(refund_id, method)
            );

            CREATE TABLE cafe_financial_bootstrap_state (
                store_id TEXT PRIMARY KEY,
                central_instance_id TEXT NOT NULL,
                baseline_server_sequence INTEGER NOT NULL,
                snapshot_id TEXT NOT NULL,
                snapshot_hash TEXT NOT NULL,
                applied_at_utc TEXT NOT NULL,
                CHECK(baseline_server_sequence >= 0)
            );

            CREATE INDEX idx_cafe_sale_refunds_sale_state
                ON cafe_sale_refunds(sale_id, state, requested_at_utc, refund_id);
            CREATE INDEX idx_cafe_sale_refund_lines_sale_item
                ON cafe_sale_refund_lines(sale_item_id, refund_id);
            CREATE INDEX idx_cafe_sale_refund_operations_refund_version
                ON cafe_sale_refund_operations(refund_id, resulting_version, operation_id);
            CREATE INDEX idx_cafe_sale_refund_events_refund_version
                ON cafe_sale_refund_events(refund_id, resulting_version, occurred_at_utc);

            ALTER TABLE accounts_receivable_entries
                ADD COLUMN refund_id TEXT NULL REFERENCES cafe_sale_refunds(refund_id);

            CREATE UNIQUE INDEX idx_ar_entries_refund_credit
                ON accounts_receivable_entries(refund_id)
                WHERE refund_id IS NOT NULL AND entry_type='AdjustmentCredit';
            """);

        Execute(connection, transaction, $"""
            CREATE UNIQUE INDEX idx_cash_movements_refund_cash
                ON cash_movements(reference_id)
                WHERE movement_type={Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.CashRefund)}
                  AND UPPER(TRIM(reference_type))='REFUND';
            """);

        foreach (string table in new[]
        {
            "cafe_sale_financial_decisions",
            "cafe_sale_financial_line_discounts",
            "cafe_sale_fees",
            "cafe_sale_refund_lines",
            "cafe_sale_refund_operations",
            "cafe_sale_refund_events",
            "cafe_sale_refund_settlements"
        })
        {
            Execute(
                connection,
                transaction,
                "CREATE TRIGGER trg_" + table + "_no_update BEFORE UPDATE ON " + table +
                " BEGIN SELECT RAISE(ABORT, '" + table + " is immutable'); END;");
            Execute(
                connection,
                transaction,
                "CREATE TRIGGER trg_" + table + "_no_delete BEFORE DELETE ON " + table +
                " BEGIN SELECT RAISE(ABORT, '" + table + " is immutable'); END;");
        }

        Execute(connection, transaction, """
            CREATE TRIGGER trg_cafe_sale_refunds_terminal_guard
            BEFORE UPDATE ON cafe_sale_refunds
            FOR EACH ROW
            WHEN OLD.state IN ('Rejected','Committed')
            BEGIN
                SELECT RAISE(ABORT, 'terminal cafe refund is immutable');
            END;

            CREATE TRIGGER trg_cafe_sale_refunds_no_delete
            BEFORE DELETE ON cafe_sale_refunds
            BEGIN
                SELECT RAISE(ABORT, 'cafe refund cannot be deleted');
            END;

            CREATE TRIGGER trg_c5_ar_refund_credit_shape
            BEFORE INSERT ON accounts_receivable_entries
            WHEN NEW.refund_id IS NOT NULL
            BEGIN
                SELECT CASE WHEN NEW.entry_type <> 'AdjustmentCredit'
                    THEN RAISE(ABORT, 'refund AR entry must be AdjustmentCredit') END;
                SELECT CASE WHEN NEW.credit_cents <= 0 OR NEW.debit_cents <> 0
                    THEN RAISE(ABORT, 'refund AR entry must be a positive credit') END;
                SELECT CASE WHEN NEW.sale_id IS NULL
                    THEN RAISE(ABORT, 'refund AR entry requires source sale') END;
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
