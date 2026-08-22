using Salryn.Infrastructure.Data.Seeding;

namespace Salryn.Tests;

public static class PosSurface1RolePageLockStoreNavChecks
{
    public static void Run()
    {
        DirectoryInfo root = FindRoot();
        string web = Path.Combine(root.FullName, "src", "Salryn.App.Web", "src");
        string navigation = File.ReadAllText(Path.Combine(web, "navigation.ts"));
        string permissions = File.ReadAllText(Path.Combine(web, "security", "permissions.ts"));
        string app = File.ReadAllText(Path.Combine(web, "App.tsx"));

        Require(!navigation.Contains("Official Output Text File", StringComparison.Ordinal), "Unsupported official output must not appear in store navigation.");
        Require(!navigation.Contains("no approved", StringComparison.OrdinalIgnoreCase), "Developer contract copy must not appear in navigation.");
        Require(!navigation.Contains("label: \"Settings\"", StringComparison.Ordinal), "Unimplemented Settings must not appear in navigation.");
        Require(!navigation.Contains("label: \"Backup\"", StringComparison.Ordinal), "Unimplemented Backup must not appear in navigation.");
        Require(navigation.Contains("group: \"Cashier\"", StringComparison.Ordinal), "Cashier navigation group is required.");
        Require(navigation.Contains("group: \"Manager\"", StringComparison.Ordinal), "Manager navigation group is required.");
        Require(navigation.Contains("group: \"Admin\"", StringComparison.Ordinal), "Admin navigation group is required.");
        Require(navigation.Contains("group: \"Advanced\"", StringComparison.Ordinal), "Advanced navigation group is required.");

        Require(permissions.Contains("const cashierPages: readonly AppPage[] = [\"checkout\", \"sales\"]", StringComparison.Ordinal), "Cashier surface must be limited to Checkout and Sales History.");
        Require(permissions.Contains("roleId === RoleIds.Cashier", StringComparison.Ordinal), "Cashier page policy must use stable role id.");
        Require(permissions.Contains("roleId === RoleIds.Manager", StringComparison.Ordinal), "Manager page policy must use stable role id.");
        Require(permissions.Contains("return hasAnyPermission", StringComparison.Ordinal), "Role surface policy must retain permission checks.");
        Require(app.Contains("canAccessPage(session, currentPage)", StringComparison.Ordinal), "Direct page rendering must remain centrally guarded.");
        Require(app.Contains("AccessDenied", StringComparison.Ordinal), "Blocked direct navigation must render Access Denied.");

        Require(DefaultSeedData.AdminPermissions.Contains(DefaultSeedData.Permissions.UsersManage), "Admin must retain Users.Manage.");
        Require(DefaultSeedData.AdminPermissions.Contains(DefaultSeedData.Permissions.StoreTerminalManage), "Admin must retain terminal administration.");
        Require(DefaultSeedData.ManagerPermissions.Contains(DefaultSeedData.Permissions.SalesVoid), "Manager must retain Sales.Void.");
        Require(!DefaultSeedData.ManagerPermissions.Contains(DefaultSeedData.Permissions.UsersManage), "Manager must not manage users.");
        Require(DefaultSeedData.CashierPermissions.Contains(DefaultSeedData.Permissions.SalesCreate), "Cashier must retain checkout permission.");
        Require(DefaultSeedData.CashierPermissions.Contains(DefaultSeedData.Permissions.SalesViewHistory), "Cashier must retain Sales History permission.");
        Require(!DefaultSeedData.CashierPermissions.Contains(DefaultSeedData.Permissions.SalesVoid), "Cashier must not void sales.");
        Require(!DefaultSeedData.CashierPermissions.Contains(DefaultSeedData.Permissions.InventoryAdjust), "Cashier must not write stock.");
    }

    private static DirectoryInfo FindRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Salryn.sln"))) current = current.Parent;
        return current ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
