import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { HostApiError, hostRequest } from "../../api/hostClient";
import { R3LiveInventoryPage } from "./R3LiveInventoryPage";

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

const ADMIN_SESSION = {
  isAuthenticated: true,
  user: {
    id: "11111111-1111-1111-1111-111111111111",
    username: "owner",
    displayName: "Owner",
    roleName: "Admin",
    permissions: [
      "Inventory.View",
      "Ingredients.View",
      "Recipes.View",
      "Inventory.Adjust",
      "Inventory.Count",
      "Purchasing.Receive",
      "Inventory.Waste",
      "Suppliers.View",
    ],
  },
};

const ING_ID = "22222222-2222-2222-2222-222222222222";

const SAMPLE_INGREDIENTS = {
  ingredients: [
    {
      id: ING_ID,
      name: "Espresso Beans",
      stockUnit: "pc",
      stockOnHandX1000: 100000,
      reorderLevelX1000: 10000,
      isActive: true,
      notes: null,
    },
  ],
};

describe("R3LiveInventoryPage (INT-01 / INT-02)", () => {
  beforeEach(() => {
    window.localStorage.clear();
    mockedHostRequest.mockReset();
  });

  afterEach(() => {
    cleanup();
  });

  it("fails closed when Host license is missing and issues zero inventory calls", async () => {
    mockedHostRequest.mockImplementation(async (path) => {
      if (path === "/api/license/status") return { status: "Unlicensed", machineMatched: false };
      throw new Error("Unexpected call: " + path);
    });

    render(<R3LiveInventoryPage />);
    await waitFor(() => {
      expect(screen.getByText(/Active Host license required/i)).toBeTruthy();
    });
    expect(mockedHostRequest).toHaveBeenCalledTimes(1);
  });

  it("persists adjustment operationId across unknown outcome and reload, blocks changed intent, and replays exact payload", async () => {
    let adjustAttempts = 0;
    const adjustPayloads: Array<{ body: any; key?: string }> = [];

    mockedHostRequest.mockImplementation(async (path, body, method, idempotencyKey) => {
      if (path === "/api/license/status") return VALID_LICENSE;
      if (path === "/api/session") return ADMIN_SESSION;
      if (path === "/api/cafe/ingredients-recipes") return SAMPLE_INGREDIENTS;
      if (path === "/api/cafe/inventory/low-stock") return [];
      if (path === "/api/cafe/suppliers") return [];
      if (path === "/api/cafe/inventory/adjust" && method === "POST") {
        adjustAttempts += 1;
        adjustPayloads.push({ body, key: idempotencyKey });
        if (adjustAttempts === 1) {
          throw new Error("Simulated network drop after dispatch");
        }
        return {
          evidence: {
            adjustmentEventId: (body as any).adjustmentEventId,
            operationId: (body as any).operationId,
            storeId: "33333333-3333-3333-3333-333333333333",
            stockTargetType: "Ingredient",
            stockTargetId: ING_ID,
            nameSnapshot: "Espresso Beans",
            stockUnitSnapshot: "pc",
            quantityDeltaX1000: (body as any).quantityDeltaX1000,
            reason: (body as any).reason,
            referenceType: null,
            referenceId: null,
            correctionOfReferenceId: null,
            actorUserId: ADMIN_SESSION.user.id,
            occurredAtUtc: (body as any).occurredAtUtc,
            contentHash: "hash-1",
          },
          isReplay: false,
        };
      }
      throw new Error(`Unhandled ${method} ${path}`);
    });

    const { unmount } = render(<R3LiveInventoryPage />);
    await waitFor(() => {
      expect(screen.getByRole("tab", { name: "Manual Adjustment" })).toBeTruthy();
    });

    fireEvent.click(screen.getByRole("tab", { name: "Manual Adjustment" }));
    fireEvent.change(screen.getByLabelText("Signed Quantity Delta"), { target: { value: "+2.5" } });
    fireEvent.change(screen.getByLabelText("Adjustment Reason"), { target: { value: "Found unopened bag" } });
    fireEvent.click(screen.getByRole("button", { name: "Commit Adjustment" }));

    await waitFor(() => {
      expect(screen.getByText(/Pending adjustment has an unresolved outcome/i)).toBeTruthy();
    });
    expect(adjustAttempts).toBe(1);

    // Attempting to submit a CHANGED intent must be blocked by the fence (zero second POST).
    fireEvent.change(screen.getByLabelText("Signed Quantity Delta"), { target: { value: "+9" } });
    fireEvent.click(screen.getByRole("button", { name: "Commit Adjustment" }));
    await waitFor(() => {
      expect(screen.getByText(/unresolved outcome\. Retry the exact prepared request/i)).toBeTruthy();
    });
    expect(adjustAttempts).toBe(1);

    // Simulate full browser reload: unmount and remount, verify localStorage restored exact pending intent.
    unmount();
    render(<R3LiveInventoryPage />);
    await waitFor(() => {
      expect(screen.getByRole("tab", { name: "Manual Adjustment" })).toBeTruthy();
    });
    fireEvent.click(screen.getByRole("tab", { name: "Manual Adjustment" }));
    expect(screen.getByText(/Pending adjustment has an unresolved outcome/i)).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Retry Exact Adjustment" }));
    await waitFor(() => {
      expect(screen.getAllByText(/Adjustment retry confirmed/i).length).toBeGreaterThan(0);
    });

    expect(adjustAttempts).toBe(2);
    expect(adjustPayloads[1].key).toBe(adjustPayloads[0].key);
    expect(adjustPayloads[1].body).toEqual(adjustPayloads[0].body);
    expect(window.localStorage.getItem("salryn.cafe.r3.pending.inventory-adjust.v1")).toBeNull();
  });

  it("executes Physical Count draft -> stale 409 handling -> finalize -> immutable finalized state", async () => {
    let savedSession: any = null;
    let finalizeCalls = 0;

    mockedHostRequest.mockImplementation(async (path, body, method) => {
      if (path === "/api/license/status") return VALID_LICENSE;
      if (path === "/api/session") return ADMIN_SESSION;
      if (path === "/api/cafe/ingredients-recipes") return SAMPLE_INGREDIENTS;
      if (path === "/api/cafe/inventory/low-stock") return [];
      if (path === "/api/cafe/suppliers") return [];
      if (path === "/api/cafe/inventory/counts/draft" && method === "POST") {
        const draft = body as any;
        savedSession = {
          countSessionId: draft.countSessionId,
          storeId: "33333333-3333-3333-3333-333333333333",
          state: "Draft",
          startedByUserId: ADMIN_SESSION.user.id,
          startedAtUtc: draft.startedAtUtc,
          finalizedByUserId: null,
          finalizedAtUtc: null,
          notes: draft.notes,
          finalizeOperationId: null,
          finalizedContentHash: null,
          lines: draft.lines.map((l: any) => ({
            ...l,
            nameSnapshot: "Espresso Beans",
            stockUnitSnapshot: "pc",
            quantityDeltaX1000: l.countedX1000 - l.expectedBeforeX1000,
          })),
        };
        return savedSession;
      }
      if (savedSession && path === `/api/cafe/inventory/counts/${savedSession.countSessionId}` && method === "GET") {
        return savedSession;
      }
      if (savedSession && path === `/api/cafe/inventory/counts/${savedSession.countSessionId}/finalize` && method === "POST") {
        finalizeCalls += 1;
        if (finalizeCalls === 1) {
          throw new HostApiError("Stock changed since draft capture.", 409, "CafeInventory.CountStale");
        }
        savedSession = {
          ...savedSession,
          state: "Finalized",
          finalizedByUserId: ADMIN_SESSION.user.id,
          finalizedAtUtc: (body as any).finalizedAtUtc,
          finalizeOperationId: (body as any).operationId,
        };
        return { evidence: savedSession, isReplay: false };
      }
      throw new Error(`Unhandled ${method} ${path}`);
    });

    render(<R3LiveInventoryPage />);
    await waitFor(() => {
      expect(screen.getByRole("tab", { name: "Physical Count" })).toBeTruthy();
    });

    fireEvent.click(screen.getByRole("tab", { name: "Physical Count" }));
    fireEvent.change(screen.getByLabelText("Physical Counted Quantity"), { target: { value: "98" } });
    fireEvent.click(screen.getByRole("button", { name: "Save Count Draft" }));

    await waitFor(() => {
      expect(screen.getByRole("button", { name: "Finalize Physical Count" })).toBeTruthy();
    });

    // First finalize returns deterministic 409 CafeInventory.CountStale.
    fireEvent.click(screen.getByRole("button", { name: "Finalize Physical Count" }));
    await waitFor(() => {
      expect(screen.getByText(/Finalize rejected \(CafeInventory\.CountStale\)/i)).toBeTruthy();
    });

    // Clear rejected finalize intent, then finalize cleanly -> Finalized & immutable.
    fireEvent.click(screen.getByRole("button", { name: "Clear Rejected Finalize" }));
    fireEvent.click(screen.getByRole("button", { name: "Finalize Physical Count" }));
    await waitFor(() => {
      expect(screen.getAllByText(/Physical count finalized/i).length).toBeGreaterThan(0);
    });
    expect(screen.queryByRole("button", { name: "Finalize Physical Count" })).toBeNull();
    expect(screen.getByText(/Immutable — finalized at/i)).toBeTruthy();
  });
});
