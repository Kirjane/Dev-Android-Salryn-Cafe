import type { SessionState } from "./api/session";
import { canAccessPage, type AppPage } from "./security/permissions";

export type { AppPage } from "./security/permissions";

export type EnabledNavItem = {
  id: AppPage;
  label: string;
  enabled: true;
  group: "Cashier" | "Manager" | "Admin" | "Advanced";
};

export type NavItemConfig = EnabledNavItem;

export const navItems: NavItemConfig[] = [
  { id: "checkout", label: "POS Checkout", enabled: true, group: "Cashier" },
  { id: "sales", label: "Sales & Receipts", enabled: true, group: "Cashier" },
  { id: "receivables", label: "Utang Ledger", enabled: true, group: "Manager" },
  { id: "dashboard", label: "Dashboard", enabled: true, group: "Manager" },
  { id: "owner-reports", label: "Reports", enabled: true, group: "Manager" },
  { id: "products", label: "Product Catalog", enabled: true, group: "Manager" },
  { id: "inventory", label: "Stock Movements", enabled: true, group: "Manager" },
  { id: "users", label: "Users", enabled: true, group: "Admin" },
  { id: "audit", label: "Audit", enabled: true, group: "Admin" },
  { id: "settings", label: "Store Settings", enabled: true, group: "Admin" },
  { id: "license", label: "License", enabled: true, group: "Admin" },
  { id: "backup-restore", label: "Backup & Restore", enabled: true, group: "Admin" },
  { id: "host-lan-control", label: "LAN Terminals · Future Feature", enabled: true, group: "Admin" },
];

const internalNavItems: NavItemConfig[] = [
  { id: "production-reports", label: "Report Artifacts", enabled: true, group: "Advanced" },
];

export function isInternalSurfaceModeEnabled(): boolean {
  return import.meta.env.VITE_SALRYN_INTERNAL_SURFACES === "true";
}

export function getNavItems(session: SessionState, enabledFeatures: readonly string[] = []): NavItemConfig[] {
  const configuredItems = isInternalSurfaceModeEnabled()
    ? [...navItems, ...internalNavItems]
    : navItems;
  return configuredItems.filter((item) =>
    canAccessPage(session, item.id) &&
    (item.id !== "receivables" || enabledFeatures.includes("ReceivablesUtangLedger")));
}
