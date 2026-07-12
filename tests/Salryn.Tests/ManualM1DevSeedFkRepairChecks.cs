using Microsoft.Data.Sqlite;
using Salryn.Composition.Local;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Bootstrap;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class ManualM1DevSeedFkRepairChecks
{
    public static void Run()
    {
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "salryn-manual-m1-dev-seed-fk-" + Guid.NewGuid().ToString("N"));

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
                throw new InvalidOperationException("Fresh initialized DB must require first-run setup and have no users/store settings before seed proof.");
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
                throw new InvalidOperationException("First-run setup must complete before dev seed FK proof runs.");
            }

            int seedResult = Salryn.Tools.DevData.Program.SeedGeneralStoreForManualProof(
                paths,
                count: 1000,
                confirmDevSeed: true);
            if (seedResult != 0)
            {
                throw new InvalidOperationException("Dev general-store seed must succeed in temp DB after first-run bootstrap. Exit code: " + seedResult);
            }

            VerifySeededProducts(paths.DatabasePath);
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

    private static void VerifySeededProducts(string databasePath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath
        }.ToString());
        connection.Open();
        SqlitePragma.Apply(connection);

        AssertCount(connection, "SELECT COUNT(*) FROM products WHERE sku LIKE 'DEV-GMHW-%';", 1000, "seeded product count");
        AssertCount(connection, "SELECT COUNT(DISTINCT sku) FROM products WHERE sku LIKE 'DEV-GMHW-%';", 1000, "unique seed SKUs");
        AssertCount(connection, "SELECT COUNT(DISTINCT name) FROM products WHERE sku LIKE 'DEV-GMHW-%';", 1000, "unique seed names");
        AssertCount(connection, "SELECT COUNT(DISTINCT barcode) FROM products WHERE sku LIKE 'DEV-GMHW-%';", 1000, "unique seed barcodes");
        AssertCount(connection, """
            SELECT COUNT(*)
            FROM products p
            LEFT JOIN categories c ON c.id = p.category_id
            WHERE p.sku LIKE 'DEV-GMHW-%' AND c.id IS NULL;
            """, 0, "seed products with missing category FK");
        AssertCount(connection, """
            SELECT COUNT(*)
            FROM products p
            LEFT JOIN brands b ON b.id = p.brand_id
            WHERE p.sku LIKE 'DEV-GMHW-%' AND b.id IS NULL;
            """, 0, "seed products with missing brand FK");
        AssertCount(connection, """
            SELECT COUNT(*)
            FROM products p
            LEFT JOIN units u ON u.id = p.unit_id
            WHERE p.sku LIKE 'DEV-GMHW-%' AND u.id IS NULL;
            """, 0, "seed products with missing unit FK");
        AssertCount(connection, """
            SELECT COUNT(*)
            FROM products
            WHERE sku LIKE 'DEV-GMHW-%'
              AND notes NOT LIKE '%no product images%';
            """, 0, "seed products missing no-image note");
    }

    private static void AssertCount(
        SqliteConnection connection,
        string sql,
        long expected,
        string label)
    {
        long actual = ScalarLong(connection, sql);
        if (actual != expected)
        {
            throw new InvalidOperationException($"Expected {expected} for {label}; actual {actual}.");
        }
    }

    private static long ScalarLong(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }
}
