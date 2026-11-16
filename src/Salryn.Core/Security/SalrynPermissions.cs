namespace Salryn.Core.Security;

public static class SalrynPermissions
{
    // Kirch: Defines stable permission keys used by routing, services, and seeded role permissions.
    public const string SettingsManage = "Settings.Manage";
    public const string UsersManage = "Users.Manage";
    public const string ProductsView = "Products.View";
    public const string ProductsManage = "Products.Manage";
    public const string ProductsFastAdd = "Products.FastAdd";
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
    public const string ProcurementView = "Procurement.View";
    public const string ProcurementRequest = "Procurement.Request";
    public const string ProcurementApproveRequisition = "Procurement.ApproveRequisition";
    public const string ProcurementManagePurchaseOrder = "Procurement.ManagePurchaseOrder";
    public const string ProcurementApprovePurchaseOrder = "Procurement.ApprovePurchaseOrder";
    public const string ReceivingView = "Receiving.View";
    public const string ReceivingManage = "Receiving.Manage";
    public const string ReceivingApproveException = "Receiving.ApproveException";
    public const string InventoryManageReturns = "Inventory.ManageReturns";
    public const string PayablesView = "Payables.View";
    public const string PayablesManageInvoices = "Payables.ManageInvoices";
    public const string PayablesApproveVariance = "Payables.ApproveVariance";
    public const string PayablesRecordPayment = "Payables.RecordPayment";
    public const string PayablesApprovePayment = "Payables.ApprovePayment";
}
