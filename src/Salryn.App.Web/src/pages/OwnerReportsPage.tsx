import { CafeManagementReportsPanel } from "./CafeManagementReportsPanel";
import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ApiError } from "../api/http";
import {
  analyticsExportUrl,
  fetchAnalyticsOverview,
  fetchArAging,
  fetchPaymentMix,
  fetchSalesTrend,
  fetchTopProducts,
} from "../api/ownerAnalytics";
import { fetchApAgingReport } from "../api/procureToPay";
import { useAppSession } from "../hooks/useAppSession";
import { Permissions, hasPermission } from "../security/permissions";
import {
  Alert,
  Card,
  CardBody,
  CardHeader,
  CardTitle,
  CommandBar,
  ErrorState,
  LoadingState,
  StatusPill,
  TextInput,
} from "../components/ui";
import { formatMoney } from "../utils/format";
import {
  SalesTrendChart,
  SalesVsVoidChart,
  PaymentMixChart,
  TopProductsChart,
  ArAgingDistributionChart,
} from "../components/analytics/AnalyticsCharts";

const peso = (cents: number | null | undefined) =>
  cents == null ? "Unavailable" : formatMoney(cents / 100);
const percent = (value: number | null) =>
  value == null ? "Not available" : `${value.toFixed(1)}%`;
const iso = (date: Date) =>
  date.toLocaleDateString("en-CA", { timeZone: "Asia/Manila" });
const shift = (days: number) => {
  const d = new Date();
  d.setUTCDate(d.getUTCDate() + days);
  return iso(d);
};
type Preset =
  | "today"
  | "yesterday"
  | "7"
  | "30"
  | "90"
  | "month"
  | "last-month"
  | "custom";

function presetRange(preset: Preset) {
  const today = iso(new Date());
  if (preset === "today") return [today, today];
  if (preset === "yesterday") return [shift(-1), shift(-1)];
  if (preset === "7") return [shift(-6), today];
  if (preset === "30") return [shift(-29), today];
  if (preset === "90") return [shift(-89), today];
  const [y, m] = today.split("-").map(Number);
  if (preset === "month") return [`${y}-${String(m).padStart(2, "0")}-01`, today];
  return [
    iso(new Date(Date.UTC(y, m - 2, 1))),
    iso(new Date(Date.UTC(y, m - 1, 0))),
  ];
}

function isForbidden(error: unknown): boolean {
  return error instanceof ApiError && error.status === 403;
}

function Metric({
  label,
  value,
  prior,
  onOpen,
}: {
  label: string;
  value: number | null;
  prior?: number;
  onOpen?: () => void;
}) {
  const delta = prior == null || value == null ? null : value - prior;
  const content = (
    <>
      <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">
        {label}
      </p>
      <p className="mt-2 text-2xl font-bold tabular-nums text-slate-950">
        {peso(value)}
      </p>
      {delta != null ? (
        <p
          className={`mt-1 text-xs font-medium tabular-nums ${
            delta >= 0 ? "text-emerald-700" : "text-rose-700"
          }`}
        >
          {delta >= 0 ? "+" : ""}
          {peso(delta)} vs equal prior period
        </p>
      ) : null}
    </>
  );
  return (
    <Card>
      <CardBody>
        {onOpen ? (
          <button
            type="button"
            className="w-full text-left"
            onClick={onOpen}
          >
            {content}
          </button>
        ) : (
          <div className="w-full text-left">{content}</div>
        )}
      </CardBody>
    </Card>
  );
}

function SummaryStat({
  label,
  value,
  note,
  tone = "neutral",
}: {
  label: string;
  value: string;
  note: string;
  tone?: "neutral" | "positive" | "warning";
}) {
  const toneClass =
    tone === "positive"
      ? "border-emerald-200 bg-emerald-50/60"
      : tone === "warning"
        ? "border-amber-200 bg-amber-50/70"
        : "border-slate-200 bg-slate-50/70";
  return (
    <div className={`rounded-xl border p-4 ${toneClass}`}>
      <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">{label}</p>
      <p className="mt-1 text-xl font-bold tabular-nums text-slate-950">{value}</p>
      <p className="mt-1 text-xs leading-5 text-slate-600">{note}</p>
    </div>
  );
}

function Bars({
  title,
  rows,
  error,
  onSelect,
}: {
  title: string;
  rows: { label: string; value: number }[];
  error?: string;
  onSelect?: (label: string) => void;
}) {
  const max = Math.max(1, ...rows.map((row) => row.value));
  return (
    <Card>
      <CardHeader>
        <CardTitle>{title}</CardTitle>
      </CardHeader>
      <CardBody>
        {error ? (
          <Alert tone="danger" title="Category classification unavailable">
            {error}
          </Alert>
        ) : rows.length === 0 ? (
          <p className="text-sm text-slate-500">
            No completed sales in this period.
          </p>
        ) : (
          <>
            <div
              role="img"
              aria-label={`${title} bar chart`}
              className="space-y-3"
            >
              {rows.map((row) => (
                <button
                  type="button"
                  disabled={!onSelect}
                  onClick={() => onSelect?.(row.label)}
                  className="block w-full text-left"
                  key={row.label}
                >
                  <div className="flex justify-between gap-3 text-xs">
                    <span className="font-medium text-slate-800">{row.label}</span>
                    <span className="font-semibold tabular-nums text-slate-900">
                      {peso(row.value)}
                    </span>
                  </div>
                  <div className="mt-1 h-3 rounded bg-slate-100">
                    <div
                      className="h-3 rounded bg-blue-600"
                      style={{ width: row.value === 0 ? "0%" : `${Math.max(2, (row.value / max) * 100)}%` }}
                    />
                  </div>
                </button>
              ))}
            </div>
            <details className="mt-3">
              <summary className="cursor-pointer text-xs font-semibold text-blue-700">
                Tabular chart data
              </summary>
              <div className="mt-2 max-h-80 overflow-y-auto">
                <table className="w-full text-xs">
                  <thead>
                    <tr className="border-b border-slate-200 text-slate-500">
                      <th className="py-1.5 text-left">Bucket</th>
                      <th className="py-1.5 text-right">Amount</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {rows.map((row) => (
                      <tr key={row.label}>
                        <td className="py-1.5">{row.label}</td>
                        <td className="py-1.5 text-right tabular-nums">
                          {peso(row.value)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </details>
          </>
        )}
      </CardBody>
    </Card>
  );
}

export function OwnerReportsPage({
  onOpenSales,
  onOpenReceivables,
  onOpenPayables,
}: {
  onOpenSales?: (from: string, to: string) => void;
  onOpenReceivables?: () => void;
  onOpenPayables?: () => void;
}) {
  const session = useAppSession();
  const canViewPayables = hasPermission(session.data, Permissions.PayablesView);
  const initial = presetRange("7");
  const [preset, setPreset] = useState<Preset>("7");
  const [from, setFrom] = useState(initial[0]);
  const [to, setTo] = useState(initial[1]);
  function choose(value: Preset) {
    setPreset(value);
    if (value !== "custom") {
      const range = presetRange(value);
      setFrom(range[0]);
      setTo(range[1]);
    }
  }
  const overview = useQuery({
    queryKey: ["owner-analytics-overview", from, to],
    queryFn: () => fetchAnalyticsOverview(from, to),
    retry: false,
  });
  const trend = useQuery({
    queryKey: ["owner-analytics-trend", from, to],
    queryFn: () => fetchSalesTrend(from, to),
    retry: false,
  });
  const mix = useQuery({
    queryKey: ["owner-analytics-mix", from, to],
    queryFn: () => fetchPaymentMix(from, to),
    retry: false,
  });
  const top = useQuery({
    queryKey: ["owner-analytics-top", from, to],
    queryFn: () => fetchTopProducts(from, to),
    retry: false,
  });
  const ar = useQuery({
    queryKey: ["owner-analytics-ar", from, to],
    queryFn: () => fetchArAging(from, to),
    retry: false,
  });
  const manilaToday = iso(new Date());
  const ap = useQuery({
    queryKey: ["owner-analytics-ap", session.data?.user?.userId ?? "anon", canViewPayables, manilaToday],
    queryFn: () => fetchApAgingReport(),
    enabled: canViewPayables,
    retry: false,
  });
  const apSnapshotTimestamp = ap.dataUpdatedAt
    ? new Date(ap.dataUpdatedAt).toLocaleString("en-PH", {
        timeZone: "Asia/Manila",
        year: "numeric",
        month: "short",
        day: "2-digit",
        hour: "2-digit",
        minute: "2-digit",
        second: "2-digit",
      })
    : `${manilaToday} (Asia/Manila)`;
  const busy =
    overview.isLoading || trend.isLoading || mix.isLoading || top.isLoading;
  const firstError = [
    overview.error,
    trend.error,
    mix.error,
    top.error,
  ].find(Boolean) as Error | undefined;

  const averageTicketCents = overview.data?.current.completedSaleCount
    ? Math.round(overview.data.current.salesTotalCents / overview.data.current.completedSaleCount)
    : null;
  const cashRealizationPercent = overview.data?.current.salesTotalCents
    ? (overview.data.current.cashOnSalesCents / overview.data.current.salesTotalCents) * 100
    : null;
  const costCoverageTotal = overview.data
    ? overview.data.margin.costedItemCount + overview.data.margin.missingCostItemCount
    : 0;
  const costCoveragePercent = costCoverageTotal > 0 && overview.data
    ? (overview.data.margin.costedItemCount / costCoverageTotal) * 100
    : null;

  return (
    <div className="space-y-4">
      <CommandBar
        title="Reports"
        subtitle="Owner analytics by store day in Asia/Manila; Read-only and bounded."
        status={<StatusPill tone="info">Reports.View</StatusPill>}
        actions={
          <>
            <a
              className="rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50"
              href={analyticsExportUrl("sales", from, to)}
            >
              Export sales CSV
            </a>
            {ar.data ? (
              <a
                className="rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50"
                href={analyticsExportUrl("ar", from, to)}
              >
                Export AR CSV
              </a>
            ) : null}
          </>
        }
      />
      <Card>
        <CardBody className="flex flex-wrap items-end gap-3">
          <div>
            <label
              htmlFor="reports-period-preset"
              className="block text-xs font-semibold uppercase tracking-wide text-slate-600"
            >
              Period
            </label>
            <select
              id="reports-period-preset"
              className="mt-1 block h-10 rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900"
              value={preset}
              onChange={(e) => choose(e.target.value as Preset)}
            >
              {[
                ["today", "Today"],
                ["yesterday", "Yesterday"],
                ["7", "Last 7 days"],
                ["30", "Last 30 days"],
                ["90", "Last 90 days"],
                ["month", "This month"],
                ["last-month", "Last month"],
                ["custom", "Custom"],
              ].map(([v, l]) => (
                <option key={v} value={v}>
                  {l}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label
              htmlFor="reports-date-from"
              className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-600"
            >
              From
            </label>
            <TextInput
              id="reports-date-from"
              type="date"
              value={from}
              onChange={(e) => {
                setPreset("custom");
                setFrom(e.target.value);
              }}
            />
          </div>
          <div>
            <label
              htmlFor="reports-date-to"
              className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-600"
            >
              To
            </label>
            <TextInput
              id="reports-date-to"
              type="date"
              value={to}
              onChange={(e) => {
                setPreset("custom");
                setTo(e.target.value);
              }}
            />
          </div>
          <p className="pb-2 text-xs text-slate-500">
            Inclusive store dates; API executes [start, next-day) UTC boundaries.
          </p>
        </CardBody>
      </Card>
      {busy ? <LoadingState label="Loading owner analytics..." /> : null}
      {firstError ? <ErrorState message={firstError.message} /> : null}
      {overview.data ? (
        <>
          <Card>
            <CardHeader>
              <CardTitle>Financial snapshot</CardTitle>
            </CardHeader>
            <CardBody>
              <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
                <SummaryStat
                  label="Completed sales"
                  value={overview.data.current.completedSaleCount.toLocaleString()}
                  note="Committed sales inside the selected store-date period."
                />
                <SummaryStat
                  label="Average ticket"
                  value={peso(averageTicketCents)}
                  note="Completed sales total divided by completed sale count."
                />
                <SummaryStat
                  label="Cash realization"
                  value={percent(cashRealizationPercent)}
                  note="Cash recorded on sales ÷ completed sales value; excludes later AR collections."
                  tone="positive"
                />
                <SummaryStat
                  label="AR outstanding"
                  value={
                    ar.data
                      ? peso(ar.data.outstandingCents)
                      : ar.isLoading
                        ? "Loading..."
                        : isForbidden(ar.error)
                          ? "Entitlement required"
                          : "Unavailable"
                  }
                  note={
                    ar.data
                      ? "Open receivable stock at the report boundary."
                      : ar.isLoading
                        ? "Loading Accounts Receivable summary..."
                        : isForbidden(ar.error)
                          ? "Requires Receivables.View and signed U1 entitlement."
                          : (ar.error as Error | null)?.message || "Failed to load AR analytics."
                  }
                  tone={
                    ar.data && ar.data.outstandingCents > 0
                      ? "warning"
                      : ar.isError && !isForbidden(ar.error)
                        ? "warning"
                        : "neutral"
                  }
                />
                <SummaryStat
                  label="AR collections"
                  value={
                    ar.data
                      ? peso(ar.data.collectionsInPeriodCents)
                      : ar.isLoading
                        ? "Loading..."
                        : isForbidden(ar.error)
                          ? "Entitlement required"
                          : "Unavailable"
                  }
                  note={
                    ar.isError && !isForbidden(ar.error)
                      ? ((ar.error as Error | null)?.message || "Failed to load AR collections.")
                      : "Collections received during the selected period; not sales cash."
                  }
                  tone={ar.isError && !isForbidden(ar.error) ? "warning" : "neutral"}
                />
              </div>
            </CardBody>
          </Card>
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            <Metric
              label="Sales"
              value={overview.data.current.salesTotalCents}
              prior={overview.data.prior.salesTotalCents}
              onOpen={() => onOpenSales?.(from, to)}
            />
            <Metric
              label="Cash on sales"
              value={overview.data.current.cashOnSalesCents}
              prior={overview.data.prior.cashOnSalesCents}
            />
            <Metric
              label="Utang originated"
              value={overview.data.current.creditOriginatedCents}
              prior={overview.data.prior.creditOriginatedCents}
            />
            <Metric
              label="Estimated gross margin"
              value={overview.data.margin.estimatedGrossMarginCents}
            />
          </div>
          {!overview.data.margin.isAvailable ? (
            <Alert tone="warning" title="Gross margin unavailable">
              {overview.data.margin.basis} Cost coverage is {percent(costCoveragePercent)} ({overview.data.margin.costedItemCount} of {costCoverageTotal} item rows). Missing historical cost on{" "}
              {overview.data.margin.missingCostItemCount} item row(s). Salryn will not substitute today&apos;s catalog cost because that would rewrite the economics of completed sales.
            </Alert>
          ) : (
            <Alert tone="info">
              Estimated gross margin only; this is not net profit.{" "}
              {overview.data.margin.basis}
            </Alert>
          )}
          <div className="grid gap-3 sm:grid-cols-2">
            <Card>
              <CardBody>
                <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">
                  Voided sales (not presumed refunds)
                </p>
                <p className="mt-1 text-2xl font-bold tabular-nums text-slate-950">
                  {overview.data.current.voidedSaleCount}
                </p>
              </CardBody>
            </Card>
            <Card>
              <CardBody>
                <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">
                  Low-stock products (current stock)
                </p>
                <p className="mt-1 text-2xl font-bold tabular-nums text-slate-950">
                  {overview.data.lowStockCount}
                </p>
              </CardBody>
            </Card>
          </div>
          <div className="grid gap-4 xl:grid-cols-2">
            <Card className="col-span-1 xl:col-span-2">
              <CardHeader>
                <CardTitle>Sales trend</CardTitle>
              </CardHeader>
              <CardBody>
                {trend.isError ? (
                  <Alert tone="danger" title="Sales trend unavailable">
                    {(trend.error as Error).message}
                  </Alert>
                ) : (
                  <SalesTrendChart
                    points={trend.data?.points ?? []}
                    onSelectDay={(day) => onOpenSales?.(day, day)}
                  />
                )}
              </CardBody>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Payment Mix Breakdown</CardTitle>
              </CardHeader>
              <CardBody>
                {mix.isError ? (
                  <Alert tone="danger" title="Payment mix unavailable">
                    {(mix.error as Error).message}
                  </Alert>
                ) : (
                  <PaymentMixChart rows={mix.data?.rows ?? []} />
                )}
              </CardBody>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Sales vs Void Audit Activity</CardTitle>
              </CardHeader>
              <CardBody>
                {trend.isError ? (
                  <Alert tone="danger" title="Sales vs void activity unavailable">
                    {(trend.error as Error).message}
                  </Alert>
                ) : (
                  <SalesVsVoidChart
                    points={trend.data?.points ?? []}
                    onSelectDay={(day) => onOpenSales?.(day, day)}
                  />
                )}
              </CardBody>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Top Products by Revenue</CardTitle>
              </CardHeader>
              <CardBody>
                {top.isError ? (
                  <Alert tone="danger" title="Top products unavailable">
                    {(top.error as Error).message}
                  </Alert>
                ) : (
                  <TopProductsChart products={top.data?.products ?? []} />
                )}
              </CardBody>
            </Card>

            <Bars
              title="Current category classification"
              error={top.isError ? (top.error as Error).message : undefined}
              rows={(top.data?.categories ?? []).map((r) => ({
                label: r.currentCategory,
                value: r.lineRevenueCents,
              }))}
            />
          </div>
          <p className="text-xs text-slate-500">
            As of {overview.data.scope.asOfUtc} · {overview.data.scope.timeZoneId}.{" "}
            {overview.data.scope.moneySemantics}{" "}
            {top.data?.classificationDisclosure}
          </p>
        </>
      ) : null}
      {ar.isError ? (
        isForbidden(ar.error) ? (
          <Alert tone="warning" title="AR analytics unavailable">
            AR requires both Receivables.View and the signed U1 entitlement. No
            zero value is inferred.
          </Alert>
        ) : (
          <Alert tone="danger" title="AR analytics failed to load">
            {(ar.error as Error).message}
          </Alert>
        )
      ) : ar.data ? (
        <Card>
          <CardHeader>
            <CardTitle>Accounts Receivable aging</CardTitle>
          </CardHeader>
          <CardBody>
            <ArAgingDistributionChart
              buckets={ar.data.buckets}
              onSelectBucket={() => onOpenReceivables?.()}
            />
            <p className="mt-3 text-xs text-slate-500">
              Outstanding {peso(ar.data.outstandingCents)} is stock as of the
              report boundary. Collections{" "}
              {peso(ar.data.collectionsInPeriodCents)} are period flow.{" "}
              {ar.data.allocationDisclosure}
            </p>
          </CardBody>
        </Card>
      ) : null}
      {!canViewPayables || (ap.isError && isForbidden(ap.error)) ? (
        <Alert tone="warning" title="AP analytics unavailable">
          Accounts Payable aging requires Payables.View. No zero value is
          inferred.
        </Alert>
      ) : ap.isError ? (
        <Alert tone="danger" title="AP analytics failed to load">
          {(ap.error as Error).message}
        </Alert>
      ) : ap.data ? (
        <Card>
          <CardHeader>
            <div>
              <CardTitle>CURRENT outstanding AP — aging today</CardTitle>
              <p className="mt-0.5 text-xs text-slate-500">
                Accounts Payable aging snapshot ({manilaToday} Asia/Manila · captured {apSnapshotTimestamp})
              </p>
            </div>
          </CardHeader>
          <CardBody>
            <ArAgingDistributionChart
              buckets={[
                { key: "current", label: "Current", outstandingCents: ap.data.currentBucketCents },
                { key: "days30", label: "1–30 days", outstandingCents: ap.data.days1To30Cents },
                { key: "days60", label: "31–60 days", outstandingCents: ap.data.days31To60Cents },
                { key: "days90", label: "61–90 days", outstandingCents: ap.data.days61To90Cents },
                { key: "older", label: "Over 90 days", outstandingCents: ap.data.daysOver90Cents },
              ]}
              onSelectBucket={() => onOpenPayables?.()}
            />
            {ap.data.supplierSummaries.length === 0 ? (
              <p className="mt-3 text-xs text-slate-500">
                No open posted supplier payables remaining as of today ({manilaToday}, Asia/Manila).
              </p>
            ) : (
              <div className="mt-4 overflow-x-auto">
                <table className="w-full text-left text-xs">
                  <thead className="border-b border-slate-200 text-slate-500">
                    <tr>
                      <th className="py-1.5">Supplier</th>
                      <th className="py-1.5 text-right">Current</th>
                      <th className="py-1.5 text-right">1–30</th>
                      <th className="py-1.5 text-right">31–60</th>
                      <th className="py-1.5 text-right">61–90</th>
                      <th className="py-1.5 text-right">90+</th>
                      <th className="py-1.5 text-right">Total</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100 tabular-nums">
                    {ap.data.supplierSummaries.map((row) => (
                      <tr key={row.supplierId}>
                        <td className="py-1.5 font-medium text-slate-900">{row.supplierName}</td>
                        <td className="py-1.5 text-right">{peso(row.currentCents)}</td>
                        <td className="py-1.5 text-right">{peso(row.days1To30Cents)}</td>
                        <td className="py-1.5 text-right">{peso(row.days31To60Cents)}</td>
                        <td className="py-1.5 text-right">{peso(row.days61To90Cents)}</td>
                        <td className="py-1.5 text-right">{peso(row.over90DaysCents)}</td>
                        <td className="py-1.5 text-right font-semibold">{peso(row.totalOutstandingCents)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
            <p className="mt-3 text-xs text-slate-500">
              CURRENT outstanding AP {peso(ap.data.totalOpenPayablesCents)} across{" "}
              {ap.data.openInvoiceCount} posted open invoice(s) (
              {ap.data.overdueInvoiceCount} overdue;{" "}
              {ap.data.varianceFlaggedInvoiceCount} unposted variance-flagged
              excluded) is current remaining posted liability aged by due date
              as of today ({manilaToday} Asia/Manila, snapshot {apSnapshotTimestamp}),
              independent of the historical report date range above. Balances
              reflect posted invoices less applied supplier-return credits and
              confirmed settlements; pending payment requests are not deducted.
            </p>
          </CardBody>
        </Card>
      ) : null}
      <CafeManagementReportsPanel />
    </div>
  );
}
