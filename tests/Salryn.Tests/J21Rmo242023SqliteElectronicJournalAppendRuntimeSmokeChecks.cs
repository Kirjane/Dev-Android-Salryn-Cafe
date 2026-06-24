using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Migrations;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class J21Rmo242023SqliteElectronicJournalAppendRuntimeSmokeChecks
{
    public static async Task RunAsync()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string compositionText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteElectronicJournalAppendServiceComposition.cs");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(compositionText, "SqliteElectronicJournalAppendServiceComposition", "J21-5V must use the SQLite append service composition.");
        RequireContains(compositionText, "SqliteElectronicJournalAppendAtomicPersistenceAdapter", "J21-5V must stay behind the atomic persistence adapter.");
        RequireContains(coreSmokeText, "J21Rmo242023SqliteElectronicJournalAppendRuntimeSmokeChecks.RunAsync().GetAwaiter().GetResult();", "CoreSmoke must run J21-5V checks.");

        RequireDoesNotContain(compositionText, "CompleteSale", "Composition must not wire checkout.");
        RequireDoesNotContain(compositionText, "PrintDocument", "Composition must not wire printing.");
        RequireDoesNotContain(compositionText, "RunPendingMigrations", "Composition must not run migrations.");

        await RunRuntimeSmoke();
    }

    private static async Task RunRuntimeSmoke()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "Salryn-J21-5V-" + Guid.NewGuid().ToString("N"));

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
            SeedDependencies(factory);

            Result<IElectronicJournalAppendService> compositionResult =
                SqliteElectronicJournalAppendServiceComposition.Create(
                    new SqliteElectronicJournalAppendServiceCompositionRequest(
                        ConnectionFactory: factory,
                        Validator: new ElectronicJournalAppendContractValidator(),
                        JournalEntryIdFactory: () => Guid.Parse("66666666-6666-6666-6666-666666666666"),
                        Clock: () => new DateTimeOffset(2026, 6, 24, 13, 0, 2, TimeSpan.Zero)));

            RequireSuccess(compositionResult, "SQLite append service composition should succeed.");

            ElectronicJournalAppendContract contract = CreateContract();

            Result<ElectronicJournalAppendResult> appendResult =
                await compositionResult.Value.AppendOriginalAsync(contract);

            RequireSuccess(appendResult, "Composed SQLite append service should append ORIGINAL journal entry.");
            RequireEqual(Guid.Parse("66666666-6666-6666-6666-666666666666"), appendResult.Value.JournalEntryId, "Append result must use configured journal entry id.");
            RequireEqual(Guid.Parse("77777777-7777-7777-7777-777777777777"), appendResult.Value.OriginalEventId, "Append result must preserve ORIGINAL event id.");
            RequireEqual("OR-2026-000004", appendResult.Value.OfficialDocumentNumber, "Append result must preserve official document number.");
            RequireEqual(new DateTimeOffset(2026, 6, 24, 13, 0, 2, TimeSpan.Zero), appendResult.Value.AppendedAt, "Append result must use configured append timestamp.");

            RequireTableCount(factory, "bir_electronic_journal_entries", 1);
            RequireTableCount(factory, "bir_electronic_journal_entry_events", 1);
            RequireJournalEntryShape(factory);
            RequireOriginalEventShape(factory);
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

    private static ElectronicJournalAppendContract CreateContract()
    {
        return new ElectronicJournalAppendContract(
            BirMachineProfileId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            SaleId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            OfficialDocumentSequenceId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            OfficialDocumentNumber: "OR-2026-000004",
            DocumentKind: ElectronicJournalDocumentKind.OfficialReceipt,
            TransactionTimestamp: new DateTimeOffset(2026, 6, 24, 13, 0, 0, TimeSpan.Zero),
            CashierUserId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
            TerminalId: "TERMINAL-1",
            SnapshotEvidence: new ElectronicJournalSnapshotEvidence(
                PayloadJson: """{"status":"runtime-smoke"}""",
                PayloadHash: "HASH-J21-5V",
                HashAlgorithm: "TEST-HASH"),
            OriginalEvent: new ElectronicJournalOriginalEventContract(
                EventId: Guid.Parse("77777777-7777-7777-7777-777777777777"),
                CashierUserId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
                CreatedAt: new DateTimeOffset(2026, 6, 24, 13, 0, 1, TimeSpan.Zero)),
            AtomicBoundarySteps: new[]
            {
                ElectronicJournalAtomicBoundaryStep.OfficialSequenceAllocation,
                ElectronicJournalAtomicBoundaryStep.SnapshotPayloadHashPersistence,
                ElectronicJournalAtomicBoundaryStep.JournalAnchorInsert,
                ElectronicJournalAtomicBoundaryStep.OriginalEventInsert
            },
            DeferredComplianceNotes: Array.Empty<string>());
    }

    private static void SeedDependencies(SqliteConnectionFactory factory)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        InsertRole(connection);
        InsertUser(connection);
        InsertMachineProfile(connection);
        InsertSequence(connection);
        InsertSale(connection);
    }

    private static void InsertRole(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO roles (id, name, is_system_role, created_at)
            VALUES ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 'Cashier', 1, $createdAt);
            """;
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void InsertUser(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO users (id, username, display_name, password_hash, role_id, status, created_at)
            VALUES (
                '44444444-4444-4444-4444-444444444444',
                'cashier',
                'cashier',
                'HASH',
                'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
                1,
                $createdAt
            );
            """;
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void InsertMachineProfile(SqliteConnection connection)
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
                compliance_mode,
                official_output_enabled,
                is_active,
                created_at
            )
            VALUES (
                '11111111-1111-1111-1111-111111111111',
                'Kirch Test Taxpayer',
                'Test Registered Address',
                '000-000-000-000',
                '00000',
                'TEST-SERIAL-J215V',
                'TEST',
                'ACCREDITED',
                1,
                1,
                $createdAt
            );
            """;
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void InsertSequence(SqliteConnection connection)
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
                '33333333-3333-3333-3333-333333333333',
                '11111111-1111-1111-1111-111111111111',
                'OFFICIAL_RECEIPT',
                'LOCAL_SINGLE_STORE',
                'OR',
                2026,
                1,
                4,
                99,
                6,
                1,
                0,
                $createdAt
            );
            """;
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void InsertSale(SqliteConnection connection)
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
                '22222222-2222-2222-2222-222222222222',
                'DEV-000004',
                $saleDate,
                '44444444-4444-4444-4444-444444444444',
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
        command.Parameters.AddWithValue("$saleDate", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void RequireJournalEntryShape(SqliteConnectionFactory factory)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                bir_machine_profile_id,
                sale_id,
                official_document_sequence_id,
                official_document_number,
                document_kind,
                cashier_user_id,
                terminal_id,
                snapshot_payload_hash,
                snapshot_hash_algorithm
            FROM bir_electronic_journal_entries
            WHERE id = '66666666-6666-6666-6666-666666666666';
            """;

        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            throw new InvalidOperationException("Expected electronic journal entry was not found.");
        }

        RequireEqual("11111111-1111-1111-1111-111111111111", reader.GetString(0), "Journal entry machine profile id mismatch.");
        RequireEqual("22222222-2222-2222-2222-222222222222", reader.GetString(1), "Journal entry sale id mismatch.");
        RequireEqual("33333333-3333-3333-3333-333333333333", reader.GetString(2), "Journal entry sequence id mismatch.");
        RequireEqual("OR-2026-000004", reader.GetString(3), "Journal entry official document number mismatch.");
        RequireEqual("OFFICIAL_RECEIPT", reader.GetString(4), "Journal entry document kind mismatch.");
        RequireEqual("44444444-4444-4444-4444-444444444444", reader.GetString(5), "Journal entry cashier id mismatch.");
        RequireEqual("TERMINAL-1", reader.GetString(6), "Journal entry terminal id mismatch.");
        RequireEqual("HASH-J21-5V", reader.GetString(7), "Journal entry snapshot hash mismatch.");
        RequireEqual("TEST-HASH", reader.GetString(8), "Journal entry snapshot hash algorithm mismatch.");
    }

    private static void RequireOriginalEventShape(SqliteConnectionFactory factory)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                journal_entry_id,
                event_type,
                original_marker,
                reprint_marker,
                void_marker,
                refund_marker,
                cancel_marker,
                cashier_user_id
            FROM bir_electronic_journal_entry_events
            WHERE id = '77777777-7777-7777-7777-777777777777';
            """;

        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            throw new InvalidOperationException("Expected ORIGINAL electronic journal event was not found.");
        }

        RequireEqual("66666666-6666-6666-6666-666666666666", reader.GetString(0), "ORIGINAL event journal entry id mismatch.");
        RequireEqual("ORIGINAL", reader.GetString(1), "ORIGINAL event type mismatch.");
        RequireEqual(1, reader.GetInt32(2), "ORIGINAL marker mismatch.");
        RequireEqual(0, reader.GetInt32(3), "Reprint marker mismatch.");
        RequireEqual(0, reader.GetInt32(4), "Void marker mismatch.");
        RequireEqual(0, reader.GetInt32(5), "Refund marker mismatch.");
        RequireEqual(0, reader.GetInt32(6), "Cancel marker mismatch.");
        RequireEqual("44444444-4444-4444-4444-444444444444", reader.GetString(7), "ORIGINAL event cashier id mismatch.");
    }

    private static void RequireTableCount(SqliteConnectionFactory factory, string tableName, long expectedCount)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";

        long count = Convert.ToInt64(command.ExecuteScalar());

        if (count != expectedCount)
        {
            throw new InvalidOperationException($"Expected {tableName} count {expectedCount}, found {count}.");
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

    private static void RequireSuccess<T>(Result<T> result, string message)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{message} Error: {result.ErrorDetail.Code}");
        }
    }

    private static void RequireEqual<T>(T expected, T actual, string message)
        where T : IEquatable<T>
    {
        if (!expected.Equals(actual))
        {
            throw new InvalidOperationException(message);
        }
    }
}
