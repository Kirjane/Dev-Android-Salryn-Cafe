using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Migrations;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class Phase6MigrationSchemaCompileChecks
{
    public static void Run()
    {
        DbPaths paths = DbPaths.CreateDevelopment("C:\\SalrynDevCheck");

        SqliteConnectionFactory factory = new(paths);

        IDatabaseMigration migration = new InitialSchemaMigration();

        MigrationRunner runner = new(factory, new[] { migration });

        if (migration.Version != 1)
        {
            throw new InvalidOperationException("Initial schema migration version check failed.");
        }

        if (string.IsNullOrWhiteSpace(migration.Name))
        {
            throw new InvalidOperationException("Initial schema migration name check failed.");
        }

        if (!runner.HasMigrations())
        {
            throw new InvalidOperationException("Migration runner should detect the initial migration.");
        }
    }
}
