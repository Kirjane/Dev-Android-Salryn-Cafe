namespace Salryn.Tests;

/// <summary>
/// PRE-I structural proof for shell/sidebar and POS layout usability. This gate is frontend-only;
/// it does not claim browser visual QA or alter checkout business behavior.
/// </summary>
public static class PreIShellSidebarCollapsePosUsabilityChecks
{
    public static void Run()
    {
        string root = FindRoot();
        string shell = Read(root, "src", "Salryn.App.Web", "src", "components", "AppShell.tsx");
        string commandBar = Read(root, "src", "Salryn.App.Web", "src", "components", "ui", "CommandBar.tsx");
        string dialog = Read(root, "src", "Salryn.App.Web", "src", "components", "ui", "Dialog.tsx");
        string dataTable = Read(root, "src", "Salryn.App.Web", "src", "components", "ui", "DataTable.tsx");
        string css = Read(root, "src", "Salryn.App.Web", "src", "index.css");
        string checkout = Read(root, "src", "Salryn.App.Web", "src", "pages", "CheckoutPage.tsx");
        string products = Read(root, "src", "Salryn.App.Web", "src", "pages", "ProductsPage.tsx");
        string audit = Read(root, "src", "Salryn.App.Web", "src", "pages", "AuditPage.tsx");
        string inventory = Read(root, "src", "Salryn.App.Web", "src", "pages", "InventoryPage.tsx");
        string sales = Read(root, "src", "Salryn.App.Web", "src", "pages", "SalesHistoryPage.tsx");
        string coreSmoke = Read(root, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(shell, "sidebarCollapsed", "Sidebar state.");
        RequireContains(shell, "Collapse sidebar", "Collapse control label.");
        RequireContains(shell, "Expand sidebar", "Expand control label.");
        RequireContains(shell, "aria-expanded={!sidebarCollapsed}", "Accessible sidebar state.");
        RequireContains(shell, "sidebarStorageKey", "Sidebar preference persistence.");
        RequireContains(shell, "sidebarCollapsed ? \"w-16\" : \"w-60\"", "Collapsed and expanded widths.");
        RequireContains(shell, "overflow-x-hidden overflow-y-auto", "Workspace horizontal overflow boundary.");
        RequireContains(shell, "min-w-0 flex-1", "Shell shrink boundary.");

        RequireContains(commandBar, "compact?: boolean", "Compact command bar option.");
        RequireContains(checkout, "<CommandBar compact title=\"POS Checkout\"", "Compact Checkout header.");
        RequireContains(checkout, "Scan, sell, and take payment.", "Compact Checkout subtitle.");
        RequireContains(checkout, "setBackendMessage(null); notify.success(\"Shift opened\"", "Normal shift-open success uses notification without a page warning.");
        RequireNotContains(checkout, "setBackendMessage(result.message); notify.success(\"Shift opened\"", "Shift-open success must not render as a warning Alert.");
        RequireContains(checkout, "xl:grid-cols-[minmax(0,0.85fr)_minmax(0,0.95fr)_minmax(21rem,1.2fr)]", "Rebalanced Checkout columns.");
        RequireContains(checkout, "min-w-0 flex-col border-slate-300", "Payment panel can shrink without page overflow.");
        RequireNotContains(checkout, "minmax(18rem,1fr)_minmax(22rem,1.05fr)_24rem", "Old fixed Checkout columns removed.");

        RequireContains(products, "flex flex-wrap gap-3", "Product filters wrap.");
        RequireContains(products, "CardBody className=\"text-sm text-slate-600\"", "Selected product uses natural page flow.");
        RequireNotContains(products, "h-[calc(100vh-9rem)]", "Product page is not trapped to viewport height.");
        RequireOrder(products, "Selected product", "More details", "Selected product primary content precedes secondary details.");

        RequireContains(audit, "flex flex-wrap gap-3", "Audit filters wrap.");
        RequireContains(inventory, "flex flex-wrap gap-3", "Inventory filters wrap.");
        RequireContains(sales, "flex shrink-0 flex-wrap gap-3", "Sales filters wrap.");
        RequireContains(css, "overflow-x: hidden", "Root horizontal overflow guard.");
        RequireContains(dialog, "overflow-x-hidden overflow-y-auto", "Dialog overflow is contained.");
        RequireContains(dialog, "flex flex-wrap justify-end", "Dialog actions wrap.");
        RequireContains(dataTable, "overflow-x-auto", "Wide tables scroll locally instead of widening the app shell.");

        RequireContains(coreSmoke, "PreIShellSidebarCollapsePosUsabilityChecks.Run();", "Full CoreSmoke registration.");
        RequireContains(coreSmoke, "pre-i-shell-sidebar-collapse-pos-usability", "Focused CoreSmoke registration.");
    }

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
        if (!source.Contains(value, StringComparison.Ordinal)) throw new InvalidOperationException(message + " Missing: " + value);
    }

    private static void RequireNotContains(string source, string value, string message)
    {
        if (source.Contains(value, StringComparison.Ordinal)) throw new InvalidOperationException(message + " Forbidden: " + value);
    }

    private static void RequireOrder(string source, string first, string second, string message)
    {
        int firstIndex = source.IndexOf(first, StringComparison.Ordinal);
        int secondIndex = source.IndexOf(second, StringComparison.Ordinal);
        if (firstIndex < 0 || secondIndex <= firstIndex) throw new InvalidOperationException(message);
    }
}
