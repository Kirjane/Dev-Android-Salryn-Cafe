import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { hostRequest, parsePhpToCents } from "../../api/hostClient";
import {
  isLicenseActiveForBootstrap,
  type CafeOrderSummary,
  type HostLicenseBootstrapStatus,
  type HostSession,
  type MenuPublication,
} from "../../cashier/types";
import "../styles/r3-design-system.css";
import { R3ToastProvider, useR3Toast } from "../components/R3Primitives";
import { useDurableIntent } from "./durableIntent";
import {
  confirmBooking,
  createBooking,
  createDelivery,
  dispatchDelivery,
  readBookingCustomers,
  readLiveBookings,
  readLiveDeliveries,
  receiveBookingDeposit,
  refundBookingDeposit,
  resolveDelivery,
  type BookingCustomerOption,
  type BookingMode,
  type CafeBookingDepositMutationResult,
  type CafeBookingMutationResult,
  type CafeBookingSummary,
  type CafeDeliveryMutationResult,
  type CafeDeliverySummary,
  type CreateBookingCommand,
  type CreateDeliveryCommand,
  type ReceiveBookingDepositCommand,
  type RefundBookingDepositCommand,
} from "./bookingDeliveryContracts";

type TabId = "bookings" | "deliveries";

type Props = {
  initialTab?: TabId;
};

type DepositReceivePayload = {
  bookingId: string;
  command: ReceiveBookingDepositCommand;
};

type DepositRefundPayload = {
  bookingId: string;
  command: RefundBookingDepositCommand;
};

function hasPerm(session: HostSession | null, perm: string): boolean {
  return (session?.user?.permissions ?? []).includes(perm);
}

async function verifyHostBookingDeliveryAccess(): Promise<HostSession> {
  const license = await hostRequest<HostLicenseBootstrapStatus>("/api/license/status", undefined, "GET");
  if (!isLicenseActiveForBootstrap(license)) {
    throw new Error("Active Host license required. Return to the workstation to activate.");
  }
  const session = await hostRequest<HostSession>("/api/session", undefined, "GET");
  if (!session?.isAuthenticated || !session.user) {
    throw new Error("Sign in through the workstation before opening Bookings & Deliveries.");
  }
  if (!hasPerm(session, "Booking.View") && !hasPerm(session, "Delivery.View")) {
    throw new Error("Insufficient permissions: Booking.View or Delivery.View is required.");
  }
  return session;
}

function R3LiveBookingsDeliveriesContent({ initialTab = "bookings" }: Props) {
  const { pushToast } = useR3Toast();
  const [session, setSession] = useState<HostSession | null>(null);
  const [loading, setLoading] = useState(true);
  const [accessError, setAccessError] = useState<string | null>(null);
  const [tab, setTab] = useState<TabId>(initialTab);

  const [bookings, setBookings] = useState<CafeBookingSummary[]>([]);
  const [customers, setCustomers] = useState<BookingCustomerOption[]>([]);
  const [menuPub, setMenuPub] = useState<MenuPublication | null>(null);
  const [deliveries, setDeliveries] = useState<CafeDeliverySummary[]>([]);
  const [takeoutOrders, setTakeoutOrders] = useState<CafeOrderSummary[]>([]);

  // Booking Create Form (INT-03)
  const [customerId, setCustomerId] = useState("");
  const [bookingMode, setBookingMode] = useState<BookingMode>("Event");
  const [eventName, setEventName] = useState("");
  const [productId, setProductId] = useState("");
  const [depositReqPhp, setDepositReqPhp] = useState("500.00");
  const [bookingMessage, setBookingMessage] = useState<string | null>(null);
  const createBookingIntent = useDurableIntent<CreateBookingCommand, CafeBookingMutationResult>("booking-create");

  // Booking Deposit Receive & Refund (INT-04)
  const [depositBookingId, setDepositBookingId] = useState("");
  const [depositAmountPhp, setDepositAmountPhp] = useState("500.00");
  const [depositRef, setDepositRef] = useState("DEP-CASH-001");
  const [lastReceivedDeposit, setLastReceivedDeposit] = useState<CafeBookingDepositMutationResult | null>(null);
  const [refundReason, setRefundReason] = useState("Client schedule change");
  const depositReceiveIntent = useDurableIntent<DepositReceivePayload, CafeBookingDepositMutationResult>(
    "booking-deposit-receive",
  );
  const depositRefundIntent = useDurableIntent<DepositRefundPayload, CafeBookingDepositMutationResult>(
    "booking-deposit-refund",
  );

  // Delivery Create Form (INT-05)
  const [deliveryOrderId, setDeliveryOrderId] = useState("");
  const [recipientName, setRecipientName] = useState("");
  const [deliveryAddress, setDeliveryAddress] = useState("");
  const [deliveryFeePhp, setDeliveryFeePhp] = useState("50.00");
  const [deliveryMessage, setDeliveryMessage] = useState<string | null>(null);
  const createDeliveryIntent = useDurableIntent<CreateDeliveryCommand, CafeDeliveryMutationResult>("delivery-create");

  const tabRefs = useRef<Partial<Record<TabId, HTMLButtonElement | null>>>({});

  const loadAll = useCallback(async () => {
    setLoading(true);
    setAccessError(null);
    try {
      const verified = await verifyHostBookingDeliveryAccess();
      setSession(verified);

      const canViewBookings = hasPerm(verified, "Booking.View");
      const canViewDeliveries = hasPerm(verified, "Delivery.View");
      const canViewOrders = hasPerm(verified, "Orders.View");

      const [bkList, custList, pubRes, delList, ordList] = await Promise.all([
        canViewBookings ? readLiveBookings() : Promise.resolve([]),
        canViewBookings ? readBookingCustomers() : Promise.resolve([]),
        canViewBookings
          ? hostRequest<MenuPublication>("/api/menu/publication/current", undefined, "GET").catch(() => null)
          : Promise.resolve(null),
        canViewDeliveries ? readLiveDeliveries() : Promise.resolve([]),
        canViewDeliveries && canViewOrders
          ? hostRequest<CafeOrderSummary[]>("/api/cafe/orders", undefined, "GET").catch(() => [])
          : Promise.resolve([]),
      ]);

      setBookings(bkList);
      setCustomers(custList);
      setMenuPub(pubRes);
      setDeliveries(delList);

      const eligibleOrders = (Array.isArray(ordList) ? ordList : []).filter(
        (o) => o.serviceMode === "Takeout" && o.state !== "Completed" && o.state !== "Cancelled",
      );
      setTakeoutOrders(eligibleOrders);

      if (custList.length > 0) setCustomerId((prev) => prev || custList[0].customerId);
      const firstItem = pubRes?.items?.[0];
      if (firstItem) setProductId((prev) => prev || firstItem.productId);
      if (bkList.length > 0) setDepositBookingId((prev) => prev || bkList[0].bookingId);
      if (eligibleOrders.length > 0) setDeliveryOrderId((prev) => prev || eligibleOrders[0].orderId);
      if (!canViewBookings && canViewDeliveries) setTab("deliveries");
      else if (canViewBookings && !canViewDeliveries) setTab("bookings");
    } catch (err) {
      setAccessError(err instanceof Error ? err.message : "Live Bookings & Deliveries unavailable.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadAll();
  }, [loadAll]);

  const canManageBookings = hasPerm(session, "Booking.Manage");
  const canDepositReceive = hasPerm(session, "Booking.DepositReceive") && hasPerm(session, "Cash.In");
  const canDepositRefund = hasPerm(session, "Booking.DepositRefund") && hasPerm(session, "Cash.Out");
  const canManageDeliveries = hasPerm(session, "Delivery.Manage");
  const canAdvanceDeliveries = hasPerm(session, "Delivery.Advance");

  const tabs = useMemo(
    () =>
      [
        { id: "bookings" as TabId, label: `Bookings (${bookings.length})`, enabled: hasPerm(session, "Booking.View") },
        { id: "deliveries" as TabId, label: `Deliveries (${deliveries.length})`, enabled: hasPerm(session, "Delivery.View") },
      ].filter((t) => t.enabled),
    [bookings.length, deliveries.length, session],
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

  // --- INT-03: Create Booking ---
  const handleCreateBooking = async (e: React.FormEvent) => {
    e.preventDefault();
    setBookingMessage(null);
    if (!customerId || !eventName.trim() || !menuPub?.revisionId || !productId) {
      setBookingMessage("Customer, event name, published menu revision, and item are required.");
      return;
    }
    let depositRequirementCents: number;
    try {
      depositRequirementCents = parsePhpToCents(depositReqPhp);
    } catch (err) {
      setBookingMessage(err instanceof Error ? err.message : "Invalid deposit requirement.");
      return;
    }

    const res = await createBookingIntent.execute(
      { customerId, bookingMode, eventName: eventName.trim(), productId, depositRequirementCents },
      () => ({
        operationId: crypto.randomUUID(),
        bookingId: crypto.randomUUID(),
        revisionId: crypto.randomUUID(),
        occurredAtUtc: new Date().toISOString(),
        configuration: {
          customerId,
          bookingMode,
          eventName: eventName.trim(),
          eventStartUtc: new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString(),
          menuPublicationRevisionId: menuPub.revisionId,
          depositRequirementCents,
          intendedServiceMode: "Takeout",
          lines: [{ bookingLineId: crypto.randomUUID(), productId, quantityX1000: 1000, displayOrder: 0 }],
        },
      }),
      createBooking,
    );

    if (res.ok) {
      setEventName("");
      setDepositBookingId(res.value.bookingId);
      await loadAll();
      const msg = `Booking created (${res.value.bookingId.slice(0, 8)}…, ${res.value.status}).`;
      setBookingMessage(msg);
      pushToast(msg);
    } else {
      setBookingMessage(`${res.code ? `[${res.code}] ` : ""}${res.message}`);
    }
  };

  const handleConfirmBooking = async (bk: CafeBookingSummary) => {
    setBookingMessage(null);
    try {
      const res = await confirmBooking(bk.bookingId, {
        operationId: crypto.randomUUID(),
        expectedVersion: bk.bookingVersion,
        occurredAtUtc: new Date().toISOString(),
      });
      await loadAll();
      const msg = `Booking ${res.bookingId.slice(0, 8)}… confirmed (v${res.bookingVersion}).`;
      setBookingMessage(msg);
      pushToast(msg);
    } catch (err) {
      setBookingMessage(err instanceof Error ? err.message : "Confirm booking failed.");
    }
  };

  // --- INT-04: Receive Deposit & Refund Deposit with Allocations ---
  const selectedBooking = bookings.find((b) => b.bookingId === depositBookingId) ?? null;

  const handleReceiveDeposit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBookingMessage(null);
    if (!selectedBooking) {
      setBookingMessage("Select a booking first.");
      return;
    }
    let amountCents: number;
    try {
      amountCents = parsePhpToCents(depositAmountPhp);
    } catch (err) {
      setBookingMessage(err instanceof Error ? err.message : "Invalid deposit amount.");
      return;
    }
    const res = await depositReceiveIntent.execute(
      { bookingId: selectedBooking.bookingId, bookingRevisionId: selectedBooking.currentRevisionId, amountCents, reference: depositRef.trim() },
      () => ({
        bookingId: selectedBooking.bookingId,
        command: {
          operationId: crypto.randomUUID(),
          depositFactId: crypto.randomUUID(),
          cashMovementId: crypto.randomUUID(),
          bookingRevisionId: selectedBooking.currentRevisionId,
          amountCents,
          reference: depositRef.trim(),
          occurredAtUtc: new Date().toISOString(),
        },
      }),
      (p) => receiveBookingDeposit(p.bookingId, p.command),
    );

    if (res.ok) {
      setLastReceivedDeposit(res.value);
      await loadAll();
      const msg = `Deposit received (${res.value.depositFactId.slice(0, 8)}…).`;
      setBookingMessage(msg);
      pushToast(msg);
    } else {
      setBookingMessage(`${res.code ? `[${res.code}] ` : ""}${res.message}`);
    }
  };

  const handleRefundLastDeposit = async () => {
    setBookingMessage(null);
    if (!lastReceivedDeposit) {
      setBookingMessage("No received deposit fact available to allocate for refund.");
      return;
    }
    const res = await depositRefundIntent.execute(
      {
        bookingId: lastReceivedDeposit.bookingId,
        receivedDepositFactId: lastReceivedDeposit.depositFactId,
        amountCents: lastReceivedDeposit.amountCents,
        reason: refundReason.trim(),
      },
      () => ({
        bookingId: lastReceivedDeposit.bookingId,
        command: {
          operationId: crypto.randomUUID(),
          depositFactId: crypto.randomUUID(),
          cashMovementId: crypto.randomUUID(),
          bookingRevisionId: lastReceivedDeposit.bookingRevisionId,
          amountCents: lastReceivedDeposit.amountCents,
          reason: refundReason.trim(),
          reference: `REF-${lastReceivedDeposit.depositFactId.slice(0, 8)}`,
          allocations: [
            {
              receivedDepositFactId: lastReceivedDeposit.depositFactId,
              amountCents: lastReceivedDeposit.amountCents,
            },
          ],
          occurredAtUtc: new Date().toISOString(),
        },
      }),
      (p) => refundBookingDeposit(p.bookingId, p.command),
    );

    if (res.ok) {
      setLastReceivedDeposit(null);
      await loadAll();
      const msg = `Deposit refunded (${res.value.depositFactId.slice(0, 8)}…).`;
      setBookingMessage(msg);
      pushToast(msg);
    } else {
      setBookingMessage(`${res.code ? `[${res.code}] ` : ""}${res.message}`);
    }
  };

  // --- INT-05: Order-Bound Delivery ---
  const handleCreateDelivery = async (e: React.FormEvent) => {
    e.preventDefault();
    setDeliveryMessage(null);
    if (!deliveryOrderId || !recipientName.trim() || !deliveryAddress.trim()) {
      setDeliveryMessage("Eligible Takeout order, recipient name, and delivery address are required.");
      return;
    }
    let deliveryFeeCents: number;
    try {
      deliveryFeeCents = parsePhpToCents(deliveryFeePhp);
    } catch (err) {
      setDeliveryMessage(err instanceof Error ? err.message : "Invalid delivery fee.");
      return;
    }

    const res = await createDeliveryIntent.execute(
      { orderId: deliveryOrderId, recipientName: recipientName.trim(), deliveryAddress: deliveryAddress.trim(), deliveryFeeCents },
      () => ({
        operationId: crypto.randomUUID(),
        deliveryId: crypto.randomUUID(),
        orderId: deliveryOrderId,
        occurredAtUtc: new Date().toISOString(),
        recipientName: recipientName.trim(),
        deliveryAddress: deliveryAddress.trim(),
        deliveryFeeCents,
      }),
      createDelivery,
    );

    if (res.ok) {
      setRecipientName("");
      setDeliveryAddress("");
      await loadAll();
      const msg = `Delivery created (${res.value.deliveryId.slice(0, 8)}…, status: ${res.value.status}).`;
      setDeliveryMessage(msg);
      pushToast(msg);
    } else {
      setDeliveryMessage(`${res.code ? `[${res.code}] ` : ""}${res.message}`);
    }
  };

  const handleDispatchDelivery = async (del: CafeDeliverySummary) => {
    setDeliveryMessage(null);
    try {
      const res = await dispatchDelivery(del.deliveryId, {
        operationId: crypto.randomUUID(),
        expectedVersion: del.version,
        occurredAtUtc: new Date().toISOString(),
      });
      await loadAll();
      const msg = `Delivery ${res.deliveryId.slice(0, 8)}… advanced to ${res.status}.`;
      setDeliveryMessage(msg);
      pushToast(msg);
    } catch (err) {
      setDeliveryMessage(err instanceof Error ? err.message : "Dispatch failed.");
    }
  };

  const handleResolveDelivery = async (del: CafeDeliverySummary, targetStatus: "Delivered" | "Failed") => {
    setDeliveryMessage(null);
    try {
      const res = await resolveDelivery(del.deliveryId, {
        operationId: crypto.randomUUID(),
        expectedVersion: del.version,
        targetStatus,
        reason: targetStatus === "Failed" ? "Customer unreachable" : null,
        occurredAtUtc: new Date().toISOString(),
      });
      await loadAll();
      const msg = `Delivery ${res.deliveryId.slice(0, 8)}… resolved as ${res.status}.`;
      setDeliveryMessage(msg);
      pushToast(msg);
    } catch (err) {
      setDeliveryMessage(err instanceof Error ? err.message : "Resolve delivery failed.");
    }
  };

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
            </p>
            <h1 className="r3-page-title">Bookings &amp; Order-Bound Deliveries</h1>
            <p className="r3-page-subtitle">
              Live Host Bookings (Event / CoffeeCart + Cash deposits) and Takeout-order-bound Deliveries (zero mock fallback).
            </p>
          </div>
          <div className="r3-page-actions">
            <button type="button" className="r3-btn r3-btn--outline r3-btn--sm" onClick={() => void loadAll()} disabled={loading}>
              {loading ? "Refreshing…" : "Refresh Live Data"}
            </button>
          </div>
        </div>

        {accessError && (
          <div className="r3-alert r3-alert--danger" role="alert">
            <div className="r3-alert-body">
              <div className="r3-alert-title">Bookings &amp; Deliveries unavailable</div>
              <div>{accessError}</div>
            </div>
          </div>
        )}

        {!accessError && session && (
          <>
            <div className="r3-tabs" role="tablist" aria-label="Bookings and Deliveries sections">
              {tabs.map((t, idx) => (
                <button
                  key={t.id}
                  id={`r3-bd-tab-${t.id}`}
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

            {tab === "bookings" && hasPerm(session, "Booking.View") && (
              <div role="tabpanel" aria-labelledby="r3-bd-tab-bookings" style={{ display: "grid", gap: 16 }}>
                <div className="r3-card">
                  <div className="r3-card-header">
                    <div>
                      <div className="r3-card-title">Live Bookings</div>
                      <div className="r3-card-subtitle">Canonical BookingMode: Event or CoffeeCart</div>
                    </div>
                  </div>
                  <div className="r3-table-wrap" tabIndex={0} role="region" aria-label="Live Bookings">
                    <table className="r3-table">
                      <caption className="r3-sr-only">Live Bookings</caption>
                      <thead>
                        <tr>
                          <th scope="col">Booking</th>
                          <th scope="col">Mode</th>
                          <th scope="col">Customer</th>
                          <th scope="col">Status</th>
                          <th scope="col">Actions</th>
                        </tr>
                      </thead>
                      <tbody>
                        {bookings.length === 0 ? (
                          <tr>
                            <td colSpan={5} className="r3-text-muted">No bookings found.</td>
                          </tr>
                        ) : (
                          bookings.map((bk) => (
                            <tr key={bk.bookingId}>
                              <th scope="row"><code>{bk.bookingId.slice(0, 8)}…</code> (v{bk.bookingVersion})</th>
                              <td>{bk.bookingMode}</td>
                              <td>{bk.customerNameSnapshot}</td>
                              <td><span className="r3-badge r3-badge--info">{bk.status}</span></td>
                              <td>
                                {canManageBookings && bk.status === "Draft" && (
                                  <button type="button" className="r3-btn r3-btn--outline r3-btn--sm" onClick={() => void handleConfirmBooking(bk)}>
                                    Confirm
                                  </button>
                                )}
                              </td>
                            </tr>
                          ))
                        )}
                      </tbody>
                    </table>
                  </div>
                </div>

                {canManageBookings && (
                  <div className="r3-card">
                    <div className="r3-card-header">
                      <div>
                        <div className="r3-card-title">Create Booking (INT-03)</div>
                        <div className="r3-card-subtitle">Nested configuration referencing live customer and published menu revision</div>
                      </div>
                    </div>
                    {createBookingIntent.pending && (
                      <div className={`r3-alert ${createBookingIntent.pending.status === "rejected" ? "r3-alert--danger" : "r3-alert--warn"}`} role="alert" style={{ marginBottom: 12 }}>
                        <div className="r3-alert-body">
                          Operation <code>{createBookingIntent.pending.payload.operationId}</code> ({createBookingIntent.pending.status})
                          {createBookingIntent.pending.lastErrorMessage ? ` — ${createBookingIntent.pending.lastErrorMessage}` : ""}
                        </div>
                        {createBookingIntent.pending.status === "rejected" ? (
                          <button type="button" className="r3-btn r3-btn--outline r3-btn--sm" onClick={() => createBookingIntent.clearPending(false)}>
                            Clear Rejected
                          </button>
                        ) : (
                          <button type="button" className="r3-btn r3-btn--primary r3-btn--sm" onClick={() => void createBookingIntent.retryPending(createBooking).then((r) => { if (r.ok) void loadAll(); })}>
                            Retry Exact Create
                          </button>
                        )}
                      </div>
                    )}
                    <form onSubmit={(e) => void handleCreateBooking(e)} style={{ display: "grid", gap: 12, maxWidth: 560 }}>
                      <label>
                        <div className="r3-text-xs r3-text-muted">Customer</div>
                        <select className="r3-input" aria-label="Booking Customer" value={customerId} onChange={(e) => setCustomerId(e.target.value)}>
                          {customers.map((c) => (
                            <option key={c.customerId} value={c.customerId}>{c.fullName}</option>
                          ))}
                        </select>
                      </label>
                      <label>
                        <div className="r3-text-xs r3-text-muted">Booking Mode</div>
                        <select className="r3-input" aria-label="Booking Mode" value={bookingMode} onChange={(e) => setBookingMode(e.target.value as BookingMode)}>
                          <option value="Event">Event</option>
                          <option value="CoffeeCart">CoffeeCart</option>
                        </select>
                      </label>
                      <label>
                        <div className="r3-text-xs r3-text-muted">Event Name</div>
                        <input className="r3-input" aria-label="Event Name" value={eventName} onChange={(e) => setEventName(e.target.value)} placeholder="Community Coffee Cart" />
                      </label>
                      <label>
                        <div className="r3-text-xs r3-text-muted">Deposit Requirement (PHP)</div>
                        <input className="r3-input" aria-label="Deposit Requirement" value={depositReqPhp} onChange={(e) => setDepositReqPhp(e.target.value)} />
                      </label>
                      <div>
                        <button type="submit" className="r3-btn r3-btn--primary" disabled={createBookingIntent.inFlight}>
                          {createBookingIntent.inFlight ? "Creating…" : "Create Booking"}
                        </button>
                      </div>
                    </form>
                  </div>
                )}

                {(canDepositReceive || canDepositRefund) && (
                  <div className="r3-card">
                    <div className="r3-card-header">
                      <div>
                        <div className="r3-card-title">Booking Cash Deposits &amp; Refunds (INT-04)</div>
                        <div className="r3-card-subtitle">Requires an open cashier shift; refunds require explicit received-deposit allocations</div>
                      </div>
                    </div>
                    {depositReceiveIntent.pending && (
                      <div className={`r3-alert ${depositReceiveIntent.pending.status === "rejected" ? "r3-alert--danger" : "r3-alert--warn"}`} role="alert" style={{ marginBottom: 12 }}>
                        <div className="r3-alert-body">
                          Deposit receive op <code>{depositReceiveIntent.pending.payload.command.operationId}</code> ({depositReceiveIntent.pending.status})
                          {depositReceiveIntent.pending.lastErrorMessage ? ` — ${depositReceiveIntent.pending.lastErrorMessage}` : ""}
                        </div>
                        {depositReceiveIntent.pending.status === "rejected" ? (
                          <button type="button" className="r3-btn r3-btn--outline r3-btn--sm" onClick={() => depositReceiveIntent.clearPending(false)}>
                            Clear Rejected Deposit
                          </button>
                        ) : (
                          <button type="button" className="r3-btn r3-btn--primary r3-btn--sm" onClick={() => void depositReceiveIntent.retryPending((p) => receiveBookingDeposit(p.bookingId, p.command)).then((r) => { if (r.ok) { setLastReceivedDeposit(r.value); void loadAll(); } })}>
                            Retry Exact Deposit
                          </button>
                        )}
                      </div>
                    )}
                    {depositRefundIntent.pending && (
                      <div className={`r3-alert ${depositRefundIntent.pending.status === "rejected" ? "r3-alert--danger" : "r3-alert--warn"}`} role="alert" style={{ marginBottom: 12 }}>
                        <div className="r3-alert-body">
                          Deposit refund op <code>{depositRefundIntent.pending.payload.command.operationId}</code> ({depositRefundIntent.pending.status})
                          {depositRefundIntent.pending.lastErrorMessage ? ` — ${depositRefundIntent.pending.lastErrorMessage}` : ""}
                        </div>
                        {depositRefundIntent.pending.status === "rejected" ? (
                          <button type="button" className="r3-btn r3-btn--outline r3-btn--sm" onClick={() => depositRefundIntent.clearPending(false)}>
                            Clear Rejected Refund
                          </button>
                        ) : (
                          <button type="button" className="r3-btn r3-btn--primary r3-btn--sm" onClick={() => void depositRefundIntent.retryPending((p) => refundBookingDeposit(p.bookingId, p.command)).then((r) => { if (r.ok) { setLastReceivedDeposit(null); void loadAll(); } })}>
                            Retry Exact Refund
                          </button>
                        )}
                      </div>
                    )}
                    <form onSubmit={(e) => void handleReceiveDeposit(e)} style={{ display: "grid", gap: 12, maxWidth: 560 }}>
                      <label>
                        <div className="r3-text-xs r3-text-muted">Booking</div>
                        <select className="r3-input" aria-label="Deposit Booking" value={depositBookingId} onChange={(e) => setDepositBookingId(e.target.value)}>
                          {bookings.map((b) => (
                            <option key={b.bookingId} value={b.bookingId}>
                              {b.bookingId.slice(0, 8)}… — {b.customerNameSnapshot} ({b.status})
                            </option>
                          ))}
                        </select>
                      </label>
                      <label>
                        <div className="r3-text-xs r3-text-muted">Cash Deposit Amount (PHP)</div>
                        <input className="r3-input" aria-label="Cash Deposit Amount" value={depositAmountPhp} onChange={(e) => setDepositAmountPhp(e.target.value)} />
                      </label>
                      <label>
                        <div className="r3-text-xs r3-text-muted">Reference</div>
                        <input className="r3-input" aria-label="Deposit Reference" value={depositRef} onChange={(e) => setDepositRef(e.target.value)} />
                      </label>
                      <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
                        {canDepositReceive && (
                          <button type="submit" className="r3-btn r3-btn--primary" disabled={depositReceiveIntent.inFlight}>
                            Receive Cash Deposit
                          </button>
                        )}
                        {canDepositRefund && lastReceivedDeposit && (
                          <button type="button" className="r3-btn r3-btn--outline" onClick={() => void handleRefundLastDeposit()} disabled={depositRefundIntent.inFlight}>
                            Refund Last Deposit ({lastReceivedDeposit.depositFactId.slice(0, 8)}…)
                          </button>
                        )}
                      </div>
                      {canDepositRefund && lastReceivedDeposit && (
                        <label>
                          <div className="r3-text-xs r3-text-muted">Refund Reason</div>
                          <input className="r3-input" aria-label="Refund Reason" value={refundReason} onChange={(e) => setRefundReason(e.target.value)} />
                        </label>
                      )}
                    </form>
                  </div>
                )}

                {bookingMessage && <p role="status" className="r3-text-sm">{bookingMessage}</p>}
              </div>
            )}

            {tab === "deliveries" && hasPerm(session, "Delivery.View") && (
              <div role="tabpanel" aria-labelledby="r3-bd-tab-deliveries" style={{ display: "grid", gap: 16 }}>
                <div className="r3-card">
                  <div className="r3-card-header">
                    <div>
                      <div className="r3-card-title">Order-Bound Deliveries (INT-05)</div>
                      <div className="r3-card-subtitle">Canonical Host statuses: Pending → Dispatched → Delivered / Failed / Cancelled</div>
                    </div>
                  </div>
                  <div className="r3-table-wrap" tabIndex={0} role="region" aria-label="Order-Bound Deliveries">
                    <table className="r3-table">
                      <caption className="r3-sr-only">Order-Bound Deliveries</caption>
                      <thead>
                        <tr>
                          <th scope="col">Delivery</th>
                          <th scope="col">Order</th>
                          <th scope="col">Status</th>
                          <th scope="col">Actions</th>
                        </tr>
                      </thead>
                      <tbody>
                        {deliveries.length === 0 ? (
                          <tr>
                            <td colSpan={4} className="r3-text-muted">No deliveries found.</td>
                          </tr>
                        ) : (
                          deliveries.map((del) => (
                            <tr key={del.deliveryId}>
                              <th scope="row"><code>{del.deliveryId.slice(0, 8)}…</code> (v{del.version})</th>
                              <td><code>{del.orderId.slice(0, 8)}…</code></td>
                              <td><span className="r3-badge r3-badge--info">{del.status}</span></td>
                              <td style={{ display: "flex", gap: 6 }}>
                                {canAdvanceDeliveries && del.status === "Pending" && (
                                  <button type="button" className="r3-btn r3-btn--outline r3-btn--sm" onClick={() => void handleDispatchDelivery(del)}>
                                    Dispatch
                                  </button>
                                )}
                                {canAdvanceDeliveries && del.status === "Dispatched" && (
                                  <>
                                    <button type="button" className="r3-btn r3-btn--primary r3-btn--sm" onClick={() => void handleResolveDelivery(del, "Delivered")}>
                                      Mark Delivered
                                    </button>
                                    <button type="button" className="r3-btn r3-btn--outline r3-btn--sm" onClick={() => void handleResolveDelivery(del, "Failed")}>
                                      Mark Failed
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

                {canManageDeliveries && (
                  <div className="r3-card">
                    <div className="r3-card-header">
                      <div>
                        <div className="r3-card-title">Create Order-Bound Delivery</div>
                        <div className="r3-card-subtitle">Must bind to an existing non-terminal same-store Takeout C4 order</div>
                      </div>
                    </div>
                    {createDeliveryIntent.pending && (
                      <div className={`r3-alert ${createDeliveryIntent.pending.status === "rejected" ? "r3-alert--danger" : "r3-alert--warn"}`} role="alert" style={{ marginBottom: 12 }}>
                        <div className="r3-alert-body">
                          Delivery create op <code>{createDeliveryIntent.pending.payload.operationId}</code> ({createDeliveryIntent.pending.status})
                          {createDeliveryIntent.pending.lastErrorMessage ? ` — ${createDeliveryIntent.pending.lastErrorMessage}` : ""}
                        </div>
                        {createDeliveryIntent.pending.status === "rejected" ? (
                          <button type="button" className="r3-btn r3-btn--outline r3-btn--sm" onClick={() => createDeliveryIntent.clearPending(false)}>
                            Clear Rejected Delivery
                          </button>
                        ) : (
                          <button type="button" className="r3-btn r3-btn--primary r3-btn--sm" onClick={() => void createDeliveryIntent.retryPending(createDelivery).then((r) => { if (r.ok) void loadAll(); })}>
                            Retry Exact Delivery
                          </button>
                        )}
                      </div>
                    )}
                    <form onSubmit={(e) => void handleCreateDelivery(e)} style={{ display: "grid", gap: 12, maxWidth: 560 }}>
                      <label>
                        <div className="r3-text-xs r3-text-muted">Eligible Takeout Order</div>
                        <select className="r3-input" aria-label="Eligible Takeout Order" value={deliveryOrderId} onChange={(e) => setDeliveryOrderId(e.target.value)}>
                          {takeoutOrders.map((o) => (
                            <option key={o.orderId} value={o.orderId}>
                              {o.orderId.slice(0, 8)}… ({o.state}, v{o.orderVersion})
                            </option>
                          ))}
                        </select>
                      </label>
                      <label>
                        <div className="r3-text-xs r3-text-muted">Recipient Name</div>
                        <input className="r3-input" aria-label="Recipient Name" value={recipientName} onChange={(e) => setRecipientName(e.target.value)} placeholder="Maria Santos" />
                      </label>
                      <label>
                        <div className="r3-text-xs r3-text-muted">Delivery Address</div>
                        <input className="r3-input" aria-label="Delivery Address" value={deliveryAddress} onChange={(e) => setDeliveryAddress(e.target.value)} placeholder="123 Rizal St, Makati" />
                      </label>
                      <label>
                        <div className="r3-text-xs r3-text-muted">Delivery Fee (PHP)</div>
                        <input className="r3-input" aria-label="Delivery Fee" value={deliveryFeePhp} onChange={(e) => setDeliveryFeePhp(e.target.value)} />
                      </label>
                      <div>
                        <button type="submit" className="r3-btn r3-btn--primary" disabled={createDeliveryIntent.inFlight}>
                          Create Delivery
                        </button>
                      </div>
                    </form>
                  </div>
                )}

                {deliveryMessage && <p role="status" className="r3-text-sm">{deliveryMessage}</p>}
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
}

export function R3LiveBookingsDeliveriesPage(props: Props) {
  return (
    <R3ToastProvider>
      <R3LiveBookingsDeliveriesContent {...props} />
    </R3ToastProvider>
  );
}
