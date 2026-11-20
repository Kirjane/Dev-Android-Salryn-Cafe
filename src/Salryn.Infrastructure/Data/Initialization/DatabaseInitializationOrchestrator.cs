using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Infrastructure.Data.Bootstrap;
using Salryn.Infrastructure.Data.Migrations;
using Salryn.Infrastructure.Data.Seeding;

namespace Salryn.Infrastructure.Data.Initialization;

public sealed class DatabaseInitializationOrchestrator
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly MigrationRunner _migrationRunner;
    private readonly DatabaseSeeder _databaseSeeder;
    private readonly FirstRunBootstrapper _firstRunBootstrapper;

    public DatabaseInitializationOrchestrator(
        SqliteConnectionFactory connectionFactory,
        MigrationRunner migrationRunner,
        DatabaseSeeder databaseSeeder,
        FirstRunBootstrapper firstRunBootstrapper)
    {
        _connectionFactory = connectionFactory;
        _migrationRunner = migrationRunner;
        _databaseSeeder = databaseSeeder;
        _firstRunBootstrapper = firstRunBootstrapper;
    }

    public string DatabasePath => _connectionFactory.DatabasePath;

    public int ConfiguredMigrationCount => _migrationRunner.Migrations.Count;

    public Result<DatabaseInitializationStatus> CheckStatus()
    {
        bool databaseFileExists = File.Exists(_connectionFactory.DatabasePath);

        if (!databaseFileExists)
        {
            return Result<DatabaseInitializationStatus>.Success(
                new DatabaseInitializationStatus(
                    DatabasePath: _connectionFactory.DatabasePath,
                    DatabaseFileExists: false,
                    ConfiguredMigrationCount: ConfiguredMigrationCount,
                    HasSeedDataFoundation: true,
                    FirstRunStatus: new FirstRunStatus(
                        IsDatabaseInitialized: false,
                        IsFirstRunRequired: true,
                        HasUsers: false,
                        HasStoreSettings: false),
                    PendingMigrationCount: ConfiguredMigrationCount));
        }

        // Bootstrap status configures WAL. Reject retained wrong/unknown databases first,
        // using the runner's read-only classifier just as migration initialization does.
        if (_migrationRunner.Migrations.Any(m => m is CafeLineageAdoptionAndKernelParityMigration))
        {
            try
            {
                var lineage = _migrationRunner.ClassifyDatabase();
                if (lineage.Kind is not (SchemaLineageKind.Fresh or SchemaLineageKind.YzaV39Legacy or SchemaLineageKind.SalrynCafe))
                {
                    return Result<DatabaseInitializationStatus>.Fail(Error.Failure(
                        "DatabaseInitialization.LineageRejected", lineage.Reason));
                }
            }
            catch (SqliteException)
            {
                return Result<DatabaseInitializationStatus>.Fail(Error.Failure(
                    "DatabaseInitialization.LineageRejected", "Database lineage could not be safely inspected."));
            }
        }

        int pendingMigrationCount = _migrationRunner.GetPendingMigrationCount();

        Result<FirstRunStatus> firstRunStatusResult = _firstRunBootstrapper.CheckStatus();

        if (firstRunStatusResult.IsFailure)
        {
            return Result<DatabaseInitializationStatus>.Fail(firstRunStatusResult.ErrorDetail);
        }

        return Result<DatabaseInitializationStatus>.Success(
            new DatabaseInitializationStatus(
                DatabasePath: _connectionFactory.DatabasePath,
                DatabaseFileExists: true,
                ConfiguredMigrationCount: ConfiguredMigrationCount,
                HasSeedDataFoundation: true,
                FirstRunStatus: firstRunStatusResult.Value,
                PendingMigrationCount: pendingMigrationCount));
    }

    public Result<AdminBootstrapSummary> CreateFirstAdmin(AdminBootstrapRequest request)
    {
        var status = CheckStatus();
        if (status.IsFailure)
        {
            return Result<AdminBootstrapSummary>.Fail(status.ErrorDetail);
        }
        return _firstRunBootstrapper.CreateFirstAdmin(request);
    }

    public Result<DatabaseInitializationSummary> InitializeDatabase()
    {
        try
        {
            _migrationRunner.RunPendingMigrations();
            // PRE-I: repair existing DBs if logo columns are missing after partial/skipped v20 apply.
            EnsureStoreLogoColumns();
            _databaseSeeder.SeedDefaultData();

            Result<FirstRunStatus> firstRunStatusResult = _firstRunBootstrapper.CheckStatus();

            if (firstRunStatusResult.IsFailure)
            {
                return Result<DatabaseInitializationSummary>.Fail(firstRunStatusResult.ErrorDetail);
            }

            return Result<DatabaseInitializationSummary>.Success(
                new DatabaseInitializationSummary(
                    DatabasePath: _connectionFactory.DatabasePath,
                    ConfiguredMigrationCount: ConfiguredMigrationCount,
                    FirstRunStatus: firstRunStatusResult.Value,
                    InitializedAt: DateTimeOffset.UtcNow));
        }
        catch (SqliteException ex)
        {
            return Result<DatabaseInitializationSummary>.Fail(Error.Unexpected(
                "DatabaseInitialization.SqliteFailed",
                $"Database initialization failed: {ex.Message}"));
        }
        catch (InvalidOperationException ex)
        {
            return Result<DatabaseInitializationSummary>.Fail(Error.Failure(
                "DatabaseInitialization.InvalidOperation",
                $"Database initialization could not continue: {ex.Message}"));
        }
    }

    private void EnsureStoreLogoColumns()
    {
        if (!File.Exists(_connectionFactory.DatabasePath))
        {
            return;
        }

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        SqlitePragma.Apply(connection);
        StoreLogoBrandingSchemaMigration.EnsureLogoColumnsPresent(connection);
    }
}
