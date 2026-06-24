namespace Salryn.Tests;

public static class J21Rmo242023OfficialSequenceAllocationServiceSharedBoundaryReuseChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string serviceText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Services", "OfficialDocumentSequenceAllocationService.cs");
        string participantText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Services", "SharedSqliteOfficialDocumentSequenceAllocationParticipant.cs");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(serviceText, "J21-5Q keeps this public service while moving allocation work behind the shared transaction boundary.", "J21-5Q service must explain why reuse exists.");
        RequireContains(serviceText, "ISqliteSharedTransactionBoundary", "J21-5Q service must depend on shared transaction boundary abstraction.");
        RequireContains(serviceText, "new SqliteSharedTransactionBoundary(connectionFactory)", "J21-5Q service must create shared boundary from existing public constructor.");
        RequireContains(serviceText, "SharedSqliteOfficialDocumentSequenceAllocationParticipant", "J21-5Q service must reuse the participant.");
        RequireContains(serviceText, "_transactionBoundary.ExecuteAsync<OfficialDocumentSequenceAllocation>", "J21-5Q service must execute allocation inside shared transaction boundary.");
        RequireContains(serviceText, "_participant.ExecuteAsync(context, request, token)", "J21-5Q service must delegate allocation behavior to participant.");
        RequireContains(participantText, "ReceiptNumber.Create", "J21-5Q participant must remain canonical formatter owner.");
        RequireContains(participantText, "current_number = $currentNumber", "J21-5Q participant must remain canonical concurrency guard owner.");
        RequireContains(participantText, "official_output_enabled = 1", "J21-5Q participant must preserve official output gate.");
        RequireContains(coreSmokeText, "J21Rmo242023OfficialSequenceAllocationServiceSharedBoundaryReuseChecks.Run();", "CoreSmoke must run J21-5Q checks.");

        RequireDoesNotContain(serviceText, "CreateOpenConnection", "J21-5Q service must not open SQLite connections directly.");
        RequireDoesNotContain(serviceText, "BeginTransaction", "J21-5Q service must not begin SQLite transactions directly.");
        RequireDoesNotContain(serviceText, ".Commit(", "J21-5Q service must not commit SQLite transactions directly.");
        RequireDoesNotContain(serviceText, ".Rollback(", "J21-5Q service must not rollback SQLite transactions directly.");
        RequireDoesNotContain(serviceText, "Microsoft.Data.Sqlite", "J21-5Q service must not own SQLite command implementation.");
        RequireDoesNotContain(serviceText, "ReceiptNumber.Create", "J21-5Q service must not duplicate official number formatting.");
        RequireDoesNotContain(serviceText, "INSERT INTO bir_electronic_journal", "J21-5Q must not add journal persistence.");
        RequireDoesNotContain(serviceText, "CompleteSale", "J21-5Q must not wire checkout.");
        RequireDoesNotContain(serviceText, "Crystal", "J21-5Q must not wire Crystal Reports.");
        RequireDoesNotContain(serviceText, "PrintDocument", "J21-5Q must not wire printing.");
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
