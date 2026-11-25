import { useEffect, useRef, useState, type KeyboardEvent } from "react";
import { Download } from "lucide-react";
import { hostRequest } from "../../api/hostClient";
import {
  isLicenseActiveForBootstrap,
  type HostLicenseBootstrapStatus,
  type HostSession,
} from "../../cashier/types";
import type { LowStockItem } from "../../backoffice/types";
import {
  AlertBanner,
  PageHeader,
  R3ToastProvider,
  StatusPill,
  formatManilaTime,
  formatMoney,
  formatQty,
  useR3Toast,
} from "../components/R3Primitives";
import {
  buildScopedReportCsv,
  hasAuthoritativeProfit,
  readScopedCafeReport,
  type ReportPeriod,
  type ScopedCafeReport,
} from "./scopedReports";
import "../styles/r3-design-system.css";

const periods = [
  { key: "daily", label: "Today" },
  { key: "weekly", label: "This Week" },
  { key: "monthly", label: "This Month" },
] as const;

type Tab = "overview" | "recipes" | "cash-ar" | "stock";

const money = (value: number | null | undefined) =>
  Number.isSafeInteger(value) ? formatMoney(value as number) : "Unavailable";

async function verifyHostAccess(): Promise<void> {
  const [license, session] = await Promise.all([
    hostRequest<HostLicenseBootstrapStatus>("/api/license/status"),
    hostRequest<HostSession>("/api/session"),
  ]);
  if (!isLicenseActiveForBootstrap(license)) {
    throw new Error("Active Host license required. Return to the workstation to activate.");
  }
  if (!session.isAuthenticated || !session.user?.permissions.includes("Reports.View")) {
    throw new Error("An authenticated operator with Reports.View is required.");
  }
}

function formatMarginPct(numerator: number, denominator: number): string | null {
  if (!Number.isSafeInteger(numerator) || !Number.isSafeInteger(denominator) || denominator <= 0) {
    return null;
  }
  return `${(Math.round((numerator / denominator) * 1000) / 10).toFixed(1)}%`;
}

function R3LiveReportsContent() {
  const { pushToast } = useR3Toast();
  const [period, setPeriod] = useState<ReportPeriod>("monthly");
  const [tab, setTab] = useState<Tab>("overview");
  const [snapshot, setSnapshot] = useState<{
    period: ReportPeriod;
    report: ScopedCafeReport;
  } | null>(null);
  const [stock, setStock] = useState<LowStockItem[] | null>(null);
  const [stockError, setStockError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [retry, setRetry] = useState(0);
  const tabRefs = useRef<Partial<Record<Tab, HTMLButtonElement | null>>>({});

  useEffect(() => {
    let active = true;
    setSnapshot(null);
    setStock(null);
    setStockError(null);
    setError(null);
    setLoading(true);
    void (async () => {
      try {
        await verifyHostAccess();
        const report = await readScopedCafeReport(period);
        if (!active) return;
        setSnapshot({ period, report });
        setLoading(false);
        try {
          const rows = await hostRequest<LowStockItem[]>("/api/cafe/inventory/low-stock");
          if (active) setStock(rows);
        } catch {
          if (active) {
            setStockError("Low stock data could not be retrieved or is not permitted.");
          }
        }
      } catch (e) {
        if (active) {
          setError(e instanceof Error ? e.message : "Host report unavailable.");
          setLoading(false);
        }
      }
    })();
    return () => {
      active = false;
    };
  }, [period, retry]);

  // Never display the previous period after switching before its request resolves.
  const report = snapshot?.period === period && !error ? snapshot.report : null;
  const p = report?.profitability;
  const authoritative = report ? hasAuthoritativeProfit(report) : false;
  const label = periods.find((x) => x.key === period)?.label ?? period;

  const tabs: Array<{ key: Tab; label: string }> = [
    { key: "overview", label: "Profitability Waterfall" },
    { key: "recipes", label: `Recipe Costing (${report ? report.recipeCosts.length : 0})` },
    { key: "cash-ar", label: "Cash & AR" },
    { key: "stock", label: `Low Stock (${stock !== null ? stock.length : "—"})` },
  ];

  function selectTabWithFocus(next: Tab) {
    setTab(next);
    const el = tabRefs.current[next];
    if (el) {
      el.focus();
      el.scrollIntoView?.({ block: "nearest", inline: "nearest" });
    }
  }

  function handleTabKeyDown(event: KeyboardEvent<HTMLButtonElement>, currentIndex: number) {
    if (event.key === "ArrowRight") {
      event.preventDefault();
      selectTabWithFocus(tabs[(currentIndex + 1) % tabs.length].key);
    } else if (event.key === "ArrowLeft") {
      event.preventDefault();
      selectTabWithFocus(tabs[(currentIndex - 1 + tabs.length) % tabs.length].key);
    } else if (event.key === "Home") {
      event.preventDefault();
      selectTabWithFocus(tabs[0].key);
    } else if (event.key === "End") {
      event.preventDefault();
      selectTabWithFocus(tabs[tabs.length - 1].key);
    }
  }

  const cogsPct = p ? formatMarginPct(p.historicalCogsCents, p.recognizedNetRevenueCents) : null;
  const grossMarginPct = p ? formatMarginPct(p.grossProfitCents, p.recognizedNetRevenueCents) : null;
  const contributionMarginPct = p
    ? formatMarginPct(p.operationalContributionCents, p.recognizedNetRevenueCents)
    : null;

  const waterfallRows = p
    ? [
        {
          label: "Recognized Net Revenue",
          cents: p.recognizedNetRevenueCents,
          barColor: "var(--r3-nav-bg)",
          sign: "",
          bold: false,
        },
        {
          label: "Less: Historical COGS",
          cents: p.historicalCogsCents,
          barColor: "#c86a32",
          sign: "− ",
          bold: false,
        },
        {
          label: "Gross Profit",
          cents: p.grossProfitCents,
          barColor: "var(--r3-success-solid)",
          sign: "",
          bold: true,
        },
        {
          label: "Less: Operational Expenses",
          cents: p.operationalExpenseCents,
          barColor: "#c88318",
          sign: "− ",
          bold: false,
        },
        {
          label: "Less: Recognized Waste Cost",
          cents: p.recognizedWasteCostCents,
          barColor: "var(--r3-danger-solid)",
          sign: "− ",
          bold: false,
        },
        {
          label: "Operational Contribution (Not Net Income)",
          cents: p.operationalContributionCents,
          barColor: "#1e4d78",
          sign: "",
          bold: true,
        },
      ]
    : [];

  function exportCsv() {
    if (!report) return;
    const csv = buildScopedReportCsv(label, report);
    const url = URL.createObjectURL(new Blob([csv], { type: "text/csv;charset=utf-8" }));
    const a = document.createElement("a");
    a.href = url;
    a.download = `salryn-cafe-live-${period}.csv`;
    a.click();
    URL.revokeObjectURL(url);
    pushToast(`Exported ${label} live Host CSV summary.`);
  }

  const syncIssues = report?.syncCompleteness.issues ?? [];

  return (
    <main className="r3-app">
      <div className="r3-content">
        <PageHeader
          title="Reports, COGS & Profitability"
          subtitle="Calendar-period sales and historical costs directly from the live Host (Asia/Manila)."
          breadcrumb={<a href="/cafe/">Return to existing workstation</a>}
          actions={
            <div style={{ display: "flex", gap: 6, flexWrap: "wrap" }}>
              {periods.map((x) => (
                <button
                  key={x.key}
                  type="button"
                  aria-pressed={period === x.key}
                  className={`r3-btn r3-btn--sm ${period === x.key ? "r3-btn--primary" : "r3-btn--secondary"}`}
                  onClick={() => setPeriod(x.key)}
                >
                  {x.label}
                </button>
              ))}
              <button
                type="button"
                className="r3-btn r3-btn--secondary r3-btn--sm"
                disabled={!report}
                onClick={exportCsv}
              >
                <Download size={14} aria-hidden="true" /> Export CSV
              </button>
            </div>
          }
        />

        <AlertBanner tone="info">
          Development integration route. Reads the live Host with no sample fallback.{" "}
          {report ? report.officialBoundary : "Not an official/BIR/RMO report."}
        </AlertBanner>

        {error && (
          <AlertBanner
            tone="danger"
            title="Host report unavailable"
            action={
              <button
                type="button"
                className="r3-btn r3-btn--secondary r3-btn--sm"
                onClick={() => setRetry((x) => x + 1)}
              >
                Retry
              </button>
            }
          >
            {error}
          </AlertBanner>
        )}

        {loading && !report && !error && (
          <div className="r3-card" style={{ padding: 24, display: "grid", gap: 10 }}>
            <div>Loading financial reports…</div>
            <progress aria-label="Loading financial reports" style={{ width: "100%", maxWidth: 260 }} />
          </div>
        )}

        {report && p && (
          <>
            <AlertBanner
              tone={authoritative ? "success" : "warning"}
              title={
                authoritative
                  ? `Authoritative profitability (${report.costCoverage.costCompleteSaleCount}/${report.costCoverage.revenueSaleCount} sales complete)`
                  : `Provisional profitability — not final (${p.authority})`
              }
            >
              <div>
                Authority: <strong>{p.authority}</strong> · Cost coverage:{" "}
                <strong>{report.costCoverage.state}</strong> (
                {report.costCoverage.costCompleteSaleCount}/{report.costCoverage.revenueSaleCount}{" "}
                sales complete) · Waste coverage:{" "}
                <strong>{report.waste.costCoverage.state}</strong> · Sync:{" "}
                <strong>{report.syncCompleteness.state}</strong>.
              </div>
              {p.authorityReasons.length > 0 && (
                <div style={{ marginTop: 4 }}>{p.authorityReasons.join("; ")}</div>
              )}
              {syncIssues.length > 0 && (
                <div style={{ marginTop: 4 }}>
                  Sync notes: {syncIssues.map((issue) => `${issue.code} (${issue.count})`).join(", ")}
                </div>
              )}
            </AlertBanner>

            <div className="r3-metrics-grid r3-metrics-grid--2x2-mobile">
              <div className="r3-metric-card r3-metric-card--accent">
                <span className="r3-metric-label">Recognized Net Revenue ({label})</span>
                <span className="r3-metric-value r3-tabular">{money(p.recognizedNetRevenueCents)}</span>
                <span className="r3-metric-sub">
                  {report.sales.completedSaleCount} sales · Gross{" "}
                  {money(report.sales.recognizedCompletedSaleRevenueCents)}
                  {report.sales.committedRefundCents > 0
                    ? ` · Refunds − ${money(report.sales.committedRefundCents)}`
                    : ""}
                </span>
              </div>

              <div className="r3-metric-card r3-metric-card--warn">
                <span className="r3-metric-label">Historical COGS ({label})</span>
                <span className="r3-metric-value r3-tabular">{money(p.historicalCogsCents)}</span>
                <span className="r3-metric-sub">
                  {cogsPct ? `${cogsPct} of net revenue` : "No net revenue in period"} ·{" "}
                  {authoritative ? "Authoritative" : "Provisional"}
                </span>
              </div>

              <div className="r3-metric-card r3-metric-card--success">
                <span className="r3-metric-label">Gross Profit ({label})</span>
                <span className="r3-metric-value r3-tabular">{money(p.grossProfitCents)}</span>
                <span className="r3-metric-sub">
                  {grossMarginPct ? (
                    <>
                      <strong>{grossMarginPct}</strong> gross margin · Excludes rent &amp; labor
                    </>
                  ) : (
                    "Excludes rent & labor"
                  )}
                </span>
              </div>

              <div className="r3-metric-card r3-metric-card--info">
                <span className="r3-metric-label">Operating Contribution</span>
                <span className="r3-metric-value r3-tabular">
                  {money(p.operationalContributionCents)}
                </span>
                <span className="r3-metric-sub">
                  {contributionMarginPct ? (
                    <>
                      <strong>{contributionMarginPct}</strong> after expenses &amp; waste (not net
                      income)
                    </>
                  ) : (
                    "After expenses & waste (not net income)"
                  )}
                </span>
              </div>
            </div>

            <div className="r3-tabs" role="tablist" aria-label="Report breakdowns">
              {tabs.map((x, idx) => (
                <button
                  key={x.key}
                  ref={(node) => {
                    tabRefs.current[x.key] = node;
                  }}
                  id={`r3-report-tab-${x.key}`}
                  type="button"
                  role="tab"
                  aria-selected={tab === x.key}
                  tabIndex={tab === x.key ? 0 : -1}
                  className="r3-tab"
                  onClick={() => setTab(x.key)}
                  onKeyDown={(event) => handleTabKeyDown(event, idx)}
                >
                  {x.label}
                </button>
              ))}
            </div>

            {tab === "overview" && (
              <div
                role="tabpanel"
                aria-labelledby="r3-report-tab-overview"
                tabIndex={0}
                className="r3-card"
              >
                <div className="r3-card-header" style={{ flexWrap: "wrap" }}>
                  <div>
                    <h2 className="r3-card-title">Contribution Waterfall — {label} (Asia/Manila)</h2>
                    <div style={{ fontSize: 12, color: "var(--r3-ink-muted)", marginTop: 2 }}>
                      Window: {formatManilaTime(report.rangeStartUtc)} to{" "}
                      {formatManilaTime(report.rangeEndUtc)} (exclusive)
                    </div>
                  </div>
                  <span style={{ fontSize: 12.5, color: "var(--r3-ink-muted)" }}>
                    Generated {formatManilaTime(report.hostGeneratedAtUtc)}
                  </span>
                </div>
                <div className="r3-card-body" style={{ display: "flex", flexDirection: "column", gap: 10 }}>
                  {waterfallRows.map((row) => {
                    const base = Math.max(
                      Math.abs(p.recognizedNetRevenueCents),
                      Math.abs(p.historicalCogsCents),
                      Math.abs(p.operationalExpenseCents),
                      Math.abs(p.recognizedWasteCostCents),
                      Math.abs(p.operationalContributionCents),
                    );
                    const pct =
                      base > 0 && row.cents !== 0
                        ? Math.max(2, Math.min(100, Math.round((Math.abs(row.cents) / base) * 100)))
                        : 0;
                    return (
                      <div
                        key={row.label}
                        className={`r3-waterfall-row ${row.bold ? "r3-waterfall-row--bold" : ""}`}
                      >
                        <div style={{ fontWeight: row.bold ? 700 : 500, fontSize: 13.5 }}>
                          {row.label}
                        </div>
                        <div className="r3-stock-bar" style={{ height: 10, marginTop: 0 }} aria-hidden="true">
                          <div
                            className="r3-stock-fill"
                            style={{ width: `${pct}%`, background: row.barColor }}
                          />
                        </div>
                        <div className="r3-num" style={{ fontWeight: row.bold ? 800 : 600 }}>
                          {row.sign}
                          {money(row.cents)}
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>
            )}

            {tab === "recipes" && (
              <div role="tabpanel" aria-labelledby="r3-report-tab-recipes">
                <div
                  className="r3-table-wrap"
                  tabIndex={0}
                  role="region"
                  aria-labelledby="r3-recipe-table-caption"
                >
                  <table className="r3-table">
                    <caption id="r3-recipe-table-caption" className="r3-sr-only">
                      Published recipe costing and coverage
                    </caption>
                    <thead>
                      <tr>
                        <th scope="col">Recipe</th>
                        <th scope="col" className="r3-num">
                          Yield
                        </th>
                        <th scope="col" className="r3-num">
                          Known Cost
                        </th>
                        <th scope="col">Cost Coverage</th>
                      </tr>
                    </thead>
                    <tbody>
                      {report.recipeCosts.length === 0 ? (
                        <tr>
                          <td colSpan={4} style={{ textAlign: "center", color: "var(--r3-ink-muted)" }}>
                            No published recipe cost rows returned by Host.
                          </td>
                        </tr>
                      ) : (
                        report.recipeCosts.map((r) => {
                          const isComplete = r.costCoverage.toLowerCase() === "complete";
                          return (
                            <tr key={r.recipeId}>
                              <th scope="row" style={{ fontWeight: 700, textTransform: "none", letterSpacing: 0, fontSize: 13.5, background: "transparent", color: "var(--r3-ink)" }}>
                                {r.recipeName}
                              </th>
                              <td className="r3-num">{r.yieldX1000 / 1000} srv</td>
                              <td className="r3-num" style={{ fontWeight: 700 }}>
                                {isComplete
                                  ? money(r.knownCostCents)
                                  : `${money(r.knownCostCents)} (partial)`}
                              </td>
                              <td>
                                <StatusPill tone={isComplete ? "success" : "warning"}>
                                  {r.costCoverage}
                                </StatusPill>
                              </td>
                            </tr>
                          );
                        })
                      )}
                    </tbody>
                  </table>
                </div>
              </div>
            )}

            {tab === "cash-ar" && (
              <div
                role="tabpanel"
                aria-labelledby="r3-report-tab-cash-ar"
                tabIndex={0}
                style={{ display: "flex", flexDirection: "column", gap: 14 }}
              >
                <AlertBanner tone="info" title="Cash & AR Scope Notice">
                  Cash flow and range collection rows reflect the selected{" "}
                  <strong>{label}</strong> Host reporting window. Outstanding and overdue Accounts
                  Receivable reflect current point-in-time customer ledger balances. Operators
                  without <code>Receivables.View</code> see explicit redactions rather than ₱0.00.
                </AlertBanner>
                <div className="r3-two-col">
                  <div className="r3-card">
                    <div className="r3-card-header">
                      <h2 className="r3-card-title">Cash Flow — {label}</h2>
                      <StatusPill tone="neutral">Period Scoped</StatusPill>
                    </div>
                    <div className="r3-card-body" style={{ display: "grid", gap: 8 }}>
                      {report.cash ? (
                        <>
                          <div style={{ display: "flex", justifyContent: "space-between", gap: 12 }}>
                            <span>Sale Cash Receipts</span>
                            <strong className="r3-tabular">
                              {money(report.cash.saleCashReceiptCents)}
                            </strong>
                          </div>
                          <div style={{ display: "flex", justifyContent: "space-between", gap: 12 }}>
                            <span>AR Cash Collections</span>
                            <strong className="r3-tabular">
                              {money(report.cash.receivableCashCollectionCents)}
                            </strong>
                          </div>
                          <div style={{ display: "flex", justifyContent: "space-between", gap: 12 }}>
                            <span>Cash In</span>
                            <strong className="r3-tabular">{money(report.cash.cashInCents)}</strong>
                          </div>
                          <div style={{ display: "flex", justifyContent: "space-between", gap: 12 }}>
                            <span>Inventory Purchase Cash Out</span>
                            <strong className="r3-tabular">
                              − {money(report.cash.inventoryPurchaseCashOutCents)}
                            </strong>
                          </div>
                          <div style={{ display: "flex", justifyContent: "space-between", gap: 12 }}>
                            <span>Operational Expense Cash Out</span>
                            <strong className="r3-tabular">
                              − {money(report.cash.operationalExpenseCashOutCents)}
                            </strong>
                          </div>
                          <div style={{ display: "flex", justifyContent: "space-between", gap: 12 }}>
                            <span>Cash Refunds</span>
                            <strong className="r3-tabular">
                              − {money(report.cash.cashRefundCents)}
                            </strong>
                          </div>
                          <div style={{ display: "flex", justifyContent: "space-between", gap: 12 }}>
                            <span>Generic Cash Out</span>
                            <strong className="r3-tabular">
                              − {money(report.cash.genericCashOutCents)}
                            </strong>
                          </div>
                          <div style={{ display: "flex", justifyContent: "space-between", gap: 12 }}>
                            <span>Reversals (Net)</span>
                            <strong className="r3-tabular">
                              {money(report.cash.reversalNetCents)}
                            </strong>
                          </div>
                          <div
                            style={{
                              display: "flex",
                              justifyContent: "space-between",
                              gap: 12,
                              paddingTop: 8,
                              borderTop: "1px solid var(--r3-border-subtle)",
                            }}
                          >
                            <strong>Net Cash Delta</strong>
                            <strong className="r3-tabular">
                              {money(report.cash.netCashDeltaCents)}
                            </strong>
                          </div>
                        </>
                      ) : (
                        <p style={{ margin: 0, color: "var(--r3-ink-secondary)" }}>
                          Unavailable or not authorized.
                        </p>
                      )}
                    </div>
                  </div>

                  <div className="r3-card">
                    <div className="r3-card-header">
                      <h2 className="r3-card-title">Accounts Receivable (Utang)</h2>
                      <StatusPill tone="info">As of Now</StatusPill>
                    </div>
                    <div className="r3-card-body" style={{ display: "grid", gap: 8 }}>
                      {report.accountsReceivable ? (
                        <>
                          <div style={{ display: "flex", justifyContent: "space-between", gap: 12 }}>
                            <span>Outstanding Balance (Current)</span>
                            <strong className="r3-tabular">
                              {money(report.accountsReceivable.outstandingBalanceCents)}
                            </strong>
                          </div>
                          <div style={{ display: "flex", justifyContent: "space-between", gap: 12 }}>
                            <span>Credit Debits ({label})</span>
                            <strong className="r3-tabular">
                              {money(report.accountsReceivable.debitCentsInRange)}
                            </strong>
                          </div>
                          <div style={{ display: "flex", justifyContent: "space-between", gap: 12 }}>
                            <span>Credits Applied ({label})</span>
                            <strong className="r3-tabular">
                              {money(report.accountsReceivable.creditCentsInRange)}
                            </strong>
                          </div>
                          <div style={{ display: "flex", justifyContent: "space-between", gap: 12 }}>
                            <span>Collections ({label})</span>
                            <strong className="r3-tabular">
                              {money(report.accountsReceivable.cashCollectionCentsInRange)}
                            </strong>
                          </div>
                          {report.receivablesSummary && (
                            <div
                              style={{
                                display: "flex",
                                justifyContent: "space-between",
                                gap: 12,
                                paddingTop: 8,
                                borderTop: "1px solid var(--r3-border-subtle)",
                              }}
                            >
                              <span>Overdue Balance (Current)</span>
                              <strong className="r3-tabular">
                                {money(report.receivablesSummary.overdueCents)}
                              </strong>
                            </div>
                          )}
                        </>
                      ) : (
                        <p style={{ margin: 0, color: "var(--r3-ink-secondary)" }}>
                          Unavailable or not authorized.
                        </p>
                      )}
                    </div>
                  </div>
                </div>
              </div>
            )}

            {tab === "stock" && (
              <div role="tabpanel" aria-labelledby="r3-report-tab-stock">
                {stockError ? (
                  <AlertBanner tone="warning">{stockError}</AlertBanner>
                ) : stock === null ? (
                  <div className="r3-card" style={{ padding: 20 }}>
                    Loading stock data…
                  </div>
                ) : (
                  <div
                    className="r3-table-wrap"
                    tabIndex={0}
                    role="region"
                    aria-labelledby="r3-low-stock-table-caption"
                  >
                    <table className="r3-table">
                      <caption id="r3-low-stock-table-caption" className="r3-sr-only">
                        Low stock items below reorder threshold
                      </caption>
                      <thead>
                        <tr>
                          <th scope="col">Stock Item</th>
                          <th scope="col" className="r3-num">
                            On Hand
                          </th>
                          <th scope="col" className="r3-num">
                            Reorder Point
                          </th>
                          <th scope="col">Status</th>
                        </tr>
                      </thead>
                      <tbody>
                        {stock.length === 0 ? (
                          <tr>
                            <td colSpan={4} style={{ textAlign: "center", color: "var(--r3-ink-muted)" }}>
                              No low-stock items returned by Host.
                            </td>
                          </tr>
                        ) : (
                          stock.map((r) => (
                            <tr key={`${r.stockTargetType}:${r.stockTargetId}`}>
                              <th scope="row" style={{ fontWeight: 700, textTransform: "none", letterSpacing: 0, fontSize: 13.5, background: "transparent", color: "var(--r3-ink)" }}>
                                {r.name}
                              </th>
                              <td className="r3-num" style={{ fontWeight: 700 }}>
                                {formatQty(r.stockOnHandX1000, r.stockUnit)}
                              </td>
                              <td className="r3-num">
                                {formatQty(r.reorderLevelX1000, r.stockUnit)}
                              </td>
                              <td>
                                <StatusPill tone="warning">Below Reorder</StatusPill>
                              </td>
                            </tr>
                          ))
                        )}
                      </tbody>
                    </table>
                  </div>
                )}
              </div>
            )}
          </>
        )}
      </div>
    </main>
  );
}

export function R3LiveReportsPage() {
  return (
    <R3ToastProvider>
      <R3LiveReportsContent />
    </R3ToastProvider>
  );
}
