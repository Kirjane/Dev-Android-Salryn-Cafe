using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Repositories;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Data.Transactions;

namespace Salryn.Tests;

public static class J21Rmo242023SqliteElectronicJournalAppendAtomicPersistenceAdapterChecks
{
    public static async Task RunAsync()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string adapterText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteElectronicJournalAppendAtomicPersistenceAdapter.cs");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(adapterText, "IElectronicJournalAppendAtomicPersistencePort", "J21-5T adapter must implement the Core atomic persistence port.");
        RequireContains(adapterText, "ISqliteSharedTransactionBoundary", "J21-5T adapter must use the shared transaction boundary.");
        RequireContains(adapterText, "ISqliteSharedTransactionParticipant<ElectronicJournalAppendPersistenceRequest, ElectronicJournalAppendPersistenceResult>", "J21-5T adapter must delegate to the journal append participant.");
        RequireContains(adapterText, "CommitOriginalAppendAsync", "J21-5T adapter must expose the Core port method.");
        RequireContains(adapterText, "ExecuteAsync<ElectronicJournalAppendPersistenceResult>", "J21-5T adapter must execute through the shared boundary.");
        RequireContains(coreSmokeText, "J21Rmo242023SqliteElectronicJournalAppendAtomicPersistenceAdapterChecks.RunAsync().GetAwaiter().GetResult();", "CoreSmoke must run J21-5T checks.");

        RequireDoesNotContain(adapterText, "CreateOpenConnection", "J21-5T adapter must not open its own connection.");
        RequireDoesNotContain(adapterText, "BeginTransaction", "J21-5T adapter must not begin transactions.");
        RequireDoesNotContain(adapterText, ".Commit(", "J21-5T adapter must not commit.");
        RequireDoesNotContain(adapterText, ".Rollback(", "J21-5T adapter must not rollback.");
        RequireDoesNotContain(adapterText, "INSERT INTO", "J21-5T adapter must not own SQL inserts.");
        RequireDoesNotContain(adapterText, "OfficialDocumentSequenceAllocation", "J21-5T adapter must not allocate official sequence numbers.");
        RequireDoesNotContain(adapterText, "CompleteSale", "J21-5T adapter must not wire checkout.");
        RequireDoesNotContain(adapterText, "Crystal", "J21-5T adapter must not wire Crystal Reports.");
        RequireDoesNotContain(adapterText, "PrintDocument", "J21-5T adapter must not wire printing.");

        await RunAdapterDelegationChecks();
        await RunFailurePropagationChecks();
    }

    private static async Task RunAdapterDelegationChecks()
    {
        SpyTransactionBoundary boundary = new();
        SpyAppendParticipant participant = new();
        SqliteElectronicJournalAppendAtomicPersistenceAdapter adapter = new(boundary, participant);
        ElectronicJournalAppendPersistenceRequest request = CreateRequest();

        using CancellationTokenSource cancellationTokenSource = new();

        Result<ElectronicJournalAppendPersistenceResult> result =
            await adapter.CommitOriginalAppendAsync(request, cancellationTokenSource.Token);

        RequireSuccess(result, "Adapter should return participant success.");
        RequireEqual(1, boundary.ExecuteCallCount, "Adapter must execute exactly one shared transaction boundary call.");
        RequireEqual(1, participant.ExecuteCallCount, "Adapter must execute exactly one append participant call.");
        RequireEqual(true, participant.ContextWasProvided, "Adapter must provide the shared transaction context to the participant.");
        RequireEqual(request.JournalEntryId, participant.CapturedRequest!.JournalEntryId, "Adapter must pass the original persistence request.");
        RequireEqual(request.Contract.OriginalEvent.EventId, result.Value.OriginalEventId, "Adapter result must preserve ORIGINAL event id.");
        RequireEqual(request.Contract.OfficialDocumentNumber, result.Value.OfficialDocumentNumber, "Adapter result must preserve official document number.");
    }

    private static async Task RunFailurePropagationChecks()
    {
        SpyTransactionBoundary boundary = new();
        SpyAppendParticipant participant = new("J21-5T.ParticipantFailure");
        SqliteElectronicJournalAppendAtomicPersistenceAdapter adapter = new(boundary, participant);

        Result<ElectronicJournalAppendPersistenceResult> result =
            await adapter.CommitOriginalAppendAsync(CreateRequest());

        RequireFailureCode(result, "J21-5T.ParticipantFailure", "Adapter must propagate participant failure.");
        RequireEqual(1, boundary.ExecuteCallCount, "Failure path must still run through shared boundary.");
        RequireEqual(1, participant.ExecuteCallCount, "Failure path must still call participant once.");
    }

    private static ElectronicJournalAppendPersistenceRequest CreateRequest()
    {
        ElectronicJournalAppendContract contract = new(
            BirMachineProfileId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            SaleId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            OfficialDocumentSequenceId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            OfficialDocumentNumber: "OR-2026-000004",
            DocumentKind: ElectronicJournalDocumentKind.OfficialReceipt,
            TransactionTimestamp: new DateTimeOffset(2026, 6, 24, 11, 0, 0, TimeSpan.Zero),
            CashierUserId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
            TerminalId: "TERMINAL-1",
            SnapshotEvidence: null,
            OriginalEvent: new ElectronicJournalOriginalEventContract(
                EventId: Guid.Parse("55555555-5555-5555-5555-555555555555"),
                CashierUserId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
                CreatedAt: new DateTimeOffset(2026, 6, 24, 11, 0, 1, TimeSpan.Zero)),
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
            AppendedAt: new DateTimeOffset(2026, 6, 24, 11, 0, 2, TimeSpan.Zero));
    }

    private sealed class SpyTransactionBoundary : ISqliteSharedTransactionBoundary
    {
        public int ExecuteCallCount { get; private set; }

        public async Task<Result<T>> ExecuteAsync<T>(
            SqliteSharedTransactionWork<T> work,
            CancellationToken cancellationToken = default)
        {
            ExecuteCallCount++;

            using SqliteConnection connection = new("Data Source=:memory:");
            connection.Open();

            using SqliteTransaction transaction = connection.BeginTransaction();
            SqliteSharedTransactionContext context = new(connection, transaction);

            Result<T> result = await work(context, cancellationToken);

            if (result.IsSuccess)
            {
                transaction.Commit();
            }
            else
            {
                transaction.Rollback();
            }

            return result;
        }
    }

    private sealed class SpyAppendParticipant :
        ISqliteSharedTransactionParticipant<ElectronicJournalAppendPersistenceRequest, ElectronicJournalAppendPersistenceResult>
    {
        private readonly string? _failureCode;

        public SpyAppendParticipant(string? failureCode = null)
        {
            _failureCode = failureCode;
        }

        public int ExecuteCallCount { get; private set; }

        public bool ContextWasProvided { get; private set; }

        public ElectronicJournalAppendPersistenceRequest? CapturedRequest { get; private set; }

        public Task<Result<ElectronicJournalAppendPersistenceResult>> ExecuteAsync(
            SqliteSharedTransactionContext context,
            ElectronicJournalAppendPersistenceRequest request,
            CancellationToken cancellationToken = default)
        {
            ExecuteCallCount++;
            ContextWasProvided = context is not null;
            CapturedRequest = request;

            if (_failureCode is not null)
            {
                return Task.FromResult(Result<ElectronicJournalAppendPersistenceResult>.Fail(Error.Validation(
                    _failureCode,
                    "Intentional participant failure.")));
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
