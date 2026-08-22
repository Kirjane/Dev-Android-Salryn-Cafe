namespace Salryn.Tests;

// UI-A1R-P1C: Product Management Full Visual Parity guard (WinForms-only).
// Proves deeper ledger console alignment on product surfaces using SalrynUi helpers.
// Enforces no product image/photo/media/upload support and no forbidden references.

public static class UiA1RProductVisualParityChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string ui = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Mvp", "SalrynUi.cs");
        string mgmt = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Products", "ProductManagementForm.cs");
        string create = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Products", "ProductCreateForm.cs");
        string edit = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Products", "ProductEditForm.cs");

        // Architecture in use
        RequireContains(ui, "public static class SalrynUi", "SalrynUi required.");
        RequireContains(mgmt, "SalrynUi.", "Management must use SalrynUi.");
        RequireContains(create, "SalrynUi.", "Create must use SalrynUi.");
        RequireContains(edit, "SalrynUi.", "Edit must use SalrynUi.");

        // P1C structures on Product Management
        RequireContains(mgmt, "CreatePageHeader", "Management must use page header helper.");
        RequireContains(mgmt, "CreateCommandBar", "Management must use command bar grouping.");
        RequireContains(mgmt, "CreateSummaryMetric", "Management must use styled summary metrics.");
        RequireContains(mgmt, "CreateDangerSeparator", "Management must visually separate Deactivate.");
        RequireContains(mgmt, "ConfigureFooterStatus", "Management status must use footer styling.");
        RequireContains(mgmt, "WireGridStatusBadges", "Management grid must apply visual status badges from existing data.");

        // P1C structures on Create/Edit
        RequireContains(create, "CreatePageHeader", "Create must use page header.");
        RequireContains(create, "CreateSectionTitle", "Create must use section title.");
        RequireContains(create, "StyleFooterStrip", "Create footer must be styled.");
        RequireContains(edit, "CreatePageHeader", "Edit must use page header.");
        RequireContains(edit, "CreateSectionTitle", "Edit must use section title.");
        RequireContains(edit, "Stock is adjusted from Inventory", "Edit must show stock helper text under Stock On Hand.");
        RequireContains(edit, "StyleFooterStrip", "Edit footer must be styled.");

        // Dialog parity via shared
        RequireContains(mgmt, "SalrynUi.ConfirmDanger", "Deactivate must use shared ConfirmDanger.");
        RequireContains(mgmt, "SalrynUi.PromptRequiredReason", "Deactivate must use shared PromptRequiredReason.");
        RequireContains(mgmt, "Products.DeactivateReasonRequired", "Blank reason must still block.");

        // Stock read-only invariant
        RequireContains(edit, "_stockOnHandTextBox.ReadOnly = true;", "Stock On Hand must remain read-only in Edit.");
        RequireContains(edit, "Stock On Hand (read-only)", "Edit must indicate read-only stock.");

        // No direct construction / sqlite
        foreach (var t in new[] { mgmt, create, edit })
        {
            RequireNotContains(t, "Microsoft.Data.Sqlite", "No SQLite in product UI.");
            RequireNotContains(t, "new .*Repository", "No repo construction in product UI.");
            RequireNotContains(t, "new ProductManagementPresenter", "No presenter construction in forms.");
        }

        // No forbidden surface markers (use precise tokens to avoid matching user-facing copy like "checkout")
        foreach (var t in new[] { ui, mgmt, create, edit })
        {
            RequireNotContains(t, "CheckoutPresenter", "No checkout presenter references.");
            RequireNotContains(t, "RMO", "No RMO references.");
            RequireNotContains(t, "OfficialOutput", "No official output references.");
            RequireNotContains(t, "printer", "No printer references.");
            RequireNotContains(t, "scanner", "No scanner references.");
            RequireNotContains(t, "hardware", "No hardware references.");
            RequireNotContains(t, "PDF", "No PDF references.");
            RequireNotContains(t, "Crystal", "No Crystal references.");
            RequireNotContains(t, "ESC/POS", "No ESC/POS references.");
            RequireNotContains(t, "LAN support", "No LAN references.");
            RequireNotContains(t, "add LAN", "No add LAN.");
        }

        // Hard ban: no product image/photo/media/upload support
        foreach (var t in new[] { ui, mgmt, create, edit })
        {
            RequireNotContains(t, "Image", "No product image support.");
            RequireNotContains(t, "Photo", "No product photo support.");
            RequireNotContains(t, "Picture", "No product picture support.");
            RequireNotContains(t, "Media", "No product media support.");
            RequireNotContains(t, "Upload", "No upload support.");
            RequireNotContains(t, "blob", "No blob/image storage.");
            RequireNotContains(t, "file picker", "No file picker for media.");
        }

        // Third party UI not introduced here
        RequireNotContains(ui, "third", "No third-party UI in SalrynUi for parity phase.");
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