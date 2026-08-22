namespace Salryn.Tests;

public static class WebA1LocalApiReactDashboardChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string apiCsprojText = ReadRequiredFile(repoRoot, "src", "Salryn.Api.Local", "Salryn.Api.Local.csproj");
        string apiProgramText = ReadRequiredFile(repoRoot, "src", "Salryn.Api.Local", "Program.cs")
            + "\n"
            + ReadRequiredFile(repoRoot, "src", "Salryn.Api.Local", "LocalApiApplicationFactory.cs");
        string webPackageJsonText = ReadRequiredFile(repoRoot, "src", "Salryn.App.Web", "package.json");
        string dashboardApiText = ReadRequiredFile(repoRoot, "src", "Salryn.App.Web", "src", "api", "dashboard.ts");
        string dashboardPageText = ReadRequiredFile(repoRoot, "src", "Salryn.App.Web", "src", "pages", "DashboardPage.tsx");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string solutionText = ReadRequiredFile(repoRoot, "Salryn.sln");

        RequireContains(solutionText, "Salryn.Api.Local", "Solution must include Salryn.Api.Local.");
        RequireContains(apiCsprojText, "Salryn.Composition.Local", "API must use shared composition.");
        RequireNotContains(apiCsprojText, "Salryn.App.WinForms", "API must not reference WinForms.");

        RequireContains(apiProgramText, "127.0.0.1", "API must bind to localhost only.");
        RequireContains(apiProgramText, "/health", "API must expose GET /health.");
        RequireContains(apiProgramText, "/api/dashboard/summary", "API must expose GET /api/dashboard/summary.");
        RequireNotContains(apiProgramText, "ListenAnyIP", "API must not listen on any IP.");
        RequireNotContains(apiProgramText, "0.0.0.0", "API must not bind to 0.0.0.0.");

        RequireContains(webPackageJsonText, "react", "Web app must use React.");
        RequireContains(webPackageJsonText, "vite", "Web app must use Vite.");
        RequireContains(webPackageJsonText, "tailwindcss", "Web app must use Tailwind CSS.");
        RequireContains(webPackageJsonText, "@tanstack/react-query", "Web app must use TanStack Query.");

        RequireContains(dashboardApiText, "/api/dashboard/summary", "Web app must call /api/dashboard/summary.");
        RequireNotContains(dashboardPageText, "checkout", "Web app must not include checkout completion logic.");
        RequireNotContains(dashboardApiText, "upload", "Web app must not include upload support.");
        RequireNotContains(dashboardApiText, "blob", "Web app must not include blob support.");
        RequireNotContains(dashboardApiText, "file-picker", "Web app must not include file-picker support.");

        RequireContains(coreSmokeText, "WebA1LocalApiReactDashboardChecks.Run();", "CoreSmoke must run WEB-A1 checks.");
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
        if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message);
        }
    }
}
