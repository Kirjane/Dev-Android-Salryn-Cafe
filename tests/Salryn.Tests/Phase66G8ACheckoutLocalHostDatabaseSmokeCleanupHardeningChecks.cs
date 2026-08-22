namespace Salryn.Tests;

public static class Phase66G8ACheckoutLocalHostDatabaseSmokeCleanupHardeningChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string programText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CheckoutLocalHostSmoke", "Program.cs");
        string factoryText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "SqliteConnectionFactory.cs");
        string handoffText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_66G8A_CHECKOUT_LOCAL_HOST_DATABASE_SMOKE_CLEANUP_HARDENING.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(factoryText, "public static void ClearAllPools()", "66G8A requires Infrastructure-owned SQLite pool cleanup API.");
        RequireContains(factoryText, "SqliteConnection.ClearAllPools();", "66G8A requires SqliteConnectionFactory to clear SQLite pools inside Infrastructure.");

        RequireContains(programText, "SalrynAppServices? services = null;", "66G8A database smoke must keep AppServices visible to cleanup.");
        RequireContains(programText, "services.LocalHostSessionIdentityProvider.SetSession(null);", "66G8A database smoke must clear passive session identity before cleanup.");
        RequireContains(programText, "services.LocalHostRuntimeLifecycleHolder.TryStop();", "66G8A database smoke must stop the Local Host runtime before cleanup.");
        RequireContains(programText, "SqliteConnectionFactory.ClearAllPools();", "66G8A database smoke must clear SQLite pools through Infrastructure.");
        RequireContains(programText, "DeleteTempRootOrThrow(tempRoot);", "66G8A database smoke must use deterministic cleanup.");
        RequireContains(programText, "Temporary 66G8 smoke folder was not deleted:", "66G8A deterministic cleanup must throw when the temp root remains.");
        RequireNotContains(programText, "WARNING: Temporary", "66G8A database smoke must not pass with warning-only cleanup.");
        RequireNotContains(programText, "Cleanup skipped because Windows still holds", "66G8A database smoke must not suppress locked database cleanup failures.");
        RequireNotContains(programText, "Microsoft.Data.Sqlite", "66G8A smoke tool must not reference Microsoft.Data.Sqlite directly.");
        RequireNotContains(programText, "SqliteConnection.ClearAllPools()", "66G8A smoke tool must not call SqliteConnection.ClearAllPools directly.");

        RequireContains(handoffText, "Status: Database smoke cleanup hardening only.", "66G8A handoff must mark cleanup-only scope.");
        RequireContains(handoffText, "The smoke tool does not directly reference Microsoft.Data.Sqlite.", "66G8A handoff must preserve SQLite boundary.");
        RequireContains(handoffText, "SQLite pool cleanup remains owned by Infrastructure.", "66G8A handoff must keep pool cleanup in Infrastructure.");

        RequireContains(coreSmokeText, "Phase66G8CheckoutLocalHostDatabaseLookupSmokeVerificationChecks.Run();", "CoreSmoke must still run 66G8.");
        RequireContains(coreSmokeText, "Phase66G8ACheckoutLocalHostDatabaseSmokeCleanupHardeningChecks.Run();", "CoreSmoke must run 66G8A.");

        int index66G8 = coreSmokeText.IndexOf("Phase66G8CheckoutLocalHostDatabaseLookupSmokeVerificationChecks.Run();", StringComparison.Ordinal);
        int index66G8A = coreSmokeText.IndexOf("Phase66G8ACheckoutLocalHostDatabaseSmokeCleanupHardeningChecks.Run();", StringComparison.Ordinal);

        if (index66G8 < 0 || index66G8A < 0 || index66G8A < index66G8)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. Phase 66G8A must run after Phase 66G8.");
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

    private static void RequireNotContains(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}
