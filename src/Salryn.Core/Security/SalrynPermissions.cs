namespace Salryn.Core.Security;

public static class SalrynPermissions
{
    // Kirch: Defines stable permission keys used by routing, services, and seeded role permissions.
    public const string SettingsManage = "Settings.Manage";
    public const string UsersManage = "Users.Manage";
    public const string ProductsView = "Products.View";
    public const string ProductsManage = "Products.Manage";
    public const string InventoryView = "Inventory.View";
    public const string InventoryAdjust = "Inventory.Adjust";
    public const string SalesCreate = "Sales.Create";
    public const string SalesVoid = "Sales.Void";
    public const string SalesViewHistory = "Sales.ViewHistory";
    public const string ReportsView = "Reports.View";
    public const string BackupManage = "Backup.Manage";
    public const string AuditView = "Audit.View";
    public const string ImportExportProducts = "ImportExport.Products";
    public const string ShiftOpenClose = "Shift.OpenClose";
}
