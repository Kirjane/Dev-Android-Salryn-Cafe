export type AppPage = "dashboard" | "products" | "inventory" | "sales" | "users" | "audit" | "remote-readonly";

export type EnabledNavItem = {
  id: AppPage;
  label: string;
  enabled: true;
};

export type DisabledNavItem = {
  id: "disabled";
  label: string;
  enabled: false;
};

export type NavItemConfig = EnabledNavItem | DisabledNavItem;

export const navItems: NavItemConfig[] = [
  { id: "dashboard", label: "Dashboard", enabled: true },
  { id: "disabled", label: "POS Checkout", enabled: false },
  { id: "remote-readonly", label: "Remote Read-Only Client", enabled: true },
  { id: "products", label: "Products", enabled: true },
  { id: "inventory", label: "Inventory", enabled: true },
  { id: "sales", label: "Sales History", enabled: true },
  { id: "disabled", label: "Official Output Text File", enabled: false },
  { id: "disabled", label: "Settings", enabled: false },
  { id: "users", label: "Users", enabled: true },
  { id: "disabled", label: "Backup", enabled: false },
  { id: "audit", label: "Audit", enabled: true },
];
