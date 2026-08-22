namespace Salryn.Tests;

// UI-A1R-P2: Inventory Visual Alignment guard.
// Proves Inventory and Adjustment screens use the SalrynUi ledger console system
// while preserving required behavior (reason required) and without forbidden references.

public static class UiA1RInventoryVisualAlignmentChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string ui = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Mvp", "SalrynUi.cs");
        string inv = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Inventory", "InventoryForm.cs");
        string adj = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Inventory", "InventoryAdjustmentForm.cs");

        // Use SalrynUi visual system
        RequireContains(inv, "SalrynUi.", "InventoryForm must use SalrynUi.");
        RequireContains(adj, "SalrynUi.", "InventoryAdjustmentForm must use SalrynUi.");

        // Ledger grid on inventory surfaces
        RequireContains(inv, "ConfigureInventoryGrid", "InventoryForm must style grids with ledger/inventory helper.");
        RequireContains(adj, "ConfigureInventoryGrid", "Adjustment must style product grid with ledger helper.");

        // Header + command/footer structures
        RequireContains(inv, "CreatePageHeader", "Inventory must use page header.");
        RequireContains(adj, "CreatePageHeader", "Adjustment must use page header.");
        RequireContains(adj, "CreateCommandBar", "Adjustment search must use grouped command bar.");
        RequireContains(adj, "CreateSectionTitle", "Adjustment must have section title.");
        RequireContains(adj, "CreateFieldLabel", "Adjustment must use field labels (with required for reason).");

        // Warning action for Adjust
        RequireContains(inv, "ConfigureWarningButton", "Inventory Adjust action must be styled with warning semantics.");
        RequireContains(adj, "ConfigureWarningButton", "Adjustment Apply must be styled warning/primary.");

        // Status/footer
        RequireContains(inv, "ConfigureFooterStatus", "Inventory status must use styled footer.");
        RequireContains(adj, "ConfigureFooterStatus", "Adjustment status must use styled footer.");

        // Reason-required flow still present (string from existing validation)
        RequireContains(adj, "Adjustment reason is required.", "Adjustment must still require a reason.");

        // Low stock / stock status visual helpers present in system
        RequireContains(ui, "GetStockStatusColor", "SalrynUi must provide stock status color helper for inventory visuals.");
        RequireContains(ui, "StyleStockStatusTag", "SalrynUi should provide stock status tag styling.");

        // No direct SQLite or construction of repos/services in the Inventory UI layer
        foreach (string t in new[] { inv, adj })
        {
            RequireNotContains(t, "Microsoft.Data.Sqlite", "Inventory UI must not reference SQLite directly.");
            RequireNotContains(t, "SqliteConnection", "Inventory UI must not create SQLite connections.");
            RequireNotContains(t, "new .*Repository", "Inventory forms must not construct repositories.");
            RequireNotContains(t, "new SalrynAppServices", "Inventory forms must not construct AppServices.");
        }

        // No forbidden markers
        foreach (string t in new[] { ui, inv, adj })
        {
            RequireNotContains(t, "CheckoutPresenter", "No checkout presenter.");
            RequireNotContains(t, "RMO", "No RMO.");
            RequireNotContains(t, "OfficialOutput", "No official output.");
            RequireNotContains(t, "printer", "No printer.");
            RequireNotContains(t, "scanner", "No scanner.");
            RequireNotContains(t, "hardware", "No hardware.");
            RequireNotContains(t, "PDF", "No PDF.");
            RequireNotContains(t, "Crystal", "No Crystal.");
            RequireNotContains(t, "ESC/POS", "No ESC/POS.");
            RequireNotContains(t, "LAN support", "No LAN.");
            RequireNotContains(t, "add LAN", "No add LAN.");
        }

        // Hard ban: no product image/photo/media/upload
        foreach (string t in new[] { ui, inv, adj })
        {
            RequireNotContains(t, "Image", "No product image support.");
            RequireNotContains(t, "Photo", "No product photo support.");
            RequireNotContains(t, "Picture", "No product picture support.");
            RequireNotContains(t, "Media", "No product media support.");
            RequireNotContains(t, "Upload", "No upload support.");
            RequireNotContains(t, "blob", "No blob storage.");
            RequireNotContains(t, "file picker", "No file picker for media.");
        }
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