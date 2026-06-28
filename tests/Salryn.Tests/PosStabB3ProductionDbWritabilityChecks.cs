namespace Salryn.Tests;

public static class PosStabB3ProductionDbWritabilityChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string programText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Program.cs");
        string launchResolverText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "SalrynAppLaunchModeResolver.cs");
        string dbPathsText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Paths", "DbPaths.cs");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(programText, "SalrynAppLaunchModeResolver.Resolve(args);", "B3 startup must resolve launch mode before composing services.");
        RequireContains(programText, "launchPlan.Mode == SalrynAppLaunchMode.Production", "B3 production database writability preflight must be production-only.");
        RequireContains(programText, "DbPaths.CreateProduction().ValidateProductionWritability();", "B3 startup must run explicit production DB writability validation before migrations.");
        RequireContains(programText, "ShowStartupFailure(BuildStartupFailureMessage(writabilityResult.ErrorDetail));", "B3 startup must show a clear operator-facing preflight error.");
        RequireContains(programText, "SalrynAppServices.CreateDevelopment(launchPlan.ProjectRoot!)", "B3 startup must preserve the separate development database path.");
        RequireContains(programText, "SalrynAppServices.CreateProduction()", "B3 startup must preserve production service composition after preflight.");

        RequireContains(launchResolverText, "--dev", "B3 launch mode guard expects development mode to remain explicit.");

        RequireContains(dbPathsText, "ValidateProductionWritability", "B3 DbPaths must expose production writability validation.");
        RequireContains(dbPathsText, "EnsureDirectories();", "B3 production writability validation must check the target directories before SQLite opens.");
        RequireContains(dbPathsText, "File.WriteAllText(", "B3 production writability validation must perform a filesystem write probe.");
        RequireContains(dbPathsText, "File.Delete(", "B3 production writability validation must clean up the write probe.");
        RequireContains(dbPathsText, "UnauthorizedAccessException", "B3 production writability validation must catch access failures explicitly.");
        RequireContains(dbPathsText, "IOException", "B3 production writability validation must catch IO failures explicitly.");
        RequireContains(dbPathsText, "DatabasePath.ProductionNotWritable", "B3 production writability validation must return a stable error code.");
        RequireContains(dbPathsText, "Database path: ", "B3 production writability validation must include the target database path.");
        RequireContains(dbPathsText, "ProgramData\\Salryn", "B3 production writability validation must guide operators to fix production folder permissions.");
        RequireContains(dbPathsText, "--dev only for development", "B3 production writability validation must not suggest silent fallback paths.");

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
}
