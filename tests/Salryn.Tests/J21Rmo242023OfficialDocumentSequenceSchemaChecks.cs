using Microsoft.Data.Sqlite;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Migrations;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class J21Rmo242023OfficialDocumentSequenceSchemaChecks
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
            "BirOfficialDocumentSequenceSchemaMigration.cs");

        string factoryText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Initialization",
            "DefaultDatabaseInitializationFactory.cs");

        string matrixText = ReadRequiredFile(
            repoRoot,
            "00-COMPLIANCE",
            "RMO_24_2023_COMPLIANCE_MATRIX.md");

        string handoffText = ReadRequiredFile(
            repoRoot,
            "00-HANDOFF",
            "J21_3A_RMO_OFFICIAL_SEQUENCE_SCHEMA.md");

        string coreSmokeText = ReadRequiredFile(
            repoRoot,
            "tools",
            "Salryn.Tools.CoreSmoke",
            "Program.cs");

        RequireContains(migrationText, "// Kirch strict dev guide:", "Official sequence migration must include strict development guide comments.");
        RequireContains(migrationText, "J21-3A owns the official receipt/invoice sequence schema only.", "Official sequence migration must explain J21-3A ownership.");
        RequireContains(migrationText, "Do not issue numbers, print receipts, generate receipt snapshots, wire checkout", "Official sequence migration must block scope creep.");
        RequireContains(migrationText, "must not use GUID/random receipt numbers", "Official sequence migration must block random/GUID official numbering.");
        RequireContains(migrationText, "public int Version => 3;", "Official sequence migration must be version 3.");
        RequireContains(migrationText, "CREATE TABLE bir_official_document_sequences", "Official sequence migration must create bir_official_document_sequences.");
        RequireContains(migrationText, "bir_machine_profile_id TEXT NOT NULL", "Official sequence must be owned by machine profile.");
        RequireContains(migrationText, "document_kind TEXT NOT NULL", "Official sequence must separate receipt and invoice document kinds.");
        RequireContains(migrationText, "start_number INTEGER NOT NULL", "Official sequence must store start number.");
        RequireContains(migrationText, "current_number INTEGER NOT NULL", "Official sequence must store current number.");
        RequireContains(migrationText, "end_number INTEGER NULL", "Official sequence must store optional end number.");
        RequireContains(migrationText, "padding_digits INTEGER NOT NULL DEFAULT 6", "Official sequence must default to six-digit padding.");
        RequireContains(migrationText, "CHECK (padding_digits >= 6 AND padding_digits <= 12)", "Official sequence must enforce minimum six-digit padding.");
        RequireContains(migrationText, "CHECK (current_number >= (start_number - 1))", "Official sequence must prevent invalid current number.");
        RequireContains(migrationText, "idx_bir_official_document_sequences_active_scope", "Official sequence must prevent duplicate active owned scopes.");
        RequireContains(factoryText, "Version 3 adds the RMO official receipt/invoice sequence ownership schema without issuing official output.", "Database initialization must explain version 3 sequence ownership.");
        RequireContains(factoryText, "new BirOfficialDocumentSequenceSchemaMigration()", "Default database initialization must register official sequence migration.");
        RequireContains(matrixText, "## J21-3A Official Receipt/Invoice Sequence Schema Foundation", "Compliance matrix must include J21-3A sequence schema section.");
        RequireContains(handoffText, "Status: schema foundation only.", "J21-3A handoff must preserve schema-only scope.");
        RequireContains(handoffText, "No official receipt/invoice number allocation yet.", "J21-3A handoff must block allocation scope creep.");
        RequireContains(coreSmokeText, "J21Rmo242023OfficialDocumentSequenceSchemaChecks.Run();", "Core smoke must run J21-3A official sequence checks.");

        RunMigrationShapeCheck();
    }

    private static void RunMigrationShapeCheck()
    {
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "Salryn-J21-3A-" + Guid.NewGuid().ToString("N"));

        try
        {
            DbPaths paths = DbPaths.CreateDevelopment(tempRoot);
            SqliteConnectionFactory factory = new(paths);

            MigrationRunner runner = new(
                factory,
                new IDatabaseMigration[]
                {
                    new InitialSchemaMigration(),
                    new BirMachineProfileSchemaMigration(),
                    new BirOfficialDocumentSequenceSchemaMigration()
                });

            runner.RunPendingMigrations();

            using SqliteConnection connection = factory.CreateConnection();
            connection.Open();

            RequireAppliedMigration(connection, 3, "BIR official document sequence schema");

            RequireTable(connection, "bir_official_document_sequences");
            RequireColumn(connection, "bir_official_document_sequences", "id");
            RequireColumn(connection, "bir_official_document_sequences", "bir_machine_profile_id");
            RequireColumn(connection, "bir_official_document_sequences", "document_kind");
            RequireColumn(connection, "bir_official_document_sequences", "sequence_scope_code");
            RequireColumn(connection, "bir_official_document_sequences", "prefix");
            RequireColumn(connection, "bir_official_document_sequences", "year");
            RequireColumn(connection, "bir_official_document_sequences", "start_number");
            RequireColumn(connection, "bir_official_document_sequences", "current_number");
            RequireColumn(connection, "bir_official_document_sequences", "end_number");
            RequireColumn(connection, "bir_official_document_sequences", "padding_digits");
            RequireColumn(connection, "bir_official_document_sequences", "is_active");
            RequireColumn(connection, "bir_official_document_sequences", "is_exhausted");
            RequireColumn(connection, "bir_official_document_sequences", "created_at");
            RequireColumn(connection, "bir_official_document_sequences", "updated_at");
            RequireColumn(connection, "bir_official_document_sequences", "deactivated_at");

            RequireIndex(connection, "idx_bir_official_document_sequences_active_scope");
            RequireIndex(connection, "idx_bir_official_document_sequences_profile");
            RequireIndex(connection, "idx_bir_official_document_sequences_kind_year");

            const string profileId = "11111111-1111-1111-1111-111111111111";

            InsertMachineProfile(connection, profileId);

            InsertSequence(
                connection,
                id: "22222222-2222-2222-2222-222222222222",
                profileId: profileId,
                documentKind: "OFFICIAL_RECEIPT",
                prefix: "OR",
                year: 2026,
                startNumber: 1,
                currentNumber: 0,
                endNumber: 999999,
                paddingDigits: 6,
                isActive: 1,
                isExhausted: 0);

            RequireConstraintFailure(
                () => InsertSequence(
                    connection,
                    id: "33333333-3333-3333-3333-333333333333",
                    profileId: profileId,
                    documentKind: "OFFICIAL_RECEIPT",
                    prefix: "ORPAD",
                    year: 2026,
                    startNumber: 1,
                    currentNumber: 0,
                    endNumber: 999999,
                    paddingDigits: 5,
                    isActive: 0,
                    isExhausted: 0),
                "Official sequence must reject padding below six digits.");

            RequireConstraintFailure(
                () => InsertSequence(
                    connection,
                    id: "44444444-4444-4444-4444-444444444444",
                    profileId: profileId,
                    documentKind: "OFFICIAL_RECEIPT",
                    prefix: "ORBAD",
                    year: 2026,
                    startNumber: 2,
                    currentNumber: 0,
                    endNumber: 999999,
                    paddingDigits: 6,
                    isActive: 0,
                    isExhausted: 0),
                "Official sequence must reject current number below start minus one.");

            RequireConstraintFailure(
                () => InsertSequence(
                    connection,
                    id: "55555555-5555-5555-5555-555555555555",
                    profileId: profileId,
                    documentKind: "OFFICIAL_RECEIPT",
                    prefix: "OR",
                    year: 2026,
                    startNumber: 1,
                    currentNumber: 0,
                    endNumber: 999999,
                    paddingDigits: 6,
                    isActive: 1,
                    isExhausted: 0),
                "Official sequence must reject duplicate active owned scope.");
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

    private static void InsertMachineProfile(SqliteConnection connection, string profileId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO bir_machine_profiles (
                id,
                registered_taxpayer_name,
                registered_address,
                taxpayer_identification_number,
                branch_code,
                machine_serial_number,
                software_version,
                created_at
            )
            VALUES (
                $id,
                $registeredTaxpayerName,
                $registeredAddress,
                $tin,
                $branchCode,
                $machineSerialNumber,
                $softwareVersion,
                $createdAt
            );
            """;

        command.Parameters.AddWithValue("$id", profileId);
        command.Parameters.AddWithValue("$registeredTaxpayerName", "Kirch Test Taxpayer");
        command.Parameters.AddWithValue("$registeredAddress", "Test Registered Address");
        command.Parameters.AddWithValue("$tin", "000-000-000-000");
        command.Parameters.AddWithValue("$branchCode", "00000");
        command.Parameters.AddWithValue("$machineSerialNumber", "TEST-SERIAL-001");
        command.Parameters.AddWithValue("$softwareVersion", "TEST");
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void InsertSequence(
        SqliteConnection connection,
        string id,
        string profileId,
        string documentKind,
        string prefix,
        int year,
        long startNumber,
        long currentNumber,
        long? endNumber,
        int paddingDigits,
        int isActive,
        int isExhausted)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO bir_official_document_sequences (
                id,
                bir_machine_profile_id,
                document_kind,
                sequence_scope_code,
                prefix,
                year,
                start_number,
                current_number,
                end_number,
                padding_digits,
                is_active,
                is_exhausted,
                created_at
            )
            VALUES (
                $id,
                $profileId,
                $documentKind,
                'LOCAL_SINGLE_STORE',
                $prefix,
                $year,
                $startNumber,
                $currentNumber,
                $endNumber,
                $paddingDigits,
                $isActive,
                $isExhausted,
                $createdAt
            );
            """;

        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$profileId", profileId);
        command.Parameters.AddWithValue("$documentKind", documentKind);
        command.Parameters.AddWithValue("$prefix", prefix);
        command.Parameters.AddWithValue("$year", year);
        command.Parameters.AddWithValue("$startNumber", startNumber);
        command.Parameters.AddWithValue("$currentNumber", currentNumber);
        command.Parameters.AddWithValue("$endNumber", endNumber.HasValue ? endNumber.Value : (object)DBNull.Value);
        command.Parameters.AddWithValue("$paddingDigits", paddingDigits);
        command.Parameters.AddWithValue("$isActive", isActive);
        command.Parameters.AddWithValue("$isExhausted", isExhausted);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void RequireConstraintFailure(Action action, string message)
    {
        try
        {
            action();
        }
        catch (SqliteException)
        {
            return;
        }

        throw new InvalidOperationException(message);
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