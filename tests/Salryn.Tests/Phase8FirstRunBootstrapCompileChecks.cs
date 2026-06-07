using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Bootstrap;
using Salryn.Infrastructure.Paths;
using Salryn.Infrastructure.Security;

namespace Salryn.Tests;

public static class Phase8FirstRunBootstrapCompileChecks
{
    public static void Run()
    {
        Pbkdf2PasswordHasher hasher = new();

        string passwordHash = hasher.HashPassword("StrongPass123!");

        if (!hasher.VerifyPassword("StrongPass123!", passwordHash))
        {
            throw new InvalidOperationException("Password verification check failed.");
        }

        if (hasher.VerifyPassword("WrongPassword123!", passwordHash))
        {
            throw new InvalidOperationException("Wrong password verification check failed.");
        }

        DbPaths paths = DbPaths.CreateDevelopment("C:\\SalrynDevCheck");

        SqliteConnectionFactory factory = new(paths);

        FirstRunBootstrapper bootstrapper = new(factory, hasher);

        AdminBootstrapRequest request = new(
            StoreName: "Sample Store",
            BusinessName: null,
            Address: null,
            ContactNumber: null,
            Tin: null,
            ReceiptWidthMm: 80,
            AdminUsername: "admin",
            AdminDisplayName: "Administrator",
            AdminPassword: "StrongPass123!");

        FirstRunStatus status = new(
            IsDatabaseInitialized: false,
            IsFirstRunRequired: true,
            HasUsers: false,
            HasStoreSettings: false);

        if (request.ReceiptWidthMm != 80)
        {
            throw new InvalidOperationException("Admin bootstrap request check failed.");
        }

        if (!status.IsFirstRunRequired)
        {
            throw new InvalidOperationException("First-run status check failed.");
        }

        _ = bootstrapper;
    }
}
