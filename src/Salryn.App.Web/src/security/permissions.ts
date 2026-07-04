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
} as const;

export type Permission = (typeof Permissions)[keyof typeof Permissions];

export type AppPage =
  | "dashboard"
  | "products"
  | "inventory"
  | "sales"
  | "users"
  | "audit"
  | "remote-readonly"
  | "remote-pos";

export const implementedPages: readonly AppPage[] = [
  "dashboard",
  "remote-readonly",
  "remote-pos",
  "products",
  "inventory",
  "sales",
  "users",
  "audit",
] as const;

const authenticatedOnlyPages = new Set<AppPage>(["dashboard", "remote-readonly", "remote-pos"]);

const pagePermissions: Partial<Record<AppPage, readonly Permission[]>> = {
  products: [Permissions.ProductsView],
  inventory: [Permissions.InventoryView],
  sales: [Permissions.SalesViewHistory],
  users: [Permissions.UsersManage],
  audit: [Permissions.AuditView],
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

  if (authenticatedOnlyPages.has(page)) {
    return true;
  }

  const requiredPermissions = pagePermissions[page];

  if (!requiredPermissions || requiredPermissions.length === 0) {
    return false;
  }

  return hasAnyPermission(session, requiredPermissions);
}

export function getFirstAllowedPage(session: SessionState | null | undefined): AppPage | null {
  return implementedPages.find((page) => canAccessPage(session, page)) ?? null;
}
