namespace Salryn.Tests;

// UI-A1: Visual System Foundation + Shell/Dashboard Refresh guard.
// Proves the visual token layer and usage in allowed surfaces only.
// Must not introduce forbidden patterns.

public static class UiA1VisualSystemFoundationChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string uiKitText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Mvp", "J20MvpUi.cs");
        string shellText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string dashboardText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Dashboard", "DashboardForm.cs");

        // Core visual system must exist and expose tokens/helpers.
        RequireContains(uiKitText, "public static class Colors", "UI-A1 visual tokens must be defined.");
        RequireContains(uiKitText, "ConfigureNavButton", "UI-A1 must provide nav button styling helper.");
        RequireContains(uiKitText, "CardWithAccent", "UI-A1 must provide improved card helper.");
        RequireContains(uiKitText, "DenseGrid", "UI-A1 must provide dense grid helper.");

        // Shell and Dashboard must consume the shared visual helper (not hardcode raw colors everywhere).
        RequireContains(shellText, "J20MvpUi.Colors", "Shell must use shared UI-A1 tokens.");
        RequireContains(shellText, "J20MvpUi.ConfigureNavButton", "Shell nav buttons must use UI-A1 styling helper.");
        RequireContains(dashboardText, "J20MvpUi.", "Dashboard must use shared UI-A1 helpers/tokens.");

        // Source guard: no forbidden patterns added by UI-A1.
        // Shell is the router and already references several modules; do not fail on legitimate existing references.
        foreach (string text in new[] { uiKitText, dashboardText })
        {
            RequireNotContains(text, "Microsoft.Data.Sqlite", "UI-A1 must not reference SQLite directly.");
            RequireNotContains(text, "SqliteConnection", "UI-A1 must not create SQLite connections.");
            RequireNotContains(text, "CheckoutPresenter", "UI-A1 must not reference checkout presenter.");
            RequireNotContains(text, "RMO", "UI-A1 must not reference RMO.");
            RequireNotContains(text, "OfficialOutput", "UI-A1 must not reference official output.");
            RequireNotContains(text, "PDF", "UI-A1 must not reference PDF.");
            RequireNotContains(text, "Crystal", "UI-A1 must not reference Crystal.");
            RequireNotContains(text, "ESC/POS", "UI-A1 must not reference ESC/POS.");
            RequireNotContains(text, "printer", "UI-A1 must not reference printer.");
            RequireNotContains(text, "scanner", "UI-A1 must not reference scanner.");
            RequireNotContains(text, "hardware", "UI-A1 must not reference hardware.");
            RequireNotContains(text, "LAN support", "UI-A1 must not add LAN support.");
            RequireNotContains(text, "LAN/mobile", "UI-A1 must not reference LAN/mobile.");
            RequireNotContains(text, "add mobile", "UI-A1 must not reference mobile.");
        }

        // Shell-specific: still ensure we didn't add new direct SQLite or hardware.
        RequireNotContains(shellText, "Microsoft.Data.Sqlite", "Shell visual must not reference SQLite.");
        RequireNotContains(shellText, "SqliteConnection", "Shell visual must not create SQLite connections.");

        // Confirm no business logic or repository changes leaked into UI files for this phase.
        RequireNotContains(shellText, "SaleReadRepository", "Shell visual refresh must not read repositories directly.");
        RequireNotContains(dashboardText, "new SaleReadRepository", "Dashboard visual must not construct repositories.");
        RequireNotContains(dashboardText, "new ProductReadRepository", "Dashboard visual must not construct repositories.");
        RequireNotContains(dashboardText, "new InventoryReadRepository", "Dashboard visual must not construct repositories.");

        // The test itself is the guard.
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
        foreach (string part in pathParts) path = Path.Combine(path, part);
        if (!File.Exists(path)) throw new InvalidOperationException($"Required file not found: {path}");
        return File.ReadAllText(path);
    }

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal)) throw new InvalidOperationException(message);
    }

    private static void RequireNotContains(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(message);
    }
}