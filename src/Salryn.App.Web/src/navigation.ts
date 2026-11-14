import type { SessionState } from "./api/session";
import { canAccessPage, type AppPage } from "./security/permissions";

export type { AppPage } from "./security/permissions";

export type NavDomain =
  | "Counter & Sales"
  | "Finance & Analytics"
  | "Catalog & Stock"
  | "Store & System"
  | "Internal Diagnostics";

export type EnabledNavItem = {
  id: AppPage;
  label: string;
  enabled: true;
  group: "Cashier" | "Manager" | "Admin" | "Advanced";
  domain: NavDomain;
};

export type NavItemConfig = EnabledNavItem;

export const navItems: NavItemConfig[] = [
  { id: "checkout", label: "POS Checkout", enabled: true, group: "Cashier", domain: "Counter & Sales" },
  { id: "price-canvass", label: "Price Canvass / Quote", enabled: true, group: "Cashier", domain: "Counter & Sales" },
  { id: "sales", label: "Sales & Receipts", enabled: true, group: "Cashier", domain: "Counter & Sales" },
  { id: "purchase-requests", label: "Purchase Requests (PR)", enabled: true, group: "Cashier", domain: "Catalog & Stock" },
  { id: "customer-orders", label: "Customer Orders", enabled: true, group: "Manager", domain: "Counter & Sales" },
  { id: "receivables", label: "Accounts Receivable", enabled: true, group: "Manager", domain: "Finance & Analytics" },
  { id: "accounts-payable", label: "Accounts Payable", enabled: true, group: "Manager", domain: "Finance & Analytics" },
  { id: "dashboard", label: "Dashboard", enabled: true, group: "Manager", domain: "Finance & Analytics" },
  { id: "owner-reports", label: "Reports", enabled: true, group: "Manager", domain: "Finance & Analytics" },
  { id: "products", label: "Product Catalog", enabled: true, group: "Manager", domain: "Catalog & Stock" },
  { id: "inventory", label: "Stock Movements", enabled: true, group: "Manager", domain: "Catalog & Stock" },
  { id: "procure-to-pay", label: "Procurement & Logistics", enabled: true, group: "Manager", domain: "Catalog & Stock" },
  { id: "users", label: "Users", enabled: true, group: "Admin", domain: "Store & System" },
  { id: "audit", label: "Audit", enabled: true, group: "Admin", domain: "Store & System" },
  { id: "settings", label: "Store Settings", enabled: true, group: "Admin", domain: "Store & System" },
  { id: "license", label: "License", enabled: true, group: "Admin", domain: "Store & System" },
  { id: "backup-restore", label: "Backup & Restore", enabled: true, group: "Admin", domain: "Store & System" },
  // { id: "host-lan-control", label: "LAN Terminals · Future Feature", enabled: true, group: "Admin" }
  { id: "host-lan-control", label: "LAN Terminals · Future Feature", enabled: true, group: "Admin", domain: "Store & System" },
];

const internalNavItems: NavItemConfig[] = [
  { id: "production-reports", label: "Report Artifacts", enabled: true, group: "Advanced", domain: "Internal Diagnostics" },
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

export function getPageLocationMeta(page: AppPage): { label: string; domain: NavDomain } {
  const found = [...navItems, ...internalNavItems].find((item) => item.id === page);
  if (found) {
    return { label: found.label, domain: found.domain };
  }
  return { label: "Internal Diagnostic Surface", domain: "Internal Diagnostics" };
}
