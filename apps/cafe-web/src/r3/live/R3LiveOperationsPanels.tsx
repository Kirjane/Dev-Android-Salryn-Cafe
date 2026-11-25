import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { ToastProvider } from "@salryn/cafe-ui";
import { createLiveBackofficeApi } from "../../api/backofficeClient";
import { createLiveHostApi, formatPhp, hostRequest, parsePhpToCents } from "../../api/hostClient";
import type {
  ArCustomerRow,
  AttendanceRow,
  CafeRefundSummaryRow,
  IncidentClassification,
  IncidentRow,
  IngredientRecipeDraft,
  LowStockItem,
  MenuCatalogDraft,
  MenuUnitOption,
  OwnerReportsSnapshot,
  RefundableSaleRow,
} from "../../backoffice/types";
import type { CafeOrderSummary, HostSession, HostShiftInfo } from "../../cashier/types";
import { CafeMenuManagerRecovery } from "../../recovery/CafeMenuManagerRecovery";
import {
  fetchMenuCatalog,
  fetchProductUnits,
  type MenuCatalogDraft as C1MenuCatalogDraft,
} from "../../recovery/menuCatalogAdapter";
import { DurableIntentBanner } from "./DurableIntentBanner";
import { useDurableIntent, type DurableIntentAuthority } from "./durableIntent";
import type { R3RouteKey } from "./r3RouteMap";

export function R3TodayPanel({
  session,
  onNavigate,
}: {
  session: HostSession;
  onNavigate: (key: R3RouteKey) => void;
}) {
  const backofficeApi = useMemo(() => createLiveBackofficeApi(), []);
  const hostApi = useMemo(() => createLiveHostApi(), []);
  const perms = session.user?.permissions ?? [];
  const has = (p: string) => perms.includes(p);

  const [reports, setReports] = useState<OwnerReportsSnapshot | null>(null);
  const [orders, setOrders] = useState<CafeOrderSummary[]>([]);
  const [lowStock, setLowStock] = useState<LowStockItem[]>([]);
  const [shift, setShift] = useState<HostShiftInfo | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [rep, ord, low, sh] = await Promise.all([
        has("Reports.View") ? backofficeApi.getOwnerReports().catch(() => null) : Promise.resolve(null),
        has("Orders.View") ? hostApi.getOrders().catch(() => []) : Promise.resolve([]),
        has("Inventory.View") ? backofficeApi.getLowStock().catch(() => []) : Promise.resolve([]),
        has("Shift.OpenClose") ? hostApi.getShift().then((r) => r.shift).catch(() => null) : Promise.resolve(null),
      ]);
      setReports(rep);
      setOrders(ord);
      setLowStock(low);
      setShift(sh);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to load today overview.");
    } finally {
      setLoading(false);
    }
  }, [backofficeApi, hostApi, perms]);

  useEffect(() => {
    void load();
  }, [load]);

  const activeKitchenCount = orders.filter((o) => o.state === "Submitted" || o.state === "Preparing").length;
  const openOrdersCount = orders.filter((o) => o.state === "Open" || o.state === "Parked" || o.state === "Ready").length;

  return (
    <div style={{ display: "grid", gap: 16 }}>
      <div className="r3-page-header">
        <div>
          <h1 className="r3-page-title">Today — Live Operations Overview</h1>
          <p className="r3-page-subtitle">
            Real-time snapshot from local Host SQLite (Asia/Manila). Zero mock or placeholder figures.
          </p>
        </div>
        <button type="button" className="r3-btn r3-btn--outline r3-btn--sm" onClick={() => void load()} disabled={loading}>
          {loading ? "Refreshing…" : "Refresh Overview"}
        </button>
      </div>

      {error && <div className="r3-alert r3-alert--danger" role="alert"><div className="r3-alert-body">{error}</div></div>}

      <div className="r3-metrics-grid r3-metrics-grid--2x2-mobile">
        <div className="r3-metric-card r3-metric-card--accent">
          <div className="r3-metric-label">Today&apos;s Sales</div>
          <div className="r3-metric-value r3-tabular">
            {reports ? formatPhp(reports.daily.salesAmountCents) : "—"}
          </div>
          <div className="r3-metric-sub">{reports ? `${reports.daily.salesCount} completed sales` : "Host summary"}</div>
        </div>
        <div className="r3-metric-card r3-metric-card--info">
          <div className="r3-metric-label">Active Orders / Kitchen</div>
          <div className="r3-metric-value r3-tabular">{activeKitchenCount} / {openOrdersCount}</div>
          <div className="r3-metric-sub">In-prep tickets / open orders</div>
        </div>
        <div className="r3-metric-card r3-metric-card--warn">
          <div className="r3-metric-label">Low Stock Alerts</div>
          <div className="r3-metric-value r3-tabular">{lowStock.length}</div>
          <div className="r3-metric-sub">Ingredients at or below reorder</div>
        </div>
        <div className="r3-metric-card r3-metric-card--success">
          <div className="r3-metric-label">Cashier Shift</div>
          <div className="r3-metric-value">{shift?.shiftState ?? "Closed"}</div>
          <div className="r3-metric-sub">
            {shift?.shiftState === "Open"
              ? `Float ${formatPhp(shift.openingCashCents ?? 0)}`
              : "Open shift in Cash & Shifts"}
          </div>
        </div>
      </div>

      <div className="r3-card">
        <div className="r3-card-header">
          <div>
            <div className="r3-card-title">Quick Workspace Actions</div>
            <div className="r3-card-subtitle">Jump directly to live C1–C11 operational modules</div>
          </div>
        </div>
        <div className="r3-card-body" style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
          <button type="button" className="r3-btn r3-btn--primary r3-btn--sm" onClick={() => onNavigate("pos")}>
            Open POS Counter
          </button>
          <button type="button" className="r3-btn r3-btn--outline r3-btn--sm" onClick={() => onNavigate("cash")}>
            Cash &amp; Shifts
          </button>
          <button type="button" className="r3-btn r3-btn--outline r3-btn--sm" onClick={() => onNavigate("inventory")}>
            Inventory Control
          </button>
          <button type="button" className="r3-btn r3-btn--outline r3-btn--sm" onClick={() => onNavigate("bookings")}>
            Bookings &amp; Deliveries
          </button>
          <button type="button" className="r3-btn r3-btn--outline r3-btn--sm" onClick={() => onNavigate("reports")}>
            Reports &amp; COGS
          </button>
        </div>
      </div>
    </div>
  );
}

export function R3LiveMenuPanel() {
  const [catalog, setCatalog] = useState<C1MenuCatalogDraft | null>(null);
  const [units, setUnits] = useState<MenuUnitOption[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async (): Promise<C1MenuCatalogDraft | null> => {
    setError(null);
    try {
      const [cat, unitList] = await Promise.all([fetchMenuCatalog(), fetchProductUnits()]);
      setCatalog(cat);
      setUnits(unitList);
      return cat;
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to load menu catalog.");
      return null;
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  return (
    <ToastProvider>
      <div style={{ display: "grid", gap: 16 }}>
        <div className="r3-page-header">
          <div>
            <h1 className="r3-page-title">Menu, Variants &amp; Modifiers</h1>
            <p className="r3-page-subtitle">
              Full C1 live Host menu authoring — categories, items, variants, modifier groups/options, and publish.
            </p>
          </div>
        </div>
        {error && <div className="r3-alert r3-alert--danger" role="alert"><div className="r3-alert-body">{error}</div></div>}
        {loading && !catalog ? (
          <div className="r3-card" style={{ padding: 20 }}>Loading live menu catalog…</div>
        ) : catalog ? (
          <CafeMenuManagerRecovery catalog={catalog} units={units} onRefresh={load} />
        ) : null}
      </div>
    </ToastProvider>
  );
}

export function R3LiveRecipesPanel({ authority }: { authority: DurableIntentAuthority | null }) {
  const api = useMemo(() => createLiveBackofficeApi(), []);
  const [draft, setDraft] = useState<IngredientRecipeDraft | null>(null);
  const [catalog, setCatalog] = useState<MenuCatalogDraft | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [msg, setMsg] = useState<string | null>(null);

  const [ingName, setIngName] = useState("");
  const [ingUnit, setIngUnit] = useState<"Gram" | "Milliliter" | "Piece">("Gram");
  const [ingReorder, setIngReorder] = useState("100");

  const [recName, setRecName] = useState("");
  const [recYield, setRecYield] = useState("1");

  const [lineRecipeId, setLineRecipeId] = useState("");
  const [lineIngredientId, setLineIngredientId] = useState("");
  const [lineQty, setLineQty] = useState("18");

  const [bindProductId, setBindProductId] = useState("");
  const [bindRecipeId, setBindRecipeId] = useState("");
  const [bindVariantId, setBindVariantId] = useState("");
  const [bindModifierOptionId, setBindModifierOptionId] = useState("");

  const ingIntent = useDurableIntent<
    { operationId: string; name: string; stockUnit: "Gram" | "Milliliter" | "Piece"; reorderLevelX1000: number },
    void
  >("recipes-save-ingredient", authority);
  const recipeIntent = useDurableIntent<{ operationId: string; name: string; yieldX1000: number }, void>(
    "recipes-save-recipe",
    authority,
  );
  const lineIntent = useDurableIntent<
    { operationId: string; recipeId: string; ingredientId: string; inputUnit: string; declaredQuantityX1000: number },
    void
  >("recipes-save-line", authority);
  const bindIntent = useDurableIntent<{ operationId: string; productId: string; recipeId: string }, void>(
    "recipes-bind-item",
    authority,
  );
  const bindVariantIntent = useDurableIntent<{ operationId: string; variantId: string; recipeId: string }, unknown>(
    "recipes-bind-variant",
    authority,
  );
  const bindModifierIntent = useDurableIntent<{ operationId: string; optionId: string; recipeId: string }, unknown>(
    "recipes-bind-modifier",
    authority,
  );
  const publishIntent = useDurableIntent<{ operationId: string }, unknown>("recipes-publish", authority);

  const load = useCallback(async () => {
    setError(null);
    try {
      const [ir, mc] = await Promise.all([
        api.getIngredientsAndRecipes(),
        api.getMenuCatalog().catch(() => null),
      ]);
      setDraft(ir);
      setCatalog(mc);
      if (ir.recipes.length > 0) {
        setLineRecipeId((p) => p || ir.recipes[0].id);
        setBindRecipeId((p) => p || ir.recipes[0].id);
      }
      if (ir.ingredients.length > 0) {
        setLineIngredientId((p) => p || ir.ingredients[0].id);
      }
      if (mc && mc.items.length > 0) {
        setBindProductId((p) => p || mc.items[0].productId);
      }
      if (mc && mc.variants.length > 0) {
        setBindVariantId((p) => p || mc.variants[0].id);
      }
      if (mc && mc.modifierOptions && mc.modifierOptions.length > 0) {
        setBindModifierOptionId((p) => p || mc.modifierOptions![0].id);
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to load ingredients and recipes.");
    }
  }, [api]);

  useEffect(() => {
    void load();
  }, [load]);

  const handleSaveIngredient = async (e: FormEvent) => {
    e.preventDefault();
    setMsg(null);
    setError(null);
    const reorderX1000 = Math.round(Number(ingReorder) * 1000);
    if (!ingName.trim() || !Number.isSafeInteger(reorderX1000) || reorderX1000 < 0) {
      setError("Valid ingredient name and nonnegative reorder level are required.");
      return;
    }
    const res = await ingIntent.execute(
      { name: ingName.trim(), stockUnit: ingUnit, reorderLevelX1000: reorderX1000 },
      () => ({
        operationId: crypto.randomUUID(),
        name: ingName.trim(),
        stockUnit: ingUnit,
        reorderLevelX1000: reorderX1000,
      }),
      (p) => api.saveIngredient(p),
    );
    if (res.ok) {
      setIngName("");
      setMsg("Ingredient saved to live Host.");
      await load();
    } else setError(res.message);
  };

  const handleSaveRecipe = async (e: FormEvent) => {
    e.preventDefault();
    setMsg(null);
    setError(null);
    const yieldX1000 = Math.round(Number(recYield) * 1000);
    if (!recName.trim() || !Number.isSafeInteger(yieldX1000) || yieldX1000 <= 0) {
      setError("Valid recipe name and positive yield are required.");
      return;
    }
    const res = await recipeIntent.execute(
      { name: recName.trim(), yieldX1000 },
      () => ({ operationId: crypto.randomUUID(), name: recName.trim(), yieldX1000 }),
      (p) => api.saveRecipe(p),
    );
    if (res.ok) {
      setRecName("");
      setMsg("Recipe draft saved.");
      await load();
    } else setError(res.message);
  };

  const handleAddLine = async (e: FormEvent) => {
    e.preventDefault();
    setMsg(null);
    setError(null);
    const qtyX1000 = Math.round(Number(lineQty) * 1000);
    const ing = draft?.ingredients.find((x) => x.id === lineIngredientId);
    if (!lineRecipeId || !ing || !Number.isSafeInteger(qtyX1000) || qtyX1000 <= 0) {
      setError("Select recipe, ingredient, and positive quantity.");
      return;
    }
    const res = await lineIntent.execute(
      { recipeId: lineRecipeId, ingredientId: ing.id, inputUnit: ing.stockUnit, declaredQuantityX1000: qtyX1000 },
      () => ({
        operationId: crypto.randomUUID(),
        recipeId: lineRecipeId,
        ingredientId: ing.id,
        inputUnit: ing.stockUnit,
        declaredQuantityX1000: qtyX1000,
      }),
      (p) => api.saveRecipeLine(p),
    );
    if (res.ok) {
      setMsg("Recipe ingredient line saved.");
      await load();
    } else setError(res.message);
  };

  const handleBind = async (e: FormEvent) => {
    e.preventDefault();
    setMsg(null);
    setError(null);
    if (!bindProductId || !bindRecipeId) return;
    const res = await bindIntent.execute(
      { productId: bindProductId, recipeId: bindRecipeId },
      () => ({ operationId: crypto.randomUUID(), productId: bindProductId, recipeId: bindRecipeId }),
      (p) => api.bindMenuItemRecipe(p),
    );
    if (res.ok) {
      setMsg("Menu item bound to recipe.");
      await load();
    } else setError(res.message);
  };

  const handleBindVariant = async (e: FormEvent) => {
    e.preventDefault();
    setMsg(null);
    setError(null);
    if (!bindVariantId || !bindRecipeId) return;
    const res = await bindVariantIntent.execute(
      { variantId: bindVariantId, recipeId: bindRecipeId },
      () => ({ operationId: crypto.randomUUID(), variantId: bindVariantId, recipeId: bindRecipeId }),
      (p) => hostRequest(`/api/cafe/recipes/variants/${encodeURIComponent(p.variantId)}`, { recipeId: p.recipeId }, "PUT", p.operationId),
    );
    if (res.ok) {
      setMsg("Variant bound to recipe (C2-DAT-10).");
      await load();
    } else setError(res.message);
  };

  const handleBindModifier = async (e: FormEvent) => {
    e.preventDefault();
    setMsg(null);
    setError(null);
    if (!bindModifierOptionId || !bindRecipeId) return;
    const res = await bindModifierIntent.execute(
      { optionId: bindModifierOptionId, recipeId: bindRecipeId },
      () => ({ operationId: crypto.randomUUID(), optionId: bindModifierOptionId, recipeId: bindRecipeId }),
      (p) => hostRequest(`/api/cafe/recipes/modifier-options/${encodeURIComponent(p.optionId)}`, { recipeId: p.recipeId }, "PUT", p.operationId),
    );
    if (res.ok) {
      setMsg("Modifier option bound to additive recipe (C2-DAT-10).");
      await load();
    } else setError(res.message);
  };

  const handlePublishRecipes = async () => {
    setMsg(null);
    setError(null);
    const res = await publishIntent.execute(
      { action: "recipes-publish" },
      () => ({ operationId: crypto.randomUUID() }),
      (p) => hostRequest("/api/cafe/recipes/publish", p, "POST", p.operationId),
    );
    if (res.ok) {
      setMsg("Recipes published to live Host.");
      await load();
    } else setError(res.message);
  };

  return (
    <div style={{ display: "grid", gap: 16 }}>
      <div className="r3-page-header">
        <div>
          <h1 className="r3-page-title">Recipes &amp; BOM Costing</h1>
          <p className="r3-page-subtitle">C2 ingredients, recipes, bill-of-materials lines, menu bindings, and recipe publication.</p>
        </div>
        <button type="button" className="r3-btn r3-btn--primary r3-btn--sm" onClick={() => void handlePublishRecipes()} disabled={publishIntent.inFlight}>
          Publish Recipes
        </button>
      </div>

      {error && <div className="r3-alert r3-alert--danger" role="alert"><div className="r3-alert-body">{error}</div></div>}
      {msg && <div className="r3-alert r3-alert--success" role="status"><div className="r3-alert-body">{msg}</div></div>}

      <DurableIntentBanner
        label="Publish Recipes"
        operationId={publishIntent.pending?.payload.operationId ?? null}
        pending={publishIntent.pending}
        corruptReason={publishIntent.corruptReason}
        authorityMismatch={publishIntent.authorityMismatch}
        inFlight={publishIntent.inFlight}
        onRetry={() =>
          void publishIntent
            .retryPending((p) => hostRequest("/api/cafe/recipes/publish", p, "POST", p.operationId))
            .then((r) => {
              if (r.ok) {
                setMsg("Recipes publish retry confirmed.");
                void load();
              } else {
                setError(r.message);
              }
            })
        }
        onClearRejected={() => publishIntent.clearPending(false)}
        onClearCorrupt={() => publishIntent.clearPending(true)}
      />

      <div className="r3-card">
        <div className="r3-card-header"><div className="r3-card-title">Live Recipes ({draft?.recipes.length ?? 0}) &amp; BOM Lines ({draft?.lines.length ?? 0})</div></div>
        <div className="r3-table-wrap" tabIndex={0} role="region" aria-label="Recipes List">
          <table className="r3-table">
            <caption className="r3-sr-only">Recipes List</caption>
            <thead>
              <tr>
                <th scope="col">Recipe</th>
                <th scope="col">Yield</th>
                <th scope="col">Lines</th>
              </tr>
            </thead>
            <tbody>
              {(draft?.recipes ?? []).length === 0 ? (
                <tr><td colSpan={3} className="r3-text-muted">No recipes created yet.</td></tr>
              ) : (
                draft!.recipes.map((r) => (
                  <tr key={r.id}>
                    <th scope="row">{r.name}</th>
                    <td className="r3-tabular">{r.yieldX1000 / 1000}</td>
                    <td className="r3-tabular">{draft!.lines.filter((l) => l.recipeId === r.id).length}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>

      <div className="r3-two-col">
        <div className="r3-card">
          <div className="r3-card-header"><div className="r3-card-title">Add Ingredient</div></div>
          <DurableIntentBanner
            label="Save Ingredient"
            operationId={ingIntent.pending?.payload.operationId ?? null}
            pending={ingIntent.pending}
            corruptReason={ingIntent.corruptReason}
            authorityMismatch={ingIntent.authorityMismatch}
            inFlight={ingIntent.inFlight}
            onRetry={() =>
              void ingIntent.retryPending((p) => api.saveIngredient(p)).then((r) => {
                if (r.ok) { setIngName(""); setMsg("Ingredient retry confirmed."); void load(); } else setError(r.message);
              })
            }
            onClearRejected={() => ingIntent.clearPending(false)}
            onClearCorrupt={() => ingIntent.clearPending(true)}
          />
          <form onSubmit={(e) => void handleSaveIngredient(e)} style={{ display: "grid", gap: 10 }}>
            <input className="r3-input" aria-label="Ingredient Name" value={ingName} onChange={(e) => setIngName(e.target.value)} placeholder="Whole Milk" />
            <select className="r3-input" aria-label="Ingredient Stock Unit" value={ingUnit} onChange={(e) => setIngUnit(e.target.value as "Gram" | "Milliliter" | "Piece")}>
              <option value="Gram">Gram</option>
              <option value="Milliliter">Milliliter</option>
              <option value="Piece">Piece</option>
            </select>
            <input className="r3-input" aria-label="Reorder Level" value={ingReorder} onChange={(e) => setIngReorder(e.target.value)} placeholder="100" />
            <button type="submit" className="r3-btn r3-btn--primary r3-btn--sm" disabled={ingIntent.inFlight}>Save Ingredient</button>
          </form>
        </div>

        <div className="r3-card">
          <div className="r3-card-header"><div className="r3-card-title">Create Recipe</div></div>
          <DurableIntentBanner
            label="Save Recipe"
            operationId={recipeIntent.pending?.payload.operationId ?? null}
            pending={recipeIntent.pending}
            corruptReason={recipeIntent.corruptReason}
            authorityMismatch={recipeIntent.authorityMismatch}
            inFlight={recipeIntent.inFlight}
            onRetry={() =>
              void recipeIntent.retryPending((p) => api.saveRecipe(p)).then((r) => {
                if (r.ok) { setRecName(""); setMsg("Recipe retry confirmed."); void load(); } else setError(r.message);
              })
            }
            onClearRejected={() => recipeIntent.clearPending(false)}
            onClearCorrupt={() => recipeIntent.clearPending(true)}
          />
          <form onSubmit={(e) => void handleSaveRecipe(e)} style={{ display: "grid", gap: 10 }}>
            <input className="r3-input" aria-label="Recipe Name" value={recName} onChange={(e) => setRecName(e.target.value)} placeholder="Iced Latte 16oz" />
            <input className="r3-input" aria-label="Recipe Yield" value={recYield} onChange={(e) => setRecYield(e.target.value)} placeholder="1" />
            <button type="submit" className="r3-btn r3-btn--primary r3-btn--sm" disabled={recipeIntent.inFlight}>Save Recipe</button>
          </form>
        </div>
      </div>

      <div className="r3-two-col">
        <div className="r3-card">
          <div className="r3-card-header"><div className="r3-card-title">Add BOM Ingredient Line</div></div>
          <DurableIntentBanner
            label="Recipe Line"
            operationId={lineIntent.pending?.payload.operationId ?? null}
            pending={lineIntent.pending}
            corruptReason={lineIntent.corruptReason}
            authorityMismatch={lineIntent.authorityMismatch}
            inFlight={lineIntent.inFlight}
            onRetry={() =>
              void lineIntent.retryPending((p) => api.saveRecipeLine(p)).then((r) => {
                if (r.ok) { setMsg("Recipe line retry confirmed."); void load(); } else setError(r.message);
              })
            }
            onClearRejected={() => lineIntent.clearPending(false)}
            onClearCorrupt={() => lineIntent.clearPending(true)}
          />
          <form onSubmit={(e) => void handleAddLine(e)} style={{ display: "grid", gap: 10 }}>
            <select className="r3-input" aria-label="Line Recipe" value={lineRecipeId} onChange={(e) => setLineRecipeId(e.target.value)}>
              {(draft?.recipes ?? []).map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}
            </select>
            <select className="r3-input" aria-label="Line Ingredient" value={lineIngredientId} onChange={(e) => setLineIngredientId(e.target.value)}>
              {(draft?.ingredients ?? []).map((i) => <option key={i.id} value={i.id}>{i.name} ({i.stockUnit})</option>)}
            </select>
            <input className="r3-input" aria-label="Line Quantity" value={lineQty} onChange={(e) => setLineQty(e.target.value)} placeholder="18" />
            <button type="submit" className="r3-btn r3-btn--outline r3-btn--sm" disabled={lineIntent.inFlight}>Add Recipe Line</button>
          </form>
        </div>

        <div className="r3-card">
          <div className="r3-card-header"><div className="r3-card-title">Bind Menu Item to Recipe</div></div>
          <DurableIntentBanner
            label="Bind Item Recipe"
            operationId={bindIntent.pending?.payload.operationId ?? null}
            pending={bindIntent.pending}
            corruptReason={bindIntent.corruptReason}
            authorityMismatch={bindIntent.authorityMismatch}
            inFlight={bindIntent.inFlight}
            onRetry={() =>
              void bindIntent.retryPending((p) => api.bindMenuItemRecipe(p)).then((r) => {
                if (r.ok) { setMsg("Menu item recipe binding retry confirmed."); void load(); } else setError(r.message);
              })
            }
            onClearRejected={() => bindIntent.clearPending(false)}
            onClearCorrupt={() => bindIntent.clearPending(true)}
          />
          <form onSubmit={(e) => void handleBind(e)} style={{ display: "grid", gap: 10 }}>
            <select className="r3-input" aria-label="Menu Item" value={bindProductId} onChange={(e) => setBindProductId(e.target.value)}>
              {(catalog?.items ?? []).map((it) => <option key={it.productId} value={it.productId}>{it.name}</option>)}
            </select>
            <select className="r3-input" aria-label="Bound Recipe" value={bindRecipeId} onChange={(e) => setBindRecipeId(e.target.value)}>
              {(draft?.recipes ?? []).map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}
            </select>
            <button type="submit" className="r3-btn r3-btn--outline r3-btn--sm" disabled={bindIntent.inFlight}>Bind Item Recipe</button>
          </form>
        </div>
      </div>

      <div className="r3-two-col">
        <div className="r3-card">
          <div className="r3-card-header"><div className="r3-card-title">Bind Variant Recipe (C2-DAT-10)</div></div>
          <DurableIntentBanner
            label="Bind Variant Recipe"
            operationId={bindVariantIntent.pending?.payload.operationId ?? null}
            pending={bindVariantIntent.pending}
            corruptReason={bindVariantIntent.corruptReason}
            authorityMismatch={bindVariantIntent.authorityMismatch}
            inFlight={bindVariantIntent.inFlight}
            onRetry={() =>
              void bindVariantIntent
                .retryPending((p) => hostRequest(`/api/cafe/recipes/variants/${encodeURIComponent(p.variantId)}`, { recipeId: p.recipeId }, "PUT", p.operationId))
                .then((r) => { if (r.ok) { setMsg("Variant recipe binding retry confirmed."); void load(); } else setError(r.message); })
            }
            onClearRejected={() => bindVariantIntent.clearPending(false)}
            onClearCorrupt={() => bindVariantIntent.clearPending(true)}
          />
          <form onSubmit={(e) => void handleBindVariant(e)} style={{ display: "grid", gap: 10 }}>
            <select className="r3-input" aria-label="Menu Variant" value={bindVariantId} onChange={(e) => setBindVariantId(e.target.value)}>
              {(catalog?.variants ?? []).length === 0 ? (
                <option value="">No variants in catalog</option>
              ) : (
                catalog!.variants.map((v) => <option key={v.id} value={v.id}>{v.name}</option>)
              )}
            </select>
            <select className="r3-input" aria-label="Variant Bound Recipe" value={bindRecipeId} onChange={(e) => setBindRecipeId(e.target.value)}>
              {(draft?.recipes ?? []).map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}
            </select>
            <button type="submit" className="r3-btn r3-btn--outline r3-btn--sm" disabled={bindVariantIntent.inFlight || !bindVariantId}>
              Bind Variant Recipe
            </button>
          </form>
        </div>

        <div className="r3-card">
          <div className="r3-card-header"><div className="r3-card-title">Bind Modifier Option Recipe (C2-DAT-10)</div></div>
          <DurableIntentBanner
            label="Bind Modifier Recipe"
            operationId={bindModifierIntent.pending?.payload.operationId ?? null}
            pending={bindModifierIntent.pending}
            corruptReason={bindModifierIntent.corruptReason}
            authorityMismatch={bindModifierIntent.authorityMismatch}
            inFlight={bindModifierIntent.inFlight}
            onRetry={() =>
              void bindModifierIntent
                .retryPending((p) => hostRequest(`/api/cafe/recipes/modifier-options/${encodeURIComponent(p.optionId)}`, { recipeId: p.recipeId }, "PUT", p.operationId))
                .then((r) => { if (r.ok) { setMsg("Modifier recipe binding retry confirmed."); void load(); } else setError(r.message); })
            }
            onClearRejected={() => bindModifierIntent.clearPending(false)}
            onClearCorrupt={() => bindModifierIntent.clearPending(true)}
          />
          <form onSubmit={(e) => void handleBindModifier(e)} style={{ display: "grid", gap: 10 }}>
            <select className="r3-input" aria-label="Modifier Option" value={bindModifierOptionId} onChange={(e) => setBindModifierOptionId(e.target.value)}>
              {(catalog?.modifierOptions ?? []).length === 0 ? (
                <option value="">No modifier options in catalog</option>
              ) : (
                catalog!.modifierOptions!.map((m) => <option key={m.id} value={m.id}>{m.name}</option>)
              )}
            </select>
            <select className="r3-input" aria-label="Modifier Bound Recipe" value={bindRecipeId} onChange={(e) => setBindRecipeId(e.target.value)}>
              {(draft?.recipes ?? []).map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}
            </select>
            <button type="submit" className="r3-btn r3-btn--outline r3-btn--sm" disabled={bindModifierIntent.inFlight || !bindModifierOptionId}>
              Bind Modifier Recipe
            </button>
          </form>
        </div>
      </div>
    </div>
  );
}

export function R3LiveCustomersArPanel({
  session,
  authority,
}: {
  session: HostSession;
  authority: DurableIntentAuthority | null;
}) {
  const api = useMemo(() => createLiveBackofficeApi(), []);
  const perms = session.user?.permissions ?? [];
  const has = (p: string) => perms.includes(p);

  const [customers, setCustomers] = useState<ArCustomerRow[]>([]);
  const [refunds, setRefunds] = useState<CafeRefundSummaryRow[]>([]);
  const [sales, setSales] = useState<RefundableSaleRow[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [msg, setMsg] = useState<string | null>(null);

  const [fullName, setFullName] = useState("");
  const [phone, setPhone] = useState("");
  const [limitPhp, setLimitPhp] = useState("5000.00");

  const [collectCustomerId, setCollectCustomerId] = useState("");
  const [collectPhp, setCollectPhp] = useState("");
  const [collectNote, setCollectNote] = useState("");

  // Full C5 Refund Request -> Approve/Reject -> Commit inside R3
  const [refundSaleId, setRefundSaleId] = useState("");
  const [refundSaleItemId, setRefundSaleItemId] = useState("");
  const [refundQty, setRefundQty] = useState("1");
  const [refundDisposition, setRefundDisposition] = useState<"ServiceNoStock" | "Restock" | "DamagedNonRestockable">("ServiceNoStock");
  const [refundReason, setRefundReason] = useState("");
  const [commitCashPhp, setCommitCashPhp] = useState<Record<string, string>>({});

  const collectIntent = useDurableIntent<
    { clientRequestId: string; customerId: string; amountCents: number; note?: string },
    void
  >("ar-cash-collection", authority);
  const requestRefundIntent = useDurableIntent<
    {
      operationId: string;
      saleId: string;
      reason: string;
      occurredAtUtc: string;
      lines: Array<{ saleItemId: string; quantityX1000: number; stockDisposition: "ServiceNoStock" | "Restock" | "DamagedNonRestockable" }>;
    },
    { refundId: string; version: number; state: string }
  >("c5-refund-request", authority);
  const resolveRefundIntent = useDurableIntent<
    { operationId: string; refundId: string; saleId: string; expectedVersion: number; approve: boolean; reason: string; occurredAtUtc: string },
    { refundId: string; version: number; state: string }
  >("c5-refund-resolve", authority);
  const commitRefundIntent = useDurableIntent<
    { operationId: string; refundId: string; saleId: string; expectedVersion: number; occurredAtUtc: string; settlements: Array<{ method: "Cash" | "AccountsReceivable"; amountCents: number }> },
    { refundId: string; version: number; state: string; totalRefundCents: number }
  >("c5-refund-commit", authority);

  const load = useCallback(async () => {
    setError(null);
    try {
      const [cust, ref, sal] = await Promise.all([
        has("Receivables.View") ? api.getReceivableCustomers().catch(() => []) : Promise.resolve([]),
        has("Sales.RefundRequest") || has("Sales.RefundApprove") ? api.getRefunds().catch(() => []) : Promise.resolve([]),
        has("Sales.RefundRequest") ? api.getRefundableSales().catch(() => []) : Promise.resolve([]),
      ]);
      setCustomers(cust);
      setRefunds(ref);
      setSales(sal);
      if (cust.length > 0) setCollectCustomerId((p) => p || cust[0].customer.id);
      if (sal.length > 0) {
        setRefundSaleId((prevSaleId) => {
          const activeSale = sal.find((s) => s.saleId === prevSaleId) ?? sal[0];
          const firstItem = activeSale.items.find((it) => it.remainingQuantityX1000 > 0) ?? activeSale.items[0];
          if (firstItem) setRefundSaleItemId((prevItem) => prevItem || firstItem.saleItemId);
          return activeSale.saleId;
        });
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to load Customers & AR.");
    }
  }, [api, perms]);

  useEffect(() => {
    void load();
  }, [load]);

  const handleCreateCustomer = async (e: FormEvent) => {
    e.preventDefault();
    setMsg(null);
    setError(null);
    if (!fullName.trim()) {
      setError("Customer full name is required.");
      return;
    }
    try {
      const creditLimitCents = limitPhp.trim() ? parsePhpToCents(limitPhp) : null;
      await api.createArCustomer({ fullName: fullName.trim(), phone: phone.trim() || null, creditLimitCents });
      setFullName("");
      setPhone("");
      setMsg("AR customer account created.");
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to create customer.");
    }
  };

  const handleCollect = async (e: FormEvent) => {
    e.preventDefault();
    setMsg(null);
    setError(null);
    let amountCents: number;
    try {
      amountCents = parsePhpToCents(collectPhp);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Invalid amount.");
      return;
    }
    if (!collectCustomerId || amountCents <= 0) {
      setError("Select customer and enter positive Cash collection amount.");
      return;
    }
    const res = await collectIntent.execute(
      { customerId: collectCustomerId, amountCents, note: collectNote.trim() || undefined },
      () => ({
        clientRequestId: crypto.randomUUID(),
        customerId: collectCustomerId,
        amountCents,
        note: collectNote.trim() || undefined,
      }),
      (p) => api.collectArCash(p),
    );
    if (res.ok) {
      setCollectPhp("");
      setCollectNote("");
      setMsg(`Cash AR collection recorded (${formatPhp(amountCents)}).`);
      await load();
    } else setError(res.message);
  };

  const selectedRefundSale = sales.find((s) => s.saleId === refundSaleId) ?? null;

  const handleRequestRefund = async (e: FormEvent) => {
    e.preventDefault();
    setMsg(null);
    setError(null);
    const qtyNum = Number(refundQty);
    const qtyX1000 = Math.round(qtyNum * 1000);
    if (!refundSaleId || !refundSaleItemId || !Number.isSafeInteger(qtyX1000) || qtyX1000 <= 0 || !refundReason.trim()) {
      setError("Select a sale item, positive refund quantity, and refund reason.");
      return;
    }
    const res = await requestRefundIntent.execute(
      { saleId: refundSaleId, saleItemId: refundSaleItemId, qtyX1000, refundDisposition, reason: refundReason.trim() },
      () => ({
        operationId: crypto.randomUUID(),
        saleId: refundSaleId,
        reason: refundReason.trim(),
        occurredAtUtc: new Date().toISOString(),
        lines: [{ saleItemId: refundSaleItemId, quantityX1000: qtyX1000, stockDisposition: refundDisposition }],
      }),
      (p) => api.requestRefund(p),
    );
    if (res.ok) {
      setRefundReason("");
      setMsg(`Refund requested (${res.value.refundId.slice(0, 8)}…, ${res.value.state}).`);
      await load();
    } else setError(res.message);
  };

  const handleResolveRefund = async (row: CafeRefundSummaryRow, approve: boolean) => {
    setMsg(null);
    setError(null);
    const reason = approve ? "Approved in unified R3 console" : "Rejected in unified R3 console";
    const res = await resolveRefundIntent.execute(
      { refundId: row.refundId, expectedVersion: row.version, approve },
      () => ({
        operationId: crypto.randomUUID(),
        refundId: row.refundId,
        saleId: row.saleId,
        expectedVersion: row.version,
        approve,
        reason,
        occurredAtUtc: new Date().toISOString(),
      }),
      (p) => api.resolveRefund(p),
    );
    if (res.ok) {
      setMsg(`Refund ${res.value.refundId.slice(0, 8)}… ${res.value.state}.`);
      await load();
    } else setError(res.message);
  };

  const getDefaultRefundCashCents = (row: CafeRefundSummaryRow): number => {
    const total = row.totalRefundCents ?? 0;
    const sale = sales.find((s) => s.saleId === row.saleId);
    if (!sale) return total;
    return Math.min(total, Math.max(0, sale.cashNetCents));
  };

  const handleCommitRefund = async (row: CafeRefundSummaryRow) => {
    setMsg(null);
    setError(null);
    const totalCents = row.totalRefundCents ?? 0;
    const defaultCashCents = getDefaultRefundCashCents(row);
    const rawPhp = (commitCashPhp[row.refundId] ?? (defaultCashCents / 100).toFixed(2)).trim();
    let cashCents: number;
    try {
      cashCents = parsePhpToCents(rawPhp);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Invalid refund cash settlement amount.");
      return;
    }
    if (cashCents < 0 || cashCents > totalCents) {
      setError("Cash refund settlement must be between PHP 0.00 and the approved refund total.");
      return;
    }
    const arCents = Math.max(0, totalCents - cashCents);
    const settlements: Array<{ method: "Cash" | "AccountsReceivable"; amountCents: number }> = [];
    if (cashCents > 0) settlements.push({ method: "Cash", amountCents: cashCents });
    if (arCents > 0) settlements.push({ method: "AccountsReceivable", amountCents: arCents });
    if (settlements.length === 0) {
      setError("Enter a positive refund settlement amount.");
      return;
    }
    const res = await commitRefundIntent.execute(
      { refundId: row.refundId, expectedVersion: row.version, settlements },
      () => ({
        operationId: crypto.randomUUID(),
        refundId: row.refundId,
        saleId: row.saleId,
        expectedVersion: row.version,
        occurredAtUtc: new Date().toISOString(),
        settlements,
      }),
      (p) => api.commitRefund(p),
    );
    if (res.ok) {
      setMsg(`Refund ${res.value.refundId.slice(0, 8)}… committed (${formatPhp(res.value.totalRefundCents)}).`);
      await load();
    } else setError(res.message);
  };

  return (
    <div style={{ display: "grid", gap: 16 }}>
      <div className="r3-page-header">
        <div>
          <h1 className="r3-page-title">Customers, Accounts Receivable (Utang) &amp; Refunds</h1>
          <p className="r3-page-subtitle">C5 live Host customer credit balances, Cash-only AR collections, and refund ledger.</p>
        </div>
        <button type="button" className="r3-btn r3-btn--outline r3-btn--sm" onClick={() => void load()}>
          Refresh AR &amp; Refunds
        </button>
      </div>
      {error && <div className="r3-alert r3-alert--danger" role="alert"><div className="r3-alert-body">{error}</div></div>}
      {msg && <div className="r3-alert r3-alert--success" role="status"><div className="r3-alert-body">{msg}</div></div>}

      <div className="r3-card">
        <div className="r3-card-header"><div className="r3-card-title">Customer AR Ledger ({customers.length})</div></div>
        <div className="r3-table-wrap" tabIndex={0} role="region" aria-label="Customer AR Balances">
          <table className="r3-table">
            <caption className="r3-sr-only">Customer AR Balances</caption>
            <thead>
              <tr>
                <th scope="col">Customer</th>
                <th scope="col">Phone</th>
                <th scope="col">Credit Limit</th>
                <th scope="col">Outstanding Balance</th>
              </tr>
            </thead>
            <tbody>
              {customers.length === 0 ? (
                <tr><td colSpan={4} className="r3-text-muted">No AR customers found.</td></tr>
              ) : (
                customers.map((row) => (
                  <tr key={row.customer.id}>
                    <th scope="row">{row.customer.fullName}</th>
                    <td>{row.customer.phone ?? "—"}</td>
                    <td className="r3-tabular">{row.customer.creditLimitCents !== null ? formatPhp(row.customer.creditLimitCents) : "Unlimited"}</td>
                    <td className="r3-tabular"><strong>{formatPhp(row.balanceCents)}</strong></td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>

      <div className="r3-two-col">
        <div className="r3-card">
          <div className="r3-card-header"><div className="r3-card-title">Collect AR Payment (Cash Only)</div></div>
          <DurableIntentBanner
            label="AR Cash Collection"
            operationId={collectIntent.pending?.payload.clientRequestId ?? null}
            pending={collectIntent.pending}
            corruptReason={collectIntent.corruptReason}
            authorityMismatch={collectIntent.authorityMismatch}
            inFlight={collectIntent.inFlight}
            onRetry={() => void collectIntent.retryPending((p) => api.collectArCash(p)).then((r) => { if (r.ok) void load(); else setError(r.message); })}
            onClearRejected={() => collectIntent.clearPending(false)}
            onClearCorrupt={() => collectIntent.clearPending(true)}
          />
          <form onSubmit={(e) => void handleCollect(e)} style={{ display: "grid", gap: 10 }}>
            <select className="r3-input" aria-label="AR Customer" value={collectCustomerId} onChange={(e) => setCollectCustomerId(e.target.value)}>
              {customers.map((c) => <option key={c.customer.id} value={c.customer.id}>{c.customer.fullName} ({formatPhp(c.balanceCents)})</option>)}
            </select>
            <input className="r3-input" aria-label="Cash Collection Amount" value={collectPhp} onChange={(e) => setCollectPhp(e.target.value)} placeholder="200.00" />
            <input className="r3-input" aria-label="Collection Note" value={collectNote} onChange={(e) => setCollectNote(e.target.value)} placeholder="Cash payment at counter" />
            <button type="submit" className="r3-btn r3-btn--primary r3-btn--sm" disabled={collectIntent.inFlight}>Record Cash Collection</button>
          </form>
        </div>

        <div className="r3-card">
          <div className="r3-card-header"><div className="r3-card-title">Register AR Customer</div></div>
          <form onSubmit={(e) => void handleCreateCustomer(e)} style={{ display: "grid", gap: 10 }}>
            <input className="r3-input" aria-label="Customer Full Name" value={fullName} onChange={(e) => setFullName(e.target.value)} placeholder="Juan Dela Cruz" />
            <input className="r3-input" aria-label="Customer Phone" value={phone} onChange={(e) => setPhone(e.target.value)} placeholder="0917-000-0000" />
            <input className="r3-input" aria-label="Credit Limit PHP" value={limitPhp} onChange={(e) => setLimitPhp(e.target.value)} placeholder="5000.00" />
            <button type="submit" className="r3-btn r3-btn--outline r3-btn--sm">Create Customer</button>
          </form>
        </div>
      </div>

      <div className="r3-card">
        <div className="r3-card-header">
          <div>
            <div className="r3-card-title">C5 Refund Workflow — Request, Approve/Reject &amp; Commit ({refunds.length})</div>
            <div className="r3-card-subtitle">Eligible completed sales: {sales.length}. Authority-bound Request → Approve/Reject → Cash Commit directly inside R3.</div>
          </div>
        </div>
        <DurableIntentBanner
          label="Request Refund"
          operationId={requestRefundIntent.pending?.payload.operationId ?? null}
          pending={requestRefundIntent.pending}
          corruptReason={requestRefundIntent.corruptReason}
          authorityMismatch={requestRefundIntent.authorityMismatch}
          inFlight={requestRefundIntent.inFlight}
          onRetry={() => void requestRefundIntent.retryPending((p) => api.requestRefund(p)).then((r) => { if (r.ok) { setMsg("Refund request retry confirmed."); void load(); } else setError(r.message); })}
          onClearRejected={() => requestRefundIntent.clearPending(false)}
          onClearCorrupt={() => requestRefundIntent.clearPending(true)}
        />
        <DurableIntentBanner
          label="Resolve Refund"
          operationId={resolveRefundIntent.pending?.payload.operationId ?? null}
          pending={resolveRefundIntent.pending}
          corruptReason={resolveRefundIntent.corruptReason}
          authorityMismatch={resolveRefundIntent.authorityMismatch}
          inFlight={resolveRefundIntent.inFlight}
          onRetry={() => void resolveRefundIntent.retryPending((p) => api.resolveRefund(p)).then((r) => { if (r.ok) { setMsg("Refund resolution retry confirmed."); void load(); } else setError(r.message); })}
          onClearRejected={() => resolveRefundIntent.clearPending(false)}
          onClearCorrupt={() => resolveRefundIntent.clearPending(true)}
        />
        <DurableIntentBanner
          label="Commit Refund"
          operationId={commitRefundIntent.pending?.payload.operationId ?? null}
          pending={commitRefundIntent.pending}
          corruptReason={commitRefundIntent.corruptReason}
          authorityMismatch={commitRefundIntent.authorityMismatch}
          inFlight={commitRefundIntent.inFlight}
          onRetry={() => void commitRefundIntent.retryPending((p) => api.commitRefund(p)).then((r) => { if (r.ok) { setMsg("Refund commit retry confirmed."); void load(); } else setError(r.message); })}
          onClearRejected={() => commitRefundIntent.clearPending(false)}
          onClearCorrupt={() => commitRefundIntent.clearPending(true)}
        />
        {has("Sales.RefundRequest") && sales.length > 0 && (
          <form onSubmit={(e) => void handleRequestRefund(e)} style={{ display: "grid", gap: 10, maxWidth: 620, marginBottom: 16 }}>
            <select
              className="r3-input"
              aria-label="Refundable Sale"
              value={refundSaleId}
              onChange={(e) => {
                const nextId = e.target.value;
                setRefundSaleId(nextId);
                const nextSale = sales.find((s) => s.saleId === nextId);
                const firstItem = nextSale?.items.find((it) => it.remainingQuantityX1000 > 0) ?? nextSale?.items[0];
                setRefundSaleItemId(firstItem?.saleItemId ?? "");
              }}
            >
              {sales.map((s) => (
                <option key={s.saleId} value={s.saleId}>
                  Receipt {s.receiptNumber} — {formatPhp(s.totalCents)} (Cash {formatPhp(s.cashNetCents)} / AR {formatPhp(s.arCents)})
                </option>
              ))}
            </select>
            <select
              className="r3-input"
              aria-label="Refund Sale Line"
              value={refundSaleItemId}
              onChange={(e) => setRefundSaleItemId(e.target.value)}
            >
              {(selectedRefundSale?.items ?? []).map((it) => (
                <option key={it.saleItemId} value={it.saleItemId}>
                  {it.productName} (remaining {it.remainingQuantityX1000 / 1000} × {formatPhp(it.unitPriceCents)})
                </option>
              ))}
            </select>
            <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
              <input className="r3-input" aria-label="Refund Quantity" value={refundQty} onChange={(e) => setRefundQty(e.target.value)} placeholder="1" style={{ maxWidth: 110 }} />
              <select
                className="r3-input"
                aria-label="Refund Stock Disposition"
                value={refundDisposition}
                onChange={(e) => setRefundDisposition(e.target.value as "ServiceNoStock" | "Restock" | "DamagedNonRestockable")}
                style={{ flex: 1 }}
              >
                <option value="ServiceNoStock">ServiceNoStock (Prepared beverage/food)</option>
                <option value="Restock">Restock (Unopened product)</option>
                <option value="DamagedNonRestockable">DamagedNonRestockable</option>
              </select>
            </div>
            <input className="r3-input" aria-label="Refund Request Reason" value={refundReason} onChange={(e) => setRefundReason(e.target.value)} placeholder="Wrong preparation / customer refund request" />
            <div>
              <button type="submit" className="r3-btn r3-btn--primary r3-btn--sm" disabled={requestRefundIntent.inFlight}>
                Request Refund
              </button>
            </div>
          </form>
        )}
        <div className="r3-table-wrap" tabIndex={0} role="region" aria-label="Refund Records">
          <table className="r3-table">
            <caption className="r3-sr-only">Refund Records</caption>
            <thead>
              <tr>
                <th scope="col">Refund</th>
                <th scope="col">Receipt</th>
                <th scope="col">State</th>
                <th scope="col">Amount</th>
                <th scope="col">Reason</th>
                <th scope="col">Actions</th>
              </tr>
            </thead>
            <tbody>
              {refunds.length === 0 ? (
                <tr><td colSpan={6} className="r3-text-muted">No refunds recorded.</td></tr>
              ) : (
                refunds.map((r) => (
                  <tr key={r.refundId}>
                    <th scope="row"><code>{r.refundId.slice(0, 8)}…</code> (v{r.version})</th>
                    <td>{r.receiptNumber ?? "—"}</td>
                    <td><span className="r3-badge r3-badge--info">{r.state}</span></td>
                    <td className="r3-tabular">{r.totalRefundCents !== null ? formatPhp(r.totalRefundCents) : "—"}</td>
                    <td>{r.reason}</td>
                    <td style={{ display: "flex", gap: 6, flexWrap: "wrap", alignItems: "center" }}>
                      {r.state === "Requested" && has("Sales.RefundApprove") && (
                        <>
                          <button type="button" className="r3-btn r3-btn--primary r3-btn--sm" disabled={resolveRefundIntent.inFlight} onClick={() => void handleResolveRefund(r, true)}>
                            Approve Refund
                          </button>
                          <button type="button" className="r3-btn r3-btn--outline r3-btn--sm" disabled={resolveRefundIntent.inFlight} onClick={() => void handleResolveRefund(r, false)}>
                            Reject
                          </button>
                        </>
                      )}
                      {r.state === "Approved" && has("Sales.RefundApprove") && (
                        <>
                          <input
                            className="r3-input"
                            aria-label={`Cash Refund Settlement for ${r.refundId.slice(0, 8)}`}
                            value={commitCashPhp[r.refundId] ?? (getDefaultRefundCashCents(r) / 100).toFixed(2)}
                            onChange={(e) => setCommitCashPhp((prev) => ({ ...prev, [r.refundId]: e.target.value }))}
                            style={{ maxWidth: 110 }}
                          />
                          <button type="button" className="r3-btn r3-btn--primary r3-btn--sm" disabled={commitRefundIntent.inFlight} onClick={() => void handleCommitRefund(r)}>
                            Commit Cash Refund
                          </button>
                        </>
                      )}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

export function R3LiveStaffIncidentsPanel({
  session,
  authority,
}: {
  session: HostSession;
  authority: DurableIntentAuthority | null;
}) {
  const api = useMemo(() => createLiveBackofficeApi(), []);
  const perms = session.user?.permissions ?? [];
  const has = (p: string) => perms.includes(p);

  const [attendance, setAttendance] = useState<AttendanceRow[]>([]);
  const [incidents, setIncidents] = useState<IncidentRow[]>([]);
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [classification, setClassification] = useState<IncidentClassification>("Operational");
  const [severity, setSeverity] = useState<"Low" | "Medium" | "High" | "Critical">("Medium");
  const [msg, setMsg] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const attIntent = useDurableIntent<
    { operationId: string; attendanceEventId: string; eventType: "ClockIn" | "ClockOut"; occurredAtUtc: string },
    void
  >("staff-attendance", authority);
  const incIntent = useDurableIntent<
    {
      operationId: string;
      incidentId: string;
      revisionId: string;
      incidentOccurredAtUtc: string;
      occurredAtUtc: string;
      title: string;
      description: string;
      classification: IncidentClassification;
      severity: "Low" | "Medium" | "High" | "Critical";
    },
    void
  >("staff-incident-create", authority);
  const attCorrectIntent = useDurableIntent<
    {
      attendanceEventId: string;
      attendanceCorrectionId: string;
      operationId: string;
      correctionKind: string;
      reason: string;
      occurredAtUtc: string;
    },
    unknown
  >("staff-attendance-correct", authority);
  const incResolveIntent = useDurableIntent<
    {
      incidentId: string;
      resolutionId: string;
      operationId: string;
      expectedVersion: number;
      resolutionNote: string;
      occurredAtUtc: string;
    },
    unknown
  >("staff-incident-resolve", authority);

  const [correctionReason, setCorrectionReason] = useState("Duplicate or erroneous clock entry");
  const [resolutionNote, setResolutionNote] = useState("Investigated and resolved by manager");

  const load = useCallback(async () => {
    setError(null);
    try {
      const [att, inc] = await Promise.all([
        has("Attendance.Record") ? api.getAttendance().catch(() => []) : Promise.resolve([]),
        has("Incident.View") ? api.getIncidents().catch(() => []) : Promise.resolve([]),
      ]);
      setAttendance(att);
      setIncidents(inc);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to load staff records.");
    }
  }, [api, perms]);

  useEffect(() => {
    void load();
  }, [load]);

  const recordClock = async (eventType: "ClockIn" | "ClockOut") => {
    setMsg(null);
    setError(null);
    const res = await attIntent.execute(
      { eventType },
      () => ({
        operationId: crypto.randomUUID(),
        attendanceEventId: crypto.randomUUID(),
        eventType,
        occurredAtUtc: new Date().toISOString(),
      }),
      (p) => api.recordAttendance(p),
    );
    if (res.ok) {
      setMsg(`Recorded ${eventType} event.`);
      await load();
    } else setError(res.message);
  };

  const handleIncident = async (e: FormEvent) => {
    e.preventDefault();
    setMsg(null);
    setError(null);
    if (!title.trim() || !description.trim()) {
      setError("Incident title and description are required.");
      return;
    }
    const res = await incIntent.execute(
      { title: title.trim(), description: description.trim(), classification, severity },
      () => {
        const nowIso = new Date().toISOString();
        return {
          operationId: crypto.randomUUID(),
          incidentId: crypto.randomUUID(),
          revisionId: crypto.randomUUID(),
          incidentOccurredAtUtc: nowIso,
          occurredAtUtc: nowIso,
          title: title.trim(),
          description: description.trim(),
          classification,
          severity,
        };
      },
      (p) => api.createIncident(p),
    );
    if (res.ok) {
      setTitle("");
      setDescription("");
      setMsg("Operational incident recorded in live Host.");
      await load();
    } else setError(res.message);
  };

  const handleCorrectAttendance = async (row: AttendanceRow) => {
    setMsg(null);
    setError(null);
    const reason = correctionReason.trim() || "Manager attendance correction";
    const res = await attCorrectIntent.execute(
      { attendanceEventId: row.attendanceEventId, correctionKind: "MarkInvalid", reason },
      () => ({
        attendanceEventId: row.attendanceEventId,
        attendanceCorrectionId: crypto.randomUUID(),
        operationId: crypto.randomUUID(),
        correctionKind: "MarkInvalid",
        reason,
        occurredAtUtc: new Date().toISOString(),
      }),
      (p) =>
        hostRequest(
          `/api/cafe/attendance/${p.attendanceEventId}/corrections`,
          {
            operationId: p.operationId,
            attendanceCorrectionId: p.attendanceCorrectionId,
            correctionKind: p.correctionKind,
            reason: p.reason,
            occurredAtUtc: p.occurredAtUtc,
          },
          "POST",
          p.operationId,
        ),
    );
    if (res.ok) {
      setMsg(`Attendance correction recorded for ${row.attendanceEventId.slice(0, 8)}….`);
      await load();
    } else setError(res.message);
  };

  const handleResolveIncident = async (inc: IncidentRow) => {
    setMsg(null);
    setError(null);
    const note = resolutionNote.trim() || "Resolved by manager";
    const res = await incResolveIntent.execute(
      { incidentId: inc.incidentId, expectedVersion: inc.version, resolutionNote: note },
      () => ({
        incidentId: inc.incidentId,
        resolutionId: crypto.randomUUID(),
        operationId: crypto.randomUUID(),
        expectedVersion: inc.version,
        resolutionNote: note,
        occurredAtUtc: new Date().toISOString(),
      }),
      (p) =>
        hostRequest(
          `/api/cafe/incidents/${p.incidentId}/resolve`,
          {
            operationId: p.operationId,
            resolutionId: p.resolutionId,
            expectedVersion: p.expectedVersion,
            resolutionNote: p.resolutionNote,
            occurredAtUtc: p.occurredAtUtc,
          },
          "POST",
          p.operationId,
        ),
    );
    if (res.ok) {
      setMsg(`Incident "${inc.title}" resolved.`);
      await load();
    } else setError(res.message);
  };

  return (
    <div style={{ display: "grid", gap: 16 }}>
      <div className="r3-page-header">
        <div>
          <h1 className="r3-page-title">Staff Attendance &amp; Operational Incidents</h1>
          <p className="r3-page-subtitle">C10 live clock-in/clock-out log and incident reporting with durable idempotency.</p>
        </div>
        {has("Attendance.Record") && (
          <div style={{ display: "flex", gap: 8 }}>
            <button type="button" className="r3-btn r3-btn--primary r3-btn--sm" onClick={() => void recordClock("ClockIn")} disabled={attIntent.inFlight}>
              Clock In
            </button>
            <button type="button" className="r3-btn r3-btn--outline r3-btn--sm" onClick={() => void recordClock("ClockOut")} disabled={attIntent.inFlight}>
              Clock Out
            </button>
          </div>
        )}
      </div>

      {error && <div className="r3-alert r3-alert--danger" role="alert"><div className="r3-alert-body">{error}</div></div>}
      {msg && <div className="r3-alert r3-alert--success" role="status"><div className="r3-alert-body">{msg}</div></div>}

      <DurableIntentBanner
        label="Staff Attendance"
        operationId={attIntent.pending?.payload.operationId ?? null}
        pending={attIntent.pending}
        corruptReason={attIntent.corruptReason}
        authorityMismatch={attIntent.authorityMismatch}
        inFlight={attIntent.inFlight}
        onRetry={() =>
          void attIntent.retryPending((p) => api.recordAttendance(p)).then((r) => {
            if (r.ok) { setMsg("Attendance retry confirmed."); void load(); } else setError(r.message);
          })
        }
        onClearRejected={() => attIntent.clearPending(false)}
        onClearCorrupt={() => attIntent.clearPending(true)}
      />

      <div className="r3-two-col">
        <div className="r3-card">
          <div className="r3-card-header"><div className="r3-card-title">Recent Attendance ({attendance.length})</div></div>
          <DurableIntentBanner
            label="Attendance Correction"
            operationId={attCorrectIntent.pending?.payload.operationId ?? null}
            pending={attCorrectIntent.pending}
            corruptReason={attCorrectIntent.corruptReason}
            authorityMismatch={attCorrectIntent.authorityMismatch}
            inFlight={attCorrectIntent.inFlight}
            onRetry={() =>
              void attCorrectIntent
                .retryPending((p) =>
                  hostRequest(
                    `/api/cafe/attendance/${p.attendanceEventId}/corrections`,
                    {
                      operationId: p.operationId,
                      attendanceCorrectionId: p.attendanceCorrectionId,
                      correctionKind: p.correctionKind,
                      reason: p.reason,
                      occurredAtUtc: p.occurredAtUtc,
                    },
                    "POST",
                    p.operationId,
                  ),
                )
                .then((r) => { if (r.ok) { setMsg("Attendance correction retry confirmed."); void load(); } else setError(r.message); })
            }
            onClearRejected={() => attCorrectIntent.clearPending(false)}
            onClearCorrupt={() => attCorrectIntent.clearPending(true)}
          />
          {has("Attendance.Manage") && (
            <div className="r3-card-body" style={{ paddingBottom: 0 }}>
              <input
                className="r3-input"
                aria-label="Attendance Correction Reason"
                value={correctionReason}
                onChange={(e) => setCorrectionReason(e.target.value)}
                placeholder="Reason for append-only attendance correction"
              />
            </div>
          )}
          <div className="r3-table-wrap" tabIndex={0} role="region" aria-label="Attendance Log">
            <table className="r3-table">
              <caption className="r3-sr-only">Attendance Log</caption>
              <thead>
                <tr>
                  <th scope="col">Event</th>
                  <th scope="col">Occurred</th>
                  {has("Attendance.Manage") && <th scope="col">Correction</th>}
                </tr>
              </thead>
              <tbody>
                {attendance.length === 0 ? (
                  <tr><td colSpan={has("Attendance.Manage") ? 3 : 2} className="r3-text-muted">No attendance records yet.</td></tr>
                ) : (
                  attendance.slice(0, 15).map((a) => (
                    <tr key={a.attendanceEventId}>
                      <th scope="row">{a.eventType}</th>
                      <td>{new Date(a.occurredAtUtc).toLocaleString()}</td>
                      {has("Attendance.Manage") && (
                        <td>
                          <button
                            type="button"
                            className="r3-btn r3-btn--outline r3-btn--sm"
                            disabled={attCorrectIntent.inFlight}
                            onClick={() => void handleCorrectAttendance(a)}
                          >
                            Mark Invalid
                          </button>
                        </td>
                      )}
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </div>

        {has("Incident.Record") && (
          <div className="r3-card">
            <div className="r3-card-header"><div className="r3-card-title">Log Operational Incident</div></div>
            <DurableIntentBanner
              label="Incident Report"
              operationId={incIntent.pending?.payload.operationId ?? null}
              pending={incIntent.pending}
              corruptReason={incIntent.corruptReason}
              authorityMismatch={incIntent.authorityMismatch}
              inFlight={incIntent.inFlight}
              onRetry={() => void incIntent.retryPending((p) => api.createIncident(p)).then((r) => { if (r.ok) void load(); else setError(r.message); })}
              onClearRejected={() => incIntent.clearPending(false)}
              onClearCorrupt={() => incIntent.clearPending(true)}
            />
            <form onSubmit={(e) => void handleIncident(e)} style={{ display: "grid", gap: 10 }}>
              <input className="r3-input" aria-label="Incident Title" value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Espresso grinder jam" />
              <textarea className="r3-input" aria-label="Incident Description" value={description} onChange={(e) => setDescription(e.target.value)} placeholder="Details and corrective action" rows={3} />
              <div style={{ display: "flex", gap: 8 }}>
                <select className="r3-input" aria-label="Incident Classification" value={classification} onChange={(e) => setClassification(e.target.value as IncidentClassification)}>
                  <option value="Operational">Operational</option>
                  <option value="Safety">Safety</option>
                  <option value="Equipment">Equipment</option>
                  <option value="Conduct">Conduct</option>
                  <option value="Other">Other</option>
                </select>
                <select className="r3-input" aria-label="Incident Severity" value={severity} onChange={(e) => setSeverity(e.target.value as "Low" | "Medium" | "High" | "Critical")}>
                  <option value="Low">Low</option>
                  <option value="Medium">Medium</option>
                  <option value="High">High</option>
                  <option value="Critical">Critical</option>
                </select>
              </div>
              <button type="submit" className="r3-btn r3-btn--primary r3-btn--sm" disabled={incIntent.inFlight}>Record Incident</button>
            </form>
          </div>
        )}
      </div>

      {has("Incident.View") && (
        <div className="r3-card">
          <div className="r3-card-header"><div className="r3-card-title">Logged Incidents ({incidents.length})</div></div>
          <DurableIntentBanner
            label="Resolve Incident"
            operationId={incResolveIntent.pending?.payload.operationId ?? null}
            pending={incResolveIntent.pending}
            corruptReason={incResolveIntent.corruptReason}
            authorityMismatch={incResolveIntent.authorityMismatch}
            inFlight={incResolveIntent.inFlight}
            onRetry={() =>
              void incResolveIntent
                .retryPending((p) =>
                  hostRequest(
                    `/api/cafe/incidents/${p.incidentId}/resolve`,
                    {
                      operationId: p.operationId,
                      resolutionId: p.resolutionId,
                      expectedVersion: p.expectedVersion,
                      resolutionNote: p.resolutionNote,
                      occurredAtUtc: p.occurredAtUtc,
                    },
                    "POST",
                    p.operationId,
                  ),
                )
                .then((r) => { if (r.ok) { setMsg("Incident resolve retry confirmed."); void load(); } else setError(r.message); })
            }
            onClearRejected={() => incResolveIntent.clearPending(false)}
            onClearCorrupt={() => incResolveIntent.clearPending(true)}
          />
          {has("Incident.Manage") && (
            <div className="r3-card-body" style={{ paddingBottom: 0, maxWidth: 520 }}>
              <input
                className="r3-input"
                aria-label="Incident Resolution Note"
                value={resolutionNote}
                onChange={(e) => setResolutionNote(e.target.value)}
                placeholder="Manager resolution note"
              />
            </div>
          )}
          <div className="r3-table-wrap" tabIndex={0} role="region" aria-label="Operational Incidents">
            <table className="r3-table">
              <caption className="r3-sr-only">Operational Incidents</caption>
              <thead>
                <tr>
                  <th scope="col">Title</th>
                  <th scope="col">Classification</th>
                  <th scope="col">Severity</th>
                  <th scope="col">Status</th>
                  {has("Incident.Manage") && <th scope="col">Actions</th>}
                </tr>
              </thead>
              <tbody>
                {incidents.length === 0 ? (
                  <tr><td colSpan={has("Incident.Manage") ? 5 : 4} className="r3-text-muted">No incidents logged.</td></tr>
                ) : (
                  incidents.map((inc) => (
                    <tr key={inc.incidentId}>
                      <th scope="row">{inc.title}</th>
                      <td>{inc.classification}</td>
                      <td>{inc.severity}</td>
                      <td><span className="r3-badge r3-badge--warn">{inc.status}</span></td>
                      {has("Incident.Manage") && (
                        <td>
                          {inc.status !== "Resolved" && (
                            <button
                              type="button"
                              className="r3-btn r3-btn--primary r3-btn--sm"
                              disabled={incResolveIntent.inFlight}
                              onClick={() => void handleResolveIncident(inc)}
                            >
                              Resolve Incident
                            </button>
                          )}
                        </td>
                      )}
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}
