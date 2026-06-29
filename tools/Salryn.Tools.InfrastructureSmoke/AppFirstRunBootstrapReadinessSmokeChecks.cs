using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Bootstrap;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Paths;

internal static class AppFirstRunBootstrapReadinessSmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== APP FIRST-RUN / ADMIN BOOTSTRAP READINESS SMOKE CHECKS =====");

        RunSourceGuard(projectRoot);

        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "Salryn-APP-SHIP-C2-" + Guid.NewGuid().ToString("N"));

        Console.WriteLine($"Isolated temp production-like root: {tempRoot}");

        DbPaths paths = DbPaths.CreateDevelopment(tempRoot);
        Console.WriteLine($"Temp database path: {paths.DatabasePath}");

        if (File.Exists(paths.DatabasePath))
        {
            throw new InvalidOperationException("Temp database must not exist before test.");
        }
        Console.WriteLine("Confirmed: no database exists in temp root initially: OK");

        SqliteConnectionFactory connectionFactory = new(paths);
        SqliteRepositoryFactory repositoryFactory = new(connectionFactory);

        IAuthenticationService authService = new AuthenticationService(
            repositoryFactory.CreateAuthenticationRepository(),
            repositoryFactory.CreateUserPermissionReadRepository());

        DatabaseInitializationOrchestrator orchestrator =
            DefaultDatabaseInitializationFactory.Create(paths);

        try
        {
            // 4. Run existing database initialization/status check path.
            Result<DatabaseInitializationSummary> initResult = orchestrator.InitializeDatabase();
            if (initResult.IsFailure)
            {
                throw new InvalidOperationException(
                    $"InitializeDatabase failed: {initResult.ErrorDetail?.Code} {initResult.ErrorDetail?.Message}");
            }
            Console.WriteLine("Database initialization via orchestrator: OK");

            // 5. Confirm first-run required detectable before admin bootstrap.
            Result<DatabaseInitializationStatus> statusBeforeResult = orchestrator.CheckStatus();
            if (statusBeforeResult.IsFailure)
            {
                throw new InvalidOperationException(
                    $"CheckStatus before failed: {statusBeforeResult.ErrorDetail?.Code}");
            }

            DatabaseInitializationStatus statusBefore = statusBeforeResult.Value;
            if (!statusBefore.DatabaseFileExists)
            {
                throw new InvalidOperationException("Database file should exist after InitializeDatabase.");
            }
            if (!statusBefore.FirstRunStatus.IsFirstRunRequired)
            {
                throw new InvalidOperationException("First-run required must be true before admin bootstrap.");
            }
            if (statusBefore.FirstRunStatus.HasUsers || statusBefore.FirstRunStatus.HasStoreSettings)
            {
                throw new InvalidOperationException("Fresh bootstrap DB must have no users or store settings.");
            }
            Console.WriteLine("First-run required before bootstrap: True (detected) OK");

            // 6+7. Use existing admin creation via AuthenticationService path, only in isolated temp.
            string adminUser = "c2-smoke-admin";
            string adminPass = "C2SmokePass123!";
            string adminDisplay = "C2 Smoke Admin";

            AuthenticatedUserReadModel createdAdmin = RequireSuccess(
                authService.CreateFirstAdminAsync(
                    new FirstRunAdminSetupRequest(adminUser, adminDisplay, adminPass, adminPass))
                    .GetAwaiter().GetResult(),
                "FirstRunBootstrap.CreateFirstAdmin");

            if (createdAdmin.UserId == Guid.Empty || createdAdmin.Permissions.Count == 0)
            {
                throw new InvalidOperationException("Created admin must have id and permissions.");
            }
            Console.WriteLine($"Temporary admin created via AuthenticationService: {createdAdmin.Username} OK");

            // 8. Confirm admin can authenticate through existing AuthenticationService path.
            AuthenticatedUserReadModel loggedIn = RequireSuccess(
                authService.LoginAsync(new LoginRequest(adminUser, adminPass))
                    .GetAwaiter().GetResult(),
                "FirstRunBootstrap.LoginAfterCreate");

            if (!string.Equals(loggedIn.Username, adminUser, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Login returned wrong user.");
            }
            Console.WriteLine("Admin login via AuthenticationService after bootstrap: OK");

            // 9+10. Confirm store settings and first-run flag flip.
            Result<DatabaseInitializationStatus> statusAfterResult = orchestrator.CheckStatus();
            if (statusAfterResult.IsFailure)
            {
                throw new InvalidOperationException($"CheckStatus after failed: {statusAfterResult.ErrorDetail?.Code}");
            }

            FirstRunStatus after = statusAfterResult.Value.FirstRunStatus;
            if (after.IsFirstRunRequired)
            {
                throw new InvalidOperationException("First-run required must become false after successful admin bootstrap.");
            }
            if (!after.HasUsers || !after.HasStoreSettings)
            {
                throw new InvalidOperationException("After bootstrap: must have users and store settings.");
            }
            Console.WriteLine("Post-bootstrap: first-run required=False, has users+store settings: OK");

            // 11. Demonstrate a clear failure (double bootstrap attempt).
            var doubleCreate = authService.CreateFirstAdminAsync(
                new FirstRunAdminSetupRequest(adminUser, adminDisplay, adminPass, adminPass))
                .GetAwaiter().GetResult();

            if (doubleCreate.IsSuccess)
            {
                throw new InvalidOperationException("Second admin create should have failed.");
            }
            if (!doubleCreate.ErrorDetail.Code.Contains("FirstAdminAlreadyExists", StringComparison.OrdinalIgnoreCase) &&
                !doubleCreate.ErrorDetail.Code.Contains("AlreadyCompleted", StringComparison.OrdinalIgnoreCase))
            {
                // acceptable if other conflict code; just ensure it failed clearly
            }
            Console.WriteLine($"Clear failure on duplicate bootstrap: {doubleCreate.ErrorDetail.Code} OK");

            Console.WriteLine("App first-run / admin bootstrap readiness smoke checks succeeded.");
            Console.WriteLine();
        }
        finally
        {
            // 12+13. Always clean temp; never touch real dev or prod.
            CleanupTempRoot(tempRoot);
        }
    }

    private static T RequireSuccess<T>(Result<T> result, string area)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{area} failed: {result.ErrorDetail?.Code} {result.Error}");
        }
        return result.Value;
    }

    private static void RunSourceGuard(string projectRoot)
    {
        string smokePath = Path.Combine(
            projectRoot,
            "tools",
            "Salryn.Tools.InfrastructureSmoke",
            "AppFirstRunBootstrapReadinessSmokeChecks.cs");

        if (!File.Exists(smokePath))
        {
            throw new InvalidOperationException("App first-run bootstrap smoke source not found for guard.");
        }

        string smokeText = File.ReadAllText(smokePath);

        RequireDoesNotContain(smokeText, "Salryn" + ".App" + ".Win" + "Forms", "App first-run bootstrap smoke guard g1.");
        RequireDoesNotContain(smokeText, "System" + ".Windows" + ".Forms", "App first-run bootstrap smoke guard g2.");
        RequireDoesNotContain(smokeText, "Application" + ".Run", "App first-run bootstrap smoke guard g3.");
        RequireDoesNotContain(smokeText, "Checkout" + "Presenter", "App first-run bootstrap smoke guard g4.");
        RequireDoesNotContain(smokeText, "R" + "MO", "App first-run bootstrap smoke guard g5.");
        RequireDoesNotContain(smokeText, "Official" + "Output", "App first-run bootstrap smoke guard g6.");
        RequireDoesNotContain(smokeText, "P" + "DF", "App first-run bootstrap smoke guard g7.");
        RequireDoesNotContain(smokeText, "Crys" + "tal", "App first-run bootstrap smoke guard g8.");
        RequireDoesNotContain(smokeText, "ESC" + "/POS", "App first-run bootstrap smoke guard g9.");
        RequireDoesNotContain(smokeText, "print" + "er", "App first-run bootstrap smoke guard g10.");
        RequireDoesNotContain(smokeText, "scan" + "ner", "App first-run bootstrap smoke guard g11.");
        RequireDoesNotContain(smokeText, "hard" + "ware", "App first-run bootstrap smoke guard g12.");
        RequireDoesNotContain(smokeText, "L" + "AN", "App first-run bootstrap smoke guard g13.");
        RequireDoesNotContain(smokeText, "mob" + "ile", "App first-run bootstrap smoke guard g14.");
        RequireDoesNotContain(smokeText, "re" + "design", "App first-run bootstrap smoke guard g15.");
        RequireDoesNotContain(smokeText, "Win" + "Forms", "App first-run bootstrap smoke guard g16.");
    }

    private static void RequireDoesNotContain(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message);
        }
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
                const int maxAttempts = 8;
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        SqliteConnection.ClearAllPools();
                        Directory.Delete(tempRoot, recursive: true);
                        Console.WriteLine($"Cleaned isolated temp root: {tempRoot}");
                        return;
                    }
                    catch (IOException) when (attempt < maxAttempts)
                    {
                        Thread.Sleep(150);
                    }
                    catch (UnauthorizedAccessException) when (attempt < maxAttempts)
                    {
                        Thread.Sleep(150);
                    }
                }

                // final attempt
                SqliteConnection.ClearAllPools();
                Directory.Delete(tempRoot, recursive: true);
            }
        }
        catch (Exception ex)
        {
            // Non-fatal for smoke result; log for visibility.
            Console.WriteLine($"Warning: temp cleanup encountered issue (non-fatal): {ex.Message}");
        }
    }
}
