using Salryn.Core.Models;
using Salryn.Core.Services;

namespace Salryn.Tests;

public static class J21Rmo242023ElectronicJournalAppendAtomicBoundaryChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string rulesText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "ElectronicJournalAppendAtomicBoundaryRules.cs");
        string serviceText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "ElectronicJournalAppendServiceBoundary.cs");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(rulesText, "ElectronicJournalAppendAtomicBoundaryRules", "J21-5H must define atomic boundary rules.");
        RequireContains(serviceText, "ElectronicJournalAppendAtomicBoundaryRules.Validate(contract);", "Append validator must use atomic boundary rules.");
        RequireContains(coreSmokeText, "J21Rmo242023ElectronicJournalAppendAtomicBoundaryChecks.Run();", "CoreSmoke must run J21-5H checks.");

        RequireDoesNotContain(rulesText, "Microsoft.Data.Sqlite", "J21-5H must not depend on SQLite.");
        RequireDoesNotContain(rulesText, "SqliteConnection", "J21-5H must not open database connections.");
        RequireDoesNotContain(rulesText, "IDatabaseMigration", "J21-5H must not define migrations.");
        RequireDoesNotContain(rulesText, "CREATE TABLE", "J21-5H must not define schema SQL.");
        RequireDoesNotContain(rulesText, "CompleteSale", "J21-5H must not wire checkout.");
        RequireDoesNotContain(rulesText, "PrintDocument", "J21-5H must not create print documents.");
        RequireDoesNotContain(rulesText, "Crystal", "J21-5H must not wire reporting output.");

        ElectronicJournalAppendContractValidator validator = new();
        ElectronicJournalAppendContract valid = CreateValidContract();

        RequireSuccess(validator.Validate(valid), "Ordered atomic boundary must pass.");

        RequireFailureCode(
            validator.Validate(valid with
            {
                AtomicBoundarySteps = new[]
                {
                    ElectronicJournalAtomicBoundaryStep.OfficialSequenceAllocation,
                    ElectronicJournalAtomicBoundaryStep.JournalAnchorInsert,
                    ElectronicJournalAtomicBoundaryStep.JournalAnchorInsert,
                    ElectronicJournalAtomicBoundaryStep.OriginalEventInsert
                }
            }),
            "ElectronicJournalAppend.DuplicateBoundaryStep",
            "Duplicate atomic steps must fail.");

        RequireFailureCode(
            validator.Validate(valid with
            {
                AtomicBoundarySteps = new[]
                {
                    ElectronicJournalAtomicBoundaryStep.JournalAnchorInsert,
                    ElectronicJournalAtomicBoundaryStep.OfficialSequenceAllocation,
                    ElectronicJournalAtomicBoundaryStep.OriginalEventInsert
                }
            }),
            "ElectronicJournalAppend.SequenceBoundaryMustStart",
            "Official sequence allocation must start the boundary.");

        RequireFailureCode(
            validator.Validate(valid with
            {
                AtomicBoundarySteps = new[]
                {
                    ElectronicJournalAtomicBoundaryStep.OfficialSequenceAllocation,
                    ElectronicJournalAtomicBoundaryStep.OriginalEventInsert,
                    ElectronicJournalAtomicBoundaryStep.JournalAnchorInsert
                }
            }),
            "ElectronicJournalAppend.OriginalEventBoundaryOrderInvalid",
            "ORIGINAL event must follow journal anchor.");

        RequireFailureCode(
            validator.Validate(valid with
            {
                SnapshotEvidence = new ElectronicJournalSnapshotEvidence(
                    PayloadJson: "{}",
                    PayloadHash: "abc123",
                    HashAlgorithm: "SHA-256")
            }),
            "ElectronicJournalAppend.SnapshotBoundaryRequired",
            "Snapshot evidence must require snapshot persistence boundary.");

        RequireFailureCode(
            validator.Validate(valid with
            {
                SnapshotEvidence = new ElectronicJournalSnapshotEvidence(
                    PayloadJson: "{}",
                    PayloadHash: "abc123",
                    HashAlgorithm: "SHA-256"),
                AtomicBoundarySteps = new[]
                {
                    ElectronicJournalAtomicBoundaryStep.OfficialSequenceAllocation,
                    ElectronicJournalAtomicBoundaryStep.JournalAnchorInsert,
                    ElectronicJournalAtomicBoundaryStep.SnapshotPayloadHashPersistence,
                    ElectronicJournalAtomicBoundaryStep.OriginalEventInsert
                }
            }),
            "ElectronicJournalAppend.SnapshotBoundaryOrderInvalid",
            "Snapshot persistence must happen before journal anchor.");

        RequireFailureCode(
            validator.Validate(valid with
            {
                AtomicBoundarySteps = new[]
                {
                    ElectronicJournalAtomicBoundaryStep.OfficialSequenceAllocation,
                    ElectronicJournalAtomicBoundaryStep.JournalAnchorInsert,
                    ElectronicJournalAtomicBoundaryStep.SaleOfficialSnapshotLink,
                    ElectronicJournalAtomicBoundaryStep.OriginalEventInsert
                }
            }),
            "ElectronicJournalAppend.SaleLinkBoundaryOrderInvalid",
            "Sale link must happen after ORIGINAL event.");
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
            DeferredComplianceNotes: new[] { "J21-5H atomic boundary validation only." });
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

    private static void RequireSuccess(Salryn.Core.Common.Result result, string message)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireFailureCode(Salryn.Core.Common.Result result, string expectedCode, string message)
    {
        if (result.IsSuccess || result.ErrorDetail.Code != expectedCode)
        {
            throw new InvalidOperationException(message);
        }
    }
}
