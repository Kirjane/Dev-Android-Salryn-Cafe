import {
  Coffee,
  PauseCircle,
  PlayCircle,
  Printer,
  Search,
  Send,
  ShoppingBag,
  Trash2,
  XCircle,
} from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import {
  Alert,
  Button,
  Card,
  CardBody,
  CardFooter,
  CardHeader,
  Dialog,
  FormField,
  SelectInput,
  StatusBadge,
  TextInput,
  useToast,
} from "@salryn/cafe-ui";
import { clearPendingPrintOp, formatPhp, getOrCreatePendingPrintOp, HostApiError, parsePhpToCents } from "../api/hostClient";
import { classifyPrintAttemptOutcome, isReceiptStation } from "./types";
import type {
  CafeOrderDetail,
  CafePrintStation,
  CustomerAccount,
  DraftLine,
  MenuItem,
  MenuPublication,
  MenuVariant,
  ServiceMode,
  TenderMode,
  WorkstationApi,
} from "./types";

export type CashierWorkstationProps = {
  api: WorkstationApi;
  menu: MenuPublication | null;
  customers: CustomerAccount[];
  printStations: CafePrintStation[];
  permissions: string[];
  activeOrder: CafeOrderDetail | null;
  lastCompletion: { orderId: string; saleId: string; receiptNumber: string } | null;
  blocked: boolean;
  onDispatchIntent: (path: string, body: Record<string, unknown>) => Promise<boolean>;
  onClearActiveOrder: () => void;
};

export function CashierWorkstation({
  api,
  menu,
  customers,
  printStations,
  permissions,
  activeOrder,
  lastCompletion,
  blocked,
  onDispatchIntent,
  onClearActiveOrder,
}: CashierWorkstationProps) {
  const { notify } = useToast();
  const searchInputRef = useRef<HTMLInputElement>(null);

  const [categoryId, setCategoryId] = useState<string>("ALL");
  const [search, setSearch] = useState("");
  const [serviceMode, setServiceMode] = useState<ServiceMode>("Takeout");
  const [serviceReference, setServiceReference] = useState("");
  const [draftLines, setDraftLines] = useState<DraftLine[]>([]);

  const [configuringItem, setConfiguringItem] = useState<MenuItem | null>(null);
  const [selectedVariantId, setSelectedVariantId] = useState<string>("");
  const [selectedOptionIds, setSelectedOptionIds] = useState<string[]>([]);
  const [lineNote, setLineNote] = useState("");
  const [dialogError, setDialogError] = useState<string | null>(null);

  const [tenderMode, setTenderMode] = useState<TenderMode>("Cash");
  const [cashPhp, setCashPhp] = useState("0");
  const [customerId, setCustomerId] = useState("");
  const [discountPhp, setDiscountPhp] = useState("");
  const [discountReason, setDiscountReason] = useState("");
  const [checkoutError, setCheckoutError] = useState<string | null>(null);

  const [cancelOpen, setCancelOpen] = useState(false);
  const [cancelReason, setCancelReason] = useState("");
  const [printingReceipt, setPrintingReceipt] = useState(false);
  const printLockRef = useRef(false);
  const pendingPrintOpsRef = useRef<Record<string, string>>({});

  useEffect(() => {
    setTenderMode("Cash");
    setCashPhp("0");
    setCustomerId("");
    setDiscountPhp("");
    setDiscountReason("");
    setCheckoutError(null);
  }, [activeOrder?.order.orderId]);

  const permitted = (perm: string) => permissions.includes(perm);

  const receiptStation = useMemo(
    () => printStations.find(isReceiptStation) ?? null,
    [printStations],
  );

  useEffect(() => {
    function handleHotkeys(event: KeyboardEvent) {
      if (event.key === "F2") {
        event.preventDefault();
        searchInputRef.current?.focus();
      }
    }
    window.addEventListener("keydown", handleHotkeys);
    return () => window.removeEventListener("keydown", handleHotkeys);
  }, []);

  const categories = useMemo(
    () => [...(menu?.categories ?? [])].sort((a, b) => a.displayOrder - b.displayOrder),
    [menu],
  );

  const filteredItems = useMemo(() => {
    if (!menu || !Array.isArray(menu.items)) return [];
    const q = search.trim().toLowerCase();
    return [...menu.items]
      .sort((a, b) => a.displayOrder - b.displayOrder)
      .filter((item) => {
        if (categoryId !== "ALL" && item.categoryId !== categoryId) return false;
        if (!q) return true;
        return (
          item.name.toLowerCase().includes(q) ||
          (item.sku ?? "").toLowerCase().includes(q)
        );
      });
  }, [menu, categoryId, search]);

  const itemVariants = useMemo<MenuVariant[]>(() => {
    if (!menu || !configuringItem) return [];
    return menu.variants
      .filter((v) => v.productId === configuringItem.productId)
      .sort((a, b) => a.displayOrder - b.displayOrder);
  }, [menu, configuringItem]);

  const applicableGroups = useMemo(() => {
    if (!menu || !configuringItem) return [];
    const ownerIds = new Set<string>([configuringItem.productId]);
    if (selectedVariantId) ownerIds.add(selectedVariantId);
    const linkedIds = new Set<string>();
    for (const link of menu.itemModifierGroups) {
      if (ownerIds.has(link.ownerId)) linkedIds.add(link.groupId);
    }
    for (const link of menu.variantModifierGroups) {
      if (ownerIds.has(link.ownerId)) linkedIds.add(link.groupId);
    }
    return menu.modifierGroups
      .filter((g) => linkedIds.has(g.groupId))
      .sort((a, b) => a.displayOrder - b.displayOrder);
  }, [menu, configuringItem, selectedVariantId]);

  const serverTotalCents = useMemo(
    () => activeOrder?.currentRevision.lines.reduce((sum, l) => sum + l.lineTotalCents, 0) ?? 0,
    [activeOrder],
  );

  // Display-only payment preview. The Host remains authoritative for final
  // discounts, fees, exact receipt amount, change and AR ledger postings.
  const paymentPreview = useMemo(() => {
    try {
      const discountCents = discountPhp.trim() ? parsePhpToCents(discountPhp) : 0;
      if (discountCents > serverTotalCents) {
        return { valid: false, dueCents: 0, cashCents: 0, changeCents: 0, arCents: 0, missingCents: 0 };
      }
      const dueCents = serverTotalCents - discountCents;
      const cashCents = tenderMode === "AccountsReceivable" ? 0 : parsePhpToCents(cashPhp);
      const arCents = tenderMode === "AccountsReceivable" ? dueCents :
        tenderMode === "Partial" ? Math.max(0, dueCents - cashCents) : 0;
      const changeCents = tenderMode === "Cash" ? Math.max(0, cashCents - dueCents) : 0;
      const missingCents = tenderMode === "Cash" ? Math.max(0, dueCents - cashCents) : 0;
      return {
        valid: !(tenderMode === "Partial" && (cashCents <= 0 || cashCents >= dueCents)),
        dueCents, cashCents, changeCents, arCents, missingCents,
      };
    } catch {
      return { valid: false, dueCents: 0, cashCents: 0, changeCents: 0, arCents: 0, missingCents: 0 };
    }
  }, [serverTotalCents, discountPhp, cashPhp, tenderMode]);

  const draftEstimatedCents = useMemo(
    () => draftLines.reduce((sum, l) => sum + l.unitPriceCents * l.quantity, 0),
    [draftLines],
  );

  function openMenuItem(item: MenuItem) {
    if (blocked || activeOrder || item.isTemporarilyUnavailable) return;
    if (!menu) return;

    const variants = menu.variants.filter((v) => v.productId === item.productId && !v.isTemporarilyUnavailable);
    const hasGroups =
      menu.itemModifierGroups.some((l) => l.ownerId === item.productId) ||
      variants.some((v) => menu.variantModifierGroups.some((l) => l.ownerId === v.variantId));

    if (variants.length === 0 && !hasGroups) {
      setDraftLines((prev) => [
        ...prev,
        {
          orderLineId: crypto.randomUUID(),
          productId: item.productId,
          productName: item.name,
          variantId: null,
          variantName: null,
          modifierOptionIds: [],
          modifierLabels: [],
          quantity: 1,
          unitPriceCents: item.sellingPriceCents,
          note: null,
        },
      ]);
      return;
    }

    setConfiguringItem(item);
    setSelectedVariantId("");
    setSelectedOptionIds([]);
    setLineNote("");
    setDialogError(null);
  }

  function toggleModifierOption(groupId: string, optionId: string, maxSelections: number) {
    if (!menu) return;
    setSelectedOptionIds((prev) => {
      if (prev.includes(optionId)) {
        return prev.filter((id) => id !== optionId);
      }
      const groupOptionIds = new Set(
        menu.modifierOptions.filter((o) => o.groupId === groupId).map((o) => o.optionId),
      );
      const activeInGroup = prev.filter((id) => groupOptionIds.has(id));
      if (maxSelections === 1) {
        return [...prev.filter((id) => !groupOptionIds.has(id)), optionId];
      }
      if (maxSelections > 0 && activeInGroup.length >= maxSelections) {
        return prev;
      }
      return [...prev, optionId];
    });
  }

  function handleConfirmConfiguredItem() {
    if (!menu || !configuringItem) return;
    for (const group of applicableGroups) {
      const groupOptionIds = new Set(
        menu.modifierOptions.filter((o) => o.groupId === group.groupId).map((o) => o.optionId),
      );
      const count = selectedOptionIds.filter((id) => groupOptionIds.has(id)).length;
      const minReq = group.isRequired ? Math.max(1, group.minSelections) : group.minSelections;
      if (count < minReq) {
        setDialogError(`${group.name} requires at least ${minReq} selection(s).`);
        return;
      }
      if (group.maxSelections > 0 && count > group.maxSelections) {
        setDialogError(`${group.name} allows at most ${group.maxSelections} selection(s).`);
        return;
      }
    }

    const chosenVariant = selectedVariantId
      ? menu.variants.find((v) => v.variantId === selectedVariantId) ?? null
      : null;
    const chosenOptions = menu.modifierOptions.filter((o) => selectedOptionIds.includes(o.optionId));
    const basePrice = chosenVariant ? chosenVariant.sellingPriceCents : configuringItem.sellingPriceCents;
    const modsDelta = chosenOptions.reduce((sum, o) => sum + o.priceDeltaCents, 0);

    setDraftLines((prev) => [
      ...prev,
      {
        orderLineId: crypto.randomUUID(),
        productId: configuringItem.productId,
        productName: configuringItem.name,
        variantId: chosenVariant?.variantId ?? null,
        variantName: chosenVariant?.name ?? null,
        modifierOptionIds: chosenOptions.map((o) => o.optionId),
        modifierLabels: chosenOptions.map((o) => o.name),
        quantity: 1,
        unitPriceCents: basePrice + modsDelta,
        note: lineNote.trim() || null,
      },
    ]);
    setConfiguringItem(null);
  }

  function adjustDraftQty(orderLineId: string, delta: number) {
    setDraftLines((prev) =>
      prev
        .map((line) => (line.orderLineId === orderLineId ? { ...line, quantity: line.quantity + delta } : line))
        .filter((line) => line.quantity > 0),
    );
  }

  async function handleSaveOrderRevision() {
    if (draftLines.length === 0 || blocked) return;
    const orderId = crypto.randomUUID();
    const ok = await onDispatchIntent("/api/cafe/orders/revision", {
      operationId: crypto.randomUUID(),
      orderId,
      expectedVersion: 0,
      serviceMode,
      serviceReference: serviceReference.trim() || null,
      lines: draftLines.map((line, index) => ({
        orderLineId: line.orderLineId,
        productId: line.productId,
        variantId: line.variantId,
        modifierOptionIds: line.modifierOptionIds,
        quantityX1000: line.quantity * 1000,
        note: line.note,
        displayOrder: index,
      })),
    });
    if (ok) {
      setDraftLines([]);
    }
  }

  async function handleCompletePayment() {
    if (!activeOrder || blocked) return;
    setCheckoutError(null);
    try {
      const orderDiscountCents = discountPhp.trim() ? parsePhpToCents(discountPhp) : 0;
      if (orderDiscountCents > serverTotalCents) {
        throw new Error("Order discount cannot exceed order subtotal.");
      }
      const netDueCents = serverTotalCents - orderDiscountCents;

      let cashCents = 0;
      if (tenderMode !== "AccountsReceivable") {
        cashCents = parsePhpToCents(cashPhp);
      }

      if (tenderMode === "Partial" && (cashCents <= 0 || cashCents >= netDueCents)) {
        throw new Error("Partial cash must be positive and below the order total.");
      }
      if (tenderMode !== "Cash" && !customerId) {
        throw new Error("Select an active AR customer account.");
      }

      const payments: Array<{ method: "Cash" | "AccountsReceivable"; amountCents: number }> = [];
      if (cashCents > 0) {
        payments.push({ method: "Cash", amountCents: cashCents });
      }
      if (tenderMode !== "Cash") {
        payments.push({ method: "AccountsReceivable", amountCents: netDueCents - cashCents });
      }

      await onDispatchIntent(`/api/cafe/orders/${activeOrder.order.orderId}/complete`, {
        completionOperationId: crypto.randomUUID(),
        saleCommittedOperationId: crypto.randomUUID(),
        financialDecisionOperationId: crypto.randomUUID(),
        financialDecisionId: crypto.randomUUID(),
        expectedVersion: activeOrder.order.orderVersion,
        customerId: tenderMode === "Cash" ? null : customerId,
        orderDiscountCents,
        discountReason: orderDiscountCents > 0 ? discountReason.trim() || "Cashier discount" : null,
        payments,
      });
    } catch (error) {
      setCheckoutError(error instanceof Error ? error.message : "Invalid checkout parameters.");
    }
  }

  async function handlePrintReceipt(saleId: string) {
    if (!receiptStation || printLockRef.current) return;
    printLockRef.current = true;
    setPrintingReceipt(true);
    const opKey = `receipt:${receiptStation.stationId}:${saleId}:Original`;
    const operationId = pendingPrintOpsRef.current[opKey] ?? getOrCreatePendingPrintOp(opKey);
    pendingPrintOpsRef.current[opKey] = operationId;
    try {
      const result = await api.printReceipt({
        operationId,
        stationId: receiptStation.stationId,
        saleId,
        copyKind: "Original",
      });
      delete pendingPrintOpsRef.current[opKey];
      clearPendingPrintOp(opKey);
      const { outcome, detail } = classifyPrintAttemptOutcome(result);
      if (outcome === "succeeded") {
        notify("info", `Receipt print sent · ${receiptStation.name}`, detail ?? undefined);
      } else if (outcome === "disabled") {
        notify("warning", `Printer disabled · ${receiptStation.name}`, detail ?? "Receipt job recorded; enable printer in Store Settings.");
      } else {
        notify("danger", `Receipt print failed · ${receiptStation.name}`, detail ?? "Printer reported a failure.");
      }
    } catch (error) {
      if (error instanceof HostApiError && error.isDeterministicRejection) {
        delete pendingPrintOpsRef.current[opKey];
        clearPendingPrintOp(opKey);
      }
      notify("danger", "Receipt print failed", error instanceof Error ? error.message : "Printer error.");
    } finally {
      printLockRef.current = false;
      setPrintingReceipt(false);
    }
  }

  return (
    <div style={{ display: "grid", gap: 16 }}>
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(auto-fit, minmax(min(100%, 310px), 1fr))",
          gap: 16,
          alignItems: "start",
        }}
      >
        <Card>
          <CardHeader
            title="Published Cafe Menu"
            subtitle={menu ? `Publication v${menu.revisionNumber} · Press F2 to search` : "No published menu available"}
            action={<StatusBadge tone="copper">Cash / AR / Partial Cash+AR</StatusBadge>}
          />
          <CardBody>
            {!menu ? (
              <Alert
                tone="warning"
                title="No Published Menu"
                description="Publish a Cafe Menu catalog revision on the Host before ringing up orders."
              />
            ) : (
              <div style={{ display: "grid", gap: 12 }}>
                <FormField label="Search Cafe Menu (F2)">
                  <input
                    ref={searchInputRef}
                    className="salryn-input salryn-focusable"
                    placeholder="Spanish Latte, Ensaymada, SKU..."
                    value={search}
                    onChange={(e) => setSearch(e.target.value)}
                  />
                </FormField>

                <div role="group" aria-label="Menu categories" style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
                  <Button
                    size="sm"
                    variant={categoryId === "ALL" ? "primary" : "secondary"}
                    aria-pressed={categoryId === "ALL"}
                    onClick={() => setCategoryId("ALL")}
                  >
                    All
                  </Button>
                  {categories.map((cat) => (
                    <Button
                      key={cat.categoryId}
                      size="sm"
                      variant={categoryId === cat.categoryId ? "primary" : "secondary"}
                      aria-pressed={categoryId === cat.categoryId}
                      onClick={() => setCategoryId(cat.categoryId)}
                    >
                      {cat.name}
                    </Button>
                  ))}
                </div>

                <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(165px, 1fr))", gap: 10 }}>
                  {filteredItems.map((item) => (
                    <button
                      key={item.productId}
                      type="button"
                      disabled={blocked || Boolean(activeOrder) || item.isTemporarilyUnavailable}
                      onClick={() => openMenuItem(item)}
                      className="salryn-btn salryn-btn--secondary salryn-focusable"
                      style={{
                        minHeight: 84,
                        flexDirection: "column",
                        alignItems: "flex-start",
                        justifyContent: "space-between",
                        padding: 12,
                        textAlign: "left",
                      }}
                    >
                      <span style={{ display: "flex", alignItems: "center", gap: 6, fontWeight: 700 }}>
                        <Coffee aria-hidden="true" size={15} />
                        {item.name}
                      </span>
                      <span className="salryn-tabular" style={{ fontSize: "0.875rem", color: "var(--salryn-dark-copper)" }}>
                        {item.isTemporarilyUnavailable ? "86'd / Unavailable" : formatPhp(item.sellingPriceCents)}
                      </span>
                    </button>
                  ))}
                  {filteredItems.length === 0 ? (
                    <div style={{ gridColumn: "1 / -1", display: "flex", alignItems: "center", gap: 8, color: "var(--salryn-mocha)" }}>
                      <Search aria-hidden="true" size={16} />
                      <span>No cafe items match &ldquo;{search}&rdquo;.</span>
                    </div>
                  ) : null}
                </div>
              </div>
            )}
          </CardBody>
        </Card>

        <Card>
          {!activeOrder ? (
            <>
              <CardHeader
                title="New Order Ticket"
                subtitle="Save order revision to Host SQLite before sending to preparation."
                action={<StatusBadge tone="info">{draftLines.reduce((s, l) => s + l.quantity, 0)} items</StatusBadge>}
              />
              <CardBody>
                <div style={{ display: "grid", gap: 12 }}>
                  <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(140px, 1fr))", gap: 10 }}>
                    <FormField label="Service mode">
                      <SelectInput
                        value={serviceMode}
                        onChange={(e) => setServiceMode(e.target.value as ServiceMode)}
                      >
                        <option value="Takeout">Takeout</option>
                        <option value="DineIn">DineIn</option>
                      </SelectInput>
                    </FormField>
                    <FormField label="Table / reference">
                      <TextInput
                        placeholder={serviceMode === "DineIn" ? "Table 1" : "Walk-in / Tag"}
                        value={serviceReference}
                        onChange={(e) => setServiceReference(e.target.value)}
                      />
                    </FormField>
                  </div>

                  {draftLines.length === 0 ? (
                    <div
                      style={{
                        padding: "18px 12px",
                        textAlign: "center",
                        color: "var(--salryn-mocha)",
                        border: "1px dashed var(--salryn-border-strong)",
                        borderRadius: 10,
                      }}
                    >
                      <ShoppingBag aria-hidden="true" size={20} style={{ marginBottom: 6 }} />
                      <div>Tap a menu item on the left to start an order.</div>
                    </div>
                  ) : (
                    <ul style={{ listStyle: "none", margin: 0, padding: 0, display: "grid", gap: 8 }}>
                      {draftLines.map((line) => (
                        <li
                          key={line.orderLineId}
                          style={{
                            display: "flex",
                            flexWrap: "wrap",
                            justifyContent: "space-between",
                            alignItems: "center",
                            gap: 8,
                            padding: 10,
                            borderRadius: 8,
                            background: "var(--salryn-surface-subtle)",
                            border: "1px solid var(--salryn-border)",
                          }}
                        >
                          <div style={{ minWidth: 0 }}>
                            <div style={{ fontWeight: 700 }}>
                              {line.productName}
                              {line.variantName ? ` (${line.variantName})` : ""}
                            </div>
                            {line.modifierLabels.length > 0 ? (
                              <div style={{ fontSize: "0.75rem", color: "var(--salryn-mocha)" }}>
                                {line.modifierLabels.join(", ")}
                              </div>
                            ) : null}
                            {line.note ? (
                              <div style={{ fontSize: "0.75rem", color: "var(--salryn-dark-copper)" }}>
                                Note: {line.note}
                              </div>
                            ) : null}
                          </div>
                          <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
                            <Button
                              size="sm"
                              variant="secondary"
                              aria-label={`Decrease ${line.productName}`}
                              onClick={() => adjustDraftQty(line.orderLineId, -1)}
                            >
                              −
                            </Button>
                            <span className="salryn-tabular" style={{ minWidth: 20, textAlign: "center", fontWeight: 700 }}>
                              {line.quantity}
                            </span>
                            <Button
                              size="sm"
                              variant="secondary"
                              aria-label={`Increase ${line.productName}`}
                              onClick={() => adjustDraftQty(line.orderLineId, 1)}
                            >
                              +
                            </Button>
                            <span className="salryn-tabular" style={{ minWidth: 72, textAlign: "right", fontWeight: 700 }}>
                              {formatPhp(line.unitPriceCents * line.quantity)}
                            </span>
                          </div>
                        </li>
                      ))}
                    </ul>
                  )}

                  <div
                    className="salryn-tabular"
                    style={{
                      padding: 12,
                      borderRadius: 10,
                      background: "var(--salryn-surface-subtle)",
                      border: "1px solid var(--salryn-border-strong)",
                      display: "flex",
                      justifyContent: "space-between",
                      fontWeight: 800,
                    }}
                  >
                    <span>Estimated Subtotal</span>
                    <span>{formatPhp(draftEstimatedCents)}</span>
                  </div>
                </div>
              </CardBody>
              <CardFooter>
                <Button
                  variant="ghost"
                  leftIcon={<Trash2 size={16} />}
                  disabled={draftLines.length === 0 || blocked}
                  onClick={() => setDraftLines([])}
                >
                  Clear Draft
                </Button>
                <Button
                  variant="primary"
                  size="lg"
                  disabled={blocked || draftLines.length === 0 || !permitted("Orders.Create")}
                  onClick={() => void handleSaveOrderRevision()}
                >
                  Save order
                </Button>
              </CardFooter>
            </>
          ) : (
            <>
              <CardHeader
                title={`Current order · ${activeOrder.order.state}`}
                subtitle={`${activeOrder.order.serviceMode} · ${activeOrder.order.serviceReference || "Walk-in"} · v${activeOrder.order.orderVersion}`}
                action={<StatusBadge tone="copper">Server Authoritative</StatusBadge>}
              />
              <CardBody>
                <div style={{ display: "grid", gap: 12 }}>
                  <ul style={{ listStyle: "none", margin: 0, padding: 0, display: "grid", gap: 8 }}>
                    {activeOrder.currentRevision.lines.map((line) => (
                      <li
                        key={line.orderLineId}
                        style={{
                          display: "flex",
                          justifyContent: "space-between",
                          gap: 8,
                          padding: 10,
                          borderRadius: 8,
                          background: "var(--salryn-surface-subtle)",
                        }}
                      >
                        <div>
                          <div style={{ fontWeight: 700 }}>
                            {Math.max(1, Math.round(line.quantityX1000 / 1000))}× {line.productNameSnapshot}
                            {line.variantNameSnapshot ? ` (${line.variantNameSnapshot})` : ""}
                          </div>
                          {line.modifierSelections.length > 0 ? (
                            <div style={{ fontSize: "0.75rem", color: "var(--salryn-mocha)" }}>
                              {line.modifierSelections.map((m) => m.optionNameSnapshot).join(", ")}
                            </div>
                          ) : null}
                          {line.note ? (
                            <div style={{ fontSize: "0.75rem", color: "var(--salryn-dark-copper)" }}>
                              Note: {line.note}
                            </div>
                          ) : null}
                        </div>
                        <span className="salryn-tabular" style={{ fontWeight: 700 }}>
                          {formatPhp(line.lineTotalCents)}
                        </span>
                      </li>
                    ))}
                  </ul>

                  <div
                    className="salryn-tabular"
                    style={{
                      padding: 12,
                      borderRadius: 10,
                      background: "var(--salryn-surface-subtle)",
                      border: "1px solid var(--salryn-border-strong)",
                      display: "flex",
                      justifyContent: "space-between",
                      fontSize: "1.125rem",
                      fontWeight: 800,
                    }}
                  >
                    <span>Confirmed order total</span>
                    <span>{formatPhp(serverTotalCents)}</span>
                  </div>

                  {activeOrder.order.state === "Ready" ? (
                    <div style={{ display: "grid", gap: 12, paddingTop: 6, borderTop: "1px solid var(--salryn-border)" }}>
                      {checkoutError ? (
                        <Alert tone="danger" title="Checkout Validation" description={checkoutError} />
                      ) : null}
                      <FormField label="Tender">
                        <SelectInput
                          value={tenderMode}
                          onChange={(e) => setTenderMode(e.target.value as TenderMode)}
                        >
                          <option value="Cash">Cash</option>
                          <option value="AccountsReceivable" disabled={!permitted("Receivables.CreateCreditSale")}>
                            Accounts Receivable
                          </option>
                          <option value="Partial" disabled={!permitted("Receivables.CreateCreditSale")}>
                            Partial Cash + AR
                          </option>
                        </SelectInput>
                      </FormField>

                      <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(140px, 1fr))", gap: 10 }}>
                        <FormField label="Order discount (PHP)">
                          <TextInput
                            inputMode="decimal"
                            placeholder="0.00"
                            value={discountPhp}
                            onChange={(e) => setDiscountPhp(e.target.value)}
                          />
                        </FormField>
                        {discountPhp.trim() ? (
                          <FormField label="Discount reason">
                            <TextInput
                              placeholder="Senior / Promo"
                              value={discountReason}
                              onChange={(e) => setDiscountReason(e.target.value)}
                            />
                          </FormField>
                        ) : null}
                      </div>

                      {tenderMode !== "AccountsReceivable" ? (
                        <FormField label="Cash amount (PHP)">
                          <TextInput
                            inputMode="decimal"
                            value={cashPhp}
                            onChange={(e) => setCashPhp(e.target.value)}
                          />
                        </FormField>
                      ) : null}

                      <div aria-live="polite" style={{
                        display: "grid", gap: 6, border: "1px solid var(--salryn-border-strong)",
                        borderRadius: 10, padding: 14, background: "var(--salryn-surface-subtle)",
                      }}>
                        <div style={{ display: "flex", justifyContent: "space-between", gap: 12, fontSize: "1.2rem", fontWeight: 800 }}>
                          <span>Estimated due (before fees)</span>
                          <span className="salryn-tabular">{paymentPreview.valid ? formatPhp(paymentPreview.dueCents) : "Check amount"}</span>
                        </div>
                        {tenderMode !== "AccountsReceivable" ? (
                          <div style={{ display: "flex", justifyContent: "space-between", gap: 12 }}>
                            <span>Cash received</span>
                            <strong className="salryn-tabular">{paymentPreview.valid ? formatPhp(paymentPreview.cashCents) : "—"}</strong>
                          </div>
                        ) : null}
                        {tenderMode === "Cash" && paymentPreview.valid ? (
                          <div style={{ display: "flex", justifyContent: "space-between", gap: 12, fontWeight: 800 }}>
                            <span>{paymentPreview.missingCents ? "Still to collect (estimated)" : "Estimated change (before fees)"}</span>
                            <span className="salryn-tabular">{formatPhp(paymentPreview.missingCents || paymentPreview.changeCents)}</span>
                          </div>
                        ) : null}
                        {tenderMode !== "Cash" && paymentPreview.valid ? (
                          <div style={{ display: "flex", justifyContent: "space-between", gap: 12, fontWeight: 800 }}>
                            <span>Estimated customer account charge</span>
                            <span className="salryn-tabular">{formatPhp(paymentPreview.arCents)}</span>
                          </div>
                        ) : null}
                        <p style={{ margin: "2px 0 0", fontSize: 12 }}>
                          Estimate from saved order lines and discount only. Host-calculated fees,
                          official totals, and final change are determined by the backend receipt.
                        </p>
                        {!paymentPreview.valid ? <Alert tone="warning" title="Check payment amounts" description="Enter a valid discount and tender amount before confirming." /> : null}
                      </div>

                      {tenderMode !== "Cash" ? (
                        <FormField label="Customer" required>
                          <SelectInput value={customerId} onChange={(e) => setCustomerId(e.target.value)}>
                            <option value="">Choose customer</option>
                            {customers.map((c) => (
                              <option key={c.id} value={c.id}>
                                {c.fullName}
                              </option>
                            ))}
                          </SelectInput>
                        </FormField>
                      ) : null}
                    </div>
                  ) : null}

                  {activeOrder.order.state === "Completed" && lastCompletion && lastCompletion.orderId === activeOrder.order.orderId ? (
                    <Alert
                      tone="success"
                      title={`Payment confirmed · Receipt ${lastCompletion.receiptNumber}`}
                      description={`Sale ID: ${lastCompletion.saleId}`}
                    />
                  ) : null}
                </div>
              </CardBody>
              <CardFooter>
                {activeOrder.order.state === "Open" ? (
                  <>
                    <Button
                      variant="accent"
                      leftIcon={<Send size={15} />}
                      disabled={blocked || !permitted("Preparation.Manage")}
                      onClick={() =>
                        void onDispatchIntent(`/api/cafe/orders/${activeOrder.order.orderId}/submit`, {
                          operationId: crypto.randomUUID(),
                          expectedVersion: activeOrder.order.orderVersion,
                        })
                      }
                    >
                      Send to preparation
                    </Button>
                    <Button
                      variant="secondary"
                      leftIcon={<PauseCircle size={15} />}
                      disabled={blocked || !permitted("Orders.Manage")}
                      onClick={() =>
                        void onDispatchIntent(`/api/cafe/orders/${activeOrder.order.orderId}/park`, {
                          operationId: crypto.randomUUID(),
                          expectedVersion: activeOrder.order.orderVersion,
                        })
                      }
                    >
                      Park order
                    </Button>
                    {permitted("Orders.Cancel") ? (
                      <Button
                        variant="danger"
                        leftIcon={<XCircle size={15} />}
                        disabled={blocked}
                        onClick={() => {
                          setCancelReason("");
                          setCancelOpen(true);
                        }}
                      >
                        Cancel order
                      </Button>
                    ) : null}
                  </>
                ) : null}

                {activeOrder.order.state === "Parked" ? (
                  <Button
                    variant="accent"
                    leftIcon={<PlayCircle size={15} />}
                    disabled={blocked || !permitted("Orders.Manage")}
                    onClick={() =>
                      void onDispatchIntent(`/api/cafe/orders/${activeOrder.order.orderId}/resume`, {
                        operationId: crypto.randomUUID(),
                        expectedVersion: activeOrder.order.orderVersion,
                      })
                    }
                  >
                    Resume order
                  </Button>
                ) : null}

                {(activeOrder.order.state === "Submitted" || activeOrder.order.state === "Preparing") && permitted("Preparation.Manage") ? (
                  <Button
                    variant="accent"
                    disabled={blocked}
                    onClick={() =>
                      void onDispatchIntent(`/api/cafe/orders/${activeOrder.order.orderId}/preparation`, {
                        operationId: crypto.randomUUID(),
                        expectedVersion: activeOrder.order.orderVersion,
                        targetState: activeOrder.order.state === "Submitted" ? "Preparing" : "Ready",
                      })
                    }
                  >
                    {activeOrder.order.state === "Submitted" ? "Mark preparing" : "Mark ready"}
                  </Button>
                ) : null}

                {activeOrder.order.state === "Ready" ? (
                  <Button
                    variant="primary"
                    size="lg"
                    disabled={
                      blocked ||
                      !permitted("Orders.Complete") ||
                      !permitted("Sales.Create") ||
                      (tenderMode !== "Cash" && !customerId) ||
                      !paymentPreview.valid ||
                      (tenderMode === "Cash" && paymentPreview.missingCents > 0)
                    }
                    onClick={() => void handleCompletePayment()}
                  >
                    Complete payment
                  </Button>
                ) : null}

                {activeOrder.order.state === "Completed" && activeOrder.order.completedSaleId && receiptStation && permitted("Printing.Print") ? (
                  <Button
                    variant="secondary"
                    leftIcon={<Printer size={15} />}
                    loading={printingReceipt}
                    onClick={() => void handlePrintReceipt(activeOrder.order.completedSaleId!)}
                  >
                    Print Receipt
                  </Button>
                ) : null}

                <Button variant="ghost" disabled={blocked} onClick={onClearActiveOrder}>
                  Start another order
                </Button>
              </CardFooter>
            </>
          )}
        </Card>
      </div>

      <Dialog
        open={Boolean(configuringItem)}
        title={configuringItem ? `Configure ${configuringItem.name}` : "Configure Item"}
        description="Choose variant and modifier options defined by the published Host menu."
        onClose={() => setConfiguringItem(null)}
        footer={
          <>
            <Button variant="secondary" onClick={() => setConfiguringItem(null)}>
              Cancel
            </Button>
            <Button variant="primary" onClick={handleConfirmConfiguredItem}>
              Add to Ticket
            </Button>
          </>
        }
      >
        {configuringItem && menu ? (
          <div style={{ display: "grid", gap: 14 }}>
            {dialogError ? <Alert tone="danger" title="Selection Required" description={dialogError} /> : null}
            {itemVariants.length > 0 ? (
              <FormField label="Variant">
                <SelectInput
                  value={selectedVariantId}
                  onChange={(e) => {
                    const nextVarId = e.target.value;
                    setSelectedVariantId(nextVarId);
                    const ownerIds = new Set<string>([configuringItem.productId]);
                    if (nextVarId) ownerIds.add(nextVarId);
                    const validGroupIds = new Set<string>();
                    for (const l of menu.itemModifierGroups) {
                      if (ownerIds.has(l.ownerId)) validGroupIds.add(l.groupId);
                    }
                    for (const l of menu.variantModifierGroups) {
                      if (ownerIds.has(l.ownerId)) validGroupIds.add(l.groupId);
                    }
                    const validOptionIds = new Set(
                      menu.modifierOptions.filter((o) => validGroupIds.has(o.groupId)).map((o) => o.optionId),
                    );
                    setSelectedOptionIds((prev) => prev.filter((id) => validOptionIds.has(id)));
                  }}
                >
                  <option value="">Standard ({formatPhp(configuringItem.sellingPriceCents)})</option>
                  {itemVariants.map((v) => (
                    <option key={v.variantId} value={v.variantId} disabled={v.isTemporarilyUnavailable}>
                      {v.name} ({formatPhp(v.sellingPriceCents)})
                    </option>
                  ))}
                </SelectInput>
              </FormField>
            ) : null}

            {applicableGroups.map((group) => {
              const opts = menu.modifierOptions
                .filter((o) => o.groupId === group.groupId)
                .sort((a, b) => a.displayOrder - b.displayOrder);
              return (
                <fieldset
                  key={group.groupId}
                  style={{
                    border: "1px solid var(--salryn-border-strong)",
                    borderRadius: 8,
                    padding: 12,
                    display: "grid",
                    gap: 8,
                  }}
                >
                  <legend style={{ fontWeight: 700, padding: "0 6px" }}>
                    {group.name} · choose {group.minSelections}–{group.maxSelections || "any"}
                  </legend>
                  {opts.map((opt) => (
                    <label key={opt.optionId} style={{ display: "flex", alignItems: "center", gap: 8, cursor: "pointer" }}>
                      <input
                        type="checkbox"
                        checked={selectedOptionIds.includes(opt.optionId)}
                        onChange={() => toggleModifierOption(group.groupId, opt.optionId, group.maxSelections)}
                      />
                      <span>
                        {opt.name}
                        {opt.priceDeltaCents > 0 ? ` (+${formatPhp(opt.priceDeltaCents)})` : ""}
                      </span>
                    </label>
                  ))}
                </fieldset>
              );
            })}

            <FormField label="Barista / Kitchen Note">
              <TextInput
                placeholder="Less ice, extra hot..."
                value={lineNote}
                onChange={(e) => setLineNote(e.target.value)}
              />
            </FormField>
          </div>
        ) : null}
      </Dialog>

      <Dialog
        open={cancelOpen}
        title="Cancel Current Order?"
        description="Audited cancellation requires a brief reason."
        onClose={() => setCancelOpen(false)}
        footer={
          <>
            <Button variant="secondary" onClick={() => setCancelOpen(false)}>
              Back
            </Button>
            <Button
              variant="danger"
              onClick={() => {
                if (!activeOrder || !cancelReason.trim()) {
                  notify("warning", "Audit reason required");
                  return;
                }
                const reason = cancelReason.trim();
                setCancelOpen(false);
                void onDispatchIntent(`/api/cafe/orders/${activeOrder.order.orderId}/cancel`, {
                  operationId: crypto.randomUUID(),
                  expectedVersion: activeOrder.order.orderVersion,
                  reason,
                });
              }}
            >
              Confirm Cancellation
            </Button>
          </>
        }
      >
        <FormField label="Cancellation Reason" required>
          <TextInput value={cancelReason} onChange={(e) => setCancelReason(e.target.value)} />
        </FormField>
      </Dialog>
    </div>
  );
}
