import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  analyticsExportUrl,
  fetchAnalyticsOverview,
  fetchArAging,
  fetchPaymentMix,
  fetchSalesTrend,
  fetchTopProducts,
} from "../api/ownerAnalytics";
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
  return (
    <Card>
      <CardBody>
        <button
          type="button"
          className="w-full text-left"
          onClick={onOpen}
          disabled={!onOpen}
        >
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
        </button>
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
  onSelect,
}: {
  title: string;
  rows: { label: string; value: number }[];
  onSelect?: (label: string) => void;
}) {
  const max = Math.max(1, ...rows.map((row) => row.value));
  return (
    <Card>
      <CardHeader>
        <CardTitle>{title}</CardTitle>
      </CardHeader>
      <CardBody>
        {rows.length === 0 ? (
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
}: {
  onOpenSales?: (from: string, to: string) => void;
  onOpenReceivables?: () => void;
}) {
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
  const busy =
    overview.isLoading || trend.isLoading || mix.isLoading || top.isLoading;
  const firstError = [
    overview.error,
    trend.error,
    mix.error,
    top.error,
  ].find(Boolean) as Error | undefined;
  const trendRows = useMemo(
    () =>
      (trend.data?.points ?? []).map((p) => ({
        label: p.storeDate,
        value: p.salesTotalCents,
      })),
    [trend.data],
  );
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
                  value={ar.data ? peso(ar.data.outstandingCents) : "Entitlement required"}
                  note={ar.data ? "Open receivable stock at the report boundary." : "Requires Receivables.View and signed U1 entitlement."}
                  tone={ar.data && ar.data.outstandingCents > 0 ? "warning" : "neutral"}
                />
                <SummaryStat
                  label="AR collections"
                  value={ar.data ? peso(ar.data.collectionsInPeriodCents) : "Entitlement required"}
                  note="Collections received during the selected period; not sales cash."
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
          <div className="grid gap-3 xl:grid-cols-2">
            <Bars
              title="Sales trend"
              rows={trendRows}
              onSelect={(day) => onOpenSales?.(day, day)}
            />
            <Bars
              title="Cash received by store day"
              rows={(trend.data?.points ?? []).map((p) => ({
                label: p.storeDate,
                value: p.cashOnSalesCents,
              }))}
              onSelect={(day) => onOpenSales?.(day, day)}
            />
            <Bars
              title="Utang originated by store day"
              rows={(trend.data?.points ?? []).map((p) => ({
                label: p.storeDate,
                value: p.creditOriginatedCents,
              }))}
              onSelect={(day) => onOpenSales?.(day, day)}
            />
            <Bars
              title="Payment mix"
              rows={(mix.data?.rows ?? []).map((r) => ({
                label: `${r.method} — ${r.scope}`,
                value: r.amountCents,
              }))}
            />
            <Bars
              title="Top products (historical label)"
              rows={(top.data?.products ?? []).map((r) => ({
                label: r.historicalLabel,
                value: r.lineRevenueCents,
              }))}
            />
            <Bars
              title="Current category classification"
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
        <Alert tone="warning" title="AR analytics unavailable">
          AR requires both Receivables.View and the signed U1 entitlement. No
          zero value is inferred.
        </Alert>
      ) : ar.data ? (
        <Card>
          <CardHeader>
            <CardTitle>Accounts Receivable aging</CardTitle>
          </CardHeader>
          <CardBody>
            <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
              {ar.data.buckets.map((b) => (
                <button
                  type="button"
                  onClick={onOpenReceivables}
                  key={b.key}
                  className="rounded-lg border border-slate-200 bg-slate-50/70 p-3 text-left transition-colors hover:border-blue-300 hover:bg-blue-50/40"
                >
                  <p className="text-xs font-medium text-slate-500">{b.label}</p>
                  <p className="mt-1 font-bold tabular-nums text-slate-950">
                    {peso(b.outstandingCents)}
                  </p>
                </button>
              ))}
            </div>
            <p className="mt-3 text-xs text-slate-500">
              Outstanding {peso(ar.data.outstandingCents)} is stock as of the
              report boundary. Collections{" "}
              {peso(ar.data.collectionsInPeriodCents)} are period flow.{" "}
              {ar.data.allocationDisclosure}
            </p>
          </CardBody>
        </Card>
      ) : null}
    </div>
  );
}
