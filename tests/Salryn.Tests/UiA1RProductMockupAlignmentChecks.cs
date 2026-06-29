namespace Salryn.Tests;

// UI-A1R-P1B: Product Mockup Alignment guard.
// Verifies Product screens are using SalrynUi and have mockup-aligned visual elements
// without introducing forbidden behavior or new backend dependencies.

public static class UiA1RProductMockupAlignmentChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string salrynUi = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Mvp", "SalrynUi.cs");
        string management = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Products", "ProductManagementForm.cs");
        string create = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Products", "ProductCreateForm.cs");
        string edit = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Products", "ProductEditForm.cs");

        // Core architecture still in use
        RequireContains(salrynUi, "public static class SalrynUi", "SalrynUi must exist.");
        RequireContains(management, "SalrynUi.", "ProductManagementForm must use SalrynUi.");
        RequireContains(create, "SalrynUi.", "ProductCreateForm must use SalrynUi.");
        RequireContains(edit, "SalrynUi.", "ProductEditForm must use SalrynUi.");

        // Mockup alignment signals
        RequireContains(management, "Summary", "Product Management should have summary strip for mockup alignment.");
        RequireContains(management, "_summaryTotal", "Summary strip must expose Total.");
        RequireContains(management, "_summaryActive", "Summary strip must expose Active.");
        RequireContains(management, "_summaryLowStock", "Summary strip must expose Low Stock when data available.");
        RequireContains(management, "UpdateSummary", "Client-side summary computation must exist.");

        // Deactivate UX via shared helpers
        RequireContains(management, "SalrynUi.ConfirmDanger", "Deactivate must use shared ConfirmDanger.");
        RequireContains(management, "SalrynUi.PromptRequiredReason", "Deactivate must use shared required reason prompt.");
        RequireContains(management, "Products.DeactivateReasonRequired", "Blank reason must still block.");

        // Stock On Hand remains strictly read-only
        RequireContains(edit, "_stockOnHandTextBox.ReadOnly = true;", "Stock On Hand must remain read-only.");
        RequireContains(edit, "Stock On Hand (read-only)", "Edit form must label Stock On Hand as read-only.");

        // No forbidden patterns
        foreach (string t in new[] { management, create, edit })
        {
            RequireNotContains(t, "Microsoft.Data.Sqlite", "Product UI must not reference SQLite.");
            RequireNotContains(t, "new .*Repository", "Product forms must not construct repositories.");
            RequireNotContains(t, "new ProductManagementPresenter", "Product forms must not construct presenters.");
        }

        // No unsafe mockup features turned into real behavior
        RequireNotContains(management, "Export", "Export button must not be added from mockup.");
        RequireNotContains(management, "Branch", "Branch selector must not be added unless backend-supported.");
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln"))) return current;
            current = current.Parent;
        }
        current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln"))) return current;
            current = current.Parent;
        }
        throw new InvalidOperationException("Could not locate Salryn repository root.");
    }

    private static string ReadRequiredFile(DirectoryInfo repoRoot, params string[] pathParts)
    {
        string path = repoRoot.FullName;
        foreach (string p in pathParts) path = Path.Combine(path, p);
        if (!File.Exists(path)) throw new InvalidOperationException("Required file not found: " + path);
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