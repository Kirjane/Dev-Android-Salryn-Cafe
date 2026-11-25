import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { formatPhp, hostRequest, parsePhpToCents } from "../../api/hostClient";
import {
  isLicenseActiveForBootstrap,
  type CafeOrderSummary,
  type HostLicenseBootstrapStatus,
  type HostSession,
  type MenuPublication,
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
  confirmBooking,
  createBooking,
  createDelivery,
  dispatchDelivery,
  readBookingCustomers,
  readLiveBookingDetail,
  readLiveBookings,
  readLiveDeliveries,
  receiveBookingDeposit,
  refundBookingDeposit,
  resolveDelivery,
  type AdvanceBookingCommand,
  type AdvanceDeliveryCommand,
  type BookingCustomerOption,
  type BookingMode,
  type CafeBookingDepositMutationResult,
  type CafeBookingDetail,
  type CafeBookingMutationResult,
  type CafeBookingSummary,
  type CafeDeliveryMutationResult,
  type CafeDeliverySummary,
  type CreateBookingCommand,
  type CreateDeliveryCommand,
  type ReceiveBookingDepositCommand,
  type RefundAllocationCommand,
  type RefundBookingDepositCommand,
  type ResolveDeliveryCommand,
} from "./bookingDeliveryContracts";

type TabId = "bookings" | "deliveries";

type Props = {
  initialTab?: TabId;
  embedded?: boolean;
};

type ConfirmBookingPayload = {
  bookingId: string;
  command: AdvanceBookingCommand;
};

type DepositReceivePayload = {
  bookingId: string;
  command: ReceiveBookingDepositCommand;
};

type DepositRefundPayload = {
  bookingId: string;
  command: RefundBookingDepositCommand;
};

type DispatchDeliveryPayload = {
  deliveryId: string;
  command: AdvanceDeliveryCommand;
};

type ResolveDeliveryPayload = {
  deliveryId: string;
  command: ResolveDeliveryCommand;
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

function R3LiveBookingsDeliveriesContent({ initialTab = "bookings", embedded = false }: Props) {
  const { pushToast } = useR3Toast();
  const [session, setSession] = useState<HostSession | null>(null);
  const [hostIdentityKey, setHostIdentityKey] = useState<string>("");
  const [loading, setLoading] = useState(true);
  const [accessError, setAccessError] = useState<string | null>(null);
  const [tab, setTab] = useState<TabId>(initialTab);

  const [bookings, setBookings] = useState<CafeBookingSummary[]>([]);
  const [customers, setCustomers] = useState<BookingCustomerOption[]>([]);
  const [menuPub, setMenuPub] = useState<MenuPublication | null>(null);
  const [deliveries, setDeliveries] = useState<CafeDeliverySummary[]>([]);
  const [takeoutOrders, setTakeoutOrders] = useState<CafeOrderSummary[]>([]);

  const authority: DurableIntentAuthority | null = useMemo(
    () =>
      session?.user?.userId && hostIdentityKey
        ? { hostIdentityKey, actorUserId: session.user.userId }
        : null,
    [session?.user?.userId, hostIdentityKey],
  );

  // Booking Create Form (INT-03)
  const [customerId, setCustomerId] = useState("");
  const [bookingMode, setBookingMode] = useState<BookingMode>("Event");
  const [eventName, setEventName] = useState("");
  const [productId, setProductId] = useState("");
  const [depositReqPhp, setDepositReqPhp] = useState("5.00");
  const [bookingMessage, setBookingMessage] = useState<string | null>(null);

  const createBookingIntent = useDurableIntent<CreateBookingCommand, CafeBookingMutationResult>(
    "booking-create",
    authority,
  );
  const confirmBookingIntent = useDurableIntent<ConfirmBookingPayload, CafeBookingMutationResult>(
    "booking-confirm",
    authority,
  );

  // Historical Booking Detail & Deposit Ledger (INT-04)
  const [depositBookingId, setDepositBookingId] = useState("");
  const [bookingDetail, setBookingDetail] = useState<CafeBookingDetail | null>(null);
  const [depositAmountPhp, setDepositAmountPhp] = useState("5.00");
  const [depositRef, setDepositRef] = useState("DEP-CASH-001");
  const [allocationInputs, setAllocationInputs] = useState<Record<string, string>>({});
  const [refundReason, setRefundReason] = useState("Client schedule change");
  const [refundRef, setRefundRef] = useState("REF-CASH-001");

  const depositReceiveIntent = useDurableIntent<DepositReceivePayload, CafeBookingDepositMutationResult>(
    "booking-deposit-receive",
    authority,
  );
  const depositRefundIntent = useDurableIntent<DepositRefundPayload, CafeBookingDepositMutationResult>(
    "booking-deposit-refund",
    authority,
  );

  // Delivery Create & Lifecycle (INT-05 — ALL durable-intent protected!)
  const [deliveryOrderId, setDeliveryOrderId] = useState("");
  const [recipientName, setRecipientName] = useState("");
  const [deliveryAddress, setDeliveryAddress] = useState("");
  const [deliveryFeePhp, setDeliveryFeePhp] = useState("50.00");
  const [deliveryMessage, setDeliveryMessage] = useState<string | null>(null);

  const createDeliveryIntent = useDurableIntent<CreateDeliveryCommand, CafeDeliveryMutationResult>(
    "delivery-create",
    authority,
  );
  const dispatchDeliveryIntent = useDurableIntent<DispatchDeliveryPayload, CafeDeliveryMutationResult>(
    "delivery-dispatch",
    authority,
  );
  const resolveDeliveryIntent = useDurableIntent<ResolveDeliveryPayload, CafeDeliveryMutationResult>(
    "delivery-resolve",
    authority,
  );

  const tabRefs = useRef<Partial<Record<TabId, HTMLButtonElement | null>>>({});

  const loadBookingDetail = useCallback(async (id: string) => {
    if (!id) {
      setBookingDetail(null);
      return;
    }
    try {
      const detail = await readLiveBookingDetail(id);
      setBookingDetail(detail);
    } catch {
      setBookingDetail(null);
    }
  }, []);

  const loadAll = useCallback(async () => {
    setLoading(true);
    setAccessError(null);
    try {
      const [verified, idKey] = await Promise.all([
        verifyHostBookingDeliveryAccess(),
        fetchHostIdentityKey(),
      ]);
      setSession(verified);
      setHostIdentityKey(idKey);

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

      const nextSelectedBooking = depositBookingId || bkList[0]?.bookingId || "";
      if (nextSelectedBooking && nextSelectedBooking !== depositBookingId) {
        setDepositBookingId(nextSelectedBooking);
      }
      if (canViewBookings && nextSelectedBooking) {
        await loadBookingDetail(nextSelectedBooking);
      }
      if (eligibleOrders.length > 0) setDeliveryOrderId((prev) => prev || eligibleOrders[0].orderId);
      if (!canViewBookings && canViewDeliveries) setTab("deliveries");
      else if (canViewBookings && !canViewDeliveries) setTab("bookings");
    } catch (err) {
      setAccessError(err instanceof Error ? err.message : "Live Bookings & Deliveries unavailable.");
    } finally {
      setLoading(false);
    }
  }, [depositBookingId, loadBookingDetail]);

  useEffect(() => {
    void loadAll();
  }, [loadAll]);

  useEffect(() => {
    setTab(initialTab);
  }, [initialTab]);

  useEffect(() => {
    if (depositBookingId && hasPerm(session, "Booking.View")) {
      void loadBookingDetail(depositBookingId);
    }
  }, [depositBookingId, session, loadBookingDetail]);

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
          lines: [{ bookingLineId: crypto.randomUUID(), productId, quantityX1000: 2000, displayOrder: 0 }],
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

  // --- Durable Confirm Booking (P1 fix: no fresh UUID per click) ---
  const handleConfirmBooking = async (bk: CafeBookingSummary) => {
    setBookingMessage(null);
    const res = await confirmBookingIntent.execute(
      { bookingId: bk.bookingId, expectedVersion: bk.bookingVersion },
      () => ({
        bookingId: bk.bookingId,
        command: {
          operationId: crypto.randomUUID(),
          expectedVersion: bk.bookingVersion,
          occurredAtUtc: new Date().toISOString(),
        },
      }),
      (p) => confirmBooking(p.bookingId, p.command),
    );
    if (res.ok) {
      await loadAll();
      const msg = `Booking ${res.value.bookingId.slice(0, 8)}… confirmed (v${res.value.bookingVersion}).`;
      setBookingMessage(msg);
      pushToast(msg);
    } else {
      setBookingMessage(`${res.code ? `[${res.code}] ` : ""}${res.message}`);
    }
  };

  // --- INT-04: Receive Deposit & Historical Partial/Multiple Allocated Refund ---
  const selectedBooking = bookingDetail?.booking ?? bookings.find((b) => b.bookingId === depositBookingId) ?? null;
  const refundableDeposits = useMemo(
    () =>
      (bookingDetail?.depositFacts ?? []).filter(
        (f) => f.factType === "Received" && f.remainingRefundableCents > 0,
      ),
    [bookingDetail],
  );

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
    if (amountCents <= 0 || !depositRef.trim()) {
      setBookingMessage("Positive deposit amount and reference are required.");
      return;
    }
    const res = await depositReceiveIntent.execute(
      {
        bookingId: selectedBooking.bookingId,
        bookingRevisionId: selectedBooking.currentRevisionId,
        amountCents,
        reference: depositRef.trim(),
      },
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
      await loadAll();
      await loadBookingDetail(selectedBooking.bookingId);
      const msg = `Deposit received (${res.value.depositFactId.slice(0, 8)}…, ${formatPhp(res.value.amountCents)}).`;
      setBookingMessage(msg);
      pushToast(msg);
    } else {
      setBookingMessage(`${res.code ? `[${res.code}] ` : ""}${res.message}`);
    }
  };

  const handleAllocatedRefund = async (e: React.FormEvent) => {
    e.preventDefault();
    setBookingMessage(null);
    if (!selectedBooking) {
      setBookingMessage("Select a booking first.");
      return;
    }
    if (!refundReason.trim() || !refundRef.trim()) {
      setBookingMessage("Refund reason and reference are required.");
      return;
    }

    const allocations: RefundAllocationCommand[] = [];
    let totalRefundCents = 0;

    try {
      for (const dep of refundableDeposits) {
        const raw = (allocationInputs[dep.depositFactId] ?? "").trim();
        if (!raw) continue;
        const cents = parsePhpToCents(raw);
        if (cents <= 0) continue;
        if (cents > dep.remainingRefundableCents) {
          setBookingMessage(
            `Allocation for deposit ${dep.reference} exceeds remaining refundable balance (${formatPhp(dep.remainingRefundableCents)}).`,
          );
          return;
        }
        allocations.push({ receivedDepositFactId: dep.depositFactId, amountCents: cents });
        totalRefundCents += cents;
      }
    } catch (err) {
      setBookingMessage(err instanceof Error ? err.message : "Invalid allocation amount.");
      return;
    }

    if (allocations.length === 0 || totalRefundCents <= 0) {
      setBookingMessage("Enter a positive refund amount on one or more historical deposit rows.");
      return;
    }

    const res = await depositRefundIntent.execute(
      {
        bookingId: selectedBooking.bookingId,
        bookingRevisionId: selectedBooking.currentRevisionId,
        amountCents: totalRefundCents,
        reason: refundReason.trim(),
        reference: refundRef.trim(),
        allocations,
      },
      () => ({
        bookingId: selectedBooking.bookingId,
        command: {
          operationId: crypto.randomUUID(),
          depositFactId: crypto.randomUUID(),
          cashMovementId: crypto.randomUUID(),
          bookingRevisionId: selectedBooking.currentRevisionId,
          amountCents: totalRefundCents,
          reason: refundReason.trim(),
          reference: refundRef.trim(),
          allocations,
          occurredAtUtc: new Date().toISOString(),
        },
      }),
      (p) => refundBookingDeposit(p.bookingId, p.command),
    );

    if (res.ok) {
      setAllocationInputs({});
      await loadAll();
      await loadBookingDetail(selectedBooking.bookingId);
      const msg = `Allocated refund committed (${formatPhp(res.value.amountCents)} across ${allocations.length} deposit fact(s)).`;
      setBookingMessage(msg);
      pushToast(msg);
    } else {
      setBookingMessage(`${res.code ? `[${res.code}] ` : ""}${res.message}`);
    }
  };

  // --- INT-05: Order-Bound Delivery (Create, Durable Dispatch, Durable Resolve) ---
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
    const res = await dispatchDeliveryIntent.execute(
      { deliveryId: del.deliveryId, expectedVersion: del.version },
      () => ({
        deliveryId: del.deliveryId,
        command: {
          operationId: crypto.randomUUID(),
          expectedVersion: del.version,
          occurredAtUtc: new Date().toISOString(),
        },
      }),
      (p) => dispatchDelivery(p.deliveryId, p.command),
    );
    if (res.ok) {
      await loadAll();
      const msg = `Delivery ${res.value.deliveryId.slice(0, 8)}… advanced to ${res.value.status}.`;
      setDeliveryMessage(msg);
      pushToast(msg);
    } else {
      setDeliveryMessage(`${res.code ? `[${res.code}] ` : ""}${res.message}`);
    }
  };

  const handleResolveDelivery = async (del: CafeDeliverySummary, targetStatus: "Delivered" | "Failed") => {
    setDeliveryMessage(null);
    const res = await resolveDeliveryIntent.execute(
      { deliveryId: del.deliveryId, expectedVersion: del.version, targetStatus },
      () => ({
        deliveryId: del.deliveryId,
        command: {
          operationId: crypto.randomUUID(),
          expectedVersion: del.version,
          targetStatus,
          reason: targetStatus === "Failed" ? "Customer unreachable" : null,
          occurredAtUtc: new Date().toISOString(),
        },
      }),
      (p) => resolveDelivery(p.deliveryId, p.command),
    );
    if (res.ok) {
      await loadAll();
      const msg = `Delivery ${res.value.deliveryId.slice(0, 8)}… resolved as ${res.value.status}.`;
      setDeliveryMessage(msg);
      pushToast(msg);
    } else {
      setDeliveryMessage(`${res.code ? `[${res.code}] ` : ""}${res.message}`);
    }
  };

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
            <h1 className="r3-page-title">Bookings &amp; Order-Bound Deliveries</h1>
            <p className="r3-page-subtitle">
              Live Host Bookings (Event / CoffeeCart + historical allocated Cash deposits) and Takeout-order-bound Deliveries.
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
                  <DurableIntentBanner
                    label="Booking Confirm"
                    operationId={confirmBookingIntent.pending?.payload.command.operationId ?? null}
                    pending={confirmBookingIntent.pending}
                    corruptReason={confirmBookingIntent.corruptReason}
                    authorityMismatch={confirmBookingIntent.authorityMismatch}
                    inFlight={confirmBookingIntent.inFlight}
                    onRetry={() =>
                      void confirmBookingIntent
                        .retryPending((p) => confirmBooking(p.bookingId, p.command))
                        .then((r) => {
                          if (r.ok) {
                            setBookingMessage(`Booking ${r.value.bookingId.slice(0, 8)}… confirm retry succeeded.`);
                            void loadAll();
                          } else {
                            setBookingMessage(r.message);
                          }
                        })
                    }
                    onClearRejected={() => confirmBookingIntent.clearPending(false)}
                    onClearCorrupt={() => confirmBookingIntent.clearPending(true)}
                  />
                  <div className="r3-table-wrap" tabIndex={0} role="region" aria-label="Live Bookings">
                    <table className="r3-table">
                      <caption className="r3-sr-only">Live Bookings</caption>
                      <thead>
                        <tr>
                          <th scope="col">Booking</th>
                          <th scope="col">Mode</th>
                          <th scope="col">Customer</th>
                          <th scope="col">Quoted / Received / Outstanding</th>
                          <th scope="col">Status</th>
                          <th scope="col">Actions</th>
                        </tr>
                      </thead>
                      <tbody>
                        {bookings.length === 0 ? (
                          <tr>
                            <td colSpan={6} className="r3-text-muted">No bookings found.</td>
                          </tr>
                        ) : (
                          bookings.map((bk) => (
                            <tr key={bk.bookingId}>
                              <th scope="row"><code>{bk.bookingId.slice(0, 8)}…</code> (v{bk.bookingVersion})</th>
                              <td>{bk.bookingMode}</td>
                              <td>{bk.customerNameSnapshot}</td>
                              <td className="r3-tabular">
                                {formatPhp(bk.quotedTotalCents ?? 0)} / {formatPhp(bk.depositReceivedTotalCents ?? 0)} /{" "}
                                {formatPhp(bk.preFulfillmentOutstandingBalanceCents ?? 0)}
                              </td>
                              <td><span className="r3-badge r3-badge--info">{bk.status}</span></td>
                              <td style={{ display: "flex", gap: 6, flexWrap: "wrap" }}>
                                <button
                                  type="button"
                                  className="r3-btn r3-btn--outline r3-btn--sm"
                                  onClick={() => setDepositBookingId(bk.bookingId)}
                                >
                                  Inspect Deposits
                                </button>
                                {canManageBookings && bk.status === "Draft" && (
                                  <button
                                    type="button"
                                    className="r3-btn r3-btn--primary r3-btn--sm"
                                    disabled={confirmBookingIntent.inFlight}
                                    onClick={() => void handleConfirmBooking(bk)}
                                  >
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
                    <DurableIntentBanner
                      label="Booking Create"
                      operationId={createBookingIntent.pending?.payload.operationId ?? null}
                      pending={createBookingIntent.pending}
                      corruptReason={createBookingIntent.corruptReason}
                      authorityMismatch={createBookingIntent.authorityMismatch}
                      inFlight={createBookingIntent.inFlight}
                      onRetry={() =>
                        void createBookingIntent.retryPending(createBooking).then((r) => {
                          if (r.ok) {
                            setDepositBookingId(r.value.bookingId);
                            setBookingMessage(`Booking create retry succeeded (${r.value.bookingId.slice(0, 8)}…).`);
                            void loadAll();
                          } else {
                            setBookingMessage(r.message);
                          }
                        })
                      }
                      onClearRejected={() => createBookingIntent.clearPending(false)}
                      onClearCorrupt={() => createBookingIntent.clearPending(true)}
                    />
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
                        <div className="r3-card-title">Historical Booking Deposits &amp; Allocated Refunds (INT-04)</div>
                        <div className="r3-card-subtitle">
                          Loaded from Host SQLite across reloads; supports partial and multi-deposit refund allocations.
                        </div>
                      </div>
                    </div>

                    <div className="r3-card-body" style={{ paddingBottom: 0, maxWidth: 620 }}>
                      <label>
                        <div className="r3-text-xs r3-text-muted">Selected Booking</div>
                        <select className="r3-input" aria-label="Deposit Booking" value={depositBookingId} onChange={(e) => setDepositBookingId(e.target.value)}>
                          {bookings.map((b) => (
                            <option key={b.bookingId} value={b.bookingId}>
                              {b.bookingId.slice(0, 8)}… — {b.customerNameSnapshot} ({b.status})
                            </option>
                          ))}
                        </select>
                      </label>
                      {selectedBooking && (
                        <div className="r3-text-sm r3-text-muted" style={{ marginTop: 8 }}>
                          Quoted: <strong>{formatPhp(selectedBooking.quotedTotalCents ?? 0)}</strong> · Received:{" "}
                          <strong>{formatPhp(selectedBooking.depositReceivedTotalCents ?? 0)}</strong> · Refunded:{" "}
                          <strong>{formatPhp(selectedBooking.depositRefundedTotalCents ?? 0)}</strong> · Refundable Credit:{" "}
                          <strong>{formatPhp(selectedBooking.preFulfillmentDepositCreditCents ?? 0)}</strong> · Outstanding:{" "}
                          <strong>{formatPhp(selectedBooking.preFulfillmentOutstandingBalanceCents ?? 0)}</strong>
                        </div>
                      )}
                    </div>

                    <DurableIntentBanner
                      label="Deposit Receive"
                      operationId={depositReceiveIntent.pending?.payload.command.operationId ?? null}
                      pending={depositReceiveIntent.pending}
                      corruptReason={depositReceiveIntent.corruptReason}
                      authorityMismatch={depositReceiveIntent.authorityMismatch}
                      inFlight={depositReceiveIntent.inFlight}
                      onRetry={() =>
                        void depositReceiveIntent
                          .retryPending((p) => receiveBookingDeposit(p.bookingId, p.command))
                          .then((r) => {
                            if (r.ok) {
                              setBookingMessage(`Deposit receive retry confirmed (${formatPhp(r.value.amountCents)}).`);
                              void loadAll();
                            } else {
                              setBookingMessage(r.message);
                            }
                          })
                      }
                      onClearRejected={() => depositReceiveIntent.clearPending(false)}
                      onClearCorrupt={() => depositReceiveIntent.clearPending(true)}
                    />

                    {canDepositReceive && (
                      <form onSubmit={(e) => void handleReceiveDeposit(e)} style={{ display: "grid", gap: 12, maxWidth: 560, marginBottom: 20 }}>
                        <label>
                          <div className="r3-text-xs r3-text-muted">Cash Deposit Amount (PHP)</div>
                          <input className="r3-input" aria-label="Cash Deposit Amount" value={depositAmountPhp} onChange={(e) => setDepositAmountPhp(e.target.value)} />
                        </label>
                        <label>
                          <div className="r3-text-xs r3-text-muted">Reference</div>
                          <input className="r3-input" aria-label="Deposit Reference" value={depositRef} onChange={(e) => setDepositRef(e.target.value)} />
                        </label>
                        <div>
                          <button type="submit" className="r3-btn r3-btn--primary" disabled={depositReceiveIntent.inFlight}>
                            Receive Cash Deposit
                          </button>
                        </div>
                      </form>
                    )}

                    <DurableIntentBanner
                      label="Deposit Refund"
                      operationId={depositRefundIntent.pending?.payload.command.operationId ?? null}
                      pending={depositRefundIntent.pending}
                      corruptReason={depositRefundIntent.corruptReason}
                      authorityMismatch={depositRefundIntent.authorityMismatch}
                      inFlight={depositRefundIntent.inFlight}
                      onRetry={() =>
                        void depositRefundIntent
                          .retryPending((p) => refundBookingDeposit(p.bookingId, p.command))
                          .then((r) => {
                            if (r.ok) {
                              setAllocationInputs({});
                              setBookingMessage(`Deposit refund retry confirmed (${formatPhp(r.value.amountCents)}).`);
                              void loadAll();
                            } else {
                              setBookingMessage(r.message);
                            }
                          })
                      }
                      onClearRejected={() => depositRefundIntent.clearPending(false)}
                      onClearCorrupt={() => depositRefundIntent.clearPending(true)}
                    />

                    {canDepositRefund && (
                      <form onSubmit={(e) => void handleAllocatedRefund(e)} style={{ display: "grid", gap: 12 }}>
                        <div className="r3-table-wrap" tabIndex={0} role="region" aria-label="Historical Refundable Deposit Facts">
                          <table className="r3-table">
                            <caption className="r3-sr-only">Historical Refundable Deposit Facts</caption>
                            <thead>
                              <tr>
                                <th scope="col">Deposit Fact</th>
                                <th scope="col">Reference</th>
                                <th scope="col">Received</th>
                                <th scope="col">Remaining Refundable</th>
                                <th scope="col">Refund Allocation (PHP)</th>
                              </tr>
                            </thead>
                            <tbody>
                              {refundableDeposits.length === 0 ? (
                                <tr>
                                  <td colSpan={5} className="r3-text-muted">No remaining refundable received deposits on this booking.</td>
                                </tr>
                              ) : (
                                refundableDeposits.map((dep) => (
                                  <tr key={dep.depositFactId}>
                                    <th scope="row"><code>{dep.depositFactId.slice(0, 8)}…</code></th>
                                    <td>{dep.reference}</td>
                                    <td className="r3-tabular">{formatPhp(dep.amountCents)}</td>
                                    <td className="r3-tabular">{formatPhp(dep.remainingRefundableCents)}</td>
                                    <td>
                                      <input
                                        className="r3-input"
                                        aria-label={`Refund allocation for ${dep.reference}`}
                                        value={allocationInputs[dep.depositFactId] ?? ""}
                                        onChange={(e) =>
                                          setAllocationInputs((prev) => ({
                                            ...prev,
                                            [dep.depositFactId]: e.target.value,
                                          }))
                                        }
                                        placeholder={(dep.remainingRefundableCents / 100).toFixed(2)}
                                        style={{ maxWidth: 140 }}
                                      />
                                    </td>
                                  </tr>
                                ))
                              )}
                            </tbody>
                          </table>
                        </div>
                        {refundableDeposits.length > 0 && (
                          <div style={{ display: "grid", gap: 10, maxWidth: 560 }}>
                            <label>
                              <div className="r3-text-xs r3-text-muted">Refund Reason</div>
                              <input className="r3-input" aria-label="Refund Reason" value={refundReason} onChange={(e) => setRefundReason(e.target.value)} />
                            </label>
                            <label>
                              <div className="r3-text-xs r3-text-muted">Refund Reference</div>
                              <input className="r3-input" aria-label="Refund Reference" value={refundRef} onChange={(e) => setRefundRef(e.target.value)} />
                            </label>
                            <div>
                              <button type="submit" className="r3-btn r3-btn--outline" disabled={depositRefundIntent.inFlight}>
                                Commit Allocated Refund
                              </button>
                            </div>
                          </div>
                        )}
                      </form>
                    )}
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
                  <DurableIntentBanner
                    label="Delivery Dispatch"
                    operationId={dispatchDeliveryIntent.pending?.payload.command.operationId ?? null}
                    pending={dispatchDeliveryIntent.pending}
                    corruptReason={dispatchDeliveryIntent.corruptReason}
                    authorityMismatch={dispatchDeliveryIntent.authorityMismatch}
                    inFlight={dispatchDeliveryIntent.inFlight}
                    onRetry={() =>
                      void dispatchDeliveryIntent
                        .retryPending((p) => dispatchDelivery(p.deliveryId, p.command))
                        .then((r) => {
                          if (r.ok) {
                            setDeliveryMessage(`Delivery dispatch retry confirmed (${r.value.status}).`);
                            void loadAll();
                          } else {
                            setDeliveryMessage(r.message);
                          }
                        })
                    }
                    onClearRejected={() => dispatchDeliveryIntent.clearPending(false)}
                    onClearCorrupt={() => dispatchDeliveryIntent.clearPending(true)}
                  />
                  <DurableIntentBanner
                    label="Delivery Resolve"
                    operationId={resolveDeliveryIntent.pending?.payload.command.operationId ?? null}
                    pending={resolveDeliveryIntent.pending}
                    corruptReason={resolveDeliveryIntent.corruptReason}
                    authorityMismatch={resolveDeliveryIntent.authorityMismatch}
                    inFlight={resolveDeliveryIntent.inFlight}
                    onRetry={() =>
                      void resolveDeliveryIntent
                        .retryPending((p) => resolveDelivery(p.deliveryId, p.command))
                        .then((r) => {
                          if (r.ok) {
                            setDeliveryMessage(`Delivery resolve retry confirmed (${r.value.status}).`);
                            void loadAll();
                          } else {
                            setDeliveryMessage(r.message);
                          }
                        })
                    }
                    onClearRejected={() => resolveDeliveryIntent.clearPending(false)}
                    onClearCorrupt={() => resolveDeliveryIntent.clearPending(true)}
                  />
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
                                  <button
                                    type="button"
                                    className="r3-btn r3-btn--outline r3-btn--sm"
                                    disabled={dispatchDeliveryIntent.inFlight}
                                    onClick={() => void handleDispatchDelivery(del)}
                                  >
                                    Dispatch
                                  </button>
                                )}
                                {canAdvanceDeliveries && del.status === "Dispatched" && (
                                  <>
                                    <button
                                      type="button"
                                      className="r3-btn r3-btn--primary r3-btn--sm"
                                      disabled={resolveDeliveryIntent.inFlight}
                                      onClick={() => void handleResolveDelivery(del, "Delivered")}
                                    >
                                      Mark Delivered
                                    </button>
                                    <button
                                      type="button"
                                      className="r3-btn r3-btn--outline r3-btn--sm"
                                      disabled={resolveDeliveryIntent.inFlight}
                                      onClick={() => void handleResolveDelivery(del, "Failed")}
                                    >
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
                    <DurableIntentBanner
                      label="Delivery Create"
                      operationId={createDeliveryIntent.pending?.payload.operationId ?? null}
                      pending={createDeliveryIntent.pending}
                      corruptReason={createDeliveryIntent.corruptReason}
                      authorityMismatch={createDeliveryIntent.authorityMismatch}
                      inFlight={createDeliveryIntent.inFlight}
                      onRetry={() =>
                        void createDeliveryIntent.retryPending(createDelivery).then((r) => {
                          if (r.ok) {
                            setDeliveryMessage(`Delivery create retry confirmed (${r.value.status}).`);
                            void loadAll();
                          } else {
                            setDeliveryMessage(r.message);
                          }
                        })
                      }
                      onClearRejected={() => createDeliveryIntent.clearPending(false)}
                      onClearCorrupt={() => createDeliveryIntent.clearPending(true)}
                    />
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
