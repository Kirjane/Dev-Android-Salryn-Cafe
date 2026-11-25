import { beforeEach, describe, expect, it, vi } from "vitest";
import { hostRequest } from "../../api/hostClient";
import {
  createBooking,
  createDelivery,
  refundBookingDeposit,
} from "./bookingDeliveryContracts";

vi.mock("../../api/hostClient", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../../api/hostClient")>();
  return { ...actual, hostRequest: vi.fn() };
});

const mockedHostRequest = vi.mocked(hostRequest);

const G1 = "11111111-1111-1111-1111-111111111111";
const G2 = "22222222-2222-2222-2222-222222222222";
const G3 = "33333333-3333-3333-3333-333333333333";
const G4 = "44444444-4444-4444-4444-444444444444";
const G5 = "55555555-5555-5555-5555-555555555555";
const NOW = "2026-11-25T04:00:00.000Z";

describe("bookingDeliveryContracts (INT-03 / INT-04 / INT-05)", () => {
  beforeEach(() => {
    mockedHostRequest.mockReset();
  });

  it("rejects non-canonical BookingMode such as InStoreEvent before HTTP dispatch", async () => {
    await expect(
      createBooking({
        operationId: G1,
        bookingId: G2,
        revisionId: G3,
        occurredAtUtc: NOW,
        configuration: {
          customerId: G4,
          bookingMode: "InStoreEvent" as any,
          eventName: "Bad Mode Event",
          eventStartUtc: NOW,
          menuPublicationRevisionId: "store:1",
          depositRequirementCents: 50000,
          intendedServiceMode: "Takeout",
          lines: [{ productId: G5, quantityX1000: 1000, displayOrder: 0 }],
        },
      }),
    ).rejects.toThrow(/Event.*CoffeeCart/i);
    expect(mockedHostRequest).not.toHaveBeenCalled();
  });

  it("dispatches canonical nested configuration booking with matching Idempotency-Key", async () => {
    mockedHostRequest.mockResolvedValue({
      bookingId: G2,
      bookingVersion: 1,
      currentRevisionId: G3,
      status: "Draft",
      fulfillmentState: "None",
      isReplay: false,
    });

    const res = await createBooking({
      operationId: G1,
      bookingId: G2,
      revisionId: G3,
      occurredAtUtc: NOW,
      configuration: {
        customerId: G4,
        bookingMode: "CoffeeCart",
        eventName: "Tech Summit Cart",
        eventStartUtc: NOW,
        menuPublicationRevisionId: "store:1",
        depositRequirementCents: 100000,
        intendedServiceMode: "Takeout",
        lines: [{ productId: G5, quantityX1000: 2000, displayOrder: 0 }],
      },
    });

    expect(res.status).toBe("Draft");
    expect(mockedHostRequest).toHaveBeenCalledWith(
      "/api/cafe/bookings",
      expect.objectContaining({
        operationId: G1,
        configuration: expect.objectContaining({ bookingMode: "CoffeeCart" }),
      }),
      "POST",
      G1,
    );
  });

  it("rejects deposit refund when allocation sum does not match amountCents", async () => {
    await expect(
      refundBookingDeposit(G2, {
        operationId: G1,
        depositFactId: G3,
        cashMovementId: G4,
        bookingRevisionId: G5,
        amountCents: 50000,
        reason: "Cancelled",
        reference: "REF-1",
        allocations: [{ receivedDepositFactId: G1, amountCents: 30000 }],
        occurredAtUtc: NOW,
      }),
    ).rejects.toThrow(/allocation sum/i);
    expect(mockedHostRequest).not.toHaveBeenCalled();
  });

  it("dispatches order-bound delivery with canonical orderId and Idempotency-Key", async () => {
    mockedHostRequest.mockResolvedValue({
      deliveryId: G2,
      orderId: G3,
      storeId: G4,
      version: 1,
      currentRevisionId: G5,
      status: "Pending",
      createdByUserId: G1,
      createdAtUtc: NOW,
      updatedAtUtc: NOW,
      terminalReason: null,
    });

    const res = await createDelivery({
      operationId: G1,
      deliveryId: G2,
      orderId: G3,
      occurredAtUtc: NOW,
      recipientName: "Maria Santos",
      deliveryAddress: "123 Rizal St",
      deliveryFeeCents: 5000,
    });

    expect(res.status).toBe("Pending");
    expect(mockedHostRequest).toHaveBeenCalledWith(
      "/api/cafe/deliveries",
      expect.objectContaining({ operationId: G1, orderId: G3 }),
      "POST",
      G1,
    );
  });
});
