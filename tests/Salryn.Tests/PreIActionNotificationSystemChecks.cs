namespace Salryn.Tests;

/// <summary>
/// PRE-I source proof for the app-level action notification infrastructure and
/// high-value action wiring. This is structural React proof, not browser proof.
/// </summary>
public static class PreIActionNotificationSystemChecks
{
    public static void Run()
    {
        string root = FindRoot();
        string infrastructure = Read(root, "src", "Salryn.App.Web", "src", "components", "ui", "Notifications.tsx");
        string app = Read(root, "src", "Salryn.App.Web", "src", "App.tsx");
        string safeErrors = Read(root, "src", "Salryn.App.Web", "src", "utils", "notificationError.ts");
        string license = ReadPage(root, "LicensePage.tsx");
        string lan = ReadPage(root, "HostLanControlPage.tsx");
        string backup = ReadPage(root, "BackupRestorePage.tsx");
        string products = ReadPage(root, "ProductsPage.tsx");
        string inventory = ReadPage(root, "InventoryPage.tsx");
        string checkout = ReadPage(root, "CheckoutPage.tsx");
        string bootstrap = ReadPage(root, "BootstrapSetupPage.tsx");
        string sales = ReadPage(root, "SalesHistoryPage.tsx");

        AssertInfrastructure(infrastructure, app, safeErrors);
        AssertLicense(license);
        AssertLan(lan);
        AssertBackupRestore(backup);
        AssertProductInventory(products, inventory);
        AssertCheckoutSales(checkout, sales);
        AssertBootstrap(bootstrap);
        AssertAntiRegression(string.Join("\n", license, lan, backup, products, inventory, checkout, bootstrap, sales));
    }

    private static void AssertInfrastructure(string infrastructure, string app, string safeErrors)
    {
        foreach (string required in new[]
        {
            "export function NotificationProvider",
            "export function useNotifications",
            "export function NotificationViewport",
            "success: (title, description)",
            "error: (title, description)",
            "warning: (title, description)",
            "info: (title, description)",
            "const dismiss = useCallback",
            "aria-live=\"polite\"",
            "aria-label={`Dismiss ${notification.title}`}",
            "maxVisibleNotifications = 4",
        }) RequireContains(infrastructure, required, "Notification infrastructure regression.");

        RequireContains(app, "<NotificationProvider>", "App root must be wrapped with the notification provider.");
        RequireContains(app, "</NotificationProvider>", "App root provider must close around all app modes.");
        RequireContains(safeErrors, "unsafeErrorText", "Notification errors must filter internal details.");
        RequireContains(safeErrors, "candidate.length > 240", "Notification descriptions must stay bounded.");
    }

    private static void AssertLicense(string page)
    {
        RequireContains(page, "notify.info(\"Machine Code copied\"", "Machine Code copy must notify.");
        RequireContains(page, "replacingLicense ? \"License replaced\" : \"License installed\"", "Install and replace success must notify distinctly.");
        RequireContains(page, "notify.error(\"License was rejected\"", "Rejected license result must notify.");
        RequireContains(page, "notify.error(\"License was not installed\"", "License import exception must notify.");
        RequireContains(page, "notify.warning(\"Terms required\"", "Terms gate must have explicit notification feedback.");
        RequireContains(page, "{message ? <Alert tone=\"success\">", "Persistent license success alert must remain.");
    }

    private static void AssertLan(string page)
    {
        foreach (string title in new[]
        {
            "Terminal link copied",
            "LAN access started",
            "LAN access stopped",
            "Terminal POS access allowed",
            "Terminal POS access stopped",
            "Host join code created",
            "Pairing request accepted",
            "Pairing request rejected",
            "Terminal disabled",
            "Terminal enabled",
            "Terminal removed",
            "Operator sessions cleared",
        }) RequireContains(page, title, "LAN action notification missing.");

        foreach (string errorTitle in new[]
        {
            "LAN access was not started",
            "LAN access was not stopped",
            "Terminal POS access was not allowed",
            "Terminal POS access was not stopped",
            "Join code was not created",
            "Pairing request was not accepted",
            "Pairing request was not rejected",
            "Terminal was not disabled",
            "Terminal was not enabled",
            "Terminal was not removed",
            "Operator sessions were not cleared",
        }) RequireContains(page, errorTitle, "LAN failure notification missing.");

        RequireContains(page, "title=\"Stop LAN access?\"", "Stop LAN confirmation must remain.");
        RequireContains(page, "title=\"Stop terminal POS access?\"", "Stop Terminal POS confirmation must remain.");
        RequireContains(page, "title=\"Reject pairing request?\"", "Reject confirmation must remain.");
        RequireContains(page, "safeNotificationDescription", "LAN failures must use safe notification descriptions.");
    }

    private static void AssertBackupRestore(string page)
    {
        foreach (string title in new[]
        {
            "Backup created",
            "Backup verified",
            "Backup verification failed",
            "Restore phrase incorrect",
            "Restore completed",
            "Restore failed",
        }) RequireContains(page, title, "Backup/restore notification missing.");

        RequireContains(page, "restoreText !== RESTORE_CONFIRMATION_INPUT", "Wrong restore phrase must have an explicit action path.");
        RequireContains(page, "{message ? <Alert tone=\"success\">", "Persistent backup success alert must remain.");
        RequireContains(page, "{error ? <Alert tone=\"danger\">", "Persistent backup error alert must remain.");
    }

    private static void AssertProductInventory(string products, string inventory)
    {
        RequireContains(products, "formMode === \"create\" ? \"Product created\" : \"Product updated\"", "Product create/update success must notify.");
        RequireContains(products, "notify.error(\"Product was not saved\"", "Product save failure must notify.");
        RequireContains(products, "notify.warning(\"Product was not saved\"", "Product validation must notify.");
        RequireContains(products, "Use Stock-In to add starting quantity.", "New product notification must guide Stock-In.");

        foreach (string page in new[] { products, inventory })
        {
            RequireContains(page, "\"Stock-In completed\"", "Stock-In success must notify.");
            RequireContains(page, "\"Stock adjustment completed\"", "Stock adjustment success must notify.");
            RequireContains(page, "notify.error(\"Stock movement failed\"", "Stock failure must notify.");
            RequireContains(page, "notify.warning(\"Stock movement was not saved\"", "Stock validation must notify.");
        }
    }

    private static void AssertCheckoutSales(string checkout, string sales)
    {
        RequireContains(checkout, "notify.success(\"Sale completed\", \"Receipt was saved.\")", "Committed sale must notify.");
        RequireContains(checkout, "notify.error(\"Sale failed\"", "Sale failure must notify.");
        RequireContains(checkout, "notify.success(\"Receipt printed\"", "Receipt print success must notify.");
        RequireContains(checkout, "notify.error(\"Print failed\"", "Receipt print failure must notify.");
        RequireContains(checkout, "notify.warning(\"Sale is not ready\"", "Cart/payment validation must notify.");

        RequireContains(sales, "notify.success(\"Receipt reprinted\"", "Reprint success must notify.");
        RequireContains(sales, "notify.error(\"Reprint failed\"", "Reprint failure must notify.");
        RequireContains(sales, "notify.success(\"Sale voided\"", "Void success must notify.");
        RequireContains(sales, "notify.error(\"Void failed\"", "Void failure must notify.");
    }

    private static void AssertBootstrap(string page)
    {
        RequireContains(page, "notify.success(\"First-run setup completed\"", "First-run success must notify.");
        RequireContains(page, "notify.error(\"Setup failed\"", "First-run failure must notify.");
        RequireContains(page, "{error ? <Alert tone=\"danger\">", "Persistent setup validation must remain.");
    }

    private static void AssertAntiRegression(string joined)
    {
        foreach (string forbidden in new[]
        {
            "notify.debug",
            "stack trace",
            "cloud activation is required",
            "BIR-ready",
            "eSales production",
            "ESC/POS supported",
            "cash drawer supported",
        }) RequireNotContains(joined, forbidden, "Forbidden notification/user-facing claim regression.");
    }

    private static string ReadPage(string root, string file) =>
        Read(root, "src", "Salryn.App.Web", "src", "pages", file);

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));

    private static string FindRoot()
    {
        DirectoryInfo? current = new(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln"))) return current.FullName;
            current = current.Parent;
        }
        throw new InvalidOperationException("Could not locate Salryn.sln.");
    }

    private static void RequireContains(string source, string value, string message)
    {
        if (!source.Contains(value, StringComparison.Ordinal))
            throw new InvalidOperationException(message + " Missing: " + value);
    }

    private static void RequireNotContains(string source, string value, string message)
    {
        if (source.Contains(value, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(message + " Forbidden: " + value);
    }
}
