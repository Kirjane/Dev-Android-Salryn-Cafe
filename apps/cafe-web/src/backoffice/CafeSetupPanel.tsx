import { useCallback, useEffect, useState, type FormEvent } from "react";
import {
  Alert,
  Button,
  Card,
  CardBody,
  CardHeader,
  FormField,
  SelectInput,
  StatusBadge,
  TextInput,
} from "@salryn/cafe-ui";
import { HostApiError, formatPhp, parsePhpToCents } from "../api/hostClient";
import type { BackofficeApi, IngredientRecipeDraft, MenuCatalogDraft, MenuUnitOption } from "./types";
import { BackofficeUnknownOutcomeError, useBackofficeOperation } from "./useBackofficeOperation";

type SetupOp = ReturnType<typeof useBackofficeOperation>;

type SetupProps = {
  api: BackofficeApi;
  onSaved: () => Promise<void>;
};

function quantityX1000(raw: string, allowZero = false): number {
  if (!/^\d+(\.\d{1,3})?$/.test(raw.trim())) {
    throw new Error("Enter a quantity with up to three decimal places.");
  }
  const result = Math.round(Number(raw) * 1000);
  if (!Number.isSafeInteger(result) || (allowZero ? result < 0 : result <= 0)) {
    throw new Error("Enter a valid " + (allowZero ? "non-negative" : "positive") + " quantity.");
  }
  return result;
}

function throwIfUncertain(operationId: string, err: unknown): never {
  if (
    (err instanceof HostApiError && (
      err.status === 0 ||
      err.status >= 500 ||
      err.code === "ERR_IDEMPOTENCY_OUTCOME_UNKNOWN" ||
      err.code === "ERR_IDEMPOTENCY_IN_FLIGHT" ||
      err.code === "ERR_NETWORK_UNKNOWN"
    )) ||
    err instanceof TypeError ||
    (err instanceof Error && err.name === "AbortError")
  ) {
    throw new BackofficeUnknownOutcomeError(
      operationId,
      "The server may have saved this change. Do not change the fields. Check the draft, then retry the same request if necessary.",
    );
  }
  throw err;
}

function OperationFeedback({ op, title }: { op: SetupOp; title: string }) {
  return (
    <>
      {op.error ? <Alert tone="danger" title={title + " could not be saved"} description={op.error} /> : null}
      {op.unknownOutcome ? (
        <Alert
          tone="warning"
          title={title + " result uncertain"}
          description={"Do not create a second record until the current draft is checked. Retry with unchanged values and the saved operation. Operation: " + op.unknownOutcome.operationId}
        />
      ) : null}
    </>
  );
}

async function submitIdempotent(
  op: SetupOp,
  key: string,
  runner: (operationId: string) => Promise<void>,
  refresh: () => Promise<void>,
): Promise<boolean> {
  const completed = await op.execute(async (operationId) => {
    try {
      await runner(operationId);
      return true;
    } catch (err) {
      throwIfUncertain(operationId, err);
    }
  }, key);
  if (completed) {
    await refresh();
    return true;
  }
  return false;
}

const formGrid = {
  display: "grid",
  gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))",
  gap: 12,
  alignItems: "end",
} as const;

export function CafeCatalogSetup({ api, onSaved }: SetupProps) {
  const [catalog, setCatalog] = useState<MenuCatalogDraft | null>(null);
  const [units, setUnits] = useState<MenuUnitOption[]>([]);
  const [recipes, setRecipes] = useState<IngredientRecipeDraft | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [categoryName, setCategoryName] = useState("");
  const [itemName, setItemName] = useState("");
  const [categoryId, setCategoryId] = useState("");
  const [unitId, setUnitId] = useState("");
  const [itemPrice, setItemPrice] = useState("100.00");
  const [variantProductId, setVariantProductId] = useState("");
  const [variantName, setVariantName] = useState("Regular");
  const [variantPrice, setVariantPrice] = useState("100.00");
  const [ingredientName, setIngredientName] = useState("");
  const [stockUnit, setStockUnit] = useState<"Gram" | "Milliliter" | "Piece">("Gram");
  const [reorderQty, setReorderQty] = useState("0");
  const [recipeName, setRecipeName] = useState("");
  const [recipeYield, setRecipeYield] = useState("1");
  const [lineRecipeId, setLineRecipeId] = useState("");
  const [lineIngredientId, setLineIngredientId] = useState("");
  const [lineQty, setLineQty] = useState("1");

  const categoryOp = useBackofficeOperation("setup-menu-category");
  const itemOp = useBackofficeOperation("setup-menu-item");
  const variantOp = useBackofficeOperation("setup-menu-variant");
  const ingredientOp = useBackofficeOperation("setup-ingredient");
  const recipeOp = useBackofficeOperation("setup-recipe");
  const lineOp = useBackofficeOperation("setup-recipe-line");
  const bindingOp = useBackofficeOperation("setup-recipe-item-binding");
  const [bindingItemId, setBindingItemId] = useState("");
  const [bindingRecipeId, setBindingRecipeId] = useState("");

  const reload = useCallback(async () => {
    setLoading(true);
    const result = await Promise.allSettled([
      api.getMenuCatalog(),
      api.getMenuUnits(),
      api.getIngredientsAndRecipes(),
    ]);
    const errors: string[] = [];
    if (result[0].status === "fulfilled") setCatalog(result[0].value);
    else { setCatalog(null); errors.push("Menu draft: " + String(result[0].reason)); }
    if (result[1].status === "fulfilled") setUnits(result[1].value);
    else { setUnits([]); errors.push("Menu units: " + String(result[1].reason)); }
    if (result[2].status === "fulfilled") setRecipes(result[2].value);
    else { setRecipes(null); errors.push("Ingredients and recipes: " + String(result[2].reason)); }
    setLoadError(errors.length ? errors.join(" · ") : null);
    setLoading(false);
  }, [api]);

  useEffect(() => { void reload(); }, [reload]);

  async function completed() {
    await reload();
    await onSaved();
  }

  const selectedIngredient = recipes?.ingredients.find((ingredient) =>
    ingredient.id === (lineIngredientId || recipes.ingredients[0]?.id));
  const menuUnitsAvailable = units.length > 0;

  return (
    <div style={{ display: "grid", gap: 16 }}>
      {loadError ? <Alert tone="danger" title="Some setup records could not load" description={loadError} /> : null}
      <Card>
        <CardHeader
          title="1. Build Your Cafe Menu"
          subtitle="Create categories and menu items, then add sizes or variants. Changes stay in draft until you publish them below."
          action={<Button variant="secondary" size="sm" loading={loading} onClick={() => void reload()}>Refresh draft</Button>}
        />
        <CardBody>
          <div style={{ display: "grid", gap: 16 }}>
            {catalog ? (
              <div style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
                <StatusBadge tone="neutral">{catalog.categories.length + " categories"}</StatusBadge>
                <StatusBadge tone="neutral">{catalog.items.length + " menu items"}</StatusBadge>
                <StatusBadge tone="neutral">{catalog.variants.length + " variants"}</StatusBadge>
                <StatusBadge tone="info">{catalog.currentPublication ? "Published revision " + catalog.currentPublication.revisionNumber : "Unpublished draft"}</StatusBadge>
              </div>
            ) : null}
            <OperationFeedback op={categoryOp} title="Category" />
            <form aria-label="Create menu category" style={formGrid} onSubmit={(e: FormEvent) => {
              e.preventDefault();
              const name = categoryName.trim();
              if (!name) return;
              void submitIdempotent(categoryOp, "category:" + name, (operationId) =>
                api.saveMenuCategory({ operationId, name }), completed).then((saved) => {
                  if (saved) setCategoryName("");
                });
            }}>
              <FormField label="New category name" required>
                <TextInput required value={categoryName} disabled={categoryOp.inFlight || Boolean(categoryOp.unknownOutcome)} onChange={(e) => setCategoryName(e.target.value)} placeholder="Coffee, Pastries, Food..." />
              </FormField>
              <Button type="submit" variant="secondary" loading={categoryOp.inFlight} disabled={!categoryName.trim()}>Add category</Button>
            </form>
            <OperationFeedback op={itemOp} title="Menu item" />
            <form aria-label="Create menu item" style={formGrid} onSubmit={(e: FormEvent) => {
              e.preventDefault();
              const name = itemName.trim();
              const unit = unitId || units[0]?.id || "";
              if (!name || !unit) return;
              void submitIdempotent(itemOp, "item:" + name + ":" + categoryId + ":" + unit + ":" + itemPrice,
                (operationId) => api.saveMenuItem({
                  operationId, name, unitId: unit, categoryId: categoryId || null,
                  sellingPriceCents: parsePhpToCents(itemPrice),
                }), completed).then((saved) => { if (saved) setItemName(""); });
            }}>
              <FormField label="Menu item name" required><TextInput required value={itemName} onChange={(e) => setItemName(e.target.value)} placeholder="Spanish Latte" /></FormField>
              <FormField label="Category">
                <SelectInput value={categoryId} onChange={(e) => setCategoryId(e.target.value)}>
                  <option value="">Uncategorized</option>
                  {(catalog?.categories ?? []).filter((c) => c.isActive).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
                </SelectInput>
              </FormField>
              <FormField label="Selling price (PHP)" required><TextInput inputMode="decimal" required value={itemPrice} onChange={(e) => setItemPrice(e.target.value)} /></FormField>
              <FormField label="Selling unit" required>
                <SelectInput value={unitId || units[0]?.id || ""} onChange={(e) => setUnitId(e.target.value)}>
                  {units.filter((u) => u.isActive).map((u) => <option key={u.id} value={u.id}>{u.name}</option>)}
                </SelectInput>
              </FormField>
              <Button type="submit" variant="primary" loading={itemOp.inFlight} disabled={!menuUnitsAvailable || !itemName.trim()}>Save menu item</Button>
            </form>
            {!menuUnitsAvailable ? <Alert tone="warning" title="No selling units available" description="Create or restore a valid selling unit in store configuration before adding menu items. The UI will not fabricate a unit ID." /> : null}
            <OperationFeedback op={variantOp} title="Menu variant" />
            <form aria-label="Create menu variant" style={formGrid} onSubmit={(e: FormEvent) => {
              e.preventDefault();
              const productId = variantProductId || catalog?.items[0]?.productId;
              const name = variantName.trim();
              if (!productId || !name) return;
              void submitIdempotent(variantOp, "variant:" + productId + ":" + name + ":" + variantPrice,
                (operationId) => api.saveMenuVariant({
                  operationId, productId, name, sellingPriceCents: parsePhpToCents(variantPrice),
                }), completed);
            }}>
              <FormField label="Menu item" required>
                <SelectInput value={variantProductId || catalog?.items[0]?.productId || ""} onChange={(e) => setVariantProductId(e.target.value)}>
                  {(catalog?.items ?? []).filter((item) => item.isActive).map((item) => <option key={item.productId} value={item.productId}>{item.name}</option>)}
                </SelectInput>
              </FormField>
              <FormField label="Size or variant" required><TextInput value={variantName} onChange={(e) => setVariantName(e.target.value)} placeholder="Regular, Large..." required /></FormField>
              <FormField label="Variant price (PHP)" required><TextInput inputMode="decimal" value={variantPrice} onChange={(e) => setVariantPrice(e.target.value)} required /></FormField>
              <Button type="submit" variant="secondary" loading={variantOp.inFlight} disabled={!catalog?.items.length || !variantName.trim()}>Add variant</Button>
            </form>
          </div>
        </CardBody>
      </Card>

      <Card>
        <CardHeader title="2. Define Ingredients and Recipes" subtitle="Add ingredient stock units and recipe quantities. Publish recipes after completing the ingredient links." />
        <CardBody>
          <div style={{ display: "grid", gap: 16 }}>
            {recipes ? <StatusBadge tone="neutral">{recipes.ingredients.length + " ingredients · " + recipes.recipes.length + " recipes"}</StatusBadge> : null}
            <OperationFeedback op={ingredientOp} title="Ingredient" />
            <form aria-label="Create ingredient" style={formGrid} onSubmit={(e: FormEvent) => {
              e.preventDefault();
              const name = ingredientName.trim();
              if (!name) return;
              void submitIdempotent(ingredientOp, "ingredient:" + name + ":" + stockUnit + ":" + reorderQty,
                (operationId) => api.saveIngredient({ operationId, name, stockUnit, reorderLevelX1000: quantityX1000(reorderQty, true) }),
                completed);
            }}>
              <FormField label="Ingredient name" required><TextInput required value={ingredientName} onChange={(e) => setIngredientName(e.target.value)} placeholder="Coffee beans, milk, cups..." /></FormField>
              <FormField label="Stock unit" required>
                <SelectInput value={stockUnit} onChange={(e) => setStockUnit(e.target.value as "Gram" | "Milliliter" | "Piece")}>
                  <option value="Gram">Gram</option><option value="Milliliter">Milliliter</option><option value="Piece">Piece</option>
                </SelectInput>
              </FormField>
              <FormField label="Low-stock level"><TextInput inputMode="decimal" value={reorderQty} onChange={(e) => setReorderQty(e.target.value)} /></FormField>
              <Button type="submit" variant="secondary" disabled={!ingredientName.trim()} loading={ingredientOp.inFlight}>Add ingredient</Button>
            </form>
            <OperationFeedback op={recipeOp} title="Recipe" />
            <form aria-label="Create recipe" style={formGrid} onSubmit={(e: FormEvent) => {
              e.preventDefault();
              const name = recipeName.trim();
              if (!name) return;
              void submitIdempotent(recipeOp, "recipe:" + name + ":" + recipeYield,
                (operationId) => api.saveRecipe({ operationId, name, yieldX1000: quantityX1000(recipeYield) }), completed);
            }}>
              <FormField label="Recipe name" required><TextInput required value={recipeName} onChange={(e) => setRecipeName(e.target.value)} placeholder="Spanish Latte" /></FormField>
              <FormField label="Recipe yield"><TextInput inputMode="decimal" required value={recipeYield} onChange={(e) => setRecipeYield(e.target.value)} /></FormField>
              <Button type="submit" variant="secondary" loading={recipeOp.inFlight} disabled={!recipeName.trim()}>Add recipe</Button>
            </form>
            <OperationFeedback op={lineOp} title="Recipe ingredient line" />
            <form aria-label="Add recipe ingredient" style={formGrid} onSubmit={(e: FormEvent) => {
              e.preventDefault();
              const recipeId = lineRecipeId || recipes?.recipes[0]?.id || "";
              const ingredientId = lineIngredientId || recipes?.ingredients[0]?.id || "";
              if (!recipeId || !ingredientId || !selectedIngredient) return;
              void submitIdempotent(lineOp, "recipe-line:" + recipeId + ":" + ingredientId + ":" + lineQty,
                (operationId) => api.saveRecipeLine({
                  operationId, recipeId, ingredientId, inputUnit: selectedIngredient.stockUnit,
                  declaredQuantityX1000: quantityX1000(lineQty),
                }), completed);
            }}>
              <FormField label="Recipe" required>
                <SelectInput value={lineRecipeId || recipes?.recipes[0]?.id || ""} onChange={(e) => setLineRecipeId(e.target.value)}>
                  {(recipes?.recipes ?? []).filter((r) => r.isActive).map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}
                </SelectInput>
              </FormField>
              <FormField label="Ingredient" required>
                <SelectInput value={lineIngredientId || recipes?.ingredients[0]?.id || ""} onChange={(e) => setLineIngredientId(e.target.value)}>
                  {(recipes?.ingredients ?? []).filter((i) => i.isActive).map((i) => <option key={i.id} value={i.id}>{i.name} ({i.stockUnit})</option>)}
                </SelectInput>
              </FormField>
              <FormField label={"Quantity (" + (selectedIngredient?.stockUnit ?? "stock unit") + ")"} required>
                <TextInput inputMode="decimal" value={lineQty} onChange={(e) => setLineQty(e.target.value)} required />
              </FormField>
              <Button type="submit" variant="secondary" loading={lineOp.inFlight} disabled={!recipes?.ingredients.length || !recipes?.recipes.length}>Add to recipe</Button>
            </form>
            <OperationFeedback op={bindingOp} title="Menu recipe binding" />
            <form aria-label="Link recipe to menu item" style={formGrid} onSubmit={(e: FormEvent) => {
              e.preventDefault();
              const productId = bindingItemId || catalog?.items[0]?.productId || "";
              const recipeId = bindingRecipeId || recipes?.recipes[0]?.id || "";
              if (!productId || !recipeId) return;
              void submitIdempotent(bindingOp, "bind-recipe:" + productId + ":" + recipeId,
                (operationId) => api.bindMenuItemRecipe({ operationId, productId, recipeId }), completed);
            }}>
              <FormField label="Menu item for this recipe" required>
                <SelectInput value={bindingItemId || catalog?.items[0]?.productId || ""} onChange={(e) => setBindingItemId(e.target.value)}>
                  {(catalog?.items ?? []).filter((i) => i.isActive).map((i) => <option value={i.productId} key={i.productId}>{i.name}</option>)}
                </SelectInput>
              </FormField>
              <FormField label="Recipe to use" required>
                <SelectInput value={bindingRecipeId || recipes?.recipes[0]?.id || ""} onChange={(e) => setBindingRecipeId(e.target.value)}>
                  {(recipes?.recipes ?? []).filter((x) => x.isActive).map((x) => <option value={x.id} key={x.id}>{x.name}</option>)}
                </SelectInput>
              </FormField>
              <Button type="submit" variant="primary" loading={bindingOp.inFlight} disabled={!catalog?.items.length || !recipes?.recipes.length}>Link recipe to item</Button>
            </form>
          </div>
        </CardBody>
      </Card>
      <Alert tone="info" title="Publish before selling" description="Creating draft items or recipes does not immediately affect cashier stations. Use Publish Menu & Recipes after reviewing the changes and recipe bindings." />
      {catalog?.items.length ? <div style={{ fontSize: 13 }}>Current draft menu: {catalog.items.map((x) => x.name + " (" + formatPhp(x.sellingPriceCents) + ")").join(" · ")}</div> : null}
    </div>
  );
}

export function CafeSupplierSetup({ api, onSaved }: SetupProps) {
  const [name, setName] = useState("");
  const [contact, setContact] = useState("");
  const op = useBackofficeOperation("setup-supplier");
  return (
    <Card>
      <CardHeader title="Supplier Setup" subtitle="Register a supplier before receiving ingredient stock. Supplier records are stored on the real Cafe Host." />
      <CardBody>
        <div style={{ display: "grid", gap: 12 }}>
          <OperationFeedback op={op} title="Supplier" />
          <form aria-label="Create supplier" style={formGrid} onSubmit={(e: FormEvent) => {
            e.preventDefault();
            const supplierName = name.trim();
            const contactNumber = contact.trim();
            if (!supplierName) return;
            void submitIdempotent(op, "supplier:" + supplierName + ":" + contactNumber,
              (operationId) => api.saveSupplier({ operationId, name: supplierName, contactNumber }), onSaved);
          }}>
            <FormField label="Supplier name" required>
              <TextInput required value={name} onChange={(e) => setName(e.target.value)} placeholder="Local dairy supplier" />
            </FormField>
            <FormField label="Contact number">
              <TextInput value={contact} onChange={(e) => setContact(e.target.value)} placeholder="Optional" />
            </FormField>
            <Button type="submit" variant="primary" loading={op.inFlight} disabled={!name.trim()}>Save supplier</Button>
          </form>
        </div>
      </CardBody>
    </Card>
  );
}
