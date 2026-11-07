import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { analyticsExportUrl, fetchAnalyticsOverview, fetchArAging, fetchPaymentMix, fetchSalesTrend, fetchTopProducts } from "../api/ownerAnalytics";
import { Alert, Card, CardBody, CardHeader, CardTitle, CommandBar, ErrorState, LoadingState, StatusPill, TextInput } from "../components/ui";
import { formatMoney } from "../utils/format";

const peso = (cents: number | null | undefined) => cents == null ? "Unavailable" : formatMoney(cents / 100);
const iso = (date: Date) => date.toLocaleDateString("en-CA", { timeZone: "Asia/Manila" });
const shift = (days: number) => { const d = new Date(); d.setUTCDate(d.getUTCDate() + days); return iso(d); };
type Preset = "today" | "yesterday" | "7" | "30" | "90" | "month" | "last-month" | "custom";
function presetRange(preset: Preset) {
  const today = iso(new Date());
  if (preset === "today") return [today, today];
  if (preset === "yesterday") return [shift(-1), shift(-1)];
  if (preset === "7") return [shift(-6), today];
  if (preset === "30") return [shift(-29), today];
  if (preset === "90") return [shift(-89), today];
  const [y, m] = today.split("-").map(Number);
  if (preset === "month") return [`${y}-${String(m).padStart(2,"0")}-01`, today];
  return [iso(new Date(Date.UTC(y, m - 2, 1))), iso(new Date(Date.UTC(y, m - 1, 0)))];
}
function Metric({ label, value, prior, onOpen }: { label: string; value: number | null; prior?: number; onOpen?: () => void }) {
  const delta = prior == null || value == null ? null : value - prior;
  return <Card><CardBody><button type="button" className="w-full text-left" onClick={onOpen} disabled={!onOpen}><p className="text-xs font-semibold uppercase text-slate-500">{label}</p><p className="mt-2 text-2xl font-bold text-slate-950">{peso(value)}</p>{delta != null ? <p className={`text-xs ${delta >= 0 ? "text-emerald-700" : "text-rose-700"}`}>{delta >= 0 ? "+" : ""}{peso(delta)} vs equal prior period</p> : null}</button></CardBody></Card>;
}
function Bars({ title, rows, onSelect }: { title: string; rows: { label: string; value: number }[]; onSelect?: (label: string) => void }) {
  const max = Math.max(1, ...rows.map(row => row.value));
  return <Card><CardHeader><CardTitle>{title}</CardTitle></CardHeader><CardBody>{rows.length === 0 ? <p className="text-sm text-slate-500">No completed sales in this period.</p> : <><div role="img" aria-label={`${title} bar chart`} className="space-y-3">{rows.map(row => <button type="button" disabled={!onSelect} onClick={() => onSelect?.(row.label)} className="block w-full text-left" key={row.label}><div className="flex justify-between gap-3 text-xs"><span>{row.label}</span><span>{peso(row.value)}</span></div><div className="mt-1 h-3 rounded bg-slate-100"><div className="h-3 rounded bg-blue-600" style={{ width: `${Math.max(2, row.value / max * 100)}%` }} /></div></button>)}</div><details className="mt-3"><summary className="cursor-pointer text-xs font-semibold text-blue-700">Tabular chart data</summary><table className="mt-2 w-full text-xs"><thead><tr><th className="text-left">Bucket</th><th className="text-right">Amount</th></tr></thead><tbody>{rows.map(row => <tr key={row.label}><td>{row.label}</td><td className="text-right">{peso(row.value)}</td></tr>)}</tbody></table></details></>}</CardBody></Card>;
}
export function OwnerReportsPage({ onOpenSales, onOpenReceivables }: { onOpenSales?: (from: string, to: string) => void; onOpenReceivables?: () => void }) {
  const initial = presetRange("7");
  const [preset, setPreset] = useState<Preset>("7"); const [from, setFrom] = useState(initial[0]); const [to, setTo] = useState(initial[1]);
  function choose(value: Preset) { setPreset(value); if (value !== "custom") { const range = presetRange(value); setFrom(range[0]); setTo(range[1]); } }
  const overview = useQuery({ queryKey: ["owner-analytics-overview", from, to], queryFn: () => fetchAnalyticsOverview(from, to), retry: false });
  const trend = useQuery({ queryKey: ["owner-analytics-trend", from, to], queryFn: () => fetchSalesTrend(from, to), retry: false });
  const mix = useQuery({ queryKey: ["owner-analytics-mix", from, to], queryFn: () => fetchPaymentMix(from, to), retry: false });
  const top = useQuery({ queryKey: ["owner-analytics-top", from, to], queryFn: () => fetchTopProducts(from, to), retry: false });
  const ar = useQuery({ queryKey: ["owner-analytics-ar", from, to], queryFn: () => fetchArAging(from, to), retry: false });
  const busy = overview.isLoading || trend.isLoading || mix.isLoading || top.isLoading;
  const firstError = [overview.error, trend.error, mix.error, top.error].find(Boolean) as Error | undefined;
  const trendRows = useMemo(() => (trend.data?.points ?? []).map(p => ({ label: p.storeDate, value: p.salesTotalCents })), [trend.data]);
  return <div className="space-y-4">
    <CommandBar title="Reports" subtitle="Owner analytics by store day in Asia/Manila; Read-only and bounded." status={<StatusPill tone="info">Reports.View</StatusPill>} actions={<><a className="rounded-lg border border-slate-300 px-3 py-2 text-sm" href={analyticsExportUrl("sales",from,to)}>Export sales CSV</a>{ar.data ? <a className="rounded-lg border border-slate-300 px-3 py-2 text-sm" href={analyticsExportUrl("ar",from,to)}>Export AR CSV</a> : null}</>} />
    <Card><CardBody className="flex flex-wrap items-end gap-3"><label className="text-xs font-semibold">Period<select className="mt-1 block rounded-lg border border-slate-300 px-3 py-2 text-sm" value={preset} onChange={e => choose(e.target.value as Preset)}>{[["today","Today"],["yesterday","Yesterday"],["7","Last 7 days"],["30","Last 30 days"],["90","Last 90 days"],["month","This month"],["last-month","Last month"],["custom","Custom"]].map(([v,l]) => <option key={v} value={v}>{l}</option>)}</select></label><label className="text-xs font-semibold">From<TextInput type="date" value={from} onChange={e => { setPreset("custom"); setFrom(e.target.value); }} /></label><label className="text-xs font-semibold">To<TextInput type="date" value={to} onChange={e => { setPreset("custom"); setTo(e.target.value); }} /></label><p className="pb-2 text-xs text-slate-500">Inclusive store dates; API executes [start, next-day) UTC boundaries.</p></CardBody></Card>
    {busy ? <LoadingState label="Loading owner analytics..." /> : null}{firstError ? <ErrorState message={firstError.message} /> : null}
    {overview.data ? <><div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4"><Metric label="Sales" value={overview.data.current.salesTotalCents} prior={overview.data.prior.salesTotalCents} onOpen={() => onOpenSales?.(from,to)} /><Metric label="Cash on sales" value={overview.data.current.cashOnSalesCents} prior={overview.data.prior.cashOnSalesCents} /><Metric label="Utang originated" value={overview.data.current.creditOriginatedCents} prior={overview.data.prior.creditOriginatedCents} /><Metric label="Estimated gross margin" value={overview.data.margin.estimatedGrossMarginCents} /></div>
      {!overview.data.margin.isAvailable ? <Alert tone="warning" title="Gross margin unavailable">{overview.data.margin.basis} Missing historical cost on {overview.data.margin.missingCostItemCount} item row(s); current catalog cost was not substituted.</Alert> : <Alert tone="info">Estimated gross margin only; this is not net profit. {overview.data.margin.basis}</Alert>}
      <div className="grid gap-3 sm:grid-cols-2"><Card><CardBody><p className="text-xs uppercase text-slate-500">Voided sales (not presumed refunds)</p><p className="text-2xl font-bold">{overview.data.current.voidedSaleCount}</p></CardBody></Card><Card><CardBody><p className="text-xs uppercase text-slate-500">Low-stock products (current stock)</p><p className="text-2xl font-bold">{overview.data.lowStockCount}</p></CardBody></Card></div>
      <div className="grid gap-3 xl:grid-cols-2"><Bars title="Sales trend" rows={trendRows} onSelect={day => onOpenSales?.(day,day)} /><Bars title="Cash received by store day" rows={(trend.data?.points ?? []).map(p => ({ label:p.storeDate,value:p.cashOnSalesCents }))} onSelect={day => onOpenSales?.(day,day)} /><Bars title="Utang originated by store day" rows={(trend.data?.points ?? []).map(p => ({ label:p.storeDate,value:p.creditOriginatedCents }))} onSelect={day => onOpenSales?.(day,day)} /><Bars title="Payment mix" rows={(mix.data?.rows ?? []).map(r => ({ label: `${r.method} — ${r.scope}`, value: r.amountCents }))} /><Bars title="Top products (historical label)" rows={(top.data?.products ?? []).map(r => ({ label: r.historicalLabel, value: r.lineRevenueCents }))} /><Bars title="Current category classification" rows={(top.data?.categories ?? []).map(r => ({ label: r.currentCategory, value: r.lineRevenueCents }))} /></div>
      <p className="text-xs text-slate-500">As of {overview.data.scope.asOfUtc} · {overview.data.scope.timeZoneId}. {overview.data.scope.moneySemantics} {top.data?.classificationDisclosure}</p></> : null}
    {ar.isError ? <Alert tone="warning" title="AR analytics unavailable">AR requires both Receivables.View and the signed U1 entitlement. No zero value is inferred.</Alert> : ar.data ? <Card><CardHeader><CardTitle>Accounts Receivable aging</CardTitle></CardHeader><CardBody><div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">{ar.data.buckets.map(b => <button type="button" onClick={onOpenReceivables} key={b.key} className="rounded border p-3 text-left"><p className="text-xs text-slate-500">{b.label}</p><p className="font-bold">{peso(b.outstandingCents)}</p></button>)}</div><p className="mt-3 text-xs text-slate-500">Outstanding {peso(ar.data.outstandingCents)} is stock as of the report boundary. Collections {peso(ar.data.collectionsInPeriodCents)} are period flow. {ar.data.allocationDisclosure}</p></CardBody></Card> : null}
  </div>;
}
