using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Repositories;
using Salryn.Core.Services;

namespace Salryn.Tests;

public static class J21Rmo242023ElectronicJournalAppendRegistrationBoundaryChecks
{
    public static async Task RunAsync()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string registrationText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "ElectronicJournalAppendServiceRegistrationBoundary.cs");
        string serviceText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "ElectronicJournalAppendService.cs");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(registrationText, "ElectronicJournalAppendServiceRegistrationRequest", "J21-5K must define registration request.");
        RequireContains(registrationText, "ElectronicJournalAppendServiceRegistrationBoundary", "J21-5K must define registration boundary.");
        RequireContains(registrationText, "Result<IElectronicJournalAppendService>", "J21-5K must expose service through interface result.");
        RequireContains(registrationText, "IElectronicJournalAppendAtomicPersistencePort", "J21-5K must require atomic persistence port.");
        RequireContains(registrationText, "new ElectronicJournalAppendService", "J21-5K must construct orchestration shell.");
        RequireContains(serviceText, "ElectronicJournalAppendService : IElectronicJournalAppendService", "J21-5J service must remain the implementation.");
        RequireContains(coreSmokeText, "J21Rmo242023ElectronicJournalAppendRegistrationBoundaryChecks.RunAsync().GetAwaiter().GetResult();", "CoreSmoke must run J21-5K.");

        RequireDoesNotContain(registrationText, "Microsoft.Data.Sqlite", "J21-5K must not depend on SQLite.");
        RequireDoesNotContain(registrationText, "SqliteConnection", "J21-5K must not open database connections.");
        RequireDoesNotContain(registrationText, "IDatabaseMigration", "J21-5K must not define migrations.");
        RequireDoesNotContain(registrationText, "CREATE TABLE", "J21-5K must not define schema SQL.");
        RequireDoesNotContain(registrationText, "CompleteSale", "J21-5K must not wire checkout.");
        RequireDoesNotContain(registrationText, "PrintDocument", "J21-5K must not create print documents.");
        RequireDoesNotContain(registrationText, "Crystal", "J21-5K must not wire reporting output.");
        RequireDoesNotContain(registrationText, "receipt_snapshot", "J21-5K must not write legacy receipt snapshots.");

        Result<IElectronicJournalAppendService> missingRequest =
            ElectronicJournalAppendServiceRegistrationBoundary.Create(null);

        RequireFailureCode(
            missingRequest,
            "ElectronicJournalAppend.RegistrationRequestRequired",
            "Missing registration request must fail.");

        Result<IElectronicJournalAppendService> missingPort =
            ElectronicJournalAppendServiceRegistrationBoundary.Create(new ElectronicJournalAppendServiceRegistrationRequest(
                AtomicPersistencePort: null!));

        RequireFailureCode(
            missingPort,
            "ElectronicJournalAppend.AtomicPersistencePortRequired",
            "Missing atomic persistence port must fail.");

        Guid journalEntryId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        DateTimeOffset appendedAt = new(2026, 6, 23, 12, 0, 2, TimeSpan.Zero);
        RecordingAtomicPersistencePort port = new();

        Result<IElectronicJournalAppendService> registration =
            ElectronicJournalAppendServiceRegistrationBoundary.Create(new ElectronicJournalAppendServiceRegistrationRequest(
                AtomicPersistencePort: port,
                Validator: new ElectronicJournalAppendContractValidator(),
                JournalEntryIdFactory: () => journalEntryId,
                Clock: () => appendedAt));

        RequireSuccess(registration, "Valid registration must produce append service.");
        RequireEqual(typeof(ElectronicJournalAppendService), registration.Value.GetType(), "Registration must create the Core orchestration service.");

        ElectronicJournalAppendContract contract = CreateValidContract();

        Result<ElectronicJournalAppendResult> appendResult =
            await registration.Value.AppendOriginalAsync(contract);

        RequireSuccess(appendResult, "Registered append service must execute against Core fake port.");
        RequireEqual(1, port.CallCount, "Registered service must call atomic persistence port once.");
        RequireEqual(journalEntryId, appendResult.Value.JournalEntryId, "Registered service must preserve generated journal id.");
        RequireEqual(contract.OriginalEvent.EventId, appendResult.Value.OriginalEventId, "Registered service must preserve ORIGINAL event id.");
        RequireEqual(contract.OfficialDocumentNumber, appendResult.Value.OfficialDocumentNumber, "Registered service must preserve official number.");
        RequireEqual(appendedAt, appendResult.Value.AppendedAt, "Registered service must preserve appended timestamp.");
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
            DeferredComplianceNotes: new[] { "J21-5K registration boundary only." });
    }

    private sealed class RecordingAtomicPersistencePort : IElectronicJournalAppendAtomicPersistencePort
    {
        public int CallCount { get; private set; }

        public Task<Result<ElectronicJournalAppendPersistenceResult>> CommitOriginalAppendAsync(
            ElectronicJournalAppendPersistenceRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;

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
