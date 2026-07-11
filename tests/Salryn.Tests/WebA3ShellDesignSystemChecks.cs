namespace Salryn.Tests;

public static class WebA3ShellDesignSystemChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string webSrc = Path.Combine(repoRoot.FullName, "src", "Salryn.App.Web", "src");

        // Required files exist.
        RequireFileExists(webSrc, "components", "AppShell.tsx");
        RequireFileExists(webSrc, "pages", "LoginPage.tsx");
        RequireFileExists(webSrc, "pages", "DashboardPage.tsx");
        RequireDirectoryExists(webSrc, "components", "ui");
        RequireFileExists(webSrc, "components", "ui", "MetricCard.tsx");
        RequireFileExists(webSrc, "components", "ui", "StatusPill.tsx");
        RequireFileExists(webSrc, "components", "ui", "DataTable.tsx");

        string sessionApiText = ReadRequiredFile(webSrc, "api", "session.ts");
        string dashboardApiText = ReadRequiredFile(webSrc, "api", "dashboard.ts");
        string webAppText = ReadRequiredFile(webSrc, "App.tsx");
        string dashboardPageText = ReadRequiredFile(webSrc, "pages", "DashboardPage.tsx");
        string loginPageText = ReadRequiredFile(webSrc, "pages", "LoginPage.tsx");
        string coreSmokeText = ReadRequiredFile(
            Path.Combine(repoRoot.FullName, "tools", "Salryn.Tools.CoreSmoke"), "Program.cs");

        // Real backend wiring preserved.
        RequireContains(sessionApiText, "/api/auth/login", "Web app must call /api/auth/login.");
        RequireContains(sessionApiText, "/api/session", "Web app must call /api/session.");
        RequireContains(dashboardApiText, "/api/dashboard/summary", "Web app must call /api/dashboard/summary.");
        RequireContains(dashboardPageText, "fetchDashboardSummary", "Dashboard must read the dashboard summary.");
        RequireContains(webAppText, "AppShell", "Authenticated app must render through AppShell.");
        RequireContains(webAppText, "useAppSession", "App must remain session-aware.");
        RequireContains(loginPageText, "Sign in to Salryn Ledger Console", "Login must keep Ledger Console branding.");
        RequireContains(loginPageText, "connects to the Salryn local API on this device", "Login must describe local API connection.");

        // Guard wired into CoreSmoke.
        RequireContains(coreSmokeText, "WebA3ShellDesignSystemChecks.Run();", "CoreSmoke must run WEB-A3 checks.");

        // Scoped: dashboard/login/session clients remain checkout-free; host checkout lives in CheckoutPage.
        foreach (string scoped in new[] { sessionApiText, dashboardApiText, dashboardPageText, loginPageText })
        {
            RequireNotContains(scoped, "checkout", "WEB-A3 must not add checkout logic to dashboard/session clients.");
            RequireNotContains(scoped, "handoff", "WEB-A3 must not reintroduce session handoff.");
            RequireNotContains(scoped, "NativeHandoff", "WEB-A3 must not reintroduce native handoff.");
        }

        // Read-only module pages are allowed after WEB-B0.
        string[] forbiddenPagePrefixes =
        {
            "Backup", "Settings", "OfficialOutput",
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
                name.Equals("AuditPage.tsx", StringComparison.Ordinal) ||
                name.Equals("CheckoutPage.tsx", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (string prefix in forbiddenPagePrefixes)
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"WEB-A3 must not add module page: {name}");
                }
            }
        }

        // Broad scan of web source for clearly-unique forbidden capabilities.
        string combinedWebSource = ReadAllWebSource(webSrc);
        string[] forbiddenTokens =
        {
            "/api/session/handoff/register",
            "/api/session/handoff/complete",
            "NativeHandoff",
            "CrystalDecisions",
            "SAPBusinessObjects",
            ".rpt",
            "ESC/POS",
            "escpos",
            "file-picker",
            "multipart/form-data",
            "/upload",
            "FormData(",
            "type=\"file\"",
            "blob",
            "window.print",
            "PrinterSettings",
            "printerTarget",
            "scanner",
            "photo",
        };

        foreach (string token in forbiddenTokens)
        {
            RequireNotContains(combinedWebSource, token, $"WEB-A3 must not add forbidden capability: {token}");
        }
    }

    private static string ReadAllWebSource(string webSrc)
    {
        IEnumerable<string> files = Directory
            .GetFiles(webSrc, "*.*", SearchOption.AllDirectories)
            .Where(path =>
                path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".css", StringComparison.OrdinalIgnoreCase));

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

    private static void RequireDirectoryExists(string root, params string[] pathParts)
    {
        string path = root;

        foreach (string pathPart in pathParts)
        {
            path = Path.Combine(path, pathPart);
        }

        if (!Directory.Exists(path))
        {
            throw new InvalidOperationException($"Required directory was not found: {path}");
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
