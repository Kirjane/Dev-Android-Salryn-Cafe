using Microsoft.Data.Sqlite;
using Salryn.Core.Models;

namespace Salryn.Infrastructure.Data.Migrations;

public sealed class CafeBookingSchemaMigration : IDatabaseMigration
{
    public int Version => 38;

    public string Name => "Cafe booking event coffee cart foundation";

    public void Up(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        RebuildFinancialDecisionCluster(connection, transaction);
        CreateBookingFoundation(connection, transaction);
        UpgradeSaleCashReceiptAuthority(connection, transaction);
        AddReservedBookingCashAuthority(connection, transaction);
        AssertForeignKeysClean(connection, transaction);
    }

    private static void RebuildFinancialDecisionCluster(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        Execute(connection, transaction, """
            CREATE TABLE cafe_sale_financial_decisions_v38 (
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
                booking_deposit_applied_cents INTEGER NOT NULL DEFAULT 0,
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
                CHECK(booking_deposit_applied_cents >= 0),
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
                CHECK(
                    amount_paid_cents =
                    cash_tender_cents
                    + accounts_receivable_tender_cents
                    + booking_deposit_applied_cents
                ),
                CHECK(balance_due_cents = accounts_receivable_tender_cents),
                CHECK(change_cents = amount_paid_cents - net_sale_total_cents),
                CHECK(
                    booking_deposit_applied_cents
                    + accounts_receivable_tender_cents
                    <= net_sale_total_cents
                ),
                CHECK(
                    (order_id IS NULL AND order_revision_id IS NULL AND expected_order_version IS NULL)
                    OR
                    (order_id IS NOT NULL AND order_revision_id IS NOT NULL AND expected_order_version IS NOT NULL)
                )
            );

            CREATE TABLE cafe_sale_financial_line_discounts_v38 (
                financial_decision_id TEXT NOT NULL,
                sale_item_id TEXT NOT NULL,
                line_discount_cents INTEGER NOT NULL,
                order_discount_allocated_cents INTEGER NOT NULL,
                total_discount_cents INTEGER NOT NULL,
                PRIMARY KEY(financial_decision_id, sale_item_id),
                FOREIGN KEY(financial_decision_id)
                    REFERENCES cafe_sale_financial_decisions_v38(financial_decision_id),
                FOREIGN KEY(sale_item_id) REFERENCES sale_items(id),
                CHECK(line_discount_cents >= 0),
                CHECK(order_discount_allocated_cents >= 0),
                CHECK(total_discount_cents = line_discount_cents + order_discount_allocated_cents)
            );

            CREATE TABLE cafe_sale_fees_v38 (
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
                    REFERENCES cafe_sale_financial_decisions_v38(financial_decision_id),
                FOREIGN KEY(sale_id) REFERENCES sales(id),
                FOREIGN KEY(actor_user_id) REFERENCES users(id),
                CHECK(fee_type IN ('Service','Delivery')),
                CHECK(amount_cents >= 0)
            );

            INSERT INTO cafe_sale_financial_decisions_v38(
                financial_decision_id,operation_id,store_id,order_id,order_revision_id,
                expected_order_version,sale_id,cashier_session_id,actor_user_id,
                gross_subtotal_cents,line_discount_total_cents,order_discount_total_cents,
                fee_total_cents,tax_cents,net_sale_total_cents,cash_tender_cents,
                accounts_receivable_tender_cents,booking_deposit_applied_cents,
                amount_paid_cents,balance_due_cents,change_cents,discount_reason,
                content_hash,created_at_utc)
            SELECT
                financial_decision_id,operation_id,store_id,order_id,order_revision_id,
                expected_order_version,sale_id,cashier_session_id,actor_user_id,
                gross_subtotal_cents,line_discount_total_cents,order_discount_total_cents,
                fee_total_cents,tax_cents,net_sale_total_cents,cash_tender_cents,
                accounts_receivable_tender_cents,0,
                amount_paid_cents,balance_due_cents,change_cents,discount_reason,
                content_hash,created_at_utc
            FROM cafe_sale_financial_decisions;

            INSERT INTO cafe_sale_financial_line_discounts_v38(
                financial_decision_id,sale_item_id,line_discount_cents,
                order_discount_allocated_cents,total_discount_cents)
            SELECT
                financial_decision_id,sale_item_id,line_discount_cents,
                order_discount_allocated_cents,total_discount_cents
            FROM cafe_sale_financial_line_discounts;

            INSERT INTO cafe_sale_fees_v38(
                fee_id,financial_decision_id,sale_id,fee_type,
                display_label_snapshot,amount_cents,actor_user_id,
                reason,created_at_utc)
            SELECT
                fee_id,financial_decision_id,sale_id,fee_type,
                display_label_snapshot,amount_cents,actor_user_id,
                reason,created_at_utc
            FROM cafe_sale_fees;
            """);

        AssertCountEqual(
            connection,
            transaction,
            "cafe_sale_financial_decisions",
            "cafe_sale_financial_decisions_v38");
        AssertCountEqual(
            connection,
            transaction,
            "cafe_sale_financial_line_discounts",
            "cafe_sale_financial_line_discounts_v38");
        AssertCountEqual(
            connection,
            transaction,
            "cafe_sale_fees",
            "cafe_sale_fees_v38");

        AssertNoRows(
            connection,
            transaction,
            """
            SELECT
                financial_decision_id,operation_id,store_id,order_id,order_revision_id,
                expected_order_version,sale_id,cashier_session_id,actor_user_id,
                gross_subtotal_cents,line_discount_total_cents,order_discount_total_cents,
                fee_total_cents,tax_cents,net_sale_total_cents,cash_tender_cents,
                accounts_receivable_tender_cents,amount_paid_cents,balance_due_cents,
                change_cents,discount_reason,content_hash,created_at_utc
            FROM cafe_sale_financial_decisions
            EXCEPT
            SELECT
                financial_decision_id,operation_id,store_id,order_id,order_revision_id,
                expected_order_version,sale_id,cashier_session_id,actor_user_id,
                gross_subtotal_cents,line_discount_total_cents,order_discount_total_cents,
                fee_total_cents,tax_cents,net_sale_total_cents,cash_tender_cents,
                accounts_receivable_tender_cents,amount_paid_cents,balance_due_cents,
                change_cents,discount_reason,content_hash,created_at_utc
            FROM cafe_sale_financial_decisions_v38;
            """,
            "C9 migration 38 parent copy lost or changed C5 financial evidence.");

        AssertNoRows(
            connection,
            transaction,
            """
            SELECT
                financial_decision_id,operation_id,store_id,order_id,order_revision_id,
                expected_order_version,sale_id,cashier_session_id,actor_user_id,
                gross_subtotal_cents,line_discount_total_cents,order_discount_total_cents,
                fee_total_cents,tax_cents,net_sale_total_cents,cash_tender_cents,
                accounts_receivable_tender_cents,amount_paid_cents,balance_due_cents,
                change_cents,discount_reason,content_hash,created_at_utc
            FROM cafe_sale_financial_decisions_v38
            EXCEPT
            SELECT
                financial_decision_id,operation_id,store_id,order_id,order_revision_id,
                expected_order_version,sale_id,cashier_session_id,actor_user_id,
                gross_subtotal_cents,line_discount_total_cents,order_discount_total_cents,
                fee_total_cents,tax_cents,net_sale_total_cents,cash_tender_cents,
                accounts_receivable_tender_cents,amount_paid_cents,balance_due_cents,
                change_cents,discount_reason,content_hash,created_at_utc
            FROM cafe_sale_financial_decisions;
            """,
            "C9 migration 38 parent copy introduced non-legacy financial evidence.");

        AssertNoRows(
            connection,
            transaction,
            """
            SELECT financial_decision_id,sale_item_id,line_discount_cents,
                   order_discount_allocated_cents,total_discount_cents
            FROM cafe_sale_financial_line_discounts
            EXCEPT
            SELECT financial_decision_id,sale_item_id,line_discount_cents,
                   order_discount_allocated_cents,total_discount_cents
            FROM cafe_sale_financial_line_discounts_v38;
            """,
            "C9 migration 38 lost C5 line-discount evidence.");

        AssertNoRows(
            connection,
            transaction,
            """
            SELECT financial_decision_id,sale_item_id,line_discount_cents,
                   order_discount_allocated_cents,total_discount_cents
            FROM cafe_sale_financial_line_discounts_v38
            EXCEPT
            SELECT financial_decision_id,sale_item_id,line_discount_cents,
                   order_discount_allocated_cents,total_discount_cents
            FROM cafe_sale_financial_line_discounts;
            """,
            "C9 migration 38 introduced unexpected line-discount evidence.");

        AssertNoRows(
            connection,
            transaction,
            """
            SELECT fee_id,financial_decision_id,sale_id,fee_type,
                   display_label_snapshot,amount_cents,actor_user_id,
                   reason,created_at_utc
            FROM cafe_sale_fees
            EXCEPT
            SELECT fee_id,financial_decision_id,sale_id,fee_type,
                   display_label_snapshot,amount_cents,actor_user_id,
                   reason,created_at_utc
            FROM cafe_sale_fees_v38;
            """,
            "C9 migration 38 lost C5/C8 fee evidence.");

        AssertNoRows(
            connection,
            transaction,
            """
            SELECT fee_id,financial_decision_id,sale_id,fee_type,
                   display_label_snapshot,amount_cents,actor_user_id,
                   reason,created_at_utc
            FROM cafe_sale_fees_v38
            EXCEPT
            SELECT fee_id,financial_decision_id,sale_id,fee_type,
                   display_label_snapshot,amount_cents,actor_user_id,
                   reason,created_at_utc
            FROM cafe_sale_fees;
            """,
            "C9 migration 38 introduced unexpected fee evidence.");

        Execute(connection, transaction, """
            DROP TABLE cafe_sale_financial_line_discounts;
            DROP TABLE cafe_sale_fees;
            DROP TABLE cafe_sale_financial_decisions;

            ALTER TABLE cafe_sale_financial_decisions_v38
                RENAME TO cafe_sale_financial_decisions;
            ALTER TABLE cafe_sale_financial_line_discounts_v38
                RENAME TO cafe_sale_financial_line_discounts;
            ALTER TABLE cafe_sale_fees_v38
                RENAME TO cafe_sale_fees;

            CREATE INDEX idx_cafe_sale_financial_order
                ON cafe_sale_financial_decisions(order_id, order_revision_id);
            CREATE INDEX idx_cafe_sale_fees_sale
                ON cafe_sale_fees(sale_id, fee_id);
            """);

        foreach (string table in new[]
        {
            "cafe_sale_financial_decisions",
            "cafe_sale_financial_line_discounts",
            "cafe_sale_fees"
        })
        {
            Execute(
                connection,
                transaction,
                "CREATE TRIGGER trg_" + table +
                "_no_update BEFORE UPDATE ON " + table +
                " BEGIN SELECT RAISE(ABORT, '" +
                table + " is immutable'); END;");

            Execute(
                connection,
                transaction,
                "CREATE TRIGGER trg_" + table +
                "_no_delete BEFORE DELETE ON " + table +
                " BEGIN SELECT RAISE(ABORT, '" +
                table + " is immutable'); END;");
        }
    }

    private static void CreateBookingFoundation(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        Execute(connection, transaction, """
            CREATE TABLE cafe_bookings (
                booking_id TEXT PRIMARY KEY,
                store_id TEXT NOT NULL,
                current_version INTEGER NOT NULL,
                current_revision_id TEXT NOT NULL,
                status TEXT NOT NULL,
                fulfillment_state TEXT NOT NULL,
                created_by_user_id TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_by_user_id TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                cancelled_by_user_id TEXT NULL,
                cancelled_at_utc TEXT NULL,
                cancellation_reason TEXT NULL,
                FOREIGN KEY(current_revision_id)
                    REFERENCES cafe_booking_revisions(revision_id)
                    DEFERRABLE INITIALLY DEFERRED,
                FOREIGN KEY(created_by_user_id) REFERENCES users(id),
                FOREIGN KEY(updated_by_user_id) REFERENCES users(id),
                FOREIGN KEY(cancelled_by_user_id) REFERENCES users(id),
                CHECK(current_version > 0),
                CHECK(status IN (
                    'Draft','Confirmed','ReadyForFulfillment','Fulfilled','Cancelled')),
                CHECK(fulfillment_state IN ('NotStarted','OrderBound','SaleBound')),
                CHECK(
                    (status='Fulfilled' AND fulfillment_state='SaleBound')
                    OR status<>'Fulfilled'
                ),
                CHECK(
                    fulfillment_state<>'SaleBound'
                    OR status='Fulfilled'
                ),
                CHECK(
                    (status='Cancelled'
                     AND cancelled_by_user_id IS NOT NULL
                     AND cancelled_at_utc IS NOT NULL
                     AND cancellation_reason IS NOT NULL
                     AND LENGTH(TRIM(cancellation_reason)) > 0)
                    OR
                    (status<>'Cancelled'
                     AND cancelled_by_user_id IS NULL
                     AND cancelled_at_utc IS NULL
                     AND cancellation_reason IS NULL)
                )
            );

            CREATE TABLE cafe_booking_revisions (
                revision_id TEXT PRIMARY KEY,
                booking_id TEXT NOT NULL,
                revision_number INTEGER NOT NULL,
                booking_version INTEGER NOT NULL,
                previous_revision_id TEXT NULL,
                customer_id TEXT NOT NULL,
                customer_name_snapshot TEXT NOT NULL,
                customer_phone_snapshot TEXT NULL,
                customer_address_snapshot TEXT NULL,
                booking_mode TEXT NOT NULL,
                event_name TEXT NOT NULL,
                event_reference TEXT NULL,
                event_start_utc TEXT NOT NULL,
                event_end_utc TEXT NULL,
                location TEXT NULL,
                setup_at_utc TEXT NULL,
                service_window_start_utc TEXT NULL,
                service_window_end_utc TEXT NULL,
                headcount INTEGER NULL,
                package_label TEXT NULL,
                menu_publication_revision_id TEXT NOT NULL,
                quoted_total_cents INTEGER NOT NULL,
                deposit_requirement_cents INTEGER NOT NULL,
                special_instructions TEXT NULL,
                notes TEXT NULL,
                fulfillment_service_mode TEXT NOT NULL,
                actor_user_id TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                FOREIGN KEY(booking_id) REFERENCES cafe_bookings(booking_id),
                FOREIGN KEY(previous_revision_id) REFERENCES cafe_booking_revisions(revision_id),
                FOREIGN KEY(customer_id) REFERENCES customers(id),
                FOREIGN KEY(menu_publication_revision_id) REFERENCES menu_revisions(revision_id),
                FOREIGN KEY(actor_user_id) REFERENCES users(id),
                CHECK(revision_number > 0),
                CHECK(booking_version > 0),
                CHECK(booking_mode IN ('Event','CoffeeCart')),
                CHECK(LENGTH(TRIM(customer_name_snapshot)) > 0),
                CHECK(LENGTH(TRIM(event_name)) > 0),
                CHECK(headcount IS NULL OR headcount > 0),
                CHECK(quoted_total_cents >= 0),
                CHECK(deposit_requirement_cents >= 0),
                CHECK(deposit_requirement_cents <= quoted_total_cents),
                CHECK(fulfillment_service_mode IN ('DineIn','Takeout')),
                CHECK(LENGTH(content_hash) = 64),
                CHECK(
                    booking_mode<>'CoffeeCart'
                    OR (
                        event_end_utc IS NOT NULL
                        AND location IS NOT NULL
                        AND LENGTH(TRIM(location)) > 0
                        AND fulfillment_service_mode='Takeout'
                    )
                ),
                UNIQUE(booking_id, revision_number),
                UNIQUE(booking_id, booking_version),
                UNIQUE(booking_id, content_hash)
            );

            CREATE TABLE cafe_booking_revision_lines (
                revision_id TEXT NOT NULL,
                booking_line_id TEXT NOT NULL,
                product_id TEXT NOT NULL,
                product_name_snapshot TEXT NOT NULL,
                unit_name_snapshot TEXT NOT NULL,
                quantity_x1000 INTEGER NOT NULL,
                base_unit_price_cents INTEGER NOT NULL,
                variant_id TEXT NULL,
                variant_name_snapshot TEXT NULL,
                variant_unit_price_cents INTEGER NULL,
                quoted_unit_price_cents INTEGER NOT NULL,
                line_total_cents INTEGER NOT NULL,
                display_order INTEGER NOT NULL,
                line_note TEXT NULL,
                PRIMARY KEY(revision_id, booking_line_id),
                FOREIGN KEY(revision_id) REFERENCES cafe_booking_revisions(revision_id),
                FOREIGN KEY(product_id) REFERENCES products(id),
                CHECK(quantity_x1000 > 0),
                CHECK(base_unit_price_cents >= 0),
                CHECK(variant_unit_price_cents IS NULL OR variant_unit_price_cents >= 0),
                CHECK(quoted_unit_price_cents >= 0),
                CHECK(line_total_cents >= 0)
            );

            CREATE TABLE cafe_booking_revision_modifier_selections (
                revision_id TEXT NOT NULL,
                booking_line_id TEXT NOT NULL,
                group_id TEXT NOT NULL,
                group_name_snapshot TEXT NOT NULL,
                option_id TEXT NOT NULL,
                option_name_snapshot TEXT NOT NULL,
                price_delta_cents INTEGER NOT NULL,
                display_order INTEGER NOT NULL,
                PRIMARY KEY(revision_id, booking_line_id, group_id, option_id),
                FOREIGN KEY(revision_id, booking_line_id)
                    REFERENCES cafe_booking_revision_lines(revision_id, booking_line_id),
                CHECK(LENGTH(TRIM(group_id)) > 0),
                CHECK(LENGTH(TRIM(group_name_snapshot)) > 0),
                CHECK(LENGTH(TRIM(option_id)) > 0),
                CHECK(LENGTH(TRIM(option_name_snapshot)) > 0)
            );

            CREATE TABLE cafe_booking_operations (
                operation_id TEXT PRIMARY KEY,
                booking_id TEXT NOT NULL,
                device_id TEXT NOT NULL,
                store_id TEXT NOT NULL,
                operation_type TEXT NOT NULL,
                payload_hash TEXT NOT NULL,
                expected_version INTEGER NOT NULL,
                resulting_version INTEGER NOT NULL,
                result_revision_id TEXT NULL,
                result_status TEXT NOT NULL,
                result_fulfillment_state TEXT NOT NULL,
                actor_user_id TEXT NOT NULL,
                occurred_at_utc TEXT NOT NULL,
                FOREIGN KEY(booking_id) REFERENCES cafe_bookings(booking_id),
                FOREIGN KEY(result_revision_id) REFERENCES cafe_booking_revisions(revision_id),
                FOREIGN KEY(actor_user_id) REFERENCES users(id),
                CHECK(operation_type IN (
                    'BookingRevisionCommittedV1',
                    'BookingStatusAdvancedV1',
                    'BookingCancelledV1',
                    'BookingDepositReceivedV1',
                    'BookingDepositRefundedV1',
                    'BookingFulfillmentOrderBoundV1',
                    'SaleCommittedV3')),
                CHECK(LENGTH(payload_hash) = 64),
                CHECK(expected_version >= 0),
                CHECK(resulting_version > 0),
                CHECK(result_status IN (
                    'Draft','Confirmed','ReadyForFulfillment','Fulfilled','Cancelled')),
                CHECK(result_fulfillment_state IN ('NotStarted','OrderBound','SaleBound'))
            );

            CREATE TABLE cafe_booking_events (
                event_id TEXT PRIMARY KEY,
                operation_id TEXT NOT NULL UNIQUE,
                booking_id TEXT NOT NULL,
                expected_version INTEGER NOT NULL,
                resulting_version INTEGER NOT NULL,
                action TEXT NOT NULL,
                from_status TEXT NULL,
                to_status TEXT NOT NULL,
                from_fulfillment_state TEXT NULL,
                to_fulfillment_state TEXT NOT NULL,
                booking_revision_id TEXT NULL,
                order_id TEXT NULL,
                sale_id TEXT NULL,
                actor_user_id TEXT NOT NULL,
                occurred_at_utc TEXT NOT NULL,
                reason TEXT NULL,
                content_hash TEXT NOT NULL,
                FOREIGN KEY(operation_id) REFERENCES cafe_booking_operations(operation_id),
                FOREIGN KEY(booking_id) REFERENCES cafe_bookings(booking_id),
                FOREIGN KEY(booking_revision_id) REFERENCES cafe_booking_revisions(revision_id),
                FOREIGN KEY(order_id) REFERENCES cafe_orders(order_id),
                FOREIGN KEY(sale_id) REFERENCES sales(id),
                FOREIGN KEY(actor_user_id) REFERENCES users(id),
                CHECK(expected_version >= 0),
                CHECK(resulting_version > 0),
                CHECK(action IN (
                    'Created','Revised','Confirmed','ReadyForFulfillment',
                    'Cancelled','OrderBound','Fulfilled')),
                CHECK(to_status IN (
                    'Draft','Confirmed','ReadyForFulfillment','Fulfilled','Cancelled')),
                CHECK(to_fulfillment_state IN ('NotStarted','OrderBound','SaleBound')),
                CHECK(LENGTH(content_hash) = 64)
            );

            CREATE TABLE cafe_booking_deposit_facts (
                deposit_fact_id TEXT PRIMARY KEY,
                operation_id TEXT NOT NULL UNIQUE,
                booking_id TEXT NOT NULL,
                store_id TEXT NOT NULL,
                booking_revision_id TEXT NOT NULL,
                fact_type TEXT NOT NULL,
                actor_user_id TEXT NOT NULL,
                amount_cents INTEGER NOT NULL,
                occurred_at_utc TEXT NOT NULL,
                reference TEXT NOT NULL,
                cashier_session_id TEXT NULL,
                cash_movement_id TEXT NULL UNIQUE,
                sale_id TEXT NULL,
                payload_hash TEXT NOT NULL,
                FOREIGN KEY(booking_id) REFERENCES cafe_bookings(booking_id),
                FOREIGN KEY(booking_revision_id) REFERENCES cafe_booking_revisions(revision_id),
                FOREIGN KEY(actor_user_id) REFERENCES users(id),
                FOREIGN KEY(cashier_session_id) REFERENCES cashier_sessions(id),
                FOREIGN KEY(cash_movement_id) REFERENCES cash_movements(id),
                FOREIGN KEY(sale_id) REFERENCES sales(id),
                CHECK(fact_type IN ('Received','Refunded','Applied')),
                CHECK(amount_cents > 0),
                CHECK(LENGTH(TRIM(reference)) > 0),
                CHECK(LENGTH(payload_hash) = 64),
                CHECK(
                    (fact_type IN ('Received','Refunded')
                     AND cashier_session_id IS NOT NULL
                     AND cash_movement_id IS NOT NULL
                     AND sale_id IS NULL)
                    OR
                    (fact_type='Applied'
                     AND cashier_session_id IS NULL
                     AND cash_movement_id IS NULL
                     AND sale_id IS NOT NULL)
                )
            );

            CREATE TABLE cafe_booking_deposit_allocations (
                action_fact_id TEXT NOT NULL,
                received_fact_id TEXT NOT NULL,
                amount_cents INTEGER NOT NULL,
                PRIMARY KEY(action_fact_id, received_fact_id),
                FOREIGN KEY(action_fact_id)
                    REFERENCES cafe_booking_deposit_facts(deposit_fact_id),
                FOREIGN KEY(received_fact_id)
                    REFERENCES cafe_booking_deposit_facts(deposit_fact_id),
                CHECK(action_fact_id <> received_fact_id),
                CHECK(amount_cents > 0)
            );

            CREATE TABLE cafe_booking_order_links (
                booking_order_link_id TEXT PRIMARY KEY,
                booking_id TEXT NOT NULL UNIQUE,
                booking_revision_id TEXT NOT NULL,
                order_id TEXT NOT NULL UNIQUE,
                operation_id TEXT NOT NULL UNIQUE,
                actor_user_id TEXT NOT NULL,
                occurred_at_utc TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                FOREIGN KEY(booking_id) REFERENCES cafe_bookings(booking_id),
                FOREIGN KEY(booking_revision_id) REFERENCES cafe_booking_revisions(revision_id),
                FOREIGN KEY(order_id) REFERENCES cafe_orders(order_id),
                FOREIGN KEY(operation_id) REFERENCES cafe_booking_operations(operation_id),
                FOREIGN KEY(actor_user_id) REFERENCES users(id),
                CHECK(LENGTH(content_hash) = 64)
            );

            CREATE TABLE cafe_booking_sale_links (
                booking_sale_link_id TEXT PRIMARY KEY,
                booking_id TEXT NOT NULL UNIQUE,
                booking_revision_id TEXT NOT NULL,
                booking_order_link_id TEXT NOT NULL UNIQUE,
                order_id TEXT NOT NULL UNIQUE,
                sale_id TEXT NOT NULL UNIQUE,
                deposit_application_fact_id TEXT NULL UNIQUE,
                operation_id TEXT NOT NULL UNIQUE,
                actor_user_id TEXT NOT NULL,
                occurred_at_utc TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                FOREIGN KEY(booking_id) REFERENCES cafe_bookings(booking_id),
                FOREIGN KEY(booking_revision_id) REFERENCES cafe_booking_revisions(revision_id),
                FOREIGN KEY(booking_order_link_id)
                    REFERENCES cafe_booking_order_links(booking_order_link_id),
                FOREIGN KEY(order_id) REFERENCES cafe_orders(order_id),
                FOREIGN KEY(sale_id) REFERENCES sales(id),
                FOREIGN KEY(deposit_application_fact_id)
                    REFERENCES cafe_booking_deposit_facts(deposit_fact_id),
                FOREIGN KEY(operation_id) REFERENCES cafe_booking_operations(operation_id),
                FOREIGN KEY(actor_user_id) REFERENCES users(id),
                CHECK(LENGTH(content_hash) = 64)
            );

            CREATE TABLE cafe_booking_bootstrap_state (
                store_id TEXT PRIMARY KEY,
                central_instance_id TEXT NOT NULL,
                baseline_server_sequence INTEGER NOT NULL,
                snapshot_id TEXT NOT NULL,
                snapshot_hash TEXT NOT NULL,
                authority_digest TEXT NOT NULL,
                applied_at_utc TEXT NOT NULL,
                CHECK(baseline_server_sequence >= 0),
                CHECK(LENGTH(snapshot_hash) = 64),
                CHECK(LENGTH(authority_digest) = 64)
            );

            CREATE INDEX idx_cafe_bookings_store_status
                ON cafe_bookings(store_id,status,updated_at_utc,booking_id);
            CREATE INDEX idx_cafe_booking_revisions_booking
                ON cafe_booking_revisions(booking_id,booking_version,revision_number);
            CREATE INDEX idx_cafe_booking_revisions_customer_event
                ON cafe_booking_revisions(customer_id,event_start_utc,booking_id);
            CREATE INDEX idx_cafe_booking_operations_booking
                ON cafe_booking_operations(booking_id,resulting_version,operation_id);
            CREATE INDEX idx_cafe_booking_events_booking
                ON cafe_booking_events(booking_id,resulting_version,occurred_at_utc);
            CREATE INDEX idx_cafe_booking_deposit_facts_booking
                ON cafe_booking_deposit_facts(booking_id,fact_type,occurred_at_utc,deposit_fact_id);
            CREATE INDEX idx_cafe_booking_deposit_allocations_received
                ON cafe_booking_deposit_allocations(received_fact_id,action_fact_id);

            CREATE TRIGGER trg_cafe_bookings_terminal_guard
            BEFORE UPDATE ON cafe_bookings
            WHEN OLD.status IN ('Fulfilled','Cancelled')
            BEGIN
                SELECT RAISE(ABORT, 'terminal cafe booking is immutable');
            END;

            CREATE TRIGGER trg_cafe_bookings_version_guard
            BEFORE UPDATE ON cafe_bookings
            WHEN NEW.current_version <> OLD.current_version + 1
              OR NEW.booking_id <> OLD.booking_id
              OR NEW.store_id <> OLD.store_id
              OR NEW.created_by_user_id <> OLD.created_by_user_id
              OR NEW.created_at_utc <> OLD.created_at_utc
            BEGIN
                SELECT RAISE(
                    ABORT,
                    'cafe booking update must advance one version without rewriting identity');
            END;

            CREATE TRIGGER trg_cafe_bookings_no_delete
            BEFORE DELETE ON cafe_bookings
            BEGIN
                SELECT RAISE(ABORT, 'cafe booking cannot be deleted');
            END;
            """);

        foreach (string table in new[]
        {
            "cafe_booking_revisions",
            "cafe_booking_revision_lines",
            "cafe_booking_revision_modifier_selections",
            "cafe_booking_operations",
            "cafe_booking_events",
            "cafe_booking_deposit_facts",
            "cafe_booking_deposit_allocations",
            "cafe_booking_order_links",
            "cafe_booking_sale_links"
        })
        {
            Execute(
                connection,
                transaction,
                "CREATE TRIGGER trg_" + table +
                "_no_update BEFORE UPDATE ON " + table +
                " BEGIN SELECT RAISE(ABORT, '" +
                table + " is immutable'); END;");

            Execute(
                connection,
                transaction,
                "CREATE TRIGGER trg_" + table +
                "_no_delete BEFORE DELETE ON " + table +
                " BEGIN SELECT RAISE(ABORT, '" +
                table + " is immutable'); END;");
        }
    }

    private static void UpgradeSaleCashReceiptAuthority(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        Execute(connection, transaction, $"""
            DROP TRIGGER IF EXISTS trg_cash_movements_sale_receipt_shape;
            DROP TRIGGER IF EXISTS trg_payments_complete_sale_posts_cash_movement;
            DROP TRIGGER IF EXISTS trg_cafe_sale_financial_decision_posts_cash_movement;

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
                ) THEN RAISE(
                    ABORT,
                    'sale cash movement must match completed source sale cashier session and actor')
                END;
                SELECT CASE WHEN (
                    SELECT COALESCE(SUM(p.amount_cents),0)
                    FROM payments p
                    WHERE p.sale_id=NEW.reference_id
                ) + COALESCE((
                    SELECT d.booking_deposit_applied_cents
                    FROM cafe_sale_financial_decisions d
                    WHERE d.sale_id=NEW.reference_id
                    LIMIT 1
                ),0) <> (
                    SELECT s.amount_paid_cents
                    FROM sales s
                    WHERE s.id=NEW.reference_id
                ) THEN RAISE(
                    ABORT,
                    'sale cash receipt requires complete payment plus booking prepayment evidence')
                END;
                SELECT CASE WHEN NEW.delta_cents <> (
                    SELECT COALESCE(SUM(
                        CASE WHEN p.method = {Convert.ToInt32(PaymentMethod.Cash)}
                             THEN p.amount_cents ELSE 0 END),0)
                    FROM payments p
                    WHERE p.sale_id=NEW.reference_id
                ) - (
                    SELECT s.change_cents
                    FROM sales s
                    WHERE s.id=NEW.reference_id
                ) THEN RAISE(
                    ABORT,
                    'sale cash receipt delta must equal persisted retained current sale cash')
                END;
            END;

            CREATE TRIGGER trg_payments_complete_sale_posts_cash_movement
            AFTER INSERT ON payments
            BEGIN
                SELECT CASE WHEN EXISTS (
                    SELECT 1
                    FROM sales s
                    WHERE s.id=NEW.sale_id
                      AND s.cashier_session_id IS NOT NULL
                      AND (
                        (SELECT COALESCE(SUM(p.amount_cents),0)
                         FROM payments p
                         WHERE p.sale_id=s.id)
                        + COALESCE((
                            SELECT d.booking_deposit_applied_cents
                            FROM cafe_sale_financial_decisions d
                            WHERE d.sale_id=s.id
                            LIMIT 1
                          ),0)
                      ) = s.amount_paid_cents
                      AND (
                        (SELECT COALESCE(SUM(
                            CASE WHEN p.method={Convert.ToInt32(PaymentMethod.Cash)}
                                 THEN p.amount_cents ELSE 0 END),0)
                         FROM payments p
                         WHERE p.sale_id=s.id)
                        - s.change_cents
                      ) < 0
                ) THEN RAISE(ABORT, 'sale change cannot exceed current cash tender') END;

                INSERT INTO cash_movements(
                    id,cashier_session_id,actor_user_id,movement_type,delta_cents,
                    reference_type,reference_id,reversal_of_movement_id,reason,created_at_utc)
                SELECT
                    lower(hex(randomblob(16))),s.cashier_session_id,s.cashier_user_id,
                    {Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.SaleCashReceipt)},
                    (
                        SELECT COALESCE(SUM(
                            CASE WHEN p.method={Convert.ToInt32(PaymentMethod.Cash)}
                                 THEN p.amount_cents ELSE 0 END),0)
                        FROM payments p
                        WHERE p.sale_id=s.id
                    ) - s.change_cents,
                    'SALE',s.id,NULL,'POS checkout retained cash',s.created_at
                FROM sales s
                WHERE s.id=NEW.sale_id
                  AND s.status={Convert.ToInt32(SaleStatus.Completed)}
                  AND s.cashier_session_id IS NOT NULL
                  AND (
                    (SELECT COALESCE(SUM(p.amount_cents),0)
                     FROM payments p
                     WHERE p.sale_id=s.id)
                    + COALESCE((
                        SELECT d.booking_deposit_applied_cents
                        FROM cafe_sale_financial_decisions d
                        WHERE d.sale_id=s.id
                        LIMIT 1
                      ),0)
                  ) = s.amount_paid_cents
                  AND (
                    (SELECT COALESCE(SUM(
                        CASE WHEN p.method={Convert.ToInt32(PaymentMethod.Cash)}
                             THEN p.amount_cents ELSE 0 END),0)
                     FROM payments p
                     WHERE p.sale_id=s.id)
                    - s.change_cents
                  ) > 0
                  AND NOT EXISTS(
                    SELECT 1
                    FROM cash_movements cm
                    WHERE cm.movement_type={Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.SaleCashReceipt)}
                      AND cm.reference_id=s.id
                  );
            END;

            CREATE TRIGGER trg_cafe_sale_financial_decision_posts_cash_movement
            AFTER INSERT ON cafe_sale_financial_decisions
            BEGIN
                SELECT CASE WHEN EXISTS (
                    SELECT 1
                    FROM sales s
                    WHERE s.id=NEW.sale_id
                      AND s.cashier_session_id IS NOT NULL
                      AND (
                        (SELECT COALESCE(SUM(p.amount_cents),0)
                         FROM payments p
                         WHERE p.sale_id=s.id)
                        + NEW.booking_deposit_applied_cents
                      ) = s.amount_paid_cents
                      AND (
                        (SELECT COALESCE(SUM(
                            CASE WHEN p.method={Convert.ToInt32(PaymentMethod.Cash)}
                                 THEN p.amount_cents ELSE 0 END),0)
                         FROM payments p
                         WHERE p.sale_id=s.id)
                        - s.change_cents
                      ) < 0
                ) THEN RAISE(ABORT, 'sale change cannot exceed current cash tender') END;

                INSERT INTO cash_movements(
                    id,cashier_session_id,actor_user_id,movement_type,delta_cents,
                    reference_type,reference_id,reversal_of_movement_id,reason,created_at_utc)
                SELECT
                    lower(hex(randomblob(16))),s.cashier_session_id,s.cashier_user_id,
                    {Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.SaleCashReceipt)},
                    (
                        SELECT COALESCE(SUM(
                            CASE WHEN p.method={Convert.ToInt32(PaymentMethod.Cash)}
                                 THEN p.amount_cents ELSE 0 END),0)
                        FROM payments p
                        WHERE p.sale_id=s.id
                    ) - s.change_cents,
                    'SALE',s.id,NULL,'POS checkout retained cash',s.created_at
                FROM sales s
                WHERE s.id=NEW.sale_id
                  AND s.status={Convert.ToInt32(SaleStatus.Completed)}
                  AND s.cashier_session_id IS NOT NULL
                  AND (
                    (SELECT COALESCE(SUM(p.amount_cents),0)
                     FROM payments p
                     WHERE p.sale_id=s.id)
                    + NEW.booking_deposit_applied_cents
                  ) = s.amount_paid_cents
                  AND (
                    (SELECT COALESCE(SUM(
                        CASE WHEN p.method={Convert.ToInt32(PaymentMethod.Cash)}
                             THEN p.amount_cents ELSE 0 END),0)
                     FROM payments p
                     WHERE p.sale_id=s.id)
                    - s.change_cents
                  ) > 0
                  AND NOT EXISTS(
                    SELECT 1
                    FROM cash_movements cm
                    WHERE cm.movement_type={Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.SaleCashReceipt)}
                      AND cm.reference_id=s.id
                  );
            END;
            """);
    }

    private static void AddReservedBookingCashAuthority(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        Execute(connection, transaction, $"""
            CREATE UNIQUE INDEX idx_cash_movements_booking_deposit_source
                ON cash_movements(
                    UPPER(TRIM(reference_type)),
                    reference_id)
                WHERE UPPER(TRIM(reference_type)) IN (
                    '{CafeMigrationSqlCodes.CafeBookingCashReferenceTypes.DepositReceipt}',
                    '{CafeMigrationSqlCodes.CafeBookingCashReferenceTypes.DepositRefund}');

            CREATE TRIGGER trg_cash_movements_booking_deposit_shape
            BEFORE INSERT ON cash_movements
            WHEN UPPER(TRIM(NEW.reference_type)) IN (
                '{CafeMigrationSqlCodes.CafeBookingCashReferenceTypes.DepositReceipt}',
                '{CafeMigrationSqlCodes.CafeBookingCashReferenceTypes.DepositRefund}')
            BEGIN
                SELECT CASE
                    WHEN UPPER(TRIM(NEW.reference_type)) =
                            '{CafeMigrationSqlCodes.CafeBookingCashReferenceTypes.DepositReceipt}'
                     AND NEW.movement_type <>
                            {Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.CashIn)}
                    THEN RAISE(
                        ABORT,
                        'booking deposit receipt cash source requires CashIn')
                END;
                SELECT CASE
                    WHEN UPPER(TRIM(NEW.reference_type)) =
                            '{CafeMigrationSqlCodes.CafeBookingCashReferenceTypes.DepositRefund}'
                     AND NEW.movement_type <>
                            {Convert.ToInt32(CafeMigrationSqlCodes.CashMovementType.CashOut)}
                    THEN RAISE(
                        ABORT,
                        'booking deposit refund cash source requires CashOut')
                END;
                SELECT CASE
                    WHEN NEW.reversal_of_movement_id IS NOT NULL
                    THEN RAISE(
                        ABORT,
                        'booking deposit cash source cannot be a generic cash reversal')
                END;
            END;
            """);
    }

    private static void AssertCountEqual(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string source,
        string target)
    {
        long sourceCount = ScalarLong(
            connection,
            transaction,
            "SELECT COUNT(*) FROM " + source + ";");
        long targetCount = ScalarLong(
            connection,
            transaction,
            "SELECT COUNT(*) FROM " + target + ";");

        if (sourceCount != targetCount)
        {
            throw new InvalidOperationException(
                "C9 migration 38 financial cluster row-count preservation failed: " +
                source + "=" + sourceCount + ", " +
                target + "=" + targetCount + ".");
        }
    }

    private static void AssertNoRows(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        string message)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        using SqliteDataReader reader = command.ExecuteReader();

        if (reader.Read())
            throw new InvalidOperationException(message);
    }

    private static void AssertForeignKeysClean(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA foreign_key_check;";
        using SqliteDataReader reader = command.ExecuteReader();

        if (reader.Read())
        {
            throw new InvalidOperationException(
                "C9 migration 38 completed with foreign-key violations.");
        }
    }

    private static long ScalarLong(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L);
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
