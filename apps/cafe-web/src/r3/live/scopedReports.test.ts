import { afterEach, describe, expect, it, vi } from "vitest";
import {
  buildScopedReportCsv,
  hasAuthoritativeProfit,
  neutralizeCsvCell,
  readScopedCafeReport,
  reportRange,
  requireScopedReport,
  type ScopedCafeReport,
} from "./scopedReports";
import { hostRequest } from "../../api/hostClient";

vi.mock("../../api/hostClient", () => ({ hostRequest: vi.fn() }));

const now = new Date("2026-11-24T16:10:00Z"); // 25 Nov 00:10, Asia/Manila
const range = reportRange("daily", now);

function fixture() {
  return {
    rangeStartUtc: range.fromUtc,
    rangeEndUtc: range.toUtc,
    // Simulates Host clamping cafeManagement.generatedAtUtc to rangeEndUtc
    generatedAtUtc: range.toUtc,
    officialBoundary: "Store operational report only. Not an official/BIR/RMO report.",
    sales: {
      completedSaleCount: 2,
      recognizedCompletedSaleRevenueCents: 1000,
      committedRefundCents: 100,
      recognizedNetRevenueCents: 900,
    },
    profitability: {
      recognizedNetRevenueCents: 900,
      historicalCogsCents: 200,
      grossProfitCents: 700,
      operationalExpenseCents: 100,
      recognizedWasteCostCents: 50,
      operationalContributionCents: 550,
      authority: "Authoritative",
      authorityReasons: [],
    },
    costCoverage: {
      state: "Complete",
      revenueSaleCount: 2,
      costCompleteSaleCount: 2,
      costIncompleteSaleCount: 0,
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
        knownValueCents: 50,
        missingLabels: [],
      },
    },
    syncCompleteness: { state: "Complete", issues: [] },
    recipeCosts: [],
    cash: null,
    accountsReceivable: null,
  };
}

afterEach(() => vi.clearAllMocks());

describe("R3 live report contract", () => {
  it("uses Manila midnight, Monday week and calendar month", () => {
    expect(range).toEqual({
      fromUtc: "2026-11-24T16:00:00.000Z",
      toUtc: "2026-11-25T16:00:00.000Z",
    });
    expect(reportRange("weekly", now)).toEqual({
      fromUtc: "2026-11-22T16:00:00.000Z",
      toUtc: "2026-11-29T16:00:00.000Z",
    });
    expect(reportRange("monthly", now)).toEqual({
      fromUtc: "2026-10-31T16:00:00.000Z",
      toUtc: "2026-11-30T16:00:00.000Z",
    });
  });

  it("handles December rollover", () => {
    expect(reportRange("monthly", new Date("2026-12-31T15:59:59Z")).toUtc).toBe(
      "2026-12-31T16:00:00.000Z",
    );
  });

  it("sends exact range, captures unclamped top-level generatedAt, and preserves Host amounts and redactions", async () => {
    vi.mocked(hostRequest).mockResolvedValue({
      generatedAt: now.toISOString(),
      officialBoundary: "Owner Reports are store operational reports only. Not an official/BIR/RMO report.",
      receivables: null,
      cafeManagement: fixture(),
    });
    const result = await readScopedCafeReport("daily", now);
    expect(hostRequest).toHaveBeenCalledWith(
      "/api/owner-reports/summary?" + new URLSearchParams(range).toString(),
      undefined,
      "GET",
    );
    expect(result.hostGeneratedAtUtc).toBe(now.toISOString());
    expect(result.generatedAtUtc).toBe(range.toUtc);
    expect(result.profitability.grossProfitCents).toBe(700);
    expect(result.sales.committedRefundCents).toBe(100);
    expect(result.cash).toBeNull();
    expect(result.accountsReceivable).toBeNull();
    expect(result.receivablesSummary).toBeNull();
    expect(hasAuthoritativeProfit(result)).toBe(true);
  });

  it("rejects missing or wrong range and missing envelope generatedAt", async () => {
    expect(() => requireScopedReport(null, range)).toThrow();
    expect(() => requireScopedReport(fixture(), reportRange("weekly", now))).toThrow();
    vi.mocked(hostRequest).mockResolvedValue({ cafeManagement: fixture() });
    await expect(readScopedCafeReport("daily", now)).rejects.toThrow("generatedAt");
  });

  it("rejects invalid or inconsistent financial, cash, or AR amounts", () => {
    const mismatch = fixture();
    mismatch.profitability.recognizedNetRevenueCents = 1;
    expect(() => requireScopedReport(mismatch, range)).toThrow();

    const fractional = fixture();
    fractional.profitability.historicalCogsCents = 1.5;
    expect(() => requireScopedReport(fractional, range)).toThrow();

    const badCash = { ...fixture(), cash: { saleCashReceiptCents: 100 } };
    expect(() => requireScopedReport(badCash, range)).toThrow("cash");

    const badAr = { ...fixture(), accountsReceivable: { outstandingBalanceCents: 100 } };
    expect(() => requireScopedReport(badAr, range)).toThrow("accounts receivable");
  });

  it("preserves provisional authority and never upgrades incomplete cost, waste, or sync", () => {
    const incompleteCost = fixture();
    incompleteCost.profitability.authority = "ProvisionalCostIncomplete";
    incompleteCost.costCoverage.state = "Incomplete";
    expect(hasAuthoritativeProfit(requireScopedReport(incompleteCost, range))).toBe(false);

    const incompleteWaste = fixture();
    incompleteWaste.waste.costCoverage.state = "Incomplete";
    incompleteWaste.waste.costCoverage.missingCostLineCount = 1;
    expect(hasAuthoritativeProfit(requireScopedReport(incompleteWaste, range))).toBe(false);

    const unknownSync = fixture();
    unknownSync.syncCompleteness.state = "Unknown";
    expect(hasAuthoritativeProfit(requireScopedReport(unknownSync, range))).toBe(false);
  });

  it("neutralizes spreadsheet formula prefixes in CSV cells while preserving negative numeric amounts and explicit Cash/AR redaction", () => {
    expect(neutralizeCsvCell("=CMD|' /C calc'!A0")).toBe(`"'=CMD|' /C calc'!A0"`);
    expect(neutralizeCsvCell("+SUM(A1:A2)")).toBe(`"'+SUM(A1:A2)"`);
    expect(neutralizeCsvCell("@HYPERLINK(1)")).toBe(`"'@HYPERLINK(1)"`);
    expect(neutralizeCsvCell("-12.50")).toBe(`"-12.50"`);

    const report: ScopedCafeReport = requireScopedReport(
      {
        ...fixture(),
        officialBoundary: "=Not official BIR report",
      },
      range,
      { hostGeneratedAtUtc: now.toISOString(), receivablesSummary: null },
    );
    const csv = buildScopedReportCsv("Today", report);
    expect(csv).toContain(`"'=Not official BIR report"`);
    expect(csv).toContain(`"Completed Sales Revenue","10.00","Today"`);
    expect(csv).toContain(`"Committed Refunds","1.00","Today"`);
    expect(csv).toContain(`"Net Cash Delta (Period)","Unavailable (redacted or not authorized)"`);
    expect(csv).toContain(
      `"Accounts Receivable Outstanding (Current)","Unavailable (redacted or not authorized)"`,
    );
  });
});
