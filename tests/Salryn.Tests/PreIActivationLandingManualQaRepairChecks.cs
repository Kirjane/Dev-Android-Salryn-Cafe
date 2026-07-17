using Microsoft.Data.Sqlite;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Migrations;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

/// <summary>
/// PRE-I: activation landing, terms-gated import, store logo migration repair, backup modals, nav order, LAN responsive.
/// </summary>
public static class PreIActivationLandingManualQaRepairChecks
{
    public static void Run()
    {
        string root = FindRoot();
        string licensePage = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "LicensePage.tsx"));
        string terminalPage = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "TerminalActivationPage.tsx"));
        string credits = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "components", "ProductCreditsCard.tsx"));
        string backupPage = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "BackupRestorePage.tsx"));
        string navigation = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "navigation.ts"));
        string appShell = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "components", "AppShell.tsx"));
        string terminalShell = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "components", "TerminalShell.tsx"));
        string hostLan = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "HostLanControlPage.tsx"));
        string orchestrator = File.ReadAllText(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "Initialization", "DatabaseInitializationOrchestrator.cs"));
        string logoMigration = File.ReadAllText(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "Migrations", "StoreLogoBrandingSchemaMigration.cs"));

        // Host landing — minimalist rebuild (see pre-i-license-page-minimalist-rebuild)
        RequireContains(licensePage, "Salryn", "Host landing wordmark.");
        RequireContains(licensePage, "by Kirjane Labs", "Host landing Kirjane Labs.");
        RequireContains(licensePage, "Activate Salryn", "Primary Activate Salryn title.");
        RequireContains(licensePage, "Import license", "Primary import CTA.");
        RequireContains(licensePage, "Machine Code", "Wizard step 1 Machine Code.");
        RequireContains(licensePage, "Copy Machine Code", "Host machine code copy.");
        RequireContains(licensePage, "Beta License Terms", "Terms step present.");
        RequireContains(licensePage, "I understand and accept these beta license terms.", "Terms checkbox.");
        RequireContains(licensePage, "termsAccepted", "Import gated on termsAccepted.");
        RequireContains(licensePage, "Import Signed Host License JSON", "Import step title.");
        RequireContains(licensePage, "Status: Active", "Active status.");
        RequireContains(licensePage, "Salryn is activated", "Active title.");
        RequireContains(licensePage, "Host license active", "Success wording.");
        RequireContains(licensePage, "Show technical details", "Technical details collapsed.");
        RequireNotContains(licensePage, "What Salryn offers", "No marketing feature wall.");
        RequireNotContains(licensePage, "Products of Kirjane Labs", "No products metadata block.");
        RequireNotContains(licensePage, "mock sample", "No mock sample.");
        RequireNotContains(licensePage, "cloud activation ready", "No cloud activation claim.");

        // Terminal landing
        RequireContains(terminalPage, "SALRYN Terminal", "Terminal landing hero.");
        RequireContains(terminalPage, "Activate this Terminal", "Terminal primary CTA.");
        RequireContains(terminalPage, "Activate this LAN terminal", "Terminal wizard step 1.");
        RequireContains(terminalPage, "Copy Terminal Machine Code", "Terminal machine code copy.");
        RequireContains(terminalPage, "do not need a Host License ID", "No Host License ID task.");
        RequireContains(terminalPage, "Terminal Activation Terms", "Terminal terms.");
        RequireContains(terminalPage, "termsAccepted", "Terminal import gated on terms.");
        RequireContains(terminalPage, "Import Signed Terminal License JSON", "Terminal import.");
        RequireContains(terminalPage, "Host licenses cannot activate a terminal", "Host license rejected.");
        RequireContains(terminalPage, "ProductCreditsCard", "Terminal uses credits card.");
        RequireNotContains(terminalPage, "Products of Kirjane Labs", "Terminal no products metadata.");
        RequireNotContains(terminalPage, "you need a Host License ID", "No Host License ID burden.");

        // Credits
        RequireContains(credits, "SALRYN", "Credits wordmark.");
        RequireContains(credits, "by Kirjane Labs", "Credits byline.");
        RequireContains(credits, "Licensing & Credits", "Credits title.");
        RequireContains(credits, "Paul Gamayot", "Business Lead.");
        RequireContains(credits, "Kirch Ivan Balite", "Technology Lead.");
        RequireContains(credits, "Jesse Edwin Culas", "Marketing Lead.");
        RequireContains(credits, "James Karl Tara", "Business Development.");
        RequireContains(credits, "Osiris Kedigadash Palac", "Dev Ops Lead.");
        RequireNotContains(credits, "Products of Kirjane Labs", "Credits no products list.");
        RequireNotContains(credits, "Build Type", "Credits no Build Type card.");

        // Backup modals
        RequireContains(backupPage, "Backup created successfully", "Create backup success modal.");
        RequireContains(backupPage, "Backup verified and restorable", "Verify success modal.");
        RequireContains(backupPage, "Restore completed", "Restore success modal.");
        RequireContains(backupPage, "Type RESTORE SALRYN BACKUP to enable restore", "Restore confirmation phrase must match backend.");
        RequireContains(backupPage, "Restore replaces the current local database", "Restore safety copy.");
        RequireContains(backupPage, "Dialog", "Backup uses Dialog components.");

        // Nav order: Dashboard then Reports
        int dash = navigation.IndexOf("id: \"dashboard\"", StringComparison.Ordinal);
        int reports = navigation.IndexOf("id: \"owner-reports\"", StringComparison.Ordinal);
        int products = navigation.IndexOf("id: \"products\"", StringComparison.Ordinal);
        RequireTrue(dash >= 0 && reports > dash && products > reports, "Reports must follow Dashboard before Product Catalog.");

        // Responsive shells
        RequireNotContains(appShell, "min-w-[48rem]", "AppShell must not force desktop min-width.");
        RequireNotContains(terminalShell, "min-w-[48rem]", "TerminalShell must not force desktop min-width.");
        RequireContains(appShell, "min-w-0", "AppShell allows flex shrink.");
        RequireContains(terminalShell, "min-w-0", "TerminalShell allows flex shrink.");

        // Terminal Management product copy
        RequireContains(hostLan, "Terminal licenses are issued from the Host license context", "Host-context issuance.");
        RequireContains(hostLan, "Copy this request for LicensingOps", "LicensingOps copy wording.");
        RequireContains(hostLan, "Each LAN terminal needs its own Terminal license", "One license per terminal.");

        // Migration repair
        RequireContains(logoMigration, "EnsureLogoColumnsPresent", "Idempotent logo column repair.");
        RequireContains(orchestrator, "EnsureStoreLogoColumns", "Startup ensures logo columns.");
        RequireContains(logoMigration, "PRAGMA table_info", "Column existence check.");

        RunLogoMigrationRepairProof();
    }

    private static void RunLogoMigrationRepairProof()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "salryn-pre-i-logo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            // Simulate a v19-ish DB without logo columns: init then strip columns by recreating store_settings without them is hard;
            // instead create empty DB and run through init — then remove columns from pragma via new connection after fake missing.
            DbPaths paths = DbPaths.CreateDevelopment(tempRoot);
            paths.EnsureDirectories();
            var orchestrator = DefaultDatabaseInitializationFactory.Create(paths);
            var init = orchestrator.InitializeDatabase();
            RequireTrue(init.IsSuccess, "Init must succeed: " + (init.IsFailure ? init.ErrorDetail.Message : ""));

            using (var connection = new SqliteConnection("Data Source=" + paths.DatabasePath))
            {
                connection.Open();
                RequireTrue(ColumnExists(connection, "logo_content_type"), "logo_content_type after init.");
                RequireTrue(ColumnExists(connection, "logo_bytes"), "logo_bytes after init.");
            }

            // Repair path: call EnsureLogoColumnsPresent when columns already exist (idempotent).
            using (var connection = new SqliteConnection("Data Source=" + paths.DatabasePath))
            {
                connection.Open();
                StoreLogoBrandingSchemaMigration.EnsureLogoColumnsPresent(connection);
                RequireTrue(ColumnExists(connection, "logo_content_type"), "logo columns remain after repair.");
            }

            // Simulate missing columns on a store_settings table that already has a row (legacy DB).
            string legacyRoot = Path.Combine(tempRoot, "legacy");
            Directory.CreateDirectory(legacyRoot);
            string legacyDb = Path.Combine(legacyRoot, "legacy.db");
            using (var connection = new SqliteConnection("Data Source=" + legacyDb))
            {
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE store_settings (
                      id TEXT PRIMARY KEY,
                      store_name TEXT NOT NULL,
                      currency_label TEXT NOT NULL,
                      receipt_width_mm INTEGER NOT NULL,
                      tax_mode_enabled INTEGER NOT NULL,
                      shift_mode_enabled INTEGER NOT NULL,
                      allow_negative_stock INTEGER NOT NULL,
                      printer_mode TEXT NOT NULL,
                      created_at TEXT NOT NULL,
                      updated_at TEXT NOT NULL
                    );
                    INSERT INTO store_settings (id, store_name, currency_label, receipt_width_mm, tax_mode_enabled, shift_mode_enabled, allow_negative_stock, printer_mode, created_at, updated_at)
                    VALUES ('00000000-0000-0000-0000-000000000001', 'Legacy Store', 'PHP', 58, 0, 1, 0, 'Disabled', '2020-01-01T00:00:00Z', '2020-01-01T00:00:00Z');
                    """;
                cmd.ExecuteNonQuery();
                RequireTrue(!ColumnExists(connection, "logo_content_type"), "legacy lacks logo column.");
                StoreLogoBrandingSchemaMigration.EnsureLogoColumnsPresent(connection);
                RequireTrue(ColumnExists(connection, "logo_content_type"), "repair adds logo_content_type.");
                RequireTrue(ColumnExists(connection, "logo_bytes"), "repair adds logo_bytes.");
            }
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { /* best effort */ }
        }
    }

    private static bool ColumnExists(SqliteConnection connection, string columnName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(store_settings);";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string FindRoot()
    {
        DirectoryInfo? current = new(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln"))) return current.FullName;
            current = current.Parent;
        }
        throw new InvalidOperationException("Could not locate Salryn.sln.");
    }

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
            throw new InvalidOperationException(message + " Missing: " + value);
    }

    private static void RequireNotContains(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(message + " Forbidden: " + value);
    }

    private static void RequireTrue(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
