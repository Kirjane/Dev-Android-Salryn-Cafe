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
  const response = await fetch("/api/dashboard/summary");

  if (!response.ok) {
    throw new Error(`Dashboard summary request failed (${response.status})`);
  }

  return response.json() as Promise<DashboardSummary>;
}
