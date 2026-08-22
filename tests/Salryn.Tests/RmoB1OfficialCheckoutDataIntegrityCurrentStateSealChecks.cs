namespace Salryn.Tests;

public static class RmoB1OfficialCheckoutDataIntegrityCurrentStateSealChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string sealText = Read(repoRoot, "00-COMPLIANCE", "RMO_B1_OFFICIAL_CHECKOUT_DATA_INTEGRITY_CURRENT_STATE_SEAL.md");
        string matrixText = Read(repoRoot, "00-COMPLIANCE", "RMO_24_2023_COMPLIANCE_MATRIX.md");
        string coreSmoke = Read(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string persistenceService = Read(repoRoot, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteOfficialCheckoutPersistenceService.cs");
        string transactionBoundary = Read(repoRoot, "src", "Salryn.Infrastructure", "Data", "Transactions", "SqliteSharedTransactionBoundary.cs");
        string sequenceParticipant = Read(repoRoot, "src", "Salryn.Infrastructure", "Data", "Services", "SharedSqliteOfficialDocumentSequenceAllocationParticipant.cs");
        string saleParticipant = Read(repoRoot, "src", "Salryn.Infrastructure", "Data", "Services", "SharedSqliteCompletedSaleWriteParticipant.cs");
        string journalParticipant = Read(repoRoot, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteElectronicJournalAppendParticipant.cs");
        string snapshotBuilder = Read(repoRoot, "src", "Salryn.Core", "Services", "OfficialDocumentSnapshotDraftBuilder.cs");
        string journalContractBuilder = Read(repoRoot, "src", "Salryn.Core", "Services", "OfficialDocumentJournalAppendContractBuilder.cs");
        string runtimeSmoke = Read(repoRoot, "tests", "Salryn.Tests", "J21Rmo242023OfficialCheckoutSharedTransactionRuntimeSmokeChecks.cs");

        RequireContains(coreSmoke, "RmoB1OfficialCheckoutDataIntegrityCurrentStateSealChecks.Run();", "CoreSmoke must run RMO-B1 current-state seal checks.");

        RequireContains(sealText, "Status: documentation and guard seal only.", "B1 seal must remain documentation and guard only.");
        RequireContains(sealText, "Single-machine official checkout data-integrity spine only.", "B1 seal must state single-machine official checkout data-integrity scope.");
        RequireContains(sealText, "SqliteOfficialCheckoutPersistenceService", "B1 seal must reference official checkout persistence service.");
        RequireContains(sealText, "SqliteSharedTransactionBoundary", "B1 seal must reference shared transaction boundary.");
        RequireContains(sealText, "SharedSqliteOfficialDocumentSequenceAllocationParticipant", "B1 seal must reference sequence allocation participant.");
        RequireContains(sealText, "SharedSqliteCompletedSaleWriteParticipant", "B1 seal must reference completed sale participant.");
        RequireContains(sealText, "SqliteElectronicJournalAppendParticipant", "B1 seal must reference electronic journal append participant.");
        RequireContains(sealText, "OfficialDocumentSnapshotDraftBuilder", "B1 seal must reference snapshot draft builder.");
        RequireContains(sealText, "OfficialDocumentJournalAppendContractBuilder", "B1 seal must reference journal append contract builder.");
        RequireContains(sealText, "rollback", "B1 seal must reference rollback behavior.");
        RequireContains(sealText, "sale_items", "B1 seal must reference sale item alignment.");
        RequireContains(sealText, "payments", "B1 seal must reference payment alignment.");
        RequireContains(sealText, "stock_movements", "B1 seal must reference stock movement alignment.");
        RequireContains(sealText, "fail-closed", "B1 seal must reference fail-closed readiness.");
        RequireContains(sealText, "OfficialCheckout.ReadinessBlocked", "B1 seal must reference readiness blocked code.");
        RequireContains(sealText, "no LAN claim", "B1 seal must deny LAN claim.");
        RequireContains(sealText, "no printer", "B1 seal must deny printer claim.");
        RequireContains(sealText, "no Web/API official-output claim", "B1 seal must deny Web/API official-output claim.");
        RequireContains(sealText, "not BIR certification", "B1 seal must deny BIR certification.");
        RequireContains(sealText, "not BIR accreditation", "B1 seal must deny BIR accreditation.");
        RequireContains(sealText, "not full RMO 24-2023 compliance", "B1 seal must deny full RMO compliance claim.");

        RequireContains(matrixText, "## RMO-B1 Official Checkout Data-Integrity Current-State Seal", "Compliance matrix must contain RMO-B1 current-state addendum.");
        RequireContains(matrixText, "historical phase sections remain phase-local", "Compliance matrix addendum must preserve phase history.");
        RequireContains(matrixText, "documentation/guard only", "Compliance matrix addendum must state documentation/guard only.");
        RequireContains(matrixText, "single-machine only", "Compliance matrix addendum must state single-machine only.");

        RequireContains(persistenceService, "class SqliteOfficialCheckoutPersistenceService", "Source must still contain official checkout persistence service.");
        RequireContains(persistenceService, "CompleteOfficialSaleAsync", "Source must still expose official checkout completion.");
        RequireContains(transactionBoundary, "class SqliteSharedTransactionBoundary", "Source must still contain shared transaction boundary.");
        RequireContains(sequenceParticipant, "class SharedSqliteOfficialDocumentSequenceAllocationParticipant", "Source must still contain sequence allocation participant.");
        RequireContains(saleParticipant, "class SharedSqliteCompletedSaleWriteParticipant", "Source must still contain completed sale participant.");
        RequireContains(journalParticipant, "class SqliteElectronicJournalAppendParticipant", "Source must still contain electronic journal append participant.");
        RequireContains(snapshotBuilder, "class OfficialDocumentSnapshotDraftBuilder", "Source must still contain snapshot draft builder.");
        RequireContains(journalContractBuilder, "class OfficialDocumentJournalAppendContractBuilder", "Source must still contain journal append contract builder.");

        RequireContains(runtimeSmoke, "Official checkout shared transaction should commit.", "Runtime smoke must assert commit path.");
        RequireContains(runtimeSmoke, "Rollback must not advance sequence.", "Runtime smoke must assert rollback path.");
        RequireContains(runtimeSmoke, "Sale receipt number must match official document number.", "Runtime smoke must assert sale receipt alignment.");
        RequireContains(runtimeSmoke, "Journal document number must match official sequence.", "Runtime smoke must assert journal document alignment.");
        RequireContains(runtimeSmoke, "Commit must insert sale item.", "Runtime smoke must assert sale item insert.");
        RequireContains(runtimeSmoke, "Commit must insert payment.", "Runtime smoke must assert payment insert.");
        RequireContains(runtimeSmoke, "Commit must insert stock movement.", "Runtime smoke must assert stock movement insert.");
        RequireContains(runtimeSmoke, "Commit must insert journal entry.", "Runtime smoke must assert journal entry insert.");
        RequireContains(runtimeSmoke, "Commit must insert journal event.", "Runtime smoke must assert journal event insert.");

        RequireNotContains(coreSmoke, "Salryn.App.WinForms", "CoreSmoke B1 guard must not require launching WinForms.");
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

    private static string Read(DirectoryInfo root, params string[] parts)
    {
        string path = root.FullName;

        foreach (string part in parts)
        {
            path = Path.Combine(path, part);
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException("Required file was not found: " + path);
        }

        return File.ReadAllText(path);
    }

    private static void RequireContains(string haystack, string needle, string message)
    {
        if (!haystack.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message + " Missing marker: " + needle);
        }
    }

    private static void RequireNotContains(string haystack, string needle, string message)
    {
        if (haystack.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message + " Unexpected marker: " + needle);
        }
    }
}
