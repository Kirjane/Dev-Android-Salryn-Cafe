using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Paths;

internal static class AppStartupFailureDiagnosticsSmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== APP STARTUP FAILURE DIAGNOSTICS SMOKE CHECKS =====");

        RunSourceGuard(projectRoot);

        // 5. Normal success comparison (run first to establish baseline)
        RunHappyPathSuccessComparison();

        // 1. Unwritable / invalid database directory
        RunUnwritableDatabaseDirectoryScenario();

        // 2. Invalid database file (garbage content)
        RunInvalidDatabaseFileScenario();

        // 3. Missing required app data directory
        RunMissingAppDataDirectoryScenario();

        // 4. Backup path unavailable / conflict
        RunBackupPathConflictScenario();

        Console.WriteLine("App startup failure diagnostics smoke checks succeeded.");
        Console.WriteLine();
    }

    private static void RunSourceGuard(string projectRoot)
    {
        string smokePath = Path.Combine(
            projectRoot,
            "tools",
            "Salryn.Tools.InfrastructureSmoke",
            "AppStartupFailureDiagnosticsSmokeChecks.cs");

        if (!File.Exists(smokePath))
        {
            throw new InvalidOperationException("App startup failure diagnostics smoke source not found for guard.");
        }

        string smokeText = File.ReadAllText(smokePath);

        RequireDoesNotContain(smokeText, "Salryn" + ".App" + ".Win" + "Forms", "App startup failure diagnostics smoke guard g1.");
        RequireDoesNotContain(smokeText, "System" + ".Windows" + ".Forms", "App startup failure diagnostics smoke guard g2.");
        RequireDoesNotContain(smokeText, "Application" + ".Run", "App startup failure diagnostics smoke guard g3.");
        RequireDoesNotContain(smokeText, "Checkout" + "Presenter", "App startup failure diagnostics smoke guard g4.");
        RequireDoesNotContain(smokeText, "R" + "MO", "App startup failure diagnostics smoke guard g5.");
        RequireDoesNotContain(smokeText, "Official" + "Output", "App startup failure diagnostics smoke guard g6.");
        RequireDoesNotContain(smokeText, "P" + "DF", "App startup failure diagnostics smoke guard g7.");
        RequireDoesNotContain(smokeText, "Crys" + "tal", "App startup failure diagnostics smoke guard g8.");
        RequireDoesNotContain(smokeText, "ESC" + "/POS", "App startup failure diagnostics smoke guard g9.");
        RequireDoesNotContain(smokeText, "print" + "er", "App startup failure diagnostics smoke guard g10.");
        RequireDoesNotContain(smokeText, "scan" + "ner", "App startup failure diagnostics smoke guard g11.");
        RequireDoesNotContain(smokeText, "hard" + "ware", "App startup failure diagnostics smoke guard g12.");
        RequireDoesNotContain(smokeText, "L" + "AN", "App startup failure diagnostics smoke guard g13.");
        RequireDoesNotContain(smokeText, "mob" + "ile", "App startup failure diagnostics smoke guard g14.");
        RequireDoesNotContain(smokeText, "re" + "design", "App startup failure diagnostics smoke guard g15.");
        RequireDoesNotContain(smokeText, "Win" + "Forms", "App startup failure diagnostics smoke guard g16.");
    }

    private static void RequireDoesNotContain(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RunHappyPathSuccessComparison()
    {
        Console.WriteLine("===== HAPPY PATH SUCCESS COMPARISON =====");

        string tempRoot = CreateUniqueTempRoot("happy");
        try
        {
            DbPaths paths = DbPaths.CreateDevelopment(tempRoot);
            DatabaseInitializationOrchestrator orchestrator =
                DefaultDatabaseInitializationFactory.Create(paths);

            Result<DatabaseInitializationStatus> statusResult = orchestrator.CheckStatus();
            if (statusResult.IsFailure)
            {
                throw new InvalidOperationException("Happy path CheckStatus should succeed.");
            }

            // Initialize should succeed on clean temp
            Result<DatabaseInitializationSummary> initResult = orchestrator.InitializeDatabase();
            if (initResult.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Happy path InitializeDatabase failed: {initResult.ErrorDetail?.Code} {initResult.ErrorDetail?.Message}");
            }

            Console.WriteLine("Observed diagnostic: SUCCESS (no failure)");
            Console.WriteLine($"Database path: {paths.DatabasePath}");
            Console.WriteLine("Happy path readiness/status: OK");
        }
        finally
        {
            CleanupTempRoot(tempRoot);
        }

        Console.WriteLine();
    }

    private static void RunUnwritableDatabaseDirectoryScenario()
    {
        Console.WriteLine("===== UNWRITABLE / INVALID DATABASE DIRECTORY =====");

        string tempRoot = CreateUniqueTempRoot("unwritable-data");
        DbPaths? paths = null;
        string blockerPath = string.Empty;

        try
        {
            paths = DbPaths.CreateDevelopment(tempRoot);

            // Ensure parent (.salryn-dev) exists, then place a *file* at the DataPath location.
            // This will cause Directory.CreateDirectory(DataPath) to fail.
            string devDir = Path.GetDirectoryName(paths.DataPath)!;
            Directory.CreateDirectory(devDir);
            blockerPath = paths.DataPath;
            File.WriteAllText(blockerPath, "BLOCKER: this is a file, not a directory");

            Console.WriteLine($"Data path blocked by file: {blockerPath}");

            DatabaseInitializationOrchestrator orchestrator =
                DefaultDatabaseInitializationFactory.Create(paths);

            SqliteConnectionFactory factory = new(paths);

            try
            {
                using var conn = factory.CreateConnection();
                conn.Open();
                SqlitePragma.Apply(conn);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or SqliteException)
            {
                string observed = $"{ex.GetType().Name}: {ex.Message}";
                Console.WriteLine($"Observed diagnostic: {observed}");
                // This is the expected failure mode for directory conflict.
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Observed diagnostic (outer): {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            // Attempt to remove blocker so cleanup can delete tree
            try { if (!string.IsNullOrEmpty(blockerPath) && File.Exists(blockerPath)) File.Delete(blockerPath); } catch { }
            CleanupTempRoot(tempRoot);
        }

        Console.WriteLine();
    }

    private static void RunInvalidDatabaseFileScenario()
    {
        Console.WriteLine("===== INVALID DATABASE FILE (GARBAGE CONTENT) =====");

        string tempRoot = CreateUniqueTempRoot("invalid-dbfile");
        try
        {
            DbPaths paths = DbPaths.CreateDevelopment(tempRoot);

            // Create the directories so the file write target is valid
            paths.EnsureDirectories();

            // Write non-SQLite garbage into the database file location
            string garbage = "NOT A VALID SQLITE DATABASE FILE " + Guid.NewGuid().ToString("N");
            File.WriteAllText(paths.DatabasePath, garbage);

            Console.WriteLine($"Garbage written to: {paths.DatabasePath}");

            DatabaseInitializationOrchestrator orchestrator =
                DefaultDatabaseInitializationFactory.Create(paths);

            Result<DatabaseInitializationSummary> result = orchestrator.InitializeDatabase();

            if (result.IsSuccess)
            {
                throw new InvalidOperationException("Expected InitializeDatabase to fail on garbage DB file.");
            }

            string observed = $"{result.ErrorDetail?.Code}: {result.ErrorDetail?.Message}";
            Console.WriteLine($"Observed diagnostic: {observed}");
        }
        finally
        {
            CleanupTempRoot(tempRoot);
        }

        Console.WriteLine();
    }

    private static void RunMissingAppDataDirectoryScenario()
    {
        Console.WriteLine("===== MISSING REQUIRED APP DATA DIRECTORY =====");

        string tempRoot = CreateUniqueTempRoot("missing-dir");
        try
        {
            DbPaths paths = DbPaths.CreateDevelopment(tempRoot);

            // Ensure root exists but deliberately remove the data subdirectory
            Directory.CreateDirectory(paths.RootPath);
            if (Directory.Exists(paths.DataPath))
            {
                Directory.Delete(paths.DataPath, recursive: true);
            }

            Console.WriteLine($"Data directory deleted for test: {paths.DataPath}");

            // Existing code should recreate via EnsureDirectories
            paths.EnsureDirectories();

            bool recreated = Directory.Exists(paths.DataPath);
            Console.WriteLine($"Observed diagnostic: EnsureDirectories recreated data dir = {recreated}");

            if (!recreated)
            {
                throw new InvalidOperationException("Existing EnsureDirectories did not recreate the missing data directory.");
            }

            // Also prove a connection factory / orchestrator path succeeds
            DatabaseInitializationOrchestrator orchestrator =
                DefaultDatabaseInitializationFactory.Create(paths);

            Result<DatabaseInitializationStatus> status = orchestrator.CheckStatus();
            if (status.IsFailure)
            {
                throw new InvalidOperationException("CheckStatus after recreation should succeed.");
            }

            Console.WriteLine("Missing directory case: code creates directories and proceeds: OK");
        }
        finally
        {
            CleanupTempRoot(tempRoot);
        }

        Console.WriteLine();
    }

    private static void RunBackupPathConflictScenario()
    {
        Console.WriteLine("===== BACKUP PATH UNAVAILABLE / CONFLICT =====");

        string tempRoot = CreateUniqueTempRoot("backup-conflict");
        string blocker = string.Empty;

        try
        {
            DbPaths paths = DbPaths.CreateDevelopment(tempRoot);

            // Create parent, then put a file where the backups directory should be
            string devParent = Path.GetDirectoryName(paths.BackupPath)!;
            Directory.CreateDirectory(devParent);
            blocker = paths.BackupPath;
            File.WriteAllText(blocker, "BLOCKER-FILE-FOR-BACKUPS");

            Console.WriteLine($"Backup path blocked by file: {blocker}");

            // Trigger EnsureDirectories which is called by connection creation and backup flows
            try
            {
                paths.EnsureDirectories();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"Observed diagnostic (EnsureDirectories): {ex.GetType().Name}: {ex.Message}");
            }

            // Also exercise through connection factory which calls Ensure
            try
            {
                SqliteConnectionFactory factory = new(paths);
                using var conn = factory.CreateConnection();
                // opening may also surface if directories are still bad
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Console.WriteLine($"Observed diagnostic (connection): {ex.GetType().Name}: {ex.Message}");
            }
        }
        finally
        {
            try { if (!string.IsNullOrEmpty(blocker) && File.Exists(blocker)) File.Delete(blocker); } catch { }
            CleanupTempRoot(tempRoot);
        }

        Console.WriteLine();
    }

    private static string CreateUniqueTempRoot(string suffix)
    {
        return Path.Combine(
            Path.GetTempPath(),
            $"Salryn-APP-SHIP-C3-{suffix}-{Guid.NewGuid():N}");
    }

    private static void CleanupTempRoot(string tempRoot)
    {
        try
        {
            SqliteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            if (Directory.Exists(tempRoot))
            {
                const int max = 8;
                for (int i = 1; i <= max; i++)
                {
                    try
                    {
                        SqliteConnection.ClearAllPools();
                        Directory.Delete(tempRoot, recursive: true);
                        return;
                    }
                    catch (IOException) when (i < max) { Thread.Sleep(120); }
                    catch (UnauthorizedAccessException) when (i < max) { Thread.Sleep(120); }
                }

                SqliteConnection.ClearAllPools();
                Directory.Delete(tempRoot, recursive: true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning during cleanup of {tempRoot}: {ex.Message}");
        }
    }
}
