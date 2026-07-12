using Salryn.Composition.Local;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Bootstrap;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class ManualM1BootstrapAuthRepairChecks
{
    public static void Run()
    {
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "salryn-manual-m1-bootstrap-auth-" + Guid.NewGuid().ToString("N"));

        try
        {
            DbPaths paths = DbPaths.CreateDevelopment(tempRoot);
            SalrynLocalComposition composition = SalrynLocalComposition.Create(paths);

            composition.InitializeDatabaseAsync().GetAwaiter().GetResult();

            var initialStatus = composition.DatabaseInitialization.CheckStatus();
            if (initialStatus.IsFailure)
            {
                throw new InvalidOperationException(initialStatus.Error);
            }

            if (!initialStatus.Value.FirstRunStatus.IsFirstRunRequired ||
                initialStatus.Value.FirstRunStatus.HasUsers ||
                initialStatus.Value.FirstRunStatus.HasStoreSettings)
            {
                throw new InvalidOperationException("Fresh initialized DB must require first-run setup and have no users/store settings.");
            }

            AdminBootstrapRequest request = new(
                StoreName: "Salryn Dev General Merchandise",
                BusinessName: null,
                Address: null,
                ContactNumber: null,
                Tin: null,
                ReceiptWidthMm: 80,
                AdminUsername: "admin",
                AdminDisplayName: "DEV Admin",
                AdminPassword: "StrongAdmin123!");

            var bootstrapResult = composition.DatabaseInitialization.CreateFirstAdmin(request);
            if (bootstrapResult.IsFailure)
            {
                throw new InvalidOperationException(bootstrapResult.Error);
            }

            var completedStatus = composition.DatabaseInitialization.CheckStatus();
            if (completedStatus.IsFailure)
            {
                throw new InvalidOperationException(completedStatus.Error);
            }

            if (completedStatus.Value.FirstRunStatus.IsFirstRunRequired ||
                !completedStatus.Value.FirstRunStatus.HasUsers ||
                !completedStatus.Value.FirstRunStatus.HasStoreSettings)
            {
                throw new InvalidOperationException("First-run setup must complete and persist users/store settings.");
            }

            var loginResult = composition.Authentication
                .LoginAsync(new LoginRequest("admin", "StrongAdmin123!"))
                .GetAwaiter()
                .GetResult();
            if (loginResult.IsFailure)
            {
                throw new InvalidOperationException("Bootstrap-created admin must authenticate through normal login: " + loginResult.Error);
            }

            if (!string.Equals(loginResult.Value.RoleName, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Bootstrap-created admin must authenticate as Admin.");
            }

            var wrongPasswordResult = composition.Authentication
                .LoginAsync(new LoginRequest("admin", "WrongAdmin123!"))
                .GetAwaiter()
                .GetResult();
            if (wrongPasswordResult.IsSuccess)
            {
                throw new InvalidOperationException("Wrong password must not authenticate bootstrap-created admin.");
            }

            var duplicateBootstrapResult = composition.DatabaseInitialization.CreateFirstAdmin(request);
            if (duplicateBootstrapResult.IsSuccess)
            {
                throw new InvalidOperationException("Duplicate first-admin bootstrap must fail after setup completion.");
            }
        }
        finally
        {
            SqliteConnectionFactory.ClearAllPools();
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
