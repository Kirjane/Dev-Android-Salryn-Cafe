using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

public sealed class ProcureToPaySchemaMigration : IDatabaseMigration
{
    public int Version => 27;
    public string Name => "LPI procure-to-pay foundation schema";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        AddColumnIfMissing(connection, transaction, "suppliers", "code", "TEXT NULL");
        AddColumnIfMissing(connection, transaction, "suppliers", "contact_person", "TEXT NULL");
        AddColumnIfMissing(connection, transaction, "suppliers", "email", "TEXT NULL");
        AddColumnIfMissing(connection, transaction, "suppliers", "address", "TEXT NULL");
        AddColumnIfMissing(connection, transaction, "suppliers", "tin", "TEXT NULL");
        AddColumnIfMissing(connection, transaction, "suppliers", "payment_terms_days", "INTEGER NOT NULL DEFAULT 30");
        AddColumnIfMissing(connection, transaction, "suppliers", "lead_time_days", "INTEGER NOT NULL DEFAULT 3");
        AddColumnIfMissing(connection, transaction, "suppliers", "default_currency", "TEXT NOT NULL DEFAULT 'PHP'");
        AddColumnIfMissing(connection, transaction, "suppliers", "created_at", "TEXT NULL");
        AddColumnIfMissing(connection, transaction, "suppliers", "updated_at", "TEXT NULL");

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE UNIQUE INDEX IF NOT EXISTS UX_suppliers_code
            ON suppliers(code)
            WHERE code IS NOT NULL AND TRIM(code) <> '';

            CREATE TABLE IF NOT EXISTS lpi_document_sequences (
                prefix TEXT PRIMARY KEY,
                current_number INTEGER NOT NULL DEFAULT 0,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS inventory_locations (
                id TEXT PRIMARY KEY,
                code TEXT NOT NULL UNIQUE,
                name TEXT NOT NULL UNIQUE,
                location_type TEXT NOT NULL,
                is_active INTEGER NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS product_stock_buckets (
                id TEXT PRIMARY KEY,
                product_id TEXT NOT NULL,
                location_id TEXT NOT NULL,
                bucket_status TEXT NOT NULL,
                quantity_x1000 INTEGER NOT NULL DEFAULT 0,
                updated_at TEXT NOT NULL,
                FOREIGN KEY (product_id) REFERENCES products(id) ON DELETE RESTRICT,
                FOREIGN KEY (location_id) REFERENCES inventory_locations(id) ON DELETE RESTRICT,
                UNIQUE (product_id, location_id, bucket_status)
            );

            CREATE TABLE IF NOT EXISTS purchase_requisitions (
                id TEXT PRIMARY KEY,
                pr_number TEXT NOT NULL UNIQUE,
                department_name TEXT NOT NULL,
                requested_by_user_id TEXT NOT NULL,
                date_requested TEXT NOT NULL,
                date_needed TEXT NOT NULL,
                priority TEXT NOT NULL DEFAULT 'Normal',
                purpose TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'Draft',
                estimated_total_cents INTEGER NOT NULL DEFAULT 0,
                approved_by_user_id TEXT NULL,
                approved_at TEXT NULL,
                rejection_or_revision_reason TEXT NULL,
                notes TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                FOREIGN KEY (requested_by_user_id) REFERENCES users(id) ON DELETE RESTRICT,
                FOREIGN KEY (approved_by_user_id) REFERENCES users(id) ON DELETE RESTRICT
            );
            CREATE INDEX IF NOT EXISTS idx_purchase_requisitions_status ON purchase_requisitions(status, created_at);

            CREATE TABLE IF NOT EXISTS purchase_requisition_lines (
                id TEXT PRIMARY KEY,
                requisition_id TEXT NOT NULL,
                line_number INTEGER NOT NULL,
                product_id TEXT NULL,
                item_code TEXT NOT NULL,
                item_description TEXT NOT NULL,
                category_name TEXT NOT NULL,
                purchase_classification TEXT NOT NULL DEFAULT 'Resale',
                unit_name TEXT NOT NULL,
                requested_qty_x1000 INTEGER NOT NULL CHECK(requested_qty_x1000 > 0),
                approved_qty_x1000 INTEGER NOT NULL DEFAULT 0 CHECK(approved_qty_x1000 >= 0),
                converted_po_qty_x1000 INTEGER NOT NULL DEFAULT 0 CHECK(converted_po_qty_x1000 >= 0),
                estimated_unit_cost_cents INTEGER NOT NULL DEFAULT 0 CHECK(estimated_unit_cost_cents >= 0),
                estimated_line_total_cents INTEGER NOT NULL DEFAULT 0 CHECK(estimated_line_total_cents >= 0),
                preferred_supplier_id TEXT NULL,
                specifications TEXT NULL,
                notes TEXT NULL,
                FOREIGN KEY (requisition_id) REFERENCES purchase_requisitions(id) ON DELETE CASCADE,
                FOREIGN KEY (product_id) REFERENCES products(id) ON DELETE RESTRICT,
                FOREIGN KEY (preferred_supplier_id) REFERENCES suppliers(id) ON DELETE SET NULL
            );
            CREATE INDEX IF NOT EXISTS idx_pr_lines_requisition ON purchase_requisition_lines(requisition_id, line_number);

            CREATE TABLE IF NOT EXISTS purchase_requisition_actions (
                id TEXT PRIMARY KEY,
                requisition_id TEXT NOT NULL,
                action_type TEXT NOT NULL,
                actor_user_id TEXT NOT NULL,
                previous_status TEXT NOT NULL,
                new_status TEXT NOT NULL,
                reason TEXT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY (requisition_id) REFERENCES purchase_requisitions(id) ON DELETE CASCADE,
                FOREIGN KEY (actor_user_id) REFERENCES users(id) ON DELETE RESTRICT
            );

            CREATE TABLE IF NOT EXISTS supplier_quotations (
                id TEXT PRIMARY KEY,
                requisition_id TEXT NULL,
                requisition_line_id TEXT NULL,
                supplier_id TEXT NOT NULL,
                product_id TEXT NULL,
                item_description TEXT NOT NULL,
                quoted_unit_cost_cents INTEGER NOT NULL CHECK(quoted_unit_cost_cents >= 0),
                minimum_order_qty_x1000 INTEGER NOT NULL DEFAULT 1000 CHECK(minimum_order_qty_x1000 > 0),
                lead_time_days INTEGER NOT NULL DEFAULT 1 CHECK(lead_time_days >= 0),
                payment_terms_days INTEGER NOT NULL DEFAULT 30 CHECK(payment_terms_days >= 0),
                validity_date TEXT NULL,
                is_selected INTEGER NOT NULL DEFAULT 0,
                selection_reason TEXT NULL,
                recorded_by_user_id TEXT NOT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY (requisition_id) REFERENCES purchase_requisitions(id) ON DELETE SET NULL,
                FOREIGN KEY (requisition_line_id) REFERENCES purchase_requisition_lines(id) ON DELETE SET NULL,
                FOREIGN KEY (supplier_id) REFERENCES suppliers(id) ON DELETE RESTRICT,
                FOREIGN KEY (product_id) REFERENCES products(id) ON DELETE SET NULL,
                FOREIGN KEY (recorded_by_user_id) REFERENCES users(id) ON DELETE RESTRICT
            );

            CREATE TABLE IF NOT EXISTS purchase_orders (
                id TEXT PRIMARY KEY,
                po_number TEXT NOT NULL UNIQUE,
                supplier_id TEXT NOT NULL,
                source_requisition_id TEXT NULL,
                order_date TEXT NOT NULL,
                expected_delivery_date TEXT NOT NULL,
                delivery_location_id TEXT NOT NULL,
                payment_terms_days INTEGER NOT NULL DEFAULT 30,
                status TEXT NOT NULL DEFAULT 'Draft',
                subtotal_cents INTEGER NOT NULL DEFAULT 0,
                discount_cents INTEGER NOT NULL DEFAULT 0,
                tax_cents INTEGER NOT NULL DEFAULT 0,
                shipping_cents INTEGER NOT NULL DEFAULT 0,
                total_amount_cents INTEGER NOT NULL DEFAULT 0,
                created_by_user_id TEXT NOT NULL,
                approved_by_user_id TEXT NULL,
                approved_at TEXT NULL,
                sent_at TEXT NULL,
                confirmed_at TEXT NULL,
                cancelled_by_user_id TEXT NULL,
                cancelled_at TEXT NULL,
                cancellation_reason TEXT NULL,
                terms_and_conditions TEXT NULL,
                notes TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                FOREIGN KEY (supplier_id) REFERENCES suppliers(id) ON DELETE RESTRICT,
                FOREIGN KEY (source_requisition_id) REFERENCES purchase_requisitions(id) ON DELETE SET NULL,
                FOREIGN KEY (delivery_location_id) REFERENCES inventory_locations(id) ON DELETE RESTRICT,
                FOREIGN KEY (created_by_user_id) REFERENCES users(id) ON DELETE RESTRICT,
                FOREIGN KEY (approved_by_user_id) REFERENCES users(id) ON DELETE RESTRICT,
                FOREIGN KEY (cancelled_by_user_id) REFERENCES users(id) ON DELETE RESTRICT
            );
            CREATE INDEX IF NOT EXISTS idx_purchase_orders_supplier_status ON purchase_orders(supplier_id, status, order_date);

            CREATE TABLE IF NOT EXISTS purchase_order_lines (
                id TEXT PRIMARY KEY,
                purchase_order_id TEXT NOT NULL,
                line_number INTEGER NOT NULL,
                requisition_line_id TEXT NULL,
                product_id TEXT NULL,
                item_code TEXT NOT NULL,
                item_description TEXT NOT NULL,
                purchase_classification TEXT NOT NULL DEFAULT 'Resale',
                unit_name TEXT NOT NULL,
                ordered_qty_x1000 INTEGER NOT NULL CHECK(ordered_qty_x1000 > 0),
                received_accepted_qty_x1000 INTEGER NOT NULL DEFAULT 0 CHECK(received_accepted_qty_x1000 >= 0),
                received_rejected_qty_x1000 INTEGER NOT NULL DEFAULT 0 CHECK(received_rejected_qty_x1000 >= 0),
                cancelled_qty_x1000 INTEGER NOT NULL DEFAULT 0 CHECK(cancelled_qty_x1000 >= 0),
                unit_cost_cents INTEGER NOT NULL CHECK(unit_cost_cents >= 0),
                discount_cents INTEGER NOT NULL DEFAULT 0 CHECK(discount_cents >= 0),
                tax_rate_bp INTEGER NOT NULL DEFAULT 0 CHECK(tax_rate_bp >= 0),
                tax_amount_cents INTEGER NOT NULL DEFAULT 0 CHECK(tax_amount_cents >= 0),
                line_total_cents INTEGER NOT NULL CHECK(line_total_cents >= 0),
                notes TEXT NULL,
                FOREIGN KEY (purchase_order_id) REFERENCES purchase_orders(id) ON DELETE CASCADE,
                FOREIGN KEY (requisition_line_id) REFERENCES purchase_requisition_lines(id) ON DELETE SET NULL,
                FOREIGN KEY (product_id) REFERENCES products(id) ON DELETE RESTRICT
            );
            CREATE INDEX IF NOT EXISTS idx_po_lines_po ON purchase_order_lines(purchase_order_id, line_number);

            CREATE TABLE IF NOT EXISTS purchase_order_history (
                id TEXT PRIMARY KEY,
                purchase_order_id TEXT NOT NULL,
                action_type TEXT NOT NULL,
                actor_user_id TEXT NOT NULL,
                previous_status TEXT NOT NULL,
                new_status TEXT NOT NULL,
                reason TEXT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY (purchase_order_id) REFERENCES purchase_orders(id) ON DELETE CASCADE,
                FOREIGN KEY (actor_user_id) REFERENCES users(id) ON DELETE RESTRICT
            );

            CREATE TABLE IF NOT EXISTS receiving_reports (
                id TEXT PRIMARY KEY,
                rr_number TEXT NOT NULL UNIQUE,
                purchase_order_id TEXT NOT NULL,
                supplier_id TEXT NOT NULL,
                location_id TEXT NOT NULL,
                delivery_receipt_no TEXT NOT NULL,
                supplier_invoice_ref TEXT NULL,
                carrier_or_driver TEXT NULL,
                received_date TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'Draft',
                has_overdelivery INTEGER NOT NULL DEFAULT 0,
                overdelivery_approved_by_user_id TEXT NULL,
                overdelivery_approval_reason TEXT NULL,
                received_by_user_id TEXT NOT NULL,
                inspected_by_user_id TEXT NULL,
                posted_by_user_id TEXT NULL,
                posted_at TEXT NULL,
                client_request_id TEXT NULL UNIQUE,
                request_payload_hash TEXT NULL,
                notes TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                FOREIGN KEY (purchase_order_id) REFERENCES purchase_orders(id) ON DELETE RESTRICT,
                FOREIGN KEY (supplier_id) REFERENCES suppliers(id) ON DELETE RESTRICT,
                FOREIGN KEY (location_id) REFERENCES inventory_locations(id) ON DELETE RESTRICT,
                FOREIGN KEY (received_by_user_id) REFERENCES users(id) ON DELETE RESTRICT,
                FOREIGN KEY (inspected_by_user_id) REFERENCES users(id) ON DELETE RESTRICT,
                FOREIGN KEY (posted_by_user_id) REFERENCES users(id) ON DELETE RESTRICT,
                FOREIGN KEY (overdelivery_approved_by_user_id) REFERENCES users(id) ON DELETE RESTRICT
            );
            CREATE INDEX IF NOT EXISTS idx_receiving_reports_po ON receiving_reports(purchase_order_id, status);

            CREATE TABLE IF NOT EXISTS receiving_report_lines (
                id TEXT PRIMARY KEY,
                receiving_report_id TEXT NOT NULL,
                purchase_order_line_id TEXT NOT NULL,
                product_id TEXT NULL,
                item_code TEXT NOT NULL,
                item_description TEXT NOT NULL,
                purchase_classification TEXT NOT NULL DEFAULT 'Resale',
                unit_name TEXT NOT NULL,
                ordered_qty_x1000 INTEGER NOT NULL,
                previously_received_qty_x1000 INTEGER NOT NULL DEFAULT 0,
                actual_delivered_qty_x1000 INTEGER NOT NULL CHECK(actual_delivered_qty_x1000 >= 0),
                accepted_qty_x1000 INTEGER NOT NULL CHECK(accepted_qty_x1000 >= 0),
                rejected_qty_x1000 INTEGER NOT NULL DEFAULT 0 CHECK(rejected_qty_x1000 >= 0),
                shortage_qty_x1000 INTEGER NOT NULL DEFAULT 0 CHECK(shortage_qty_x1000 >= 0),
                overdelivery_qty_x1000 INTEGER NOT NULL DEFAULT 0 CHECK(overdelivery_qty_x1000 >= 0),
                unit_cost_cents INTEGER NOT NULL CHECK(unit_cost_cents >= 0),
                accepted_line_total_cents INTEGER NOT NULL DEFAULT 0 CHECK(accepted_line_total_cents >= 0),
                rejection_reason TEXT NULL,
                rejection_disposition TEXT NOT NULL DEFAULT 'None',
                lot_or_batch_no TEXT NULL,
                expiry_date TEXT NULL,
                stock_movement_id TEXT NULL,
                FOREIGN KEY (receiving_report_id) REFERENCES receiving_reports(id) ON DELETE CASCADE,
                FOREIGN KEY (purchase_order_line_id) REFERENCES purchase_order_lines(id) ON DELETE RESTRICT,
                FOREIGN KEY (product_id) REFERENCES products(id) ON DELETE RESTRICT,
                FOREIGN KEY (stock_movement_id) REFERENCES stock_movements(id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS stock_count_adjustments (
                id TEXT PRIMARY KEY,
                adjustment_number TEXT NOT NULL UNIQUE,
                product_id TEXT NOT NULL,
                location_id TEXT NOT NULL,
                system_qty_x1000 INTEGER NOT NULL,
                actual_counted_qty_x1000 INTEGER NOT NULL CHECK(actual_counted_qty_x1000 >= 0),
                variance_qty_x1000 INTEGER NOT NULL,
                unit_cost_cents INTEGER NOT NULL DEFAULT 0,
                variance_value_cents INTEGER NOT NULL DEFAULT 0,
                reason_code TEXT NOT NULL,
                notes TEXT NULL,
                status TEXT NOT NULL DEFAULT 'Posted',
                counted_by_user_id TEXT NOT NULL,
                approved_by_user_id TEXT NOT NULL,
                stock_movement_id TEXT NULL,
                created_at TEXT NOT NULL,
                posted_at TEXT NULL,
                FOREIGN KEY (product_id) REFERENCES products(id) ON DELETE RESTRICT,
                FOREIGN KEY (location_id) REFERENCES inventory_locations(id) ON DELETE RESTRICT,
                FOREIGN KEY (counted_by_user_id) REFERENCES users(id) ON DELETE RESTRICT,
                FOREIGN KEY (approved_by_user_id) REFERENCES users(id) ON DELETE RESTRICT,
                FOREIGN KEY (stock_movement_id) REFERENCES stock_movements(id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS supplier_invoices (
                id TEXT PRIMARY KEY,
                ap_voucher_number TEXT NOT NULL UNIQUE,
                supplier_id TEXT NOT NULL,
                normalized_supplier_invoice_no TEXT NOT NULL,
                supplier_invoice_no TEXT NOT NULL,
                purchase_order_id TEXT NULL,
                receiving_report_id TEXT NULL,
                invoice_date TEXT NOT NULL,
                due_date TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'Draft',
                match_status TEXT NOT NULL DEFAULT 'Unmatched',
                subtotal_cents INTEGER NOT NULL DEFAULT 0,
                discount_cents INTEGER NOT NULL DEFAULT 0,
                tax_cents INTEGER NOT NULL DEFAULT 0,
                withholding_tax_cents INTEGER NOT NULL DEFAULT 0,
                total_invoice_cents INTEGER NOT NULL CHECK(total_invoice_cents >= 0),
                credit_memo_applied_cents INTEGER NOT NULL DEFAULT 0 CHECK(credit_memo_applied_cents >= 0),
                paid_amount_cents INTEGER NOT NULL DEFAULT 0 CHECK(paid_amount_cents >= 0),
                remaining_balance_cents INTEGER NOT NULL DEFAULT 0 CHECK(remaining_balance_cents >= 0),
                variance_notes TEXT NULL,
                variance_approved_by_user_id TEXT NULL,
                variance_approval_reason TEXT NULL,
                recorded_by_user_id TEXT NOT NULL,
                posted_by_user_id TEXT NULL,
                posted_at TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                FOREIGN KEY (supplier_id) REFERENCES suppliers(id) ON DELETE RESTRICT,
                FOREIGN KEY (purchase_order_id) REFERENCES purchase_orders(id) ON DELETE RESTRICT,
                FOREIGN KEY (receiving_report_id) REFERENCES receiving_reports(id) ON DELETE RESTRICT,
                FOREIGN KEY (recorded_by_user_id) REFERENCES users(id) ON DELETE RESTRICT,
                FOREIGN KEY (posted_by_user_id) REFERENCES users(id) ON DELETE RESTRICT,
                FOREIGN KEY (variance_approved_by_user_id) REFERENCES users(id) ON DELETE RESTRICT
            );
            CREATE UNIQUE INDEX IF NOT EXISTS UX_supplier_invoices_supplier_normalized_no
            ON supplier_invoices(supplier_id, normalized_supplier_invoice_no)
            WHERE status <> 'Voided';
            CREATE INDEX IF NOT EXISTS idx_supplier_invoices_supplier_due ON supplier_invoices(supplier_id, due_date, status);

            CREATE TABLE IF NOT EXISTS supplier_invoice_lines (
                id TEXT PRIMARY KEY,
                supplier_invoice_id TEXT NOT NULL,
                purchase_order_line_id TEXT NULL,
                receiving_report_line_id TEXT NULL,
                product_id TEXT NULL,
                item_description TEXT NOT NULL,
                po_ordered_qty_x1000 INTEGER NOT NULL DEFAULT 0,
                rr_accepted_qty_x1000 INTEGER NOT NULL DEFAULT 0,
                invoiced_qty_x1000 INTEGER NOT NULL CHECK(invoiced_qty_x1000 > 0),
                po_unit_cost_cents INTEGER NOT NULL DEFAULT 0,
                invoiced_unit_cost_cents INTEGER NOT NULL CHECK(invoiced_unit_cost_cents >= 0),
                tax_cents INTEGER NOT NULL DEFAULT 0,
                line_total_cents INTEGER NOT NULL CHECK(line_total_cents >= 0),
                qty_variance_x1000 INTEGER NOT NULL DEFAULT 0,
                unit_cost_variance_cents INTEGER NOT NULL DEFAULT 0,
                line_match_status TEXT NOT NULL DEFAULT 'Matched',
                FOREIGN KEY (supplier_invoice_id) REFERENCES supplier_invoices(id) ON DELETE CASCADE,
                FOREIGN KEY (purchase_order_line_id) REFERENCES purchase_order_lines(id) ON DELETE SET NULL,
                FOREIGN KEY (receiving_report_line_id) REFERENCES receiving_report_lines(id) ON DELETE SET NULL,
                FOREIGN KEY (product_id) REFERENCES products(id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS supplier_returns (
                id TEXT PRIMARY KEY,
                return_number TEXT NOT NULL UNIQUE,
                supplier_id TEXT NOT NULL,
                receiving_report_id TEXT NOT NULL,
                purchase_order_id TEXT NOT NULL,
                supplier_invoice_id TEXT NULL,
                return_date TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'Posted',
                total_return_amount_cents INTEGER NOT NULL DEFAULT 0 CHECK(total_return_amount_cents >= 0),
                reason TEXT NOT NULL,
                created_by_user_id TEXT NOT NULL,
                approved_by_user_id TEXT NOT NULL,
                posted_at TEXT NOT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY (supplier_id) REFERENCES suppliers(id) ON DELETE RESTRICT,
                FOREIGN KEY (receiving_report_id) REFERENCES receiving_reports(id) ON DELETE RESTRICT,
                FOREIGN KEY (purchase_order_id) REFERENCES purchase_orders(id) ON DELETE RESTRICT,
                FOREIGN KEY (supplier_invoice_id) REFERENCES supplier_invoices(id) ON DELETE SET NULL,
                FOREIGN KEY (created_by_user_id) REFERENCES users(id) ON DELETE RESTRICT,
                FOREIGN KEY (approved_by_user_id) REFERENCES users(id) ON DELETE RESTRICT
            );

            CREATE TABLE IF NOT EXISTS supplier_return_lines (
                id TEXT PRIMARY KEY,
                supplier_return_id TEXT NOT NULL,
                receiving_report_line_id TEXT NOT NULL,
                product_id TEXT NOT NULL,
                return_qty_x1000 INTEGER NOT NULL CHECK(return_qty_x1000 > 0),
                unit_cost_cents INTEGER NOT NULL CHECK(unit_cost_cents >= 0),
                line_total_cents INTEGER NOT NULL CHECK(line_total_cents >= 0),
                reason TEXT NOT NULL,
                stock_movement_id TEXT NULL,
                FOREIGN KEY (supplier_return_id) REFERENCES supplier_returns(id) ON DELETE CASCADE,
                FOREIGN KEY (receiving_report_line_id) REFERENCES receiving_report_lines(id) ON DELETE RESTRICT,
                FOREIGN KEY (product_id) REFERENCES products(id) ON DELETE RESTRICT,
                FOREIGN KEY (stock_movement_id) REFERENCES stock_movements(id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS supplier_payable_entries (
                id TEXT PRIMARY KEY,
                supplier_id TEXT NOT NULL,
                supplier_invoice_id TEXT NOT NULL,
                entry_type TEXT NOT NULL,
                reference_number TEXT NOT NULL,
                debit_cents INTEGER NOT NULL DEFAULT 0 CHECK(debit_cents >= 0),
                credit_cents INTEGER NOT NULL DEFAULT 0 CHECK(credit_cents >= 0),
                balance_after_cents INTEGER NOT NULL,
                note TEXT NULL,
                actor_user_id TEXT NOT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY (supplier_id) REFERENCES suppliers(id) ON DELETE RESTRICT,
                FOREIGN KEY (supplier_invoice_id) REFERENCES supplier_invoices(id) ON DELETE RESTRICT,
                FOREIGN KEY (actor_user_id) REFERENCES users(id) ON DELETE RESTRICT
            );
            CREATE INDEX IF NOT EXISTS idx_supplier_payable_entries_supplier ON supplier_payable_entries(supplier_id, created_at);

            CREATE TABLE IF NOT EXISTS supplier_payments (
                id TEXT PRIMARY KEY,
                payment_number TEXT NOT NULL UNIQUE,
                supplier_invoice_id TEXT NOT NULL,
                supplier_id TEXT NOT NULL,
                payment_date TEXT NOT NULL,
                amount_cents INTEGER NOT NULL CHECK(amount_cents > 0),
                payment_method TEXT NOT NULL,
                disbursement_source TEXT NOT NULL,
                reference_no TEXT NOT NULL,
                check_no TEXT NULL,
                bank_name TEXT NULL,
                remarks TEXT NULL,
                status TEXT NOT NULL DEFAULT 'Settled',
                recorded_by_user_id TEXT NOT NULL,
                approved_by_user_id TEXT NULL,
                approved_at TEXT NULL,
                client_request_id TEXT NULL UNIQUE,
                request_payload_hash TEXT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY (supplier_invoice_id) REFERENCES supplier_invoices(id) ON DELETE RESTRICT,
                FOREIGN KEY (supplier_id) REFERENCES suppliers(id) ON DELETE RESTRICT,
                FOREIGN KEY (recorded_by_user_id) REFERENCES users(id) ON DELETE RESTRICT,
                FOREIGN KEY (approved_by_user_id) REFERENCES users(id) ON DELETE RESTRICT
            );
            CREATE INDEX IF NOT EXISTS idx_supplier_payments_invoice ON supplier_payments(supplier_invoice_id, payment_date);

            CREATE TABLE IF NOT EXISTS lpi_overdelivery_approvals (
                id TEXT PRIMARY KEY,
                receiving_report_id TEXT NOT NULL,
                purchase_order_id TEXT NOT NULL,
                approver_user_id TEXT NOT NULL,
                approval_reason TEXT NOT NULL,
                approved_lines_json TEXT NOT NULL,
                approved_at TEXT NOT NULL,
                is_revoked INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (receiving_report_id) REFERENCES receiving_reports(id) ON DELETE RESTRICT,
                FOREIGN KEY (purchase_order_id) REFERENCES purchase_orders(id) ON DELETE RESTRICT,
                FOREIGN KEY (approver_user_id) REFERENCES users(id) ON DELETE RESTRICT
            );
            CREATE INDEX IF NOT EXISTS idx_lpi_overdelivery_approvals_rr ON lpi_overdelivery_approvals(receiving_report_id, is_revoked);
            """;
        command.ExecuteNonQuery();

        AddColumnIfMissing(connection, transaction, "receiving_reports", "request_payload_hash", "TEXT NULL");
        AddColumnIfMissing(connection, transaction, "supplier_payments", "status", "TEXT NOT NULL DEFAULT 'Settled'");
        AddColumnIfMissing(connection, transaction, "supplier_payments", "approved_at", "TEXT NULL");
        AddColumnIfMissing(connection, transaction, "supplier_payments", "request_payload_hash", "TEXT NULL");

        SeedDefaultLocationsAndSequences(connection, transaction);
        BackfillLegacyProductStockBuckets(connection, transaction);
    }

    private static void AddColumnIfMissing(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        string columnName,
        string columnDefinition)
    {
        using SqliteCommand check = connection.CreateCommand();
        check.Transaction = transaction;
        check.CommandText = $"PRAGMA table_info({tableName});";
        using SqliteDataReader reader = check.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
        reader.Close();

        using SqliteCommand alter = connection.CreateCommand();
        alter.Transaction = transaction;
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        alter.ExecuteNonQuery();
    }

    private static void SeedDefaultLocationsAndSequences(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        string now = DateTimeOffset.UtcNow.ToString("O");
        (string Id, string Code, string Name, string Type)[] locations =
        {
            ("00000000-0000-0000-0000-000000000501", "MAIN-STORE", "Main Store Warehouse & Floor", "SellableStorage"),
            ("00000000-0000-0000-0000-000000000502", "RECEIVING-DOCK", "Receiving & Inspection Area", "ReceivingDock"),
            ("00000000-0000-0000-0000-000000000503", "QUARANTINE-HOLD", "Quarantine & Quality Hold", "Quarantine"),
            ("00000000-0000-0000-0000-000000000504", "DAMAGED-RETURN", "Damaged & Supplier Return Staging", "DamagedStaging")
        };

        foreach (var loc in locations)
        {
            using SqliteCommand locCmd = connection.CreateCommand();
            locCmd.Transaction = transaction;
            locCmd.CommandText = """
                INSERT OR IGNORE INTO inventory_locations (id, code, name, location_type, is_active, created_at)
                VALUES ($id, $code, $name, $type, 1, $now);
                """;
            locCmd.Parameters.AddWithValue("$id", loc.Id);
            locCmd.Parameters.AddWithValue("$code", loc.Code);
            locCmd.Parameters.AddWithValue("$name", loc.Name);
            locCmd.Parameters.AddWithValue("$type", loc.Type);
            locCmd.Parameters.AddWithValue("$now", now);
            locCmd.ExecuteNonQuery();
        }

        string[] prefixes = { "PR", "PO", "RR", "SRET", "ADJ", "APV", "PAY" };
        foreach (string prefix in prefixes)
        {
            using SqliteCommand seqCmd = connection.CreateCommand();
            seqCmd.Transaction = transaction;
            seqCmd.CommandText = """
                INSERT OR IGNORE INTO lpi_document_sequences (prefix, current_number, updated_at)
                VALUES ($prefix, 0, $now);
                """;
            seqCmd.Parameters.AddWithValue("$prefix", prefix);
            seqCmd.Parameters.AddWithValue("$now", now);
            seqCmd.ExecuteNonQuery();
        }
    }

    private static void BackfillLegacyProductStockBuckets(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using SqliteCommand backfillCmd = connection.CreateCommand();
        backfillCmd.Transaction = transaction;
        backfillCmd.CommandText = """
            INSERT OR IGNORE INTO product_stock_buckets (id, product_id, location_id, bucket_status, quantity_x1000, updated_at)
            SELECT
                lower(hex(randomblob(16))),
                p.id,
                loc.id,
                'Available',
                p.stock_on_hand_x1000,
                p.updated_at
            FROM products p
            CROSS JOIN inventory_locations loc
            WHERE loc.code = 'MAIN-STORE'
              AND p.track_stock = 1
              AND p.is_active = 1;
            """;
        backfillCmd.ExecuteNonQuery();
    }
}
