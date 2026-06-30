namespace Salryn.Tests;

public static class WebA4NativeHostVisualFidelityChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string hostDir = Path.Combine(repoRoot.FullName, "src", "Salryn.Desktop.WebView");
        string webSrc = Path.Combine(repoRoot.FullName, "src", "Salryn.App.Web", "src");

        // --- Native WebView host project ---
        string hostCsprojPath = Path.Combine(hostDir, "Salryn.Desktop.WebView.csproj");
        if (!File.Exists(hostCsprojPath))
        {
            throw new InvalidOperationException("Salryn.Desktop.WebView project must exist.");
        }

        string hostCsproj = File.ReadAllText(hostCsprojPath);
        string solutionText = ReadRequiredFile(repoRoot.FullName, "Salryn.sln");
        string hostSource = ReadAllSource(hostDir, new[] { ".cs" }, SearchOption.TopDirectoryOnly);

        RequireContains(solutionText, "Salryn.Desktop.WebView", "Native host must be in the solution.");
        RequireNotContains(hostCsproj, "Salryn.App.WinForms", "Native host must not reference WinForms project.");
        RequireNotContains(hostCsproj, "Salryn.Core", "Native host must not reference Core directly.");
        RequireNotContains(hostCsproj, "Salryn.Infrastructure", "Native host must not reference Infrastructure directly.");
        RequireContains(hostCsproj, "Microsoft.Web.WebView2", "Native host must use WebView2 package.");

        RequireContains(hostSource, "WebView2", "Native host must use WebView2.");
        RequireContains(hostSource, "127.0.0.1", "Native host must target localhost only.");
        RequireContains(hostSource, "/health", "Native host must check API /health.");
        RequireContains(hostSource, "SALRYN_PROJECT_ROOT", "WebView API launcher must set SALRYN_PROJECT_ROOT for dev DB resolution.");
        RequireContains(hostSource, "LocalWebOriginPolicy", "WebView host must validate local frontend origins.");
        RequireContains(hostSource, "NavigationStarting", "WebView host must block non-local navigation.");
        RequireNotContains(hostSource, "0.0.0.0", "Native host must not target 0.0.0.0.");
        RequireNotContains(hostSource, "ListenAnyIP", "Native host must not use ListenAnyIP.");
        RequireNotContains(hostSource, "handoff", "Native host must not reintroduce session handoff.");

        // --- Local API security posture preserved (with optional static hosting) ---
        string apiProgram = ReadRequiredFile(repoRoot.FullName, "src", "Salryn.Api.Local", "Program.cs");
        RequireContains(apiProgram, "127.0.0.1", "API must bind to localhost only.");
        RequireNotContains(apiProgram, "0.0.0.0", "API must not bind to 0.0.0.0.");
        RequireNotContains(apiProgram, "ListenAnyIP", "API must not listen on any IP.");
        RequireNotContains(apiProgram, "UseDirectoryBrowser", "API must not enable directory browsing.");
        RequireNotContains(apiProgram, "handoff", "API must not reintroduce session handoff.");

        // --- Web foundation still wired to real backend ---
        string sessionApiText = ReadRequiredFile(webSrc, "api", "session.ts");
        string dashboardApiText = ReadRequiredFile(webSrc, "api", "dashboard.ts");
        string webAppText = ReadRequiredFile(webSrc, "App.tsx");
        string dashboardPageText = ReadRequiredFile(webSrc, "pages", "DashboardPage.tsx");
        string loginPageText = ReadRequiredFile(webSrc, "pages", "LoginPage.tsx");
        string coreSmokeText = ReadRequiredFile(
            Path.Combine(repoRoot.FullName, "tools", "Salryn.Tools.CoreSmoke"), "Program.cs");

        RequireFileExists(webSrc, "components", "AppShell.tsx");
        RequireContains(sessionApiText, "/api/auth/login", "Web app must call /api/auth/login.");
        RequireContains(sessionApiText, "/api/session", "Web app must call /api/session.");
        RequireContains(loginPageText, "login(", "Login page must invoke the login client.");
        RequireContains(dashboardApiText, "/api/dashboard/summary", "Web app must call /api/dashboard/summary.");
        RequireContains(dashboardPageText, "fetchDashboardSummary", "Dashboard must read the dashboard summary.");
        RequireContains(dashboardPageText, "fetchDashboardSummary", "Dashboard must read the dashboard summary.");
        RequireContains(webAppText, "AppShell", "Authenticated app must render through AppShell.");

        RequireContains(coreSmokeText, "WebA4NativeHostVisualFidelityChecks.Run();", "CoreSmoke must run WEB-A4 checks.");

        // --- Scoped: no real module/checkout/handoff wiring in API clients or pages ---
        foreach (string scoped in new[] { sessionApiText, dashboardApiText, dashboardPageText, loginPageText })
        {
            RequireNotContains(scoped, "checkout", "WEB-A4 must not add checkout logic to API clients or pages.");
            RequireNotContains(scoped, "handoff", "WEB-A4 must not reintroduce session handoff.");
            RequireNotContains(scoped, "NativeHandoff", "WEB-A4 must not reintroduce native handoff.");
        }

        // Read-only module pages are allowed after WEB-B0.
        string[] forbiddenPagePrefixes =
        {
            "Checkout", "Backup", "Settings", "OfficialOutput", "Rmo",
        };
        string pagesDir = Path.Combine(webSrc, "pages");
        foreach (string pageFile in Directory.GetFiles(pagesDir, "*.tsx", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(pageFile);
            if (name.Equals("LoginPage.tsx", StringComparison.Ordinal) ||
                name.Equals("DashboardPage.tsx", StringComparison.Ordinal) ||
                name.Equals("ProductsPage.tsx", StringComparison.Ordinal) ||
                name.Equals("InventoryPage.tsx", StringComparison.Ordinal) ||
                name.Equals("SalesHistoryPage.tsx", StringComparison.Ordinal) ||
                name.Equals("UsersPage.tsx", StringComparison.Ordinal) ||
                name.Equals("AuditPage.tsx", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (string prefix in forbiddenPagePrefixes)
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"WEB-A4 must not add module page: {name}");
                }
            }
        }

        // --- Broad scan of web source for forbidden capabilities ---
        string combinedWebSource = ReadAllSource(webSrc, new[] { ".ts", ".tsx", ".css" }, SearchOption.AllDirectories);
        string[] forbiddenTokens =
        {
            "/api/session/handoff/register",
            "/api/session/handoff/complete",
            "NativeHandoff",
            "Crystal",
            "ESC/POS",
            "escpos",
            "file-picker",
            "multipart/form-data",
            "upload",
            "blob",
            "printer",
            "scanner",
            "photo",
            "RMO",
        };

        foreach (string token in forbiddenTokens)
        {
            RequireNotContains(combinedWebSource, token, $"WEB-A4 must not add forbidden capability: {token}");
        }

        // --- No fake module data copied from the visual reference screenshots ---
        string[] fakeDataMarkers =
        {
            "Maria Santos",
            "Dela Cruz",
            "PRD-0",
            "INV-0",
            "Nescafe",
            "Receipt No",
        };

        foreach (string marker in fakeDataMarkers)
        {
            RequireNotContains(combinedWebSource, marker, $"WEB-A4 must not embed fake module data: {marker}");
        }
    }

    private static string ReadAllSource(string root, string[] extensions, SearchOption searchOption)
    {
        IEnumerable<string> files = Directory
            .GetFiles(root, "*.*", searchOption)
            .Where(path => extensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));

        return string.Join("\n", files.Select(File.ReadAllText));
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

    private static void RequireFileExists(string root, params string[] pathParts)
    {
        string path = root;

        foreach (string pathPart in pathParts)
        {
            path = Path.Combine(path, pathPart);
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Required file was not found: {path}");
        }
    }

    private static string ReadRequiredFile(string root, params string[] pathParts)
    {
        string path = root;

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
