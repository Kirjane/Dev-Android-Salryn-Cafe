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

public static class J21Rmo242023SqliteElectronicJournalAppendParticipantSkeletonChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string participantText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteElectronicJournalAppendParticipant.cs");
        string schemaText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Migrations", "BirElectronicJournalSchemaMigration.cs");
        string hardeningText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Migrations", "BirElectronicJournalSchemaHardeningMigration.cs");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(participantText, "SqliteElectronicJournalAppendParticipant", "J21-5R participant skeleton must exist.");
        RequireContains(participantText, "ISqliteSharedTransactionParticipant<ElectronicJournalAppendPersistenceRequest, ElectronicJournalAppendPersistenceResult>", "J21-5R must implement shared transaction participant shape.");
        RequireContains(participantText, "SqliteParticipantNotImplemented", "J21-5R must explicitly fail as skeleton-only.");
        RequireContains(participantText, "ToDatabaseDocumentKind", "J21-5R must reserve document-kind database mapping.");
        RequireContains(participantText, "OriginalEventType = \"ORIGINAL\"", "J21-5R must reserve ORIGINAL event mapping.");
        RequireContains(schemaText, "bir_electronic_journal_entries", "J21-5R must stay aligned with journal entry schema.");
        RequireContains(schemaText, "bir_electronic_journal_entry_events", "J21-5R must stay aligned with journal event schema.");
        RequireContains(hardeningText, "idx_bir_electronic_journal_entry_events_single_original", "J21-5R must respect single ORIGINAL hardening.");
        RequireContains(coreSmokeText, "J21Rmo242023SqliteElectronicJournalAppendParticipantSkeletonChecks.Run();", "CoreSmoke must run J21-5R checks.");

        RequireDoesNotContain(participantText, "CreateOpenConnection", "J21-5R participant must not open its own connection.");
        RequireDoesNotContain(participantText, "BeginTransaction", "J21-5R participant must not begin transactions.");
        RequireDoesNotContain(participantText, ".Commit(", "J21-5R participant must not commit.");
        RequireDoesNotContain(participantText, ".Rollback(", "J21-5R participant must not rollback.");
        RequireDoesNotContain(participantText, "ExecuteNonQuery", "J21-5R skeleton must not write journal rows yet.");
        RequireDoesNotContain(participantText, "INSERT INTO bir_electronic_journal_entries", "J21-5R skeleton must not insert journal entry rows yet.");
        RequireDoesNotContain(participantText, "INSERT INTO bir_electronic_journal_entry_events", "J21-5R skeleton must not insert journal event rows yet.");
        RequireDoesNotContain(participantText, "OfficialDocumentSequenceAllocation", "J21-5R participant must not allocate official sequence numbers.");
        RequireDoesNotContain(participantText, "CompleteSale", "J21-5R must not wire checkout.");
        RequireDoesNotContain(participantText, "Crystal", "J21-5R must not wire Crystal Reports.");
        RequireDoesNotContain(participantText, "PrintDocument", "J21-5R must not wire printing.");

        RunRuntimeChecks();
    }

    private static void RunRuntimeChecks()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "Salryn-J21-5R-" + Guid.NewGuid().ToString("N"));

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

            SqliteSharedTransactionBoundary boundary = new(factory);
            SqliteElectronicJournalAppendParticipant participant = new();

            Result<ElectronicJournalAppendPersistenceResult> missingRequest =
                boundary.ExecuteAsync<ElectronicJournalAppendPersistenceResult>(
                    (context, token) => participant.ExecuteAsync(context, null!, token)).GetAwaiter().GetResult();

            RequireFailureCode(missingRequest, "ElectronicJournalAppend.PersistenceRequestRequired", "Missing persistence request must fail validation.");

            Result<ElectronicJournalAppendPersistenceResult> skeletonResult =
                boundary.ExecuteAsync<ElectronicJournalAppendPersistenceResult>(
                    (context, token) => participant.ExecuteAsync(context, CreateValidRequest(), token)).GetAwaiter().GetResult();

            RequireFailureCode(skeletonResult, "ElectronicJournalAppend.SqliteParticipantNotImplemented", "J21-5R must remain skeleton-only.");

            RequireJournalEntryCount(factory, 0);
            RequireJournalEventCount(factory, 0);
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

    private static ElectronicJournalAppendPersistenceRequest CreateValidRequest()
    {
        ElectronicJournalAppendContract contract = new(
            BirMachineProfileId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            SaleId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            OfficialDocumentSequenceId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            OfficialDocumentNumber: "OR-2026-000001",
            DocumentKind: ElectronicJournalDocumentKind.OfficialReceipt,
            TransactionTimestamp: new DateTimeOffset(2026, 6, 24, 10, 0, 0, TimeSpan.Zero),
            CashierUserId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
            TerminalId: "TERMINAL-1",
            SnapshotEvidence: null,
            OriginalEvent: new ElectronicJournalOriginalEventContract(
                EventId: Guid.Parse("55555555-5555-5555-5555-555555555555"),
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
            JournalEntryId: Guid.Parse("66666666-6666-6666-6666-666666666666"),
            Contract: contract,
            AppendedAt: new DateTimeOffset(2026, 6, 24, 10, 0, 2, TimeSpan.Zero));
    }

    private static void RequireJournalEntryCount(SqliteConnectionFactory factory, long expectedCount)
    {
        RequireTableCount(factory, "bir_electronic_journal_entries", expectedCount);
    }

    private static void RequireJournalEventCount(SqliteConnectionFactory factory, long expectedCount)
    {
        RequireTableCount(factory, "bir_electronic_journal_entry_events", expectedCount);
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
}
