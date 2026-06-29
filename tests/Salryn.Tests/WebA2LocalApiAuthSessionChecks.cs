namespace Salryn.Tests;

public static class WebA2LocalApiAuthSessionChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string compositionCsprojText = ReadRequiredFile(repoRoot, "src", "Salryn.Composition.Local", "Salryn.Composition.Local.csproj");
        string compositionText = ReadRequiredFile(repoRoot, "src", "Salryn.Composition.Local", "SalrynLocalComposition.cs");
        string apiCsprojText = ReadRequiredFile(repoRoot, "src", "Salryn.Api.Local", "Salryn.Api.Local.csproj");
        string apiProgramText = ReadRequiredFile(repoRoot, "src", "Salryn.Api.Local", "Program.cs");
        string sessionServiceText = ReadRequiredFile(repoRoot, "src", "Salryn.Api.Local", "Auth", "LocalApiSessionService.cs");
        string sessionContractsText = ReadRequiredFile(repoRoot, "src", "Salryn.Api.Local", "Contracts", "AuthSessionContracts.cs");
        string webSessionApiText = ReadRequiredFile(repoRoot, "src", "Salryn.App.Web", "src", "api", "session.ts");
        string webAppText = ReadRequiredFile(repoRoot, "src", "Salryn.App.Web", "src", "App.tsx");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string solutionText = ReadRequiredFile(repoRoot, "Salryn.sln");

        RequireContains(solutionText, "Salryn.Composition.Local", "Solution must include Salryn.Composition.Local.");
        RequireContains(compositionCsprojText, "Salryn.Core", "Shared composition must reference Salryn.Core.");
        RequireContains(compositionCsprojText, "Salryn.Infrastructure", "Shared composition must reference Salryn.Infrastructure.");
        RequireNotContains(compositionCsprojText, "Salryn.App.WinForms", "Shared composition must not reference WinForms.");
        RequireNotContains(compositionCsprojText, "Salryn.Host.Local", "Shared composition must not reference Host.Local.");

        RequireContains(compositionText, "IAuthenticationService", "Shared composition must wire existing authentication service.");
        RequireContains(compositionText, "AuthenticationService", "Shared composition must use Salryn AuthenticationService.");
        RequireContains(compositionText, "DefaultDatabaseInitializationFactory", "Shared composition must use existing database initialization.");

        RequireContains(apiCsprojText, "Salryn.Composition.Local", "API must use shared composition project.");
        RequireNotContains(apiCsprojText, "Salryn.App.WinForms", "API must not reference WinForms.");

        RequireContains(apiProgramText, "SalrynLocalLaunchModeResolver", "API must resolve launch mode through shared composition.");
        RequireContains(apiProgramText, "127.0.0.1", "API must bind to localhost only.");
        RequireContains(apiProgramText, "/api/auth/login", "API must expose POST /api/auth/login.");
        RequireContains(apiProgramText, "/api/auth/logout", "API must expose POST /api/auth/logout.");
        RequireContains(apiProgramText, "/api/session", "API must expose GET /api/session.");
        RequireNotContains(apiProgramText, "ListenAnyIP", "API must not listen on any IP.");
        RequireNotContains(apiProgramText, "0.0.0.0", "API must not bind to 0.0.0.0.");

        RequireContains(sessionServiceText, "IAuthenticationService", "Session service must use existing authentication service.");
        RequireContains(sessionServiceText, "LoginAsync", "Session service must call existing LoginAsync.");
        RequireContains(sessionServiceText, "EstablishSessionFromAuthenticatedUser", "Session service must support native handoff foundation.");
        RequireNotContains(sessionContractsText, "PasswordHash", "Session contracts must not expose password hashes.");
        RequireNotContains(sessionServiceText, "SalrynPasswordHasher", "Session service must not bypass authentication hashing path.");

        RequireContains(webSessionApiText, "/api/session", "Web app must read session state.");
        RequireContains(webSessionApiText, "/api/auth/login", "Web app must support API login fallback.");
        RequireContains(webAppText, "LoginPage", "Web app must include dev login fallback.");
        RequireNotContains(webSessionApiText, "upload", "Web app must not include upload support.");
        RequireNotContains(webSessionApiText, "blob", "Web app must not include blob support.");
        RequireNotContains(webSessionApiText, "file-picker", "Web app must not include file-picker support.");

        RequireContains(coreSmokeText, "WebA2LocalApiAuthSessionChecks.Run();", "CoreSmoke must run WEB-A2 checks.");

        string migrationsPath = Path.Combine(repoRoot.FullName, "src", "Salryn.Infrastructure", "Data", "Migrations");
        int migrationCount = Directory.Exists(migrationsPath)
            ? Directory.GetFiles(migrationsPath, "*.cs", SearchOption.AllDirectories).Length
            : 0;

        if (migrationCount == 0)
        {
            throw new InvalidOperationException("Expected existing migrations folder for WEB-A2 no-schema guard baseline.");
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
        if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message);
        }
    }
}
