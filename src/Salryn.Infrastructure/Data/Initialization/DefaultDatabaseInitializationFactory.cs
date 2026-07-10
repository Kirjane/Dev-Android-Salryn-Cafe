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
        // Version 8 adds persisted product tax classification for official checkout VAT bucket wiring.
        // Version 9 adds LAN-M1 deployment identity and Salryn Store foundation.
        // Version 10 adds LAN-M4 terminal registry and pairing preparation.
        // Version 11 adds LAN-M5 controlled remote transport gate configuration.
        // Version 12 adds LAN-M6 hash-only terminal credential and remote handshake state.
        // Version 13 adds official receipt profile setup text (RMO-BIR-M4); no checkout/sales mutation.
        // Version 14 adds internal official dry-run package storage (RMO-BIR-M8); no live issuance.
        // Version 15 adds vat_registration_mode on bir_machine_profiles (TRAD-M1 tax spine); no live issuance.
        IDatabaseMigration[] migrations =
        {
            new InitialSchemaMigration(),
            new BirMachineProfileSchemaMigration(),
            new BirOfficialDocumentSequenceSchemaMigration(),
            new BirElectronicJournalSchemaMigration(),
            new BirElectronicJournalSchemaHardeningMigration(),
            new BirZReadingSchemaMigration(),
            new BirElectronicJournalCorrectionEventSchemaMigration(),
            new ProductTaxClassificationSchemaMigration(),
            new DeploymentIdentitySchemaMigration(),
            new TerminalRegistrySchemaMigration(),
            new LanRemoteTransportSchemaMigration(),
            new TerminalCredentialHandshakeSchemaMigration(),
            new OfficialReceiptProfileSchemaMigration(),
            new OfficialDryRunPackageSchemaMigration(),
            new VatRegistrationModeSchemaMigration(),
            new OfficialDocumentLifecycleCompletionSchemaMigration()
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
