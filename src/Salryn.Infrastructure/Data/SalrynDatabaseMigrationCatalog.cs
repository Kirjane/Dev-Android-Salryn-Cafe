namespace Salryn.Infrastructure.Data;

public static class SalrynDatabaseMigrationCatalog
{
    public const int SupportedMigrationVersion = Migrations.CafeMigrationCatalog.CurrentCafeSchemaVersion;

    private static readonly (int Version, string Table)[] CriticalTablesByIntroducingVersion =
    {
        (1, "roles"),
        (1, "users"),
        (1, "store_settings"),
        (1, "products"),
        (1, "sales"),
        (1, "sale_items"),
        (1, "payments"),
        (1, "stock_movements"),
        (1, "cashier_sessions"),
        (1, "receipt_sequences"),
        (1, "audit_logs"),
        (1, "error_logs"),
        (1, "backup_logs"),
        (2, "bir_machine_profiles"),
        (3, "bir_official_document_sequences"),
        (4, "bir_electronic_journal_entries"),
        (4, "bir_electronic_journal_preservation_batches"),
        (4, "bir_electronic_journal_entry_events"),
        (6, "bir_z_readings"),
        (9, "deployment_identity"),
        (10, "salryn_terminal_registrations"),
        (10, "salryn_terminal_pairing_invites"),
        (11, "salryn_lan_transport_config"),
        (12, "salryn_terminal_credentials"),
        (12, "salryn_remote_client_handshakes"),
        (13, "official_receipt_profiles"),
        (14, "official_dry_run_packages"),
        (16, "official_document_lifecycle_state"),
        (16, "official_document_lifecycle_idempotency"),
        (17, "official_document_lifecycle_operations"),
        (17, "official_document_lifecycle_settlements"),
        (17, "official_document_adjustment_documents"),
        (21, "remote_pos_checkout_requests"),
        (22, "customers"),
        (22, "accounts_receivable_entries"),
        (22, "accounts_receivable_collections"),
        (25, "cashier_session_reports"),
        (26, "cash_movements"),
        (28, "sync_device_state"),
        (28, "sync_outbox"),
        (29, "sync_remote_state"),
        (29, "sync_change_inbox"),
        (30, "menu_item_availability"),
        (30, "menu_variants"),
        (30, "menu_modifier_groups"),
        (30, "menu_modifier_options"),
        (30, "menu_item_modifier_groups"),
        (30, "menu_variant_modifier_groups"),
        (30, "menu_revisions"),
        (30, "menu_revision_categories"),
        (30, "menu_revision_items"),
        (30, "menu_revision_variants"),
        (30, "menu_revision_modifier_groups"),
        (30, "menu_revision_modifier_options"),
        (30, "menu_revision_item_modifier_groups"),
        (30, "menu_revision_variant_modifier_groups"),
        (30, "menu_current_publication"),
        (31, "ingredients"),
        (31, "recipes"),
        (31, "recipe_lines"),
        (31, "recipe_item_bindings"),
        (31, "recipe_variant_bindings"),
        (31, "recipe_modifier_option_bindings"),
        (31, "recipe_book_revisions"),
        (31, "recipe_book_revision_ingredients"),
        (31, "recipe_book_revision_recipes"),
        (31, "recipe_book_revision_lines"),
        (31, "recipe_book_revision_bindings"),
        (31, "recipe_book_current_publication"),
        (31, "sale_ingredient_consumption_decisions"),
        (31, "sale_ingredient_consumption_lines"),
        (31, "ingredient_stock_movements"),
        (31, "cafe_ingredient_bootstrap_state"),
        (32, "supplier_directory_revisions"),
        (32, "supplier_directory_revision_items"),
        (32, "supplier_directory_current_publication"),
        (32, "purchase_receipts"),
        (32, "purchase_receipt_lines"),
        (32, "inventory_waste_events"),
        (32, "inventory_waste_lines"),
        (32, "inventory_adjustment_events"),
        (32, "inventory_count_sessions"),
        (32, "inventory_count_lines"),
        (32, "cafe_inventory_bootstrap_state"),
        (33, "cafe_orders"),
        (33, "cafe_order_revisions"),
        (33, "cafe_order_revision_lines"),
        (33, "cafe_order_revision_modifiers"),
        (33, "cafe_order_operations"),
        (33, "cafe_order_lifecycle_events"),
        (33, "cafe_order_sale_links"),
        (33, "sale_item_order_links"),
        (33, "cafe_order_bootstrap_state"),
        (34, "cafe_sale_financial_decisions"),
        (34, "cafe_sale_financial_line_discounts"),
        (34, "cafe_sale_fees"),
        (34, "cafe_sale_refunds"),
        (34, "cafe_sale_refund_lines"),
        (34, "cafe_sale_refund_operations"),
        (34, "cafe_sale_refund_events"),
        (34, "cafe_sale_refund_settlements"),
        (34, "cafe_financial_bootstrap_state"),
        (35, "cafe_cash_operations"),
        (35, "cafe_cash_bootstrap_state"),
        (36, "cafe_printer_stations"),
        (36, "cafe_print_jobs"),
        (36, "cafe_print_attempts"),
        (37, "cafe_delivery_fulfillments"),
        (37, "cafe_delivery_revisions"),
        (37, "cafe_delivery_operations"),
        (37, "cafe_delivery_events"),
        (37, "cafe_delivery_bootstrap_state"),
        (38, "cafe_bookings"),
        (38, "cafe_booking_revisions"),
        (38, "cafe_booking_revision_lines"),
        (38, "cafe_booking_revision_modifier_selections"),
        (38, "cafe_booking_operations"),
        (38, "cafe_booking_events"),
        (38, "cafe_booking_deposit_facts"),
        (38, "cafe_booking_deposit_allocations"),
        (38, "cafe_booking_order_links"),
        (38, "cafe_booking_sale_links"),
        (38, "cafe_booking_bootstrap_state"),
        (39, "cafe_attendance_events"),
        (39, "cafe_attendance_corrections"),
        (39, "cafe_incidents"),
        (39, "cafe_incident_revisions"),
        (39, "cafe_incident_operations"),
        (39, "cafe_incident_resolutions"),
        (39, "cafe_attendance_incident_bootstrap_state"),
        (40, "salryn_schema_lineage"),
        (40, "host_checkout_requests"),
        (40, "cash_drawer_events"),
        (40, "cash_drawer_reconciliations"),
        (40, "cash_drawer_reviews"),
        (41, "cafe_mutation_idempotency"),
        (41, "sync_device_trust_state"),
    };

    private static readonly string[] LegacyCoreTables =
    {
        "roles",
        "users",
        "store_settings",
        "products",
        "sales",
    };

    public static IReadOnlyList<string> LegacyCoreTableList => LegacyCoreTables;

    public static IReadOnlyList<string> GetCriticalTablesForMigrationVersion(int migrationVersion)
    {
        if (migrationVersion <= 0)
        {
            return Array.Empty<string>();
        }

        return CriticalTablesByIntroducingVersion
            .Where(entry => entry.Version <= migrationVersion)
            .Select(entry => entry.Table)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(table => table, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<string> GetMissingCriticalTables(
        int backupMigrationVersion,
        IReadOnlySet<string> existingTables)
    {
        List<string> missing = new();

        foreach (string table in GetCriticalTablesForMigrationVersion(backupMigrationVersion))
        {
            if (!existingTables.Contains(table))
            {
                missing.Add(table);
            }
        }

        return missing;
    }

    public static bool LegacyCoreTablesExist(IReadOnlySet<string> existingTables) =>
        LegacyCoreTables.All(existingTables.Contains);
}
