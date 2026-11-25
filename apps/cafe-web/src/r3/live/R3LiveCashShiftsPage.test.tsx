import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { HostApiError, hostRequest } from "../../api/hostClient";
import { R3LiveCashShiftsPage } from "./R3LiveCashShiftsPage";

vi.mock("../../api/hostClient", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../../api/hostClient")>();
  return {
    ...actual,
    hostRequest: vi.fn(),
  };
});

const mockedHostRequest = vi.mocked(hostRequest);

const VALID_LICENSE = {
  status: "Licensed",
  licenseType: "Host",
  applicationId: "Salryn.Cafe",
  machineMatched: true,
};

const OWNER_SESSION = {
  isAuthenticated: true,
  user: {
    userId: "11111111-1111-1111-1111-111111111111",
    username: "owner",
    displayName: "Cafe Owner",
    roleName: "Owner",
    permissions: ["Shift.OpenClose", "Cash.In", "Cash.Out", "Audit.View"],
  },
};

const SESSION_ID = "99991111-1111-1111-1111-111111111111";

describe("R3LiveCashShiftsPage (Gate C Shift Open -> Blind Close -> Variance Review)", () => {
  beforeEach(() => {
    window.localStorage.clear();
    mockedHostRequest.mockReset();
  });

  afterEach(() => {
    cleanup();
  });

  it("opens shift, requires physical confirmation for blind close while hiding expected cash, and submits variance review", async () => {
    let shiftOpen = false;
    let closedHistory: any[] = [];
    const closeBodies: any[] = [];

    mockedHostRequest.mockImplementation(async (path, body, method) => {
      if (path === "/api/license/status") return VALID_LICENSE;
      if (path === "/api/session") return OWNER_SESSION;
      if (path === "/api/system/identity") return { productId: "Salryn.Cafe", databasePath: "C:\\tmp\\cafe.db" };
      if (path === "/api/cash-drawer") {
        if (!shiftOpen) {
          throw new HostApiError("No open shift.", 404, "CashDrawer.NoOpenShift");
        }
        return {
          sessionId: SESSION_ID,
          state: "Open",
          openingCashCents: 100000,
          expectedCashCents: null,
          countedCashCents: null,
          varianceCents: null,
          coverageStatus: "Full",
          reviewStatus: "None",
          events: [],
          reviews: [],
        };
      }
      if (path.startsWith("/api/cash-drawer/history")) return { items: closedHistory, totalCount: closedHistory.length };
      if (path === "/api/shift/open" && method === "POST") {
        shiftOpen = true;
        return {
          status: "Opened",
          shift: { id: SESSION_ID, openingCashCents: (body as any).openingCashCents },
        };
      }
      if (path === "/api/shift/close" && method === "POST") {
        closeBodies.push(body);
        shiftOpen = false;
        closedHistory = [
          {
            sessionId: SESSION_ID,
            openingCashCents: 100000,
            expectedCashCents: 100000,
            countedCashCents: 98000,
            varianceCents: -2000,
            status: "Closed",
            reviewStatus: "PendingReview",
            openedByUserId: OWNER_SESSION.user.userId,
            closedByUserId: OWNER_SESSION.user.userId,
          },
        ];
        return {
          status: "Closed",
          shift: { id: SESSION_ID, varianceCents: -2000 },
        };
      }
      throw new Error(`Unhandled ${method} ${path}`);
    });

    render(<R3LiveCashShiftsPage />);

    // Starts with Open Shift card when no shift is open.
    await waitFor(() => {
      expect(screen.getByRole("button", { name: "Open Shift" })).toBeTruthy();
    });

    fireEvent.change(screen.getByLabelText("Opening Cash Float"), { target: { value: "1000.00" } });
    fireEvent.click(screen.getByRole("button", { name: "Open Shift" }));

    // Blind Close card appears; expected cash is hidden.
    await waitFor(() => {
      expect(screen.getByRole("button", { name: "Close Shift Blind" })).toBeTruthy();
    });
    expect(screen.getByText(/Expected cash stays hidden until blind close/i)).toBeTruthy();

    // Attempting blind close without physical confirmation is blocked client-side.
    fireEvent.change(screen.getByLabelText("Physical Counted Cash"), { target: { value: "980.00" } });
    fireEvent.click(screen.getByRole("button", { name: "Close Shift Blind" }));
    await waitFor(() => {
      expect(screen.getByText(/Confirm that you physically counted the cash drawer/i)).toBeTruthy();
    });
    expect(closeBodies).toHaveLength(0);

    // Check confirmation and close blind.
    fireEvent.click(screen.getByRole("checkbox"));
    fireEvent.click(screen.getByRole("button", { name: "Close Shift Blind" }));
    await waitFor(() => {
      expect(closeBodies).toHaveLength(1);
    });
    expect(closeBodies[0].countedCashCents).toBe(98000);
    expect(closeBodies[0].physicalCountConfirmed).toBe(true);

    // Switch to History & Variance Review -> closed shift with -20.00 variance is visible.
    fireEvent.click(screen.getByRole("tab", { name: /History & Variance Review/i }));
    await waitFor(() => {
      expect(screen.getByText("PendingReview")).toBeTruthy();
    });
  });
});
