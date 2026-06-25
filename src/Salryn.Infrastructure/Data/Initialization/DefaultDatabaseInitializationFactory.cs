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

        // Kirch strict dev guide:
        // Migration order is compliance-sensitive.
        // Version 1 creates the POS foundation.
        // Version 2 adds the RMO machine/POS registration profile used by future official receipt layers.
        // Version 3 adds the RMO official receipt/invoice sequence ownership schema without issuing official output.
        // Version 4 adds the RMO electronic journal schema foundation without checkout writes or official output.
        // Version 5 adds electronic journal DB hardening triggers without app behavior.
        // Version 6 adds Z-reading finalization records without rendering, printing, or mutating source evidence.
        // Version 7 lets CORRECTION remain an append-only journal event instead of a source-row rewrite.
        IDatabaseMigration[] migrations =
        {
            new InitialSchemaMigration(),
            new BirMachineProfileSchemaMigration(),
            new BirOfficialDocumentSequenceSchemaMigration(),
            new BirElectronicJournalSchemaMigration(),
            new BirElectronicJournalSchemaHardeningMigration(),
            new BirZReadingSchemaMigration(),
            new BirElectronicJournalCorrectionEventSchemaMigration()
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
