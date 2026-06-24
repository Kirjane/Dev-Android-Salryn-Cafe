namespace Salryn.Tests;

public static class J21Rmo242023ElectronicJournalPersistenceRuntimeDocumentationSealChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string sealText = ReadRequiredFile(repoRoot, "00-COMPLIANCE", "RMO_24_2023_ELECTRONIC_JOURNAL_PERSISTENCE_RUNTIME_SEAL.md");
        string handoffText = ReadRequiredFile(repoRoot, "00-HANDOFF", "J21_5X_ELECTRONIC_JOURNAL_PERSISTENCE_RUNTIME_DOCUMENTATION_SEAL.md");
        string matrixText = ReadRequiredFile(repoRoot, "00-COMPLIANCE", "RMO_24_2023_COMPLIANCE_MATRIX.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        string transactionBoundaryText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Transactions", "SqliteSharedTransactionBoundary.cs");
        string sequenceParticipantText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Services", "SharedSqliteOfficialDocumentSequenceAllocationParticipant.cs");
        string sequenceServiceText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Services", "OfficialDocumentSequenceAllocationService.cs");
        string journalParticipantText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteElectronicJournalAppendParticipant.cs");
        string atomicAdapterText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteElectronicJournalAppendAtomicPersistenceAdapter.cs");
        string compositionText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteElectronicJournalAppendServiceComposition.cs");
        string runtimeSmokeText = ReadRequiredFile(repoRoot, "tests", "Salryn.Tests", "J21Rmo242023SharedTransactionSequenceAndJournalAppendRuntimeSmokeChecks.cs");

        RequireContains(sealText, "Status: documentation seal only.", "J21-5X seal must remain documentation-only.");
        RequireContains(sealText, "J21-5P", "J21-5X must seal J21-5P.");
        RequireContains(sealText, "J21-5W", "J21-5X must seal J21-5W.");
        RequireContains(sealText, "official number drift", "J21-5X must document the drift risk reduced.");
        RequireContains(sealText, "No checkout wiring.", "J21-5X must block checkout wiring.");
        RequireContains(sealText, "No Crystal Reports wiring.", "J21-5X must block Crystal wiring.");
        RequireContains(sealText, "No official receipt or invoice output claim.", "J21-5X must block official output claims.");
        RequireContains(sealText, "Salryn still is not RMO 24-2023 complete", "J21-5X must not overclaim compliance.");

        RequireContains(handoffText, "Status: documentation seal only.", "J21-5X handoff must remain documentation-only.");
        RequireContains(handoffText, "No behavior is added in this phase.", "J21-5X handoff must block behavior changes.");
        RequireContains(handoffText, "No accreditation-ready claim.", "J21-5X handoff must block accreditation claims.");
        RequireContains(handoffText, "The next implementation phase must be explicitly confirmed.", "J21-5X handoff must require confirmation before the next phase.");

        RequireContains(matrixText, "## J21-5X Electronic Journal Persistence Runtime Documentation Seal", "Compliance matrix must include J21-5X.");
        RequireContains(matrixText, "sequence allocation and journal append can commit or roll back together", "Compliance matrix must record the J21-5W atomicity proof.");
        RequireContains(matrixText, "No checkout wiring.", "Compliance matrix must keep checkout blocked.");
        RequireContains(matrixText, "No official output claim.", "Compliance matrix must keep official output blocked.");

        RequireContains(coreSmokeText, "J21Rmo242023ElectronicJournalPersistenceRuntimeDocumentationSealChecks.Run();", "CoreSmoke must run J21-5X checks.");

        RequireContains(transactionBoundaryText, "SqliteSharedTransactionBoundary", "J21-5X must seal the shared transaction boundary.");
        RequireContains(sequenceParticipantText, "SharedSqliteOfficialDocumentSequenceAllocationParticipant", "J21-5X must seal the shared sequence participant.");
        RequireContains(sequenceServiceText, "SharedSqliteOfficialDocumentSequenceAllocationParticipant", "J21-5X must prove sequence service reuse remains in place.");
        RequireContains(journalParticipantText, "SqliteElectronicJournalAppendParticipant", "J21-5X must seal the SQLite journal participant.");
        RequireContains(atomicAdapterText, "SqliteElectronicJournalAppendAtomicPersistenceAdapter", "J21-5X must seal the atomic persistence adapter.");
        RequireContains(compositionText, "SqliteElectronicJournalAppendServiceComposition", "J21-5X must seal SQLite append service composition.");
        RequireContains(runtimeSmokeText, "SharedSqliteOfficialDocumentSequenceAllocationParticipant", "J21-5X must seal the shared sequence and journal smoke.");
        RequireContains(runtimeSmokeText, "J21-5W.IntentionalRollback", "J21-5X must seal rollback coverage.");
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
}
