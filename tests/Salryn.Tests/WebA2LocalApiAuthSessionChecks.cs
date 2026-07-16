namespace Salryn.Tests;

public static class WebA2LocalApiAuthSessionChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string compositionCsprojText = ReadRequiredFile(repoRoot, "src", "Salryn.Composition.Local", "Salryn.Composition.Local.csproj");
        string compositionText = ReadRequiredFile(repoRoot, "src", "Salryn.Composition.Local", "SalrynLocalComposition.cs");
        string apiCsprojText = ReadRequiredFile(repoRoot, "src", "Salryn.Api.Local", "Salryn.Api.Local.csproj");
        string apiProgramText = ReadRequiredFile(repoRoot, "src", "Salryn.Api.Local", "Program.cs")
            + "\n"
            + ReadRequiredFile(repoRoot, "src", "Salryn.Api.Local", "LocalApiApplicationFactory.cs");
        string hostCheckoutEndpointsText = ReadRequiredFile(repoRoot, "src", "Salryn.Api.Local", "LocalHostCheckoutEndpoints.cs");
        string hostCheckoutServiceText = ReadRequiredFile(repoRoot, "src", "Salryn.Api.Local", "Services", "HostOperatorCheckoutApiService.cs");
        string remoteReadOnlyEndpointsText = ReadRequiredFile(repoRoot, "src", "Salryn.Api.Local", "RemoteReadOnlyApiEndpoints.cs");
        string hostBootstrapText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Host.Local",
            "Bootstrap",
            "LocalHostHandlerBootstrapModels.cs");
        string sessionServiceText = ReadRequiredFile(repoRoot, "src", "Salryn.Api.Local", "Auth", "LocalApiSessionService.cs");
        string sessionContractsText = ReadRequiredFile(repoRoot, "src", "Salryn.Api.Local", "Contracts", "AuthSessionContracts.cs");
        string webSessionApiText = ReadRequiredFile(repoRoot, "src", "Salryn.App.Web", "src", "api", "session.ts");
        string webLoginPageText = ReadRequiredFile(repoRoot, "src", "Salryn.App.Web", "src", "pages", "LoginPage.tsx");
        string webAppText = ReadRequiredFile(repoRoot, "src", "Salryn.App.Web", "src", "App.tsx");
        string webUseAppSessionText = ReadRequiredFile(repoRoot, "src", "Salryn.App.Web", "src", "hooks", "useAppSession.ts");
        string webDashboardPageText = ReadRequiredFile(repoRoot, "src", "Salryn.App.Web", "src", "pages", "DashboardPage.tsx");
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
        RequireContains(apiProgramText, "AllowCredentials", "API CORS must allow credentials for session cookies.");
        RequireNotContains(apiProgramText, "AllowAnyOrigin", "API CORS must not use AllowAnyOrigin.");
        RequireContains(apiProgramText, "/api/auth/login", "API must expose POST /api/auth/login.");
        RequireContains(apiProgramText, "/api/auth/logout", "API must expose POST /api/auth/logout.");
        RequireContains(apiProgramText, "/api/session", "API must expose GET /api/session.");
        RequireContains(apiProgramText, "sessions.LoginAsync", "API login must use session service LoginAsync.");
        RequireNotContains(apiProgramText, "ListenAnyIP", "API must not listen on any IP.");
        RequireNotContains(apiProgramText, "0.0.0.0", "API must not bind to 0.0.0.0.");
        RequireNotContains(apiProgramText, "/api/session/handoff/register", "API must not expose handoff register endpoint.");
        RequireNotContains(apiProgramText, "/api/session/handoff/complete", "API must not expose handoff complete endpoint.");
        RequireNotContains(apiProgramText, "NativeHandoff", "API must not include native handoff types.");
        RequireContains(apiProgramText, "MapLocalHostCheckoutEndpoints", "API must map host-local checkout endpoints.");
        RequireContains(hostCheckoutEndpointsText, "/api/checkout", "Host-local POST /api/checkout must exist.");
        RequireContains(hostCheckoutEndpointsText, "LocalApiPermissions.SalesCreate", "Host checkout must require Sales.Create.");
        RequireContains(hostCheckoutServiceText, "IPosCheckoutApplicationService", "Host checkout must use authoritative sale completion service.");
        RequireNotContains(hostCheckoutServiceText, "LocalHostBootstrapNotImplementedHandler", "Host checkout must not use placeholder Local Host handlers.");
        RequireContains(hostBootstrapText, "LocalHostBootstrapNotImplementedHandler", "Placeholder Local Host handlers remain quarantined.");
        RequireNotContains(remoteReadOnlyEndpointsText, "MapPost(\"/api/checkout\"", "Remote read-only API must not expose host checkout mutation.");

        RequireContains(sessionServiceText, "IAuthenticationService", "Session service must use existing authentication service.");
        RequireContains(sessionServiceText, "_authentication.LoginAsync", "Session service must call IAuthenticationService.LoginAsync.");
        RequireNotContains(sessionServiceText, "NativeHandoff", "Session service must not include native handoff code.");
        RequireNotContains(sessionServiceText, "RegisterNativeHandoff", "Session service must not register native handoff sessions.");
        RequireNotContains(sessionServiceText, "CompleteNativeHandoff", "Session service must not complete native handoff sessions.");
        RequireNotContains(sessionServiceText, "EstablishSessionFromAuthenticatedUser", "Session service must not mint sessions from caller-supplied identity.");
        RequireNotContains(sessionContractsText, "PasswordHash", "Session contracts must not expose password hashes.");
        RequireNotContains(sessionContractsText, "NativeHandoff", "Session contracts must not include native handoff contracts.");
        RequireNotContains(sessionServiceText, "SalrynPasswordHasher", "Session service must not bypass authentication hashing path.");

        RequireContains(webSessionApiText, "/api/session", "Web app must read session state.");
        RequireContains(webSessionApiText, "/api/auth/login", "Web app must call /api/auth/login.");
        RequireContains(webSessionApiText, "credentials: \"include\"", "Web session client must send cookies.");
        RequireContains(webAppText, "LoginPage", "Web app must include login page.");
        RequireContains(webLoginPageText, "Sign in to Salryn POS", "Web login copy must use client-facing POS branding.");
        RequireNotContains(webLoginPageText, "local API", "Web login copy must not expose API diagnostics.");
        RequireNotContains(webLoginPageText, "WinForms login first", "Web login copy must not describe native login-first flow.");
        RequireNotContains(webLoginPageText, "Chromium shell handoff", "Web login copy must not describe native handoff.");
        RequireNotContains(webLoginPageText, "native login handoff", "Web login copy must not describe native login handoff.");
        RequireContains(webUseAppSessionText, "fetchSession", "Web app must remain session-aware.");
        RequireContains(webAppText, "useAppSession", "App must use session hook.");
        RequireNotContains(webSessionApiText, "upload", "Web app must not include upload support.");
        RequireNotContains(webSessionApiText, "blob", "Web app must not include blob support.");
        RequireNotContains(webSessionApiText, "file-picker", "Web app must not include file-picker support.");
        RequireNotContains(webDashboardPageText, "checkout", "Web app must not include checkout completion logic.");

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
