using Microsoft.Data.Sqlite;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Migrations;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class J21Rmo242023ElectronicJournalSchemaFoundationChecks
{
    // Kirch strict dev guide:
    // Run() is the CoreSmoke entry point; it proves J21-5C added schema only and no live journal behavior.
    // Syntax note: static lets Program.cs call this guard without constructing a test object.
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string migrationText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Migrations",
            "BirElectronicJournalSchemaMigration.cs");

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
            "J21_5C_ELECTRONIC_JOURNAL_SCHEMA_FOUNDATION.md");

        string planText = ReadRequiredFile(
            repoRoot,
            "00-COMPLIANCE",
            "RMO_24_2023_ELECTRONIC_JOURNAL_SCHEMA_PLAN.md");

        string coreSmokeText = ReadRequiredFile(
            repoRoot,
            "tools",
            "Salryn.Tools.CoreSmoke",
            "Program.cs");

        RequireContains(migrationText, "// Kirch strict dev guide:", "J21-5C migration must include strict guide comments.");
        RequireContains(migrationText, "J21-5C owns the electronic journal schema foundation only.", "J21-5C migration must explain ownership.");
        RequireContains(migrationText, "Do not wire checkout, write sale receipt_snapshot, persist official documents from live sales", "J21-5C migration must block behavior scope creep.");
        RequireContains(migrationText, "public int Version => 4;", "J21-5C migration must be version 4.");
        RequireContains(migrationText, "CREATE TABLE bir_electronic_journal_entries", "J21-5C must create journal entries table.");
        RequireContains(migrationText, "CREATE TABLE bir_electronic_journal_entry_events", "J21-5C must create journal events table.");
        RequireContains(migrationText, "CREATE TABLE bir_electronic_journal_preservation_batches", "J21-5C must create preservation batch table.");
        RequireContains(migrationText, "bir_machine_profile_id TEXT NOT NULL", "Journal entry must be owned by machine profile.");
        RequireContains(migrationText, "official_document_sequence_id TEXT NOT NULL", "Journal entry must reference official sequence.");
        RequireContains(migrationText, "official_document_number TEXT NOT NULL", "Journal entry must include official document number.");
        RequireContains(migrationText, "document_kind TEXT NOT NULL", "Journal entry must include document kind.");
        RequireContains(migrationText, "snapshot_payload_hash TEXT NULL", "Journal entry must include future snapshot hash.");
        RequireContains(migrationText, "event_type TEXT NOT NULL", "Journal events must include event type.");
        RequireContains(migrationText, "original_marker INTEGER NOT NULL DEFAULT 0", "Journal events must include original marker.");
        RequireContains(migrationText, "reprint_marker INTEGER NOT NULL DEFAULT 0", "Journal events must include reprint marker.");
        RequireContains(migrationText, "void_marker INTEGER NOT NULL DEFAULT 0", "Journal events must include void marker.");
        RequireContains(migrationText, "refund_marker INTEGER NOT NULL DEFAULT 0", "Journal events must include refund marker.");
        RequireContains(migrationText, "cancel_marker INTEGER NOT NULL DEFAULT 0", "Journal events must include cancel marker.");
        RequireContains(migrationText, "idx_bir_electronic_journal_entries_doc_identity", "Journal entries must prevent duplicate document identity.");
        RequireContains(migrationText, "idx_bir_electronic_journal_entry_events_type_created", "Journal events must support type/time lookup.");

        RequireContains(factoryText, "Version 4 adds the RMO electronic journal schema foundation without checkout writes or official output.", "Factory must explain version 4.");
        RequireContains(factoryText, "new BirElectronicJournalSchemaMigration()", "Factory must register J21-5C migration.");
        RequireContains(matrixText, "## J21-5C Electronic Journal Schema Foundation", "Compliance matrix must include J21-5C section.");
        RequireContains(handoffText, "Status: schema foundation only.", "J21-5C handoff must preserve schema-only status.");
        RequireContains(handoffText, "No actual journal append service.", "J21-5C handoff must block append service.");
        RequireContains(handoffText, "No official receipt/invoice output claim.", "J21-5C handoff must block official output claim.");
        RequireContains(planText, "future electronic_journal_entries", "J21-5C must be grounded in J21-5B plan.");
        RequireContains(coreSmokeText, "J21Rmo242023ElectronicJournalSchemaFoundationChecks.Run();", "CoreSmoke must run J21-5C checks.");

        RunMigrationShapeCheck();
    }

    // Kirch strict dev guide:
    // RunMigrationShapeCheck() applies migrations on a temp DB and proves the schema exists without touching dev/prod data.
    // Syntax note: try/finally guarantees temp files are cleaned even if a schema assertion fails.
    private static void RunMigrationShapeCheck()
    {
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "Salryn-J21-5C-" + Guid.NewGuid().ToString("N"));

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
                    new BirOfficialDocumentSequenceSchemaMigration(),
                    new BirElectronicJournalSchemaMigration()
                });

            runner.RunPendingMigrations();

            using SqliteConnection connection = factory.CreateConnection();
            connection.Open();

            RequireAppliedMigration(connection, 4, "BIR electronic journal schema");

            RequireTable(connection, "bir_electronic_journal_entries");
            RequireTable(connection, "bir_electronic_journal_entry_events");
            RequireTable(connection, "bir_electronic_journal_preservation_batches");

            RequireColumn(connection, "bir_electronic_journal_entries", "bir_machine_profile_id");
            RequireColumn(connection, "bir_electronic_journal_entries", "sale_id");
            RequireColumn(connection, "bir_electronic_journal_entries", "official_document_sequence_id");
            RequireColumn(connection, "bir_electronic_journal_entries", "official_document_number");
            RequireColumn(connection, "bir_electronic_journal_entries", "document_kind");
            RequireColumn(connection, "bir_electronic_journal_entries", "transaction_timestamp");
            RequireColumn(connection, "bir_electronic_journal_entries", "cashier_user_id");
            RequireColumn(connection, "bir_electronic_journal_entries", "terminal_id");
            RequireColumn(connection, "bir_electronic_journal_entries", "snapshot_payload_json");
            RequireColumn(connection, "bir_electronic_journal_entries", "snapshot_payload_hash");
            RequireColumn(connection, "bir_electronic_journal_entries", "snapshot_hash_algorithm");
            RequireColumn(connection, "bir_electronic_journal_entries", "metadata_json");

            RequireColumn(connection, "bir_electronic_journal_entry_events", "journal_entry_id");
            RequireColumn(connection, "bir_electronic_journal_entry_events", "event_type");
            RequireColumn(connection, "bir_electronic_journal_entry_events", "original_marker");
            RequireColumn(connection, "bir_electronic_journal_entry_events", "reprint_marker");
            RequireColumn(connection, "bir_electronic_journal_entry_events", "void_marker");
            RequireColumn(connection, "bir_electronic_journal_entry_events", "refund_marker");
            RequireColumn(connection, "bir_electronic_journal_entry_events", "cancel_marker");
            RequireColumn(connection, "bir_electronic_journal_entry_events", "preservation_batch_id");

            RequireColumn(connection, "bir_electronic_journal_preservation_batches", "batch_type");
            RequireColumn(connection, "bir_electronic_journal_preservation_batches", "journal_entry_count");
            RequireColumn(connection, "bir_electronic_journal_preservation_batches", "storage_reference");
            RequireColumn(connection, "bir_electronic_journal_preservation_batches", "file_hash");
            RequireColumn(connection, "bir_electronic_journal_preservation_batches", "metadata_json");

            RequireIndex(connection, "idx_bir_electronic_journal_entries_doc_identity");
            RequireIndex(connection, "idx_bir_electronic_journal_entries_machine_time");
            RequireIndex(connection, "idx_bir_electronic_journal_entries_sale");
            RequireIndex(connection, "idx_bir_electronic_journal_entry_events_entry");
            RequireIndex(connection, "idx_bir_electronic_journal_entry_events_type_created");
            RequireIndex(connection, "idx_bir_electronic_journal_preservation_batches_type_created");

            string roleId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
            string userId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
            string profileId = "cccccccc-cccc-cccc-cccc-cccccccccccc";
            string sequenceId = "dddddddd-dddd-dddd-dddd-dddddddddddd";
            string saleId = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee";
            string entryId = "ffffffff-ffff-ffff-ffff-ffffffffffff";
            string batchId = "11111111-2222-3333-4444-555555555555";

            InsertRole(connection, roleId);
            InsertUser(connection, userId, roleId);
            InsertMachineProfile(connection, profileId);
            InsertSequence(connection, sequenceId, profileId);
            InsertSale(connection, saleId, userId);
            InsertJournalEntry(connection, entryId, profileId, saleId, sequenceId, userId, "OR-000001");
            InsertPreservationBatch(connection, batchId, userId);
            InsertJournalEvent(connection, "99999999-9999-9999-9999-999999999999", entryId, userId, "ORIGINAL", 1, 0, 0, 0, 0, null);

            RequireConstraintFailure(
                () => InsertJournalEntry(connection, "12121212-1212-1212-1212-121212121212", profileId, saleId, sequenceId, userId, "OR-000001"),
                "Journal schema must reject duplicate document identity.");

            RequireConstraintFailure(
                () => InsertJournalEvent(connection, "23232323-2323-2323-2323-232323232323", entryId, userId, "REPRINT", 0, 0, 0, 0, 0, null),
                "Journal event schema must reject mismatched reprint marker.");
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

    private static void InsertRole(SqliteConnection connection, string roleId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO roles (id, name, is_system_role, created_at)
            VALUES ($id, 'J21-5C Test Role', 1, $createdAt);
            """;
        command.Parameters.AddWithValue("$id", roleId);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void InsertUser(SqliteConnection connection, string userId, string roleId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO users (
                id,
                username,
                display_name,
                password_hash,
                role_id,
                status,
                created_at
            )
            VALUES (
                $id,
                'j21-5c-user',
                'J21 5C User',
                'hash',
                $roleId,
                1,
                $createdAt
            );
            """;
        command.Parameters.AddWithValue("$id", userId);
        command.Parameters.AddWithValue("$roleId", roleId);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
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
                'Kirch Test Taxpayer',
                'Test Registered Address',
                '000-000-000-000',
                '00000',
                'TEST-SERIAL-001',
                'TEST',
                $createdAt
            );
            """;
        command.Parameters.AddWithValue("$id", profileId);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void InsertSequence(SqliteConnection connection, string sequenceId, string profileId)
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
                'OFFICIAL_RECEIPT',
                'LOCAL_SINGLE_STORE',
                'OR',
                2026,
                1,
                0,
                999999,
                6,
                1,
                0,
                $createdAt
            );
            """;
        command.Parameters.AddWithValue("$id", sequenceId);
        command.Parameters.AddWithValue("$profileId", profileId);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void InsertSale(SqliteConnection connection, string saleId, string userId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sales (
                id,
                receipt_no,
                sale_date,
                cashier_user_id,
                subtotal_cents,
                discount_cents,
                tax_cents,
                total_cents,
                amount_paid_cents,
                change_cents,
                status,
                created_at
            )
            VALUES (
                $id,
                'POS-J21-5C',
                $saleDate,
                $userId,
                10000,
                0,
                0,
                10000,
                10000,
                0,
                1,
                $createdAt
            );
            """;
        command.Parameters.AddWithValue("$id", saleId);
        command.Parameters.AddWithValue("$saleDate", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$userId", userId);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void InsertJournalEntry(
        SqliteConnection connection,
        string entryId,
        string profileId,
        string saleId,
        string sequenceId,
        string userId,
        string officialDocumentNumber)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO bir_electronic_journal_entries (
                id,
                bir_machine_profile_id,
                sale_id,
                official_document_sequence_id,
                official_document_number,
                document_kind,
                transaction_timestamp,
                cashier_user_id,
                terminal_id,
                snapshot_payload_hash,
                snapshot_hash_algorithm,
                created_at
            )
            VALUES (
                $id,
                $profileId,
                $saleId,
                $sequenceId,
                $officialDocumentNumber,
                'OFFICIAL_RECEIPT',
                $transactionTimestamp,
                $userId,
                'TERMINAL-1',
                'HASH-PLACEHOLDER',
                'SHA-256',
                $createdAt
            );
            """;
        command.Parameters.AddWithValue("$id", entryId);
        command.Parameters.AddWithValue("$profileId", profileId);
        command.Parameters.AddWithValue("$saleId", saleId);
        command.Parameters.AddWithValue("$sequenceId", sequenceId);
        command.Parameters.AddWithValue("$officialDocumentNumber", officialDocumentNumber);
        command.Parameters.AddWithValue("$transactionTimestamp", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$userId", userId);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void InsertPreservationBatch(SqliteConnection connection, string batchId, string userId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO bir_electronic_journal_preservation_batches (
                id,
                batch_type,
                journal_entry_count,
                created_by_user_id,
                created_at
            )
            VALUES (
                $id,
                'PRESERVATION',
                1,
                $userId,
                $createdAt
            );
            """;
        command.Parameters.AddWithValue("$id", batchId);
        command.Parameters.AddWithValue("$userId", userId);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void InsertJournalEvent(
        SqliteConnection connection,
        string eventId,
        string entryId,
        string userId,
        string eventType,
        int originalMarker,
        int reprintMarker,
        int voidMarker,
        int refundMarker,
        int cancelMarker,
        string? preservationBatchId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO bir_electronic_journal_entry_events (
                id,
                journal_entry_id,
                event_type,
                original_marker,
                reprint_marker,
                void_marker,
                refund_marker,
                cancel_marker,
                cashier_user_id,
                preservation_batch_id,
                created_at
            )
            VALUES (
                $id,
                $entryId,
                $eventType,
                $originalMarker,
                $reprintMarker,
                $voidMarker,
                $refundMarker,
                $cancelMarker,
                $userId,
                $preservationBatchId,
                $createdAt
            );
            """;
        command.Parameters.AddWithValue("$id", eventId);
        command.Parameters.AddWithValue("$entryId", entryId);
        command.Parameters.AddWithValue("$eventType", eventType);
        command.Parameters.AddWithValue("$originalMarker", originalMarker);
        command.Parameters.AddWithValue("$reprintMarker", reprintMarker);
        command.Parameters.AddWithValue("$voidMarker", voidMarker);
        command.Parameters.AddWithValue("$refundMarker", refundMarker);
        command.Parameters.AddWithValue("$cancelMarker", cancelMarker);
        command.Parameters.AddWithValue("$userId", userId);
        command.Parameters.AddWithValue("$preservationBatchId", preservationBatchId is null ? DBNull.Value : preservationBatchId);
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

    private static void RequireAppliedMigration(SqliteConnection connection, int version, string name)
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

    private static void RequireColumn(SqliteConnection connection, string tableName, string columnName)
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