import { describe, expect, it } from "vitest";
import { hasHostPermission, R3_HOST_PERMISSIONS } from "./r3Permissions";

describe("r3Permissions (INT-11 canonical Host permission parity)", () => {
  it("uses exact singular Host permissions rather than drifted ISO plural names", () => {
    const values = Object.values(R3_HOST_PERMISSIONS);
    expect(values).toContain("Booking.View");
    expect(values).toContain("Delivery.View");
    expect(values).toContain("Incident.View");
    expect(values).toContain("Sales.RefundRequest");
    expect(values).toContain("Sales.RefundApprove");
    expect(values).toContain("Users.Manage");
    expect(values).toContain("Settings.Manage");
    expect(values).not.toContain("Bookings.View");
    expect(values).not.toContain("Deliveries.View");
    expect(values).not.toContain("Incidents.View");
    expect(values).not.toContain("Refunds.View");
    expect(values).not.toContain("Users.View");
    expect(values).not.toContain("Settings.View");
    expect(values).not.toContain("Inventory.Manage");
  });

  it("fails closed when permissions are null or missing", () => {
    expect(hasHostPermission(null, R3_HOST_PERMISSIONS.ReportsView)).toBe(false);
    expect(hasHostPermission(undefined, R3_HOST_PERMISSIONS.AuditView)).toBe(false);
    expect(hasHostPermission(["Reports.View"], R3_HOST_PERMISSIONS.ReportsView)).toBe(true);
  });
});
