import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { formatPhp, hostRequest, parsePhpToCents } from "../../api/hostClient";
import {
  isLicenseActiveForBootstrap,
  type HostLicenseBootstrapStatus,
  type HostSession,
} from "../../cashier/types";
import "../styles/r3-design-system.css";
import { R3ToastProvider, useR3Toast } from "../components/R3Primitives";
import { useDurableIntent } from "./durableIntent";
import {
  readCashDrawerHistory,
  readCurrentCashDrawer,
  recordCashDrawerMovement,
  reviewCashDrawerVariance,
  type CashDrawerEventType,
  type CashDrawerSnapshot,
  type DrawerMovementCommand,
  type DrawerReviewCommand,
} from "./cashDrawerContracts";

type TabId = "history" | "drawer";

type ReviewPayload = {
  sessionId: string;
  command: DrawerReviewCommand;
};

function hasPerm(session: HostSession | null, perm: string): boolean {
  return (session?.user?.permissions ?? []).includes(perm);
}

async function verifyHostCashAccess(): Promise<HostSession> {
  const license = await hostRequest<HostLicenseBootstrapStatus>("/api/license/status", undefined, "GET");
  if (!isLicenseActiveForBootstrap(license)) {
    throw new Error("Active Host license required. Return to the workstation to activate.");
  }
  const session = await hostRequest<HostSession>("/api/session", undefined, "GET");
  if (!session?.isAuthenticated || !session.user) {
    throw new Error("Sign in through the workstation before opening Cash Drawer & Shifts.");
  }
  if (!hasPerm(session, "Audit.View") && !hasPerm(session, "Shift.OpenClose")) {
    throw new Error("Insufficient permissions: Audit.View or Shift.OpenClose is required.");
  }
  return session;
}

function R3LiveCashShiftsContent() {
  const { pushToast } = useR3Toast();
  const [session, setSession] = useState<HostSession | null>(null);
  const [loading, setLoading] = useState(true);
  const [accessError, setAccessError] = useState<string | null>(null);
  const [tab, setTab] = useState<TabId>("history");

  const [currentDrawer, setCurrentDrawer] = useState<CashDrawerSnapshot | null>(null);
  const [history, setHistory] = useState<CashDrawerSnapshot[]>([]);

  // Cash Drawer Movement form
  const [moveType, setMoveType] = useState<CashDrawerEventType>("CashIn");
  const [moveAmountPhp, setMoveAmountPhp] = useState("");
  const [moveReason, setMoveReason] = useState("");
  const [moveMessage, setMoveMessage] = useState<string | null>(null);
  const moveIntent = useDurableIntent<DrawerMovementCommand, CashDrawerSnapshot>("cash-drawer-movement");

  // Independent Variance Review form (INT-08)
  const [reviewSessionId, setReviewSessionId] = useState("");
  const [reviewReason, setReviewReason] = useState("");
  const [reviewMessage, setReviewMessage] = useState<string | null>(null);
  const reviewIntent = useDurableIntent<ReviewPayload, CashDrawerSnapshot>("cash-drawer-review");

  const tabRefs = useRef<Partial<Record<TabId, HTMLButtonElement | null>>>({});

  const loadAll = useCallback(async () => {
    setLoading(true);
    setAccessError(null);
    try {
      const verified = await verifyHostCashAccess();
      setSession(verified);

      const canAudit = hasPerm(verified, "Audit.View");
      const canShift = hasPerm(verified, "Shift.OpenClose");

      const [histPage, openSnap] = await Promise.all([
        canAudit ? readCashDrawerHistory(50, 0) : Promise.resolve({ items: [], limit: 50, offset: 0 }),
        canShift ? readCurrentCashDrawer().catch(() => null) : Promise.resolve(null),
      ]);

      setHistory(histPage.items);
      setCurrentDrawer(openSnap);

      const pendingReview = histPage.items.find(
        (s) => s.reviewStatus === "PendingReview" && (s.varianceCents ?? 0) !== 0,
      );
      if (pendingReview) {
        setReviewSessionId((prev) => prev || pendingReview.sessionId);
      }

      if (!canAudit && canShift) setTab("drawer");
      else if (canAudit && !canShift) setTab("history");
    } catch (err) {
      setAccessError(err instanceof Error ? err.message : "Cash Drawer & Shifts unavailable.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadAll();
  }, [loadAll]);

  const canAudit = hasPerm(session, "Audit.View");
  const canShift = hasPerm(session, "Shift.OpenClose");

  const tabs = useMemo(
    () =>
      [
        { id: "history" as TabId, label: `Closed Shift History & Variance Review (${history.length})`, enabled: canAudit },
        { id: "drawer" as TabId, label: "Open Drawer Movements", enabled: canShift },
      ].filter((t) => t.enabled),
    [history.length, canAudit, canShift],
  );

  const handleTabKeyDown = (e: React.KeyboardEvent<HTMLButtonElement>, idx: number) => {
    let nextIdx: number | null = null;
    if (e.key === "ArrowRight") nextIdx = (idx + 1) % tabs.length;
    else if (e.key === "ArrowLeft") nextIdx = (idx - 1 + tabs.length) % tabs.length;
    else if (e.key === "Home") nextIdx = 0;
    else if (e.key === "End") nextIdx = tabs.length - 1;
    if (nextIdx !== null) {
      e.preventDefault();
      setTab(tabs[nextIdx].id);
      tabRefs.current[tabs[nextIdx].id]?.focus();
    }
  };

  const handleMovementSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setMoveMessage(null);
    let amountCents: number;
    try {
      amountCents = parsePhpToCents(moveAmountPhp);
    } catch (err) {
      setMoveMessage(err instanceof Error ? err.message : "Invalid amount.");
      return;
    }
    if (amountCents <= 0 || !moveReason.trim()) {
      setMoveMessage("Positive amount and reason are required.");
      return;
    }

    const res = await moveIntent.execute(
      { eventType: moveType, amountCents, reason: moveReason.trim() },
      () => ({
        eventType: moveType,
        amountCents,
        reason: moveReason.trim(),
        clientRequestId: crypto.randomUUID(),
      }),
      recordCashDrawerMovement,
    );

    if (res.ok) {
      setCurrentDrawer(res.value);
      setMoveAmountPhp("");
      setMoveReason("");
      const msg = `${moveType} movement recorded (${formatPhp(amountCents)}).`;
      setMoveMessage(msg);
      pushToast(msg);
    } else {
      setMoveMessage(`${res.code ? `[${res.code}] ` : ""}${res.message}`);
    }
  };

  const handleReviewSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setReviewMessage(null);
    if (!reviewSessionId || !reviewReason.trim()) {
      setReviewMessage("Select a shift with variance and enter an acknowledgement reason.");
      return;
    }

    const res = await reviewIntent.execute(
      { sessionId: reviewSessionId, decision: "Acknowledged", reason: reviewReason.trim() },
      () => ({
        sessionId: reviewSessionId,
        command: {
          decision: "Acknowledged",
          reason: reviewReason.trim(),
          clientRequestId: crypto.randomUUID(),
        },
      }),
      (p) => reviewCashDrawerVariance(p.sessionId, p.command),
    );

    if (res.ok) {
      setReviewReason("");
      await loadAll();
      const msg = `Variance acknowledged for shift ${res.value.sessionId.slice(0, 8)}… (${res.value.reviewStatus}).`;
      setReviewMessage(msg);
      pushToast(msg);
    } else {
      setReviewMessage(`${res.code ? `[${res.code}] ` : ""}${res.message}`);
    }
  };

  const pendingVarianceShifts = history.filter(
    (s) => s.reviewStatus === "PendingReview" && (s.varianceCents ?? 0) !== 0,
  );

  return (
    <div className="r3-root">
      <div className="r3-content" style={{ maxWidth: 1200, margin: "0 auto" }}>
        <div className="r3-page-header">
          <div>
            <p className="r3-text-xs r3-text-muted" style={{ marginBottom: 4 }}>
              <a href="/cafe/">Return to existing workstation</a>
              {" · "}
              <a href="/cafe/integration/r3/reports">Live Reports</a>
              {" · "}
              <a href="/cafe/integration/r3/inventory">Live Inventory</a>
              {" · "}
              <a href="/cafe/integration/r3/bookings">Bookings &amp; Deliveries</a>
            </p>
            <h1 className="r3-page-title">Cash Drawer &amp; Independent Variance Review</h1>
            <p className="r3-page-subtitle">
              Live Host drawer reconciliation history and non-self variance acknowledgement (Audit.View enforced).
            </p>
          </div>
          <div className="r3-page-actions">
            <button type="button" className="r3-btn r3-btn--outline r3-btn--sm" onClick={() => void loadAll()} disabled={loading}>
              {loading ? "Refreshing…" : "Refresh Live Drawer"}
            </button>
          </div>
        </div>

        {accessError && (
          <div className="r3-alert r3-alert--danger" role="alert">
            <div className="r3-alert-body">
              <div className="r3-alert-title">Cash Drawer unavailable</div>
              <div>{accessError}</div>
            </div>
          </div>
        )}

        {!accessError && session && (
          <>
            <div className="r3-tabs" role="tablist" aria-label="Cash drawer sections">
              {tabs.map((t, idx) => (
                <button
                  key={t.id}
                  id={`r3-cash-tab-${t.id}`}
                  ref={(el) => {
                    tabRefs.current[t.id] = el;
                  }}
                  type="button"
                  role="tab"
                  aria-selected={tab === t.id}
                  tabIndex={tab === t.id ? 0 : -1}
                  className={`r3-tab${tab === t.id ? " r3-tab--active" : ""}`}
                  onClick={() => setTab(t.id)}
                  onKeyDown={(e) => handleTabKeyDown(e, idx)}
                >
                  {t.label}
                </button>
              ))}
            </div>

            {tab === "history" && canAudit && (
              <div role="tabpanel" aria-labelledby="r3-cash-tab-history" style={{ display: "grid", gap: 16 }}>
                <div className="r3-card">
                  <div className="r3-card-header">
                    <div>
                      <div className="r3-card-title">Closed Shift Reconciliations</div>
                      <div className="r3-card-subtitle">Directly from GET /api/cash-drawer/history</div>
                    </div>
                  </div>
                  <div className="r3-table-wrap" tabIndex={0} role="region" aria-label="Closed Shift Reconciliations">
                    <table className="r3-table">
                      <caption className="r3-sr-only">Closed Shift Reconciliations</caption>
                      <thead>
                        <tr>
                          <th scope="col">Shift Session</th>
                          <th scope="col">Expected</th>
                          <th scope="col">Counted</th>
                          <th scope="col">Variance</th>
                          <th scope="col">Review Status</th>
                        </tr>
                      </thead>
                      <tbody>
                        {history.length === 0 ? (
                          <tr>
                            <td colSpan={5} className="r3-text-muted">No closed shift reconciliations found.</td>
                          </tr>
                        ) : (
                          history.map((row) => (
                            <tr key={row.sessionId}>
                              <th scope="row"><code>{row.sessionId.slice(0, 8)}…</code></th>
                              <td className="r3-tabular">{row.expectedCashCents !== null ? formatPhp(row.expectedCashCents) : "—"}</td>
                              <td className="r3-tabular">{row.countedCashCents !== null ? formatPhp(row.countedCashCents) : "—"}</td>
                              <td className="r3-tabular">{row.varianceCents !== null ? formatPhp(row.varianceCents) : "—"}</td>
                              <td>
                                <span className={`r3-badge ${row.reviewStatus === "PendingReview" ? "r3-badge--warn" : "r3-badge--success"}`}>
                                  {row.reviewStatus}
                                </span>
                              </td>
                            </tr>
                          ))
                        )}
                      </tbody>
                    </table>
                  </div>
                </div>

                <div className="r3-card">
                  <div className="r3-card-header">
                    <div>
                      <div className="r3-card-title">Independent Variance Acknowledgement (INT-08)</div>
                      <div className="r3-card-subtitle">
                        Non-self reviewer enforced by Host (submitting operator receives 409 CashDrawer.SelfReviewDenied).
                      </div>
                    </div>
                  </div>
                  {reviewIntent.pending && (
                    <div className={`r3-alert ${reviewIntent.pending.status === "rejected" ? "r3-alert--danger" : "r3-alert--warn"}`} role="alert" style={{ marginBottom: 12 }}>
                      <div className="r3-alert-body">
                        Review request <code>{reviewIntent.pending.payload.command.clientRequestId}</code> ({reviewIntent.pending.status})
                        {reviewIntent.pending.lastErrorCode ? ` [${reviewIntent.pending.lastErrorCode}]` : ""}
                        {reviewIntent.pending.lastErrorMessage ? ` — ${reviewIntent.pending.lastErrorMessage}` : ""}
                      </div>
                      {reviewIntent.pending.status === "rejected" ? (
                        <button type="button" className="r3-btn r3-btn--outline r3-btn--sm" onClick={() => reviewIntent.clearPending(false)}>
                          Clear Rejected Review
                        </button>
                      ) : (
                        <button
                          type="button"
                          className="r3-btn r3-btn--primary r3-btn--sm"
                          onClick={() => void reviewIntent.retryPending((p) => reviewCashDrawerVariance(p.sessionId, p.command)).then((r) => { if (r.ok) void loadAll(); })}
                        >
                          Retry Exact Review
                        </button>
                      )}
                    </div>
                  )}
                  <form onSubmit={(e) => void handleReviewSubmit(e)} style={{ display: "grid", gap: 12, maxWidth: 560 }}>
                    <label>
                      <div className="r3-text-xs r3-text-muted">Shift Pending Review</div>
                      <select className="r3-input" aria-label="Shift Pending Review" value={reviewSessionId} onChange={(e) => setReviewSessionId(e.target.value)}>
                        <option value="">Select shift…</option>
                        {pendingVarianceShifts.map((s) => (
                          <option key={s.sessionId} value={s.sessionId}>
                            {s.sessionId.slice(0, 8)}… (Variance: {formatPhp(s.varianceCents ?? 0)})
                          </option>
                        ))}
                      </select>
                    </label>
                    <label>
                      <div className="r3-text-xs r3-text-muted">Independent Review Reason</div>
                      <input className="r3-input" aria-label="Independent Review Reason" value={reviewReason} onChange={(e) => setReviewReason(e.target.value)} placeholder="Verified short count against petty cash slip" />
                    </label>
                    <div>
                      <button type="submit" className="r3-btn r3-btn--primary" disabled={reviewIntent.inFlight}>
                        Acknowledge Variance
                      </button>
                    </div>
                  </form>
                  {reviewMessage && <p role="status" style={{ marginTop: 12 }} className="r3-text-sm">{reviewMessage}</p>}
                </div>
              </div>
            )}

            {tab === "drawer" && canShift && (
              <div role="tabpanel" aria-labelledby="r3-cash-tab-drawer" className="r3-card">
                <div className="r3-card-header">
                  <div>
                    <div className="r3-card-title">Open Shift Cash Movements</div>
                    <div className="r3-card-subtitle">
                      {currentDrawer
                        ? `Open shift ${currentDrawer.sessionId.slice(0, 8)}… (Opening float: ${formatPhp(currentDrawer.openingCashCents)}; blind close hides expected balance until close)`
                        : "No open shift for current operator."}
                    </div>
                  </div>
                </div>
                {moveIntent.pending && (
                  <div className={`r3-alert ${moveIntent.pending.status === "rejected" ? "r3-alert--danger" : "r3-alert--warn"}`} role="alert" style={{ marginBottom: 12 }}>
                    <div className="r3-alert-body">
                      Movement request <code>{moveIntent.pending.payload.clientRequestId}</code> ({moveIntent.pending.status})
                      {moveIntent.pending.lastErrorMessage ? ` — ${moveIntent.pending.lastErrorMessage}` : ""}
                    </div>
                    {moveIntent.pending.status === "rejected" ? (
                      <button type="button" className="r3-btn r3-btn--outline r3-btn--sm" onClick={() => moveIntent.clearPending(false)}>
                        Clear Rejected Movement
                      </button>
                    ) : (
                      <button
                        type="button"
                        className="r3-btn r3-btn--primary r3-btn--sm"
                        onClick={() => void moveIntent.retryPending(recordCashDrawerMovement).then((r) => { if (r.ok) setCurrentDrawer(r.value); })}
                      >
                        Retry Exact Movement
                      </button>
                    )}
                  </div>
                )}
                <form onSubmit={(e) => void handleMovementSubmit(e)} style={{ display: "grid", gap: 12, maxWidth: 560 }}>
                  <label>
                    <div className="r3-text-xs r3-text-muted">Movement Type</div>
                    <select className="r3-input" aria-label="Movement Type" value={moveType} onChange={(e) => setMoveType(e.target.value as CashDrawerEventType)}>
                      <option value="CashIn">CashIn</option>
                      <option value="CashOut">CashOut</option>
                      <option value="CashRefund">CashRefund</option>
                    </select>
                  </label>
                  <label>
                    <div className="r3-text-xs r3-text-muted">Amount (PHP)</div>
                    <input className="r3-input" aria-label="Movement Amount" value={moveAmountPhp} onChange={(e) => setMoveAmountPhp(e.target.value)} placeholder="50.00" />
                  </label>
                  <label>
                    <div className="r3-text-xs r3-text-muted">Reason</div>
                    <input className="r3-input" aria-label="Movement Reason" value={moveReason} onChange={(e) => setMoveReason(e.target.value)} placeholder="Change fund top-up" />
                  </label>
                  <div>
                    <button type="submit" className="r3-btn r3-btn--primary" disabled={moveIntent.inFlight}>
                      Record Cash Movement
                    </button>
                  </div>
                </form>
                {moveMessage && <p role="status" style={{ marginTop: 12 }} className="r3-text-sm">{moveMessage}</p>}
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
}

export function R3LiveCashShiftsPage() {
  return (
    <R3ToastProvider>
      <R3LiveCashShiftsContent />
    </R3ToastProvider>
  );
}
