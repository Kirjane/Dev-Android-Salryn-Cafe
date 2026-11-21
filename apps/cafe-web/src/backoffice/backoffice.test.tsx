import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import axe from "axe-core";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { HostApiError } from "../api/hostClient";
import { BackofficeConsoleApp } from "./BackofficeConsoleApp";
import type {
  BackofficeApi,
  BackupPackageRow,
  CafeRefundSummaryRow,
  CentralAdminSession,
  CentralDeviceRow,
  CentralOwnerApi,
  OwnerReportsSnapshot,
} from "./types";

const UUID_V4_REGEX = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

function makeReports(): OwnerReportsSnapshot {
  return {
    generatedAt: "2026-11-22T05:00:00Z",
    officialBoundary: "Live SQLite Host Authority (C1-C11)",
    daily: { salesCount: 18, salesAmountCents: 145000, averageSaleCents: 8055 },
    weekly: { salesCount: 92, salesAmountCents: 740000, averageSaleCents: 8043 },
    monthly: { salesCount: 310, salesAmountCents: 2480000, averageSaleCents: 8000 },
    receivables: { outstandingCents: 120000, collectionsCents: 45000, overdueCents: 20000 },
    lowStock: [],
    cafeManagement: {
      costCoverage: { state: "Complete", revenueSaleCount: 310, costCompleteSaleCount: 310, costIncompleteSaleCount: 0 },
      profitability: {
        recognizedNetRevenueCents: 2480000,
        historicalCogsCents: 740000,
        grossProfitCents: 1740000,
        operationalExpenseCents: 120000,
        recognizedWasteCostCents: 15000,
        operationalContributionCents: 1605000,
      },
      recipeCosts: [
        { recipeId: "rec-1", recipeName: "Spanish Latte (Iced)", yieldX1000: 1000, knownCostCents: 3800, costCoverage: "Complete" },
      ],
    },
  };
}

function createFakeBackofficeHarness() {
  const refunds: CafeRefundSummaryRow[] = [];
  const backups: BackupPackageRow[] = [
    { backupId: "bkp-20261122-010000", createdAt: "2026-11-22T01:00:00Z", sizeBytes: 51200 },
  ];
  let arOutstanding = 120000;

  const collectArCash = vi.fn(async (req: { clientRequestId: string; customerId: string; amountCents: number }) => {
    arOutstanding = Math.max(0, arOutstanding - req.amountCents);
  });
  const requestRefund = vi.fn(async (req: { operationId: string; saleId: string; reason: string }) => {
    const row: CafeRefundSummaryRow = {
      refundId: "ref-100",
      saleId: req.saleId,
      receiptNumber: "RC-1001",
      version: 1,
      state: "Requested",
      reason: req.reason,
      requestedAtUtc: "2026-11-22T05:10:00Z",
      resolutionReason: null,
      totalRefundCents: 18500,
    };
    refunds.splice(0, refunds.length, row);
    return { refundId: row.refundId, version: 1, state: "Requested" };
  });
  const resolveRefund = vi.fn(async (req: { refundId: string; approve: boolean }) => {
    const target = refunds.find((r) => r.refundId === req.refundId)!;
    target.version += 1;
    target.state = req.approve ? "Approved" : "Rejected";
    return { refundId: target.refundId, version: target.version, state: target.state };
  });
  const commitRefund = vi.fn(async (req: { refundId: string }) => {
    const target = refunds.find((r) => r.refundId === req.refundId)!;
    target.version += 1;
    target.state = "Committed";
    return { refundId: target.refundId, version: target.version, state: "Committed", totalRefundCents: 18500 };
  });
  const recordWaste = vi.fn(async () => undefined);
  const verifyBackup = vi.fn(async () => ({ valid: true, summary: "Manifest v40 verified" }));
  const restoreBackup = vi.fn(async () => undefined);

  const api: BackofficeApi = {
    getOwnerReports: async () => makeReports(),
    getIngredientsAndRecipes: async () => ({
      ingredients: [
        { id: "ing-beans", name: "Espresso Roast Beans", stockUnit: "Gram", stockOnHandX1000: 5000000, reorderLevelX1000: 1000000, isActive: true, notes: null },
      ],
      recipes: [
        { id: "rec-1", name: "Spanish Latte", yieldX1000: 1000, isActive: true },
      ],
      lines: [{ recipeId: "rec-1", ingredientId: "ing-beans", inputUnit: "Gram", declaredQuantityX1000: 18000, normalizedQuantityX1000: 18000, displayOrder: 1 }],
    }),
    publishMenuAndRecipes: async () => undefined,
    getLowStock: async () => [],
    getSuppliers: async () => [{ id: "sup-1", name: "Bukidnon Roasters", isActive: true }],
    receivePurchase: async () => undefined,
    recordWaste,
    getReceivableCustomers: async () => [
      {
        customer: { id: "cust-1", fullName: "Ana Santos", phone: "09171234567", creditLimitCents: 500000, isActive: true },
        outstandingBalanceCents: arOutstanding,
        overdueBalanceCents: 0,
      },
    ],
    collectArCash,
    getCashMovements: async () => [
      { movementId: "cm-1", movementType: 3, deltaCents: 50000, reason: "Shift float", referenceType: "Manual", createdAtUtc: "2026-11-22T02:00:00Z" },
      { movementId: "cm-2", movementType: 5, deltaCents: -12000, reason: "Ice delivery", referenceType: "Manual", createdAtUtc: "2026-11-22T02:30:00Z" },
    ],
    postCashMovement: async () => undefined,
    getRefunds: async () => refunds.map((r) => ({ ...r })),
    getRefundableSales: async () => [
      {
        saleId: "sale-1",
        receiptNumber: "RC-1001",
        createdAtUtc: "2026-11-22T04:00:00Z",
        totalCents: 18500,
        cashNetCents: 18500,
        arCents: 0,
        items: [
          {
            saleItemId: "si-1",
            productId: "prod-1",
            productName: "Spanish Latte (Iced)",
            quantityX1000: 1000,
            remainingQuantityX1000: 1000,
            unitPriceCents: 18500,
            lineTotalCents: 18500,
            trackStock: false,
          },
        ],
      },
    ],
    requestRefund,
    resolveRefund,
    commitRefund,
    getBookings: async () => [],
    advanceBookingStatus: async () => undefined,
    getDeliveries: async () => [],
    advanceDeliveryStatus: async () => undefined,
    getAttendance: async () => [],
    recordAttendance: async () => undefined,
    getIncidents: async () => [],
    createIncident: async () => undefined,
    getAuditLogs: async () => [],
    getUsers: async () => [],
    getUserRoles: async () => [{ roleId: "role-cashier", roleName: "Cashier", isActive: true }],
    createUser: async (req) => ({ id: "u-2", username: req.username, displayName: req.displayName, roleName: "Cashier", status: "Active" }),
    getStoreSettings: async () => ({
      id: "store-1",
      storeName: "Salryn Cafe Main",
      businessName: "Salryn Cafe Yza",
      address: "Bacolod City",
      contactNumber: "09170000000",
      tin: "000-111-222",
      currencyLabel: "PHP",
      receiptWidthMm: 58,
      receiptHeader: "Welcome",
      receiptFooter: "Thank you!",
      printerMode: "Bluetooth",
      windowsPrinterName: null,
    }),
    updateStoreSettings: async () => undefined,
    getBackups: async () => backups.map((b) => ({ ...b })),
    createBackup: async () => {
      const next: BackupPackageRow = { backupId: "bkp-20261122-052000", createdAt: "2026-11-22T05:20:00Z", sizeBytes: 61440 };
      backups.unshift(next);
      return next;
    },
    verifyBackup,
    restoreBackup,
    getLicenseStatus: async () => ({
      status: 1,
      edition: 2,
      machineMatched: true,
      message: "Signed Salryn.Cafe license active.",
      enabledFeatures: ["Pos", "Backup", "LanTerminals"],
      expiresAt: "2027-11-22T00:00:00Z",
    }),
    getLanStatus: async () => ({
      remoteTransportEnabled: true,
      remotePosRuntimeEnabled: true,
      bindMode: "LanExplicit",
      configuredBindAddress: "192.168.1.10",
      hostLocalUrl: "http://127.0.0.1:5399",
      hostLanUrl: "https://192.168.1.10:5400",
    }),
    getLanPairing: async () => ({
      joinCodeIssued: true,
      joinCodeExpiresAtUtc: "2026-11-22T06:00:00Z",
      pendingRequestCount: 0,
      pendingRequests: [],
      message: "Join code active.",
      activeJoinCode: "481920",
    }),
    rotateLanJoinCode: async () => ({
      joinCodeIssued: true,
      joinCodeExpiresAtUtc: "2026-11-22T06:10:00Z",
      pendingRequestCount: 0,
      pendingRequests: [],
      message: "Rotated.",
      activeJoinCode: "992104",
    }),
  };

  return { api, collectArCash, requestRefund, resolveRefund, commitRefund, recordWaste, verifyBackup, restoreBackup };
}

function createFakeCentralHarness() {
  const devices: CentralDeviceRow[] = [
    {
      deviceId: "11111111-1111-4111-8111-111111111111",
      tenantId: "t-1",
      storeId: "s-1",
      productId: "Salryn.Cafe",
      deviceName: "Counter Tablet 01",
      deviceRole: "Cashier",
      publicKeyThumbprint: "sha256:abcdef1234567890",
      status: "PendingApproval",
      enrolledAtUtc: "2026-11-22T04:00:00Z",
      approvedAtUtc: null,
      revokedAtUtc: null,
      revokeReason: null,
      lastSeenAtUtc: null,
    },
  ];

  const centralApi: CentralOwnerApi = {
    getHealth: async () => ({
      status: "Healthy",
      productId: "Salryn.Cafe",
      centralInstanceId: "central-1",
      serverEpoch: 3,
      headSequence: 12,
      schemaVersion: 1,
      timestampUtc: "2026-11-22T05:00:00Z",
    }),
    login: async (_baseUrl, username): Promise<CentralAdminSession> => ({
      accessToken: "central-jwt-1",
      session: {
        adminId: "adm-1",
        tenantId: "t-1",
        storeId: "s-1",
        productId: "Salryn.Cafe",
        username,
        displayName: "Cafe Owner",
        role: "Owner",
        expiresAtUtc: "2026-11-22T12:00:00Z",
      },
    }),
    logout: async () => undefined,
    listDevices: async () => devices.map((d) => ({ ...d })),
    createInvite: async (_b, _t, deviceRole) => ({
      inviteId: "inv-1",
      joinCode: "654321",
      deviceRole,
      expiresAtUtc: "2026-11-22T05:30:00Z",
    }),
    approveDevice: async (_b, _t, deviceId) => {
      const target = devices.find((d) => d.deviceId === deviceId)!;
      target.status = "Active";
      target.approvedAtUtc = "2026-11-22T05:05:00Z";
      return { ...target };
    },
    revokeDevice: async (_b, _t, deviceId) => {
      const target = devices.find((d) => d.deviceId === deviceId)!;
      target.status = "Revoked";
      target.revokedAtUtc = "2026-11-22T05:10:00Z";
      return { ...target };
    },
    listQuarantine: async () => [],
    reviewQuarantine: async () => ({
      quarantineId: "q-1",
      deviceId: "d-1",
      operationId: "op-1",
      operationType: "Sale",
      receivedAtUtc: "2026-11-22T05:00:00Z",
      reviewStatus: "Approved",
      reviewNote: null,
    }),
  };

  return { centralApi, devices };
}

describe("Salryn Cafe Back-Office & Central Owner Console (Live Contracts)", () => {
  beforeEach(() => {
    window.localStorage.clear();
  });

  it("renders live Owner Reports & COGS and strictly gates Host Backup/License on Central Owner Portal", async () => {
    const user = userEvent.setup();
    const { api } = createFakeBackofficeHarness();
    const { centralApi } = createFakeCentralHarness();

    render(<BackofficeConsoleApp api={api} centralApi={centralApi} initialSurface="CENTRAL_OWNER" />);

    expect(await screen.findByText("₱24,800.00")).toBeInTheDocument();
    expect(screen.getByRole("table", { name: "Recipe Cost Coverage" })).toBeInTheDocument();

    // Navigate to Windows Host Only tab in CENTRAL_OWNER -> MUST be disabled
    await user.click(screen.getByRole("button", { name: "Host Backup & License" }));
    expect(screen.getByRole("alert")).toHaveTextContent(/Local Windows Host Only/i);

    // Switch to Windows Host View -> Host controls + numeric enum license badge unlock
    await user.click(screen.getByRole("button", { name: "Windows Host Console" }));
    expect(screen.getByText("Licensed")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Create Verified Backup/i })).toBeInTheDocument();
  });

  it("executes Central device enrollment invite, approval, and handles ambiguous revoke failure with UnknownOutcome retry", async () => {
    const user = userEvent.setup();
    const { api } = createFakeBackofficeHarness();
    const { centralApi } = createFakeCentralHarness();

    let revokeAttempts = 0;
    const revokeSpy = vi.fn(async (_b: string, _t: string, deviceId: string) => {
      revokeAttempts += 1;
      if (revokeAttempts === 1) {
        throw new HostApiError("Gateway timeout during revocation.", 504);
      }
      return {
        deviceId,
        tenantId: "t-1",
        storeId: "s-1",
        productId: "Salryn.Cafe",
        deviceName: "Counter Tablet 01",
        deviceRole: "Cashier",
        publicKeyThumbprint: "sha256:abcdef1234567890",
        status: "Revoked",
        enrolledAtUtc: "2026-11-22T04:00:00Z",
        approvedAtUtc: "2026-11-22T05:05:00Z",
        revokedAtUtc: "2026-11-22T05:10:00Z",
        revokeReason: "Revoked",
        lastSeenAtUtc: null,
      };
    });
    centralApi.revokeDevice = revokeSpy;

    render(<BackofficeConsoleApp api={api} centralApi={centralApi} initialSurface="CENTRAL_OWNER" initialNav="devices" />);

    await user.type(screen.getByLabelText(/Central HTTPS Server URL/i), "https://central.salryncafe.test");
    await user.type(screen.getByLabelText(/Central Admin Username/i), "owner");
    await user.type(screen.getByLabelText(/Central Admin Password/i), "Owner!2026");
    await user.click(screen.getByRole("button", { name: /Sign In to Central/i }));

    expect(await screen.findByRole("button", { name: /Issue Join Code/i })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /Issue Join Code/i }));
    expect(await screen.findByText(/Active Join Code: 654321/i)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /^Approve$/i }));
    await waitFor(() => expect(screen.getByRole("button", { name: /^Revoke$/i })).toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: /^Revoke$/i }));
    await user.click(screen.getByRole("button", { name: /Revoke device/i }));

    const retryBtn = await screen.findByRole("button", { name: /Retry & Confirm Revocation/i });
    await user.click(retryBtn);

    await waitFor(() => expect(revokeSpy).toHaveBeenCalledTimes(2));
  });

  it("posts Cash-only AR collection and executes C5 Refund Request (canonical stockDisposition) -> Approve -> Cash Commit with exact operationId retry", async () => {
    const user = userEvent.setup();
    const { api, collectArCash, requestRefund, resolveRefund, commitRefund, recordWaste } = createFakeBackofficeHarness();
    const { centralApi } = createFakeCentralHarness();

    let commitAttempts = 0;
    const commitSpy = vi.fn(async (req: Parameters<BackofficeApi["commitRefund"]>[0]) => {
      commitAttempts += 1;
      if (commitAttempts === 1) {
        throw new HostApiError("Unknown commit outcome.", 504, "ERR_IDEMPOTENCY_OUTCOME_UNKNOWN");
      }
      return commitRefund(req);
    });
    api.commitRefund = commitSpy;

    render(<BackofficeConsoleApp api={api} centralApi={centralApi} initialNav="inventory" />);
    expect(await screen.findByRole("button", { name: /Record Waste/i })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /Record Waste/i }));
    await waitFor(() => expect(recordWaste).toHaveBeenCalledWith(expect.objectContaining({
      stockTargetId: "ing-beans",
      quantityWastedX1000: 1000,
      reason: "Spoilage",
    })));

    await user.click(screen.getByRole("button", { name: /Cash, Expense, AR & Refunds/i }));
    expect(await screen.findByRole("table", { name: "Customer AR Balances" })).toBeInTheDocument();
    const cashTable = screen.getByRole("table", { name: "Recent Cash Movements" });
    expect(within(cashTable).getByText("CashIn")).toBeInTheDocument();
    expect(within(cashTable).getByText("OperationalExpense")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /Post Cash AR Collection/i }));
    await waitFor(() => expect(collectArCash).toHaveBeenCalledTimes(1));
    expect(collectArCash.mock.calls[0][0].clientRequestId).toMatch(UUID_V4_REGEX);

    // Request refund using canonical DamagedNonRestockable stockDisposition
    await user.selectOptions(screen.getByLabelText(/Stock Disposition/i), "DamagedNonRestockable");
    await user.type(screen.getByLabelText(/Refund Reason/i), "Wrong syrup order");
    await user.click(screen.getByRole("button", { name: /^Request Refund$/i }));
    await waitFor(() => expect(requestRefund).toHaveBeenCalledWith(expect.objectContaining({
      lines: [expect.objectContaining({ stockDisposition: "DamagedNonRestockable" })],
    })));

    // Approve & Commit -> simulated 504 UnknownOutcome preserves EXACT operationId on retry
    const refundsTable = screen.getByRole("table", { name: "Sale Refunds" });
    await user.click(within(refundsTable).getByRole("button", { name: /^Approve$/i }));
    await waitFor(() => expect(resolveRefund).toHaveBeenCalledWith(expect.objectContaining({ approve: true })));

    await user.click(within(refundsTable).getByRole("button", { name: /Commit Settlement/i }));
    const dialog = screen.getByRole("dialog");
    const confirmBtn = within(dialog).getByRole("button", { name: /Confirm & Commit Refund/i });
    await user.click(confirmBtn);
    await waitFor(() => expect(screen.getByText(/Refund Commit Outcome Unknown/i)).toBeInTheDocument());

    await user.click(confirmBtn);
    await waitFor(() => expect(commitSpy).toHaveBeenCalledTimes(2));
    expect(commitSpy.mock.calls[0][0].operationId).toBe(commitSpy.mock.calls[1][0].operationId);
    expect(commitSpy.mock.calls[1][0].settlements).toEqual([{ method: "Cash", amountCents: 18500 }]);
  });

  it("verifies backup package integrity and requires explicit RESTORE confirmation on Windows Host", async () => {
    const user = userEvent.setup();
    const { api, verifyBackup, restoreBackup } = createFakeBackofficeHarness();
    const { centralApi } = createFakeCentralHarness();

    render(<BackofficeConsoleApp api={api} centralApi={centralApi} initialSurface="WINDOWS_HOST" initialNav="host-ops" />);

    const table = await screen.findByRole("table", { name: "Host Backup Archives" });
    await user.click(within(table).getByRole("button", { name: /^Verify$/i }));
    await waitFor(() => expect(verifyBackup).toHaveBeenCalledWith("bkp-20261122-010000"));

    await user.click(within(table).getByRole("button", { name: /^Restore$/i }));
    const dialog = screen.getByRole("dialog");
    const restoreBtn = within(dialog).getByRole("button", { name: /Restore Database/i });
    expect(restoreBtn).toBeDisabled();

    await user.type(within(dialog).getByLabelText(/Confirmation/i), "RESTORE");
    expect(restoreBtn).toBeEnabled();
    await user.click(restoreBtn);
    await waitFor(() => expect(restoreBackup).toHaveBeenCalledWith("bkp-20261122-010000", "RESTORE"));
  });

  it("has zero critical/serious axe-core accessibility violations across the Backoffice shell", async () => {
    const { api } = createFakeBackofficeHarness();
    const { centralApi } = createFakeCentralHarness();
    const { container } = render(<BackofficeConsoleApp api={api} centralApi={centralApi} />);
    await screen.findByText("₱24,800.00");

    let results: axe.AxeResults | null = null;
    await act(async () => {
      results = await axe.run(container);
    });
    expect(results!.violations.filter((v) => v.impact === "critical" || v.impact === "serious")).toEqual([]);
  });
});
