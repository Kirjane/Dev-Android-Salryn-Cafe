using Microsoft.Data.Sqlite;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Migrations;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class J21Rmo242023ElectronicJournalSchemaHardeningChecks
{
    // Kirch strict dev guide:
    // Run() is the CoreSmoke entry point; it proves J21-5E added DB hardening only and no app behavior.
    // Syntax note: static lets Program.cs call this check without constructing a test object.
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string migrationText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Migrations",
            "BirElectronicJournalSchemaHardeningMigration.cs");

        string versionFourMigrationText = ReadRequiredFile(
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

        string handoffText = ReadRequiredFile(
            repoRoot,
            "00-HANDOFF",
            "J21_5E_ELECTRONIC_JOURNAL_SCHEMA_HARDENING.md");

        string matrixText = ReadRequiredFile(
            repoRoot,
            "00-COMPLIANCE",
            "RMO_24_2023_COMPLIANCE_MATRIX.md");

        string auditText = ReadRequiredFile(
            repoRoot,
            "00-COMPLIANCE",
            "RMO_24_2023_ELECTRONIC_JOURNAL_DATABASE_RISK_AUDIT.md");

        string coreSmokeText = ReadRequiredFile(
            repoRoot,
            "tools",
            "Salryn.Tools.CoreSmoke",
            "Program.cs");

        RequireContains(migrationText, "public int Version => 5;", "J21-5E migration must be version 5.");
        RequireContains(migrationText, "BIR electronic journal schema hardening", "J21-5E migration name must be explicit.");
        RequireContains(migrationText, "J21-5E owns electronic journal schema hardening only.", "J21-5E migration must explain ownership.");
        RequireContains(migrationText, "Do not edit the committed version 4 BirElectronicJournalSchemaMigration.", "J21-5E migration must block v4 edits.");
        RequireContains(migrationText, "trg_bir_electronic_journal_entries_no_update", "J21-5E must block journal entry updates.");
        RequireContains(migrationText, "trg_bir_electronic_journal_entries_no_delete", "J21-5E must block journal entry deletes.");
        RequireContains(migrationText, "trg_bir_electronic_journal_entry_events_no_update", "J21-5E must block journal event updates.");
        RequireContains(migrationText, "trg_bir_electronic_journal_entry_events_no_delete", "J21-5E must block journal event deletes.");
        RequireContains(migrationText, "trg_bir_electronic_journal_preservation_batches_no_update", "J21-5E must block preservation batch updates.");
        RequireContains(migrationText, "trg_bir_electronic_journal_preservation_batches_no_delete", "J21-5E must block preservation batch deletes.");
        RequireContains(migrationText, "trg_bir_electronic_journal_entry_events_reversal_reason_required", "J21-5E must require reversal reasons.");
        RequireContains(migrationText, "trg_bir_electronic_journal_entry_events_preservation_batch_required", "J21-5E must require preservation batch links.");
        RequireContains(migrationText, "idx_bir_electronic_journal_entry_events_single_original", "J21-5E must enforce one original event.");
        RequireContains(migrationText, "Renderer and official snapshot persistence do not exist yet.", "J21-5E must defer snapshot hardening.");

        RequireContains(versionFourMigrationText, "public int Version => 4;", "J21-5E must leave version 4 migration intact.");
        RequireContains(factoryText, "Version 5 adds electronic journal DB hardening triggers without app behavior.", "Factory must explain version 5.");
        RequireContains(factoryText, "new BirElectronicJournalSchemaHardeningMigration()", "Factory must register J21-5E migration.");
        RequireContains(handoffText, "Status: DB hardening only.", "J21-5E handoff must be DB hardening only.");
        RequireContains(handoffText, "No append service.", "J21-5E handoff must block append service.");
        RequireContains(handoffText, "No checkout wiring.", "J21-5E handoff must block checkout wiring.");
        RequireContains(handoffText, "No official receipt/invoice output claim.", "J21-5E handoff must block official output claim.");
        RequireContains(handoffText, "No snapshot hash NOT NULL constraint.", "J21-5E handoff must block snapshot NOT NULL hardening.");
        RequireContains(matrixText, "## J21-5E Electronic Journal Schema Hardening Foundation", "Compliance matrix must include J21-5E section.");
        RequireContains(auditText, "Use a new forward migration for any future schema hardening.", "J21-5E must be grounded in J21-5D audit.");
        RequireContains(coreSmokeText, "J21Rmo242023ElectronicJournalSchemaHardeningChecks.Run();", "CoreSmoke must run J21-5E checks.");

        RequireDoesNotContain(migrationText, "snapshot_payload_hash TEXT NOT NULL", "J21-5E must not make snapshot hash NOT NULL.");
        RequireDoesNotContain(migrationText, "snapshot_payload_json TEXT NOT NULL", "J21-5E must not make snapshot payload NOT NULL.");
        RequireDoesNotContain(migrationText, "OfficialDocumentSequenceAllocationService", "J21-5E must not touch sequence allocation service.");
        RequireDoesNotContain(migrationText, "AllocateNextAsync", "J21-5E must not allocate sequence numbers.");

        RunMigrationHardeningCheck();
    }

    // Kirch strict dev guide:
    // RunMigrationHardeningCheck() applies migrations 1-5 on a temp DB and proves only DB hardening was added.
    // Syntax note: try/finally deletes temp files even when a trigger assertion fails.
    private static void RunMigrationHardeningCheck()
    {
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "Salryn-J21-5E-" + Guid.NewGuid().ToString("N"));

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
                    new BirElectronicJournalSchemaMigration(),
                    new BirElectronicJournalSchemaHardeningMigration()
                });

            runner.RunPendingMigrations();

            using SqliteConnection connection = factory.CreateConnection();
            connection.Open();

            RequireAppliedMigration(connection, 5, "BIR electronic journal schema hardening");

            RequireTrigger(connection, "trg_bir_electronic_journal_entries_no_update");
            RequireTrigger(connection, "trg_bir_electronic_journal_entries_no_delete");
            RequireTrigger(connection, "trg_bir_electronic_journal_entry_events_no_update");
            RequireTrigger(connection, "trg_bir_electronic_journal_entry_events_no_delete");
            RequireTrigger(connection, "trg_bir_electronic_journal_preservation_batches_no_update");
            RequireTrigger(connection, "trg_bir_electronic_journal_preservation_batches_no_delete");
            RequireTrigger(connection, "trg_bir_electronic_journal_entry_events_reversal_reason_required");
            RequireTrigger(connection, "trg_bir_electronic_journal_entry_events_preservation_batch_required");
            RequireIndex(connection, "idx_bir_electronic_journal_entry_events_single_original");

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
            InsertJournalEvent(connection, "99999999-9999-9999-9999-999999999999", entryId, userId, "ORIGINAL", 1, 0, 0, 0, 0, null, null);

            RequireConstraintFailure(
                () => ExecuteNonQuery(connection, "UPDATE bir_electronic_journal_entries SET metadata_json = '{}' WHERE id = 'ffffffff-ffff-ffff-ffff-ffffffffffff';"),
                "Journal entry UPDATE must be blocked.");

            RequireConstraintFailure(
                () => ExecuteNonQuery(connection, "DELETE FROM bir_electronic_journal_entries WHERE id = 'ffffffff-ffff-ffff-ffff-ffffffffffff';"),
                "Journal entry DELETE must be blocked.");

            RequireConstraintFailure(
                () => ExecuteNonQuery(connection, "UPDATE bir_electronic_journal_entry_events SET metadata_json = '{}' WHERE id = '99999999-9999-9999-9999-999999999999';"),
                "Journal event UPDATE must be blocked.");

            RequireConstraintFailure(
                () => ExecuteNonQuery(connection, "DELETE FROM bir_electronic_journal_entry_events WHERE id = '99999999-9999-9999-9999-999999999999';"),
                "Journal event DELETE must be blocked.");

            RequireConstraintFailure(
                () => ExecuteNonQuery(connection, "UPDATE bir_electronic_journal_preservation_batches SET metadata_json = '{}' WHERE id = '11111111-2222-3333-4444-555555555555';"),
                "Preservation batch UPDATE must be blocked.");

            RequireConstraintFailure(
                () => ExecuteNonQuery(connection, "DELETE FROM bir_electronic_journal_preservation_batches WHERE id = '11111111-2222-3333-4444-555555555555';"),
                "Preservation batch DELETE must be blocked.");

            RequireConstraintFailure(
                () => InsertJournalEvent(connection, "22222222-2222-2222-2222-222222222222", entryId, userId, "VOID", 0, 0, 1, 0, 0, null, null),
                "VOID without reason must fail.");

            RequireConstraintFailure(
                () => InsertJournalEvent(connection, "33333333-3333-3333-3333-333333333333", entryId, userId, "REFUND", 0, 0, 0, 1, 0, null, null),
                "REFUND without reason must fail.");

            RequireConstraintFailure(
                () => InsertJournalEvent(connection, "44444444-4444-4444-4444-444444444444", entryId, userId, "CANCEL", 0, 0, 0, 0, 1, null, null),
                "CANCEL without reason must fail.");

            InsertJournalEvent(connection, "55555555-5555-5555-5555-555555555555", entryId, userId, "VOID", 0, 0, 1, 0, 0, "Test void reason", null);
            InsertJournalEvent(connection, "66666666-6666-6666-6666-666666666666", entryId, userId, "REFUND", 0, 0, 0, 1, 0, "Test refund reason", null);
            InsertJournalEvent(connection, "77777777-7777-7777-7777-777777777777", entryId, userId, "CANCEL", 0, 0, 0, 0, 1, "Test cancel reason", null);

            RequireConstraintFailure(
                () => InsertJournalEvent(connection, "88888888-8888-8888-8888-888888888888", entryId, userId, "EXPORT", 0, 0, 0, 0, 0, null, null),
                "EXPORT without preservation_batch_id must fail.");

            RequireConstraintFailure(
                () => InsertJournalEvent(connection, "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", entryId, userId, "PRESERVATION", 0, 0, 0, 0, 0, null, null),
                "PRESERVATION without preservation_batch_id must fail.");

            RequireConstraintFailure(
                () => InsertJournalEvent(connection, "bbbbbbbb-cccc-dddd-eeee-ffffffffffff", entryId, userId, "BACKUP_RECOVERY", 0, 0, 0, 0, 0, null, null),
                "BACKUP_RECOVERY without preservation_batch_id must fail.");

            InsertJournalEvent(connection, "cccccccc-dddd-eeee-ffff-aaaaaaaaaaaa", entryId, userId, "EXPORT", 0, 0, 0, 0, 0, null, batchId);
            InsertJournalEvent(connection, "dddddddd-eeee-ffff-aaaa-bbbbbbbbbbbb", entryId, userId, "PRESERVATION", 0, 0, 0, 0, 0, null, batchId);
            InsertJournalEvent(connection, "eeeeeeee-ffff-aaaa-bbbb-cccccccccccc", entryId, userId, "BACKUP_RECOVERY", 0, 0, 0, 0, 0, null, batchId);

            RequireConstraintFailure(
                () => InsertJournalEvent(connection, "12121212-1212-1212-1212-121212121212", entryId, userId, "ORIGINAL", 1, 0, 0, 0, 0, null, null),
                "Duplicate ORIGINAL event for the same journal entry must fail.");
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
            VALUES ($id, 'J21-5E Test Role', 1, $createdAt);
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
                'j21-5e-user',
                'J21 5E User',
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
                'POS-J21-5E',
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
        string? reason,
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
                reason,
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
                $reason,
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
        command.Parameters.AddWithValue("$reason", reason is null ? DBNull.Value : reason);
        command.Parameters.AddWithValue("$preservationBatchId", preservationBatchId is null ? DBNull.Value : preservationBatchId);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void ExecuteNonQuery(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
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

    private static void RequireTrigger(SqliteConnection connection, string triggerName)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'trigger'
              AND name = $triggerName;
            """;
        command.Parameters.AddWithValue("$triggerName", triggerName);

        long count = Convert.ToInt64(command.ExecuteScalar());

        if (count != 1)
        {
            throw new InvalidOperationException($"Required trigger was not found: {triggerName}");
        }
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

    private static void RequireDoesNotContain(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}