import { hostRequest } from "../../api/hostClient";
import type { OwnerReportsSnapshot } from "../../backoffice/types";

export type ReportPeriod = "daily" | "weekly" | "monthly";
export type ReportRange = { fromUtc: string; toUtc: string };

export type ScopedCashSummary = {
  saleCashReceiptCents: number;
  receivableCashCollectionCents: number;
  cashInCents: number;
  inventoryPurchaseCashOutCents: number;
  operationalExpenseCashOutCents: number;
  cashRefundCents: number;
  genericCashOutCents: number;
  reversalNetCents: number;
  netCashDeltaCents: number;
};

export type ScopedArSummary = {
  debitCentsInRange: number;
  creditCentsInRange: number;
  cashCollectionCentsInRange: number;
  outstandingBalanceCents: number;
};

export type ScopedWasteCoverage = {
  state: string;
  valuedLineCount: number;
  missingCostLineCount: number;
  knownValueCents: number;
  missingLabels: string[];
};

export type ScopedWasteSummary = {
  wasteEventCount: number;
  wasteLineCount: number;
  ingredientWasteQuantityX1000: number;
  productWasteQuantityX1000: number;
  costCoverage: ScopedWasteCoverage;
};

export type ScopedSyncIssue = {
  code: string;
  description: string;
  count: number;
};

export type ScopedReceivablesSummary = {
  creditSalesCents: number;
  cashCollectedOnSalesCents: number;
  collectionsCents: number;
  outstandingCents: number;
  overdueCents: number;
};

type Management = NonNullable<OwnerReportsSnapshot["cafeManagement"]>;

export type ScopedCafeReport = Omit<Management, "cash" | "accountsReceivable"> & {
  rangeStartUtc: string;
  rangeEndUtc: string;
  generatedAtUtc: string;
  /** Actual top-level Host generation instant (unclamped by rangeEndUtc). */
  hostGeneratedAtUtc: string;
  officialBoundary: string;
  sales: {
    completedSaleCount: number;
    voidedSaleCount?: number;
    recognizedCompletedSaleRevenueCents: number;
    committedRefundCents: number;
    recognizedNetRevenueCents: number;
  };
  profitability: Management["profitability"] & {
    authority: string;
    authorityReasons: string[];
  };
  waste: ScopedWasteSummary;
  cash: ScopedCashSummary | null;
  accountsReceivable: ScopedArSummary | null;
  receivablesSummary: ScopedReceivablesSummary | null;
  syncCompleteness: {
    state: string;
    issues?: ScopedSyncIssue[];
  };
};

type OwnerReportsEnvelope = {
  generatedAt?: unknown;
  officialBoundary?: unknown;
  receivables?: unknown;
  cafeManagement?: unknown;
};

const MANILA_OFFSET_MS = 8 * 60 * 60 * 1000;

function isValidIsoInstant(value: unknown): value is string {
  return typeof value === "string" && Number.isFinite(Date.parse(value));
}

function validateCashSummary(value: unknown): ScopedCashSummary | null {
  if (value === null || value === undefined) return null;
  const cash = value as Partial<ScopedCashSummary>;
  const fields = [
    cash.saleCashReceiptCents,
    cash.receivableCashCollectionCents,
    cash.cashInCents,
    cash.inventoryPurchaseCashOutCents,
    cash.operationalExpenseCashOutCents,
    cash.cashRefundCents,
    cash.genericCashOutCents,
    cash.reversalNetCents,
    cash.netCashDeltaCents,
  ];
  if (!fields.every(Number.isSafeInteger)) {
    throw new Error("Host report cash breakdown is invalid.");
  }
  return cash as ScopedCashSummary;
}

function validateArSummary(value: unknown): ScopedArSummary | null {
  if (value === null || value === undefined) return null;
  const ar = value as Partial<ScopedArSummary>;
  const fields = [
    ar.debitCentsInRange,
    ar.creditCentsInRange,
    ar.cashCollectionCentsInRange,
    ar.outstandingBalanceCents,
  ];
  if (!fields.every(Number.isSafeInteger)) {
    throw new Error("Host report accounts receivable breakdown is invalid.");
  }
  return ar as ScopedArSummary;
}

function validateReceivablesSummary(value: unknown): ScopedReceivablesSummary | null {
  if (value === null || value === undefined) return null;
  const rec = value as Partial<ScopedReceivablesSummary>;
  const fields = [
    rec.creditSalesCents,
    rec.cashCollectedOnSalesCents,
    rec.collectionsCents,
    rec.outstandingCents,
    rec.overdueCents,
  ];
  if (!fields.every(Number.isSafeInteger)) {
    throw new Error("Host report receivables summary is invalid.");
  }
  return rec as ScopedReceivablesSummary;
}

// Host uses Asia/Manila calendar boundaries, Monday-start weeks, exclusive end.
export function reportRange(period: ReportPeriod, now: Date): ReportRange {
  if (!Number.isFinite(now.getTime())) throw new Error("Invalid report clock.");
  const local = new Date(now.getTime() + MANILA_OFFSET_MS);
  const year = local.getUTCFullYear();
  const month = local.getUTCMonth();
  const day = local.getUTCDate();
  const midnight = Date.UTC(year, month, day);
  const start =
    period === "monthly"
      ? Date.UTC(year, month, 1)
      : period === "weekly"
        ? midnight - ((local.getUTCDay() + 6) % 7) * 86400000
        : midnight;
  const end =
    period === "monthly"
      ? Date.UTC(year, month + 1, 1)
      : start + (period === "weekly" ? 7 : 1) * 86400000;
  return {
    fromUtc: new Date(start - MANILA_OFFSET_MS).toISOString(),
    toUtc: new Date(end - MANILA_OFFSET_MS).toISOString(),
  };
}

export function requireScopedReport(
  value: unknown,
  range: ReportRange,
  envelopeMeta?: {
    hostGeneratedAtUtc?: unknown;
    officialBoundary?: unknown;
    receivablesSummary?: unknown;
  },
): ScopedCafeReport {
  const report = value as Partial<ScopedCafeReport> | null | undefined;
  if (
    !report ||
    !isValidIsoInstant(report.rangeStartUtc) ||
    !isValidIsoInstant(report.rangeEndUtc) ||
    !isValidIsoInstant(report.generatedAtUtc) ||
    Date.parse(report.rangeStartUtc) !== Date.parse(range.fromUtc) ||
    Date.parse(report.rangeEndUtc) !== Date.parse(range.toUtc) ||
    !report.profitability ||
    !report.costCoverage ||
    !report.sales ||
    !report.waste ||
    !report.waste.costCoverage ||
    typeof report.waste.costCoverage.state !== "string" ||
    !Number.isSafeInteger(report.waste.costCoverage.missingCostLineCount) ||
    !report.syncCompleteness ||
    typeof report.syncCompleteness.state !== "string" ||
    !Array.isArray(report.recipeCosts) ||
    typeof report.profitability.authority !== "string" ||
    !Array.isArray(report.profitability.authorityReasons)
  ) {
    throw new Error("Host did not return the requested Cafe management report range.");
  }

  const hostGeneratedAtUtc =
    envelopeMeta?.hostGeneratedAtUtc !== undefined
      ? envelopeMeta.hostGeneratedAtUtc
      : report.hostGeneratedAtUtc ?? report.generatedAtUtc;
  if (!isValidIsoInstant(hostGeneratedAtUtc)) {
    throw new Error("Host report generation timestamp is invalid.");
  }

  const officialBoundary =
    typeof report.officialBoundary === "string" && report.officialBoundary.trim()
      ? report.officialBoundary.trim()
      : typeof envelopeMeta?.officialBoundary === "string" && envelopeMeta.officialBoundary.trim()
        ? envelopeMeta.officialBoundary.trim()
        : "Store operational report only. Not an official/BIR/RMO report.";

  const amounts = [
    report.sales.recognizedNetRevenueCents,
    report.sales.recognizedCompletedSaleRevenueCents,
    report.sales.committedRefundCents,
    report.sales.completedSaleCount,
    report.profitability.recognizedNetRevenueCents,
    report.profitability.historicalCogsCents,
    report.profitability.grossProfitCents,
    report.profitability.operationalExpenseCents,
    report.profitability.recognizedWasteCostCents,
    report.profitability.operationalContributionCents,
    report.costCoverage.revenueSaleCount,
    report.costCoverage.costCompleteSaleCount,
    report.costCoverage.costIncompleteSaleCount,
  ];
  if (
    !amounts.every(Number.isSafeInteger) ||
    report.sales.recognizedNetRevenueCents !== report.profitability.recognizedNetRevenueCents
  ) {
    throw new Error("Host report financial values are invalid or inconsistent.");
  }

  const cash = validateCashSummary(report.cash);
  const accountsReceivable = validateArSummary(report.accountsReceivable);
  const receivablesSummary = validateReceivablesSummary(
    envelopeMeta?.receivablesSummary !== undefined
      ? envelopeMeta.receivablesSummary
      : report.receivablesSummary,
  );

  return {
    ...(report as ScopedCafeReport),
    hostGeneratedAtUtc,
    officialBoundary,
    cash,
    accountsReceivable,
    receivablesSummary,
  };
}

export async function readScopedCafeReport(
  period: ReportPeriod,
  now = new Date(),
): Promise<ScopedCafeReport> {
  const range = reportRange(period, now);
  const query = new URLSearchParams(range);
  const result = await hostRequest<OwnerReportsEnvelope>(
    "/api/owner-reports/summary?" + query.toString(),
    undefined,
    "GET",
  );
  if (!isValidIsoInstant(result?.generatedAt)) {
    throw new Error("Host report response omitted a valid generatedAt timestamp.");
  }
  return requireScopedReport(result.cafeManagement, range, {
    hostGeneratedAtUtc: result.generatedAt,
    officialBoundary: result.officialBoundary,
    receivablesSummary: result.receivables ?? null,
  });
}

export function hasAuthoritativeProfit(report: ScopedCafeReport): boolean {
  return (
    report.profitability.authority === "Authoritative" &&
    report.costCoverage.state === "Complete" &&
    report.costCoverage.costIncompleteSaleCount === 0 &&
    report.waste.costCoverage.state === "Complete" &&
    report.waste.costCoverage.missingCostLineCount === 0 &&
    report.syncCompleteness.state === "Complete"
  );
}

/**
 * Neutralizes spreadsheet formula injection on non-numeric CSV text cells
 * while preserving signed decimal currency values such as "-12.50".
 */
export function neutralizeCsvCell(value: string): string {
  const isSignedNumeric = /^-?\d+(\.\d+)?$/.test(value);
  const safe = !isSignedNumeric && /^[=+\-@\t\r]/.test(value) ? "'" + value : value;
  return '"' + safe.replace(/"/g, '""') + '"';
}

function centsToCsvAmount(cents: number | null | undefined, redactedLabel = "Unavailable"): string {
  return Number.isSafeInteger(cents) ? ((cents as number) / 100).toFixed(2) : redactedLabel;
}

export function buildScopedReportCsv(periodLabel: string, report: ScopedCafeReport): string {
  const p = report.profitability;
  const s = report.sales;
  const meta = [
    periodLabel,
    report.rangeStartUtc,
    report.rangeEndUtc,
    report.hostGeneratedAtUtc,
    p.authority,
    report.costCoverage.state,
    report.waste.costCoverage.state,
    report.syncCompleteness.state,
    report.officialBoundary,
  ];
  const header = [
    "Metric",
    "Amount (PHP) / Disclosure",
    "Period",
    "Range Start UTC",
    "Range End UTC",
    "Host Generated UTC",
    "Host Authority",
    "Cost Coverage",
    "Waste Coverage",
    "Sync Completeness",
    "Official Boundary",
  ];
  const rows: string[][] = [
    header,
    ["Completed Sales Revenue", centsToCsvAmount(s.recognizedCompletedSaleRevenueCents), ...meta],
    ["Committed Refunds", centsToCsvAmount(s.committedRefundCents), ...meta],
    ["Recognized Net Revenue", centsToCsvAmount(p.recognizedNetRevenueCents), ...meta],
    ["Historical COGS", centsToCsvAmount(p.historicalCogsCents), ...meta],
    ["Gross Profit", centsToCsvAmount(p.grossProfitCents), ...meta],
    ["Operational Expenses", centsToCsvAmount(p.operationalExpenseCents), ...meta],
    ["Recognized Waste Cost", centsToCsvAmount(p.recognizedWasteCostCents), ...meta],
    [
      "Operational Contribution (Not Net Income)",
      centsToCsvAmount(p.operationalContributionCents),
      ...meta,
    ],
    [
      "Net Cash Delta (Period)",
      report.cash
        ? centsToCsvAmount(report.cash.netCashDeltaCents)
        : "Unavailable (redacted or not authorized)",
      ...meta,
    ],
    [
      "Accounts Receivable Outstanding (Current)",
      report.accountsReceivable
        ? centsToCsvAmount(report.accountsReceivable.outstandingBalanceCents)
        : "Unavailable (redacted or not authorized)",
      ...meta,
    ],
  ];
  return rows.map((row) => row.map(neutralizeCsvCell).join(",")).join("\r\n");
}
