using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Repositories;
using Salryn.Core.Services;

namespace Salryn.Tests;

public static class J21Rmo242023ElectronicJournalAppendOrchestrationShellChecks
{
    public static async Task RunAsync()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string serviceText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "ElectronicJournalAppendService.cs");
        string boundaryText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "ElectronicJournalAppendServiceBoundary.cs");
        string portsText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Repositories", "ElectronicJournalAppendPersistencePorts.cs");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(serviceText, "ElectronicJournalAppendService : IElectronicJournalAppendService", "J21-5J must implement append service boundary.");
        RequireContains(serviceText, "ElectronicJournalAppendContractValidator", "J21-5J must use append validator.");
        RequireContains(serviceText, "IElectronicJournalAppendAtomicPersistencePort", "J21-5J must depend on atomic persistence port.");
        RequireContains(serviceText, "CommitOriginalAppendAsync", "J21-5J must commit through atomic persistence port.");
        RequireContains(serviceText, "ElectronicJournalAppendPersistenceRequest", "J21-5J must create persistence request.");
        RequireContains(boundaryText, "ElectronicJournalAppendAtomicBoundaryRules.Validate(contract);", "Append validator must still enforce atomic boundary rules.");
        RequireContains(portsText, "IElectronicJournalAppendAtomicPersistencePort", "J21-5I persistence port must remain available.");
        RequireContains(coreSmokeText, "J21Rmo242023ElectronicJournalAppendOrchestrationShellChecks.RunAsync().GetAwaiter().GetResult();", "CoreSmoke must run J21-5J.");

        RequireDoesNotContain(serviceText, "Microsoft.Data.Sqlite", "J21-5J must not depend on SQLite.");
        RequireDoesNotContain(serviceText, "SqliteConnection", "J21-5J must not open database connections.");
        RequireDoesNotContain(serviceText, "IDatabaseMigration", "J21-5J must not define migrations.");
        RequireDoesNotContain(serviceText, "CREATE TABLE", "J21-5J must not define schema SQL.");
        RequireDoesNotContain(serviceText, "CompleteSale", "J21-5J must not wire checkout.");
        RequireDoesNotContain(serviceText, "PrintDocument", "J21-5J must not create print documents.");
        RequireDoesNotContain(serviceText, "Crystal", "J21-5J must not wire reporting output.");
        RequireDoesNotContain(serviceText, "receipt_snapshot", "J21-5J must not write legacy receipt snapshots.");

        Guid journalEntryId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        DateTimeOffset appendedAt = new(2026, 6, 23, 12, 0, 2, TimeSpan.Zero);
        RecordingAtomicPersistencePort port = new();

        ElectronicJournalAppendService service = new(
            persistencePort: port,
            validator: new ElectronicJournalAppendContractValidator(),
            journalEntryIdFactory: () => journalEntryId,
            clock: () => appendedAt);

        ElectronicJournalAppendContract valid = CreateValidContract();

        Result<ElectronicJournalAppendResult> result =
            await service.AppendOriginalAsync(valid);

        RequireSuccess(result, "Valid append contract must pass orchestration shell.");
        RequireEqual(1, port.CallCount, "Persistence port must be called once.");
        RequireEqual(journalEntryId, port.LastRequest?.JournalEntryId, "Service must pass generated journal entry id.");
        RequireEqual(valid, port.LastRequest?.Contract, "Service must pass original contract.");
        RequireEqual(appendedAt, result.Value.AppendedAt, "Service must preserve appended timestamp.");
        RequireEqual(valid.OfficialDocumentNumber, result.Value.OfficialDocumentNumber, "Service must preserve official number.");
        RequireEqual(valid.OriginalEvent.EventId, result.Value.OriginalEventId, "Service must preserve ORIGINAL event id.");

        RecordingAtomicPersistencePort invalidPort = new();
        ElectronicJournalAppendService invalidService = new(
            persistencePort: invalidPort,
            validator: new ElectronicJournalAppendContractValidator(),
            journalEntryIdFactory: () => journalEntryId,
            clock: () => appendedAt);

        Result<ElectronicJournalAppendResult> invalidResult =
            await invalidService.AppendOriginalAsync(valid with { BirMachineProfileId = Guid.Empty });

        RequireFailureCode(
            invalidResult,
            "ElectronicJournalAppend.MachineProfileRequired",
            "Invalid contract must fail before persistence.");

        RequireEqual(0, invalidPort.CallCount, "Persistence port must not be called after validation failure.");

        ElectronicJournalAppendService emptyIdService = new(
            persistencePort: new RecordingAtomicPersistencePort(),
            validator: new ElectronicJournalAppendContractValidator(),
            journalEntryIdFactory: () => Guid.Empty,
            clock: () => appendedAt);

        RequireFailureCode(
            await emptyIdService.AppendOriginalAsync(valid),
            "ElectronicJournalAppend.JournalEntryIdRequired",
            "Empty generated journal entry id must fail.");

        RecordingAtomicPersistencePort failingPort = new()
        {
            Failure = Error.Failure(
                "ElectronicJournalAppend.PersistenceFailure",
                "Persistence failure from fake port.")
        };

        ElectronicJournalAppendService failingService = new(
            persistencePort: failingPort,
            validator: new ElectronicJournalAppendContractValidator(),
            journalEntryIdFactory: () => journalEntryId,
            clock: () => appendedAt);

        RequireFailureCode(
            await failingService.AppendOriginalAsync(valid),
            "ElectronicJournalAppend.PersistenceFailure",
            "Persistence failure must be forwarded.");
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
            DeferredComplianceNotes: new[] { "J21-5J orchestration shell only." });
    }

    private sealed class RecordingAtomicPersistencePort : IElectronicJournalAppendAtomicPersistencePort
    {
        public int CallCount { get; private set; }

        public ElectronicJournalAppendPersistenceRequest? LastRequest { get; private set; }

        public Error? Failure { get; init; }

        public Task<Result<ElectronicJournalAppendPersistenceResult>> CommitOriginalAppendAsync(
            ElectronicJournalAppendPersistenceRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;

            if (Failure is Error failure)
            {
                return Task.FromResult(Result<ElectronicJournalAppendPersistenceResult>.Fail(failure));
            }

            ElectronicJournalAppendPersistenceResult result = new(
                JournalEntryId: request.JournalEntryId,
                OriginalEventId: request.Contract.OriginalEvent.EventId,
                OfficialDocumentNumber: request.Contract.OfficialDocumentNumber,
                AppendedAt: request.AppendedAt);

            return Task.FromResult(Result<ElectronicJournalAppendPersistenceResult>.Success(result));
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

    private static void RequireFailureCode<T>(Result<T> result, string expectedCode, string message)
    {
        if (result.IsSuccess || result.ErrorDetail.Code != expectedCode)
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
