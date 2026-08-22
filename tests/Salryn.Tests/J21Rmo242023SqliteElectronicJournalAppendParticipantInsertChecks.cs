using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Repositories;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Migrations;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Data.Transactions;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class J21Rmo242023SqliteElectronicJournalAppendParticipantInsertChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string participantText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteElectronicJournalAppendParticipant.cs");
        string hardeningText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Migrations", "BirElectronicJournalSchemaHardeningMigration.cs");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(participantText, "INSERT INTO bir_electronic_journal_entries", "J21-5S must insert journal entry anchors.");
        RequireContains(participantText, "INSERT INTO bir_electronic_journal_entry_events", "J21-5S must insert ORIGINAL journal event.");
        RequireContains(participantText, "original_marker", "J21-5S must mark ORIGINAL event.");
        RequireContains(participantText, "ElectronicJournalAppend.SqliteAppendFailed", "J21-5S must map SQLite append failures.");
        RequireContains(participantText, "OriginalEventCashierMismatch", "J21-5S must guard cashier mismatch.");
        RequireContains(hardeningText, "idx_bir_electronic_journal_entry_events_single_original", "J21-5S must stay aligned with single ORIGINAL hardening.");
        RequireContains(coreSmokeText, "J21Rmo242023SqliteElectronicJournalAppendParticipantInsertChecks.Run();", "CoreSmoke must run J21-5S checks.");

        RequireDoesNotContain(participantText, "CreateOpenConnection", "J21-5S participant must not open its own connection.");
        RequireDoesNotContain(participantText, "BeginTransaction", "J21-5S participant must not begin transactions.");
        RequireDoesNotContain(participantText, ".Commit(", "J21-5S participant must not commit.");
        RequireDoesNotContain(participantText, ".Rollback(", "J21-5S participant must not rollback.");
        RequireDoesNotContain(participantText, "OfficialDocumentSequenceAllocation", "J21-5S participant must not allocate official sequence numbers.");
        RequireDoesNotContain(participantText, "CompleteSale", "J21-5S must not wire checkout.");
        RequireDoesNotContain(participantText, "Crystal", "J21-5S must not wire Crystal Reports.");
        RequireDoesNotContain(participantText, "PrintDocument", "J21-5S must not wire printing.");

        RunRuntimeChecks();
    }

    private static void RunRuntimeChecks()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "Salryn-J21-5S-" + Guid.NewGuid().ToString("N"));

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

            SqliteSharedTransactionBoundary boundary = new(factory);
            SqliteElectronicJournalAppendParticipant participant = new();

            ElectronicJournalAppendPersistenceRequest firstRequest = CreateValidRequest(
                journalEntryId: "66666666-6666-6666-6666-666666666666",
                eventId: "77777777-7777-7777-7777-777777777777",
                documentNumber: "OR-2026-000001");

            Result<ElectronicJournalAppendPersistenceResult> firstResult =
                boundary.ExecuteAsync<ElectronicJournalAppendPersistenceResult>(
                    (context, token) => participant.ExecuteAsync(context, firstRequest, token)).GetAwaiter().GetResult();

            RequireSuccess(firstResult, "Journal append insert should succeed.");
            RequireEqual(firstRequest.JournalEntryId, firstResult.Value.JournalEntryId, "Result must preserve journal entry id.");
            RequireEqual(firstRequest.Contract.OriginalEvent.EventId, firstResult.Value.OriginalEventId, "Result must preserve ORIGINAL event id.");
            RequireEqual("OR-2026-000001", firstResult.Value.OfficialDocumentNumber, "Result must preserve official document number.");
            RequireTableCount(factory, "bir_electronic_journal_entries", 1);
            RequireTableCount(factory, "bir_electronic_journal_entry_events", 1);
            RequireOriginalEventShape(factory, firstRequest.JournalEntryId.ToString("D"));

            ElectronicJournalAppendPersistenceRequest rollbackRequest = CreateValidRequest(
                journalEntryId: "88888888-8888-8888-8888-888888888888",
                eventId: "99999999-9999-9999-9999-999999999999",
                documentNumber: "OR-2026-000002");

            Result<ElectronicJournalAppendPersistenceResult> rollbackResult =
                boundary.ExecuteAsync<ElectronicJournalAppendPersistenceResult>(
                    async (context, token) =>
                    {
                        Result<ElectronicJournalAppendPersistenceResult> appendResult =
                            await participant.ExecuteAsync(context, rollbackRequest, token);

                        if (appendResult.IsFailure)
                        {
                            return appendResult;
                        }

                        return Result<ElectronicJournalAppendPersistenceResult>.Fail(Error.Validation(
                            "J21-5S.IntentionalRollback",
                            "Intentional rollback after journal append insert."));
                    }).GetAwaiter().GetResult();

            RequireFailureCode(rollbackResult, "J21-5S.IntentionalRollback", "Rollback transaction should surface caller failure.");
            RequireTableCount(factory, "bir_electronic_journal_entries", 1);
            RequireTableCount(factory, "bir_electronic_journal_entry_events", 1);

            ElectronicJournalAppendPersistenceRequest mismatchRequest = CreateCashierMismatchRequest();

            Result<ElectronicJournalAppendPersistenceResult> mismatchResult =
                boundary.ExecuteAsync<ElectronicJournalAppendPersistenceResult>(
                    (context, token) => participant.ExecuteAsync(context, mismatchRequest, token)).GetAwaiter().GetResult();

            RequireFailureCode(mismatchResult, "ElectronicJournalAppend.OriginalEventCashierMismatch", "Cashier mismatch must fail before DB insert.");
            RequireTableCount(factory, "bir_electronic_journal_entries", 1);
            RequireTableCount(factory, "bir_electronic_journal_entry_events", 1);
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

    private static void SeedDependencies(SqliteConnectionFactory factory)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        InsertRole(connection);
        InsertUser(connection, "44444444-4444-4444-4444-444444444444", "cashier");
        InsertUser(connection, "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "othercashier");
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

    private static void InsertUser(SqliteConnection connection, string userId, string username)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO users (id, username, display_name, password_hash, role_id, status, created_at)
            VALUES ($id, $username, $displayName, 'HASH', 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 1, $createdAt);
            """;
        command.Parameters.AddWithValue("$id", userId);
        command.Parameters.AddWithValue("$username", username);
        command.Parameters.AddWithValue("$displayName", username);
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
                'TEST-SERIAL-J215S',
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
                2,
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
                'DEV-000001',
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

    private static ElectronicJournalAppendPersistenceRequest CreateValidRequest(
        string journalEntryId,
        string eventId,
        string documentNumber)
    {
        ElectronicJournalAppendContract contract = new(
            BirMachineProfileId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            SaleId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            OfficialDocumentSequenceId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            OfficialDocumentNumber: documentNumber,
            DocumentKind: ElectronicJournalDocumentKind.OfficialReceipt,
            TransactionTimestamp: new DateTimeOffset(2026, 6, 24, 10, 0, 0, TimeSpan.Zero),
            CashierUserId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
            TerminalId: "TERMINAL-1",
            SnapshotEvidence: new ElectronicJournalSnapshotEvidence(
                PayloadJson: """{"status":"draft"}""",
                PayloadHash: "HASH-123",
                HashAlgorithm: "TEST-HASH"),
            OriginalEvent: new ElectronicJournalOriginalEventContract(
                EventId: Guid.Parse(eventId),
                CashierUserId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
                CreatedAt: new DateTimeOffset(2026, 6, 24, 10, 0, 1, TimeSpan.Zero)),
            AtomicBoundarySteps: new[]
            {
                ElectronicJournalAtomicBoundaryStep.OfficialSequenceAllocation,
                ElectronicJournalAtomicBoundaryStep.JournalAnchorInsert,
                ElectronicJournalAtomicBoundaryStep.OriginalEventInsert
            },
            DeferredComplianceNotes: Array.Empty<string>());

        return new ElectronicJournalAppendPersistenceRequest(
            JournalEntryId: Guid.Parse(journalEntryId),
            Contract: contract,
            AppendedAt: new DateTimeOffset(2026, 6, 24, 10, 0, 2, TimeSpan.Zero));
    }

    private static ElectronicJournalAppendPersistenceRequest CreateCashierMismatchRequest()
    {
        ElectronicJournalAppendPersistenceRequest request = CreateValidRequest(
            journalEntryId: "cccccccc-cccc-cccc-cccc-cccccccccccc",
            eventId: "dddddddd-dddd-dddd-dddd-dddddddddddd",
            documentNumber: "OR-2026-000003");

        ElectronicJournalAppendContract source = request.Contract;

        ElectronicJournalAppendContract mismatchContract = source with
        {
            OriginalEvent = source.OriginalEvent with
            {
                CashierUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
            }
        };

        return request with
        {
            Contract = mismatchContract
        };
    }

    private static void RequireOriginalEventShape(SqliteConnectionFactory factory, string journalEntryId)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT event_type, original_marker, reprint_marker, void_marker, refund_marker, cancel_marker
            FROM bir_electronic_journal_entry_events
            WHERE journal_entry_id = $journalEntryId;
            """;
        command.Parameters.AddWithValue("$journalEntryId", journalEntryId);

        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            throw new InvalidOperationException("Expected ORIGINAL event was not found.");
        }

        string eventType = reader.GetString(0);
        int originalMarker = reader.GetInt32(1);
        int reprintMarker = reader.GetInt32(2);
        int voidMarker = reader.GetInt32(3);
        int refundMarker = reader.GetInt32(4);
        int cancelMarker = reader.GetInt32(5);

        if (eventType != "ORIGINAL" ||
            originalMarker != 1 ||
            reprintMarker != 0 ||
            voidMarker != 0 ||
            refundMarker != 0 ||
            cancelMarker != 0)
        {
            throw new InvalidOperationException("ORIGINAL event marker shape is invalid.");
        }
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
            throw new InvalidOperationException($"{message} Error: {result.Error}");
        }
    }

    private static void RequireFailureCode<T>(Result<T> result, string expectedCode, string message)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException(message);
        }

        if (!string.Equals(result.ErrorDetail.Code, expectedCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected error code {expectedCode}, found {result.ErrorDetail.Code}.");
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
