import { beforeEach, describe, expect, it, vi } from "vitest";
import { hostRequest } from "../../api/hostClient";
import {
  readCashDrawerHistory,
  recordCashDrawerMovement,
  reviewCashDrawerVariance,
} from "./cashDrawerContracts";

vi.mock("../../api/hostClient", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../../api/hostClient")>();
  return { ...actual, hostRequest: vi.fn() };
});

const mockedHostRequest = vi.mocked(hostRequest);
const S1 = "11111111-1111-1111-1111-111111111111";

describe("cashDrawerContracts (INT-07 / INT-08)", () => {
  beforeEach(() => {
    mockedHostRequest.mockReset();
  });

  it("rejects non-Acknowledged variance review decisions before HTTP dispatch", async () => {
    await expect(
      reviewCashDrawerVariance(S1, {
        decision: "Approved" as any,
        reason: "Invalid decision",
        clientRequestId: "req-1",
      }),
    ).rejects.toThrow(/Acknowledged/i);
    expect(mockedHostRequest).not.toHaveBeenCalled();
  });

  it("dispatches independent variance review to /api/cash-drawer/{sessionId}/review", async () => {
    mockedHostRequest.mockResolvedValue({
      sessionId: S1,
      state: "Closed",
      openingCashCents: 100000,
      expectedCashCents: 105000,
      countedCashCents: 102000,
      varianceCents: -3000,
      coverageStatus: "Verified",
      reviewStatus: "Acknowledged",
      events: [],
      reviews: [{ decision: "Acknowledged", reason: "Verified short", reviewerUserId: S1, createdAt: "2026-11-25T07:00:00Z" }],
    });

    const res = await reviewCashDrawerVariance(S1, {
      decision: "Acknowledged",
      reason: "Verified short",
      clientRequestId: "req-review-1",
    });

    expect(res.reviewStatus).toBe("Acknowledged");
    expect(mockedHostRequest).toHaveBeenCalledWith(
      `/api/cash-drawer/${S1}/review`,
      { decision: "Acknowledged", reason: "Verified short", clientRequestId: "req-review-1" },
      "POST",
    );
  });

  it("validates history pagination bounds and queries /api/cash-drawer/history", async () => {
    await expect(readCashDrawerHistory(0, 0)).rejects.toThrow(/limit/i);
    mockedHostRequest.mockResolvedValue({ items: [], limit: 25, offset: 5 });
    const page = await readCashDrawerHistory(25, 5);
    expect(page.limit).toBe(25);
    expect(mockedHostRequest).toHaveBeenCalledWith("/api/cash-drawer/history?limit=25&offset=5", undefined, "GET");
  });

  it("rejects non-positive cash drawer movements", async () => {
    await expect(
      recordCashDrawerMovement({
        eventType: "CashIn",
        amountCents: 0,
        clientRequestId: "mv-1",
        reason: "Zero movement",
      }),
    ).rejects.toThrow(/positive integer/i);
    expect(mockedHostRequest).not.toHaveBeenCalled();
  });
});
