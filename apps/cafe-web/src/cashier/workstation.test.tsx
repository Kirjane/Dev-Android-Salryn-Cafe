import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import axe from "axe-core";
import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  HostApiError,
  PENDING_INTENT_STORAGE_KEY,
  PENDING_PRINT_OPS_STORAGE_KEY,
  REJECTION_HISTORY_STORAGE_KEY,
  deriveScopedOperationId,
} from "../api/hostClient";
import { classifyPrintAttemptOutcome } from "./types";
import type {
  CafeOrderDetail,
  CafeOrderSummary,
  HostSession,
  MenuPublication,
  WorkstationApi,
} from "./types";
import { WorkstationApp } from "./WorkstationApp";

const UUID_V4_REGEX = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

const SAMPLE_MENU: MenuPublication = {
  revisionId: "rev-1",
  storeId: "11111111-1111-4111-8111-111111111111",
  revisionNumber: 3,
  publishedAtUtc: "2026-11-22T01:00:00Z",
  categories: [
    { categoryId: "cat-coffee", name: "Espresso", displayOrder: 1 },
    { categoryId: "cat-pastry", name: "Pastries", displayOrder: 2 },
  ],
  items: [
    {
      productId: "22222222-2222-4222-8222-222222222221",
      categoryId: "cat-coffee",
      name: "Spanish Latte",
      description: "Double espresso + condensed milk",
      unitId: "unit-cup",
      unitName: "cup",
      sku: "ESP-01",
      barcode: null,
      sellingPriceCents: 16500,
      costPriceCents: 5200,
      isTemporarilyUnavailable: false,
      displayOrder: 1,
    },
    {
      productId: "22222222-2222-4222-8222-222222222222",
      categoryId: "cat-pastry",
      name: "Ensaymada",
      description: "Brioche pastry",
      unitId: "unit-pc",
      unitName: "pc",
      sku: "PAS-01",
      barcode: null,
      sellingPriceCents: 9500,
      costPriceCents: 3100,
      isTemporarilyUnavailable: false,
      displayOrder: 2,
    },
  ],
  variants: [
    {
      variantId: "var-hot-12",
      productId: "22222222-2222-4222-8222-222222222221",
      name: "12oz Hot",
      sku: "ESP-01-H",
      barcode: null,
      sellingPriceCents: 16500,
      costPriceCents: 5200,
      isTemporarilyUnavailable: false,
      displayOrder: 1,
    },
  ],
  modifierGroups: [
    {
      groupId: "grp-milk",
      name: "Milk Choice",
      isRequired: true,
      minSelections: 1,
      maxSelections: 1,
      displayOrder: 1,
    },
  ],
  modifierOptions: [
    { optionId: "opt-full", groupId: "grp-milk", name: "Full Cream", priceDeltaCents: 0, displayOrder: 1 },
    { optionId: "opt-oat", groupId: "grp-milk", name: "Oat Milk", priceDeltaCents: 3500, displayOrder: 2 },
  ],
  itemModifierGroups: [
    { ownerId: "22222222-2222-4222-8222-222222222221", groupId: "grp-milk", displayOrder: 1 },
  ],
  variantModifierGroups: [],
};

function createTestHostApi(options: {
  delayMs?: number;
  failNextRawIntentAsUnknown?: boolean;
  failNextRawIntentWith409?: boolean;
} = {}): WorkstationApi & { rawCalls: Array<{ path: string; body: Record<string, unknown> }> } {
  let failUnknown = Boolean(options.failNextRawIntentAsUnknown);
  let fail409 = Boolean(options.failNextRawIntentWith409);
  const rawCalls: Array<{ path: string; body: Record<string, unknown> }> = [];
  const ordersById = new Map<string, CafeOrderDetail>();
  let shiftState: "Open" | "Closed" = "Open";
  const session: HostSession = {
    isAuthenticated: true,
    user: {
      userId: "33333333-3333-4333-8333-333333333333",
      username: "cashier1",
      displayName: "Ana Cashier",
      roleName: "Cashier",
      permissions: [
        "Orders.View",
        "Orders.Create",
        "Orders.Manage",
        "Orders.Cancel",
        "Orders.Complete",
        "Sales.Create",
        "Preparation.View",
        "Preparation.Manage",
        "Receivables.View",
        "Receivables.CreateCreditSale",
        "Shift.OpenClose",
        "Printing.View",
        "Printing.Print",
      ],
    },
  };

  const api: WorkstationApi & { rawCalls: Array<{ path: string; body: Record<string, unknown> }> } = {
    rawCalls,
    getSession: async () => session,
    getLicenseStatus: async () => ({
      status: 1,
      licenseType: 0,
      applicationId: "Salryn.Cafe",
      machineMatched: true,
      machineCode: "SALRYN-CAFE-TEST-HOST",
      message: "Local Host license is valid.",
    }),
    getBootstrapStatus: async () => ({
      databaseInitialized: true,
      databaseFileExists: true,
      firstRunRequired: false,
      hasUsers: true,
      hasStoreSettings: true,
    }),
    login: async () => session,
    logout: async () => undefined,
    getPublishedMenu: async () => SAMPLE_MENU,
    getOrders: async () => Array.from(ordersById.values()).map((d) => d.order),
    getOrderDetail: async (orderId) => {
      const detail = ordersById.get(orderId);
      if (!detail) throw new HostApiError("Order not found", 404);
      return detail;
    },
    getCustomers: async () => [
      { id: "cust-1", fullName: "Maria Clara AR", isActive: true, outstandingBalanceCents: 12000 },
    ],
    getShift: async () => ({
      code: "HostOperator.Shift.Open",
      message: "Open",
      shift: { shiftState, shiftId: "shift-1", openingCashCents: 500000 },
    }),
    openShift: async (openingCashCents) => {
      shiftState = "Open";
      return {
        code: "HostOperator.Shift.Opened",
        message: "Opened",
        shift: { shiftState: "Open", shiftId: "shift-1", openingCashCents },
      };
    },
    getPrintStations: async () => [
      { stationId: "st-receipt", name: "Counter Epson", stationKind: 1, routeKey: "usb:tm20", isActive: true },
      { stationId: "st-kitchen", name: "Espresso Bar", stationKind: 2, routeKey: "usb:kitchen", isActive: true },
    ],
    printReceipt: async () => ({ job: { jobId: "j1", stationId: "s1", status: "Sent", copyKind: "Original" } }),
    printKitchenTicket: async () => ({ job: { jobId: "j2", stationId: "s2", status: "Sent", copyKind: "Original" } }),
    enrollCentral: async () => ({ deviceId: "d1", status: "PendingAdministratorApproval" }),
    synchronizeCentral: async () => ({ applied: 0, pending: 0 }),
    saveOrderRevision: async (payload) => api.sendRawIntent("/api/cafe/orders/revision", payload) as never,
    submitOrder: async (orderId, payload) => api.sendRawIntent(`/api/cafe/orders/${orderId}/submit`, payload) as never,
    setPreparationStatus: async (orderId, payload) =>
      api.sendRawIntent(`/api/cafe/orders/${orderId}/preparation`, payload) as never,
    parkOrder: async (orderId, payload) => api.sendRawIntent(`/api/cafe/orders/${orderId}/park`, payload) as never,
    resumeOrder: async (orderId, payload) => api.sendRawIntent(`/api/cafe/orders/${orderId}/resume`, payload) as never,
    cancelOrder: async (orderId, payload) => api.sendRawIntent(`/api/cafe/orders/${orderId}/cancel`, payload) as never,
    completeOrder: async (orderId, payload) =>
      api.sendRawIntent(`/api/cafe/orders/${orderId}/complete`, payload) as never,
    closeShift: async (payload) => api.sendRawIntent("/api/shift/close", payload) as never,
    sendRawIntent: async (path, body) => {
      rawCalls.push({ path, body });
      if (options.delayMs) await new Promise((r) => setTimeout(r, options.delayMs));
      if (failUnknown) {
        failUnknown = false;
        throw new Error("Simulated transient network interruption");
      }
      if (fail409) {
        fail409 = false;
        throw new HostApiError("Version conflict.", 409, "CafeOrder.ExpectedVersionConflict");
      }

      if (path === "/api/cafe/orders/revision") {
        const orderId = String(body.orderId);
        const linesInput = body.lines as Array<{
          orderLineId: string;
          productId: string;
          variantId: string | null;
          modifierOptionIds: string[];
          quantityX1000: number;
          note: string | null;
          displayOrder: number;
        }>;
        const lines = linesInput.map((l) => {
          const item = SAMPLE_MENU.items.find((i) => i.productId === l.productId)!;
          const variant = SAMPLE_MENU.variants.find((v) => v.variantId === l.variantId) ?? null;
          const mods = SAMPLE_MENU.modifierOptions.filter((m) => l.modifierOptionIds.includes(m.optionId));
          const unitPrice = (variant ? variant.sellingPriceCents : item.sellingPriceCents) + mods.reduce((s, m) => s + m.priceDeltaCents, 0);
          const qty = Math.max(1, Math.round(l.quantityX1000 / 1000));
          return {
            orderLineId: l.orderLineId,
            productId: l.productId,
            productNameSnapshot: item.name,
            unitNameSnapshot: item.unitName ?? "pc",
            quantityX1000: l.quantityX1000,
            variantId: l.variantId,
            variantNameSnapshot: variant?.name ?? null,
            modifierSelections: mods.map((m) => ({
              optionId: m.optionId,
              optionNameSnapshot: m.name,
              priceDeltaCents: m.priceDeltaCents,
            })),
            note: l.note,
            calculatedUnitPriceCents: unitPrice,
            lineTotalCents: unitPrice * qty,
            displayOrder: l.displayOrder,
          };
        });
        const summary: CafeOrderSummary = {
          orderId,
          storeId: SAMPLE_MENU.storeId,
          orderVersion: 1,
          currentRevisionId: "rev-o1",
          menuPublicationRevisionId: SAMPLE_MENU.revisionId,
          state: "Open",
          serviceMode: (body.serviceMode as CafeOrderSummary["serviceMode"]) ?? "Takeout",
          serviceReference: (body.serviceReference as string | null) ?? null,
          createdAtUtc: "2026-11-22T01:00:00Z",
          updatedAtUtc: "2026-11-22T01:00:00Z",
          completedSaleId: null,
          cancellationReason: null,
        };
        ordersById.set(orderId, {
          order: summary,
          currentRevision: {
            revisionId: "rev-o1",
            revisionNumber: 1,
            orderVersion: 1,
            serviceMode: summary.serviceMode,
            serviceReference: summary.serviceReference,
            lines,
          },
        });
        return { orderId, orderVersion: 1, state: "Open" };
      }

      const submitMatch = path.match(/^\/api\/cafe\/orders\/([^/]+)\/submit$/);
      if (submitMatch) {
        const detail = ordersById.get(submitMatch[1])!;
        detail.order = { ...detail.order, state: "Submitted", orderVersion: detail.order.orderVersion + 1 };
        return { orderId: detail.order.orderId, orderVersion: detail.order.orderVersion, state: "Submitted" };
      }

      const prepMatch = path.match(/^\/api\/cafe\/orders\/([^/]+)\/preparation$/);
      if (prepMatch) {
        const detail = ordersById.get(prepMatch[1])!;
        const target = String(body.targetState);
        detail.order = { ...detail.order, state: target, orderVersion: detail.order.orderVersion + 1 };
        return { orderId: detail.order.orderId, orderVersion: detail.order.orderVersion, state: target };
      }

      const completeMatch = path.match(/^\/api\/cafe\/orders\/([^/]+)\/complete$/);
      if (completeMatch) {
        const detail = ordersById.get(completeMatch[1])!;
        const saleId = "99999999-9999-4999-8999-999999999999";
        detail.order = {
          ...detail.order,
          state: "Completed",
          orderVersion: detail.order.orderVersion + 1,
          completedSaleId: saleId,
        };
        return { orderId: detail.order.orderId, saleId, receiptNumber: "CY-2001" };
      }

      if (path === "/api/shift/close") {
        const countedCashCents = Number(body.countedCashCents);
        const expectedCashCents = 1845000;
        shiftState = "Closed";
        return {
          code: "HostOperator.Shift.Closed",
          message: "Closed",
          shift: {
            shiftState: "Closed",
            countedCashCents,
            expectedCashCents,
            varianceCents: countedCashCents - expectedCashCents,
          },
        };
      }

      return {};
    },
  };
  return api;
}

describe("Salryn Cafe R2 — Live Host Integrated Cashier, Orders & Kitchen", () => {
  beforeEach(() => {
    window.localStorage.clear();
  });

  it("rings variant+modifier order, drives prep lifecycle, enforces Cash/AR/Partial only, and commits receipt", async () => {
    const user = userEvent.setup();
    const api = createTestHostApi();
    render(<WorkstationApp api={api} />);

    await waitFor(() => expect(screen.getByRole("button", { name: /Spanish Latte/i })).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: /Spanish Latte/i }));

    const dialog = screen.getByRole("dialog", { name: "Configure Spanish Latte" });
    await user.click(within(dialog).getByLabelText(/Oat Milk/i));
    await user.click(within(dialog).getByRole("button", { name: "Add to Ticket" }));

    expect(screen.getAllByText("₱200.00").length).toBeGreaterThanOrEqual(1);

    const modeSelect = screen.getByLabelText("Service mode") as HTMLSelectElement;
    expect(Array.from(modeSelect.options).map((o) => o.value)).toEqual(["Takeout", "DineIn"]);

    await user.click(screen.getByRole("button", { name: "Save order" }));
    expect(await screen.findByText("Current order · Open")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Send to preparation" }));
    expect(await screen.findByText("Current order · Submitted")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Mark preparing" }));
    expect(await screen.findByText("Current order · Preparing")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Mark ready" }));
    expect(await screen.findByText("Current order · Ready")).toBeInTheDocument();

    const tenderSelect = screen.getByLabelText("Tender") as HTMLSelectElement;
    expect(Array.from(tenderSelect.options).map((o) => o.value)).toEqual([
      "Cash",
      "AccountsReceivable",
      "Partial",
    ]);

    await user.selectOptions(tenderSelect, "Partial");
    await user.clear(screen.getByLabelText("Cash amount (PHP)"));
    await user.type(screen.getByLabelText("Cash amount (PHP)"), "120");
    await user.selectOptions(screen.getByLabelText(/Customer/i), "cust-1");
    await user.click(screen.getByRole("button", { name: "Complete payment" }));

    expect(await screen.findByText(/Payment confirmed · Receipt CY-2001/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Print Receipt" })).toBeInTheDocument();
  });


  it("shows amount due, insufficient cash, and change before saving a Cash sale", async () => {
    const user = userEvent.setup();
    const api = createTestHostApi();
    render(<WorkstationApp api={api} />);
    await waitFor(() => expect(screen.getByRole("button", { name: /Spanish Latte/i })).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: /Spanish Latte/i }));
    const dialog = screen.getByRole("dialog", { name: "Configure Spanish Latte" });
    await user.click(within(dialog).getByRole("button", { name: "Add to Ticket" }));
    await user.click(screen.getByRole("button", { name: "Save order" }));
    expect(await screen.findByText("Current order · Open")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Send to preparation" }));
    expect(await screen.findByText("Current order · Submitted")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Mark preparing" }));
    expect(await screen.findByText("Current order · Preparing")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Mark ready" }));
    expect(await screen.findByText("Current order · Ready")).toBeInTheDocument();

    const complete = screen.getByRole("button", { name: "Complete payment" });
    expect(screen.getByText("Estimated due (before fees)")).toBeInTheDocument();
    expect(screen.getByText("Still to collect")).toBeInTheDocument();
    expect(complete).toBeDisabled();

    await user.clear(screen.getByLabelText("Cash amount (PHP)"));
    await user.type(screen.getByLabelText("Cash amount (PHP)"), "500");
    expect(screen.getByText("Estimated change (before fees)")).toBeInTheDocument();
    expect(complete).toBeEnabled();
  });

  it("retains exact RFC-4122 completionOperationId in localStorage on unknown outcome and replays identical payload after reload", async () => {
    const user = userEvent.setup();
    const api = createTestHostApi();
    const { unmount } = render(<WorkstationApp api={api} />);

    await waitFor(() => expect(screen.getByRole("button", { name: /Ensaymada/i })).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: /Ensaymada/i }));
    const modeSelect = screen.getByLabelText("Service mode") as HTMLSelectElement;
    expect(Array.from(modeSelect.options).map((o) => o.value)).toEqual(["Takeout", "DineIn"]);

    await user.click(screen.getByRole("button", { name: "Save order" }));
    await user.click(await screen.findByRole("button", { name: "Send to preparation" }));
    await user.click(await screen.findByRole("button", { name: "Mark preparing" }));
    await user.click(await screen.findByRole("button", { name: "Mark ready" }));
    expect(await screen.findByText("Current order · Ready")).toBeInTheDocument();

    await user.clear(screen.getByLabelText("Cash amount (PHP)"));
    await user.type(screen.getByLabelText("Cash amount (PHP)"), "95");

    // Simulate network drop on the /complete call only
    const originalSend = api.sendRawIntent;
    let dropOnce = true;
    api.sendRawIntent = async (path, body) => {
      if (dropOnce && path.endsWith("/complete")) {
        dropOnce = false;
        api.rawCalls.push({ path, body });
        throw new Error("Connection lost");
      }
      return originalSend(path, body);
    };

    await user.click(screen.getByRole("button", { name: "Complete payment" }));
    expect(await screen.findByRole("button", { name: "Retry retained request" })).toBeInTheDocument();

    const stored = JSON.parse(window.localStorage.getItem(PENDING_INTENT_STORAGE_KEY) ?? "{}") as {
      body: { completionOperationId: string };
    };
    expect(stored.body.completionOperationId).toMatch(UUID_V4_REGEX);

    // Remount app cleanly (simulating browser reload) and retry
    unmount();
    render(<WorkstationApp api={api} />);
    await user.click(await screen.findByRole("button", { name: "Retry retained request" }));
    expect(await screen.findByText(/Payment confirmed · Receipt CY-2001/i)).toBeInTheDocument();

    const completeCalls = api.rawCalls.filter((c) => c.path.endsWith("/complete"));
    expect(completeCalls).toHaveLength(2);
    expect(completeCalls[0].body.completionOperationId).toBe(completeCalls[1].body.completionOperationId);
    expect(window.localStorage.getItem(PENDING_INTENT_STORAGE_KEY)).toBeNull();
  });

  it("blocks rapid synchronous double-click on Complete payment via ref lock", async () => {
    const user = userEvent.setup();
    const api = createTestHostApi({ delayMs: 40 });
    const spy = vi.spyOn(api, "sendRawIntent");
    render(<WorkstationApp api={api} />);

    await waitFor(() => expect(screen.getByRole("button", { name: /Ensaymada/i })).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: /Ensaymada/i }));
    const modeSelect = screen.getByLabelText("Service mode") as HTMLSelectElement;
    expect(Array.from(modeSelect.options).map((o) => o.value)).toEqual(["Takeout", "DineIn"]);

    await user.click(screen.getByRole("button", { name: "Save order" }));
    await user.click(await screen.findByRole("button", { name: "Send to preparation" }));
    await user.click(await screen.findByRole("button", { name: "Mark preparing" }));
    await user.click(await screen.findByRole("button", { name: "Mark ready" }));
    expect(await screen.findByText("Current order · Ready")).toBeInTheDocument();

    await user.clear(screen.getByLabelText("Cash amount (PHP)"));
    await user.type(screen.getByLabelText("Cash amount (PHP)"), "95");

    const callsBefore = spy.mock.calls.length;
    const submitBtn = screen.getByRole("button", { name: "Complete payment" });
    act(() => {
      submitBtn.click();
      submitBtn.click();
    });

    expect(await screen.findByText(/Payment confirmed · Receipt CY-2001/i)).toBeInTheDocument();
    expect(spy.mock.calls.length - callsBefore).toBe(1);
  });

  it("archives deterministically rejected 409 intent only with audit reason", async () => {
    const user = userEvent.setup();
    const api = createTestHostApi({ failNextRawIntentWith409: true });
    render(<WorkstationApp api={api} />);

    await waitFor(() => expect(screen.getByRole("button", { name: /Ensaymada/i })).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: /Ensaymada/i }));
    const modeSelect = screen.getByLabelText("Service mode") as HTMLSelectElement;
    expect(Array.from(modeSelect.options).map((o) => o.value)).toEqual(["Takeout", "DineIn"]);

    await user.click(screen.getByRole("button", { name: "Save order" }));

    const archiveBtn = await screen.findByRole("button", { name: "Archive rejected request" });
    expect(archiveBtn).toBeDisabled();
    // Draft ticket items MUST NOT be wiped when saving fails!
    expect(screen.getByText("1 items")).toBeInTheDocument();

    await user.type(screen.getByLabelText(/Reason to archive rejected request/i), "Order version changed by manager");
    expect(archiveBtn).toBeEnabled();
    await user.click(archiveBtn);

    expect(screen.queryByRole("button", { name: "Archive rejected request" })).not.toBeInTheDocument();
    const history = JSON.parse(window.localStorage.getItem(REJECTION_HISTORY_STORAGE_KEY) ?? "[]");
    expect(history).toHaveLength(1);
  });

  it("retains 409 ERR_IDEMPOTENCY_OUTCOME_UNKNOWN for retry without enabling Archive rejected request", async () => {
    const user = userEvent.setup();
    const api = createTestHostApi();
    const originalSend = api.sendRawIntent;
    let returnOutcomeUnknownOnce = true;
    api.sendRawIntent = async (path, body) => {
      if (returnOutcomeUnknownOnce && path === "/api/cafe/orders/revision") {
        returnOutcomeUnknownOnce = false;
        api.rawCalls.push({ path, body });
        throw new HostApiError(
          "Operation is executing or its committed outcome needs reconciliation.",
          409,
          "ERR_IDEMPOTENCY_OUTCOME_UNKNOWN",
        );
      }
      return originalSend(path, body);
    };

    render(<WorkstationApp api={api} />);
    await waitFor(() => expect(screen.getByRole("button", { name: /Ensaymada/i })).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: /Ensaymada/i }));
    const modeSelect = screen.getByLabelText("Service mode") as HTMLSelectElement;
    expect(Array.from(modeSelect.options).map((o) => o.value)).toEqual(["Takeout", "DineIn"]);

    await user.click(screen.getByRole("button", { name: "Save order" }));

    expect(await screen.findByRole("button", { name: "Retry retained request" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Archive rejected request" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Retry retained request" }));
    expect(await screen.findByText("Current order · Open")).toBeInTheDocument();
  });

  it("enforces Blind Shift Close without exposing expected cash beforehand and handles invalid counts cleanly", async () => {
    const user = userEvent.setup();
    const api = createTestHostApi();
    const { container } = render(<WorkstationApp api={api} />);

    await waitFor(() => expect(screen.getByRole("button", { name: "Blind Shift Close" })).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Blind Shift Close" }));

    const dialog = screen.getByRole("dialog", { name: "Blind Shift Close" });
    const countInput = within(dialog).getByLabelText(/Counted Drawer Cash \(PHP\)/i);
    expect(within(dialog).queryByText(/18,450/)).not.toBeInTheDocument();
    expect(countInput).toHaveAttribute("placeholder", "0.00");

    await user.type(countInput, "-12.5");
    await user.click(within(dialog).getByRole("button", { name: "Submit Blind Count" }));
    expect(await within(dialog).findByText(/Invalid Cash Count/i)).toBeInTheDocument();

    await user.clear(countInput);
    await user.type(countInput, "18400");
    await user.click(within(dialog).getByRole("button", { name: "Submit Blind Count" }));

    expect(await within(dialog).findByText(/Shift Variance Computed by Server/i)).toBeInTheDocument();
    expect(within(dialog).getByText(/-₱50\.00/)).toBeInTheDocument();

    const audit = await axe.run(container, { rules: { "color-contrast": { enabled: false } } });
    expect(audit.violations).toEqual([]);
  });

  it("derives stable distinct RFC-4122 operationIds per scope for print attempts and publish retries", () => {
    const base = "12345678-1234-4123-8123-123456789abc";
    const kitchenAttempt1 = deriveScopedOperationId(base, "kitchen-attempt");
    const kitchenAttempt2 = deriveScopedOperationId(base, "kitchen-attempt");
    const receiptAttempt = deriveScopedOperationId(base, "receipt-attempt");
    const menuPublish = deriveScopedOperationId(base, "menu-publish");
    const recipePublish = deriveScopedOperationId(base, "recipes-publish");

    const uuidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-5[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
    expect(kitchenAttempt1).toMatch(uuidRegex);
    expect(kitchenAttempt1).toBe(kitchenAttempt2);
    expect(new Set([base, kitchenAttempt1, receiptAttempt, menuPublish, recipePublish]).size).toBe(5);
  });

  it("classifies print attempt outcomes truthfully and clears pending print op on deterministic 4xx rejection", async () => {
    expect(classifyPrintAttemptOutcome({ job: { jobId: "j1", stationId: "s1" }, attempt: { status: 1, resultMessage: "Printed" } }).outcome).toBe("succeeded");
    expect(classifyPrintAttemptOutcome({ job: { jobId: "j1", stationId: "s1" }, attempt: { status: 3, resultMessage: "Printer disabled" } }).outcome).toBe("disabled");
    expect(classifyPrintAttemptOutcome({ job: { jobId: "j1", stationId: "s1" }, attempt: { status: 2, errorMessage: "Paper out" } }).outcome).toBe("failed");

    const user = userEvent.setup();
    const api = createTestHostApi();
    const seenOpIds: string[] = [];
    let callCount = 0;
    api.printKitchenTicket = async (req) => {
      seenOpIds.push(req.operationId);
      callCount++;
      if (callCount === 1) {
        throw new HostApiError("Station inactive", 409, "CafePrint.StationInactive");
      }
      return {
        job: { jobId: "job-k2", stationId: req.stationId, status: "Recorded", copyKind: req.copyKind },
        attempt: { status: 3, resultCode: "ReceiptPrinter.Disabled", resultMessage: "Printer mode is Disabled." },
      };
    };

    render(<WorkstationApp api={api} />);
    await waitFor(() => expect(screen.getByRole("button", { name: /Ensaymada/i })).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: /Ensaymada/i }));
    await user.click(screen.getByRole("button", { name: "Save order" }));
    await user.click(await screen.findByRole("button", { name: "Send to preparation" }));
    await user.click(screen.getByRole("button", { name: /Kitchen & Barista/i }));

    const printBtn = await screen.findByRole("button", { name: "Print Kitchen Ticket" });
    await user.click(printBtn);
    expect(await screen.findByText(/Station inactive/i)).toBeInTheDocument();
    // Deterministic 409 must clear the stored print op so retry uses a fresh operationId
    expect(window.localStorage.getItem(PENDING_PRINT_OPS_STORAGE_KEY)).toBeNull();

    await user.click(printBtn);
    expect(await screen.findByText(/Printer disabled · Espresso Bar/i)).toBeInTheDocument();
    expect(screen.queryByText(/Kitchen ticket sent/i)).not.toBeInTheDocument();
    expect(seenOpIds).toHaveLength(2);
    expect(seenOpIds[0]).not.toBe(seenOpIds[1]);
  });

  it("hides first-admin and sign-in completely while unlicensed, then unlocks first-admin after valid Host license install", async () => {
    const user = userEvent.setup();
    const api = createTestHostApi();
    let licensed = false;
    let firstRunRequired = true;
    api.getSession = async () => ({ isAuthenticated: false });
    api.getBootstrapStatus = async () => ({
      databaseInitialized: true,
      databaseFileExists: true,
      firstRunRequired,
      hasUsers: !firstRunRequired,
      hasStoreSettings: !firstRunRequired,
    });
    api.getLicenseStatus = async () => ({
      status: licensed ? 1 : 0,
      licenseType: licensed ? 0 : null,
      applicationId: licensed ? "Salryn.Cafe" : null,
      machineMatched: licensed,
      machineCode: "SALRYN-CAFE-TEST-MACHINE",
      message: licensed ? "Local Host license is valid." : "No local license file was found. Manual activation is required.",
    });
    api.installLicense = async () => {
      licensed = true;
      return { installStatus: "Installed" };
    };
    const createSpy = vi.fn(async () => {
      firstRunRequired = false;
      return {
        adminUserId: "admin-1",
        status: {
          databaseInitialized: true,
          databaseFileExists: true,
          firstRunRequired: false,
          hasUsers: true,
          hasStoreSettings: true,
        },
      };
    });
    api.createFirstAdmin = createSpy;

    render(<WorkstationApp api={api} />);
    expect(await screen.findByText("Host License Activation Required")).toBeInTheDocument();
    expect(screen.getByTestId("workstation-machine-code")).toHaveTextContent("SALRYN-CAFE-TEST-MACHINE");

    // Issue #38 core invariant: NO admin form, NO sign-in form, NO operational nav before activation!
    expect(screen.queryByText(/Create First Administrator/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/Store Name/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/Admin Username/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/Admin Display Name/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/Admin Password/i)).not.toBeInTheDocument();
    expect(screen.queryByText("Operator sign in")).not.toBeInTheDocument();
    expect(screen.queryByRole("navigation")).not.toBeInTheDocument();

    const licenseField = screen.getByLabelText(/Signed Host License JSON/i);
    await user.click(licenseField);
    await user.paste('{"applicationId":"Salryn.Cafe","licenseId":"test"}');
    await user.click(screen.getByRole("button", { name: "Install Signed License" }));

    // After valid Host license install: Create First Administrator appears cleanly
    expect(await screen.findByRole("button", { name: "Create First Administrator" })).toBeEnabled();
    expect(screen.queryByText("Host License Activation Required")).not.toBeInTheDocument();

    await user.type(screen.getByLabelText(/Admin Password/i), "Synthetic-FirstRun!2026");
    await user.click(screen.getByRole("button", { name: "Create First Administrator" }));

    expect(createSpy).toHaveBeenCalledTimes(1);
    expect(await screen.findByText("Operator sign in")).toBeInTheDocument();
  });

  it("fails closed to activation-only or error screen on expired, invalid, wrong-machine, wrong-product, Terminal, or unreachable Host", async () => {
    const scenarios = [
      {
        name: "expired",
        lic: { status: 2, licenseType: 0, applicationId: "Salryn.Cafe", machineMatched: true, machineCode: "MC-1", message: "Local license is expired." },
        badge: "License Expired",
      },
      {
        name: "wrong-machine",
        lic: { status: 3, licenseType: 0, applicationId: "Salryn.Cafe", machineMatched: false, machineCode: "MC-2", message: "Machine mismatch." },
        badge: "Invalid / Unmatched License",
      },
      {
        name: "wrong-product",
        lic: { status: 1, licenseType: 0, applicationId: "Salryn.POS", machineMatched: true, machineCode: "MC-3", message: "Wrong product." },
        badge: "Wrong Product License",
      },
      {
        name: "terminal-type",
        lic: { status: 1, licenseType: 1, applicationId: "Salryn.Cafe", machineMatched: true, machineCode: "MC-4", message: "Terminal license does not unlock Host." },
        badge: "Host License Required",
      },
      {
        name: "grace-period",
        lic: { status: 4, licenseType: 0, applicationId: "Salryn.Cafe", machineMatched: true, machineCode: "MC-5", message: "License is in grace period and requires renewal." },
        badge: "Renewal Required",
      },
      {
        name: "missing-application-id",
        lic: { status: 1, licenseType: 0, applicationId: "", machineMatched: true, machineCode: "MC-6", message: "Missing product identifier." },
        badge: "License Required",
      },
    ];

    for (const sc of scenarios) {
      const api = createTestHostApi();
      api.getSession = async () => ({ isAuthenticated: false });
      api.getLicenseStatus = async () => sc.lic;
      const { unmount } = render(<WorkstationApp api={api} />);
      expect(await screen.findByText(sc.badge)).toBeInTheDocument();
      expect(screen.queryByText(/Create First Administrator/i)).not.toBeInTheDocument();
      expect(screen.queryByText("Operator sign in")).not.toBeInTheDocument();
      expect(screen.queryByRole("navigation")).not.toBeInTheDocument();
      unmount();
    }

    // Host license API error must also fail closed with zero admin/sign-in forms
    const errApi = createTestHostApi();
    errApi.getLicenseStatus = async () => {
      throw new Error("Host offline");
    };
    render(<WorkstationApp api={errApi} />);
    expect(await screen.findByText("Host License Verification Unavailable")).toBeInTheDocument();
    expect(screen.queryByText(/Create First Administrator/i)).not.toBeInTheDocument();
    expect(screen.queryByText("Operator sign in")).not.toBeInTheDocument();
  });

  it("drops an active authenticated session back to activation-only when license expires on Refresh from Host", async () => {
    const user = userEvent.setup();
    let expired = false;
    const api = createTestHostApi();
    api.getLicenseStatus = async () =>
      expired
        ? {
            status: 2,
            licenseType: 0,
            applicationId: "Salryn.Cafe",
            machineMatched: true,
            machineCode: "MC-RUNTIME-EXP",
            message: "Local license is expired.",
          }
        : {
            status: 1,
            licenseType: 0,
            applicationId: "Salryn.Cafe",
            machineMatched: true,
            machineCode: "MC-RUNTIME-EXP",
            message: "Valid license.",
          };

    render(<WorkstationApp api={api} />);
    expect(await screen.findByRole("button", { name: "Refresh from Host" })).toBeInTheDocument();

    expired = true;
    await user.click(screen.getByRole("button", { name: "Refresh from Host" }));

    expect(await screen.findByText("License Expired")).toBeInTheDocument();
    expect(screen.queryByRole("navigation")).not.toBeInTheDocument();
    expect(screen.queryByText(/Create First Administrator/i)).not.toBeInTheDocument();
  });

  it("shows Operator sign in (not first-admin setup) when licensed and users already exist with empty store_settings", async () => {
    const api = createTestHostApi();
    api.getSession = async () => ({ isAuthenticated: false });
    api.getBootstrapStatus = async () => ({
      databaseInitialized: true,
      databaseFileExists: true,
      firstRunRequired: true,
      hasUsers: true,
      hasStoreSettings: false,
    });

    render(<WorkstationApp api={api} />);
    expect(await screen.findByText("Operator sign in")).toBeInTheDocument();
    expect(screen.queryByText(/Create First Administrator/i)).not.toBeInTheDocument();
  });
});
