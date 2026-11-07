import { readJson } from "./http";

export type AnalyticsScope = { asOfUtc: string; timeZoneId: string; rangeStartUtc: string; rangeEndUtc: string; priorRangeStartUtc: string; priorRangeEndUtc: string; rangeSemantics: string; moneySemantics: string; productAttributionSemantics: string };
export type AnalyticsPeriod = { completedSaleCount: number; salesTotalCents: number; cashOnSalesCents: number; creditOriginatedCents: number; voidedSaleCount: number };
export type AnalyticsOverview = { scope: AnalyticsScope; current: AnalyticsPeriod; prior: AnalyticsPeriod; margin: { isAvailable: boolean; estimatedCogsCents: number | null; estimatedGrossMarginCents: number | null; costedItemCount: number; missingCostItemCount: number; basis: string }; lowStockCount: number };
export type TrendPoint = { storeDate: string; salesTotalCents: number; cashOnSalesCents: number; creditOriginatedCents: number; completedSaleCount: number; voidedSaleCount: number };
export type SalesTrend = { scope: AnalyticsScope; bucket: string; points: TrendPoint[] };
export type PaymentMix = { scope: AnalyticsScope; rows: { method: string; amountCents: number; scope: string }[] };
export type TopProducts = { scope: AnalyticsScope; products: { productId: string; historicalLabel: string; historicalBarcode: string | null; quantityX1000: number; lineRevenueCents: number; estimatedCogsCents: number | null; costCoverageComplete: boolean }[]; categories: { currentCategory: string; quantityX1000: number; lineRevenueCents: number }[]; classificationDisclosure: string };
export type ArAging = { scope: AnalyticsScope; outstandingCents: number; collectionsInPeriodCents: number; collectionsByMethod: { method: string; amountCents: number; scope: string }[]; buckets: { key: string; label: string; outstandingCents: number }[]; allocationDisclosure: string };

async function get<T>(path: string, from: string, to: string): Promise<T> {
  const query = new URLSearchParams({ from, to });
  const response = await fetch(`${path}?${query}`, { credentials: "include" });
  return readJson<T>(response);
}
export const fetchAnalyticsOverview = (from: string, to: string) => get<AnalyticsOverview>("/api/owner-analytics/overview", from, to);
export const fetchSalesTrend = (from: string, to: string) => get<SalesTrend>("/api/owner-analytics/sales-trend", from, to);
export const fetchPaymentMix = (from: string, to: string) => get<PaymentMix>("/api/owner-analytics/payment-mix", from, to);
export const fetchTopProducts = (from: string, to: string) => get<TopProducts>("/api/owner-analytics/top-products", from, to);
export const fetchArAging = (from: string, to: string) => get<ArAging>("/api/owner-analytics/ar-aging", from, to);
export function analyticsExportUrl(kind: "sales" | "ar", from: string, to: string) { return `/api/owner-analytics/export/${kind}.csv?${new URLSearchParams({ from, to })}`; }
