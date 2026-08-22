import { readJson } from "./http";

export type DashboardSummary = {
  todaySalesTotal: number;
  todayTransactionCount: number;
  productCount: number;
  lowStockCount: number;
  refreshedAt: string;
  status: string;
  warnings: string[];
};

export async function fetchDashboardSummary(): Promise<DashboardSummary> {
  const response = await fetch("/api/dashboard/summary", {
    credentials: "include",
  });

  return readJson<DashboardSummary>(response);
}
