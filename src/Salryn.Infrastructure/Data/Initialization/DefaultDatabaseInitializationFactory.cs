using Salryn.Infrastructure.Data.Bootstrap;
using Salryn.Infrastructure.Data.Migrations;
using Salryn.Infrastructure.Data.Seeding;
using Salryn.Infrastructure.Paths;
using Salryn.Infrastructure.Security;

namespace Salryn.Infrastructure.Data.Initialization;

public static class DefaultDatabaseInitializationFactory
{
    public static DatabaseInitializationOrchestrator CreateProduction()
    {
        return Create(DbPaths.CreateProduction());
    }

    public static DatabaseInitializationOrchestrator CreateDevelopment(string projectRoot)
    {
        return Create(DbPaths.CreateDevelopment(projectRoot));
    }

    public static DatabaseInitializationOrchestrator Create(DbPaths dbPaths)
    {
        SqliteConnectionFactory connectionFactory = new(dbPaths);

        IDatabaseMigration[] migrations =
        {
            new InitialSchemaMigration()
        };

        MigrationRunner migrationRunner = new(connectionFactory, migrations);
        DatabaseSeeder databaseSeeder = new(connectionFactory);
        Pbkdf2PasswordHasher passwordHasher = new();
        FirstRunBootstrapper bootstrapper = new(connectionFactory, passwordHasher);

        return new DatabaseInitializationOrchestrator(
            connectionFactory,
            migrationRunner,
            databaseSeeder,
            bootstrapper);
    }
}
