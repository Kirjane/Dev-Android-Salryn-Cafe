namespace Salryn.Tests;

public static class PosStabB3ProductionDbWritabilityChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string programText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Program.cs");
        string launchResolverText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "SalrynAppLaunchModeResolver.cs");
        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string dbPathsText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Paths", "DbPaths.cs");
        string installerText = ReadRequiredFile(repoRoot, "installer", "SalrynSetup.iss");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(programText, "SalrynAppLaunchModeResolver.CreateServices(args);", "B3 startup must preserve the app launch resolver composition entry point.");
        RequireContains(programText, "ShowStartupFailure(BuildStartupFailureMessage(ex.Message));", "B3 startup must show a clear operator-facing preflight error.");

        RequireContains(launchResolverText, "SalrynAppLaunchPlan plan = Resolve(args);", "B3 launch resolver must still resolve launch mode before composing services.");
        RequireContains(launchResolverText, "SalrynAppServices.CreateDevelopment(plan.ProjectRoot!)", "B3 startup must preserve the separate development database path.");
        RequireContains(launchResolverText, "SalrynAppServices.CreateProduction()", "B3 startup must preserve production service composition after preflight.");
        RequireContains(launchResolverText, "--dev", "B3 launch mode guard expects development mode to remain explicit.");
        RequireDoesNotContain(launchResolverText, "Salryn.Infrastructure", "B3 launch mode resolver must not leak Infrastructure outside the App.WinForms composition root.");
        RequireDoesNotContain(launchResolverText, "ValidateProductionWritability", "B3 production DB writability preflight must stay inside the App.WinForms composition root.");

        RequireContains(appServicesText, "public static SalrynAppServices CreateProduction()", "B3 startup must keep production composition in AppServices.");
        RequireContains(appServicesText, "DbPaths productionPaths = DbPaths.CreateProduction();", "B3 production database writability preflight must use production database paths.");
        RequireContains(appServicesText, "productionPaths.ValidateProductionWritability();", "B3 startup must run explicit production DB writability validation before migrations.");
        RequireContains(appServicesText, "throw new InvalidOperationException(", "B3 startup must surface production database writability failures before migrations.");
        RequireContains(appServicesText, "return Create(productionPaths);", "B3 startup must compose services only after production path validation passes.");

        RequireContains(dbPathsText, "ValidateProductionWritability", "B3 DbPaths must expose production writability validation.");
        RequireContains(dbPathsText, "EnsureDirectories();", "B3 production writability validation must check the target directories before SQLite opens.");
        RequireContains(dbPathsText, "File.WriteAllText(", "B3 production writability validation must perform a filesystem write probe.");
        RequireContains(dbPathsText, "File.Delete(", "B3 production writability validation must clean up the write probe.");
        RequireContains(dbPathsText, "File.Exists(DatabasePath)", "B3 production writability validation must detect an existing database file.");
        RequireContains(dbPathsText, "File.Open(", "B3 production writability validation must probe existing database file write access.");
        RequireContains(dbPathsText, "FileAccess.ReadWrite", "B3 production writability validation must require read/write access to the existing database file.");
        RequireContains(dbPathsText, "UnauthorizedAccessException", "B3 production writability validation must catch access failures explicitly.");
        RequireContains(dbPathsText, "IOException", "B3 production writability validation must catch IO failures explicitly.");
        RequireContains(dbPathsText, "DatabasePath.ProductionNotWritable", "B3 production writability validation must return a stable error code.");
        RequireContains(dbPathsText, "database folder or database file is not writable", "B3 production writability validation must explain both folder and existing database file failures.");
        RequireContains(dbPathsText, "Database path: ", "B3 production writability validation must include the target database path.");
        RequireContains(dbPathsText, "ProgramData\\\\Salryn", "B3 production writability validation must guide operators to fix production folder permissions.");
        RequireContains(dbPathsText, "--dev only for development", "B3 production writability validation must not suggest silent fallback paths.");

        RequireContains(installerText, "[Dirs]", "B3 installer must provision the production data directories explicitly.");
        RequireContains(installerText, "{commonappdata}\\Salryn", "B3 installer must target the production ProgramData root.");
        RequireContains(installerText, "{commonappdata}\\Salryn\\data", "B3 installer must provision the SQLite data directory.");
        RequireContains(installerText, "Permissions: users-modify", "B3 installer must allow the non-elevated local API to write SQLite WAL/SHM files.");

        RequireContains(coreSmokeText, "PosStabB3ProductionDbWritabilityChecks.Run();", "CoreSmoke must run POS-STAB-B3 production DB writability checks.");
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
