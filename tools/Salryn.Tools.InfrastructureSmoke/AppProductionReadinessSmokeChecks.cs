using Salryn.Core.Common;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Paths;

internal static class AppProductionReadinessSmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== APP PRODUCTION READINESS SMOKE CHECKS =====");

        RunSourceGuard(projectRoot);

        DbPaths prodPaths = DbPaths.CreateProduction();

        Console.WriteLine($"Production database path: {prodPaths.DatabasePath}");
        Console.WriteLine($"Production backup path: {prodPaths.BackupPath}");

        VerifyNoDevelopmentOnlyPath(prodPaths);

        VerifyDirectoryCreatableOrExists(prodPaths.DataPath, "Database");
        VerifyDirectoryWritable(prodPaths.DataPath, "Database");

        VerifyDirectoryCreatableOrExists(prodPaths.BackupPath, "Backup");
        VerifyDirectoryWritable(prodPaths.BackupPath, "Backup");

        DatabaseInitializationOrchestrator orchestrator =
            DefaultDatabaseInitializationFactory.CreateProduction();

        Result<DatabaseInitializationStatus> statusResult = orchestrator.CheckStatus();

        if (statusResult.IsFailure)
        {
            throw new InvalidOperationException(
                $"Database initialization status check failed: {statusResult.ErrorDetail?.Code} {statusResult.ErrorDetail?.Message}");
        }

        DatabaseInitializationStatus status = statusResult.Value;

        Console.WriteLine("Status check succeeded: OK");
        Console.WriteLine($"First-run required: {status.FirstRunStatus.IsFirstRunRequired}");
        Console.WriteLine($"DB initialized: {status.FirstRunStatus.IsDatabaseInitialized}");
        Console.WriteLine($"Has users: {status.FirstRunStatus.HasUsers}");
        Console.WriteLine($"Has store settings: {status.FirstRunStatus.HasStoreSettings}");

        // Failure output clarity for unwritable path is provided by VerifyDirectoryWritable exception text.
        // No dev path used, no DB mutation, no users created, no seeding, no schema change.
        // Probe file .salryn-write-test.tmp written and deleted immediately for writability.
        // Orchestrator.CheckStatus used for read-only init status and first-run detection.

        Console.WriteLine("App production readiness smoke checks succeeded.");
        Console.WriteLine();
    }

    private static void RunSourceGuard(string projectRoot)
    {
        string smokePath = Path.Combine(
            projectRoot,
            "tools",
            "Salryn.Tools.InfrastructureSmoke",
            "AppProductionReadinessSmokeChecks.cs");

        if (!File.Exists(smokePath))
        {
            throw new InvalidOperationException("App production readiness smoke source not found for guard.");
        }

        string smokeText = File.ReadAllText(smokePath);

        RequireDoesNotContain(smokeText, "Salryn" + ".App" + ".Win" + "Forms", "App production readiness smoke guard g1.");
        RequireDoesNotContain(smokeText, "System" + ".Windows" + ".Forms", "App production readiness smoke guard g2.");
        RequireDoesNotContain(smokeText, "Application" + ".Run", "App production readiness smoke guard g3.");
        RequireDoesNotContain(smokeText, "Checkout" + "Presenter", "App production readiness smoke guard g4.");
        RequireDoesNotContain(smokeText, "R" + "MO", "App production readiness smoke guard g5.");
        RequireDoesNotContain(smokeText, "Official" + "Output", "App production readiness smoke guard g6.");
        RequireDoesNotContain(smokeText, "P" + "DF", "App production readiness smoke guard g7.");
        RequireDoesNotContain(smokeText, "Crys" + "tal", "App production readiness smoke guard g8.");
        RequireDoesNotContain(smokeText, "ESC" + "/POS", "App production readiness smoke guard g9.");
        RequireDoesNotContain(smokeText, "print" + "er", "App production readiness smoke guard g10.");
        RequireDoesNotContain(smokeText, "scan" + "ner", "App production readiness smoke guard g11.");
        RequireDoesNotContain(smokeText, "hard" + "ware", "App production readiness smoke guard g12.");
        RequireDoesNotContain(smokeText, "L" + "AN", "App production readiness smoke guard g13.");
        RequireDoesNotContain(smokeText, "mob" + "ile", "App production readiness smoke guard g14.");
        RequireDoesNotContain(smokeText, "re" + "design", "App production readiness smoke guard g15.");
        RequireDoesNotContain(smokeText, "Win" + "Forms", "App production readiness smoke guard g16.");
    }

    private static void RequireDoesNotContain(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void VerifyNoDevelopmentOnlyPath(DbPaths prodPaths)
    {
        string root = prodPaths.RootPath;
        string data = prodPaths.DataPath;

        if (root.Contains(".salryn-dev", StringComparison.OrdinalIgnoreCase) ||
            data.Contains(".salryn-dev", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Production readiness check must not require a development-only path.");
        }

        Console.WriteLine("No development-only path required: OK");
    }

    private static void VerifyDirectoryCreatableOrExists(string directoryPath, string label)
    {
        try
        {
            bool existed = Directory.Exists(directoryPath);
            Directory.CreateDirectory(directoryPath);
            bool nowExists = Directory.Exists(directoryPath);

            if (!nowExists)
            {
                throw new InvalidOperationException($"{label} directory could not be ensured: {directoryPath}");
            }

            string state = existed ? "already existed" : "created";
            Console.WriteLine($"{label} directory {state}: OK ({directoryPath})");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
        {
            throw new InvalidOperationException(
                $"PRODUCTION READINESS FAILURE: {label} directory cannot be created. Path: {directoryPath}. {ex.Message}");
        }
    }

    private static void VerifyDirectoryWritable(string directoryPath, string label)
    {
        string probePath = Path.Combine(directoryPath, ".salryn-write-test.tmp");

        try
        {
            // Write then immediately delete probe to verify writability without leaving artifacts.
            File.WriteAllText(probePath, DateTimeOffset.UtcNow.ToString("O"));
            if (!File.Exists(probePath))
            {
                throw new InvalidOperationException($"Probe file was not written for {label}.");
            }
            File.Delete(probePath);

            Console.WriteLine($"{label} directory writable (probe): OK");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            try { if (File.Exists(probePath)) File.Delete(probePath); } catch { }

            throw new InvalidOperationException(
                $"PRODUCTION READINESS FAILURE: {label} directory is not writable. Path: {directoryPath}. " +
                $"Error: {ex.Message}. " +
                "Fix folder permissions for production deployment (ProgramData\\Salryn or equivalent).");
        }
        finally
        {
            try { if (File.Exists(probePath)) File.Delete(probePath); } catch { }
        }
    }
}
