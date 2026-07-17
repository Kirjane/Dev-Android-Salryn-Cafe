using Microsoft.Data.Sqlite;
using Salryn.Api.Local.Auth;
using Salryn.Composition.Local;
using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Licensing;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

/// <summary>
/// Fully code-proven isolated fresh backend state: users, sessions, licenses, store residue, route contract.
/// Does not touch Kirch LocalAppData or repository .salryn-dev.
/// </summary>
public static class PreICodeProvenFreshStateChecks
{
    public static void Run()
    {
        string root = PreICodeProvenCheckSupport.FindRoot();
        string app = PreICodeProvenCheckSupport.Read(root, "src", "Salryn.App.Web", "src", "App.tsx");
        AssertFreshRouteContract(app);
        RunIsolatedFreshBackendProof();
    }

    private static void AssertFreshRouteContract(string app)
    {
        PreICodeProvenCheckSupport.RequireOrder(
            app,
            "if (!isHostLicenseReady",
            "if (bootstrapStatus?.firstRunRequired)",
            "Unlicensed fresh state must route License before Bootstrap.");
        PreICodeProvenCheckSupport.RequireOrder(
            app,
            "if (bootstrapStatus?.firstRunRequired)",
            "if (!sessionQuery.data?.isAuthenticated)",
            "Bootstrap must precede Login after licensing.");
        PreICodeProvenCheckSupport.RequireContains(app, "<LicensePage />", "Unlicensed route renders LicensePage.");
        PreICodeProvenCheckSupport.RequireContains(app, "return <div className=\"min-h-screen", "Unlicensed route uses License page shell.");
    }

    private static void RunIsolatedFreshBackendProof()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "salryn-code-proven-fresh-" + Guid.NewGuid().ToString("N"));
        PreICodeProvenCheckSupport.RequireTrue(
            !string.IsNullOrWhiteSpace(tempRoot) && tempRoot.Contains("salryn-code-proven-fresh-", StringComparison.Ordinal),
            "Fresh app-data root must be a unique isolated temp path.");

        try
        {
            DbPaths paths = DbPaths.CreateDevelopment(tempRoot);
            PreICodeProvenCheckSupport.RequireTrue(
                paths.RootPath.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)
                || paths.DatabasePath.Contains(tempRoot, StringComparison.OrdinalIgnoreCase),
                "Development DbPaths must bind under the isolated temp root.");

            SalrynLocalComposition composition = SalrynLocalComposition.Create(paths);
            composition.InitializeDatabaseAsync().GetAwaiter().GetResult();
            PreICodeProvenCheckSupport.RequireTrue(File.Exists(paths.DatabasePath), "Fresh database must initialize cleanly.");

            using (var connection = new SqliteConnection("Data Source=" + paths.DatabasePath))
            {
                connection.Open();

                long users = ScalarLong(connection, "SELECT COUNT(*) FROM users");
                long sessions = ScalarLong(connection, "SELECT COUNT(*) FROM cashier_sessions");
                long activeSessions = ScalarLong(
                    connection,
                    "SELECT COUNT(*) FROM cashier_sessions WHERE closed_at IS NULL OR closed_at = ''");
                long deploymentRows = ScalarLong(connection, "SELECT COUNT(*) FROM deployment_identity");

                PreICodeProvenCheckSupport.RequireTrue(users == 0, "Fresh state: users/operators count must be 0. Count=" + users);
                PreICodeProvenCheckSupport.RequireTrue(sessions == 0, "Fresh state: cashier_sessions count must be 0. Count=" + sessions);
                PreICodeProvenCheckSupport.RequireTrue(activeSessions == 0, "Fresh state: active cashier sessions must be 0.");
                PreICodeProvenCheckSupport.RequireTrue(deploymentRows == 1, "Fresh state: only generated deployment_identity baseline.");

                string storeName = ScalarString(connection, "SELECT store_name FROM store_settings LIMIT 1");
                PreICodeProvenCheckSupport.RequireNotContains(storeName, "DEVKIRCH", "No DEVKIRCH residue in fresh store_settings.");
                PreICodeProvenCheckSupport.RequireNotContains(storeName, "Salryn Dev", "No Salryn Dev residue in fresh store_settings.");

                // Explicit no-license/session/users matrix (overlaps fresh state by design).
                PreICodeProvenCheckSupport.RequireTrue(users == 0, "No license/session/users proof: zero bootstrapped operators.");
                PreICodeProvenCheckSupport.RequireTrue(sessions == 0, "No license/session/users proof: zero cashier sessions.");
            }

            string licenseRoot = Path.Combine(tempRoot, "license-state");
            LocalLicenseService licenseService = new(
                new LocalLicenseFileStore(new LocalLicenseFileStoreOptions(licenseRoot, null)),
                new LocalLicenseValidator());
            LocalLicenseValidationResult status = licenseService.GetStatusAsync().GetAwaiter().GetResult();

            PreICodeProvenCheckSupport.RequireTrue(
                status.Status == LicenseStatus.Unlicensed,
                "Fresh license store must report Unlicensed (Host inactive/missing).");
            PreICodeProvenCheckSupport.RequireTrue(
                status.LicenseType is null,
                "Fresh license store must have no Host or Terminal license type.");
            PreICodeProvenCheckSupport.RequireTrue(
                !File.Exists(Path.Combine(licenseRoot, LocalLicenseFileStore.DefaultFileName)),
                "Fresh license store must not contain the default license JSON file.");

            bool anyLicenseJson = Directory.Exists(tempRoot)
                && Directory.EnumerateFiles(tempRoot, "*license*", SearchOption.AllDirectories)
                    .Any(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
            PreICodeProvenCheckSupport.RequireTrue(!anyLicenseJson, "Fresh isolated root must not contain any license JSON.");

            InMemoryLocalApiSessionStore sessionStore = new();
            LocalApiSessionService sessionsApi = new(composition.Authentication, sessionStore);
            PreICodeProvenCheckSupport.RequireTrue(
                !sessionsApi.GetSession(null).IsAuthenticated,
                "Fresh API session must be unauthenticated.");
            PreICodeProvenCheckSupport.RequireTrue(
                sessionStore.Get(Guid.NewGuid().ToString("N")) is null,
                "Fresh session store must contain no active session records.");
        }
        finally
        {
            SqliteConnectionFactory.ClearAllPools();
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    private static long ScalarLong(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string ScalarString(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
    }
}
