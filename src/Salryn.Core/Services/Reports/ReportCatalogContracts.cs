namespace Salryn.Core.Services.Reports;

public enum ProductionReportPriority
{
    P0 = 0,
    P1 = 1,
    P2 = 2
}

public enum ProductionReportFamily
{
    StatutoryOfficialOutput = 1,
    OperationalManagement = 2,
    AuditEvidence = 3,
    InternalDiagnostic = 4
}

public static class ProductionReportKinds
{
    public const string TraditionalXReading = "traditional-x-reading";
    public const string TraditionalZReading = "traditional-z-reading";
    public const string EjExport = "ej-export";
    public const string InternalESalesDraft = "internal-esales-draft";
    public const string OfficialInvoicePresentation = "official-invoice-presentation";
    public const string ReprintAdjustmentPresentation = "reprint-adjustment-presentation";
    public const string SalesRefundVoidSummary = "sales-refund-void-summary";
    public const string TaxBucketSummary = "tax-bucket-summary";
    public const string CashierTenderSummary = "cashier-tender-summary";
    public const string AuditOperatorActivity = "audit-operator-activity";
    public const string SalesByProductCategory = "sales-by-product-category";
    public const string InventoryOnHand = "inventory-on-hand";
    public const string StockMovement = "stock-movement";
    public const string LowStockReorder = "low-stock-reorder";
    public const string BackupRestoreEvidence = "backup-restore-evidence";
}

public static class ProductionReportPermissions
{
    public const string ReportsView = "Reports.View";
    public const string OfficialReadingView = "OfficialReading.View";
    public const string OfficialEvidenceExport = "OfficialEvidence.Export";
    public const string InventoryView = "Inventory.View";
    public const string AuditView = "Audit.View";
}

public sealed record ProductionReportCatalogItem(
    string Kind,
    string Title,
    ProductionReportPriority Priority,
    ProductionReportFamily Family,
    string RequiredPermission,
    bool DatasetAvailable,
    bool UsesCanonicalM5Projection,
    bool RendererRequired,
    string? UnavailableReason,
    IReadOnlyList<string> RequiredFilters);

public static class PreCrystalReportCatalog
{
    private static readonly ProductionReportCatalogItem[] Items =
    {
        new(
            ProductionReportKinds.TraditionalXReading,
            "Traditional X Reading",
            ProductionReportPriority.P0,
            ProductionReportFamily.StatutoryOfficialOutput,
            ProductionReportPermissions.OfficialReadingView,
            DatasetAvailable: true,
            UsesCanonicalM5Projection: true,
            RendererRequired: true,
            UnavailableReason: null,
            RequiredFilters: new[] { "rangeStart", "rangeEnd", "documentKind" }),
        new(
            ProductionReportKinds.TraditionalZReading,
            "Traditional Z Reading Dataset",
            ProductionReportPriority.P0,
            ProductionReportFamily.StatutoryOfficialOutput,
            ProductionReportPermissions.OfficialReadingView,
            DatasetAvailable: true,
            UsesCanonicalM5Projection: true,
            RendererRequired: true,
            UnavailableReason: null,
            RequiredFilters: new[] { "rangeStart", "rangeEnd", "documentKind" }),
        new(
            ProductionReportKinds.EjExport,
            "EJ Examiner Export Dataset",
            ProductionReportPriority.P0,
            ProductionReportFamily.AuditEvidence,
            ProductionReportPermissions.OfficialEvidenceExport,
            DatasetAvailable: true,
            UsesCanonicalM5Projection: true,
            RendererRequired: true,
            UnavailableReason: null,
            RequiredFilters: new[] { "rangeStart", "rangeEnd", "documentKind" }),
        new(
            ProductionReportKinds.InternalESalesDraft,
            "Internal eSales Draft Dataset",
            ProductionReportPriority.P0,
            ProductionReportFamily.StatutoryOfficialOutput,
            ProductionReportPermissions.OfficialEvidenceExport,
            DatasetAvailable: true,
            UsesCanonicalM5Projection: true,
            RendererRequired: true,
            UnavailableReason: "BIR authoritative eSales schema remains unconfirmed; transmission remains disabled.",
            RequiredFilters: new[] { "rangeStart", "rangeEnd", "documentKind" }),
        new(
            ProductionReportKinds.OfficialInvoicePresentation,
            "Official Invoice Presentation Dataset",
            ProductionReportPriority.P0,
            ProductionReportFamily.StatutoryOfficialOutput,
            ProductionReportPermissions.OfficialReadingView,
            DatasetAvailable: false,
            UsesCanonicalM5Projection: false,
            RendererRequired: true,
            UnavailableReason: "Official invoice presentation source boundary exists outside this M0 dataset spine and requires a separate safe template binding pass.",
            RequiredFilters: new[] { "documentNumber" }),
        new(
            ProductionReportKinds.ReprintAdjustmentPresentation,
            "Reprint / Adjustment Presentation Dataset",
            ProductionReportPriority.P0,
            ProductionReportFamily.StatutoryOfficialOutput,
            ProductionReportPermissions.OfficialReadingView,
            DatasetAvailable: false,
            UsesCanonicalM5Projection: false,
            RendererRequired: true,
            UnavailableReason: "Lifecycle presentation binding is intentionally unavailable until the source DTO boundary is locked.",
            RequiredFilters: new[] { "documentNumber" }),
        new(
            ProductionReportKinds.SalesRefundVoidSummary,
            "Sales / Refund / Void Summary",
            ProductionReportPriority.P1,
            ProductionReportFamily.OperationalManagement,
            ProductionReportPermissions.ReportsView,
            DatasetAvailable: true,
            UsesCanonicalM5Projection: true,
            RendererRequired: true,
            UnavailableReason: null,
            RequiredFilters: new[] { "rangeStart", "rangeEnd", "documentKind" }),
        new(
            ProductionReportKinds.TaxBucketSummary,
            "VAT / Non-VAT Tax Bucket Summary",
            ProductionReportPriority.P1,
            ProductionReportFamily.OperationalManagement,
            ProductionReportPermissions.ReportsView,
            DatasetAvailable: true,
            UsesCanonicalM5Projection: true,
            RendererRequired: true,
            UnavailableReason: null,
            RequiredFilters: new[] { "rangeStart", "rangeEnd", "documentKind" }),
        new(
            ProductionReportKinds.CashierTenderSummary,
            "Cashier / Tender Summary",
            ProductionReportPriority.P1,
            ProductionReportFamily.OperationalManagement,
            ProductionReportPermissions.ReportsView,
            DatasetAvailable: false,
            UsesCanonicalM5Projection: false,
            RendererRequired: true,
            UnavailableReason: "No safe cashier/tender projection is locked for PRE-CRYSTAL M0; direct database report queries are forbidden.",
            RequiredFilters: new[] { "rangeStart", "rangeEnd" }),
        new(
            ProductionReportKinds.AuditOperatorActivity,
            "Audit / Operator Activity",
            ProductionReportPriority.P1,
            ProductionReportFamily.AuditEvidence,
            ProductionReportPermissions.AuditView,
            DatasetAvailable: false,
            UsesCanonicalM5Projection: false,
            RendererRequired: true,
            UnavailableReason: "Audit report projection boundary is not locked for PRE-CRYSTAL M0.",
            RequiredFilters: new[] { "rangeStart", "rangeEnd" }),
        new(
            ProductionReportKinds.SalesByProductCategory,
            "Sales by Product / Category",
            ProductionReportPriority.P2,
            ProductionReportFamily.OperationalManagement,
            ProductionReportPermissions.ReportsView,
            DatasetAvailable: false,
            UsesCanonicalM5Projection: false,
            RendererRequired: true,
            UnavailableReason: "Needs a safe sales analytics projection; no direct Crystal SQL is allowed.",
            RequiredFilters: new[] { "rangeStart", "rangeEnd" }),
        new(
            ProductionReportKinds.InventoryOnHand,
            "Inventory on Hand",
            ProductionReportPriority.P2,
            ProductionReportFamily.OperationalManagement,
            ProductionReportPermissions.InventoryView,
            DatasetAvailable: false,
            UsesCanonicalM5Projection: false,
            RendererRequired: true,
            UnavailableReason: "Inventory report source boundary is not safe for PRE-CRYSTAL M0.",
            RequiredFilters: Array.Empty<string>()),
        new(
            ProductionReportKinds.StockMovement,
            "Stock Movement",
            ProductionReportPriority.P2,
            ProductionReportFamily.OperationalManagement,
            ProductionReportPermissions.InventoryView,
            DatasetAvailable: false,
            UsesCanonicalM5Projection: false,
            RendererRequired: true,
            UnavailableReason: "Stock ledger report source boundary is not safe for PRE-CRYSTAL M0.",
            RequiredFilters: new[] { "rangeStart", "rangeEnd" }),
        new(
            ProductionReportKinds.LowStockReorder,
            "Low Stock / Reorder",
            ProductionReportPriority.P2,
            ProductionReportFamily.OperationalManagement,
            ProductionReportPermissions.InventoryView,
            DatasetAvailable: false,
            UsesCanonicalM5Projection: false,
            RendererRequired: true,
            UnavailableReason: "Reorder report source boundary is not safe for PRE-CRYSTAL M0.",
            RequiredFilters: Array.Empty<string>()),
        new(
            ProductionReportKinds.BackupRestoreEvidence,
            "Backup / Restore Evidence",
            ProductionReportPriority.P2,
            ProductionReportFamily.AuditEvidence,
            ProductionReportPermissions.AuditView,
            DatasetAvailable: false,
            UsesCanonicalM5Projection: false,
            RendererRequired: true,
            UnavailableReason: "Backup evidence report projection is not locked for PRE-CRYSTAL M0.",
            RequiredFilters: new[] { "rangeStart", "rangeEnd" })
    };

    public static IReadOnlyList<ProductionReportCatalogItem> All => Items;

    public static bool IsKnown(string? kind) => TryGet(kind, out _);

    public static bool TryGet(string? kind, out ProductionReportCatalogItem item)
    {
        item = default!;
        if (string.IsNullOrWhiteSpace(kind))
        {
            return false;
        }

        ProductionReportCatalogItem? found = Items.FirstOrDefault(x =>
            string.Equals(x.Kind, kind.Trim(), StringComparison.OrdinalIgnoreCase));
        if (found is null)
        {
            return false;
        }

        item = found;
        return true;
    }
}
