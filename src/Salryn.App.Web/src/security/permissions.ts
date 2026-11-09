import type { SessionState } from "../api/session";

export const Permissions = {
  SettingsManage: "Settings.Manage",
  UsersManage: "Users.Manage",
  ProductsView: "Products.View",
  ProductsManage: "Products.Manage",
  InventoryView: "Inventory.View",
  InventoryAdjust: "Inventory.Adjust",
  SalesCreate: "Sales.Create",
  SalesVoid: "Sales.Void",
  SalesViewHistory: "Sales.ViewHistory",
  ReportsView: "Reports.View",
  OfficialOutputRun: "OfficialOutput.Run",
  OfficialDocumentReprint: "OfficialDocument.Reprint",
  OfficialDocumentVoid: "OfficialDocument.Void",
  OfficialDocumentCancel: "OfficialDocument.Cancel",
  OfficialDocumentCorrection: "OfficialDocument.Correction",
  OfficialDocumentRefund: "OfficialDocument.Refund",
  OfficialReadingView: "OfficialReading.View",
  OfficialReadingFinalize: "OfficialReading.Finalize",
  OfficialEvidenceExport: "OfficialEvidence.Export",
  BackupManage: "Backup.Manage",
  AuditView: "Audit.View",
  ImportExportProducts: "ImportExport.Products",
  ShiftOpenClose: "Shift.OpenClose",
  StoreDeploymentManage: "StoreDeployment.Manage",
  StoreTerminalView: "StoreTerminal.View",
  StoreTerminalManage: "StoreTerminal.Manage",
  StoreTerminalPairingIssue: "StoreTerminal.PairingIssue",
  StoreTerminalPairingRevoke: "StoreTerminal.PairingRevoke",
  ReceivablesView: "Receivables.View",
  ReceivablesCreateCreditSale: "Receivables.CreateCreditSale",
  ReceivablesCollect: "Receivables.Collect",
  ReceivablesAdjust: "Receivables.Adjust",
  ProcurementView: "Procurement.View",
  ProcurementRequest: "Procurement.Request",
  ProcurementApproveRequisition: "Procurement.ApproveRequisition",
  ProcurementManagePurchaseOrder: "Procurement.ManagePurchaseOrder",
  ProcurementApprovePurchaseOrder: "Procurement.ApprovePurchaseOrder",
  ReceivingView: "Receiving.View",
  ReceivingManage: "Receiving.Manage",
  ReceivingApproveException: "Receiving.ApproveException",
  InventoryManageReturns: "Inventory.ManageReturns",
  PayablesView: "Payables.View",
  PayablesManageInvoices: "Payables.ManageInvoices",
  PayablesApproveVariance: "Payables.ApproveVariance",
  PayablesRecordPayment: "Payables.RecordPayment",
  PayablesApprovePayment: "Payables.ApprovePayment",
} as const;

export type Permission = (typeof Permissions)[keyof typeof Permissions];

export type HostPage =
  | "dashboard"
  | "checkout"
  | "products"
  | "inventory"
  | "sales"
  | "receivables"
  | "owner-reports"
  | "settings"
  | "license"
  | "backup-restore"
  | "users"
  | "audit"
  | "host-lan-control"
  | "official-setup"
  | "traditional-reporting"
  | "production-reports"
  | "procure-to-pay";

export type AppPage = HostPage;

export const implementedPages: readonly AppPage[] = [
  "dashboard",
  "checkout",
  "products",
  "inventory",
  "sales",
  "receivables",
  "owner-reports",
  "settings",
  "license",
  "backup-restore",
  "users",
  "audit",
  "host-lan-control",
  "official-setup",
  "traditional-reporting",
  "production-reports",
  "procure-to-pay",
] as const;

const RoleIds = {
  Admin: "00000000-0000-0000-0000-000000000001",
  Manager: "00000000-0000-0000-0000-000000000002",
  Cashier: "00000000-0000-0000-0000-000000000003",
} as const;

const authenticatedOnlyPages = new Set<AppPage>(["dashboard"]);

const pagePermissions: Partial<Record<AppPage, readonly Permission[]>> = {
  "host-lan-control": [
    Permissions.StoreTerminalView,
    Permissions.StoreTerminalManage,
    Permissions.StoreDeploymentManage,
  ],
  checkout: [Permissions.SalesCreate],
  products: [Permissions.ProductsView],
  inventory: [Permissions.InventoryView],
  sales: [Permissions.SalesViewHistory],
  receivables: [Permissions.ReceivablesView],
  "owner-reports": [Permissions.ReportsView],
  settings: [Permissions.SettingsManage],
  license: [Permissions.SettingsManage],
  "backup-restore": [Permissions.BackupManage],
  users: [Permissions.UsersManage],
  audit: [Permissions.AuditView],
  // Host-local setup only; Settings.Manage operators (Admin/Owner style).
  "official-setup": [Permissions.SettingsManage],
  // Host-only traditional reporting lab; not in standard manager navigation.
  "traditional-reporting": [Permissions.ReportsView, Permissions.OfficialReadingView, Permissions.OfficialEvidenceExport],
  // Host-only internal artifact export spine; no remote POS client access.
"production-reports": [Permissions.ReportsView, Permissions.OfficialReadingView],
  "procure-to-pay": [
    Permissions.ProcurementView,
    Permissions.ReceivingView,
    Permissions.PayablesView,
  ],
};

export function hasPermission(session: SessionState | null | undefined, permission: Permission): boolean {
  if (!session?.isAuthenticated || !session.user) {
    return false;
  }

  const permissions = session.user.permissions;

  return permissions.includes(permission);
}

export function hasAnyPermission(
  session: SessionState | null | undefined,
  permissions: readonly Permission[],
): boolean {
  return permissions.some((permission) => hasPermission(session, permission));
}

export function canAccessPage(session: SessionState | null | undefined, page: AppPage): boolean {
  if (!session?.isAuthenticated || !session.user) {
    return false;
  }

  const roleId = session.user.roleId.toLowerCase();
  const cashierPages: readonly AppPage[] = ["checkout", "sales"];
  const managerPages: readonly AppPage[] = ["dashboard", "checkout", "products", "inventory", "sales", "receivables", "owner-reports", "audit", "procure-to-pay"];
  if (roleId === RoleIds.Cashier && !cashierPages.includes(page)) return false;
  if (roleId === RoleIds.Manager && !managerPages.includes(page)) return false;

  if (authenticatedOnlyPages.has(page)) return roleId !== RoleIds.Cashier;

  const requiredPermissions = pagePermissions[page];

  if (!requiredPermissions || requiredPermissions.length === 0) {
    return false;
  }

  return hasAnyPermission(session, requiredPermissions);
}

export function getFirstAllowedPage(session: SessionState | null | undefined): AppPage | null {
  return implementedPages.find((page) => canAccessPage(session, page)) ?? null;
}
