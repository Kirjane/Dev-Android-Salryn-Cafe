namespace Salryn.Tests;

// Kirch: POS-STAB-BATCH-7 (B7) Presentation & Operator UX Polish guard.
// Mirrors the static source-text-check style used by PosStabB6AuditOperationsChecks: it reads the
// touched POS UI surfaces from the repo root and asserts that the B7 polish is present while no
// forbidden markers, direct SQL, or schema changes leaked into the UI layer.
public static class PosStabB7PresentationUxChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string mvpUiText = ReadFeatureFile(repoRoot, "Mvp", "J20MvpUi.cs");
        string shellText = ReadFeatureFile(repoRoot, "Shell", "SalrynShellForm.cs");
        string authText = ReadFeatureFile(repoRoot, "Auth", "AuthStartupForm.cs");
        string dashboardText = ReadFeatureFile(repoRoot, "Dashboard", "DashboardForm.cs");
        string backupText = ReadFeatureFile(repoRoot, "Backup", "BackupForm.cs");
        string auditText = ReadFeatureFile(repoRoot, "Audit", "AuditLogViewerForm.cs");
        string usersText = ReadFeatureFile(repoRoot, "Users", "UsersAdminForm.cs");
        string productsText = ReadFeatureFile(repoRoot, "Products", "ProductManagementForm.cs");
        string inventoryText = ReadFeatureFile(repoRoot, "Inventory", "InventoryForm.cs");
        string checkoutText = ReadFeatureFile(repoRoot, "Checkout", "CheckoutShellForm.cs");
        string salesText = ReadFeatureFile(repoRoot, "Sales", "SalesHistoryForm.cs");
        string settingsText = ReadFeatureFile(repoRoot, "Settings", "StoreSettingsForm.cs");
        string coreSmokeText = ReadRequiredFile(
            repoRoot,
            "tools",
            "Salryn.Tools.CoreSmoke",
            "Program.cs");

        // Key POS screens still exist. ReadFeatureFile above already throws if any is missing; these
        // explicit checks document the required primary form surfaces for B7.
        RequireContains(shellText, "class SalrynShellForm", "B7 shell form must still exist.");
        RequireContains(authText, "class AuthStartupForm", "B7 auth startup form must still exist.");
        RequireContains(dashboardText, "class DashboardForm", "B7 dashboard form must still exist.");
        RequireContains(backupText, "class BackupForm", "B7 backup form must still exist.");
        RequireContains(auditText, "class AuditLogViewerForm", "B7 audit viewer form must still exist.");
        RequireContains(usersText, "class UsersAdminForm", "B7 users admin form must still exist.");
        RequireContains(productsText, "class ProductManagementForm", "B7 products form must still exist.");
        RequireContains(inventoryText, "class InventoryForm", "B7 inventory form must still exist.");
        RequireContains(checkoutText, "class CheckoutShellForm", "B7 checkout form must still exist.");
        RequireContains(salesText, "class SalesHistoryForm", "B7 sales history form must still exist.");
        RequireContains(settingsText, "class StoreSettingsForm", "B7 store settings form must still exist.");

        // Shared J20MvpUi helpers introduced by B7 must exist so polish stays centralized.
        RequireContains(mvpUiText, "public static ToolTip CreateToolTip()", "B7 must add a shared tooltip helper to J20MvpUi.");
        RequireContains(mvpUiText, "public static Button InfoButton(", "B7 must add a shared info/help-popup button to J20MvpUi.");
        RequireContains(mvpUiText, "public static bool ConfirmDestructiveAction(", "B7 must add a shared confirmation popup helper to J20MvpUi.");
        RequireContains(mvpUiText, "public static void ShowBlockedAction(", "B7 must add a shared blocked-action popup helper to J20MvpUi.");
        RequireContains(mvpUiText, "public static Label EmptyStateLabel(", "B7 must add a shared empty-state label helper to J20MvpUi.");
        RequireContains(mvpUiText, "NothingToShow", "B7 must add a standardized empty-state string to J20MvpUi.");
        RequireContains(mvpUiText, "MessageBox.Show", "B7 shared helpers must use standard WinForms MessageBox popups.");

        // Tooltip usage (hover guidance) must appear across the touched POS surfaces.
        RequireContains(shellText, "J20MvpUi.CreateToolTip()", "B7 shell must use shared tooltip guidance.");
        RequireContains(shellText, "_toolTip.SetToolTip(", "B7 shell must attach hover tooltips (e.g. locked module reasons).");
        RequireContains(authText, "_toolTip.SetToolTip(", "B7 auth form must attach hover tooltips.");
        RequireContains(dashboardText, "_toolTip.SetToolTip(", "B7 dashboard must attach hover tooltips.");
        RequireContains(backupText, "_toolTip.SetToolTip(", "B7 backup must attach hover tooltips.");
        RequireContains(auditText, "_toolTip.SetToolTip(", "B7 audit viewer must attach hover tooltips.");
        RequireContains(usersText, "_toolTip.SetToolTip(", "B7 users admin must attach hover tooltips.");
        RequireContains(productsText, "_toolTip.SetToolTip(", "B7 products must attach hover tooltips.");
        RequireContains(inventoryText, "_toolTip.SetToolTip(", "B7 inventory must attach hover tooltips.");
        RequireContains(salesText, "_toolTip.SetToolTip(", "B7 sales history must attach hover tooltips.");
        RequireContains(settingsText, "_toolTip.SetToolTip(", "B7 store settings must attach hover tooltips.");

        // Info/help popups must be present where screens previously relied on static text only.
        RequireContains(dashboardText, "J20MvpUi.InfoButton(", "B7 dashboard must add an info/help popup.");
        RequireContains(backupText, "J20MvpUi.InfoButton(", "B7 backup must add an info/help popup.");
        RequireContains(settingsText, "J20MvpUi.InfoButton(", "B7 store settings must add an info/help popup.");

        // Confirmation popups must guard risky / irreversible operator actions.
        RequireContains(backupText, "J20MvpUi.ConfirmDestructiveAction(", "B7 backup restore must show a confirmation popup.");
        RequireContains(usersText, "J20MvpUi.ConfirmDestructiveAction(", "B7 user deactivation must show a confirmation popup.");
        RequireContains(productsText, "J20MvpUi.ConfirmDestructiveAction(", "B7 product deactivation must show a confirmation popup.");

        // Blocked-action popups must give clear operator-facing failure messages.
        RequireContains(inventoryText, "J20MvpUi.ShowBlockedAction(", "B7 inventory must surface clear blocked-action popups.");
        RequireContains(salesText, "J20MvpUi.ShowBlockedAction(", "B7 sales history must surface clear blocked-action popups.");
        RequireContains(usersText, "J20MvpUi.ShowBlockedAction(", "B7 users admin must surface clear blocked-action popups.");

        // Standardized empty-state copy must appear where lists can be empty.
        RequireContains(productsText, "J20MvpUi.StateText.NothingToShow", "B7 products must show a standardized empty state.");
        RequireContains(inventoryText, "J20MvpUi.StateText.NothingToShow", "B7 inventory must show a standardized empty state.");
        RequireContains(salesText, "J20MvpUi.StateText.NothingToShow", "B7 sales history must show a standardized empty state.");
        RequireContains(auditText, "J20MvpUi.StateText.NothingToShow", "B7 audit viewer must show a standardized empty state.");

        // No forbidden markers, direct SQL, or schema changes may leak into the touched UI surfaces.
        foreach (KeyValuePair<string, string> surface in TouchedSurfaces(
            mvpUiText,
            shellText,
            authText,
            dashboardText,
            backupText,
            auditText,
            usersText,
            productsText,
            inventoryText,
            salesText,
            settingsText))
        {
            RequireNoForbiddenMarkers(surface.Value, surface.Key);
            RequireNoDirectSql(surface.Value, surface.Key);
            RequireNoSchemaChange(surface.Value, surface.Key);
        }

        // CoreSmoke must execute the B7 guard right after the B6 guard.
        RequireContains(coreSmokeText, "PosStabB7PresentationUxChecks.Run();", "CoreSmoke must run POS-STAB-B7 presentation UX checks.");
    }

    private static Dictionary<string, string> TouchedSurfaces(
        string mvpUiText,
        string shellText,
        string authText,
        string dashboardText,
        string backupText,
        string auditText,
        string usersText,
        string productsText,
        string inventoryText,
        string salesText,
        string settingsText)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["J20MvpUi"] = mvpUiText,
            ["Shell"] = shellText,
            ["Auth"] = authText,
            ["Dashboard"] = dashboardText,
            ["Backup"] = backupText,
            ["Audit viewer"] = auditText,
            ["Users admin"] = usersText,
            ["Products"] = productsText,
            ["Inventory"] = inventoryText,
            ["Sales history"] = salesText,
            ["Store settings"] = settingsText
        };
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

    private static string ReadFeatureFile(
        DirectoryInfo repoRoot,
        string feature,
        string fileName)
    {
        return ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Features",
            feature,
            fileName);
    }

    private static string ReadRequiredFile(
        DirectoryInfo repoRoot,
        params string[] pathParts)
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

    // Kirch: split each marker so this guard file itself does not contain the literal scanned text.
    private static string[] ForbiddenMarkers()
    {
        return new[]
        {
            "R" + "MO",
            "official " + "output",
            "P" + "DF",
            "Cry" + "stal",
            "." + "rpt",
            "ESC" + "/" + "POS",
            "scan" + "ner",
            "hard" + "ware",
            "raw " + "USB",
            "prin" + "ter"
        };
    }

    private static void RequireNoForbiddenMarkers(string text, string surface)
    {
        foreach (string marker in ForbiddenMarkers())
        {
            RequireDoesNotContain(text, marker, $"B7 {surface} must not introduce forbidden marker: {marker}");
        }
    }

    private static void RequireNoDirectSql(string text, string surface)
    {
        RequireDoesNotContain(text, "Microsoft.Data.Sqlite", $"B7 {surface} must not reference SQLite directly.");
        RequireDoesNotContain(text, "SqliteConnection", $"B7 {surface} must not open SQLite connections directly.");
        RequireDoesNotContain(text, "INSERT " + "INTO ", $"B7 {surface} must not issue insert SQL.");
        RequireDoesNotContain(text, "UPDATE " + "SET ", $"B7 {surface} must not issue update SQL.");
        RequireDoesNotContain(text, "DELETE " + "FROM ", $"B7 {surface} must not issue delete SQL.");
    }

    private static void RequireNoSchemaChange(string text, string surface)
    {
        RequireDoesNotContain(text, "CREATE " + "TABLE", $"B7 {surface} must not add schema creation.");
        RequireDoesNotContain(text, "DROP " + "TABLE", $"B7 {surface} must not drop schema.");
    }

    private static void RequireContains(
        string text,
        string value,
        string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireDoesNotContain(
        string text,
        string value,
        string message)
    {
        if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message);
        }
    }
}
