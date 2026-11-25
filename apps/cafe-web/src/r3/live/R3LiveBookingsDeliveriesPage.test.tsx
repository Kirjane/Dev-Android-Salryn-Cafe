import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { hostRequest } from "../../api/hostClient";
import { R3LiveBookingsDeliveriesPage } from "./R3LiveBookingsDeliveriesPage";

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

const MANAGER_SESSION = {
  isAuthenticated: true,
  user: {
    userId: "aaaa1111-1111-1111-1111-111111111111",
    username: "manager",
    displayName: "Cafe Manager",
    roleName: "Manager",
    permissions: [
      "Booking.View",
      "Booking.Manage",
      "Booking.DepositReceive",
      "Booking.DepositRefund",
      "Cash.In",
      "Cash.Out",
      "Delivery.View",
      "Delivery.Manage",
      "Delivery.Advance",
      "Orders.View",
    ],
  },
};

const BOOKING_ID = "bbbb1111-1111-1111-1111-111111111111";
const DEP_1 = "cccc1111-1111-1111-1111-111111111111";
const DEP_2 = "cccc2222-2222-2222-2222-222222222222";
const DELIVERY_ID = "dddd1111-1111-1111-1111-111111111111";

describe("R3LiveBookingsDeliveriesPage (Gate C P1 durable intents & historical deposits)", () => {
  beforeEach(() => {
    window.localStorage.clear();
    mockedHostRequest.mockReset();
  });

  afterEach(() => {
    cleanup();
  });

  it("loads historical multi-deposit facts from Host and commits partial multi-deposit refund allocations", async () => {
    const refundCalls: Array<{ path: string; body: any; key?: string }> = [];

    mockedHostRequest.mockImplementation(async (path, body, method, idempotencyKey) => {
      if (path === "/api/license/status") return VALID_LICENSE;
      if (path === "/api/session") return MANAGER_SESSION;
      if (path === "/api/system/identity") return { productId: "Salryn.Cafe", databasePath: "C:\\tmp\\cafe.db" };
      if (path === "/api/cafe/bookings" && (!method || method === "GET")) {
        return [
          {
            bookingId: BOOKING_ID,
            storeId: "11111111-1111-1111-1111-111111111111",
            bookingVersion: 2,
            currentRevisionId: "ffff1111-1111-1111-1111-111111111111",
            bookingMode: "Event",
            status: "Confirmed",
            fulfillmentState: "NotStarted",
            customerId: "11111111-1111-1111-1111-111111111111",
            customerNameSnapshot: "Maria Santos",
            quotedTotalCents: 200000,
            depositReceivedTotalCents: 150000,
            depositRefundedTotalCents: 0,
            preFulfillmentDepositCreditCents: 150000,
            preFulfillmentOutstandingBalanceCents: 50000,
            eventStartUtc: "2026-12-01T09:00:00Z",
            eventEndUtc: null,
          },
        ];
      }
      if (path === `/api/cafe/bookings/${BOOKING_ID}` && (!method || method === "GET")) {
        return {
          booking: {
            bookingId: BOOKING_ID,
            storeId: "11111111-1111-1111-1111-111111111111",
            bookingVersion: 2,
            currentRevisionId: "ffff1111-1111-1111-1111-111111111111",
            bookingMode: "Event",
            status: "Confirmed",
            fulfillmentState: "NotStarted",
            customerId: "11111111-1111-1111-1111-111111111111",
            customerNameSnapshot: "Maria Santos",
            quotedTotalCents: 200000,
            depositReceivedTotalCents: 150000,
            depositRefundedTotalCents: 0,
            preFulfillmentDepositCreditCents: 150000,
            preFulfillmentOutstandingBalanceCents: 50000,
            eventStartUtc: "2026-12-01T09:00:00Z",
            eventEndUtc: null,
          },
          currentRevision: {
            revisionId: "ffff1111-1111-1111-1111-111111111111",
            bookingId: BOOKING_ID,
            revisionNumber: 1,
            eventName: "Maria Event",
            quotedTotalCents: 200000,
            depositRequirementCents: 100000,
          },
          depositFacts: [
            {
              depositFactId: DEP_1,
              bookingRevisionId: "ffff1111-1111-1111-1111-111111111111",
              factType: "Received",
              actorUserId: MANAGER_SESSION.user.userId,
              amountCents: 100000,
              allocatedAmountCents: 0,
              remainingRefundableCents: 100000,
              occurredAtUtc: "2026-11-20T01:00:00Z",
              reference: "DEP-FIRST",
            },
            {
              depositFactId: DEP_2,
              bookingRevisionId: "ffff1111-1111-1111-1111-111111111111",
              factType: "Received",
              actorUserId: MANAGER_SESSION.user.userId,
              amountCents: 50000,
              allocatedAmountCents: 0,
              remainingRefundableCents: 50000,
              occurredAtUtc: "2026-11-21T01:00:00Z",
              reference: "DEP-SECOND",
            },
          ],
        };
      }
      if (path === `/api/cafe/bookings/${BOOKING_ID}/deposits/refund` && method === "POST") {
        refundCalls.push({ path, body, key: idempotencyKey });
        return {
          depositFactId: "eeee1111-1111-1111-1111-111111111111",
          bookingId: BOOKING_ID,
          bookingVersion: 3,
          factType: "Refund",
          amountCents: 75000,
        };
      }
      if (path === "/api/cafe/bookings/customers" || path === "/api/customers") return [];
      if (path === "/api/menu/publication/current") return null;
      if (path === "/api/cafe/deliveries") return [];
      if (path === "/api/cafe/orders") return [];
      throw new Error(`Unhandled ${method} ${path}`);
    });

    render(<R3LiveBookingsDeliveriesPage initialTab="bookings" />);
    await waitFor(() => {
      expect(screen.getByText("DEP-FIRST")).toBeTruthy();
      expect(screen.getByText("DEP-SECOND")).toBeTruthy();
    });

    fireEvent.change(screen.getByLabelText("Refund allocation for DEP-FIRST"), { target: { value: "500.00" } });
    fireEvent.change(screen.getByLabelText("Refund allocation for DEP-SECOND"), { target: { value: "250.00" } });
    fireEvent.click(screen.getByRole("button", { name: "Commit Allocated Refund" }));

    await waitFor(() => {
      expect(refundCalls).toHaveLength(1);
    });
    expect(refundCalls[0].body.allocations).toEqual([
      { receivedDepositFactId: DEP_1, amountCents: 50000 },
      { receivedDepositFactId: DEP_2, amountCents: 25000 },
    ]);
  });

  it("wraps confirmBooking and dispatchDelivery in durable intents that replay exact operationId after reload", async () => {
    let confirmAttempts = 0;
    const confirmBodies: any[] = [];
    let dispatchAttempts = 0;
    const dispatchBodies: any[] = [];

    mockedHostRequest.mockImplementation(async (path, body, method) => {
      if (path === "/api/license/status") return VALID_LICENSE;
      if (path === "/api/session") return MANAGER_SESSION;
      if (path === "/api/system/identity") return { productId: "Salryn.Cafe", databasePath: "C:\\tmp\\cafe.db" };
      if (path === "/api/cafe/bookings" && (!method || method === "GET")) {
        return [
          {
            bookingId: BOOKING_ID,
            bookingVersion: 1,
            bookingMode: "Event",
            status: "Draft",
            customerId: "11111111-1111-1111-1111-111111111111",
            customerNameSnapshot: "Maria Santos",
            quotedTotalCents: 50000,
          },
        ];
      }
      if (path === `/api/cafe/bookings/${BOOKING_ID}` && (!method || method === "GET")) {
        return {
          summary: {
            bookingId: BOOKING_ID,
            bookingVersion: 1,
            bookingMode: "Event",
            status: "Draft",
            customerId: "11111111-1111-1111-1111-111111111111",
            customerNameSnapshot: "Maria Santos",
          },
          depositFacts: [],
        };
      }
      if (path === `/api/cafe/bookings/${BOOKING_ID}/confirm` && method === "POST") {
        confirmAttempts += 1;
        confirmBodies.push(body);
        if (confirmAttempts === 1) throw new Error("Simulated Host restart mid-confirm");
        return { bookingId: BOOKING_ID, bookingVersion: 2, status: "Confirmed" };
      }
      if (path === "/api/cafe/deliveries" && (!method || method === "GET")) {
        return [
          {
            deliveryId: DELIVERY_ID,
            version: 1,
            storeId: "11111111-1111-1111-1111-111111111111",
            currentRevisionId: "ffff2222-2222-2222-2222-222222222222",
            orderId: "eeee2222-2222-2222-2222-222222222222",
            status: "Pending",
            createdByUserId: MANAGER_SESSION.user.userId,
            createdAtUtc: "2026-11-21T01:00:00Z",
            updatedAtUtc: "2026-11-21T01:00:00Z",
            terminalReason: null,
          },
        ];
      }
      if (path === `/api/cafe/deliveries/${DELIVERY_ID}/dispatch` && method === "POST") {
        dispatchAttempts += 1;
        dispatchBodies.push(body);
        if (dispatchAttempts === 1) throw new Error("Simulated Host restart mid-dispatch");
        return { deliveryId: DELIVERY_ID, deliveryVersion: 2, status: "Dispatched" };
      }
      if (path === "/api/cafe/bookings/customers" || path === "/api/customers") return [];
      if (path === "/api/menu/publication/current") return null;
      if (path === "/api/cafe/orders") return [];
      throw new Error(`Unhandled ${method} ${path}`);
    });

    const { unmount } = render(<R3LiveBookingsDeliveriesPage initialTab="bookings" />);
    await waitFor(() => {
      expect(screen.getByRole("button", { name: "Confirm" })).toBeTruthy();
    });

    // Confirm fails with uncertain outcome -> persists in localStorage v2
    fireEvent.click(screen.getByRole("button", { name: "Confirm" }));
    await waitFor(() => {
      expect(screen.getByText(/Booking Confirm: pending request has an unresolved outcome/i)).toBeTruthy();
    });

    // Reload -> retry exact confirm -> identical operationId & occurredAtUtc
    unmount();
    render(<R3LiveBookingsDeliveriesPage initialTab="bookings" />);
    await waitFor(() => {
      expect(screen.getByRole("button", { name: "Retry Exact Booking Confirm" })).toBeTruthy();
    });
    fireEvent.click(screen.getByRole("button", { name: "Retry Exact Booking Confirm" }));
    await waitFor(() => {
      expect(confirmAttempts).toBe(2);
    });
    expect(confirmBodies[1]).toEqual(confirmBodies[0]);

    // Switch to Deliveries -> Dispatch fails -> retry exact dispatch -> identical payload
    fireEvent.click(screen.getByRole("tab", { name: /Deliveries/i }));
    await waitFor(() => {
      expect(screen.getByRole("button", { name: "Dispatch" })).toBeTruthy();
    });
    fireEvent.click(screen.getByRole("button", { name: "Dispatch" }));
    await waitFor(() => {
      expect(screen.getByRole("button", { name: "Retry Exact Delivery Dispatch" })).toBeTruthy();
    });
    fireEvent.click(screen.getByRole("button", { name: "Retry Exact Delivery Dispatch" }));
    await waitFor(() => {
      expect(dispatchAttempts).toBe(2);
    });
    expect(dispatchBodies[1]).toEqual(dispatchBodies[0]);
  });
});
