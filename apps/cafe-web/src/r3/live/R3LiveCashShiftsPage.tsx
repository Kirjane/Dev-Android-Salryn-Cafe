import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { formatPhp, HostApiError, hostRequest, parsePhpToCents } from "../../api/hostClient";
import {
  isLicenseActiveForBootstrap,
  type HostLicenseBootstrapStatus,
  type HostSession,
} from "../../cashier/types";
import "../styles/r3-design-system.css";
import { R3ToastProvider, useR3Toast } from "../components/R3Primitives";
import { DurableIntentBanner } from "./DurableIntentBanner";
import {
  fetchHostIdentityKey,
  useDurableIntent,
  type DurableIntentAuthority,
} from "./durableIntent";
import {
  closeCashShiftBlind,
  openCashShift,
  postCafeCashMovement,
  readCafeCashMovements,
  readCashDrawerHistory,
  readCurrentCashDrawer,
  recordCashDrawerMovement,
  reverseCafeCashMovement,
  reviewCashDrawerVariance,
  type CafeCashMovementLedgerRow,
  type CafeCashMovementType,
  type CashDrawerEventType,
  type CashDrawerSnapshot,
  type CloseShiftBlindCommand,
  type DrawerMovementCommand,
  type DrawerReviewCommand,
  type OpenShiftCommand,
  type PostCafeCashMovementCommand,
  type ReverseCafeCashMovementCommand,
  type ShiftOperationEnvelope,
} from "./cashDrawerContracts";

type TabId = "drawer" | "history";

type Props = {
  embedded?: boolean;
};

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

function R3LiveCashShiftsContent({ embedded = false }: Props) {
  const { pushToast } = useR3Toast();
  const [session, setSession] = useState<HostSession | null>(null);
  const [hostIdentityKey, setHostIdentityKey] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [accessError, setAccessError] = useState<string | null>(null);
  const [drawerError, setDrawerError] = useState<string | null>(null);
  const [tab, setTab] = useState<TabId>("drawer");

  const [currentDrawer, setCurrentDrawer] = useState<CashDrawerSnapshot | null>(null);
  const [history, setHistory] = useState<CashDrawerSnapshot[]>([]);

  const authority: DurableIntentAuthority | null = useMemo(
    () =>
      session?.user?.userId && hostIdentityKey
        ? { hostIdentityKey, actorUserId: session.user.userId }
        : null,
    [session?.user?.userId, hostIdentityKey],
  );

  // Shift Open form
  const [openingFloatPhp, setOpeningFloatPhp] = useState("1000.00");
  const [shiftMessage, setShiftMessage] = useState<string | null>(null);
  const openShiftIntent = useDurableIntent<OpenShiftCommand, ShiftOperationEnvelope>(
    "cash-shift-open",
    authority,
  );

  // Blind Shift Close form (expected cash NEVER displayed prior to close!)
  const [countedCashPhp, setCountedCashPhp] = useState("");
  const [physicalConfirmed, setPhysicalConfirmed] = useState(false);
  const [closeNote, setCloseNote] = useState("");
  const closeShiftIntent = useDurableIntent<CloseShiftBlindCommand, ShiftOperationEnvelope>(
    "cash-shift-close",
    authority,
  );

  // Cash Drawer Movement form
  const [moveType, setMoveType] = useState<CashDrawerEventType>("CashIn");
  const [moveAmountPhp, setMoveAmountPhp] = useState("");
  const [moveReason, setMoveReason] = useState("");
  const [moveMessage, setMoveMessage] = useState<string | null>(null);
  const moveIntent = useDurableIntent<DrawerMovementCommand, CashDrawerSnapshot>(
    "cash-drawer-movement",
    authority,
  );

  // C6 Canonical Cash / Expense Operations & Reversals (/api/cafe/cash/movements)
  const [c6Movements, setC6Movements] = useState<CafeCashMovementLedgerRow[]>([]);
  const [c6Type, setC6Type] = useState<CafeCashMovementType>("OperationalExpense");
  const [c6AmountPhp, setC6AmountPhp] = useState("");
  const [c6Reason, setC6Reason] = useState("");
  const [c6ReferenceId, setC6ReferenceId] = useState("");
  const [c6Message, setC6Message] = useState<string | null>(null);
  const c6PostIntent = useDurableIntent<PostCafeCashMovementCommand, unknown>(
    "cash-c6-post",
    authority,
  );
  const c6ReverseIntent = useDurableIntent<
    { originalMovementId: string; command: ReverseCafeCashMovementCommand },
    unknown
  >("cash-c6-reverse", authority);

  // Independent Variance Review form (INT-08)
  const [reviewSessionId, setReviewSessionId] = useState("");
  const [reviewReason, setReviewReason] = useState("");
  const [reviewMessage, setReviewMessage] = useState<string | null>(null);
  const reviewIntent = useDurableIntent<ReviewPayload, CashDrawerSnapshot>(
    "cash-drawer-review",
    authority,
  );

  const tabRefs = useRef<Partial<Record<TabId, HTMLButtonElement | null>>>({});

  const loadAll = useCallback(async () => {
    setLoading(true);
    setAccessError(null);
    setDrawerError(null);
    try {
      const [verified, idKey] = await Promise.all([
        verifyHostCashAccess(),
        fetchHostIdentityKey(),
      ]);
      setSession(verified);
      setHostIdentityKey(idKey);

      const canAudit = hasPerm(verified, "Audit.View");
      const canShift = hasPerm(verified, "Shift.OpenClose");

      const histPage = canAudit
        ? await readCashDrawerHistory(50, 0)
        : { items: [], limit: 50, offset: 0 };

      let openSnap: CashDrawerSnapshot | null = null;
      if (canShift) {
        try {
          openSnap = await readCurrentCashDrawer();
        } catch (err) {
          if (err instanceof HostApiError && err.status === 404) {
            openSnap = null;
          } else {
            setDrawerError(err instanceof Error ? err.message : "Failed to read current cash drawer.");
          }
        }
      }

      setHistory(histPage.items);
      setCurrentDrawer(openSnap);

      if (openSnap && hasPerm(verified, "Cash.View")) {
        readCafeCashMovements().then(setC6Movements).catch(() => setC6Movements([]));
      } else {
        setC6Movements([]);
      }

      const pendingReview = histPage.items.find(
        (s) => s.reviewStatus === "PendingReview" && (s.varianceCents ?? 0) !== 0,
      );
      setReviewSessionId((prev) => {
        if (prev && histPage.items.some((s) => s.sessionId === prev && s.reviewStatus === "PendingReview")) {
          return prev;
        }
        return pendingReview?.sessionId ?? "";
      });

      if (!canShift && canAudit) setTab("history");
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
  const canExpense = hasPerm(session, "Expenses.Record");
  const canCashReverse = hasPerm(session, "Cash.Reverse");

  const tabs = useMemo(
    () =>
      [
        { id: "drawer" as TabId, label: currentDrawer ? "Active Shift & Blind Close" : "Open Shift", enabled: canShift },
        { id: "history" as TabId, label: `Closed History & Variance Review (${history.length})`, enabled: canAudit },
      ].filter((t) => t.enabled),
    [currentDrawer, history.length, canAudit, canShift],
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

  const handleOpenShift = async (e: React.FormEvent) => {
    e.preventDefault();
    setShiftMessage(null);
    let openingCashCents: number;
    try {
      openingCashCents = parsePhpToCents(openingFloatPhp);
    } catch (err) {
      setShiftMessage(err instanceof Error ? err.message : "Invalid opening float.");
      return;
    }
    const res = await openShiftIntent.execute(
      { openingCashCents },
      () => ({ openingCashCents, clientRequestId: crypto.randomUUID() }),
      openCashShift,
    );
    if (res.ok) {
      await loadAll();
      const msg = `Cashier shift opened with ${formatPhp(openingCashCents)} float.`;
      setShiftMessage(msg);
      pushToast(msg);
    } else {
      setShiftMessage(`${res.code ? `[${res.code}] ` : ""}${res.message}`);
    }
  };

  const handleBlindCloseShift = async (e: React.FormEvent) => {
    e.preventDefault();
    setShiftMessage(null);
    let countedCashCents: number;
    try {
      countedCashCents = parsePhpToCents(countedCashPhp);
    } catch (err) {
      setShiftMessage(err instanceof Error ? err.message : "Invalid counted cash amount.");
      return;
    }
    if (!physicalConfirmed) {
      setShiftMessage("Confirm that you physically counted the cash drawer before closing blind.");
      return;
    }
    const note = closeNote.trim() || null;
    const res = await closeShiftIntent.execute(
      { countedCashCents, physicalCountConfirmed: true, note },
      () => ({
        countedCashCents,
        physicalCountConfirmed: true,
        clientRequestId: crypto.randomUUID(),
        note,
      }),
      closeCashShiftBlind,
    );
    if (res.ok) {
      setCountedCashPhp("");
      setPhysicalConfirmed(false);
      setCloseNote("");
      await loadAll();
      const msg = `Shift closed blind (counted ${formatPhp(countedCashCents)}).`;
      setShiftMessage(msg);
      pushToast(msg);
    } else {
      setShiftMessage(`${res.code ? `[${res.code}] ` : ""}${res.message}`);
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
      await loadAll();
    } else {
      setMoveMessage(`${res.code ? `[${res.code}] ` : ""}${res.message}`);
    }
  };

  const handleC6PostSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setC6Message(null);
    let amountCents: number;
    try {
      amountCents = parsePhpToCents(c6AmountPhp);
    } catch (err) {
      setC6Message(err instanceof Error ? err.message : "Invalid amount.");
      return;
    }
    if (amountCents <= 0 || !c6Reason.trim()) {
      setC6Message("Positive amount and reason are required.");
      return;
    }
    const refId = c6ReferenceId.trim() || null;
    const res = await c6PostIntent.execute(
      { movementType: c6Type, amountCents, reason: c6Reason.trim(), referenceId: refId },
      () => ({
        operationId: crypto.randomUUID(),
        movementId: crypto.randomUUID(),
        movementType: c6Type,
        amountCents,
        reason: c6Reason.trim(),
        referenceId: refId,
        occurredAtUtc: new Date().toISOString(),
      }),
      postCafeCashMovement,
    );
    if (res.ok) {
      setC6AmountPhp("");
      setC6Reason("");
      setC6ReferenceId("");
      const msg = `C6 ${c6Type} recorded (${formatPhp(amountCents)}).`;
      setC6Message(msg);
      pushToast(msg);
      await loadAll();
    } else {
      setC6Message(`${res.code ? `[${res.code}] ` : ""}${res.message}`);
    }
  };

  const handleC6Reverse = async (row: CafeCashMovementLedgerRow) => {
    setC6Message(null);
    const reason = `Reversal of ${row.movementType} ${row.movementId.slice(0, 8)}`;
    const res = await c6ReverseIntent.execute(
      { originalMovementId: row.movementId, reason },
      () => ({
        originalMovementId: row.movementId,
        command: {
          operationId: crypto.randomUUID(),
          movementId: crypto.randomUUID(),
          reason,
          occurredAtUtc: new Date().toISOString(),
        },
      }),
      (p) => reverseCafeCashMovement(p.originalMovementId, p.command),
    );
    if (res.ok) {
      const msg = `Movement ${row.movementId.slice(0, 8)}… reversed.`;
      setC6Message(msg);
      pushToast(msg);
      await loadAll();
    } else {
      setC6Message(`${res.code ? `[${res.code}] ` : ""}${res.message}`);
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
      setReviewSessionId("");
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
    <div className={embedded ? "" : "r3-root"}>
      <div className={embedded ? "" : "r3-content"} style={embedded ? undefined : { maxWidth: 1200, margin: "0 auto" }}>
        <div className="r3-page-header">
          <div>
            {!embedded && (
              <p className="r3-text-xs r3-text-muted" style={{ marginBottom: 4 }}>
                <a href="/cafe/">Return to existing workstation</a>
                {" · "}
                <a href="/cafe/r3">Unified R3 App</a>
              </p>
            )}
            <h1 className="r3-page-title">Cash Drawer &amp; Shift Reconciliation</h1>
            <p className="r3-page-subtitle">
              Full shift lifecycle: Open Shift, Cash Movements, Blind Shift Close, and Independent Non-Self Variance Review.
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

        {drawerError && (
          <div className="r3-alert r3-alert--danger" role="alert">
            <div className="r3-alert-body">{drawerError}</div>
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

            {tab === "drawer" && canShift && (
              <div role="tabpanel" aria-labelledby="r3-cash-tab-drawer" style={{ display: "grid", gap: 16 }}>
                {/* Always render pending/corrupt shift banners outside the open/closed branch so reloads never hide unresolved intents */}
                <DurableIntentBanner
                  label="Open Shift"
                  operationId={openShiftIntent.pending?.payload.clientRequestId ?? null}
                  pending={openShiftIntent.pending}
                  corruptReason={openShiftIntent.corruptReason}
                  authorityMismatch={openShiftIntent.authorityMismatch}
                  inFlight={openShiftIntent.inFlight}
                  onRetry={() =>
                    void openShiftIntent.retryPending(openCashShift).then((r) => {
                      if (r.ok) {
                        setShiftMessage("Shift open retry confirmed.");
                        void loadAll();
                      } else {
                        setShiftMessage(r.message);
                      }
                    })
                  }
                  onClearRejected={() => openShiftIntent.clearPending(false)}
                  onClearCorrupt={() => openShiftIntent.clearPending(true)}
                />
                <DurableIntentBanner
                  label="Cash Movement"
                  operationId={moveIntent.pending?.payload.clientRequestId ?? null}
                  pending={moveIntent.pending}
                  corruptReason={moveIntent.corruptReason}
                  authorityMismatch={moveIntent.authorityMismatch}
                  inFlight={moveIntent.inFlight}
                  onRetry={() =>
                    void moveIntent.retryPending(recordCashDrawerMovement).then((r) => {
                      if (r.ok) {
                        setCurrentDrawer(r.value);
                        setMoveMessage("Cash movement retry confirmed.");
                      } else {
                        setMoveMessage(r.message);
                      }
                    })
                  }
                  onClearRejected={() => moveIntent.clearPending(false)}
                  onClearCorrupt={() => moveIntent.clearPending(true)}
                />
                <DurableIntentBanner
                  label="Blind Shift Close"
                  operationId={closeShiftIntent.pending?.payload.clientRequestId ?? null}
                  pending={closeShiftIntent.pending}
                  corruptReason={closeShiftIntent.corruptReason}
                  authorityMismatch={closeShiftIntent.authorityMismatch}
                  inFlight={closeShiftIntent.inFlight}
                  onRetry={() =>
                    void closeShiftIntent.retryPending(closeCashShiftBlind).then((r) => {
                      if (r.ok) {
                        setShiftMessage("Blind shift close retry confirmed.");
                        void loadAll();
                      } else {
                        setShiftMessage(r.message);
                      }
                    })
                  }
                  onClearRejected={() => closeShiftIntent.clearPending(false)}
                  onClearCorrupt={() => closeShiftIntent.clearPending(true)}
                />

                {!currentDrawer ? (
                  <div className="r3-card">
                    <div className="r3-card-header">
                      <div>
                        <div className="r3-card-title">Open Cashier Shift</div>
                        <div className="r3-card-subtitle">Declare opening cash float to begin taking Cash sales and deposits.</div>
                      </div>
                    </div>
                    <form onSubmit={(e) => void handleOpenShift(e)} style={{ display: "grid", gap: 12, maxWidth: 460 }}>
                      <label>
                        <div className="r3-text-xs r3-text-muted">Opening Cash Float (PHP)</div>
                        <input
                          className="r3-input"
                          aria-label="Opening Cash Float"
                          value={openingFloatPhp}
                          onChange={(e) => setOpeningFloatPhp(e.target.value)}
                          disabled={openShiftIntent.inFlight}
                        />
                      </label>
                      <div>
                        <button type="submit" className="r3-btn r3-btn--primary" disabled={openShiftIntent.inFlight}>
                          {openShiftIntent.inFlight ? "Opening…" : "Open Shift"}
                        </button>
                      </div>
                    </form>
                    {shiftMessage && <p role="status" style={{ marginTop: 12 }} className="r3-text-sm">{shiftMessage}</p>}
                  </div>
                ) : (
                  <>
                    <div className="r3-card">
                      <div className="r3-card-header">
                        <div>
                          <div className="r3-card-title">Open Shift Cash Movements</div>
                          <div className="r3-card-subtitle">
                            Active shift <code>{currentDrawer.sessionId.slice(0, 8)}…</code> · Opening float:{" "}
                            <strong>{formatPhp(currentDrawer.openingCashCents)}</strong> (Expected cash stays hidden until blind close.)
                          </div>
                        </div>
                      </div>
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

                    <div className="r3-card">
                      <div className="r3-card-header">
                        <div>
                          <div className="r3-card-title">C6 Operational Expenses &amp; Cash Reversals</div>
                          <div className="r3-card-subtitle">
                            Record OperationalExpense, CashIn/CashOut, or InventoryPurchase via /api/cafe/cash/movements and reverse eligible manual movements (expected drawer total stays hidden).
                          </div>
                        </div>
                      </div>
                      <DurableIntentBanner
                        label="C6 Cash/Expense"
                        operationId={c6PostIntent.pending?.payload.operationId ?? null}
                        pending={c6PostIntent.pending}
                        corruptReason={c6PostIntent.corruptReason}
                        authorityMismatch={c6PostIntent.authorityMismatch}
                        inFlight={c6PostIntent.inFlight}
                        onRetry={() =>
                          void c6PostIntent.retryPending(postCafeCashMovement).then((r) => {
                            if (r.ok) { setC6Message("C6 cash/expense retry confirmed."); void loadAll(); }
                            else setC6Message(r.message);
                          })
                        }
                        onClearRejected={() => c6PostIntent.clearPending(false)}
                        onClearCorrupt={() => c6PostIntent.clearPending(true)}
                      />
                      <DurableIntentBanner
                        label="Cash Reversal"
                        operationId={c6ReverseIntent.pending?.payload.command.operationId ?? null}
                        pending={c6ReverseIntent.pending}
                        corruptReason={c6ReverseIntent.corruptReason}
                        authorityMismatch={c6ReverseIntent.authorityMismatch}
                        inFlight={c6ReverseIntent.inFlight}
                        onRetry={() =>
                          void c6ReverseIntent
                            .retryPending((p) => reverseCafeCashMovement(p.originalMovementId, p.command))
                            .then((r) => {
                              if (r.ok) { setC6Message("Cash reversal retry confirmed."); void loadAll(); }
                              else setC6Message(r.message);
                            })
                        }
                        onClearRejected={() => c6ReverseIntent.clearPending(false)}
                        onClearCorrupt={() => c6ReverseIntent.clearPending(true)}
                      />
                      {(canExpense || hasPerm(session, "Cash.In") || hasPerm(session, "Cash.Out")) && (
                        <form onSubmit={(e) => void handleC6PostSubmit(e)} style={{ display: "grid", gap: 12, maxWidth: 560, marginBottom: 16 }}>
                          <label>
                            <div className="r3-text-xs r3-text-muted">C6 Movement Type</div>
                            <select className="r3-input" aria-label="C6 Movement Type" value={c6Type} onChange={(e) => setC6Type(e.target.value as CafeCashMovementType)}>
                              {canExpense && <option value="OperationalExpense">OperationalExpense</option>}
                              {hasPerm(session, "Cash.In") && <option value="CashIn">CashIn</option>}
                              {hasPerm(session, "Cash.Out") && <option value="CashOut">CashOut</option>}
                              {hasPerm(session, "Cash.Out") && <option value="InventoryPurchase">InventoryPurchase (requires purchase ID)</option>}
                            </select>
                          </label>
                          <label>
                            <div className="r3-text-xs r3-text-muted">Expense / Movement Amount (PHP)</div>
                            <input className="r3-input" aria-label="C6 Amount" value={c6AmountPhp} onChange={(e) => setC6AmountPhp(e.target.value)} placeholder="120.00" />
                          </label>
                          <label>
                            <div className="r3-text-xs r3-text-muted">Expense / Movement Reason</div>
                            <input className="r3-input" aria-label="C6 Reason" value={c6Reason} onChange={(e) => setC6Reason(e.target.value)} placeholder="Ice delivery / store supplies" />
                          </label>
                          {c6Type === "InventoryPurchase" && (
                            <label>
                              <div className="r3-text-xs r3-text-muted">Purchase Receipt ID (GUID)</div>
                              <input className="r3-input" aria-label="Purchase Receipt ID" value={c6ReferenceId} onChange={(e) => setC6ReferenceId(e.target.value)} placeholder="Accepted C3 purchase receipt GUID" />
                            </label>
                          )}
                          <div>
                            <button type="submit" className="r3-btn r3-btn--primary" disabled={c6PostIntent.inFlight}>
                              Post C6 Cash / Expense
                            </button>
                          </div>
                        </form>
                      )}
                      {c6Message && <p role="status" style={{ marginBottom: 12 }} className="r3-text-sm">{c6Message}</p>}
                      <div className="r3-table-wrap" tabIndex={0} role="region" aria-label="Session C6 Cash Movements">
                        <table className="r3-table">
                          <caption className="r3-sr-only">Session C6 Cash Movements</caption>
                          <thead>
                            <tr>
                              <th scope="col">Type</th>
                              <th scope="col">Delta</th>
                              <th scope="col">Reason</th>
                              <th scope="col">Action</th>
                            </tr>
                          </thead>
                          <tbody>
                            {c6Movements.length === 0 ? (
                              <tr><td colSpan={4} className="r3-text-muted">No session cash movements yet.</td></tr>
                            ) : (
                              c6Movements.map((m) => {
                                const reversible =
                                  canCashReverse &&
                                  !m.isReversed &&
                                  !m.reversalOfMovementId &&
                                  (m.movementType === "CashIn" ||
                                    m.movementType === "CashOut" ||
                                    m.movementType === "OperationalExpense" ||
                                    m.movementType === "InventoryPurchase");
                                return (
                                  <tr key={m.movementId}>
                                    <th scope="row">{m.movementType}</th>
                                    <td className="r3-tabular">{formatPhp(m.deltaCents)}</td>
                                    <td>{m.reason ?? "—"}</td>
                                    <td>
                                      {reversible ? (
                                        <button
                                          type="button"
                                          className="r3-btn r3-btn--outline r3-btn--sm"
                                          disabled={c6ReverseIntent.inFlight}
                                          onClick={() => void handleC6Reverse(m)}
                                        >
                                          Reverse
                                        </button>
                                      ) : (
                                        <span className="r3-text-xs r3-text-muted">
                                          {m.reversalOfMovementId ? "Reversal" : m.isReversed ? "Reversed" : "Source-owned"}
                                        </span>
                                      )}
                                    </td>
                                  </tr>
                                );
                              })
                            )}
                          </tbody>
                        </table>
                      </div>
                    </div>

                    <div className="r3-card">
                      <div className="r3-card-header">
                        <div>
                          <div className="r3-card-title">Blind Shift Close</div>
                          <div className="r3-card-subtitle">
                            Enter your physical drawer count without viewing expected cash. Any nonzero variance enters PendingReview for independent review.
                          </div>
                        </div>
                      </div>
                      <form onSubmit={(e) => void handleBlindCloseShift(e)} style={{ display: "grid", gap: 12, maxWidth: 560 }}>
                        <label>
                          <div className="r3-text-xs r3-text-muted">Physical Counted Cash (PHP)</div>
                          <input
                            className="r3-input"
                            aria-label="Physical Counted Cash"
                            value={countedCashPhp}
                            onChange={(e) => setCountedCashPhp(e.target.value)}
                            placeholder="1020.00"
                            disabled={closeShiftIntent.inFlight}
                          />
                        </label>
                        <label>
                          <div className="r3-text-xs r3-text-muted">Close Note (optional)</div>
                          <input
                            className="r3-input"
                            aria-label="Shift Close Note"
                            value={closeNote}
                            onChange={(e) => setCloseNote(e.target.value)}
                            placeholder="End of day blind count"
                            disabled={closeShiftIntent.inFlight}
                          />
                        </label>
                        <label style={{ display: "flex", alignItems: "center", gap: 8 }}>
                          <input
                            type="checkbox"
                            checked={physicalConfirmed}
                            onChange={(e) => setPhysicalConfirmed(e.target.checked)}
                            disabled={closeShiftIntent.inFlight}
                          />
                          <span className="r3-text-sm">I physically counted the cash drawer and confirm this amount.</span>
                        </label>
                        <div>
                          <button type="submit" className="r3-btn r3-btn--primary" disabled={closeShiftIntent.inFlight}>
                            {closeShiftIntent.inFlight ? "Closing…" : "Close Shift Blind"}
                          </button>
                        </div>
                      </form>
                      {shiftMessage && <p role="status" style={{ marginTop: 12 }} className="r3-text-sm">{shiftMessage}</p>}
                    </div>
                  </>
                )}
              </div>
            )}

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
                  <DurableIntentBanner
                    label="Variance Review"
                    operationId={reviewIntent.pending?.payload.command.clientRequestId ?? null}
                    pending={reviewIntent.pending}
                    corruptReason={reviewIntent.corruptReason}
                    authorityMismatch={reviewIntent.authorityMismatch}
                    inFlight={reviewIntent.inFlight}
                    onRetry={() =>
                      void reviewIntent
                        .retryPending((p) => reviewCashDrawerVariance(p.sessionId, p.command))
                        .then((r) => {
                          if (r.ok) {
                            setReviewSessionId("");
                            setReviewReason("");
                            setReviewMessage(`Variance retry confirmed (${r.value.reviewStatus}).`);
                            void loadAll();
                          } else {
                            setReviewMessage(r.message);
                          }
                        })
                    }
                    onClearRejected={() => reviewIntent.clearPending(false)}
                    onClearCorrupt={() => reviewIntent.clearPending(true)}
                  />
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
          </>
        )}
      </div>
    </div>
  );
}

export function R3LiveCashShiftsPage(props: Props = {}) {
  return (
    <R3ToastProvider>
      <R3LiveCashShiftsContent {...props} />
    </R3ToastProvider>
  );
}
