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
  { id: "sales", label: "Sales History", enabled: true, group: "Cashier" },
  { id: "dashboard", label: "Dashboard", enabled: true, group: "Manager" },
  { id: "products", label: "Product Catalog", enabled: true, group: "Manager" },
  { id: "inventory", label: "Stock Movements", enabled: true, group: "Manager" },
  { id: "owner-reports", label: "Owner Reports", enabled: true, group: "Manager" },
  { id: "users", label: "Users", enabled: true, group: "Admin" },
  { id: "audit", label: "Audit", enabled: true, group: "Admin" },
  { id: "host-lan-control", label: "Host LAN Control", enabled: true, group: "Advanced" },
  { id: "official-setup", label: "Official Setup", enabled: true, group: "Advanced" },
  { id: "traditional-reporting", label: "Traditional Reporting", enabled: true, group: "Advanced" },
  { id: "production-reports", label: "Production Reports", enabled: true, group: "Advanced" },
];

export function getNavItems(session: SessionState): NavItemConfig[] {
  return navItems.filter((item) => canAccessPage(session, item.id));
}
