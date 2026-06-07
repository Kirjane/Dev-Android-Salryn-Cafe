using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Seeding;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class Phase7SeedDefaultDataCompileChecks
{
    public static void Run()
    {
        DbPaths paths = DbPaths.CreateDevelopment("C:\\SalrynDevCheck");

        SqliteConnectionFactory factory = new(paths);

        DatabaseSeeder seeder = new(factory);

        if (DefaultSeedData.Roles.Count < 3)
        {
            throw new InvalidOperationException("Default role seed check failed.");
        }

        if (DefaultSeedData.RolePermissions.Count == 0)
        {
            throw new InvalidOperationException("Default permission seed check failed.");
        }

        if (DefaultSeedData.Units.Count == 0)
        {
            throw new InvalidOperationException("Default unit seed check failed.");
        }

        if (DefaultSeedData.Categories.Count == 0)
        {
            throw new InvalidOperationException("Default category seed check failed.");
        }

        if (DefaultSeedData.ReceiptSequence.Prefix != "SLRN")
        {
            throw new InvalidOperationException("Default receipt sequence check failed.");
        }

        if (DefaultSeedData.StoreDefaults.CurrencyLabel != "PHP")
        {
            throw new InvalidOperationException("Default store setting check failed.");
        }

        _ = seeder;
    }
}
