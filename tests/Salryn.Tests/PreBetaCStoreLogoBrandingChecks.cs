using Microsoft.Data.Sqlite;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

/// <summary>
/// PRE-BETA-C: store logo branding migration, upload validation rules, branding endpoints, shell consumption.
/// </summary>
public static class PreBetaCStoreLogoBrandingChecks
{
    public static void Run()
    {
        string root = FindRoot();
        string catalog = File.ReadAllText(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "SalrynDatabaseMigrationCatalog.cs"));
        string factory = File.ReadAllText(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "Initialization", "DefaultDatabaseInitializationFactory.cs"));
        string brandingEndpoints = File.ReadAllText(Path.Combine(root, "src", "Salryn.Api.Local", "LocalStoreBrandingEndpoints.cs"));
        string remoteEndpoints = File.ReadAllText(Path.Combine(root, "src", "Salryn.Api.Local", "RemotePos", "RemotePosApiEndpoints.cs"));
        string storePage = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "StoreSettingsPage.tsx"));
        string appShell = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "components", "AppShell.tsx"));
        string terminalShell = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "components", "TerminalShell.tsx"));
        string migration = File.ReadAllText(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "Migrations", "StoreLogoBrandingSchemaMigration.cs"));

        RequireContains(catalog, "SupportedMigrationVersion = 22", "Catalog must include the U1 accounts receivable migration.");
        RequireContains(factory, "StoreLogoBrandingSchemaMigration", "Factory must register store logo migration.");
        RequireContains(migration, "logo_content_type", "Migration must add logo_content_type.");
        RequireContains(migration, "logo_bytes", "Migration must add logo_bytes.");
        RequireContains(brandingEndpoints, "MaxLogoBytes = 512 * 1024", "Logo max size must be 512 KB.");
        RequireContains(brandingEndpoints, "image/png", "PNG must be allowed.");
        RequireContains(brandingEndpoints, "image/jpeg", "JPEG must be allowed.");
        RequireContains(brandingEndpoints, "image/webp", "WebP must be allowed.");
        RequireContains(brandingEndpoints, "SVG is not accepted", "SVG must be rejected.");
        RequireContains(brandingEndpoints, "/api/branding/store", "Host branding endpoint required.");
        RequireContains(brandingEndpoints, "/api/settings/store/logo", "Logo upload endpoint required.");
        RequireContains(remoteEndpoints, "/branding/store", "Remote POS branding endpoint required.");
        RequireContains(storePage, "Upload logo", "Store settings must expose logo upload.");
        RequireContains(storePage, "Remove logo", "Store settings must expose logo remove.");
        RequireContains(appShell, "fetchStoreBranding", "Host AppShell must read branding.");
        RequireContains(appShell, "logoUrl", "Host AppShell must render logo when present.");
        RequireContains(terminalShell, "logoUrl", "TerminalShell must accept logo branding.");

        string tempRoot = Path.Combine(Path.GetTempPath(), "salryn-logo-c-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            DbPaths paths = DbPaths.CreateDevelopment(tempRoot);
            paths.EnsureDirectories();
            var init = DefaultDatabaseInitializationFactory.Create(paths).InitializeDatabase();
            RequireTrue(init.IsSuccess, "DB init must succeed: " + (init.IsFailure ? init.ErrorDetail.Message : ""));
            new Salryn.Infrastructure.Data.Seeding.DatabaseSeeder(new SqliteConnectionFactory(paths)).SeedDefaultData();

            using var connection = new SqliteConnection("Data Source=" + paths.DatabasePath);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(MAX(version),0) FROM schema_migrations;";
            int max = Convert.ToInt32(cmd.ExecuteScalar());
            RequireTrue(max == 21, "DB must migrate to version 21.");

            HashSet<string> cols = new(StringComparer.OrdinalIgnoreCase);
            cmd.CommandText = "PRAGMA table_info(store_settings);";
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    cols.Add(reader.GetString(1));
                }
            }

            RequireTrue(cols.Contains("logo_content_type"), "store_settings must have logo_content_type.");
            RequireTrue(cols.Contains("logo_bytes"), "store_settings must have logo_bytes.");

            var factoryRepos = new SqliteRepositoryFactory(new SqliteConnectionFactory(paths));
            var writer = factoryRepos.CreateStoreSettingsWriteRepository();
            var readerRepo = factoryRepos.CreateStoreSettingsReadRepository();
            var current = readerRepo.GetCurrentAsync().GetAwaiter().GetResult();
            if (!current.IsSuccess || current.Value is null)
            {
                // Seed fallback if store_settings empty after init.
                using var insert = connection.CreateCommand();
                insert.CommandText = """
                    INSERT INTO store_settings (id, store_name, currency_label, receipt_width_mm, tax_mode_enabled, shift_mode_enabled, allow_negative_stock, printer_mode, created_at, updated_at)
                    VALUES ($id, 'Logo Test Store', 'PHP', 58, 0, 1, 0, 'Disabled', $ts, $ts);
                    """;
                insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
                insert.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToString("O"));
                insert.ExecuteNonQuery();
                current = readerRepo.GetCurrentAsync().GetAwaiter().GetResult();
            }
            RequireTrue(current.IsSuccess && current.Value is not null, "Store settings row must exist.");

            byte[] png = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
            var set = writer.SetLogoAsync(current.Value!.Id, "image/png", png).GetAwaiter().GetResult();
            RequireTrue(set.IsSuccess, "Valid PNG logo upload must succeed.");
            var logo = readerRepo.GetLogoAsync().GetAwaiter().GetResult();
            RequireTrue(logo.IsSuccess && logo.Value is not null && logo.Value.Bytes.Length == png.Length, "Logo must round-trip.");
            var after = readerRepo.GetCurrentAsync().GetAwaiter().GetResult();
            RequireTrue(after.Value!.HasLogo, "HasLogo must be true after set.");

            var clear = writer.ClearLogoAsync(current.Value.Id).GetAwaiter().GetResult();
            RequireTrue(clear.IsSuccess, "Clear logo must succeed.");
            var cleared = readerRepo.GetLogoAsync().GetAwaiter().GetResult();
            RequireTrue(cleared.IsSuccess && cleared.Value is null, "Logo must be null after clear.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { /* best effort */ }
        }
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

    private static void RequireContains(string source, string value, string message)
    {
        if (!source.Contains(value, StringComparison.Ordinal)) throw new InvalidOperationException(message + " Missing: " + value);
    }

    private static void RequireTrue(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
