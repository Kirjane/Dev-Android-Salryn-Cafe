import { R3_HOST_PERMISSIONS, type R3HostPermission } from "./r3Permissions";

export type R3RouteKey =
  | "today"
  | "pos"
  | "orders"
  | "kitchen"
  | "cash"
  | "menu"
  | "recipes"
  | "inventory"
  | "customers"
  | "bookings"
  | "deliveries"
  | "staff"
  | "reports"
  | "users"
  | "settings"
  | "printers"
  | "devices"
  | "backup"
  | "audit";

export type R3NavGroupKey =
  | "Overview"
  | "Sales & Service"
  | "Menu & Stock"
  | "Operations"
  | "Insights"
  | "Administration";

export type R3RouteDefinition = {
  key: R3RouteKey;
  path: string;
  label: string;
  group: R3NavGroupKey;
  collapsibleGroup?: boolean;
  /** Operator must hold at least one of these canonical Host permissions to access the route. */
  anyOfPermissions: readonly R3HostPermission[];
  /** C1–C11 donor & Dev functional domain traceability tags. */
  domains: readonly string[];
};

export const R3_ROUTES: readonly R3RouteDefinition[] = [
  {
    key: "today",
    path: "/cafe/r3/today",
    label: "Today",
    group: "Overview",
    anyOfPermissions: [R3_HOST_PERMISSIONS.ReportsView],
    domains: ["C8", "C11"],
  },
  {
    key: "pos",
    path: "/cafe/r3/pos",
    label: "POS Counter",
    group: "Sales & Service",
    anyOfPermissions: [R3_HOST_PERMISSIONS.OrdersCreate, R3_HOST_PERMISSIONS.OrdersView],
    domains: ["C3", "C4", "C11"],
  },
  {
    key: "orders",
    path: "/cafe/r3/orders",
    label: "Orders",
    group: "Sales & Service",
    anyOfPermissions: [R3_HOST_PERMISSIONS.OrdersView],
    domains: ["C3", "C4"],
  },
  {
    key: "kitchen",
    path: "/cafe/r3/kitchen",
    label: "Kitchen Display",
    group: "Sales & Service",
    anyOfPermissions: [R3_HOST_PERMISSIONS.PreparationManage, R3_HOST_PERMISSIONS.OrdersView],
    domains: ["C3"],
  },
  {
    key: "cash",
    path: "/cafe/r3/cash",
    label: "Cash & Shifts",
    group: "Sales & Service",
    anyOfPermissions: [
      R3_HOST_PERMISSIONS.ShiftOpenClose,
      R3_HOST_PERMISSIONS.CashIn,
      R3_HOST_PERMISSIONS.CashOut,
      R3_HOST_PERMISSIONS.AuditView,
    ],
    domains: ["C4", "C5"],
  },
  {
    key: "menu",
    path: "/cafe/r3/menu",
    label: "Menu & Modifiers",
    group: "Menu & Stock",
    anyOfPermissions: [R3_HOST_PERMISSIONS.ProductsManage],
    domains: ["C1"],
  },
  {
    key: "recipes",
    path: "/cafe/r3/recipes",
    label: "Recipes & BOM",
    group: "Menu & Stock",
    anyOfPermissions: [R3_HOST_PERMISSIONS.RecipesView, R3_HOST_PERMISSIONS.IngredientsView],
    domains: ["C2"],
  },
  {
    key: "inventory",
    path: "/cafe/r3/inventory",
    label: "Inventory",
    group: "Menu & Stock",
    anyOfPermissions: [R3_HOST_PERMISSIONS.InventoryView, R3_HOST_PERMISSIONS.IngredientsView],
    domains: ["C2", "C7"],
  },
  {
    key: "customers",
    path: "/cafe/r3/customers",
    label: "Customers & AR",
    group: "Operations",
    anyOfPermissions: [R3_HOST_PERMISSIONS.ReceivablesView, R3_HOST_PERMISSIONS.SalesRefundRequest],
    domains: ["C5"],
  },
  {
    key: "bookings",
    path: "/cafe/r3/bookings",
    label: "Bookings & Cart",
    group: "Operations",
    anyOfPermissions: [R3_HOST_PERMISSIONS.BookingView],
    domains: ["C6"],
  },
  {
    key: "deliveries",
    path: "/cafe/r3/deliveries",
    label: "Deliveries",
    group: "Operations",
    anyOfPermissions: [R3_HOST_PERMISSIONS.DeliveryView],
    domains: ["C6"],
  },
  {
    key: "staff",
    path: "/cafe/r3/staff",
    label: "Staff & Incidents",
    group: "Operations",
    anyOfPermissions: [
      R3_HOST_PERMISSIONS.AttendanceRecord,
      R3_HOST_PERMISSIONS.IncidentView,
      R3_HOST_PERMISSIONS.IncidentRecord,
    ],
    domains: ["C10"],
  },
  {
    key: "reports",
    path: "/cafe/r3/reports",
    label: "Reports & COGS",
    group: "Insights",
    anyOfPermissions: [R3_HOST_PERMISSIONS.ReportsView],
    domains: ["C8"],
  },
  {
    key: "users",
    path: "/cafe/r3/users",
    label: "Users & Roles",
    group: "Administration",
    collapsibleGroup: true,
    anyOfPermissions: [R3_HOST_PERMISSIONS.UsersManage],
    domains: ["C9"],
  },
  {
    key: "settings",
    path: "/cafe/r3/settings",
    label: "Store Settings",
    group: "Administration",
    collapsibleGroup: true,
    anyOfPermissions: [R3_HOST_PERMISSIONS.SettingsManage],
    domains: ["C9"],
  },
  {
    key: "printers",
    path: "/cafe/r3/printers",
    label: "Print Stations",
    group: "Administration",
    collapsibleGroup: true,
    anyOfPermissions: [R3_HOST_PERMISSIONS.SettingsManage],
    domains: ["C3", "C9"],
  },
  {
    key: "devices",
    path: "/cafe/r3/devices",
    label: "Devices & Central",
    group: "Administration",
    collapsibleGroup: true,
    anyOfPermissions: [R3_HOST_PERMISSIONS.SettingsManage],
    domains: ["C9", "C11"],
  },
  {
    key: "backup",
    path: "/cafe/r3/backup",
    label: "Backup & License",
    group: "Administration",
    collapsibleGroup: true,
    anyOfPermissions: [R3_HOST_PERMISSIONS.SettingsManage],
    domains: ["C9", "C11"],
  },
  {
    key: "audit",
    path: "/cafe/r3/audit",
    label: "Audit Log",
    group: "Administration",
    collapsibleGroup: true,
    anyOfPermissions: [R3_HOST_PERMISSIONS.AuditView],
    domains: ["C9"],
  },
] as const;

export const R3_ROUTE_BY_KEY: Record<R3RouteKey, R3RouteDefinition> = Object.fromEntries(
  R3_ROUTES.map((r) => [r.key, r]),
) as Record<R3RouteKey, R3RouteDefinition>;

export function canAccessR3Route(
  permissions: readonly string[] | null | undefined,
  routeKey: R3RouteKey,
): boolean {
  if (!Array.isArray(permissions) || permissions.length === 0) return false;
  const def = R3_ROUTE_BY_KEY[routeKey];
  if (!def) return false;
  return def.anyOfPermissions.some((perm) => permissions.includes(perm));
}

export function resolveDefaultR3Route(permissions: readonly string[] | null | undefined): R3RouteKey | null {
  for (const route of R3_ROUTES) {
    if (canAccessR3Route(permissions, route.key)) {
      return route.key;
    }
  }
  return null;
}

export function parseR3RouteFromPathname(pathname: string): R3RouteKey | null {
  const normalized = pathname.replace(/\/+$/, "").toLowerCase();
  if (normalized === "/cafe/r3") return null;
  if (!normalized.startsWith("/cafe/r3/")) return null;
  const segment = normalized.slice("/cafe/r3/".length).split("/")[0];
  if (segment === "cash-shifts") return "cash";
  if (segment in R3_ROUTE_BY_KEY) return segment as R3RouteKey;
  return null;
}

export function isUnifiedR3Path(pathname: string): boolean {
  const normalized = pathname.replace(/\/+$/, "").toLowerCase();
  return normalized === "/cafe/r3" || normalized.startsWith("/cafe/r3/");
}
