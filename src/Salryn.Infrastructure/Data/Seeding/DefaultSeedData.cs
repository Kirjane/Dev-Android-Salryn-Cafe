namespace Salryn.Infrastructure.Data.Seeding;

public static class DefaultSeedData
{
    public const string AdminRoleId = "00000000-0000-0000-0000-000000000001";
    public const string ManagerRoleId = "00000000-0000-0000-0000-000000000002";
    public const string CashierRoleId = "00000000-0000-0000-0000-000000000003";

    public static class Permissions
    {
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
        public const string OfficialOutputRun = "OfficialOutput.Run";
        public const string OfficialDocumentReprint = "OfficialDocument.Reprint";
        public const string OfficialDocumentVoid = "OfficialDocument.Void";
        public const string OfficialDocumentCancel = "OfficialDocument.Cancel";
        public const string OfficialDocumentCorrection = "OfficialDocument.Correction";
        public const string OfficialDocumentRefund = "OfficialDocument.Refund";
        public const string OfficialReadingView = "OfficialReading.View";
        public const string OfficialReadingFinalize = "OfficialReading.Finalize";
        public const string OfficialEvidenceExport = "OfficialEvidence.Export";
        public const string BackupManage = "Backup.Manage";
        public const string AuditView = "Audit.View";
        public const string ImportExportProducts = "ImportExport.Products";
        public const string ShiftOpenClose = "Shift.OpenClose";
        public const string StoreDeploymentManage = "StoreDeployment.Manage";
        public const string StoreTerminalView = "StoreTerminal.View";
        public const string StoreTerminalManage = "StoreTerminal.Manage";
        public const string StoreTerminalPairingIssue = "StoreTerminal.PairingIssue";
        public const string StoreTerminalPairingRevoke = "StoreTerminal.PairingRevoke";
        public const string ReceivablesView = "Receivables.View";
        public const string ReceivablesCreateCreditSale = "Receivables.CreateCreditSale";
        public const string ReceivablesCollect = "Receivables.Collect";
        public const string ReceivablesAdjust = "Receivables.Adjust";
        public const string ReceivablesExport = "Receivables.Export";
        public const string ReceivablesManageCustomers = "Receivables.ManageCustomers";
    }

    public static IReadOnlyList<SeedRole> Roles { get; } = new SeedRole[]
    {
        new(AdminRoleId, "Admin", true),
        new(ManagerRoleId, "Manager", true),
        new(CashierRoleId, "Cashier", true)
    };

    public static IReadOnlyList<string> AdminPermissions { get; } = new string[]
    {
        Permissions.SettingsManage,
        Permissions.UsersManage,
        Permissions.ProductsView,
        Permissions.ProductsManage,
        Permissions.InventoryView,
        Permissions.InventoryAdjust,
        Permissions.SalesCreate,
        Permissions.SalesVoid,
        Permissions.SalesViewHistory,
        Permissions.ReportsView,
        Permissions.OfficialOutputRun,
        Permissions.OfficialDocumentReprint,
        Permissions.OfficialDocumentVoid,
        Permissions.OfficialDocumentCancel,
        Permissions.OfficialDocumentCorrection,
        Permissions.OfficialDocumentRefund,
        Permissions.OfficialReadingView,
        Permissions.OfficialReadingFinalize,
        Permissions.OfficialEvidenceExport,
        Permissions.BackupManage,
        Permissions.AuditView,
        Permissions.ImportExportProducts,
        Permissions.ShiftOpenClose,
        Permissions.StoreDeploymentManage,
        Permissions.StoreTerminalView,
        Permissions.StoreTerminalManage,
        Permissions.StoreTerminalPairingIssue,
        Permissions.StoreTerminalPairingRevoke,
        Permissions.ReceivablesView,
        Permissions.ReceivablesCreateCreditSale,
        Permissions.ReceivablesCollect,
        Permissions.ReceivablesAdjust,
        Permissions.ReceivablesExport,
        Permissions.ReceivablesManageCustomers
    };

    public static IReadOnlyList<string> ManagerPermissions { get; } = new string[]
    {
        Permissions.ProductsView,
        Permissions.ProductsManage,
        Permissions.InventoryView,
        Permissions.InventoryAdjust,
        Permissions.SalesCreate,
        Permissions.SalesVoid,
        Permissions.SalesViewHistory,
        Permissions.ReportsView,
        Permissions.OfficialReadingView,
        Permissions.OfficialEvidenceExport,
        Permissions.ImportExportProducts,
        Permissions.ShiftOpenClose,
        Permissions.StoreTerminalView,
        Permissions.ReceivablesView,
        Permissions.ReceivablesCreateCreditSale,
        Permissions.ReceivablesCollect,
        Permissions.ReceivablesExport,
        Permissions.ReceivablesManageCustomers
    };

    public static IReadOnlyList<string> CashierPermissions { get; } = new string[]
    {
        Permissions.ProductsView,
        Permissions.SalesCreate,
        Permissions.SalesViewHistory,
        Permissions.ShiftOpenClose
    };

    public static IReadOnlyList<SeedPermission> RolePermissions { get; } = BuildRolePermissions();

    public static IReadOnlyList<SeedUnit> Units { get; } = new SeedUnit[]
    {
        new("00000000-0000-0000-0000-000000000101", "Piece", "pc", false),
        new("00000000-0000-0000-0000-000000000102", "Pack", "pack", false),
        new("00000000-0000-0000-0000-000000000103", "Box", "box", false),
        new("00000000-0000-0000-0000-000000000104", "Kilogram", "kg", true),
        new("00000000-0000-0000-0000-000000000105", "Gram", "g", true),
        new("00000000-0000-0000-0000-000000000106", "Liter", "L", true),
        new("00000000-0000-0000-0000-000000000107", "Milliliter", "mL", true),
        new("00000000-0000-0000-0000-000000000108", "Meter", "m", true)
    };

    public static IReadOnlyList<SeedCategory> Categories { get; } = new SeedCategory[]
    {
        new("00000000-0000-0000-0000-000000000201", "Uncategorized"),
        new("00000000-0000-0000-0000-000000000202", "General Merchandise")
    };

    public static IReadOnlyList<SeedBrand> Brands { get; } = new SeedBrand[]
    {
        new("00000000-0000-0000-0000-000000000301", "Generic")
    };

    public static SeedReceiptSequence ReceiptSequence
    {
        get
        {
            return new SeedReceiptSequence(
                "00000000-0000-0000-0000-000000000401",
                "SLRN",
                DateTimeOffset.UtcNow.Year,
                0,
                6);
        }
    }

    public static FirstRunStoreDefaults StoreDefaults { get; } = new(
        CurrencyLabel: "PHP",
        ReceiptWidthMm: 80,
        TaxModeEnabled: false,
        ShiftModeEnabled: true,
        AllowNegativeStock: false);

    private static IReadOnlyList<SeedPermission> BuildRolePermissions()
    {
        var permissions = new List<SeedPermission>();

        AddPermissions(permissions, AdminRoleId, AdminPermissions);
        AddPermissions(permissions, ManagerRoleId, ManagerPermissions);
        AddPermissions(permissions, CashierRoleId, CashierPermissions);

        return permissions;
    }

    private static void AddPermissions(
        List<SeedPermission> list,
        string roleId,
        IEnumerable<string> permissions)
    {
        foreach (var permission in permissions)
        {
            list.Add(new SeedPermission(roleId, permission));
        }
    }
}
