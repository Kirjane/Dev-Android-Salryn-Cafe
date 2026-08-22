namespace Salryn.Tests;

// UI-A1R-P1: Product Management Visual + Action UX Pilot guard.
// Proves the new SalrynUi architecture is present and used on the product surfaces.
// Must not regress B3 behavior invariants or introduce forbidden references.

public static class UiA1RProductManagementVisualPilotChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string uiKitText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Mvp", "SalrynUi.cs");
        string mvpCompatText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Mvp", "J20MvpUi.cs");
        string managementText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Products", "ProductManagementForm.cs");
        string createText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Products", "ProductCreateForm.cs");
        string editText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Products", "ProductEditForm.cs");

        // Architecture exists
        RequireContains(uiKitText, "public static class SalrynUi", "UI-A1R-P1 must introduce SalrynUi.");
        RequireContains(uiKitText, "public static class Colors", "SalrynUi must define Colors palette.");
        RequireContains(uiKitText, "AppBackground", "SalrynUi palette must include AppBackground.");
        RequireContains(uiKitText, "Surface", "SalrynUi palette must include Surface.");
        RequireContains(uiKitText, "HeaderSlate", "SalrynUi palette must include HeaderSlate.");
        RequireContains(uiKitText, "Primary", "SalrynUi palette must include Primary.");
        RequireContains(uiKitText, "Success", "SalrynUi palette must include Success.");
        RequireContains(uiKitText, "Warning", "SalrynUi palette must include Warning.");
        RequireContains(uiKitText, "Danger", "SalrynUi palette must include Danger.");

        RequireContains(uiKitText, "public static class Typography", "SalrynUi must define Typography.");
        RequireContains(uiKitText, "public static class Spacing", "SalrynUi must define Spacing.");

        RequireContains(uiKitText, "ConfigurePrimaryButton", "SalrynUi must provide ConfigurePrimaryButton.");
        RequireContains(uiKitText, "ConfigureSecondaryButton", "SalrynUi must provide ConfigureSecondaryButton.");
        RequireContains(uiKitText, "ConfigureSuccessButton", "SalrynUi must provide ConfigureSuccessButton.");
        RequireContains(uiKitText, "ConfigureDangerButton", "SalrynUi must provide ConfigureDangerButton.");

        RequireContains(uiKitText, "ConfigureLedgerGrid", "SalrynUi must provide ConfigureLedgerGrid.");
        RequireContains(uiKitText, "SetStatusNeutral", "SalrynUi must provide SetStatusNeutral.");
        RequireContains(uiKitText, "SetStatusSuccess", "SalrynUi must provide SetStatusSuccess.");
        RequireContains(uiKitText, "SetStatusWarning", "SalrynUi must provide SetStatusWarning.");
        RequireContains(uiKitText, "SetStatusError", "SalrynUi must provide SetStatusError.");

        RequireContains(uiKitText, "ConfirmDanger", "SalrynUi must provide ConfirmDanger.");
        RequireContains(uiKitText, "PromptRequiredReason", "SalrynUi must provide PromptRequiredReason.");
        RequireContains(uiKitText, "ShowBlocked", "SalrynUi must provide ShowBlocked.");
        RequireContains(uiKitText, "ShowError", "SalrynUi must provide ShowError.");

        // Product surfaces use the new architecture
        RequireContains(managementText, "SalrynUi.", "ProductManagementForm must use SalrynUi.");
        RequireContains(createText, "SalrynUi.", "ProductCreateForm must use SalrynUi.");
        RequireContains(editText, "SalrynUi.", "ProductEditForm must use SalrynUi.");

        // Toolbar hierarchy intent visible via the configure calls
        RequireContains(managementText, "ConfigureSuccessButton", "Products toolbar must use success for Add.");
        RequireContains(managementText, "ConfigureDangerButton", "Products toolbar must use danger for Deactivate.");

        // Behavior invariants (B3) must still be present
        RequireContains(editText, "_stockOnHandTextBox.ReadOnly = true;", "Stock On Hand must remain read-only in Product Edit.");
        RequireContains(editText, "Stock On Hand (read-only)", "Stock On Hand label must still indicate read-only.");
        RequireContains(managementText, "PromptDeactivateReason", "Deactivate must still surface a reason prompt (B3 contract).");
        RequireContains(managementText, "Products.DeactivateReasonRequired", "Blank reason must still be rejected.");

        // Deactivate flow uses the shared helpers
        RequireContains(managementText, "SalrynUi.ConfirmDanger", "Deactivate must use SalrynUi.ConfirmDanger.");
        RequireContains(managementText, "SalrynUi.PromptRequiredReason", "Deactivate must use SalrynUi.PromptRequiredReason.");

        // No direct SQLite or construction of repos/services in the UI layer
        foreach (string t in new[] { managementText, createText, editText })
        {
            RequireNotContains(t, "Microsoft.Data.Sqlite", "Product UI must not reference SQLite directly.");
            RequireNotContains(t, "SqliteConnection", "Product UI must not create SQLite connections.");
            RequireNotContains(t, "new ProductManagementPresenter", "Product forms must not construct presenters.");
            RequireNotContains(t, "new .*Repository", "Product forms must not construct repositories.");
        }

        // No forbidden surface markers introduced in this pilot
        foreach (string t in new[] { uiKitText, managementText, createText, editText })
        {
            RequireNotContains(t, "CheckoutPresenter", "UI-A1R must not reference checkout presenter.");
            RequireNotContains(t, "RMO", "UI-A1R must not reference RMO.");
            RequireNotContains(t, "OfficialOutput", "UI-A1R must not reference official output.");
            RequireNotContains(t, "printer", "UI-A1R must not reference printer.");
            RequireNotContains(t, "scanner", "UI-A1R must not reference scanner.");
            RequireNotContains(t, "hardware", "UI-A1R must not reference hardware.");
            RequireNotContains(t, "PDF", "UI-A1R must not reference PDF.");
            RequireNotContains(t, "Crystal", "UI-A1R must not reference Crystal.");
            RequireNotContains(t, "ESC/POS", "UI-A1R must not reference ESC/POS.");
            RequireNotContains(t, "LAN support", "UI-A1R must not reference LAN support.");
            RequireNotContains(t, "add LAN", "UI-A1R must not add LAN.");
            RequireNotContains(t, "LAN/mobile", "UI-A1R must not reference LAN/mobile marker.");
            RequireNotContains(t, "add mobile", "UI-A1R must not reference add mobile.");
        }

        // J20MvpUi compatibility layer still present
        RequireContains(mvpCompatText, "ConfigurePrimaryButton", "J20MvpUi must continue to expose ConfigurePrimaryButton for compatibility.");
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