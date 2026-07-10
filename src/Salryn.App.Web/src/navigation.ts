import type { SessionState } from "./api/session";
import { canAccessPage, type AppPage } from "./security/permissions";

export type { AppPage } from "./security/permissions";

export type EnabledNavItem = {
  id: AppPage;
  label: string;
  enabled: true;
};

export type DisabledNavItem = {
  id: "disabled";
  label: string;
  enabled: false;
  reason: string;
};

export type NavItemConfig = EnabledNavItem | DisabledNavItem;

export const navItems: NavItemConfig[] = [
  { id: "dashboard", label: "Dashboard", enabled: true },
  { id: "checkout", label: "POS Checkout", enabled: true },
  { id: "products", label: "Products", enabled: true },
  { id: "inventory", label: "Inventory", enabled: true },
  { id: "sales", label: "Sales History", enabled: true },
  {
    id: "disabled",
    label: "Official Output Text File",
    enabled: false,
    reason: "Disabled because the React shell has no approved Official Output Text File API contract yet.",
  },
  {
    id: "disabled",
    label: "Settings",
    enabled: false,
    reason: "Disabled because the React shell has no approved settings API contract yet.",
  },
  { id: "users", label: "Users", enabled: true },
  {
    id: "disabled",
    label: "Backup",
    enabled: false,
    reason: "Disabled because the React shell has no approved backup API contract yet.",
  },
  { id: "audit", label: "Audit", enabled: true },
  { id: "host-lan-control", label: "Host LAN Control", enabled: true },
  { id: "official-setup", label: "Official Setup", enabled: true },
  { id: "traditional-reporting", label: "Traditional Reporting", enabled: true },
];

export function getNavItems(session: SessionState): NavItemConfig[] {
  return navItems.filter((item) => !item.enabled || canAccessPage(session, item.id));
}
