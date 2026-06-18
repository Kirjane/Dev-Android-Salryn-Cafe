using Microsoft.Data.Sqlite;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Migrations;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class J21Rmo242023MachineProfileSchemaChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string migrationText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Migrations",
            "BirMachineProfileSchemaMigration.cs");

        string factoryText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Initialization",
            "DefaultDatabaseInitializationFactory.cs");

        string schemaDocText = ReadRequiredFile(
            repoRoot,
            "00-COMPLIANCE",
            "RMO_24_2023_MACHINE_PROFILE_SCHEMA.md");

        string matrixText = ReadRequiredFile(
            repoRoot,
            "00-COMPLIANCE",
            "RMO_24_2023_COMPLIANCE_MATRIX.md");

        string handoffText = ReadRequiredFile(
            repoRoot,
            "00-HANDOFF",
            "J21_2_RMO_MACHINE_PROFILE_SCHEMA.md");

        string coreSmokeText = ReadRequiredFile(
            repoRoot,
            "tools",
            "Salryn.Tools.CoreSmoke",
            "Program.cs");

        RequireContains(migrationText, "// Kirch strict dev guide:", "BIR machine profile migration must include strict development guide comments.");
        RequireContains(migrationText, "J21-2 owns the machine/POS registration schema only.", "BIR machine profile migration comments must explain J21-2 ownership.");
        RequireContains(migrationText, "Do not add receipt numbering, printing, UI, tenant routing, or hardware adapters here.", "BIR machine profile migration comments must block scope creep.");
        RequireContains(migrationText, "This is the main safety switch.", "BIR machine profile migration comments must explain official output gating.");
        RequireContains(factoryText, "Migration order is compliance-sensitive.", "Database initialization must explain compliance-sensitive migration order.");

        RequireContains(migrationText, "public int Version => 2;", "BIR machine profile migration must be version 2.");
        RequireContains(migrationText, "CREATE TABLE bir_machine_profiles", "BIR machine profile migration must create bir_machine_profiles.");
        RequireContains(migrationText, "registered_taxpayer_name TEXT NOT NULL", "Machine profile must store registered taxpayer name.");
        RequireContains(migrationText, "taxpayer_identification_number TEXT NOT NULL", "Machine profile must store taxpayer identification number.");
        RequireContains(migrationText, "branch_code TEXT NOT NULL", "Machine profile must store branch code.");
        RequireContains(migrationText, "machine_identification_number TEXT NULL", "Machine profile must allow machine identification number.");
        RequireContains(migrationText, "machine_serial_number TEXT NOT NULL", "Machine profile must store machine serial number.");
        RequireContains(migrationText, "accreditation_number TEXT NULL", "Machine profile must allow accreditation number.");
        RequireContains(migrationText, "permit_to_use_number TEXT NULL", "Machine profile must allow permit to use number.");
        RequireContains(migrationText, "software_version TEXT NOT NULL", "Machine profile must store software version.");
        RequireContains(migrationText, "official_output_enabled INTEGER NOT NULL DEFAULT 0", "Machine profile must default official output to disabled.");
        RequireContains(migrationText, "CHECK ((official_output_enabled = 0) OR (compliance_mode = 'ACCREDITED'))", "Machine profile must block official output unless accredited.");
        RequireContains(migrationText, "idx_bir_machine_profiles_active_scope", "Machine profile must have an active scope guard.");

        RequireContains(factoryText, "new BirMachineProfileSchemaMigration()", "Default database initialization must register BIR machine profile migration.");
        RequireContains(schemaDocText, "J21-2 status: schema foundation.", "J21-2 schema doc must declare schema foundation scope.");
        RequireContains(schemaDocText, "No physical printer is required for this phase.", "J21-2 schema doc must preserve printer hardware boundary.");
        RequireContains(schemaDocText, "No schema is required for basic barcode scanner support.", "J21-2 schema doc must preserve barcode scanner boundary.");
        RequireContains(matrixText, "## J21-2 Schema Foundation", "Compliance matrix must mention J21-2 schema foundation.");
        RequireContains(handoffText, "Status: schema foundation only.", "J21-2 handoff must preserve schema-only scope.");
        RequireContains(handoffText, "J21-3 must build the official receipt/invoice sequence service.", "J21-2 handoff must point to receipt sequence next.");
        RequireContains(coreSmokeText, "J21Rmo242023MachineProfileSchemaChecks.Run();", "Core smoke must run J21-2 machine profile schema checks.");

        RunMigrationShapeCheck();
    }

    private static void RunMigrationShapeCheck()
    {
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "Salryn-J21-2-" + Guid.NewGuid().ToString("N"));

        try
        {
            DbPaths paths = DbPaths.CreateDevelopment(tempRoot);
            SqliteConnectionFactory factory = new(paths);

            MigrationRunner runner = new(
                factory,
                new IDatabaseMigration[]
                {
                    new InitialSchemaMigration(),
                    new BirMachineProfileSchemaMigration()
                });

            runner.RunPendingMigrations();

            using SqliteConnection connection = factory.CreateConnection();
            connection.Open();

            RequireAppliedMigration(connection, 2, "BIR machine profile schema");

            RequireTable(connection, "bir_machine_profiles");

            RequireColumn(connection, "bir_machine_profiles", "registration_scope_code");
            RequireColumn(connection, "bir_machine_profiles", "store_settings_id");
            RequireColumn(connection, "bir_machine_profiles", "registered_taxpayer_name");
            RequireColumn(connection, "bir_machine_profiles", "business_name");
            RequireColumn(connection, "bir_machine_profiles", "registered_address");
            RequireColumn(connection, "bir_machine_profiles", "branch_address");
            RequireColumn(connection, "bir_machine_profiles", "taxpayer_identification_number");
            RequireColumn(connection, "bir_machine_profiles", "branch_code");
            RequireColumn(connection, "bir_machine_profiles", "machine_identification_number");
            RequireColumn(connection, "bir_machine_profiles", "machine_serial_number");
            RequireColumn(connection, "bir_machine_profiles", "pos_terminal_number");
            RequireColumn(connection, "bir_machine_profiles", "accreditation_number");
            RequireColumn(connection, "bir_machine_profiles", "permit_to_use_number");
            RequireColumn(connection, "bir_machine_profiles", "permit_to_use_issued_at");
            RequireColumn(connection, "bir_machine_profiles", "accreditation_issued_at");
            RequireColumn(connection, "bir_machine_profiles", "accreditation_valid_until");
            RequireColumn(connection, "bir_machine_profiles", "software_name");
            RequireColumn(connection, "bir_machine_profiles", "software_version");
            RequireColumn(connection, "bir_machine_profiles", "software_build");
            RequireColumn(connection, "bir_machine_profiles", "compliance_mode");
            RequireColumn(connection, "bir_machine_profiles", "official_output_enabled");
            RequireColumn(connection, "bir_machine_profiles", "is_active");
            RequireColumn(connection, "bir_machine_profiles", "created_at");
            RequireColumn(connection, "bir_machine_profiles", "updated_at");
            RequireColumn(connection, "bir_machine_profiles", "deactivated_at");

            RequireIndex(connection, "idx_bir_machine_profiles_active_scope");
            RequireIndex(connection, "idx_bir_machine_profiles_tin_branch");
            RequireIndex(connection, "idx_bir_machine_profiles_serial");
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static void RequireAppliedMigration(
        SqliteConnection connection,
        int version,
        string name)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM schema_migrations
            WHERE version = $version
              AND name = $name;
            """;
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$name", name);

        long count = Convert.ToInt64(command.ExecuteScalar());

        if (count != 1)
        {
            throw new InvalidOperationException($"Expected migration {version} / {name} to be applied.");
        }
    }

    private static void RequireTable(SqliteConnection connection, string tableName)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name = $tableName;
            """;
        command.Parameters.AddWithValue("$tableName", tableName);

        long count = Convert.ToInt64(command.ExecuteScalar());

        if (count != 1)
        {
            throw new InvalidOperationException($"Required table was not found: {tableName}");
        }
    }

    private static void RequireColumn(
        SqliteConnection connection,
        string tableName,
        string columnName)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";

        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new InvalidOperationException($"Required column was not found: {tableName}.{columnName}");
    }

    private static void RequireIndex(SqliteConnection connection, string indexName)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'index'
              AND name = $indexName;
            """;
        command.Parameters.AddWithValue("$indexName", indexName);

        long count = Convert.ToInt64(command.ExecuteScalar());

        if (count != 1)
        {
            throw new InvalidOperationException($"Required index was not found: {indexName}");
        }
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

        current = new DirectoryInfo(Directory.GetCurrentDirectory());

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

    private static string ReadRequiredFile(DirectoryInfo repoRoot, params string[] pathParts)
    {
        string path = repoRoot.FullName;

        foreach (string pathPart in pathParts)
        {
            path = Path.Combine(path, pathPart);
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Required file was not found: {path}");
        }

        return File.ReadAllText(path);
    }

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}