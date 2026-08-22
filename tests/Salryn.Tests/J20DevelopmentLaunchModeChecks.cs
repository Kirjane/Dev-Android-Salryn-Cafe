namespace Salryn.Tests;

public static class J20DevelopmentLaunchModeChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string programText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Program.cs");
        string resolverText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "SalrynAppLaunchModeResolver.cs");
        string launchSettingsText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Properties", "launchSettings.json");
        string handoffText = ReadRequiredFile(repoRoot, "00-HANDOFF", "J20_3B_DEVELOPMENT_APP_LAUNCH_MODE.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(programText, "static void Main(string[] args)", "Program.Main must accept launch arguments.");
        RequireContains(programText, "SalrynAppLaunchModeResolver.CreateServices(args)", "Program must create services through the launch resolver.");
        RequireNotContains(programText, "SalrynAppServices.CreateProduction()", "Program must not hardcode production services directly.");

        RequireContains(resolverText, "public const string DevelopmentArgument = \"--dev\";", "Resolver must expose --dev launch argument.");
        RequireContains(resolverText, "SALRYN_APP_ENV", "Resolver must support development launch through environment variable.");
        RequireContains(resolverText, "SALRYN_PROJECT_ROOT", "Resolver must support explicit project root override.");
        RequireContains(resolverText, "SalrynAppServices.CreateDevelopment(plan.ProjectRoot!)", "Resolver must route development launches to CreateDevelopment.");
        RequireContains(resolverText, "SalrynAppServices.CreateProduction()", "Resolver must preserve production default behavior.");
        RequireContains(resolverText, "Salryn.sln", "Resolver must locate the repository root for development database paths.");

        RequireContains(launchSettingsText, "\"Salryn.App.WinForms Development\"", "Launch settings must include a development profile.");
        RequireContains(launchSettingsText, "\"commandLineArgs\": \"--dev\"", "Development launch profile must pass --dev.");
        RequireContains(launchSettingsText, "\"SALRYN_APP_ENV\": \"Development\"", "Development launch profile must set SALRYN_APP_ENV.");

        RequireContains(handoffText, "Status: Development launch mode only.", "J20-3B handoff must mark launch-mode-only scope.");
        RequireContains(handoffText, "No database schema changes.", "J20-3B handoff must preserve database boundary.");
        RequireContains(handoffText, ".salryn-dev", "J20-3B handoff must document development database path.");

        RequireContains(coreSmokeText, "J20DevelopmentLaunchModeChecks.Run();", "CoreSmoke must run J20 development launch checks.");
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