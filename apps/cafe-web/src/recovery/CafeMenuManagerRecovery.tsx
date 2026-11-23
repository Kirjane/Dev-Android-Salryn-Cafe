/**
 * Canonical source attribution:
 *   Repository: Kirjane/Salryn-CafeYza
 *   Commit: 4241ea1ec0a8cebf552d93f25a7ac134d7d4d6f3
 *   Original path: src/Salryn.App.Web/src/components/CafeMenuManager.tsx
 *
 * Adapted for Dev-Salryn-Cafe current Host authority:
 *   - Uses @salryn/cafe-ui + cafe-tokens.css primitives without adding dependencies.
 *   - Uses useBackofficeOperation for durable GUID operationId, Idempotency-Key
 *     retry preservation, double-submit locking, and unknown-outcome fencing.
 *   - Distinguishes failed catalog load (401/403/5xx/network) from authenticated empty catalog.
 *   - Adds a bounded recovery extension control for variant-level modifier group
 *     linking (PUT /api/menu/variants/{variantId}/modifier-groups), which existed
 *     in canonical menuCatalog.ts/Host APIs but lacked UI in CafeMenuManager.tsx.
 *   - Fails closed unless mounted against a disposable test Host (/api/test/host-identity)
 *     or explicit Vite DEV mode.
 */

import { useCallback, useEffect, useMemo, useState, type CSSProperties, type FormEvent } from "react";
import {
  Alert,
  BrandMark,
  Button,
  Card,
  CardBody,
  CardHeader,
  ErrorBoundary,
  FormField,
  LoadingState,
  SelectInput,
  StatusBadge,
  TextInput,
} from "@salryn/cafe-ui";
import { HostApiError, formatPhp, parsePhpToCents } from "../api/hostClient";
import type { MenuUnitOption } from "../backoffice/types";
import { useBackofficeOperation } from "../backoffice/useBackofficeOperation";
import type { HostSession } from "../cashier/types";
import {
  fetchMenuCatalog,
  fetchProductUnits,
  fetchSession,
  fetchTestHostIdentity,
  loginOperator,
  logoutOperator,
  publishMenu,
  saveMenuCategory,
  saveMenuItem,
  saveMenuModifierGroup,
  saveMenuModifierOption,
  saveMenuVariant,
  setMenuItemModifierGroups,
  setMenuVariantModifierGroups,
  type MenuCatalogDraft,
  type MenuItem,
  type MenuModifierGroup,
  type MenuVariant,
  type TestHostIdentity,
} from "./menuCatalogAdapter";

function pesosFromCents(value: number): string {
  return (value / 100).toFixed(2);
}

function describeError(error: unknown): string {
  if (error instanceof HostApiError) {
    return error.code
      ? `${error.message} [${error.code}] (HTTP ${error.status})`
      : `${error.message} (HTTP ${error.status})`;
  }
  return error instanceof Error ? error.message : "Unexpected menu catalog error.";
}

function isProtectedDatabasePath(path: string): boolean {
  const normalized = path.replace(/\//g, "\\").toLowerCase();
  return normalized.includes("\\programdata\\salryncafe") || normalized.includes("\\programdata\\salryn\\");
}

const twoColumnGrid: CSSProperties = {
  display: "grid",
  gap: 16,
  gridTemplateColumns: "repeat(auto-fit, minmax(min(100%, 300px), 1fr))",
};

const twoFieldGrid: CSSProperties = {
  display: "grid",
  gap: 12,
  gridTemplateColumns: "repeat(auto-fit, minmax(min(100%, 200px), 1fr))",
};

export type CafeMenuManagerRecoveryProps = {
  catalog: MenuCatalogDraft;
  units: MenuUnitOption[];
  onRefresh: () => Promise<MenuCatalogDraft | null>;
};

export function CafeMenuManagerRecovery({
  catalog,
  units,
  onRefresh,
}: CafeMenuManagerRecoveryProps) {
  const op = useBackofficeOperation("c1-menu-recovery");
  const busy = op.inFlight;

  const [message, setMessage] = useState<string | null>(null);
  const [localError, setLocalError] = useState<string | null>(null);

  const [categoryName, setCategoryName] = useState("");
  const [selectedItemId, setSelectedItemId] = useState("");
  const [itemName, setItemName] = useState("");
  const [itemCategoryId, setItemCategoryId] = useState("");
  const [itemUnitId, setItemUnitId] = useState("");
  const [itemSku, setItemSku] = useState("");
  const [itemBarcode, setItemBarcode] = useState("");
  const [itemPrice, setItemPrice] = useState("");

  const [variantName, setVariantName] = useState("");
  const [variantSku, setVariantSku] = useState("");
  const [variantBarcode, setVariantBarcode] = useState("");
  const [variantPrice, setVariantPrice] = useState("");

  const [groupName, setGroupName] = useState("");
  const [groupRequired, setGroupRequired] = useState(false);
  const [groupMin, setGroupMin] = useState("0");
  const [groupMax, setGroupMax] = useState("1");
  const [selectedGroupId, setSelectedGroupId] = useState("");
  const [optionName, setOptionName] = useState("");
  const [optionDelta, setOptionDelta] = useState("");
  const [linkedGroupIds, setLinkedGroupIds] = useState<string[]>([]);

  // Bounded C1 recovery extension state: variant-level modifier group linking
  const [selectedVariantLinkId, setSelectedVariantLinkId] = useState("");
  const [variantLinkedGroupIds, setVariantLinkedGroupIds] = useState<string[]>([]);

  const selectedItem = useMemo(
    () => catalog.items.find((item) => item.productId === selectedItemId) ?? null,
    [catalog.items, selectedItemId],
  );

  const selectedItemVariants = useMemo(
    () => catalog.variants.filter((variant) => variant.productId === selectedItemId),
    [catalog.variants, selectedItemId],
  );

  const syncVariantLinksFor = useCallback(
    (variantId: string) => {
      setSelectedVariantLinkId(variantId);
      setVariantLinkedGroupIds(
        catalog.variantModifierGroups
          .filter((link) => link.ownerId === variantId)
          .sort((a, b) => a.displayOrder - b.displayOrder)
          .map((link) => link.groupId),
      );
    },
    [catalog.variantModifierGroups],
  );

  useEffect(() => {
    if (!selectedItemId) {
      setSelectedVariantLinkId("");
      setVariantLinkedGroupIds([]);
      return;
    }
    const stillValid = selectedItemVariants.some((v) => v.id === selectedVariantLinkId);
    const targetId = stillValid ? selectedVariantLinkId : (selectedItemVariants[0]?.id ?? "");
    if (targetId !== selectedVariantLinkId) {
      syncVariantLinksFor(targetId);
    }
  }, [selectedItemId, selectedItemVariants, selectedVariantLinkId, syncVariantLinksFor]);

  async function runMutation<T>(
    intentKey: string,
    action: (operationId: string) => Promise<T>,
    success: string,
    onSuccess?: (result: T, refreshed: MenuCatalogDraft | null) => void,
  ): Promise<void> {
    setLocalError(null);
    setMessage(null);
    let succeeded = false;
    const result = await op.execute(async (operationId) => {
      const value = await action(operationId);
      succeeded = true;
      return value;
    }, intentKey);
    if (succeeded) {
      setMessage(success);
      const refreshed = await onRefresh();
      if (onSuccess) {
        onSuccess(result as T, refreshed);
      }
    }
  }

  function chooseItem(item: MenuItem, draftOverride?: MenuCatalogDraft | null) {
    const source = draftOverride ?? catalog;
    setSelectedItemId(item.productId);
    setItemName(item.name);
    setItemCategoryId(item.categoryId ?? "");
    setItemUnitId(item.unitId);
    setItemSku(item.sku ?? "");
    setItemBarcode(item.barcode ?? "");
    setItemPrice(pesosFromCents(item.sellingPriceCents));
    setLinkedGroupIds(
      source.itemModifierGroups
        .filter((link) => link.ownerId === item.productId)
        .sort((a, b) => a.displayOrder - b.displayOrder)
        .map((link) => link.groupId),
    );
    const firstVariant = source.variants.find((v) => v.productId === item.productId);
    if (firstVariant) {
      setSelectedVariantLinkId(firstVariant.id);
      setVariantLinkedGroupIds(
        source.variantModifierGroups
          .filter((link) => link.ownerId === firstVariant.id)
          .sort((a, b) => a.displayOrder - b.displayOrder)
          .map((link) => link.groupId),
      );
    } else {
      setSelectedVariantLinkId("");
      setVariantLinkedGroupIds([]);
    }
  }

  function resetItemEditor() {
    setSelectedItemId("");
    setItemName("");
    setItemCategoryId("");
    setItemUnitId("");
    setItemSku("");
    setItemBarcode("");
    setItemPrice("");
    setLinkedGroupIds([]);
    setSelectedVariantLinkId("");
    setVariantLinkedGroupIds([]);
  }

  async function saveExistingItem(patch: Partial<MenuItem>, success: string) {
    if (!selectedItem) return;
    const next = { ...selectedItem, ...patch };
    const intentKey = [
      "item-patch",
      next.productId,
      next.categoryId ?? "",
      next.name.trim(),
      next.unitId,
      next.sku ?? "",
      next.barcode ?? "",
      String(next.sellingPriceCents),
      String(next.isActive),
      String(next.isTemporarilyUnavailable),
    ].join(":");
    await runMutation(
      intentKey,
      (operationId) =>
        saveMenuItem(operationId, {
          productId: next.productId,
          categoryId: next.categoryId,
          name: next.name,
          description: next.description,
          unitId: next.unitId,
          sku: next.sku,
          barcode: next.barcode,
          sellingPriceCents: next.sellingPriceCents,
          costPriceCents: next.costPriceCents,
          isActive: next.isActive,
          isTemporarilyUnavailable: next.isTemporarilyUnavailable,
        }),
      success,
    );
  }

  async function updateVariant(variant: MenuVariant, patch: Partial<MenuVariant>, success: string) {
    const next = { ...variant, ...patch };
    const intentKey = [
      "variant-patch",
      next.id,
      next.name.trim(),
      String(next.sellingPriceCents),
      String(next.isActive),
      String(next.isTemporarilyUnavailable),
      String(next.displayOrder),
    ].join(":");
    await runMutation(
      intentKey,
      (operationId) =>
        saveMenuVariant(operationId, {
          id: next.id,
          productId: next.productId,
          name: next.name,
          sku: next.sku,
          barcode: next.barcode,
          sellingPriceCents: next.sellingPriceCents,
          costPriceCents: next.costPriceCents,
          isActive: next.isActive,
          isTemporarilyUnavailable: next.isTemporarilyUnavailable,
          displayOrder: next.displayOrder,
        }),
      success,
    );
  }

  const publication = catalog.currentPublication;
  const activeError = localError ?? op.error;
  const isEmptyCatalog = catalog.items.length === 0;

  return (
    <Card>
      <CardHeader
        title="Cafe Menu / Catalog"
        subtitle="C1 manages customer-facing menu identity, first-class sizes, modifiers, lifecycle, temporary availability, and immutable published revisions. Ingredient recipes and stock consumption remain outside C1."
        action={
          <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center", gap: 8 }}>
            {publication ? (
              <StatusBadge tone="success">Published r{publication.revisionNumber}</StatusBadge>
            ) : (
              <StatusBadge tone="warning">Not published</StatusBadge>
            )}
            <Button
              variant="primary"
              loading={busy}
              onClick={() =>
                void runMutation(
                  `publish:${publication?.revisionNumber ?? 0}:${catalog.items.length}:${catalog.variants.length}:${catalog.modifierGroups.length}`,
                  (operationId) => publishMenu(operationId),
                  "Current menu content published as an immutable revision.",
                )
              }
            >
              Publish menu
            </Button>
          </div>
        }
      />
      <CardBody>
        <div style={{ display: "grid", gap: 18 }}>
          {message ? <Alert tone="success" title="C1 menu update" description={message} /> : null}
          {activeError ? <Alert tone="danger" title="C1 validation" description={activeError} /> : null}

          {op.unknownOutcome ? (
            <div style={{ display: "grid", gap: 8 }}>
              <Alert
                tone="warning"
                title="C1 mutation outcome uncertain"
                description={`The Host outcome could not be confirmed for operation ${op.unknownOutcome.operationId}. Do not submit changed details with a replacement key; refresh and verify the draft first, or retry the exact identical request.`}
              />
              <div>
                <Button
                  size="sm"
                  variant="secondary"
                  onClick={() => {
                    op.reset();
                    void onRefresh();
                  }}
                >
                  Refresh &amp; clear fence
                </Button>
              </div>
            </div>
          ) : null}

          {isEmptyCatalog ? (
            <Alert
              tone="info"
              title="Empty menu catalog"
              description="The menu catalog loaded cleanly from the Host, and no menu items exist yet. Add a category or pick an existing one to create your first menu item."
            />
          ) : null}

          {publication ? (
            <div
              style={{
                display: "grid",
                gap: 12,
                gridTemplateColumns: "repeat(auto-fit, minmax(min(100%, 170px), 1fr))",
                padding: 12,
                borderRadius: "var(--salryn-radius-md)",
                border: "1px solid var(--salryn-border)",
                background: "var(--salryn-surface-subtle)",
                fontSize: "0.8125rem",
              }}
            >
              <div>
                <strong>Revision</strong>
                <div className="salryn-tabular" style={{ wordBreak: "break-all" }}>{publication.revisionId}</div>
              </div>
              <div>
                <strong>Content hash</strong>
                <div style={{ fontFamily: "var(--salryn-font-mono)", wordBreak: "break-all" }}>
                  {publication.contentHash.slice(0, 20)}...
                </div>
              </div>
              <div>
                <strong>Publication hash</strong>
                <div style={{ fontFamily: "var(--salryn-font-mono)", wordBreak: "break-all" }}>
                  {publication.publicationHash.slice(0, 20)}...
                </div>
              </div>
              <div>
                <strong>Published</strong>
                <div>{new Date(publication.publishedAtUtc).toLocaleString()}</div>
              </div>
            </div>
          ) : null}

          <div style={twoColumnGrid}>
            <section
              aria-label="Menu categories"
              style={{
                display: "grid",
                gap: 12,
                alignContent: "start",
                padding: 16,
                borderRadius: "var(--salryn-radius-md)",
                border: "1px solid var(--salryn-border)",
              }}
            >
              <div>
                <h2 style={{ margin: 0, fontSize: "1rem", color: "var(--salryn-espresso)" }}>Menu categories</h2>
                <p style={{ margin: "4px 0 0", fontSize: "0.8125rem", color: "var(--salryn-mocha)" }}>
                  Existing catalog categories are reused as stable menu-category identity.
                </p>
              </div>
              <form
                style={{ display: "flex", flexWrap: "wrap", gap: 8, alignItems: "end" }}
                onSubmit={(e: FormEvent) => {
                  e.preventDefault();
                  const trimmed = categoryName.trim();
                  if (!trimmed) return;
                  void runMutation(
                    `category:add:${trimmed}`,
                    (operationId) => saveMenuCategory(operationId, { id: null, name: trimmed, isActive: true }),
                    "Menu category saved.",
                    () => setCategoryName(""),
                  );
                }}
              >
                <div style={{ flex: "1 1 180px" }}>
                  <FormField label="Category name">
                    <TextInput
                      value={categoryName}
                      onChange={(event) => setCategoryName(event.target.value)}
                      placeholder="Coffee, Pastries, Rice Meals..."
                    />
                  </FormField>
                </div>
                <Button type="submit" variant="secondary" disabled={busy || !categoryName.trim()}>
                  Add category
                </Button>
              </form>
              <div style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
                {catalog.categories.map((category) => (
                  <Button
                    key={category.id}
                    size="sm"
                    variant={category.isActive ? "secondary" : "ghost"}
                    disabled={busy}
                    onClick={() =>
                      void runMutation(
                        `category:toggle:${category.id}:${!category.isActive}`,
                        (operationId) =>
                          saveMenuCategory(operationId, {
                            id: category.id,
                            name: category.name,
                            isActive: !category.isActive,
                          }),
                        category.isActive ? "Category deactivated." : "Category reactivated.",
                      )
                    }
                  >
                    {category.name}
                    {category.isActive ? "" : " · inactive"}
                  </Button>
                ))}
              </div>
            </section>

            <section
              aria-label="Create or edit menu item"
              style={{
                display: "grid",
                gap: 12,
                alignContent: "start",
                padding: 16,
                borderRadius: "var(--salryn-radius-md)",
                border: "1px solid var(--salryn-border)",
              }}
            >
              <div>
                <h2 style={{ margin: 0, fontSize: "1rem", color: "var(--salryn-espresso)" }}>
                  {selectedItem ? `Edit menu item — ${selectedItem.name}` : "Create menu item"}
                </h2>
                <p style={{ margin: "4px 0 0", fontSize: "0.8125rem", color: "var(--salryn-mocha)" }}>
                  {selectedItem
                    ? `Editing stable ID ${selectedItem.productId} without creating a duplicate.`
                    : "A stable menu item gets a first-class Standard variant automatically."}
                </p>
                {selectedItem ? (
                  <div style={{ marginTop: 8 }}>
                    <Button size="sm" variant="ghost" disabled={busy} onClick={resetItemEditor}>
                      Create another item
                    </Button>
                  </div>
                ) : null}
              </div>

              <form
                style={{ display: "grid", gap: 12 }}
                onSubmit={(e: FormEvent) => {
                  e.preventDefault();
                  if (!itemName.trim() || !itemUnitId) return;
                  let priceCents: number;
                  try {
                    priceCents = parsePhpToCents(itemPrice);
                  } catch (err) {
                    setLocalError(describeError(err));
                    return;
                  }
                  const intentKey = [
                    "item:save",
                    selectedItem?.productId ?? "new",
                    itemCategoryId || "none",
                    itemName.trim(),
                    itemUnitId,
                    itemSku.trim(),
                    itemBarcode.trim(),
                    String(priceCents),
                  ].join(":");
                  void runMutation(
                    intentKey,
                    (operationId) =>
                      saveMenuItem(operationId, {
                        productId: selectedItem?.productId ?? null,
                        categoryId: itemCategoryId || null,
                        name: itemName.trim(),
                        description: selectedItem?.description ?? null,
                        unitId: itemUnitId,
                        sku: itemSku.trim() || null,
                        barcode: itemBarcode.trim() || null,
                        sellingPriceCents: priceCents,
                        costPriceCents: selectedItem?.costPriceCents ?? null,
                        isActive: selectedItem?.isActive ?? true,
                        isTemporarilyUnavailable: selectedItem?.isTemporarilyUnavailable ?? false,
                      }),
                    selectedItem ? "Menu item changes saved." : "Menu item and Standard variant created.",
                    (saved, refreshed) => chooseItem(saved, refreshed),
                  );
                }}
              >
                <div style={twoFieldGrid}>
                  <FormField label="Menu item name" required>
                    <TextInput
                      value={itemName}
                      onChange={(event) => setItemName(event.target.value)}
                      placeholder="Menu item name"
                    />
                  </FormField>
                  <FormField label="Category">
                    <SelectInput
                      value={itemCategoryId}
                      onChange={(event) => setItemCategoryId(event.target.value)}
                    >
                      <option value="">No category</option>
                      {catalog.categories
                        .filter((x) => x.isActive)
                        .map((category) => (
                          <option key={category.id} value={category.id}>
                            {category.name}
                          </option>
                        ))}
                    </SelectInput>
                  </FormField>
                  <FormField label="Unit" required>
                    <SelectInput
                      value={itemUnitId}
                      onChange={(event) => setItemUnitId(event.target.value)}
                    >
                      <option value="">Choose unit</option>
                      {units
                        .filter((x) => x.isActive)
                        .map((unit) => (
                          <option key={unit.id} value={unit.id}>
                            {unit.name}
                          </option>
                        ))}
                    </SelectInput>
                  </FormField>
                  <FormField label="Base price (PHP)" required>
                    <TextInput
                      value={itemPrice}
                      onChange={(event) => setItemPrice(event.target.value)}
                      placeholder="145.00"
                      inputMode="decimal"
                    />
                  </FormField>
                  <FormField label="SKU">
                    <TextInput
                      value={itemSku}
                      onChange={(event) => setItemSku(event.target.value)}
                      placeholder="SKU optional"
                    />
                  </FormField>
                  <FormField label="Barcode">
                    <TextInput
                      value={itemBarcode}
                      onChange={(event) => setItemBarcode(event.target.value)}
                      placeholder="Barcode optional"
                    />
                  </FormField>
                </div>
                <div>
                  <Button
                    type="submit"
                    variant="secondary"
                    disabled={busy || !itemName.trim() || !itemUnitId || !itemPrice.trim()}
                  >
                    {selectedItem ? "Save item changes" : "Create menu item"}
                  </Button>
                </div>
              </form>
            </section>
          </div>

          <section
            aria-label="Items lifecycle and availability"
            style={{
              display: "grid",
              gap: 12,
              padding: 16,
              borderRadius: "var(--salryn-radius-md)",
              border: "1px solid var(--salryn-border)",
            }}
          >
            <div>
              <h2 style={{ margin: 0, fontSize: "1rem", color: "var(--salryn-espresso)" }}>
                Items / lifecycle / availability
              </h2>
              <p style={{ margin: "4px 0 0", fontSize: "0.8125rem", color: "var(--salryn-mocha)" }}>
                Inactive lifecycle and temporary sold-out state are separate controls.
              </p>
            </div>
            <div
              style={{
                display: "grid",
                gap: 10,
                gridTemplateColumns: "repeat(auto-fit, minmax(min(100%, 220px), 1fr))",
              }}
            >
              {catalog.items.map((item) => {
                const isSelected = selectedItemId === item.productId;
                return (
                  <button
                    key={item.productId}
                    type="button"
                    onClick={() => chooseItem(item)}
                    className="salryn-focusable"
                    style={{
                      textAlign: "left",
                      padding: 12,
                      minHeight: 44,
                      borderRadius: "var(--salryn-radius-md)",
                      border: isSelected
                        ? "2px solid var(--salryn-espresso)"
                        : "1px solid var(--salryn-border)",
                      background: isSelected ? "var(--salryn-surface-subtle)" : "var(--salryn-surface)",
                      color: "var(--salryn-espresso)",
                      cursor: "pointer",
                    }}
                  >
                    <div style={{ display: "flex", justifyContent: "space-between", gap: 8, fontWeight: 700 }}>
                      <span>{item.name}</span>
                      <span className="salryn-tabular">{formatPhp(item.sellingPriceCents)}</span>
                    </div>
                    <div style={{ marginTop: 4, fontSize: "0.75rem", color: "var(--salryn-mocha)" }}>
                      {item.isActive ? "Active" : "Inactive"} ·{" "}
                      {item.isTemporarilyUnavailable ? "Sold out" : "Available"}
                      {item.sku ? ` · ${item.sku}` : ""}
                    </div>
                  </button>
                );
              })}
            </div>

            {selectedItem ? (
              <div
                style={{
                  display: "flex",
                  flexWrap: "wrap",
                  gap: 8,
                  padding: 12,
                  borderRadius: "var(--salryn-radius-md)",
                  background: "var(--salryn-surface-subtle)",
                }}
              >
                <Button
                  variant="secondary"
                  disabled={busy}
                  onClick={() =>
                    void saveExistingItem(
                      { isTemporarilyUnavailable: !selectedItem.isTemporarilyUnavailable },
                      selectedItem.isTemporarilyUnavailable
                        ? "Item marked available."
                        : "Item marked temporarily unavailable.",
                    )
                  }
                >
                  {selectedItem.isTemporarilyUnavailable ? "Mark available" : "Mark sold out"}
                </Button>
                <Button
                  variant={selectedItem.isActive ? "danger" : "secondary"}
                  disabled={busy}
                  onClick={() =>
                    void saveExistingItem(
                      { isActive: !selectedItem.isActive },
                      selectedItem.isActive ? "Menu item deactivated." : "Menu item reactivated.",
                    )
                  }
                >
                  {selectedItem.isActive ? "Deactivate" : "Reactivate"}
                </Button>
              </div>
            ) : null}
          </section>

          {selectedItem ? (
            <div style={twoColumnGrid}>
              <section
                aria-label="Variants and sizes"
                style={{
                  display: "grid",
                  gap: 12,
                  alignContent: "start",
                  padding: 16,
                  borderRadius: "var(--salryn-radius-md)",
                  border: "1px solid var(--salryn-border)",
                }}
              >
                <div>
                  <h2 style={{ margin: 0, fontSize: "1rem", color: "var(--salryn-espresso)" }}>
                    Variants / sizes for {selectedItem.name}
                  </h2>
                  <p style={{ margin: "4px 0 0", fontSize: "0.8125rem", color: "var(--salryn-mocha)" }}>
                    Sizes are stable first-class menu variants, not separate stock recipes.
                  </p>
                </div>

                <form
                  style={{ display: "grid", gap: 10 }}
                  onSubmit={(e: FormEvent) => {
                    e.preventDefault();
                    const trimmed = variantName.trim();
                    if (!trimmed) return;
                    let priceCents: number;
                    try {
                      priceCents = parsePhpToCents(variantPrice.trim() || "0");
                    } catch (err) {
                      setLocalError(describeError(err));
                      return;
                    }
                    void runMutation(
                      `variant:add:${selectedItem.productId}:${trimmed}:${priceCents}:${variantSku.trim()}`,
                      (operationId) =>
                        saveMenuVariant(operationId, {
                          id: null,
                          productId: selectedItem.productId,
                          name: trimmed,
                          sku: variantSku.trim() || null,
                          barcode: variantBarcode.trim() || null,
                          sellingPriceCents: priceCents,
                          costPriceCents: null,
                          isActive: true,
                          isTemporarilyUnavailable: false,
                          displayOrder: selectedItemVariants.length,
                        }),
                      "Variant saved.",
                      () => {
                        setVariantName("");
                        setVariantSku("");
                        setVariantBarcode("");
                        setVariantPrice("");
                      },
                    );
                  }}
                >
                  <div style={twoFieldGrid}>
                    <FormField label="Variant name" required>
                      <TextInput
                        value={variantName}
                        onChange={(event) => setVariantName(event.target.value)}
                        placeholder="Small, Medium, Large..."
                      />
                    </FormField>
                    <FormField label="Variant price (PHP)" required>
                      <TextInput
                        value={variantPrice}
                        onChange={(event) => setVariantPrice(event.target.value)}
                        placeholder="150.00"
                        inputMode="decimal"
                      />
                    </FormField>
                    <FormField label="Variant SKU">
                      <TextInput
                        value={variantSku}
                        onChange={(event) => setVariantSku(event.target.value)}
                        placeholder="Variant SKU optional"
                      />
                    </FormField>
                    <FormField label="Variant barcode">
                      <TextInput
                        value={variantBarcode}
                        onChange={(event) => setVariantBarcode(event.target.value)}
                        placeholder="Variant barcode optional"
                      />
                    </FormField>
                  </div>
                  <div>
                    <Button type="submit" variant="secondary" disabled={busy || !variantName.trim()}>
                      Add variant
                    </Button>
                  </div>
                </form>

                <div style={{ display: "grid", gap: 8 }}>
                  {selectedItemVariants.map((variant) => (
                    <div
                      key={variant.id}
                      style={{
                        display: "flex",
                        flexWrap: "wrap",
                        alignItems: "center",
                        justifyContent: "space-between",
                        gap: 8,
                        padding: 10,
                        borderRadius: "var(--salryn-radius-sm)",
                        border: "1px solid var(--salryn-border)",
                        fontSize: "0.8125rem",
                      }}
                    >
                      <div>
                        <strong>{variant.name}</strong>{" "}
                        <span style={{ color: "var(--salryn-mocha)" }}>
                          {formatPhp(variant.sellingPriceCents)} · {variant.isActive ? "active" : "inactive"} ·{" "}
                          {variant.isTemporarilyUnavailable ? "sold out" : "available"}
                        </span>
                      </div>
                      <div style={{ display: "flex", gap: 6 }}>
                        <Button
                          size="sm"
                          variant="ghost"
                          disabled={busy}
                          onClick={() =>
                            void updateVariant(
                              variant,
                              { isTemporarilyUnavailable: !variant.isTemporarilyUnavailable },
                              variant.isTemporarilyUnavailable ? "Variant available." : "Variant sold out.",
                            )
                          }
                        >
                          {variant.isTemporarilyUnavailable ? "Available" : "Sold out"}
                        </Button>
                        <Button
                          size="sm"
                          variant="ghost"
                          disabled={busy}
                          onClick={() =>
                            void updateVariant(
                              variant,
                              { isActive: !variant.isActive },
                              variant.isActive ? "Variant deactivated." : "Variant reactivated.",
                            )
                          }
                        >
                          {variant.isActive ? "Deactivate" : "Reactivate"}
                        </Button>
                      </div>
                    </div>
                  ))}
                </div>
              </section>

              <section
                aria-label="Modifier groups and add-ons"
                style={{
                  display: "grid",
                  gap: 14,
                  alignContent: "start",
                  padding: 16,
                  borderRadius: "var(--salryn-radius-md)",
                  border: "1px solid var(--salryn-border)",
                }}
              >
                <div>
                  <h2 style={{ margin: 0, fontSize: "1rem", color: "var(--salryn-espresso)" }}>
                    Modifier groups / add-ons
                  </h2>
                  <p style={{ margin: "4px 0 0", fontSize: "0.8125rem", color: "var(--salryn-mocha)" }}>
                    Price effect is C1 authority. Ingredient consumption remains C2.
                  </p>
                </div>

                <form
                  style={{ display: "grid", gap: 10 }}
                  onSubmit={(e: FormEvent) => {
                    e.preventDefault();
                    const trimmed = groupName.trim();
                    if (!trimmed) return;
                    const min = Math.max(0, Number(groupMin) || 0);
                    const max = Math.max(0, Number(groupMax) || 0);
                    void runMutation(
                      `modgroup:add:${trimmed}:${groupRequired}:${min}:${max}`,
                      (operationId) =>
                        saveMenuModifierGroup(operationId, {
                          id: null,
                          name: trimmed,
                          isRequired: groupRequired,
                          minSelections: min,
                          maxSelections: max,
                          isActive: true,
                          displayOrder: catalog.modifierGroups.length,
                        }),
                      "Modifier group saved.",
                      (saved) => {
                        setSelectedGroupId(saved.id);
                        setGroupName("");
                      },
                    );
                  }}
                >
                  <div style={twoFieldGrid}>
                    <FormField label="Modifier group name" required>
                      <TextInput
                        value={groupName}
                        onChange={(event) => setGroupName(event.target.value)}
                        placeholder="Milk choice, Add-ons..."
                      />
                    </FormField>
                    <label
                      style={{
                        display: "flex",
                        alignItems: "center",
                        gap: 8,
                        minHeight: 44,
                        padding: "0 12px",
                        borderRadius: "var(--salryn-radius-md)",
                        border: "1px solid var(--salryn-border)",
                        fontSize: "0.875rem",
                      }}
                    >
                      <input
                        type="checkbox"
                        checked={groupRequired}
                        onChange={(event) => {
                          setGroupRequired(event.target.checked);
                          if (event.target.checked && Number(groupMin) < 1) setGroupMin("1");
                        }}
                      />
                      <span>Required group</span>
                    </label>
                    <FormField label="Min selections">
                      <TextInput
                        value={groupMin}
                        onChange={(event) => setGroupMin(event.target.value)}
                        placeholder="0"
                        inputMode="numeric"
                      />
                    </FormField>
                    <FormField label="Max selections">
                      <TextInput
                        value={groupMax}
                        onChange={(event) => setGroupMax(event.target.value)}
                        placeholder="1"
                        inputMode="numeric"
                      />
                    </FormField>
                  </div>
                  <div>
                    <Button type="submit" variant="secondary" disabled={busy || !groupName.trim()}>
                      Add modifier group
                    </Button>
                  </div>
                </form>

                <form
                  style={{ display: "grid", gap: 10 }}
                  onSubmit={(e: FormEvent) => {
                    e.preventDefault();
                    const trimmed = optionName.trim();
                    if (!selectedGroupId || !trimmed) return;
                    let deltaCents: number;
                    try {
                      deltaCents = parsePhpToCents(optionDelta.trim() || "0");
                    } catch (err) {
                      setLocalError(describeError(err));
                      return;
                    }
                    void runMutation(
                      `modopt:add:${selectedGroupId}:${trimmed}:${deltaCents}`,
                      (operationId) =>
                        saveMenuModifierOption(operationId, {
                          id: null,
                          groupId: selectedGroupId,
                          name: trimmed,
                          priceDeltaCents: deltaCents,
                          isActive: true,
                          displayOrder: catalog.modifierOptions.filter((o) => o.groupId === selectedGroupId).length,
                        }),
                      "Modifier option saved.",
                      () => {
                        setOptionName("");
                        setOptionDelta("");
                      },
                    );
                  }}
                >
                  <FormField label="Target modifier group for option">
                    <SelectInput
                      value={selectedGroupId}
                      onChange={(event) => setSelectedGroupId(event.target.value)}
                    >
                      <option value="">Choose modifier group for option</option>
                      {catalog.modifierGroups
                        .filter((group) => group.isActive)
                        .map((group) => (
                          <option key={group.id} value={group.id}>
                            {group.name}
                          </option>
                        ))}
                    </SelectInput>
                  </FormField>
                  <div style={{ display: "flex", flexWrap: "wrap", gap: 8, alignItems: "end" }}>
                    <div style={{ flex: "2 1 160px" }}>
                      <FormField label="Option name">
                        <TextInput
                          value={optionName}
                          onChange={(event) => setOptionName(event.target.value)}
                          placeholder="Oat milk, Extra shot..."
                        />
                      </FormField>
                    </div>
                    <div style={{ flex: "1 1 110px" }}>
                      <FormField label="Price delta (PHP)">
                        <TextInput
                          value={optionDelta}
                          onChange={(event) => setOptionDelta(event.target.value)}
                          placeholder="35.00"
                          inputMode="decimal"
                        />
                      </FormField>
                    </div>
                    <Button
                      type="submit"
                      variant="secondary"
                      disabled={busy || !selectedGroupId || !optionName.trim()}
                    >
                      Add option
                    </Button>
                  </div>
                </form>

                <div style={{ display: "grid", gap: 8 }}>
                  <h3 style={{ margin: 0, fontSize: "0.875rem", color: "var(--salryn-espresso)" }}>
                    Link groups to selected item
                  </h3>
                  <div style={{ display: "grid", gap: 6 }}>
                    {catalog.modifierGroups
                      .filter((group) => group.isActive)
                      .map((group: MenuModifierGroup) => (
                        <label
                          key={group.id}
                          style={{
                            display: "flex",
                            alignItems: "center",
                            justifyContent: "space-between",
                            gap: 10,
                            padding: "8px 12px",
                            borderRadius: "var(--salryn-radius-sm)",
                            border: "1px solid var(--salryn-border)",
                            fontSize: "0.8125rem",
                          }}
                        >
                          <span>
                            {group.name} · {group.minSelections}–{group.maxSelections} selections
                          </span>
                          <input
                            type="checkbox"
                            aria-label={`Item link: ${group.name}`}
                            checked={linkedGroupIds.includes(group.id)}
                            onChange={(event) =>
                              setLinkedGroupIds((current) =>
                                event.target.checked
                                  ? [...current, group.id]
                                  : current.filter((id) => id !== group.id),
                              )
                            }
                          />
                        </label>
                      ))}
                  </div>
                  <div>
                    <Button
                      variant="secondary"
                      disabled={busy}
                      onClick={() =>
                        void runMutation(
                          `item-links:${selectedItem.productId}:${[...linkedGroupIds].sort().join(",")}`,
                          (operationId) =>
                            setMenuItemModifierGroups(operationId, selectedItem.productId, linkedGroupIds),
                          "Modifier links saved for menu item.",
                        )
                      }
                    >
                      Save item modifier links
                    </Button>
                  </div>
                </div>

                <div
                  style={{
                    display: "grid",
                    gap: 8,
                    paddingTop: 10,
                    borderTop: "1px dashed var(--salryn-border-strong)",
                  }}
                >
                  <div>
                    <h3 style={{ margin: 0, fontSize: "0.875rem", color: "var(--salryn-espresso)" }}>
                      Link groups to selected variant (C1 recovery extension)
                    </h3>
                    <p style={{ margin: "2px 0 0", fontSize: "0.75rem", color: "var(--salryn-mocha)" }}>
                      New bounded operator control wired to PUT /api/menu/variants/&#123;variantId&#125;/modifier-groups.
                    </p>
                  </div>
                  <FormField label="Target variant for modifier links">
                    <SelectInput
                      value={selectedVariantLinkId}
                      onChange={(event) => syncVariantLinksFor(event.target.value)}
                    >
                      {selectedItemVariants.map((v) => (
                        <option key={v.id} value={v.id}>
                          {v.name} ({formatPhp(v.sellingPriceCents)})
                        </option>
                      ))}
                    </SelectInput>
                  </FormField>
                  <div style={{ display: "grid", gap: 6 }}>
                    {catalog.modifierGroups
                      .filter((group) => group.isActive)
                      .map((group: MenuModifierGroup) => (
                        <label
                          key={group.id}
                          style={{
                            display: "flex",
                            alignItems: "center",
                            justifyContent: "space-between",
                            gap: 10,
                            padding: "8px 12px",
                            borderRadius: "var(--salryn-radius-sm)",
                            border: "1px solid var(--salryn-border)",
                            fontSize: "0.8125rem",
                          }}
                        >
                          <span>
                            {group.name} · {group.minSelections}–{group.maxSelections} selections
                          </span>
                          <input
                            type="checkbox"
                            aria-label={`Variant link: ${group.name}`}
                            checked={variantLinkedGroupIds.includes(group.id)}
                            onChange={(event) =>
                              setVariantLinkedGroupIds((current) =>
                                event.target.checked
                                  ? [...current, group.id]
                                  : current.filter((id) => id !== group.id),
                              )
                            }
                          />
                        </label>
                      ))}
                  </div>
                  <div>
                    <Button
                      variant="secondary"
                      disabled={busy || !selectedVariantLinkId}
                      onClick={() =>
                        void runMutation(
                          `variant-links:${selectedVariantLinkId}:${[...variantLinkedGroupIds].sort().join(",")}`,
                          (operationId) =>
                            setMenuVariantModifierGroups(
                              operationId,
                              selectedVariantLinkId,
                              variantLinkedGroupIds,
                            ),
                          "Modifier links saved for variant.",
                        )
                      }
                    >
                      Save variant modifier links
                    </Button>
                  </div>
                </div>
              </section>
            </div>
          ) : null}

          <Alert
            tone="info"
            title="Publication is versioned configuration"
            description="Publishing unchanged content is idempotent. Changed content creates a new immutable revision. A later revision does not rewrite completed sale lines or persisted receipt snapshots."
          />
        </div>
      </CardBody>
    </Card>
  );
}

export type CafeMenuManagerRecoveryPageProps = {
  onOpenWorkstation?: () => void;
  bypassDisposableHostCheck?: boolean;
};

export function CafeMenuManagerRecoveryPage({
  onOpenWorkstation,
  bypassDisposableHostCheck = false,
}: CafeMenuManagerRecoveryPageProps) {
  const [hostGate, setHostGate] = useState<
    | { status: "checking" }
    | { status: "blocked"; reason: string }
    | { status: "allowed"; identity: TestHostIdentity | null }
  >({ status: "checking" });

  const [session, setSession] = useState<HostSession>({ isAuthenticated: false });
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [catalog, setCatalog] = useState<MenuCatalogDraft | null>(null);
  const [units, setUnits] = useState<MenuUnitOption[]>([]);

  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [authError, setAuthError] = useState<string | null>(null);
  const [signingIn, setSigningIn] = useState(false);

  const loadAll = useCallback(async (): Promise<MenuCatalogDraft | null> => {
    setLoadError(null);
    try {
      const currentSession = await fetchSession();
      setSession(currentSession);
      const [draft, unitList] = await Promise.all([fetchMenuCatalog(), fetchProductUnits()]);
      setCatalog(draft);
      setUnits(unitList);
      return draft;
    } catch (err) {
      setCatalog(null);
      setLoadError(describeError(err));
      return null;
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    let active = true;
    async function verifyEnvironment() {
      if (bypassDisposableHostCheck) {
        if (active) setHostGate({ status: "allowed", identity: null });
        await loadAll();
        return;
      }

      try {
        const identity = await fetchTestHostIdentity();
        if (!active) return;
        if (!identity.testHostNonce || isProtectedDatabasePath(identity.databasePath)) {
          setHostGate({
            status: "blocked",
            reason: "Refusing to mount C1 recovery editor against a protected or non-disposable database path.",
          });
          return;
        }
        setHostGate({ status: "allowed", identity });
        await loadAll();
      } catch (err) {
        if (!active) return;
        if (import.meta.env.DEV) {
          setHostGate({ status: "allowed", identity: null });
          await loadAll();
          return;
        }
        setHostGate({
          status: "blocked",
          reason:
            err instanceof HostApiError
              ? `Disposable test host identity endpoint is unavailable (HTTP ${err.status}). The C1 recovery route cannot operate on a production host.`
              : "Disposable test host identity check failed; route remains blocked.",
        });
      }
    }

    void verifyEnvironment();
    return () => {
      active = false;
    };
  }, [bypassDisposableHostCheck, loadAll]);

  async function handleLogin(e: FormEvent) {
    e.preventDefault();
    setAuthError(null);
    setSigningIn(true);
    try {
      const next = await loginOperator(username.trim(), password);
      setSession(next);
      setPassword("");
      setLoading(true);
      await loadAll();
    } catch (err) {
      setAuthError(describeError(err));
    } finally {
      setSigningIn(false);
    }
  }

  async function handleLogout() {
    await logoutOperator().catch(() => undefined);
    setSession({ isAuthenticated: false });
    setCatalog(null);
    setLoading(true);
    await loadAll();
  }

  return (
    <ErrorBoundary>
      <div
        style={{
          minHeight: "100vh",
          background: "var(--salryn-background)",
          color: "var(--salryn-text)",
          padding: "16px",
        }}
      >
        <div style={{ maxWidth: 1200, margin: "0 auto", display: "grid", gap: 16 }}>
          <header
            style={{
              display: "flex",
              flexWrap: "wrap",
              alignItems: "center",
              justifyContent: "space-between",
              gap: 12,
              padding: "12px 16px",
              borderRadius: "var(--salryn-radius-md)",
              background: "var(--salryn-espresso)",
              color: "var(--salryn-cream)",
            }}
          >
            <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
              <BrandMark variant="logo" height={40} decorative />
              <div>
                <h1 style={{ margin: 0, fontSize: "1.125rem", lineHeight: 1.2 }}>
                  Salryn Cafe — Canonical C1 Recovery Proof
                </h1>
                <div style={{ fontSize: "0.75rem", color: "var(--salryn-latte)" }}>
                  Isolated Dev/Disposable Route · Source: Salryn-CafeYza@4241ea1
                </div>
              </div>
            </div>
            <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center", gap: 8 }}>
              <StatusBadge tone="warning">DEBUG / RECOVERY ROUTE</StatusBadge>
              {onOpenWorkstation ? (
                <Button size="sm" variant="secondary" onClick={onOpenWorkstation}>
                  Open Cashier (/cafe/)
                </Button>
              ) : null}
              {session.isAuthenticated && session.user ? (
                <Button size="sm" variant="secondary" onClick={() => void handleLogout()}>
                  Sign out ({session.user.username})
                </Button>
              ) : null}
            </div>
          </header>

          <main style={{ display: "grid", gap: 16 }}>
            {hostGate.status === "checking" ? (
              <LoadingState label="Verifying isolated disposable Host boundary..." />
            ) : hostGate.status === "blocked" ? (
              <Alert
                tone="danger"
                title="C1 recovery route blocked on non-disposable host"
                description={hostGate.reason}
              />
            ) : (
              <>
                {loading && !catalog ? <LoadingState label="Loading C1 menu authority..." /> : null}

                {!(loading && !catalog) && loadError ? (
                  <div style={{ display: "grid", gap: 8 }}>
                    <Alert
                      tone="danger"
                      title="C1 menu catalog unavailable"
                      description={loadError}
                    />
                    <div>
                      <Button size="sm" variant="secondary" onClick={() => void loadAll()}>
                        Retry load
                      </Button>
                    </div>
                  </div>
                ) : null}

                {!session.isAuthenticated ? (
                  <Card>
                    <CardHeader
                      title="Operator Sign In"
                      subtitle="Sign in with a Host operator session to test canonical C1 menu management and server-side RBAC."
                    />
                    <CardBody>
                      <form
                        style={{ display: "grid", gap: 12, maxWidth: 420 }}
                        onSubmit={(e) => void handleLogin(e)}
                      >
                        {authError ? (
                          <Alert tone="danger" title="Sign-in failed" description={authError} />
                        ) : null}
                        <FormField label="Username" required>
                          <TextInput
                            value={username}
                            onChange={(e) => setUsername(e.target.value)}
                            autoComplete="username"
                          />
                        </FormField>
                        <FormField label="Password" required>
                          <TextInput
                            type="password"
                            value={password}
                            onChange={(e) => setPassword(e.target.value)}
                            autoComplete="current-password"
                          />
                        </FormField>
                        <div>
                          <Button type="submit" variant="primary" loading={signingIn}>
                            Sign in
                          </Button>
                        </div>
                      </form>
                    </CardBody>
                  </Card>
                ) : null}

                {!loadError && catalog ? (
                  <CafeMenuManagerRecovery catalog={catalog} units={units} onRefresh={loadAll} />
                ) : null}
              </>
            )}
          </main>
        </div>
      </div>
    </ErrorBoundary>
  );
}
