import { useEffect, useState } from "react";
import { Download } from "lucide-react";
import { hostRequest } from "../../api/hostClient";
import { isLicenseActiveForBootstrap, type HostSession, type HostLicenseBootstrapStatus } from "../../cashier/types";
import type { LowStockItem } from "../../backoffice/types";
import { AlertBanner, PageHeader, StatusPill, formatMoney, formatQty, formatManilaTime } from "../components/R3Primitives";
import { readScopedCafeReport, hasAuthoritativeProfit, type ScopedCafeReport, type ReportPeriod } from "./scopedReports";
import "../styles/r3-design-system.css";

const periods = [{ key: "daily", label: "Today" }, { key: "weekly", label: "This Week" }, { key: "monthly", label: "This Month" }] as const;
type Tab = "overview" | "recipes" | "cash-ar" | "stock";
const tabs: {key: Tab; label: string}[] = [{key:"overview",label:"Profitability Waterfall"},{key:"recipes",label:"Recipe Costing"},{key:"cash-ar",label:"Cash & AR"},{key:"stock",label:"Low Stock"}];
const money = (value: number | null | undefined) => Number.isSafeInteger(value) ? formatMoney(value as number) : "Unavailable";

async function verifyHostAccess(): Promise<void> {
  const [license, session] = await Promise.all([
    hostRequest<HostLicenseBootstrapStatus>("/api/license/status"),
    hostRequest<HostSession>("/api/session"),
  ]);
  if (!isLicenseActiveForBootstrap(license)) throw new Error("Active Host license required. Return to the workstation to activate.");
  if (!session.isAuthenticated || !session.user?.permissions.includes("Reports.View")) {
    throw new Error("An authenticated operator with Reports.View is required.");
  }
}

export function R3LiveReportsPage() {
  const [period, setPeriod] = useState<ReportPeriod>("monthly");
  const [tab, setTab] = useState<Tab>("overview");
  const [snapshot, setSnapshot] = useState<{period: ReportPeriod; report: ScopedCafeReport} | null>(null);
  const [stock, setStock] = useState<LowStockItem[] | null>(null);
  const [stockError, setStockError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    let active = true;
    setSnapshot(null); setStock(null); setStockError(null); setError(null); setLoading(true);
    void (async () => {
      try {
        await verifyHostAccess();
        const report = await readScopedCafeReport(period);
        if (!active) return;
        setSnapshot({period,report}); setLoading(false);
        try {
          const rows = await hostRequest<LowStockItem[]>("/api/cafe/inventory/low-stock");
          if (active) setStock(rows);
        } catch {
          if (active) setStockError("Low stock data could not be retrieved or is not permitted.");
        }
      } catch (e) {
        if (active) { setError(e instanceof Error ? e.message : "Host report unavailable."); setLoading(false); }
      }
    })();
    return () => { active = false; };
  }, [period,retry]);
  // Never display the previous period after switching before its request resolves.
  const report = snapshot?.period === period && !error ? snapshot.report : null;
  const p = report?.profitability;
  const authoritative = report ? hasAuthoritativeProfit(report) : false;
  const label = periods.find(x => x.key === period)?.label ?? period;
  const financialRows: [string, number | undefined][] = [
    ["Recognized Net Revenue", p?.recognizedNetRevenueCents],
    ["Historical COGS", p?.historicalCogsCents],
    ["Gross Profit", p?.grossProfitCents],
    ["Operational Expenses", p?.operationalExpenseCents],
    ["Recognized Waste Cost", p?.recognizedWasteCostCents],
    ["Operational Contribution (Not Net Income)", p?.operationalContributionCents],
  ];
  function exportCsv() {
    if (!report || !p) return;
    const rows = [["Metric","Amount (PHP)","Range Start UTC","Range End UTC","Host Authority","Cost Coverage","Sync Completeness"],
      ...financialRows.map(([name, cents]) => [name, cents === undefined ? "Unavailable" : (cents/100).toFixed(2),
        report.rangeStartUtc, report.rangeEndUtc, p.authority, report.costCoverage.state, report.syncCompleteness.state])];
    const csv = rows.map(row => row.map(v => '"' + String(v).replace(/"/g,'""') + '"').join(",")).join("\r\n");
    const url = URL.createObjectURL(new Blob([csv],{type:"text/csv;charset=utf-8"}));
    const a = document.createElement("a"); a.href=url; a.download="salryn-cafe-live-"+period+".csv"; a.click(); URL.revokeObjectURL(url);
  }
  return <main className="r3-app" style={{padding:"clamp(12px,3vw,32px)"}}>
    <PageHeader title="Reports, COGS & Profitability" subtitle="Real Host integration proof · Asia/Manila calendar periods"
      breadcrumb={<a href="/cafe/">Return to existing workstation</a>}
      actions={<>{periods.map(x => <button key={x.key} type="button" aria-pressed={period===x.key}
        className={"r3-btn r3-btn--sm "+(period===x.key?"r3-btn--primary":"r3-btn--secondary")}
        onClick={()=>setPeriod(x.key)}>{x.label}</button>)}
        <button type="button" className="r3-btn r3-btn--secondary r3-btn--sm" disabled={!report} onClick={exportCsv}><Download size={14}/> Export CSV</button></>}/>
    <AlertBanner tone="info">Development integration route. Reads the actual Host; no sample fallback. Not an official BIR report.</AlertBanner>
    {error && <AlertBanner tone="danger" title="Host report unavailable" action={<button className="r3-btn r3-btn--secondary" onClick={()=>setRetry(x=>x+1)}>Retry</button>}>{error}</AlertBanner>}
    {loading && !report && !error && <p role="status">Loading financial reports…</p>}
    {report && p && <>
      <AlertBanner tone={authoritative?"success":"warning"} title={authoritative?"Authoritative profitability":"Provisional profitability — not final"}>
        {p.authority} · Cost coverage: {report.costCoverage.state} · Sync: {report.syncCompleteness.state}.
        {" "}{p.authorityReasons.join("; ")}
      </AlertBanner>
      <p>{label} · {formatManilaTime(report.rangeStartUtc)} to {formatManilaTime(report.rangeEndUtc)} (exclusive) · Generated {formatManilaTime(report.generatedAtUtc)}</p>
      <div className="r3-metrics-grid">
        {[["Recognized Net Revenue",p.recognizedNetRevenueCents],["Historical COGS",p.historicalCogsCents],["Gross Profit",p.grossProfitCents],["Operating Contribution",p.operationalContributionCents]].map(([name,value])=>
          <div className="r3-metric-card" key={name}><span className="r3-metric-label">{name}</span>
          <span className="r3-metric-value">{money(value as number)}</span><span className="r3-metric-sub">{name==="Recognized Net Revenue"?"Host reported net revenue":authoritative?"Authoritative":"Provisional · see coverage"}</span></div>)}
      </div>
      <div className="r3-tabs" role="tablist" aria-label="Report breakdowns">{tabs.map(x=><button key={x.key} type="button" role="tab" aria-selected={tab===x.key} className="r3-tab" onClick={()=>setTab(x.key)}>{x.label}</button>)}</div>
      {tab==="overview" && <div className="r3-card"><div className="r3-card-header"><h2 className="r3-card-title">Contribution Waterfall — {label}</h2></div><div className="r3-card-body" style={{display:"grid",gap:12}}>
        {financialRows.map(([name,value])=><div key={name} style={{display:"flex",justifyContent:"space-between",gap:12}}><span>{name}</span><strong>{money(value)}</strong></div>)}
        </div></div>}
      {tab==="recipes" && <div className="r3-table-wrap"><table className="r3-table"><thead><tr><th>Recipe</th><th>Yield</th><th>Known Cost</th><th>Coverage</th></tr></thead><tbody>
        {report.recipeCosts.map(r=><tr key={r.recipeId}><td>{r.recipeName}</td><td>{r.yieldX1000/1000}</td><td>{money(r.knownCostCents)}</td><td><StatusPill tone={r.costCoverage==="Complete"?"success":"warning"}>{r.costCoverage}</StatusPill></td></tr>)}
        </tbody></table></div>}
      {tab==="cash-ar" && <div className="r3-two-col">
        <div className="r3-card"><div className="r3-card-header"><h2 className="r3-card-title">Cash Flow — {label}</h2></div><div className="r3-card-body">
          {report.cash ? <><p>Sale cash receipts: {money(report.cash.saleCashReceiptCents)}</p><p>AR cash collections: {money(report.cash.receivableCashCollectionCents)}</p><p>Cash refunds: {money(report.cash.cashRefundCents)}</p><p>Net cash delta: {money(report.cash.netCashDeltaCents)}</p></> : <p>Unavailable or not authorized.</p>}
        </div></div>
        <div className="r3-card"><div className="r3-card-header"><h2 className="r3-card-title">Accounts Receivable (Utang)</h2></div><div className="r3-card-body">
          {report.accountsReceivable ? <><p>Outstanding balance (current): {money(report.accountsReceivable.outstandingBalanceCents)}</p><p>Collections ({label}): {money(report.accountsReceivable.cashCollectionCentsInRange)}</p></> : <p>Unavailable or not authorized.</p>}
        </div></div></div>}
      {tab==="stock" && (stockError ? <AlertBanner tone="warning">{stockError}</AlertBanner> : stock===null ? <p>Loading stock data…</p> : <div className="r3-table-wrap"><table className="r3-table"><thead><tr><th>Stock Item</th><th>On Hand</th><th>Reorder Point</th></tr></thead><tbody>
        {stock.length===0?<tr><td colSpan={3}>No low-stock items returned by Host.</td></tr>:stock.map(r=><tr key={r.stockTargetType+":"+r.stockTargetId}><td>{r.name}</td><td>{formatQty(r.stockOnHandX1000,r.stockUnit)}</td><td>{formatQty(r.reorderLevelX1000,r.stockUnit)}</td></tr>)}
        </tbody></table></div>)}
    </>}
  </main>;
}
