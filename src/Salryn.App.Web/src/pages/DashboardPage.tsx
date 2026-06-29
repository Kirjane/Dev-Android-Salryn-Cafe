import { useQuery } from "@tanstack/react-query";
import { fetchDashboardSummary } from "../api/dashboard";
import { MetricCard } from "../components/MetricCard";
import { Shell } from "../components/Shell";

function formatMoney(amount: number): string {
  return `PHP ${amount.toLocaleString("en-PH", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })}`;
}

export function DashboardPage() {
  const { data, isLoading, isError, error, refetch, isFetching } = useQuery({
    queryKey: ["dashboard-summary"],
    queryFn: fetchDashboardSummary,
  });

  return (
    <Shell>
      <div className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
        <div className="mb-6 flex items-start justify-between gap-4">
          <div>
            <h2 className="text-2xl font-semibold text-slate-900">Dashboard</h2>
            <p className="mt-1 text-sm text-slate-500">
              Sales, product, and inventory overview from the local API.
            </p>
          </div>
          <button
            type="button"
            onClick={() => refetch()}
            className="rounded-lg bg-navy-900 px-4 py-2 text-sm font-medium text-white hover:bg-navy-800"
          >
            Refresh
          </button>
        </div>

        {isLoading || isFetching ? (
          <p className="text-sm text-slate-500">Loading dashboard...</p>
        ) : null}

        {isError ? (
          <p className="text-sm text-amber-700">
            {(error as Error).message || "Failed to load dashboard summary."}
          </p>
        ) : null}

        {data ? (
          <>
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
              <MetricCard
                title="Today Sales"
                value={formatMoney(data.todaySalesTotal)}
                subtitle="Completed sales total"
              />
              <MetricCard
                title="Transactions"
                value={data.todayTransactionCount.toLocaleString()}
                subtitle="Completed today"
              />
              <MetricCard
                title="Products"
                value={data.productCount.toLocaleString()}
                subtitle="Catalog records"
              />
              <MetricCard
                title="Low Stock"
                value={data.lowStockCount.toLocaleString()}
                subtitle="Needs attention"
              />
            </div>

            <div className="mt-6 rounded-xl border border-slate-200 bg-slate-50 p-4">
              <p className="text-sm text-slate-600">
                {data.status === "ok"
                  ? `Dashboard refreshed ${new Date(data.refreshedAt).toLocaleString()}.`
                  : "Dashboard loaded with one or more repository warnings."}
              </p>
              {data.warnings.length > 0 ? (
                <ul className="mt-2 list-disc pl-5 text-sm text-amber-700">
                  {data.warnings.map((warning) => (
                    <li key={warning}>{warning}</li>
                  ))}
                </ul>
              ) : null}
            </div>
          </>
        ) : null}
      </div>
    </Shell>
  );
}
