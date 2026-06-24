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

public static class J21Rmo242023SharedTransactionSequenceAndJournalAppendRuntimeSmokeChecks
{
    public static async Task RunAsync()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string checkText = ReadRequiredFile(repoRoot, "tests", "Salryn.Tests", "J21Rmo242023SharedTransactionSequenceAndJournalAppendRuntimeSmokeChecks.cs");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(checkText, "SharedSqliteOfficialDocumentSequenceAllocationParticipant", "J21-5W must use the shared sequence allocation participant.");
        RequireContains(checkText, "SqliteElectronicJournalAppendParticipant", "J21-5W must use the SQLite journal append participant.");
        RequireContains(checkText, "SqliteSharedTransactionBoundary", "J21-5W must execute both participants inside the shared boundary.");
        RequireContains(coreSmokeText, "J21Rmo242023SharedTransactionSequenceAndJournalAppendRuntimeSmokeChecks.RunAsync().GetAwaiter().GetResult();", "CoreSmoke must run J21-5W checks.");

        await RunCommitAndRollbackSmoke();
    }

    private static async Task RunCommitAndRollbackSmoke()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "Salryn-J21-5W-" + Guid.NewGuid().ToString("N"));

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
            SharedSqliteOfficialDocumentSequenceAllocationParticipant sequenceParticipant = new();
            SqliteElectronicJournalAppendParticipant journalParticipant = new();

            Result<ElectronicJournalAppendPersistenceResult> committedResult =
                await boundary.ExecuteAsync<ElectronicJournalAppendPersistenceResult>(
                    async (context, token) =>
                    {
                        Result<OfficialDocumentSequenceAllocation> allocationResult =
                            await sequenceParticipant.ExecuteAsync(
                                context,
                                CreateSequenceRequest(),
                                token);

                        if (allocationResult.IsFailure)
                        {
                            return Result<ElectronicJournalAppendPersistenceResult>.Fail(allocationResult.ErrorDetail);
                        }

                        ElectronicJournalAppendPersistenceRequest appendRequest = CreateAppendRequest(
                            journalEntryId: "66666666-6666-6666-6666-666666666666",
                            eventId: "77777777-7777-7777-7777-777777777777",
                            allocation: allocationResult.Value,
                            appendedAt: new DateTimeOffset(2026, 6, 24, 14, 0, 2, TimeSpan.Zero));

                        return await journalParticipant.ExecuteAsync(context, appendRequest, token);
                    });

            RequireSuccess(committedResult, "Shared transaction sequence allocation and journal append should commit.");
            RequireEqual("OR-2026-000005", committedResult.Value.OfficialDocumentNumber, "Committed journal must use allocated document number.");
            RequireEqual(5L, ReadCurrentSequenceNumber(factory), "Committed transaction must advance sequence once.");
            RequireTableCount(factory, "bir_electronic_journal_entries", 1);
            RequireTableCount(factory, "bir_electronic_journal_entry_events", 1);
            RequireJournalDocumentNumber(factory, "OR-2026-000005");

            Result<ElectronicJournalAppendPersistenceResult> rollbackResult =
                await boundary.ExecuteAsync<ElectronicJournalAppendPersistenceResult>(
                    async (context, token) =>
                    {
                        Result<OfficialDocumentSequenceAllocation> allocationResult =
                            await sequenceParticipant.ExecuteAsync(
                                context,
                                CreateSequenceRequest(),
                                token);

                        if (allocationResult.IsFailure)
                        {
                            return Result<ElectronicJournalAppendPersistenceResult>.Fail(allocationResult.ErrorDetail);
                        }

                        ElectronicJournalAppendPersistenceRequest appendRequest = CreateAppendRequest(
                            journalEntryId: "88888888-8888-8888-8888-888888888888",
                            eventId: "99999999-9999-9999-9999-999999999999",
                            allocation: allocationResult.Value,
                            appendedAt: new DateTimeOffset(2026, 6, 24, 14, 1, 2, TimeSpan.Zero));

                        Result<ElectronicJournalAppendPersistenceResult> appendResult =
                            await journalParticipant.ExecuteAsync(context, appendRequest, token);

                        if (appendResult.IsFailure)
                        {
                            return appendResult;
                        }

                        return Result<ElectronicJournalAppendPersistenceResult>.Fail(Error.Validation(
                            "J21-5W.IntentionalRollback",
                            "Intentional rollback after shared sequence allocation and journal append."));
                    });

            RequireFailureCode(
                rollbackResult,
                "J21-5W.IntentionalRollback",
                "Rollback path must return the caller failure.");

            RequireEqual(5L, ReadCurrentSequenceNumber(factory), "Rollback must not advance sequence.");
            RequireTableCount(factory, "bir_electronic_journal_entries", 1);
            RequireTableCount(factory, "bir_electronic_journal_entry_events", 1);
            RequireJournalDocumentNumber(factory, "OR-2026-000005");
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

    private static OfficialDocumentSequenceAllocationRequest CreateSequenceRequest()
    {
        return new OfficialDocumentSequenceAllocationRequest(
            BirMachineProfileId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            DocumentKind: "OFFICIAL_RECEIPT",
            SequenceScopeCode: "LOCAL_SINGLE_STORE",
            Year: 2026);
    }

    private static ElectronicJournalAppendPersistenceRequest CreateAppendRequest(
        string journalEntryId,
        string eventId,
        OfficialDocumentSequenceAllocation allocation,
        DateTimeOffset appendedAt)
    {
        ElectronicJournalAppendContract contract = new(
            BirMachineProfileId: allocation.BirMachineProfileId,
            SaleId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            OfficialDocumentSequenceId: allocation.SequenceId,
            OfficialDocumentNumber: allocation.FormattedDocumentNumber,
            DocumentKind: ElectronicJournalDocumentKind.OfficialReceipt,
            TransactionTimestamp: appendedAt.AddSeconds(-2),
            CashierUserId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
            TerminalId: "TERMINAL-1",
            SnapshotEvidence: null,
            OriginalEvent: new ElectronicJournalOriginalEventContract(
                EventId: Guid.Parse(eventId),
                CashierUserId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
                CreatedAt: appendedAt.AddSeconds(-1)),
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
            AppendedAt: appendedAt);
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
                'TEST-SERIAL-J215W',
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
                'DEV-000005',
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

    private static long ReadCurrentSequenceNumber(SqliteConnectionFactory factory)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT current_number
            FROM bir_official_document_sequences
            WHERE id = '33333333-3333-3333-3333-333333333333';
            """;

        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static void RequireJournalDocumentNumber(SqliteConnectionFactory factory, string expectedDocumentNumber)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT official_document_number
            FROM bir_electronic_journal_entries
            WHERE id = '66666666-6666-6666-6666-666666666666';
            """;

        string actual = Convert.ToString(command.ExecuteScalar()) ?? string.Empty;

        RequireEqual(expectedDocumentNumber, actual, "Journal document number must match the committed sequence allocation.");
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

    private static void RequireSuccess<T>(Result<T> result, string message)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{message} Error: {result.ErrorDetail.Code}");
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
