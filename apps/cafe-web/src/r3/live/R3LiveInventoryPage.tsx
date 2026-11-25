import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { hostRequest, parsePhpToCents } from "../../api/hostClient";
import {
  isLicenseActiveForBootstrap,
  type HostLicenseBootstrapStatus,
  type HostSession,
} from "../../cashier/types";
import "../styles/r3-design-system.css";
import { R3ToastProvider, useR3Toast } from "../components/R3Primitives";
import { DurableIntentBanner } from "./DurableIntentBanner";
import { fetchHostIdentityKey, useDurableIntent, type DurableIntentAuthority } from "./durableIntent";
import {
  commitAdjustment,
  commitPurchase,
  commitWaste,
  finalizeCount,
  readCount,
  readLiveIngredients,
  readLiveLowStock,
  readLiveSuppliers,
  saveCountDraft,
  type AdjustmentCommand,
  type CountDraft,
  type CountFinalize,
  type CountSession,
  type InventoryAdjustmentEvidence,
  type InventoryCommitResult,
  type LiveIngredient,
  type LiveLowStockItem,
  type LiveSupplier,
  type PurchaseReceiveCommand,
  type WasteCommitCommand,
  type WasteReason,
} from "./inventoryContracts";

type TabId = "stock" | "adjust" | "count" | "receive" | "waste";

type DraftIntentPayload = {
  operationId: string;
  draft: CountDraft;
};

type FinalizeIntentPayload = {
  countSessionId: string;
  command: CountFinalize;
};

const ACTIVE_COUNT_STORAGE_KEY = "salryn.cafe.r3.active-count-session.v1";

function formatQty(x1000: number, unit?: string): string {
  const num = (x1000 / 1000).toLocaleString("en-PH", {
    minimumFractionDigits: 0,
    maximumFractionDigits: 3,
  });
  return unit ? `${num} ${unit}` : num;
}

function parseSignedQtyX1000(raw: string): number {
  const trimmed = raw.trim();
  if (!/^[+-]?\d+(\.\d{1,3})?$/.test(trimmed)) {
    throw new Error("Enter a signed quantity with at most 3 decimal places (e.g. +2.5 or -1).");
  }
  const value = Math.round(Number(trimmed) * 1000);
  if (!Number.isSafeInteger(value) || value === 0) {
    throw new Error("Adjustment quantity must be a nonzero number.");
  }
  return value;
}

function parseNonNegativeQtyX1000(raw: string): number {
  const trimmed = raw.trim();
  if (!/^\d+(\.\d{1,3})?$/.test(trimmed)) {
    throw new Error("Enter a nonnegative quantity with at most 3 decimal places.");
  }
  const value = Math.round(Number(trimmed) * 1000);
  if (!Number.isSafeInteger(value) || value < 0) {
    throw new Error("Quantity must be nonnegative.");
  }
  return value;
}

function parsePositiveQtyX1000(raw: string): number {
  const val = parseNonNegativeQtyX1000(raw);
  if (val <= 0) throw new Error("Quantity must be greater than zero.");
  return val;
}

function hasPerm(session: HostSession | null, perm: string): boolean {
  const perms = session?.user?.permissions ?? [];
  return perms.includes(perm);
}

async function verifyHostInventoryAccess(): Promise<HostSession> {
  const license = await hostRequest<HostLicenseBootstrapStatus>("/api/license/status", undefined, "GET");
  if (!isLicenseActiveForBootstrap(license)) {
    throw new Error("Active Host license required. Return to the workstation to activate.");
  }
  const session = await hostRequest<HostSession>("/api/session", undefined, "GET");
  if (!session?.isAuthenticated || !session.user) {
    throw new Error("Sign in through the workstation before opening live inventory.");
  }
  const canView =
    hasPerm(session, "Inventory.View") || hasPerm(session, "Ingredients.View");
  if (!canView) {
    throw new Error("Insufficient permissions: Inventory.View or Ingredients.View is required.");
  }
  return session;
}

function R3LiveInventoryContent({ embedded = false }: { embedded?: boolean }) {
  const { pushToast } = useR3Toast();
  const [session, setSession] = useState<HostSession | null>(null);
  const [hostIdentityKey, setHostIdentityKey] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [accessError, setAccessError] = useState<string | null>(null);
  const [tab, setTab] = useState<TabId>("stock");

  const [ingredients, setIngredients] = useState<LiveIngredient[]>([]);
  const [lowStock, setLowStock] = useState<LiveLowStockItem[]>([]);
  const [suppliers, setSuppliers] = useState<LiveSupplier[]>([]);
  const [lastRefreshedAt, setLastRefreshedAt] = useState<string | null>(null);

  const authority: DurableIntentAuthority | null = useMemo(
    () =>
      hostIdentityKey && session?.user?.userId
        ? { hostIdentityKey, actorUserId: session.user.userId }
        : null,
    [hostIdentityKey, session?.user?.userId],
  );

  // Manual Adjustment state (INT-01)
  const [adjIngredientId, setAdjIngredientId] = useState("");
  const [adjDeltaInput, setAdjDeltaInput] = useState("");
  const [adjReason, setAdjReason] = useState("");
  const [adjMessage, setAdjMessage] = useState<string | null>(null);
  const [lastAdjEvidence, setLastAdjEvidence] = useState<InventoryAdjustmentEvidence | null>(null);
  const adjIntent = useDurableIntent<AdjustmentCommand, InventoryCommitResult<InventoryAdjustmentEvidence>>(
    "inventory-adjust",
    authority,
  );

  // Physical Count state (INT-02)
  const [countIngredientId, setCountIngredientId] = useState("");
  const [countedInput, setCountedInput] = useState("");
  const [countNotes, setCountNotes] = useState("");
  const [activeCount, setActiveCount] = useState<CountSession | null>(null);
  const [lookupCountId, setLookupCountId] = useState("");
  const [countMessage, setCountMessage] = useState<string | null>(null);
  const draftIntent = useDurableIntent<DraftIntentPayload, CountSession>("inventory-count-draft", authority);
  const finalizeIntent = useDurableIntent<FinalizeIntentPayload, InventoryCommitResult<CountSession>>(
    "inventory-count-finalize",
    authority,
  );

  // Receive Stock state
  const [recSupplierId, setRecSupplierId] = useState("");
  const [recIngredientId, setRecIngredientId] = useState("");
  const [recQtyInput, setRecQtyInput] = useState("");
  const [recCostInput, setRecCostInput] = useState("");
  const [recRef, setRecRef] = useState("");
  const [recMessage, setRecMessage] = useState<string | null>(null);
  const purchaseIntent = useDurableIntent<PurchaseReceiveCommand, InventoryCommitResult<unknown>>(
    "inventory-purchase",
    authority,
  );

  // Record Waste state
  const [wasteIngredientId, setWasteIngredientId] = useState("");
  const [wasteQtyInput, setWasteQtyInput] = useState("");
  const [wasteReason, setWasteReason] = useState<WasteReason>("Waste");
  const [wasteNotes, setWasteNotes] = useState("");
  const [wasteMessage, setWasteMessage] = useState<string | null>(null);
  const wasteIntent = useDurableIntent<WasteCommitCommand, InventoryCommitResult<unknown>>(
    "inventory-waste",
    authority,
  );

  const tabRefs = useRef<Partial<Record<TabId, HTMLButtonElement | null>>>({});

  const loadAll = useCallback(async () => {
    setLoading(true);
    setAccessError(null);
    try {
      const verified = await verifyHostInventoryAccess();
      const hostKey = await fetchHostIdentityKey();
      setSession(verified);
      setHostIdentityKey(hostKey);

      const [ingList, lowList, supList] = await Promise.all([
        hasPerm(verified, "Ingredients.View") && hasPerm(verified, "Recipes.View")
          ? readLiveIngredients()
          : Promise.resolve([]),
        hasPerm(verified, "Inventory.View") ? readLiveLowStock() : Promise.resolve([]),
        hasPerm(verified, "Suppliers.View") ? readLiveSuppliers() : Promise.resolve([]),
      ]);

      setIngredients(ingList);
      setLowStock(lowList);
      setSuppliers(supList);
      setLastRefreshedAt(new Date().toISOString());

      if (ingList.length > 0) {
        setAdjIngredientId((prev) => prev || ingList[0].id);
        setCountIngredientId((prev) => prev || ingList[0].id);
        setRecIngredientId((prev) => prev || ingList[0].id);
        setWasteIngredientId((prev) => prev || ingList[0].id);
      }
      if (supList.length > 0) {
        setRecSupplierId((prev) => prev || supList[0].id);
      }

      // Restore active count session from Host if one was saved across reload.
      try {
        const storedCountId = window.localStorage.getItem(ACTIVE_COUNT_STORAGE_KEY);
        if (storedCountId && hasPerm(verified, "Inventory.View")) {
          const loaded = await readCount(storedCountId);
          setActiveCount(loaded);
        }
      } catch {
        // Ignore if session no longer exists or cannot be read.
      }
    } catch (err) {
      setAccessError(err instanceof Error ? err.message : "Live inventory unavailable.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadAll();
  }, [loadAll]);

  const canAdjust = hasPerm(session, "Inventory.Adjust");
  const canCount = hasPerm(session, "Inventory.Count");
  const canReceive = hasPerm(session, "Purchasing.Receive");
  const canWaste = hasPerm(session, "Inventory.Waste");

  const tabs = useMemo(
    () =>
      [
        { id: "stock" as TabId, label: `Stock Overview (${ingredients.length})`, enabled: true },
        { id: "adjust" as TabId, label: "Manual Adjustment", enabled: canAdjust },
        { id: "count" as TabId, label: "Physical Count", enabled: canCount },
        { id: "receive" as TabId, label: "Receive Stock", enabled: canReceive },
        { id: "waste" as TabId, label: "Record Waste", enabled: canWaste },
      ].filter((t) => t.enabled),
    [ingredients.length, canAdjust, canCount, canReceive, canWaste],
  );

  const handleTabKeyDown = (e: React.KeyboardEvent<HTMLButtonElement>, idx: number) => {
    let nextIdx: number | null = null;
    if (e.key === "ArrowRight") nextIdx = (idx + 1) % tabs.length;
    else if (e.key === "ArrowLeft") nextIdx = (idx - 1 + tabs.length) % tabs.length;
    else if (e.key === "Home") nextIdx = 0;
    else if (e.key === "End") nextIdx = tabs.length - 1;
    if (nextIdx !== null) {
      e.preventDefault();
      const target = tabs[nextIdx];
      setTab(target.id);
      tabRefs.current[target.id]?.focus();
    }
  };

  // --- INT-01: Manual Adjustment Submit & Retry ---
  const handleAdjustmentSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setAdjMessage(null);
    let deltaX1000: number;
    try {
      deltaX1000 = parseSignedQtyX1000(adjDeltaInput);
    } catch (err) {
      setAdjMessage(err instanceof Error ? err.message : "Invalid quantity.");
      return;
    }
    const reason = adjReason.trim();
    if (!adjIngredientId || !reason) {
      setAdjMessage("Select an ingredient and enter a non-empty reason.");
      return;
    }

    const res = await adjIntent.execute(
      { stockTargetType: "Ingredient", stockTargetId: adjIngredientId, quantityDeltaX1000: deltaX1000, reason },
      () => ({
        operationId: crypto.randomUUID(),
        adjustmentEventId: crypto.randomUUID(),
        stockTargetType: "Ingredient",
        stockTargetId: adjIngredientId,
        quantityDeltaX1000: deltaX1000,
        reason,
        occurredAtUtc: new Date().toISOString(),
      }),
      commitAdjustment,
    );

    if (res.ok) {
      setLastAdjEvidence(res.value.evidence);
      setAdjDeltaInput("");
      setAdjReason("");
      await loadAll();
      const msg = `Adjustment committed (${formatQty(res.value.evidence.quantityDeltaX1000, res.value.evidence.stockUnitSnapshot)}).`;
      setAdjMessage(msg);
      pushToast(msg);
    } else {
      setAdjMessage(`${res.code ? `[${res.code}] ` : ""}${res.message}`);
    }
  };

  const handleAdjustmentRetry = async () => {
    setAdjMessage(null);
    const res = await adjIntent.retryPending(commitAdjustment);
    if (res.ok) {
      setLastAdjEvidence(res.value.evidence);
      setAdjDeltaInput("");
      setAdjReason("");
      await loadAll();
      const msg = `Adjustment retry confirmed (${formatQty(res.value.evidence.quantityDeltaX1000, res.value.evidence.stockUnitSnapshot)}).`;
      setAdjMessage(msg);
      pushToast(msg);
    } else {
      setAdjMessage(`${res.code ? `[${res.code}] ` : ""}${res.message}`);
    }
  };

  // --- INT-02: Physical Count Draft & Finalize ---
  const selectedCountIngredient = ingredients.find((i) => i.id === countIngredientId) ?? null;

  const handleCountDraftSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setCountMessage(null);
    if (!selectedCountIngredient) {
      setCountMessage("Select an ingredient to count.");
      return;
    }
    let countedX1000: number;
    try {
      countedX1000 = parseNonNegativeQtyX1000(countedInput);
    } catch (err) {
      setCountMessage(err instanceof Error ? err.message : "Invalid counted quantity.");
      return;
    }

    const expectedBeforeX1000 = selectedCountIngredient.stockOnHandX1000;
    const notes = countNotes.trim() || null;

    const res = await draftIntent.execute(
      {
        stockTargetType: "Ingredient",
        stockTargetId: selectedCountIngredient.id,
        expectedBeforeX1000,
        countedX1000,
        notes,
      },
      () => ({
        operationId: crypto.randomUUID(),
        draft: {
          countSessionId: crypto.randomUUID(),
          startedAtUtc: new Date().toISOString(),
          notes,
          lines: [
            {
              lineId: crypto.randomUUID(),
              stockTargetType: "Ingredient",
              stockTargetId: selectedCountIngredient.id,
              expectedBeforeX1000,
              countedX1000,
            },
          ],
        },
      }),
      (payload) => saveCountDraft(payload.operationId, payload.draft),
    );

    if (res.ok) {
      setActiveCount(res.value);
      try {
        window.localStorage.setItem(ACTIVE_COUNT_STORAGE_KEY, res.value.countSessionId);
      } catch {
        // Ignore storage error.
      }
      setCountedInput("");
      const msg = `Physical count draft saved (${res.value.countSessionId.slice(0, 8)}…). No stock changed yet — finalize to apply.`;
      setCountMessage(msg);
      pushToast(msg);
    } else {
      setCountMessage(`${res.code ? `[${res.code}] ` : ""}${res.message}`);
    }
  };

  const handleCountDraftRetry = async () => {
    setCountMessage(null);
    const res = await draftIntent.retryPending((payload) => saveCountDraft(payload.operationId, payload.draft));
    if (res.ok) {
      setActiveCount(res.value);
      try {
        window.localStorage.setItem(ACTIVE_COUNT_STORAGE_KEY, res.value.countSessionId);
      } catch {
        // Ignore storage error.
      }
      const msg = `Physical count draft confirmed (${res.value.countSessionId.slice(0, 8)}…).`;
      setCountMessage(msg);
      pushToast(msg);
    } else {
      setCountMessage(`${res.code ? `[${res.code}] ` : ""}${res.message}`);
    }
  };

  const handleFinalizeCount = async () => {
    if (!activeCount || activeCount.state !== "Draft") return;
    setCountMessage(null);
    const sessionId = activeCount.countSessionId;

    const res = await finalizeIntent.execute(
      { countSessionId: sessionId },
      () => ({
        countSessionId: sessionId,
        command: {
          operationId: crypto.randomUUID(),
          finalizedAtUtc: new Date().toISOString(),
        },
      }),
      (payload) => finalizeCount(payload.countSessionId, payload.command),
    );

    if (res.ok) {
      setActiveCount(res.value.evidence);
      await loadAll();
      const msg = `Physical count finalized (${res.value.evidence.countSessionId.slice(0, 8)}…). Ledger balances updated.`;
      setCountMessage(msg);
      pushToast(msg);
    } else {
      setCountMessage(`${res.code ? `[${res.code}] ` : ""}${res.message}`);
    }
  };

  const handleFinalizeRetry = async () => {
    setCountMessage(null);
    const res = await finalizeIntent.retryPending((payload) =>
      finalizeCount(payload.countSessionId, payload.command),
    );
    if (res.ok) {
      setActiveCount(res.value.evidence);
      await loadAll();
      const msg = `Physical count finalization confirmed (${res.value.evidence.countSessionId.slice(0, 8)}…).`;
      setCountMessage(msg);
      pushToast(msg);
    } else {
      setCountMessage(`${res.code ? `[${res.code}] ` : ""}${res.message}`);
    }
  };

  const handleLoadCountSession = async (e: React.FormEvent) => {
    e.preventDefault();
    setCountMessage(null);
    const trimmed = lookupCountId.trim();
    if (!trimmed) return;
    try {
      const loaded = await readCount(trimmed);
      setActiveCount(loaded);
      try {
        window.localStorage.setItem(ACTIVE_COUNT_STORAGE_KEY, loaded.countSessionId);
      } catch {
        // Ignore.
      }
      setCountMessage(`Loaded count session ${loaded.countSessionId.slice(0, 8)}… (${loaded.state}).`);
    } catch (err) {
      setCountMessage(err instanceof Error ? err.message : "Failed to load count session.");
    }
  };

  // --- Receive Stock ---
  const handleReceiveSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setRecMessage(null);
    let qtyX1000: number;
    let costCents: number;
    try {
      qtyX1000 = parsePositiveQtyX1000(recQtyInput);
      costCents = parsePhpToCents(recCostInput);
    } catch (err) {
      setRecMessage(err instanceof Error ? err.message : "Invalid receive input.");
      return;
    }
    if (!recSupplierId || !recIngredientId) {
      setRecMessage("Select a supplier and an ingredient.");
      return;
    }
    const extRef = recRef.trim() || null;

    const res = await purchaseIntent.execute(
      {
        supplierId: recSupplierId,
        stockTargetId: recIngredientId,
        quantityReceivedX1000: qtyX1000,
        lineAcquisitionCostCents: costCents,
        externalReference: extRef,
      },
      () => ({
        operationId: crypto.randomUUID(),
        purchaseReceiptId: crypto.randomUUID(),
        supplierId: recSupplierId,
        receivedAtUtc: new Date().toISOString(),
        externalReference: extRef,
        notes: null,
        lines: [
          {
            lineId: crypto.randomUUID(),
            stockTargetType: "Ingredient",
            stockTargetId: recIngredientId,
            quantityReceivedX1000: qtyX1000,
            lineAcquisitionCostCents: costCents,
          },
        ],
      }),
      commitPurchase,
    );

    if (res.ok) {
      setRecQtyInput("");
      setRecCostInput("");
      setRecRef("");
      await loadAll();
      const msg = "Purchase receipt committed to Host inventory ledger.";
      setRecMessage(msg);
      pushToast(msg);
    } else {
      setRecMessage(`${res.code ? `[${res.code}] ` : ""}${res.message}`);
    }
  };

  const handleReceiveRetry = async () => {
    setRecMessage(null);
    const res = await purchaseIntent.retryPending(commitPurchase);
    if (res.ok) {
      setRecQtyInput("");
      setRecCostInput("");
      setRecRef("");
      await loadAll();
      const msg = "Purchase receipt retry confirmed on Host inventory ledger.";
      setRecMessage(msg);
      pushToast(msg);
    } else {
      setRecMessage(`${res.code ? `[${res.code}] ` : ""}${res.message}`);
    }
  };

  // --- Record Waste ---
  const handleWasteSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setWasteMessage(null);
    let qtyX1000: number;
    try {
      qtyX1000 = parsePositiveQtyX1000(wasteQtyInput);
    } catch (err) {
      setWasteMessage(err instanceof Error ? err.message : "Invalid waste quantity.");
      return;
    }
    if (!wasteIngredientId) {
      setWasteMessage("Select an ingredient.");
      return;
    }
    const notes = wasteNotes.trim() || null;

    const res = await wasteIntent.execute(
      { stockTargetId: wasteIngredientId, quantityWastedX1000: qtyX1000, reason: wasteReason, notes },
      () => ({
        operationId: crypto.randomUUID(),
        wasteEventId: crypto.randomUUID(),
        occurredAtUtc: new Date().toISOString(),
        reason: wasteReason,
        notes,
        lines: [
          {
            lineId: crypto.randomUUID(),
            stockTargetType: "Ingredient",
            stockTargetId: wasteIngredientId,
            quantityWastedX1000: qtyX1000,
          },
        ],
      }),
      commitWaste,
    );

    if (res.ok) {
      setWasteQtyInput("");
      setWasteNotes("");
      await loadAll();
      const msg = "Waste event committed to Host inventory ledger.";
      setWasteMessage(msg);
      pushToast(msg);
    } else {
      setWasteMessage(`${res.code ? `[${res.code}] ` : ""}${res.message}`);
    }
  };

  const handleWasteRetry = async () => {
    setWasteMessage(null);
    const res = await wasteIntent.retryPending(commitWaste);
    if (res.ok) {
      setWasteQtyInput("");
      setWasteNotes("");
      await loadAll();
      const msg = "Waste event retry confirmed on Host inventory ledger.";
      setWasteMessage(msg);
      pushToast(msg);
    } else {
      setWasteMessage(`${res.code ? `[${res.code}] ` : ""}${res.message}`);
    }
  };

  const inner = (
      <div className={embedded ? undefined : "r3-content"} style={embedded ? undefined : { maxWidth: 1200, margin: "0 auto" }}>
        <div className="r3-page-header">
          <div>
            {!embedded && (
              <p className="r3-text-xs r3-text-muted" style={{ marginBottom: 4 }}>
                <a href="/cafe/">Return to existing workstation</a>
                {" · "}
                <a href="/cafe/r3/reports">Live Reports</a>
              </p>
            )}
            <h1 className="r3-page-title">Inventory &amp; Stock Control</h1>
            <p className="r3-page-subtitle">
              Live Host inventory ledger with durable pending-intent idempotency (zero mock fallback).
            </p>
          </div>
          <div className="r3-page-actions">
            <button
              type="button"
              className="r3-btn r3-btn--outline r3-btn--sm"
              onClick={() => void loadAll()}
              disabled={loading}
            >
              {loading ? "Refreshing…" : "Refresh Live Stock"}
            </button>
          </div>
        </div>

        <div className="r3-alert r3-alert--info" role="status">
          <div className="r3-alert-body">
            All reads and writes execute directly against the live Host SQLite ledger.
            Prepared requests are bound to operator and Host identity and retain identical operation IDs across reloads and retries.
            {lastRefreshedAt ? ` Last synced: ${new Date(lastRefreshedAt).toLocaleTimeString()}.` : ""}
          </div>
        </div>

        {accessError && (
          <div className="r3-alert r3-alert--danger" role="alert">
            <div className="r3-alert-body">
              <div className="r3-alert-title">Live inventory unavailable</div>
              <div>{accessError}</div>
            </div>
            <button type="button" className="r3-btn r3-btn--outline r3-btn--sm" onClick={() => void loadAll()}>
              Retry
            </button>
          </div>
        )}

        {!accessError && session && (
          <>
            <div className="r3-metrics-grid r3-metrics-grid--2x2-mobile">
              <div className="r3-metric-card r3-metric-card--accent">
                <div className="r3-metric-label">Tracked Ingredients</div>
                <div className="r3-metric-value r3-tabular">{ingredients.length}</div>
                <div className="r3-metric-sub">Live Host catalog</div>
              </div>
              <div className="r3-metric-card r3-metric-card--warn">
                <div className="r3-metric-label">Low Stock Alerts</div>
                <div className="r3-metric-value r3-tabular">{lowStock.length}</div>
                <div className="r3-metric-sub">At or below reorder level</div>
              </div>
              <div className="r3-metric-card r3-metric-card--info">
                <div className="r3-metric-label">Active Suppliers</div>
                <div className="r3-metric-value r3-tabular">{suppliers.filter((s) => s.isActive).length}</div>
                <div className="r3-metric-sub">Host supplier directory</div>
              </div>
              <div className="r3-metric-card r3-metric-card--success">
                <div className="r3-metric-label">Pending Intents</div>
                <div className="r3-metric-value r3-tabular">
                  {[adjIntent.pending, draftIntent.pending, finalizeIntent.pending, purchaseIntent.pending, wasteIntent.pending].filter(
                    Boolean,
                  ).length}
                </div>
                <div className="r3-metric-sub">Durable localStorage fences</div>
              </div>
            </div>

            <div className="r3-tabs" role="tablist" aria-label="Inventory sections">
              {tabs.map((t, idx) => (
                <button
                  key={t.id}
                  id={`r3-inv-tab-${t.id}`}
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

            {tab === "stock" && (
              <div role="tabpanel" aria-labelledby="r3-inv-tab-stock" className="r3-card">
                <div className="r3-card-header">
                  <div>
                    <div className="r3-card-title">Live Ingredient Balances</div>
                    <div className="r3-card-subtitle">Authoritative stock on hand directly from Host SQLite</div>
                  </div>
                </div>
                <div className="r3-table-wrap" tabIndex={0} role="region" aria-label="Live Ingredient Balances">
                  <table className="r3-table">
                    <caption className="r3-sr-only">Live Ingredient Balances</caption>
                    <thead>
                      <tr>
                        <th scope="col">Ingredient</th>
                        <th scope="col">Stock On Hand</th>
                        <th scope="col">Reorder Level</th>
                        <th scope="col">Status</th>
                      </tr>
                    </thead>
                    <tbody>
                      {ingredients.length === 0 ? (
                        <tr>
                          <td colSpan={4} className="r3-text-muted">
                            No ingredients found in Host catalog.
                          </td>
                        </tr>
                      ) : (
                        ingredients.map((ing) => {
                          const isLow = ing.stockOnHandX1000 <= ing.reorderLevelX1000;
                          return (
                            <tr key={ing.id}>
                              <th scope="row">{ing.name}</th>
                              <td className="r3-tabular">{formatQty(ing.stockOnHandX1000, ing.stockUnit)}</td>
                              <td className="r3-tabular">{formatQty(ing.reorderLevelX1000, ing.stockUnit)}</td>
                              <td>
                                <span className={`r3-badge ${isLow ? "r3-badge--warn" : "r3-badge--success"}`}>
                                  {isLow ? "Low Stock" : "OK"}
                                </span>
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

            {tab === "adjust" && canAdjust && (
              <div role="tabpanel" aria-labelledby="r3-inv-tab-adjust" className="r3-card">
                <div className="r3-card-header">
                  <div>
                    <div className="r3-card-title">Manual Stock Adjustment (INT-01)</div>
                    <div className="r3-card-subtitle">
                      Commits directly to POST /api/cafe/inventory/adjust with durable operationId fencing.
                    </div>
                  </div>
                </div>

                <DurableIntentBanner
                  label="Adjustment"
                  operationId={adjIntent.pending?.payload.operationId ?? null}
                  pending={adjIntent.pending}
                  corruptReason={adjIntent.corruptReason}
                  authorityMismatch={adjIntent.authorityMismatch}
                  inFlight={adjIntent.inFlight}
                  onRetry={() => void handleAdjustmentRetry()}
                  onClearRejected={() => {
                    adjIntent.clearPending(false);
                    setAdjMessage("Cleared rejected adjustment intent.");
                  }}
                  onClearCorrupt={() => {
                    adjIntent.clearPending(true);
                    setAdjMessage("Cleared corrupt adjustment storage after verification.");
                  }}
                />

                <form onSubmit={(e) => void handleAdjustmentSubmit(e)} style={{ display: "grid", gap: 12, maxWidth: 560 }}>
                  <label>
                    <div className="r3-text-xs r3-text-muted" style={{ marginBottom: 4 }}>
                      Ingredient
                    </div>
                    <select
                      className="r3-input"
                      aria-label="Adjustment Ingredient"
                      value={adjIngredientId}
                      onChange={(e) => setAdjIngredientId(e.target.value)}
                      disabled={adjIntent.inFlight}
                    >
                      {ingredients.map((i) => (
                        <option key={i.id} value={i.id}>
                          {i.name} (On hand: {formatQty(i.stockOnHandX1000, i.stockUnit)})
                        </option>
                      ))}
                    </select>
                  </label>
                  <label>
                    <div className="r3-text-xs r3-text-muted" style={{ marginBottom: 4 }}>
                      Signed Quantity Delta (e.g. +2.5 or -1)
                    </div>
                    <input
                      className="r3-input"
                      aria-label="Signed Quantity Delta"
                      value={adjDeltaInput}
                      onChange={(e) => setAdjDeltaInput(e.target.value)}
                      placeholder="+2 or -1.5"
                      disabled={adjIntent.inFlight}
                    />
                  </label>
                  <label>
                    <div className="r3-text-xs r3-text-muted" style={{ marginBottom: 4 }}>
                      Reason (required)
                    </div>
                    <input
                      className="r3-input"
                      aria-label="Adjustment Reason"
                      value={adjReason}
                      onChange={(e) => setAdjReason(e.target.value)}
                      placeholder="Audit correction or unopened pack found"
                      disabled={adjIntent.inFlight}
                    />
                  </label>
                  <div>
                    <button
                      type="submit"
                      className="r3-btn r3-btn--primary"
                      disabled={adjIntent.inFlight}
                    >
                      {adjIntent.inFlight ? "Committing…" : "Commit Adjustment"}
                    </button>
                  </div>
                </form>

                {adjMessage && (
                  <p role="status" style={{ marginTop: 12 }} className="r3-text-sm">
                    {adjMessage}
                  </p>
                )}

                {lastAdjEvidence && (
                  <div className="r3-alert r3-alert--success" style={{ marginTop: 12 }}>
                    <div className="r3-alert-body">
                      <div className="r3-alert-title">Last Committed Adjustment Evidence</div>
                      <div>
                        Event <code>{lastAdjEvidence.adjustmentEventId}</code> · {lastAdjEvidence.nameSnapshot}:{" "}
                        <strong>{formatQty(lastAdjEvidence.quantityDeltaX1000, lastAdjEvidence.stockUnitSnapshot)}</strong>{" "}
                        ({lastAdjEvidence.reason})
                      </div>
                    </div>
                  </div>
                )}
              </div>
            )}

            {tab === "count" && canCount && (
              <div role="tabpanel" aria-labelledby="r3-inv-tab-count" style={{ display: "grid", gap: 16 }}>
                <div className="r3-card">
                  <div className="r3-card-header">
                    <div>
                      <div className="r3-card-title">Step 1 — Save Physical Count Draft (INT-02)</div>
                      <div className="r3-card-subtitle">
                        Captures current Host stock as expectedBeforeX1000. Saving a draft does not mutate stock until Step 2 finalize.
                      </div>
                    </div>
                  </div>

                  <DurableIntentBanner
                    label="Draft"
                    operationId={draftIntent.pending?.payload.operationId ?? null}
                    pending={draftIntent.pending}
                    corruptReason={draftIntent.corruptReason}
                    authorityMismatch={draftIntent.authorityMismatch}
                    inFlight={draftIntent.inFlight}
                    onRetry={() => void handleCountDraftRetry()}
                    onClearRejected={() => {
                      draftIntent.clearPending(false);
                      setCountMessage("Cleared rejected draft intent. Refresh live stock if balances changed.");
                    }}
                    onClearCorrupt={() => {
                      draftIntent.clearPending(true);
                      setCountMessage("Cleared corrupt count-draft storage after verification.");
                    }}
                  />

                  <form onSubmit={(e) => void handleCountDraftSubmit(e)} style={{ display: "grid", gap: 12, maxWidth: 560 }}>
                    <label>
                      <div className="r3-text-xs r3-text-muted" style={{ marginBottom: 4 }}>
                        Ingredient
                      </div>
                      <select
                        className="r3-input"
                        aria-label="Count Ingredient"
                        value={countIngredientId}
                        onChange={(e) => setCountIngredientId(e.target.value)}
                        disabled={draftIntent.inFlight}
                      >
                        {ingredients.map((i) => (
                          <option key={i.id} value={i.id}>
                            {i.name} (Expected on hand: {formatQty(i.stockOnHandX1000, i.stockUnit)})
                          </option>
                        ))}
                      </select>
                    </label>
                    {selectedCountIngredient && (
                      <div className="r3-text-sm r3-text-muted">
                        Live expected balance from Host:{" "}
                        <strong className="r3-tabular">
                          {formatQty(selectedCountIngredient.stockOnHandX1000, selectedCountIngredient.stockUnit)}
                        </strong>
                      </div>
                    )}
                    <label>
                      <div className="r3-text-xs r3-text-muted" style={{ marginBottom: 4 }}>
                        Physical Counted Quantity
                      </div>
                      <input
                        className="r3-input"
                        aria-label="Physical Counted Quantity"
                        value={countedInput}
                        onChange={(e) => setCountedInput(e.target.value)}
                        placeholder="e.g. 95"
                        disabled={draftIntent.inFlight}
                      />
                    </label>
                    <label>
                      <div className="r3-text-xs r3-text-muted" style={{ marginBottom: 4 }}>
                        Count Notes (optional)
                      </div>
                      <input
                        className="r3-input"
                        aria-label="Count Notes"
                        value={countNotes}
                        onChange={(e) => setCountNotes(e.target.value)}
                        placeholder="Closing bar shelf count"
                        disabled={draftIntent.inFlight}
                      />
                    </label>
                    <div>
                      <button type="submit" className="r3-btn r3-btn--primary" disabled={draftIntent.inFlight}>
                        {draftIntent.inFlight ? "Saving Draft…" : "Save Count Draft"}
                      </button>
                    </div>
                  </form>
                </div>

                <div className="r3-card">
                  <div className="r3-card-header">
                    <div>
                      <div className="r3-card-title">Step 2 — Review &amp; Finalize Count Session</div>
                      <div className="r3-card-subtitle">
                        Finalizing applies exact variance deltas atomically and seals the count session as immutable.
                      </div>
                    </div>
                    <form onSubmit={(e) => void handleLoadCountSession(e)} style={{ display: "flex", gap: 8 }}>
                      <input
                        className="r3-input"
                        aria-label="Lookup Count Session ID"
                        value={lookupCountId}
                        onChange={(e) => setLookupCountId(e.target.value)}
                        placeholder="Load session GUID…"
                        style={{ width: 220 }}
                      />
                      <button type="submit" className="r3-btn r3-btn--outline r3-btn--sm">
                        Load
                      </button>
                    </form>
                  </div>

                  <DurableIntentBanner
                    label="Finalize"
                    operationId={finalizeIntent.pending?.payload.command.operationId ?? null}
                    pending={finalizeIntent.pending}
                    corruptReason={finalizeIntent.corruptReason}
                    authorityMismatch={finalizeIntent.authorityMismatch}
                    inFlight={finalizeIntent.inFlight}
                    onRetry={() => void handleFinalizeRetry()}
                    onClearRejected={() => {
                      finalizeIntent.clearPending(false);
                      setCountMessage("Cleared rejected finalize intent.");
                    }}
                    onClearCorrupt={() => {
                      finalizeIntent.clearPending(true);
                      setCountMessage("Cleared corrupt finalize storage after verification.");
                    }}
                  />

                  {!activeCount ? (
                    <p className="r3-text-muted r3-text-sm">
                      No active count session loaded. Save a count draft above or load an existing session GUID.
                    </p>
                  ) : (
                    <div>
                      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 12, marginBottom: 12, flexWrap: "wrap" }}>
                        <div>
                          Session <code>{activeCount.countSessionId}</code>{" "}
                          <span
                            className={`r3-badge ${activeCount.state === "Finalized" ? "r3-badge--success" : "r3-badge--warn"}`}
                          >
                            {activeCount.state}
                          </span>
                        </div>
                        {activeCount.state === "Draft" ? (
                          <button
                            type="button"
                            className="r3-btn r3-btn--primary"
                            onClick={() => void handleFinalizeCount()}
                            disabled={finalizeIntent.inFlight}
                          >
                            {finalizeIntent.inFlight ? "Finalizing…" : "Finalize Physical Count"}
                          </button>
                        ) : (
                          <span className="r3-text-xs r3-text-muted">
                            Immutable — finalized at {activeCount.finalizedAtUtc ? new Date(activeCount.finalizedAtUtc).toLocaleString() : "—"}
                          </span>
                        )}
                      </div>
                      <div className="r3-table-wrap" tabIndex={0} role="region" aria-label="Count Session Lines">
                        <table className="r3-table">
                          <caption className="r3-sr-only">Count Session Lines</caption>
                          <thead>
                            <tr>
                              <th scope="col">Item</th>
                              <th scope="col">Expected Before</th>
                              <th scope="col">Counted</th>
                              <th scope="col">Variance Delta</th>
                            </tr>
                          </thead>
                          <tbody>
                            {activeCount.lines.map((line) => (
                              <tr key={line.lineId}>
                                <th scope="row">{line.nameSnapshot}</th>
                                <td className="r3-tabular">{formatQty(line.expectedBeforeX1000, line.stockUnitSnapshot)}</td>
                                <td className="r3-tabular">{formatQty(line.countedX1000, line.stockUnitSnapshot)}</td>
                                <td className="r3-tabular">{formatQty(line.quantityDeltaX1000, line.stockUnitSnapshot)}</td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                    </div>
                  )}

                  {countMessage && (
                    <p role="status" style={{ marginTop: 12 }} className="r3-text-sm">
                      {countMessage}
                    </p>
                  )}
                </div>
              </div>
            )}

            {tab === "receive" && canReceive && (
              <div role="tabpanel" aria-labelledby="r3-inv-tab-receive" className="r3-card">
                <div className="r3-card-header">
                  <div>
                    <div className="r3-card-title">Receive Purchase Stock</div>
                    <div className="r3-card-subtitle">Commits directly to POST /api/cafe/purchases/receive</div>
                  </div>
                </div>
                <DurableIntentBanner
                  label="Purchase"
                  operationId={purchaseIntent.pending?.payload.operationId ?? null}
                  pending={purchaseIntent.pending}
                  corruptReason={purchaseIntent.corruptReason}
                  authorityMismatch={purchaseIntent.authorityMismatch}
                  inFlight={purchaseIntent.inFlight}
                  onRetry={() => void handleReceiveRetry()}
                  onClearRejected={() => {
                    purchaseIntent.clearPending(false);
                    setRecMessage("Cleared rejected purchase intent.");
                  }}
                  onClearCorrupt={() => {
                    purchaseIntent.clearPending(true);
                    setRecMessage("Cleared corrupt purchase storage after verification.");
                  }}
                />
                <form onSubmit={(e) => void handleReceiveSubmit(e)} style={{ display: "grid", gap: 12, maxWidth: 560 }}>
                  <label>
                    <div className="r3-text-xs r3-text-muted" style={{ marginBottom: 4 }}>Supplier</div>
                    <select className="r3-input" value={recSupplierId} onChange={(e) => setRecSupplierId(e.target.value)} disabled={purchaseIntent.inFlight}>
                      {suppliers.map((s) => (
                        <option key={s.id} value={s.id}>{s.name}</option>
                      ))}
                    </select>
                  </label>
                  <label>
                    <div className="r3-text-xs r3-text-muted" style={{ marginBottom: 4 }}>Ingredient</div>
                    <select className="r3-input" value={recIngredientId} onChange={(e) => setRecIngredientId(e.target.value)} disabled={purchaseIntent.inFlight}>
                      {ingredients.map((i) => (
                        <option key={i.id} value={i.id}>{i.name} ({i.stockUnit})</option>
                      ))}
                    </select>
                  </label>
                  <label>
                    <div className="r3-text-xs r3-text-muted" style={{ marginBottom: 4 }}>Quantity Received</div>
                    <input className="r3-input" value={recQtyInput} onChange={(e) => setRecQtyInput(e.target.value)} placeholder="10" disabled={purchaseIntent.inFlight} />
                  </label>
                  <label>
                    <div className="r3-text-xs r3-text-muted" style={{ marginBottom: 4 }}>Total Line Cost (PHP)</div>
                    <input className="r3-input" value={recCostInput} onChange={(e) => setRecCostInput(e.target.value)} placeholder="500.00" disabled={purchaseIntent.inFlight} />
                  </label>
                  <label>
                    <div className="r3-text-xs r3-text-muted" style={{ marginBottom: 4 }}>Receipt Reference (optional)</div>
                    <input className="r3-input" value={recRef} onChange={(e) => setRecRef(e.target.value)} placeholder="INV-1001" disabled={purchaseIntent.inFlight} />
                  </label>
                  <div>
                    <button type="submit" className="r3-btn r3-btn--primary" disabled={purchaseIntent.inFlight}>
                      {purchaseIntent.inFlight ? "Committing…" : "Commit Purchase Receipt"}
                    </button>
                  </div>
                </form>
                {recMessage && <p role="status" style={{ marginTop: 12 }} className="r3-text-sm">{recMessage}</p>}
              </div>
            )}

            {tab === "waste" && canWaste && (
              <div role="tabpanel" aria-labelledby="r3-inv-tab-waste" className="r3-card">
                <div className="r3-card-header">
                  <div>
                    <div className="r3-card-title">Record Inventory Waste</div>
                    <div className="r3-card-subtitle">Commits directly to POST /api/cafe/inventory/waste</div>
                  </div>
                </div>
                <DurableIntentBanner
                  label="Waste"
                  operationId={wasteIntent.pending?.payload.operationId ?? null}
                  pending={wasteIntent.pending}
                  corruptReason={wasteIntent.corruptReason}
                  authorityMismatch={wasteIntent.authorityMismatch}
                  inFlight={wasteIntent.inFlight}
                  onRetry={() => void handleWasteRetry()}
                  onClearRejected={() => {
                    wasteIntent.clearPending(false);
                    setWasteMessage("Cleared rejected waste intent.");
                  }}
                  onClearCorrupt={() => {
                    wasteIntent.clearPending(true);
                    setWasteMessage("Cleared corrupt waste storage after verification.");
                  }}
                />
                <form onSubmit={(e) => void handleWasteSubmit(e)} style={{ display: "grid", gap: 12, maxWidth: 560 }}>
                  <label>
                    <div className="r3-text-xs r3-text-muted" style={{ marginBottom: 4 }}>Ingredient</div>
                    <select className="r3-input" value={wasteIngredientId} onChange={(e) => setWasteIngredientId(e.target.value)} disabled={wasteIntent.inFlight}>
                      {ingredients.map((i) => (
                        <option key={i.id} value={i.id}>{i.name} (On hand: {formatQty(i.stockOnHandX1000, i.stockUnit)})</option>
                      ))}
                    </select>
                  </label>
                  <label>
                    <div className="r3-text-xs r3-text-muted" style={{ marginBottom: 4 }}>Quantity Wasted</div>
                    <input className="r3-input" value={wasteQtyInput} onChange={(e) => setWasteQtyInput(e.target.value)} placeholder="1" disabled={wasteIntent.inFlight} />
                  </label>
                  <label>
                    <div className="r3-text-xs r3-text-muted" style={{ marginBottom: 4 }}>Reason</div>
                    <select className="r3-input" value={wasteReason} onChange={(e) => setWasteReason(e.target.value as WasteReason)} disabled={wasteIntent.inFlight}>
                      <option value="Waste">Waste</option>
                      <option value="Spoilage">Spoilage</option>
                      <option value="Damage">Damage</option>
                      <option value="Expired">Expired</option>
                    </select>
                  </label>
                  <label>
                    <div className="r3-text-xs r3-text-muted" style={{ marginBottom: 4 }}>Notes (optional)</div>
                    <input className="r3-input" value={wasteNotes} onChange={(e) => setWasteNotes(e.target.value)} placeholder="Spilled during prep" disabled={wasteIntent.inFlight} />
                  </label>
                  <div>
                    <button type="submit" className="r3-btn r3-btn--primary" disabled={wasteIntent.inFlight}>
                      {wasteIntent.inFlight ? "Committing…" : "Commit Waste Event"}
                    </button>
                  </div>
                </form>
                {wasteMessage && <p role="status" style={{ marginTop: 12 }} className="r3-text-sm">{wasteMessage}</p>}
              </div>
            )}
          </>
        )}
      </div>
  );

  if (embedded) return inner;
  return <div className="r3-root">{inner}</div>;
}

export function R3LiveInventoryPage({ embedded = false }: { embedded?: boolean } = {}) {
  if (embedded) {
    return <R3LiveInventoryContent embedded />;
  }
  return (
    <R3ToastProvider>
      <R3LiveInventoryContent />
    </R3ToastProvider>
  );
}
