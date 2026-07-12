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
        return _firstRunBootstrapper.CreateFirstAdmin(request);
    }

    public Result<DatabaseInitializationSummary> InitializeDatabase()
    {
        try
        {
            _migrationRunner.RunPendingMigrations();
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
}
