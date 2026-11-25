import { describe, expect, it } from "vitest";
import { R3_HOST_PERMISSIONS } from "./r3Permissions";
import {
  R3_ROUTES,
  canAccessR3Route,
  isUnifiedR3Path,
  parseR3RouteFromPathname,
  resolveDefaultR3Route,
} from "./r3RouteMap";

describe("r3RouteMap (Unified R3 RBAC & C1-C11 coverage)", () => {
  it("covers all 19 ISO R3 routes and C1-C11 domains using canonical Host permissions", () => {
    expect(R3_ROUTES).toHaveLength(19);
    const validPermissions = new Set<string>(Object.values(R3_HOST_PERMISSIONS));
    const coveredDomains = new Set<string>();
    for (const route of R3_ROUTES) {
      expect(route.anyOfPermissions.length).toBeGreaterThan(0);
      for (const perm of route.anyOfPermissions) {
        expect(validPermissions.has(perm)).toBe(true);
      }
      for (const d of route.domains) coveredDomains.add(d);
    }
    for (let i = 1; i <= 11; i++) {
      expect(coveredDomains.has(`C${i}`)).toBe(true);
    }
  });

  it("hides owner/admin routes from Cashiers and defaults Cashiers to POS Counter", () => {
    const cashierPerms = [
      R3_HOST_PERMISSIONS.OrdersView,
      R3_HOST_PERMISSIONS.OrdersCreate,
      R3_HOST_PERMISSIONS.OrdersComplete,
      R3_HOST_PERMISSIONS.ShiftOpenClose,
      R3_HOST_PERMISSIONS.CashIn,
      R3_HOST_PERMISSIONS.CashOut,
      R3_HOST_PERMISSIONS.AttendanceRecord,
    ];
    expect(resolveDefaultR3Route(cashierPerms)).toBe("pos");
    expect(canAccessR3Route(cashierPerms, "pos")).toBe(true);
    expect(canAccessR3Route(cashierPerms, "orders")).toBe(true);
    expect(canAccessR3Route(cashierPerms, "cash")).toBe(true);
    expect(canAccessR3Route(cashierPerms, "staff")).toBe(true);

    // Must fail closed for Owner/Admin routes
    expect(canAccessR3Route(cashierPerms, "today")).toBe(false);
    expect(canAccessR3Route(cashierPerms, "reports")).toBe(false);
    expect(canAccessR3Route(cashierPerms, "inventory")).toBe(false);
    expect(canAccessR3Route(cashierPerms, "menu")).toBe(false);
    expect(canAccessR3Route(cashierPerms, "recipes")).toBe(false);
    expect(canAccessR3Route(cashierPerms, "users")).toBe(false);
    expect(canAccessR3Route(cashierPerms, "settings")).toBe(false);
    expect(canAccessR3Route(cashierPerms, "backup")).toBe(false);
    expect(canAccessR3Route(cashierPerms, "audit")).toBe(false);
  });

  it("parses /cafe/r3 deep links cleanly", () => {
    expect(isUnifiedR3Path("/cafe/r3")).toBe(true);
    expect(isUnifiedR3Path("/cafe/r3/reports")).toBe(true);
    expect(isUnifiedR3Path("/cafe/")).toBe(false);
    expect(parseR3RouteFromPathname("/cafe/r3")).toBeNull();
    expect(parseR3RouteFromPathname("/cafe/r3/inventory")).toBe("inventory");
    expect(parseR3RouteFromPathname("/cafe/r3/cash-shifts")).toBe("cash");
    expect(parseR3RouteFromPathname("/cafe/r3/unknown")).toBeNull();
  });
});
