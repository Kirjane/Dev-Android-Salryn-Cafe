namespace Salryn.Tests;

public static class J21Rmo242023SqliteElectronicJournalAppendParticipantSkeletonChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string participantText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteElectronicJournalAppendParticipant.cs");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(participantText, "SqliteElectronicJournalAppendParticipant", "J21-5R participant must exist.");
        RequireContains(participantText, "ISqliteSharedTransactionParticipant<ElectronicJournalAppendPersistenceRequest, ElectronicJournalAppendPersistenceResult>", "J21-5R participant must keep shared transaction participant shape.");
        RequireContains(participantText, "ToDatabaseDocumentKind", "J21-5R participant must keep document-kind database mapping.");
        RequireContains(participantText, "OriginalEventType = \"ORIGINAL\"", "J21-5R participant must keep ORIGINAL event mapping.");
        RequireContains(coreSmokeText, "J21Rmo242023SqliteElectronicJournalAppendParticipantSkeletonChecks.Run();", "CoreSmoke must keep J21-5R checks.");

        RequireDoesNotContain(participantText, "CreateOpenConnection", "Journal append participant must not open its own connection.");
        RequireDoesNotContain(participantText, "BeginTransaction", "Journal append participant must not begin transactions.");
        RequireDoesNotContain(participantText, ".Commit(", "Journal append participant must not commit.");
        RequireDoesNotContain(participantText, ".Rollback(", "Journal append participant must not rollback.");
        RequireDoesNotContain(participantText, "OfficialDocumentSequenceAllocation", "Journal append participant must not allocate official sequence numbers.");
        RequireDoesNotContain(participantText, "CompleteSale", "Journal append participant must not wire checkout.");
        RequireDoesNotContain(participantText, "Crystal", "Journal append participant must not wire Crystal Reports.");
        RequireDoesNotContain(participantText, "PrintDocument", "Journal append participant must not wire printing.");
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
}
