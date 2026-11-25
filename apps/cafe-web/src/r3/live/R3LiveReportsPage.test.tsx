import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { R3LiveReportsPage } from "./R3LiveReportsPage";
import { hostRequest } from "../../api/hostClient";
import { reportRange } from "./scopedReports";

vi.mock("../../api/hostClient", () => ({ hostRequest: vi.fn() }));

function makeEnvelope(period: "daily" | "weekly" | "monthly", opts?: {
  netRevenueCents?: number;
  withCashAr?: boolean;
  authority?: string;
  costState?: string;
  generatedAt?: string;
}) {
  const now = new Date();
  const range = reportRange(period, now);
  const net = opts?.netRevenueCents ?? 125000;
  const withCashAr = opts?.withCashAr ?? true;
  return {
    generatedAt: opts?.generatedAt ?? "2026-11-25T02:15:00.000Z",
    officialBoundary: "Owner Reports are store operational reports only. Not an official/BIR/RMO report.",
    receivables: withCashAr
      ? {
          creditSalesCents: 20000,
          cashCollectedOnSalesCents: 105000,
          collectionsCents: 15000,
          outstandingCents: 45000,
          overdueCents: 5000,
        }
      : null,
    cafeManagement: {
      rangeStartUtc: range.fromUtc,
      rangeEndUtc: range.toUtc,
      // Clamped future end timestamp from backend report service
      generatedAtUtc: range.toUtc,
      officialBoundary: "Owner Reports are store operational reports only. Not an official/BIR/RMO report.",
      sales: {
        completedSaleCount: 4,
        recognizedCompletedSaleRevenueCents: net + 5000,
        committedRefundCents: 5000,
        recognizedNetRevenueCents: net,
      },
      profitability: {
        recognizedNetRevenueCents: net,
        historicalCogsCents: 40000,
        grossProfitCents: net - 40000,
        operationalExpenseCents: 10000,
        recognizedWasteCostCents: 2500,
        operationalContributionCents: net - 52500,
        authority: opts?.authority ?? "ProvisionalCostIncomplete",
        authorityReasons:
          opts?.authority === "Authoritative" ? [] : ["1 completed sale lacks historical cost."],
      },
      costCoverage: {
        state: opts?.costState ?? "Incomplete",
        revenueSaleCount: 4,
        costCompleteSaleCount: opts?.costState === "Complete" ? 4 : 3,
        costIncompleteSaleCount: opts?.costState === "Complete" ? 0 : 1,
      },
      waste: {
        wasteEventCount: 1,
        wasteLineCount: 1,
        ingredientWasteQuantityX1000: 1000,
        productWasteQuantityX1000: 0,
        costCoverage: {
          state: "Complete",
          valuedLineCount: 1,
          missingCostLineCount: 0,
          knownValueCents: 2500,
          missingLabels: [],
        },
      },
      syncCompleteness: {
        state: "Complete",
        issues: [],
      },
      recipeCosts: [
        {
          recipeId: "40000000-0000-0000-0000-000000000101",
          recipeName: "Iced Latte",
          yieldX1000: 1000,
          knownCostCents: 4200,
          costCoverage: "Complete",
        },
        {
          recipeId: "40000000-0000-0000-0000-000000000102",
          recipeName: "Matcha Cloud",
          yieldX1000: 1000,
          knownCostCents: 2100,
          costCoverage: "Incomplete",
        },
      ],
      cash: withCashAr
        ? {
            saleCashReceiptCents: 105000,
            receivableCashCollectionCents: 15000,
            cashInCents: 0,
            inventoryPurchaseCashOutCents: 12000,
            operationalExpenseCashOutCents: 10000,
            cashRefundCents: 5000,
            genericCashOutCents: 0,
            reversalNetCents: 0,
            netCashDeltaCents: 93000,
          }
        : null,
      accountsReceivable: withCashAr
        ? {
            debitCentsInRange: 20000,
            creditCentsInRange: 15000,
            cashCollectionCentsInRange: 15000,
            outstandingBalanceCents: 45000,
          }
        : null,
    },
  };
}

const ACTIVE_LICENSE = {
  status: "Licensed",
  licenseType: "Host",
  applicationId: "Salryn.Cafe",
  machineMatched: true,
};

afterEach(() => vi.clearAllMocks());

describe("R3LiveReportsPage", () => {
  it("renders actual Host generatedAt (not future rangeEndUtc), provisional banner, partial recipe costing, and explicit redaction when Cash/AR are null", async () => {
    vi.mocked(hostRequest).mockImplementation(async (path: string) => {
      if (path === "/api/license/status") return ACTIVE_LICENSE;
      if (path === "/api/session") {
        return {
          isAuthenticated: true,
          user: { id: "u1", username: "rep", displayName: "Reports", roleName: "Staff", permissions: ["Reports.View"] },
        };
      }
      if (path.startsWith("/api/owner-reports/summary?")) {
        return makeEnvelope("monthly", { withCashAr: false, generatedAt: "2026-11-25T02:15:00.000Z" });
      }
      if (path === "/api/cafe/inventory/low-stock") {
        throw new Error("403 Forbidden");
      }
      throw new Error("Unexpected path: " + path);
    });

    const user = userEvent.setup();
    render(<R3LiveReportsPage />);

    await waitFor(() => {
      expect(screen.getByText(/Provisional profitability — not final/i)).toBeInTheDocument();
    });
    // Actual Host timestamp 2026-11-25 10:15 AM Manila, NOT Dec 1 rangeEndUtc
    expect(screen.getByText(/Generated Nov 25, 10:15 AM/i)).toBeInTheDocument();
    expect(screen.getAllByText("₱1,250.00")).toHaveLength(2);

    // Recipes tab shows (partial) suffix for incomplete recipe
    await user.click(screen.getByRole("tab", { name: /Recipe Costing \(2\)/i }));
    expect(screen.getByText("Iced Latte")).toBeInTheDocument();
    expect(screen.getByText("₱21.00 (partial)")).toBeInTheDocument();

    // Cash & AR tab explicitly redacts instead of rendering ₱0.00
    await user.click(screen.getByRole("tab", { name: "Cash & AR" }));
    expect(screen.getAllByText("Unavailable or not authorized.")).toHaveLength(2);

    // Low Stock tab discloses retrieval denial cleanly
    await user.click(screen.getByRole("tab", { name: /Low Stock/i }));
    expect(screen.getByText(/Low stock data could not be retrieved or is not permitted/i)).toBeInTheDocument();
  });

  it("drops stale period responses when switching periods quickly", async () => {
    let resolveMonthly!: (value: unknown) => void;
    const monthlyDeferred = new Promise((res) => {
      resolveMonthly = res;
    });
    let summaryCall = 0;

    vi.mocked(hostRequest).mockImplementation(async (path: string) => {
      if (path === "/api/license/status") return ACTIVE_LICENSE;
      if (path === "/api/session") {
        return {
          isAuthenticated: true,
          user: { id: "u1", username: "owner", displayName: "Owner", roleName: "Admin", permissions: ["Reports.View", "Receivables.View"] },
        };
      }
      if (path.startsWith("/api/owner-reports/summary?")) {
        summaryCall++;
        if (summaryCall === 1) return monthlyDeferred;
        return makeEnvelope("daily", { netRevenueCents: 35000, authority: "Authoritative", costState: "Complete" });
      }
      if (path === "/api/cafe/inventory/low-stock") return [];
      throw new Error("Unexpected path: " + path);
    });

    const user = userEvent.setup();
    render(<R3LiveReportsPage />);

    // Switch to Today while This Month is still in flight
    await user.click(screen.getByRole("button", { name: "Today" }));
    await waitFor(() => {
      expect(screen.getByText(/Authoritative profitability/i)).toBeInTheDocument();
    });
    expect(screen.getAllByText("₱350.00").length).toBeGreaterThan(0);

    // Now resolve the stale monthly response; Today values must remain intact
    resolveMonthly(makeEnvelope("monthly", { netRevenueCents: 999900 }));
    await new Promise((r) => setTimeout(r, 20));
    expect(screen.queryByText("₱9,999.00")).not.toBeInTheDocument();
    expect(screen.getAllByText("₱350.00").length).toBeGreaterThan(0);
  });

  it("fails closed when Reports.View permission is missing or Host license is inactive", async () => {
    vi.mocked(hostRequest).mockImplementation(async (path: string) => {
      if (path === "/api/license/status") return ACTIVE_LICENSE;
      if (path === "/api/session") {
        return {
          isAuthenticated: true,
          user: { id: "u2", username: "cashier", displayName: "Cashier", roleName: "Cashier", permissions: ["Sales.Create"] },
        };
      }
      throw new Error("Unexpected path: " + path);
    });

    const { unmount } = render(<R3LiveReportsPage />);
    await waitFor(() => {
      expect(screen.getByText(/An authenticated operator with Reports\.View is required/i)).toBeInTheDocument();
    });
    unmount();

    vi.mocked(hostRequest).mockImplementation(async (path: string) => {
      if (path === "/api/license/status") return { status: "Missing", machineMatched: false };
      if (path === "/api/session") return { isAuthenticated: true, user: { id: "u1", username: "owner", displayName: "Owner", permissions: ["Reports.View"] } };
      throw new Error("Unexpected path: " + path);
    });
    render(<R3LiveReportsPage />);
    await waitFor(() => {
      expect(screen.getByText(/Active Host license required/i)).toBeInTheDocument();
    });
  });
});
