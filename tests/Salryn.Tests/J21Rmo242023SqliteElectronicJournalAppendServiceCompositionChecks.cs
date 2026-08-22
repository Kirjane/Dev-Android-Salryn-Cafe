using Salryn.Core.Common;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class J21Rmo242023SqliteElectronicJournalAppendServiceCompositionChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string compositionText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteElectronicJournalAppendServiceComposition.cs");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(compositionText, "SqliteElectronicJournalAppendServiceComposition", "J21-5U composition factory must exist.");
        RequireContains(compositionText, "SqliteSharedTransactionBoundary", "J21-5U must compose the shared SQLite transaction boundary.");
        RequireContains(compositionText, "SqliteElectronicJournalAppendParticipant", "J21-5U must compose the SQLite journal append participant.");
        RequireContains(compositionText, "SqliteElectronicJournalAppendAtomicPersistenceAdapter", "J21-5U must compose the atomic persistence adapter.");
        RequireContains(compositionText, "ElectronicJournalAppendServiceRegistrationBoundary.Create", "J21-5U must finish through the Core registration boundary.");
        RequireContains(coreSmokeText, "J21Rmo242023SqliteElectronicJournalAppendServiceCompositionChecks.Run();", "CoreSmoke must run J21-5U checks.");

        RequireDoesNotContain(compositionText, "CompleteSale", "J21-5U must not wire checkout.");
        RequireDoesNotContain(compositionText, "Crystal", "J21-5U must not wire Crystal Reports.");
        RequireDoesNotContain(compositionText, "PrintDocument", "J21-5U must not wire printing.");
        RequireDoesNotContain(compositionText, "INSERT INTO", "J21-5U composition must not own SQL.");
        RequireDoesNotContain(compositionText, "RunPendingMigrations", "J21-5U composition must not run migrations.");
        RequireDoesNotContain(compositionText, "OfficialDocumentSequenceAllocation", "J21-5U composition must not allocate official sequence numbers.");

        RunCompositionRuntimeCheck();
    }

    private static void RunCompositionRuntimeCheck()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "Salryn-J21-5U-" + Guid.NewGuid().ToString("N"));

        try
        {
            DbPaths paths = DbPaths.CreateDevelopment(tempRoot);
            SqliteConnectionFactory connectionFactory = new(paths);

            Result<IElectronicJournalAppendService> result =
                SqliteElectronicJournalAppendServiceComposition.Create(
                    new SqliteElectronicJournalAppendServiceCompositionRequest(
                        ConnectionFactory: connectionFactory,
                        Validator: new ElectronicJournalAppendContractValidator(),
                        JournalEntryIdFactory: () => Guid.Parse("11111111-1111-1111-1111-111111111111"),
                        Clock: () => new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero)));

            RequireSuccess(result, "SQLite append service composition should succeed.");
            RequireEqual(true, result.Value is ElectronicJournalAppendService, "Composition must return the Core append service implementation behind its interface.");

            Result<IElectronicJournalAppendService> nullRequestResult =
                SqliteElectronicJournalAppendServiceComposition.Create(null);

            RequireFailureCode(
                nullRequestResult,
                "ElectronicJournalAppend.SqliteCompositionRequestRequired",
                "Null composition request must fail.");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
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
