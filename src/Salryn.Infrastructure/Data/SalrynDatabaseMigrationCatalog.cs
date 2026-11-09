namespace Salryn.Infrastructure.Data;

internal static class SalrynDatabaseMigrationCatalog
{
    public const int SupportedMigrationVersion = 29;
    // SupportedMigrationVersion = 26
    // SupportedMigrationVersion = 24

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
        (25, "cash_drawer_events"),
        (25, "cash_drawer_reconciliations"),
        (25, "cash_drawer_reviews"),
        (28, "lpi_document_sequences"),
        (28, "inventory_locations"),
        (28, "product_stock_buckets"),
        (28, "purchase_requisitions"),
        (28, "purchase_orders"),
        (28, "receiving_reports"),
        (28, "supplier_invoices"),
        (28, "supplier_payable_entries"),
        (28, "supplier_payments"),
        (29, "lpi_overdelivery_approvals"),
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
