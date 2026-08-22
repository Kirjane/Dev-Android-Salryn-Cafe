namespace Salryn.Tests;

public static class PosManual1FrontendRepairChecks
{
    public static void Run()
    {
        DirectoryInfo root = FindRoot();
        string web = Path.Combine(root.FullName, "src", "Salryn.App.Web", "src");
        string navigation = Read(web, "navigation.ts");
        string app = Read(web, "App.tsx");
        string checkout = Read(web, "pages", "CheckoutPage.tsx");
        string sales = Read(web, "pages", "SalesHistoryPage.tsx");
        string settings = Read(web, "pages", "StoreSettingsPage.tsx");
        string products = Read(web, "pages", "ProductsPage.tsx");
        string inventory = Read(web, "pages", "InventoryPage.tsx");
        string dashboard = Read(web, "pages", "DashboardPage.tsx");
        string reports = Read(web, "pages", "OwnerReportsPage.tsx");
        string users = Read(web, "pages", "UsersPage.tsx");

        RequireContains(navigation, "Sales & Receipts", "Navigation must make saved receipts discoverable.");
        RequireContains(navigation, "label: \"Reports\"", "Navigation must expose Reports.");
        RequireNotContains(navigation, "label: \"Owner Reports\"", "Navigation must not keep the old Owner Reports label.");
        RequireContains(checkout, "Saved under Sales & Receipts", "Completed checkout must keep Sales & Receipts discoverable without route-away button.");
        RequireNotContains(checkout, "View Sales & Receipts", "Checkout completion must not offer the route-away button that resets completion state.");
        RequireNotContains(app, "onOpenSalesHistory", "Checkout no longer receives a post-sale route-away action.");
        RequireContains(checkout, "Print Receipt", "Existing receipt print action must remain separate.");
        RequireContains(checkout, "New Sale (F5)", "New Sale must remain a distinct action.");

        RequireContains(sales, "Sale & Receipt Details", "Sales list must expose clear receipt detail copy.");
        RequireContains(sales, "Receipt Snapshot Preview", "Sales detail must expose a receipt-form snapshot preview.");
        RequireContains(sales, "saved snapshot only", "Reprint must explain its persisted source.");
        RequireContains(sales, "voidConfirmed", "Void flow must require explicit confirmation in addition to a reason.");
        RequireContains(sales, "Confirm Void Sale", "Authorized void action must be discoverable and explicit.");
        RequireContains(sales, "cannot be repeated", "Double-void boundary must be visible to the operator.");

        RequireContains(settings, "title=\"Store Settings\"", "Settings must present a clear store-configuration purpose.");
        RequireContains(settings, "Manage store details, receipts, and printing.", "Settings must explain its client-facing purpose.");
        RequireContains(settings, "Store profile", "Settings must group store identity.");
        RequireContains(settings, "Regional / Currency", "Settings must group currency behavior.");
        RequireContains(settings, "Receipt appearance", "Settings must group receipt behavior.");
        RequireContains(settings, "Printer mode", "Settings must expose the configured printer mode.");
        RequireContains(settings, "Exact Windows printer name", "Printer setup must request the exact Windows printer name.");
        RequireContains(settings, "Saved receipts remain available when printing is disabled", "Printer copy must keep receipt availability clear.");
        // PRE-BETA-C: store logo upload is now supported (PNG/JPEG/WebP, Settings.Manage).
        RequireContains(settings, "Upload logo", "Settings must expose store logo upload.");
        RequireContains(settings, "Remove logo", "Settings must expose store logo remove.");
        RequireContains(settings, "type=\"file\"", "Settings logo upload must use a file input.");

        RequireContains(products, "Unit of measure", "Catalog must distinguish unit of measure from quantity.");
        RequireContains(products, "Product details do not change stock", "Catalog edit must stay stock-neutral.");
        RequireContains(products, "Confirm product deactivation", "Product deactivation must have a confirmation guard.");
        RequireContains(products, "Confirm Deactivate Product", "Product deactivation must remain an explicit action.");
        RequireContains(products, "delete past records", "Deactivate copy must explain historical records are preserved.");
        RequireContains(inventory, "Stock on hand", "Inventory must label quantity clearly.");
        RequireContains(inventory, "Recorded stock changes", "Inventory movement semantics must be clear.");
        RequireContains(inventory, "Quantity changes are recorded as stock movements", "Inventory must explain how stock changes are recorded.");

        RequireContains(dashboard, "Store operations", "Dashboard must prioritize daily store workflows.");
        RequireContains(dashboard, "Open Sales & Receipts", "Dashboard must link to sales and receipts.");
        RequireContains(dashboard, "Owner home summary", "Dashboard must act as owner home summary.");
        RequireContains(dashboard, "Open Reports", "Dashboard must link to Reports.");
        RequireContains(reports, "title=\"Reports\"", "Report page must use the Reports label.");
        RequireContains(reports, "max-h-80 overflow-y-auto", "Large detailed-report tables must have bounded scrolling.");
        RequireContains(users, "Role access guide", "Users page must explain manual role expectations.");
        RequireContains(users, "An Auditor role is not available", "Missing Auditor role must not be implied.");

        string operatorSource = string.Join("\n", checkout, sales, settings, products, inventory, dashboard, reports, users);
        foreach (string token in new[] { "POS-M2", "POS-M4", "POS-M5", "POS-M6", "runtime proof" })
        {
            RequireNotContains(operatorSource, token, "Operator pages must not expose engineering milestone copy: " + token);
        }

        RequireNotContains(operatorSource, "BIR-approved", "Repair must not introduce a BIR approval claim.");
        RequireNotContains(operatorSource, "eSales ready", "Repair must not introduce an eSales readiness claim.");
        RequireNotContains(operatorSource, "hardware update later", "Repair must not introduce hardware-roadmap drift.");
        RequireNotContains(operatorSource, "next hardware update", "Repair must not introduce hardware-roadmap drift.");
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));

    private static DirectoryInfo FindRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Salryn.sln"))) current = current.Parent;
        return current ?? throw new InvalidOperationException("Repository root not found.");
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
