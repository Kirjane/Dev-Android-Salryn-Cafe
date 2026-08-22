using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Bootstrap;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Migrations;
using Salryn.Infrastructure.Data.Seeding;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class DatabaseInitializationStatusReadOnlyChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();
        string orchestratorSource = Read(
            repoRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Initialization",
            "DatabaseInitializationOrchestrator.cs");

        RequireCheckStatusDoesNotRunPendingMigrations(orchestratorSource);
        RequireCheckStatusLeavesOldSchemaUnchangedUntilExplicitUpgrade();
    }

    private static void RequireCheckStatusDoesNotRunPendingMigrations(string orchestratorSource)
    {
        string checkStatusBody = ExtractMethodBody(
            orchestratorSource,
            "public Result<DatabaseInitializationStatus> CheckStatus()");

        RequireNotContains(
            checkStatusBody,
            "RunPendingMigrations",
            "CheckStatus must remain read-only and must not run pending migrations.");
        RequireContains(
            checkStatusBody,
            "GetPendingMigrationCount",
            "CheckStatus must detect pending migrations without mutating schema.");
    }

    private static void RequireCheckStatusLeavesOldSchemaUnchangedUntilExplicitUpgrade()
    {
        string projectRoot = CreateTempProjectRoot();

        try
        {
            SqliteConnectionFactory connectionFactory = new(DbPaths.CreateDevelopment(projectRoot));
            MigrationRunner preV8Runner = new(
                connectionFactory,
                CreateMigrationsThroughVersion7());

            preV8Runner.RunPendingMigrations();

            using (SqliteConnection connection = connectionFactory.CreateConnection())
            {
                connection.Open();
                SeedUnit(connection);

                using SqliteCommand insert = connection.CreateCommand();
                insert.CommandText = """
                    INSERT INTO products (
                        id, name, unit_id, selling_price_cents, stock_on_hand_x1000, reorder_level_x1000,
                        track_stock, allow_discount, is_active, created_at
                    )
                    VALUES (
                        'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
                        'Legacy Product',
                        'bbbbbbbb-cccc-dddd-eeee-ffffffff0001',
                        1000,
                        0,
                        0,
                        1,
                        1,
                        1,
                        '2026-01-01T00:00:00.0000000+00:00'
                    );
                    """;
                insert.ExecuteNonQuery();
            }

            DatabaseInitializationOrchestrator orchestrator = CreateFullOrchestrator(projectRoot);

            Result<DatabaseInitializationStatus> statusResult = orchestrator.CheckStatus();
            RequireSuccess(statusResult, "CheckStatus should succeed on a v7 schema database.");

            if (!statusResult.Value.RequiresDatabaseUpgrade)
            {
                throw new InvalidOperationException("CheckStatus must report pending schema upgrade for v7 databases.");
            }

            if (ColumnExists(connectionFactory, "products", "tax_classification"))
            {
                throw new InvalidOperationException("CheckStatus must not add products.tax_classification.");
            }

            if (MigrationVersionApplied(connectionFactory, 8))
            {
                throw new InvalidOperationException("CheckStatus must not record v8 migration history.");
            }

            Result<DatabaseInitializationSummary> upgradeResult = orchestrator.InitializeDatabase();
            RequireSuccess(upgradeResult, "Explicit InitializeDatabase must apply pending migrations.");

            if (!ColumnExists(connectionFactory, "products", "tax_classification"))
            {
                throw new InvalidOperationException("Explicit upgrade must add products.tax_classification.");
            }

            if (!MigrationVersionApplied(connectionFactory, 8))
            {
                throw new InvalidOperationException("Explicit upgrade must record v8 migration history.");
            }

            using SqliteConnection readConnection = connectionFactory.CreateConnection();
            readConnection.Open();

            using SqliteCommand read = readConnection.CreateCommand();
            read.CommandText = "SELECT tax_classification FROM products WHERE id = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee';";
            int storedValue = Convert.ToInt32(read.ExecuteScalar());

            if (storedValue != (int)ProductTaxClassification.Vatable)
            {
                throw new InvalidOperationException("Existing products must default to VATable after explicit v8 upgrade.");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            if (Directory.Exists(projectRoot))
            {
                try
                {
                    Directory.Delete(projectRoot, recursive: true);
                }
                catch (IOException)
                {
                    // Best-effort temp cleanup only.
                }
            }
        }
    }

    private static IDatabaseMigration[] CreateMigrationsThroughVersion7()
    {
        return
        [
            new InitialSchemaMigration(),
            new BirMachineProfileSchemaMigration(),
            new BirOfficialDocumentSequenceSchemaMigration(),
            new BirElectronicJournalSchemaMigration(),
            new BirElectronicJournalSchemaHardeningMigration(),
            new BirZReadingSchemaMigration(),
            new BirElectronicJournalCorrectionEventSchemaMigration()
        ];
    }

    private static DatabaseInitializationOrchestrator CreateFullOrchestrator(string projectRoot)
    {
        SqliteConnectionFactory connectionFactory = new(DbPaths.CreateDevelopment(projectRoot));
        MigrationRunner migrationRunner = new(
            connectionFactory,
            CreateMigrationsThroughVersion7().Concat([new ProductTaxClassificationSchemaMigration()]).ToArray());

        return new DatabaseInitializationOrchestrator(
            connectionFactory,
            migrationRunner,
            new DatabaseSeeder(connectionFactory),
            new FirstRunBootstrapper(connectionFactory));
    }

    private static bool ColumnExists(
        SqliteConnectionFactory connectionFactory,
        string tableName,
        string columnName)
    {
        using SqliteConnection connection = connectionFactory.CreateConnection();
        connection.Open();

        using SqliteCommand pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA table_info(" + tableName + ");";

        using SqliteDataReader reader = pragma.ExecuteReader();

        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MigrationVersionApplied(SqliteConnectionFactory connectionFactory, int version)
    {
        using SqliteConnection connection = connectionFactory.CreateConnection();
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM schema_migrations WHERE version = $version LIMIT 1;";
        command.Parameters.AddWithValue("$version", version);

        return command.ExecuteScalar() is not null;
    }

    private static void SeedUnit(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO units (id, name, abbreviation)
            VALUES ('bbbbbbbb-cccc-dddd-eeee-ffffffff0001', 'Piece', 'pc');
            """;
        command.ExecuteNonQuery();
    }

    private static string CreateTempProjectRoot()
    {
        string projectRoot = Path.Combine(Path.GetTempPath(), "salryn-checkstatus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectRoot);
        return projectRoot;
    }

    private static string ExtractMethodBody(string source, string methodSignature)
    {
        int signatureIndex = source.IndexOf(methodSignature, StringComparison.Ordinal);

        if (signatureIndex < 0)
        {
            throw new InvalidOperationException("Could not locate CheckStatus method for read-only guard.");
        }

        int openBraceIndex = source.IndexOf('{', signatureIndex);

        if (openBraceIndex < 0)
        {
            throw new InvalidOperationException("Could not locate CheckStatus method body for read-only guard.");
        }

        int depth = 0;

        for (int index = openBraceIndex; index < source.Length; index++)
        {
            char current = source[index];

            if (current == '{')
            {
                depth++;
            }
            else if (current == '}')
            {
                depth--;

                if (depth == 0)
                {
                    return source.Substring(openBraceIndex, index - openBraceIndex + 1);
                }
            }
        }

        throw new InvalidOperationException("Could not extract CheckStatus method body for read-only guard.");
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate Salryn repository root.");
    }

    private static string Read(DirectoryInfo root, params string[] parts)
    {
        string path = root.FullName;

        foreach (string part in parts)
        {
            path = Path.Combine(path, part);
        }

        return File.ReadAllText(path);
    }

    private static void RequireContains(string haystack, string needle, string message)
    {
        if (!haystack.Contains(needle, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message + " Missing: " + needle);
        }
    }

    private static void RequireNotContains(string haystack, string needle, string message)
    {
        if (haystack.Contains(needle, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message + " Unexpected: " + needle);
        }
    }

    private static void RequireSuccess<T>(Result<T> result, string message)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                message + " " + (result.ErrorDetail?.Message ?? result.Error?.ToString() ?? "unknown error"));
        }
    }
}
