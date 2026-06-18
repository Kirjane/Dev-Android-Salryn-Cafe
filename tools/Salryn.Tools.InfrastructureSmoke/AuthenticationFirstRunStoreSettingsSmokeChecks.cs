using Microsoft.Data.Sqlite;
using Salryn.App.WinForms.Composition;
using Salryn.Core.Common;
using Salryn.Core.ReadModels;
using Salryn.Core.Security;
using Salryn.Infrastructure.Data.Bootstrap;
using Salryn.Infrastructure.Paths;

internal static class AuthenticationFirstRunStoreSettingsSmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== AUTH FIRST-RUN STORE SETTINGS SMOKE CHECKS =====");

        string smokeRoot = Path.Combine(projectRoot, ".salryn-j20-auth-first-run-smoke");

        ResetSmokeRoot(smokeRoot);

        try
        {
            DbPaths paths = DbPaths.CreateDevelopment(smokeRoot);
            SalrynAppServices services = SalrynAppServices.Create(paths);

            RequireSuccess(
                services.DatabaseInitialization.InitializeDatabase(),
                "AuthFirstRun.InitializeDatabase");

            FirstRunStatus before = RequireSuccess(
                services.DatabaseInitialization.CheckStatus(),
                "AuthFirstRun.CheckStatusBefore")
                .FirstRunStatus;

            if (!before.IsDatabaseInitialized)
            {
                throw new InvalidOperationException("Database should be initialized before auth first-run smoke.");
            }

            if (!before.IsFirstRunRequired || before.HasUsers || before.HasStoreSettings)
            {
                throw new InvalidOperationException("Fresh initialized database should require first-run and have no users/store settings.");
            }

            AuthenticatedUserReadModel admin = RequireSuccess(
                services.Authentication.CreateFirstAdminAsync(
                    new FirstRunAdminSetupRequest(
                        "admin",
                        "SALRYN Admin",
                        "StrongAdmin123!",
                        "StrongAdmin123!")).GetAwaiter().GetResult(),
                "AuthFirstRun.CreateFirstAdmin");

            if (admin.RoleId == Guid.Empty)
            {
                throw new InvalidOperationException("Authenticated admin must carry a RoleId.");
            }

            if (admin.Permissions.Count == 0)
            {
                throw new InvalidOperationException("Authenticated admin must carry permissions.");
            }

            FirstRunStatus after = RequireSuccess(
                services.DatabaseInitialization.CheckStatus(),
                "AuthFirstRun.CheckStatusAfter")
                .FirstRunStatus;

            if (!after.IsDatabaseInitialized)
            {
                throw new InvalidOperationException("Database should remain initialized after first-run auth setup.");
            }

            if (after.IsFirstRunRequired)
            {
                throw new InvalidOperationException("First-run should be cleared after auth first admin setup.");
            }

            if (!after.HasUsers)
            {
                throw new InvalidOperationException("Auth first-run setup should create an admin user.");
            }

            if (!after.HasStoreSettings)
            {
                throw new InvalidOperationException("Auth first-run setup should create default store settings.");
            }

            StoreSettingsReadModel? settings = RequireSuccess(
                services.StoreSettingsReadRepository.GetCurrentAsync().GetAwaiter().GetResult(),
                "AuthFirstRun.GetStoreSettings");

            if (settings is null)
            {
                throw new InvalidOperationException("Store settings should be readable after auth first-run setup.");
            }

            if (!string.Equals(settings.StoreName, "SALRYN STORE", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unexpected default store name: {settings.StoreName}");
            }

            if (!string.Equals(settings.CurrencyLabel, "PHP", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Unexpected currency label: {settings.CurrencyLabel}");
            }

            Console.WriteLine($"Auth first-run admin created: {admin.UserId}");
            Console.WriteLine($"Default store settings created: {settings.Id}");
            Console.WriteLine("Auth first-run store settings smoke checks succeeded.");
            Console.WriteLine();
        }
        finally
        {
            ResetSmokeRoot(smokeRoot);
        }
    }

    private static T RequireSuccess<T>(Result<T> result, string label)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{label} failed: {result.Error}");
        }

        return result.Value;
    }

    private static void ResetSmokeRoot(string smokeRoot)
    {
        SqliteConnection.ClearAllPools();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        if (Directory.Exists(smokeRoot))
        {
            Directory.Delete(smokeRoot, recursive: true);
        }
    }
}