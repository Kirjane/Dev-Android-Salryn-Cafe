import {
  AlertTriangle,
  Coffee,
  Printer,
  RefreshCw,
  RotateCcw,
  Search,
  ShoppingBag,
  Trash2,
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
import {
  MOCK_AR_ACCOUNTS,
  MOCK_MENU_ITEMS,
  type CafeWorkstationClient,
} from "./mockCafeClient";
import {
  formatPhp,
  UX_B_DATA_SOURCE,
  type BlindCloseResult,
  type CafeMenuItem,
  type CartLine,
  type CommittedReceipt,
  type DiscountKind,
  type ModifierOption,
  type ServiceChannel,
  type StationTrustState,
  type TenderMode,
} from "./types";
import { useDurableOperation } from "./useDurableOperation";

export type CashierWorkstationProps = {
  client: CafeWorkstationClient;
  trustState: StationTrustState;
  onCommittedSale?: (receipt: CommittedReceipt) => void;
};

const CHANNEL_LABELS: Record<ServiceChannel, string> = {
  DINE_IN: "Dine-In",
  TAKEOUT: "Takeout",
  DELIVERY: "Delivery",
};

const TENDER_LABELS: Record<TenderMode, string> = {
  CASH: "Cash Only",
  AR: "AR Account",
  PARTIAL_CASH_AR: "Partial Cash + AR",
};

export function CashierWorkstation({
  client,
  trustState,
  onCommittedSale,
}: CashierWorkstationProps) {
  const { notify } = useToast();
  const searchInputRef = useRef<HTMLInputElement>(null);

  const [category, setCategory] = useState<string>("All");
  const [search, setSearch] = useState("");
  const [channel, setChannel] = useState<ServiceChannel>("DINE_IN");
  const [tableOrRef, setTableOrRef] = useState("Table 1");
  const [discount, setDiscount] = useState<DiscountKind>("NONE");
  const [tenderMode, setTenderMode] = useState<TenderMode>("CASH");
  const [cashTenderedPhp, setCashTenderedPhp] = useState("");
  const [arChargePhp, setArChargePhp] = useState("");
  const [arCustomerId, setArCustomerId] = useState(MOCK_AR_ACCOUNTS[0].id);

  const [cart, setCart] = useState<CartLine[]>([]);
  const [configuringItem, setConfiguringItem] = useState<CafeMenuItem | null>(null);
  const [selectedModsByGroup, setSelectedModsByGroup] = useState<Record<string, ModifierOption>>({});
  const [lineNote, setLineNote] = useState("");

  const [lastReceipt, setLastReceipt] = useState<CommittedReceipt | null>(null);
  const [refundOpen, setRefundOpen] = useState(false);
  const [refundReceiptNum, setRefundReceiptNum] = useState("CY-1041");
  const [refundAmountPhp, setRefundAmountPhp] = useState("165.00");
  const [refundReason, setRefundReason] = useState("");

  const [blindCloseOpen, setBlindCloseOpen] = useState(false);
  const [blindCountPhp, setBlindCountPhp] = useState("");
  const [blindCloseResult, setBlindCloseResult] = useState<BlindCloseResult | null>(null);

  const checkoutOp = useDurableOperation("sale");
  const refundOp = useDurableOperation("refund");
  const blindCloseOp = useDurableOperation("blindclose");
  const reprintOp = useDurableOperation("reprint");

  const stationBlocked = trustState === "OFFLINE_LEASE_EXPIRED" || trustState === "REVOKED";

  const quote = useMemo(() => client.quoteCart(cart, discount), [client, cart, discount]);

  const filteredMenu = useMemo(() => {
    const q = search.trim().toLowerCase();
    return MOCK_MENU_ITEMS.filter((item) => {
      if (category !== "All" && item.category !== category) return false;
      if (!q) return true;
      return item.name.toLowerCase().includes(q) || item.sku.toLowerCase().includes(q);
    });
  }, [category, search]);

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

  function openItem(item: CafeMenuItem) {
    if (item.modifierGroups.length === 0) {
      addLineToCart(item, [], "");
      return;
    }
    const defaults: Record<string, ModifierOption> = {};
    for (const group of item.modifierGroups) {
      if (group.options.length > 0) {
        defaults[group.id] = group.options[0];
      }
    }
    setSelectedModsByGroup(defaults);
    setLineNote("");
    setConfiguringItem(item);
  }

  function addLineToCart(item: CafeMenuItem, modifiers: ModifierOption[], note: string) {
    checkoutOp.resetIntent();
    setCart((prev) => [
      ...prev,
      {
        lineId: `${item.id}-${Date.now()}-${Math.random().toString(36).slice(2, 6)}`,
        item,
        quantity: 1,
        selectedModifiers: modifiers,
        note: note.trim() || undefined,
      },
    ]);
  }

  function updateQty(lineId: string, delta: number) {
    checkoutOp.resetIntent();
    setCart((prev) =>
      prev
        .map((line) => (line.lineId === lineId ? { ...line, quantity: line.quantity + delta } : line))
        .filter((line) => line.quantity > 0),
    );
  }

  function parsePhpToCents(raw: string): number {
    const num = Number(raw);
    if (!Number.isFinite(num) || num < 0) return 0;
    return Math.round(num * 100);
  }

  async function handleCheckoutSubmit() {
    if (stationBlocked) return;

    const cashCents =
      tenderMode === "AR"
        ? 0
        : cashTenderedPhp.trim()
          ? parsePhpToCents(cashTenderedPhp)
          : tenderMode === "CASH"
            ? quote.totalDueCents
            : 0;

    const arCents =
      tenderMode === "CASH"
        ? 0
        : tenderMode === "AR"
          ? quote.totalDueCents
          : parsePhpToCents(arChargePhp);

    const committed = await checkoutOp.execute((operationId) =>
      client.submitSale({
        operationId,
        channel,
        tableOrRef,
        discount,
        tenderMode,
        cashTenderedCents: cashCents,
        arChargeCents: arCents,
        arCustomerId: tenderMode === "CASH" ? undefined : arCustomerId,
        lines: cart,
      }),
    );

    if (committed) {
      setLastReceipt(committed);
      setCart([]);
      setCashTenderedPhp("");
      setArChargePhp("");
      onCommittedSale?.(committed);
      notify(
        "success",
        committed.replayedIdempotently
          ? `Recovered receipt ${committed.receiptNumber}`
          : `Sale committed · ${committed.receiptNumber}`,
        `Kitchen ticket ${committed.kitchenTicketNumber} queued (${formatPhp(committed.totalDueCents)}).`,
      );
    }
  }

  async function handleReprintReceipt() {
    if (!lastReceipt) return;
    const res = await reprintOp.execute((operationId) =>
      client.reprintDocument({ operationId, kind: "RECEIPT", reference: lastReceipt.receiptNumber }),
    );
    if (res?.printed) {
      notify("info", `Reprint sent · ${res.reference}`, "Thermal receipt job queued.");
    }
  }

  async function handleRefundSubmit() {
    const res = await refundOp.execute((operationId) =>
      client.submitRefund({
        operationId,
        receiptNumber: refundReceiptNum,
        refundedCents: parsePhpToCents(refundAmountPhp),
        reason: refundReason,
      }),
    );
    if (res) {
      setRefundOpen(false);
      setRefundReason("");
      notify("warning", `Refund recorded · ${res.receiptNumber}`, `${formatPhp(res.refundedCents)} (${res.operationId})`);
    }
  }

  async function handleBlindCloseSubmit() {
    const res = await blindCloseOp.execute((operationId) =>
      client.submitBlindClose({
        operationId,
        countedCashCents: parsePhpToCents(blindCountPhp),
      }),
    );
    if (res) {
      setBlindCloseResult(res);
      notify("info", "Blind shift close submitted", `Variance: ${formatPhp(res.varianceCents)}`);
    }
  }

  return (
    <div style={{ display: "grid", gap: 16 }}>
      <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center", justifyContent: "space-between", gap: 10 }}>
        <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center", gap: 8 }}>
          <StatusBadge tone="copper">{UX_B_DATA_SOURCE}</StatusBadge>
          <StatusBadge tone="neutral">Cash / AR / Partial Cash+AR Only</StatusBadge>
          {checkoutOp.pendingOperationId ? (
            <StatusBadge tone="info">Intent ID: {checkoutOp.pendingOperationId}</StatusBadge>
          ) : null}
        </div>
        <div style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
          <Button variant="secondary" leftIcon={<RotateCcw size={16} />} onClick={() => setRefundOpen(true)}>
            Audited Refund
          </Button>
          <Button variant="secondary" onClick={() => { setBlindCloseResult(null); setBlindCloseOpen(true); }}>
            Blind Shift Close
          </Button>
        </div>
      </div>

      {trustState === "OFFLINE_LEASE_VALID" ? (
        <Alert
          tone="warning"
          title="Offline Lease Active (Local Queue Mode)"
          description="Station is operating offline under a valid signed lease. Committed sales are fenced with durable operation IDs for central sync."
        />
      ) : null}

      {stationBlocked ? (
        <Alert
          tone="danger"
          title={trustState === "REVOKED" ? "Terminal Revoked — Checkout Locked" : "Offline Lease Expired — Renewal Required"}
          description="New checkout mutations are blocked until device trust is restored with the Salryn Cafe Host or Central authority."
        />
      ) : null}

      {checkoutOp.unknownOutcome ? (
        <Card>
          <CardBody>
            <Alert
              tone="warning"
              title={`Unknown Checkout Outcome · Operation ${checkoutOp.unknownOutcome.operationId}`}
              description={checkoutOp.unknownOutcome.message}
            />
            <div style={{ marginTop: 12, display: "flex", flexWrap: "wrap", gap: 10 }}>
              <Button
                variant="accent"
                leftIcon={<RefreshCw size={16} />}
                loading={checkoutOp.inFlight}
                onClick={() => void handleCheckoutSubmit()}
              >
                Retry &amp; Verify Same Operation ID
              </Button>
            </div>
          </CardBody>
        </Card>
      ) : null}

      {checkoutOp.error ? (
        <Alert tone="danger" title="Checkout Rejected" description={checkoutOp.error} />
      ) : null}

      <div style={{ display: "grid", gridTemplateColumns: "minmax(0, 1.4fr) minmax(320px, 1fr)", gap: 16, alignItems: "start" }}>
        <Card>
          <CardHeader
            title="Cafe Menu & Modifiers"
            subtitle="Touch-first >= 48px tiles. Press F2 to focus menu search."
          />
          <CardBody>
            <div style={{ display: "grid", gap: 12 }}>
              <FormField label="Search Cafe Menu (F2)">
                <input
                  ref={searchInputRef}
                  className="salryn-input salryn-focusable"
                  placeholder="Spanish Latte, Ensaymada, ESP-01..."
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                />
              </FormField>

              <div role="group" aria-label="Menu categories" style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
                {["All", "Espresso", "Cold Brew & Tea", "Pastries", "All-Day Meals"].map((cat) => (
                  <Button
                    key={cat}
                    size="sm"
                    variant={category === cat ? "primary" : "secondary"}
                    aria-pressed={category === cat}
                    onClick={() => setCategory(cat)}
                  >
                    {cat}
                  </Button>
                ))}
              </div>

              <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(175px, 1fr))", gap: 10 }}>
                {filteredMenu.map((item) => (
                  <button
                    key={item.id}
                    type="button"
                    onClick={() => openItem(item)}
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
                      {formatPhp(item.basePriceCents)}
                      {item.modifierGroups.length > 0 ? " · Modifiers" : ""}
                    </span>
                  </button>
                ))}
                {filteredMenu.length === 0 ? (
                  <div style={{ gridColumn: "1 / -1", display: "flex", alignItems: "center", gap: 8, color: "var(--salryn-mocha)" }}>
                    <Search aria-hidden="true" size={16} />
                    <span>No cafe items match "{search}".</span>
                  </div>
                ) : null}
              </div>
            </div>
          </CardBody>
        </Card>

        <Card>
          <CardHeader
            title="Current Cafe Ticket"
            subtitle="Server-authoritative totals (Cash, AR, or Partial Cash + AR)."
            action={<StatusBadge tone="info">{cart.reduce((s, l) => s + l.quantity, 0)} items</StatusBadge>}
          />
          <CardBody>
            <div style={{ display: "grid", gap: 12 }}>
              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 10 }}>
                <FormField label="Service Channel">
                  <SelectInput
                    value={channel}
                    onChange={(e) => {
                      const next = e.target.value as ServiceChannel;
                      setChannel(next);
                      setTableOrRef(next === "DINE_IN" ? "Table 1" : next === "TAKEOUT" ? "Walk-in" : "Rider Ref");
                    }}
                  >
                    {(Object.keys(CHANNEL_LABELS) as ServiceChannel[]).map((ch) => (
                      <option key={ch} value={ch}>
                        {CHANNEL_LABELS[ch]}
                      </option>
                    ))}
                  </SelectInput>
                </FormField>

                <FormField label={channel === "DINE_IN" ? "Table Number" : channel === "DELIVERY" ? "Delivery Ref" : "Customer / Tag"}>
                  <TextInput value={tableOrRef} onChange={(e) => setTableOrRef(e.target.value)} />
                </FormField>
              </div>

              {cart.length === 0 ? (
                <div style={{ padding: "18px 12px", textAlign: "center", color: "var(--salryn-mocha)", border: "1px dashed var(--salryn-border-strong)", borderRadius: 10 }}>
                  <ShoppingBag aria-hidden="true" size={20} style={{ marginBottom: 6 }} />
                  <div>Tap any cafe item to start an order.</div>
                </div>
              ) : (
                <ul style={{ listStyle: "none", margin: 0, padding: 0, display: "grid", gap: 8 }}>
                  {cart.map((line) => {
                    const modSum = line.selectedModifiers.reduce((a, m) => a + m.priceDeltaCents, 0);
                    const lineTotal = (line.item.basePriceCents + modSum) * line.quantity;
                    return (
                      <li
                        key={line.lineId}
                        style={{
                          display: "flex",
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
                          <div style={{ fontWeight: 700 }}>{line.item.name}</div>
                          {line.selectedModifiers.length > 0 ? (
                            <div style={{ fontSize: "0.75rem", color: "var(--salryn-mocha)" }}>
                              {line.selectedModifiers.map((m) => m.label).join(", ")}
                            </div>
                          ) : null}
                          {line.note ? (
                            <div style={{ fontSize: "0.75rem", color: "var(--salryn-dark-copper)" }}>Note: {line.note}</div>
                          ) : null}
                        </div>
                        <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
                          <Button size="sm" variant="secondary" aria-label={`Decrease ${line.item.name}`} onClick={() => updateQty(line.lineId, -1)}>
                            −
                          </Button>
                          <span className="salryn-tabular" style={{ minWidth: 20, textAlign: "center", fontWeight: 700 }}>
                            {line.quantity}
                          </span>
                          <Button size="sm" variant="secondary" aria-label={`Increase ${line.item.name}`} onClick={() => updateQty(line.lineId, 1)}>
                            +
                          </Button>
                          <span className="salryn-tabular" style={{ minWidth: 72, textAlign: "right", fontWeight: 700 }}>
                            {formatPhp(lineTotal)}
                          </span>
                        </div>
                      </li>
                    );
                  })}
                </ul>
              )}

              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 10 }}>
                <FormField label="Discount Policy">
                  <SelectInput
                    value={discount}
                    onChange={(e) => {
                      checkoutOp.resetIntent();
                      setDiscount(e.target.value as DiscountKind);
                    }}
                  >
                    <option value="NONE">No Discount</option>
                    <option value="SENIOR_CITIZEN">Senior Citizen (20%)</option>
                    <option value="PWD">PWD (20%)</option>
                    <option value="PROMO_10">Cafe Promo (10%)</option>
                  </SelectInput>
                </FormField>

                <FormField label="Tender Mode">
                  <SelectInput
                    value={tenderMode}
                    onChange={(e) => {
                      checkoutOp.resetIntent();
                      setTenderMode(e.target.value as TenderMode);
                    }}
                  >
                    {(Object.keys(TENDER_LABELS) as TenderMode[]).map((mode) => (
                      <option key={mode} value={mode}>
                        {TENDER_LABELS[mode]}
                      </option>
                    ))}
                  </SelectInput>
                </FormField>
              </div>

              {tenderMode !== "CASH" ? (
                <FormField label="Approved AR Account" required>
                  <SelectInput value={arCustomerId} onChange={(e) => setArCustomerId(e.target.value)}>
                    {MOCK_AR_ACCOUNTS.map((acct) => (
                      <option key={acct.id} value={acct.id}>
                        {acct.name} (Bal: {formatPhp(acct.currentBalanceCents)})
                      </option>
                    ))}
                  </SelectInput>
                </FormField>
              ) : null}

              <div style={{ display: "grid", gridTemplateColumns: tenderMode === "PARTIAL_CASH_AR" ? "1fr 1fr" : "1fr", gap: 10 }}>
                {tenderMode !== "AR" ? (
                  <FormField label="Cash Tendered (PHP)" hint={tenderMode === "CASH" ? "Leave blank for exact cash." : "Cash portion."}>
                    <TextInput
                      inputMode="decimal"
                      placeholder={(quote.totalDueCents / 100).toFixed(2)}
                      value={cashTenderedPhp}
                      onChange={(e) => setCashTenderedPhp(e.target.value)}
                    />
                  </FormField>
                ) : null}

                {tenderMode === "PARTIAL_CASH_AR" ? (
                  <FormField label="AR Portion (PHP)" required>
                    <TextInput
                      inputMode="decimal"
                      placeholder="100.00"
                      value={arChargePhp}
                      onChange={(e) => setArChargePhp(e.target.value)}
                    />
                  </FormField>
                ) : null}
              </div>

              <div
                className="salryn-tabular"
                style={{
                  padding: 12,
                  borderRadius: 10,
                  background: "var(--salryn-surface-subtle)",
                  border: "1px solid var(--salryn-border-strong)",
                  display: "grid",
                  gap: 4,
                }}
              >
                <div style={{ display: "flex", justifyContent: "space-between", fontSize: "0.875rem" }}>
                  <span>Subtotal</span>
                  <span>{formatPhp(quote.subtotalCents)}</span>
                </div>
                <div style={{ display: "flex", justifyContent: "space-between", fontSize: "0.875rem", color: "var(--salryn-mocha)" }}>
                  <span>Discount</span>
                  <span>−{formatPhp(quote.discountCents)}</span>
                </div>
                <div style={{ display: "flex", justifyContent: "space-between", fontSize: "1.125rem", fontWeight: 800, marginTop: 4 }}>
                  <span>Server Total Due</span>
                  <span>{formatPhp(quote.totalDueCents)}</span>
                </div>
              </div>
            </div>
          </CardBody>
          <CardFooter>
            <Button
              variant="ghost"
              leftIcon={<Trash2 size={16} />}
              disabled={cart.length === 0 || checkoutOp.inFlight}
              onClick={() => {
                setCart([]);
                checkoutOp.resetIntent();
              }}
            >
              Clear
            </Button>
            <Button
              variant="primary"
              size="lg"
              loading={checkoutOp.inFlight}
              disabled={cart.length === 0 || stationBlocked}
              onClick={() => void handleCheckoutSubmit()}
            >
              Complete Sale ({TENDER_LABELS[tenderMode]})
            </Button>
          </CardFooter>
        </Card>
      </div>

      {lastReceipt ? (
        <Card>
          <CardHeader
            title={`Committed Receipt · ${lastReceipt.receiptNumber}`}
            subtitle={`Operation ID: ${lastReceipt.operationId} · Kitchen Ticket: ${lastReceipt.kitchenTicketNumber}`}
            action={
              <Button
                variant="secondary"
                leftIcon={<Printer size={16} />}
                loading={reprintOp.inFlight}
                onClick={() => void handleReprintReceipt()}
              >
                Reprint Receipt
              </Button>
            }
          />
          <CardBody>
            <div className="salryn-tabular" style={{ display: "flex", flexWrap: "wrap", gap: 16 }}>
              <span>Total: <strong>{formatPhp(lastReceipt.totalDueCents)}</strong></span>
              <span>Cash: <strong>{formatPhp(lastReceipt.cashTenderedCents)}</strong></span>
              <span>AR: <strong>{formatPhp(lastReceipt.arChargeCents)}</strong></span>
              <span>Change: <strong>{formatPhp(lastReceipt.changeCents)}</strong></span>
              {lastReceipt.replayedIdempotently ? (
                <StatusBadge tone="info">Idempotent Recovery (No Duplicate Charge)</StatusBadge>
              ) : null}
            </div>
          </CardBody>
        </Card>
      ) : null}

      <Dialog
        open={Boolean(configuringItem)}
        title={configuringItem ? `Configure ${configuringItem.name}` : "Configure Item"}
        description="Select cafe modifiers before adding to the ticket."
        onClose={() => setConfiguringItem(null)}
        footer={
          <>
            <Button variant="secondary" onClick={() => setConfiguringItem(null)}>
              Cancel
            </Button>
            <Button
              variant="primary"
              onClick={() => {
                if (!configuringItem) return;
                addLineToCart(configuringItem, Object.values(selectedModsByGroup), lineNote);
                setConfiguringItem(null);
              }}
            >
              Add to Ticket
            </Button>
          </>
        }
      >
        {configuringItem ? (
          <div style={{ display: "grid", gap: 14 }}>
            {configuringItem.modifierGroups.map((group) => (
              <FormField key={group.id} label={group.name} required={group.required}>
                <SelectInput
                  value={selectedModsByGroup[group.id]?.id ?? ""}
                  onChange={(e) => {
                    const opt = group.options.find((o) => o.id === e.target.value);
                    if (opt) setSelectedModsByGroup((prev) => ({ ...prev, [group.id]: opt }));
                  }}
                >
                  {group.options.map((opt) => (
                    <option key={opt.id} value={opt.id}>
                      {opt.label} {opt.priceDeltaCents > 0 ? `(+${formatPhp(opt.priceDeltaCents)})` : ""}
                    </option>
                  ))}
                </SelectInput>
              </FormField>
            ))}
            <FormField label="Barista / Kitchen Note">
              <TextInput placeholder="Less ice, extra hot..." value={lineNote} onChange={(e) => setLineNote(e.target.value)} />
            </FormField>
          </div>
        ) : null}
      </Dialog>

      <Dialog
        open={refundOpen}
        title="Audited Refund"
        description="Refunds require a reason and carry a durable operation ID."
        onClose={() => setRefundOpen(false)}
        footer={
          <>
            <Button variant="secondary" onClick={() => setRefundOpen(false)}>
              Cancel
            </Button>
            <Button variant="danger" loading={refundOp.inFlight} onClick={() => void handleRefundSubmit()}>
              Submit Refund
            </Button>
          </>
        }
      >
        <div style={{ display: "grid", gap: 12 }}>
          {refundOp.error ? <Alert tone="danger" title="Refund Error" description={refundOp.error} /> : null}
          <FormField label="Receipt Number" required>
            <TextInput value={refundReceiptNum} onChange={(e) => setRefundReceiptNum(e.target.value)} />
          </FormField>
          <FormField label="Refund Amount (PHP)" required>
            <TextInput inputMode="decimal" value={refundAmountPhp} onChange={(e) => setRefundAmountPhp(e.target.value)} />
          </FormField>
          <FormField label="Audit Reason" required>
            <TextInput placeholder="Wrong beverage prepared" value={refundReason} onChange={(e) => setRefundReason(e.target.value)} />
          </FormField>
        </div>
      </Dialog>

      <Dialog
        open={blindCloseOpen}
        title="Blind Shift Close"
        description="Enter your counted drawer cash without seeing expected totals beforehand."
        onClose={() => setBlindCloseOpen(false)}
        footer={
          <>
            <Button variant="secondary" onClick={() => setBlindCloseOpen(false)}>
              Close
            </Button>
            {!blindCloseResult ? (
              <Button variant="primary" loading={blindCloseOp.inFlight} onClick={() => void handleBlindCloseSubmit()}>
                Submit Blind Count
              </Button>
            ) : null}
          </>
        }
      >
        <div style={{ display: "grid", gap: 12 }}>
          {blindCloseOp.error ? <Alert tone="danger" title="Blind Close Failed" description={blindCloseOp.error} /> : null}
          {!blindCloseResult ? (
            <FormField label="Counted Drawer Cash (PHP)" required hint="Expected cash is revealed only after submission.">
              <TextInput
                inputMode="decimal"
                placeholder="18450.00"
                value={blindCountPhp}
                onChange={(e) => setBlindCountPhp(e.target.value)}
              />
            </FormField>
          ) : (
            <div className="salryn-tabular" style={{ display: "grid", gap: 6 }}>
              <Alert
                tone={blindCloseResult.varianceCents === 0 ? "success" : "warning"}
                title="Shift Variance Computed by Server"
                description={`Counted: ${formatPhp(blindCloseResult.countedCashCents)} · Expected: ${formatPhp(blindCloseResult.expectedCashCents)} · Variance: ${formatPhp(blindCloseResult.varianceCents)}`}
              />
              <div style={{ display: "flex", alignItems: "center", gap: 6, fontSize: "0.8125rem", color: "var(--salryn-mocha)" }}>
                <AlertTriangle aria-hidden="true" size={14} />
                <span>Operation ID: {blindCloseResult.operationId}</span>
              </div>
            </div>
          )}
        </div>
      </Dialog>
    </div>
  );
}
