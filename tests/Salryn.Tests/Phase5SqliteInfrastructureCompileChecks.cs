using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Migrations;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class Phase5SqliteInfrastructureCompileChecks
{
    public static void Run()
    {
        DbPaths paths = DbPaths.CreateDevelopment("C:\\SalrynDevCheck");

        SqliteConnectionFactory factory = new(paths);

        string connectionString = factory.CreateConnectionString();

        MigrationRunner runner = new(factory, Array.Empty<IDatabaseMigration>());

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string check failed.");
        }

        if (runner.HasMigrations())
        {
            throw new InvalidOperationException("Migration runner should have no migrations yet.");
        }
    }
}
