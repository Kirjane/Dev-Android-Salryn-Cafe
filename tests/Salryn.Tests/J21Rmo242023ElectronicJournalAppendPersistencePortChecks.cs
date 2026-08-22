using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Repositories;

namespace Salryn.Tests;

public static class J21Rmo242023ElectronicJournalAppendPersistencePortChecks
{
    public static async Task RunAsync()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string portsText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Repositories", "ElectronicJournalAppendPersistencePorts.cs");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(portsText, "ElectronicJournalAppendPersistenceRequest", "J21-5I must define persistence request shape.");
        RequireContains(portsText, "ElectronicJournalAppendPersistenceResult", "J21-5I must define persistence result shape.");
        RequireContains(portsText, "IElectronicJournalAppendPersistencePort", "J21-5I must define append persistence port.");
        RequireContains(portsText, "IElectronicJournalAppendAtomicPersistencePort", "J21-5I must define atomic persistence port.");
        RequireContains(portsText, "AppendOriginalAsync", "J21-5I must define original append persistence method.");
        RequireContains(portsText, "CommitOriginalAppendAsync", "J21-5I must define atomic original append method.");

        RequireDoesNotContain(portsText, "Microsoft.Data.Sqlite", "J21-5I must not depend on SQLite.");
        RequireDoesNotContain(portsText, "SqliteConnection", "J21-5I must not open database connections.");
        RequireDoesNotContain(portsText, "IDatabaseMigration", "J21-5I must not define migrations.");
        RequireDoesNotContain(portsText, "CREATE TABLE", "J21-5I must not define schema SQL.");
        RequireDoesNotContain(portsText, "CompleteSale", "J21-5I must not wire checkout.");
        RequireDoesNotContain(portsText, "PrintDocument", "J21-5I must not create print documents.");
        RequireDoesNotContain(portsText, "Crystal", "J21-5I must not wire reporting output.");
        RequireDoesNotContain(portsText, "receipt_snapshot", "J21-5I must not write legacy receipt snapshots.");

        RequireContains(coreSmokeText, "J21Rmo242023ElectronicJournalAppendAtomicBoundaryChecks.Run();", "CoreSmoke must still run J21-5H.");
        RequireContains(coreSmokeText, "J21Rmo242023ElectronicJournalAppendPersistencePortChecks.RunAsync().GetAwaiter().GetResult();", "CoreSmoke must run J21-5I.");

        ElectronicJournalAppendPersistenceRequest request = new(
            JournalEntryId: Guid.Parse("66666666-6666-6666-6666-666666666666"),
            Contract: CreateValidContract(),
            AppendedAt: new DateTimeOffset(2026, 6, 23, 12, 0, 2, TimeSpan.Zero));

        IElectronicJournalAppendPersistencePort persistencePort = new FakeAppendPersistencePort();
        IElectronicJournalAppendAtomicPersistencePort atomicPort = new FakeAtomicAppendPersistencePort();

        Result<ElectronicJournalAppendPersistenceResult> persistenceResult =
            await persistencePort.AppendOriginalAsync(request);

        Result<ElectronicJournalAppendPersistenceResult> atomicResult =
            await atomicPort.CommitOriginalAppendAsync(request);

        RequireSuccess(persistenceResult, "Persistence port fake must return success.");
        RequireSuccess(atomicResult, "Atomic persistence port fake must return success.");

        RequireEqual(request.JournalEntryId, persistenceResult.Value.JournalEntryId, "Persistence result must preserve journal entry id.");
        RequireEqual(request.Contract.OriginalEvent.EventId, persistenceResult.Value.OriginalEventId, "Persistence result must preserve ORIGINAL event id.");
        RequireEqual(request.Contract.OfficialDocumentNumber, persistenceResult.Value.OfficialDocumentNumber, "Persistence result must preserve official document number.");
        RequireEqual(request.AppendedAt, atomicResult.Value.AppendedAt, "Atomic result must preserve appended timestamp.");
    }

    private static ElectronicJournalAppendContract CreateValidContract()
    {
        return new ElectronicJournalAppendContract(
            BirMachineProfileId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            SaleId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            OfficialDocumentSequenceId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            OfficialDocumentNumber: "OR-2026-000001",
            DocumentKind: ElectronicJournalDocumentKind.OfficialReceipt,
            TransactionTimestamp: new DateTimeOffset(2026, 6, 23, 12, 0, 0, TimeSpan.Zero),
            CashierUserId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
            TerminalId: "TERMINAL-1",
            SnapshotEvidence: null,
            OriginalEvent: new ElectronicJournalOriginalEventContract(
                EventId: Guid.Parse("55555555-5555-5555-5555-555555555555"),
                CashierUserId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
                CreatedAt: new DateTimeOffset(2026, 6, 23, 12, 0, 1, TimeSpan.Zero)),
            AtomicBoundarySteps: new[]
            {
                ElectronicJournalAtomicBoundaryStep.OfficialSequenceAllocation,
                ElectronicJournalAtomicBoundaryStep.JournalAnchorInsert,
                ElectronicJournalAtomicBoundaryStep.OriginalEventInsert
            },
            DeferredComplianceNotes: new[] { "J21-5I persistence port contracts only." });
    }

    private static ElectronicJournalAppendPersistenceResult CreateResult(ElectronicJournalAppendPersistenceRequest request)
    {
        return new ElectronicJournalAppendPersistenceResult(
            JournalEntryId: request.JournalEntryId,
            OriginalEventId: request.Contract.OriginalEvent.EventId,
            OfficialDocumentNumber: request.Contract.OfficialDocumentNumber,
            AppendedAt: request.AppendedAt);
    }

    private sealed class FakeAppendPersistencePort : IElectronicJournalAppendPersistencePort
    {
        public Task<Result<ElectronicJournalAppendPersistenceResult>> AppendOriginalAsync(
            ElectronicJournalAppendPersistenceRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<ElectronicJournalAppendPersistenceResult>.Success(CreateResult(request)));
        }
    }

    private sealed class FakeAtomicAppendPersistencePort : IElectronicJournalAppendAtomicPersistencePort
    {
        public Task<Result<ElectronicJournalAppendPersistenceResult>> CommitOriginalAppendAsync(
            ElectronicJournalAppendPersistenceRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<ElectronicJournalAppendPersistenceResult>.Success(CreateResult(request)));
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
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(message);
        }
    }
}
