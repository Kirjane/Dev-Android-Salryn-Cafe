using Salryn.Core.Security;

namespace Salryn.App.WinForms.Features.Shell;

public static class ShellModuleKeys
{
    public const string PosCheckout = "POS_CHECKOUT";
    public const string Products = "PRODUCTS";
    public const string Inventory = "INVENTORY";
    public const string SalesHistory = "SALES_HISTORY";
    public const string Reports = "REPORTS";
    public const string Settings = "SETTINGS";
    public const string Users = "USERS";
    public const string Backup = "BACKUP";
    public const string Audit = "AUDIT";
}

public sealed record ShellModule(
    string Key,
    string Title,
    string Description,
    bool IsEnabled,
    string DisabledReason);

public sealed record ShellViewModel(
    string Title,
    string UserLine,
    string StatusLine,
    IReadOnlyList<ShellModule> Modules);

public sealed class ShellPresenter
{
    private readonly CurrentAppSession? _session;

    public ShellPresenter(CurrentAppSession? session)
    {
        _session = session;
    }

    // Kirch: Builds the shell view state from the current user's permission list.
    public ShellViewModel Build()
    {
        if (_session is null)
        {
            return new ShellViewModel(
                "SALRYN",
                "No active user loaded",
                "First-run setup or login is required before modules can be opened.",
                BuildLockedModules("No active user session."));
        }

        string userLine = $"{_session.DisplayName} · {_session.RoleName}";
        string statusLine = $"Session started {_session.StartedAt:yyyy-MM-dd HH:mm} UTC";

        return new ShellViewModel(
            "SALRYN App Shell",
            userLine,
            statusLine,
            BuildModules());
    }

    public bool TryGetModule(string key, out ShellModule module)
    {
        ShellModule? found = Build().Modules.FirstOrDefault(
            item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));

        if (found is null)
        {
            module = default!;
            return false;
        }

        module = found;
        return true;
    }

    private IReadOnlyList<ShellModule> BuildModules()
    {
        return new[]
        {
            Module(
                ShellModuleKeys.PosCheckout,
                "POS Checkout",
                "Cashier-facing product lookup and checkout workspace.",
                Can(SalrynPermissions.SalesCreate),
                "Requires Sales.Create permission."),

            Module(
                ShellModuleKeys.Products,
                "Products",
                "Product catalog, barcode data, prices, units, brands, and categories.",
                Can(SalrynPermissions.ProductsView) || Can(SalrynPermissions.ProductsManage),
                "Requires Products.View or Products.Manage permission."),

            Module(
                ShellModuleKeys.Inventory,
                "Inventory",
                "Stock-in, adjustments, damaged or expired stock, and movement history.",
                Can(SalrynPermissions.InventoryView) || Can(SalrynPermissions.InventoryAdjust),
                "Requires Inventory.View or Inventory.Adjust permission."),

            Module(
                ShellModuleKeys.SalesHistory,
                "Sales History",
                "Receipt lookup, transaction history, and sale review.",
                Can(SalrynPermissions.SalesViewHistory),
                "Requires Sales.ViewHistory permission."),

            Module(
                ShellModuleKeys.Reports,
                "Dashboard",
                "Sales, transactions, products, and low-stock overview.",
                Can(SalrynPermissions.ReportsView),
                "Requires Reports.View permission."),

            Module(
                ShellModuleKeys.Settings,
                "Settings",
                "Store settings, receipt settings, and system configuration.",
                Can(SalrynPermissions.SettingsManage),
                "Requires Settings.Manage permission."),

            Module(
                ShellModuleKeys.Users,
                "Users",
                "User and role administration.",
                Can(SalrynPermissions.UsersManage),
                "Requires Users.Manage permission."),

            Module(
                ShellModuleKeys.Backup,
                "Backup",
                "Local database backup and restore workflow.",
                Can(SalrynPermissions.BackupManage),
                "Requires Backup.Manage permission."),

            Module(
                ShellModuleKeys.Audit,
                "Audit",
                "Audit logs and system activity review.",
                Can(SalrynPermissions.AuditView),
                "Requires Audit.View permission.")
        };
    }

    private static IReadOnlyList<ShellModule> BuildLockedModules(string reason)
    {
        return new[]
        {
            Module(ShellModuleKeys.PosCheckout, "POS Checkout", "Cashier-facing product lookup and checkout workspace.", false, reason),
            Module(ShellModuleKeys.Products, "Products", "Product catalog, barcode data, prices, units, brands, and categories.", false, reason),
            Module(ShellModuleKeys.Inventory, "Inventory", "Stock-in, adjustments, damaged or expired stock, and movement history.", false, reason),
            Module(ShellModuleKeys.SalesHistory, "Sales History", "Receipt lookup, transaction history, and sale review.", false, reason),
            Module(ShellModuleKeys.Reports, "Reports", "Sales, inventory, low-stock, and business summary reports.", false, reason),
            Module(ShellModuleKeys.Settings, "Settings", "Store settings, receipt settings, and system configuration.", false, reason),
            Module(ShellModuleKeys.Users, "Users", "User and role administration.", false, reason),
            Module(ShellModuleKeys.Backup, "Backup", "Local database backup and restore workflow.", false, reason),
            Module(ShellModuleKeys.Audit, "Audit", "Audit logs and system activity review.", false, reason)
        };
    }

    private static ShellModule Module(
        string key,
        string title,
        string description,
        bool isEnabled,
        string disabledReason)
    {
        return new ShellModule(
            key,
            title,
            description,
            isEnabled,
            isEnabled ? string.Empty : disabledReason);
    }

    private bool Can(string permission)
    {
        return _session is not null &&
            _session.IsActive &&
            _session.Can(permission);
    }
}
