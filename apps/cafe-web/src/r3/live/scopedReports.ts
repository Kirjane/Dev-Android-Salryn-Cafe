import { hostRequest } from "../../api/hostClient";
import type { OwnerReportsSnapshot } from "../../backoffice/types";

export type ReportPeriod = "daily" | "weekly" | "monthly";
export type ReportRange = { fromUtc: string; toUtc: string };
type Management = NonNullable<OwnerReportsSnapshot["cafeManagement"]>;
export type ScopedCafeReport = Management & {
  rangeStartUtc: string;
  rangeEndUtc: string;
  generatedAtUtc: string;
  sales: { completedSaleCount: number; recognizedNetRevenueCents: number };
  profitability: Management["profitability"] & { authority: string; authorityReasons: string[] };
  syncCompleteness: { state: string };
};

const MANILA_OFFSET_MS = 8 * 60 * 60 * 1000;

// Host uses Asia/Manila calendar boundaries, Monday-start weeks, exclusive end.
export function reportRange(period: ReportPeriod, now: Date): ReportRange {
  if (!Number.isFinite(now.getTime())) throw new Error("Invalid report clock.");
  const local = new Date(now.getTime() + MANILA_OFFSET_MS);
  const year = local.getUTCFullYear(), month = local.getUTCMonth(), day = local.getUTCDate();
  const midnight = Date.UTC(year, month, day);
  const start = period === "monthly" ? Date.UTC(year, month, 1)
    : period === "weekly" ? midnight - ((local.getUTCDay() + 6) % 7) * 86400000 : midnight;
  const end = period === "monthly" ? Date.UTC(year, month + 1, 1)
    : start + (period === "weekly" ? 7 : 1) * 86400000;
  return { fromUtc: new Date(start - MANILA_OFFSET_MS).toISOString(),
    toUtc: new Date(end - MANILA_OFFSET_MS).toISOString() };
}

export function requireScopedReport(value: unknown, range: ReportRange): ScopedCafeReport {
  const report = value as ScopedCafeReport | null | undefined;
  if (!report || Date.parse(report.rangeStartUtc) !== Date.parse(range.fromUtc)
    || Date.parse(report.rangeEndUtc) !== Date.parse(range.toUtc)
    || !report.profitability || !report.costCoverage || !report.sales
    || !report.syncCompleteness || !Array.isArray(report.recipeCosts)
    || typeof report.profitability.authority !== "string"
    || !Array.isArray(report.profitability.authorityReasons)) {
    throw new Error("Host did not return the requested Cafe management report range.");
  }
  const amounts = [report.sales.recognizedNetRevenueCents, report.sales.completedSaleCount,
    report.profitability.recognizedNetRevenueCents, report.profitability.historicalCogsCents,
    report.profitability.grossProfitCents, report.profitability.operationalExpenseCents,
    report.profitability.recognizedWasteCostCents, report.profitability.operationalContributionCents];
  if (!amounts.every(Number.isSafeInteger) ||
      report.sales.recognizedNetRevenueCents !== report.profitability.recognizedNetRevenueCents) {
    throw new Error("Host report financial values are invalid or inconsistent.");
  }
  return report;
}

export async function readScopedCafeReport(period: ReportPeriod, now = new Date()): Promise<ScopedCafeReport> {
  const range = reportRange(period, now);
  const query = new URLSearchParams(range);
  const result = await hostRequest<{ cafeManagement?: unknown }>(
    "/api/owner-reports/summary?" + query.toString(), undefined, "GET");
  return requireScopedReport(result.cafeManagement, range);
}

export function hasAuthoritativeProfit(report: ScopedCafeReport): boolean {
  return report.profitability.authority === "Authoritative"
    && report.costCoverage.state === "Complete"
    && report.costCoverage.costIncompleteSaleCount === 0
    && report.syncCompleteness.state === "Complete";
}
