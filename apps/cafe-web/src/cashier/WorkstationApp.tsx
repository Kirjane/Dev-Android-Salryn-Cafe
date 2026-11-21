import {
  ClipboardList,
  Coffee,
  RefreshCw,
  ShieldCheck,
  UtensilsCrossed,
  Wallet,
} from "lucide-react";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  Alert,
  BrandMark,
  Button,
  CafeShell,
  Card,
  CardBody,
  CardHeader,
  Dialog,
  ErrorBoundary,
  FormField,
  StatusBadge,
  TextInput,
  ToastProvider,
  useToast,
  type CafeShellNavItem,
} from "@salryn/cafe-ui";
import {
  HostApiError,
  appendRejectedIntentHistory,
  createLiveHostApi,
  formatPhp,
  loadPendingHostIntent,
  parsePhpToCents,
  savePendingHostIntent,
} from "../api/hostClient";
import { KitchenDisplay } from "../kitchen/KitchenDisplay";
import { OrdersBoard } from "../orders/OrdersBoard";
import { CashierWorkstation } from "./CashierWorkstation";
import type {
  CafeOrderDetail,
  CafeOrderSummary,
  CafePrintStation,
  CustomerAccount,
  HostSession,
  HostShiftInfo,
  MenuPublication,
  PendingHostIntent,
  WorkstationApi,
} from "./types";

export type WorkstationAppProps = {
  api?: WorkstationApi;
  initialNav?: string;
  onOpenBackoffice?: () => void;
};

function WorkstationInner({
  api,
  initialNav = "cashier",
  onOpenBackoffice,
}: {
  api: WorkstationApi;
  initialNav?: string;
  onOpenBackoffice?: () => void;
}) {
  const { notify } = useToast();
  const [activeNav, setActiveNav] = useState(initialNav);
  const [session, setSession] = useState<HostSession>({ isAuthenticated: false });
  const [menu, setMenu] = useState<MenuPublication | null>(null);
  const [orders, setOrders] = useState<CafeOrderSummary[]>([]);
  const [activeOrder, setActiveOrder] = useState<CafeOrderDetail | null>(null);
  const [lastCompletion, setLastCompletion] = useState<{
    orderId: string;
    saleId: string;
    receiptNumber: string;
  } | null>(null);
  const [customers, setCustomers] = useState<CustomerAccount[]>([]);
  const [printStations, setPrintStations] = useState<CafePrintStation[]>([]);
  const [shift, setShift] = useState<HostShiftInfo | null>(null);

  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [statusMessage, setStatusMessage] = useState("Connecting to the local Host…");
  const [busy, setBusy] = useState(false);
  const lockRef = useRef(false);

  const [pending, setPending] = useState<PendingHostIntent | null>(() => loadPendingHostIntent());
  const [rejectionReason, setRejectionReason] = useState("");

  const [openShiftOpen, setOpenShiftOpen] = useState(false);
  const [openingCashPhp, setOpeningCashPhp] = useState("0");
  const [blindCloseOpen, setBlindCloseOpen] = useState(false);
  const [blindCountPhp, setBlindCountPhp] = useState("");
  const [blindCloseError, setBlindCloseError] = useState<string | null>(null);
  const [blindCloseResult, setBlindCloseResult] = useState<HostShiftInfo | null>(null);

  const [centralOrigin, setCentralOrigin] = useState("");
  const [centralTenant, setCentralTenant] = useState("");
  const [centralStore, setCentralStore] = useState("");
  const [centralPin, setCentralPin] = useState("");
  const [joinCode, setJoinCode] = useState("");
  const [syncPassword, setSyncPassword] = useState("");

  const permissions = session.user?.permissions ?? [];
  const permitted = useCallback((perm: string) => permissions.includes(perm), [permissions]);
  const blocked = busy || Boolean(pending);

  const refreshHostState = useCallback(
    async (currentSession: HostSession) => {
      if (!currentSession.isAuthenticated || !currentSession.user) return;
      const perms = currentSession.user.permissions;
      const [publishedMenu, savedOrders] = await Promise.all([
        api.getPublishedMenu(),
        api.getOrders(),
      ]);
      setMenu(publishedMenu);
      setOrders(savedOrders);

      const warnings: string[] = [];
      const extras: Promise<void>[] = [];
      if (perms.includes("Receivables.View")) {
        extras.push(
          api
            .getCustomers()
            .then(setCustomers)
            .catch((err) => {
              warnings.push(err instanceof Error ? err.message : "Could not load AR customers.");
            }),
        );
      }
      if (perms.includes("Shift.OpenClose")) {
        extras.push(
          api
            .getShift()
            .then((res) => setShift(res.shift))
            .catch((err) => {
              warnings.push(err instanceof Error ? err.message : "Could not load shift status.");
            }),
        );
      }
      if (perms.includes("Printing.View")) {
        extras.push(
          api
            .getPrintStations()
            .then(setPrintStations)
            .catch((err) => {
              warnings.push(err instanceof Error ? err.message : "Could not load printer stations.");
            }),
        );
      }
      await Promise.all(extras);
      if (warnings.length > 0) {
        notify("warning", "Partial station refresh", warnings.join(" · "));
      }
    },
    [api, notify],
  );

  const runExclusive = useCallback(async (action: () => Promise<void>): Promise<boolean> => {
    if (lockRef.current) return false;
    lockRef.current = true;
    setBusy(true);
    try {
      await action();
      return true;
    } catch (error) {
      setStatusMessage(
        error instanceof Error
          ? error.message
          : "Host connection failed. Retained requests can be retried.",
      );
      return false;
    } finally {
      lockRef.current = false;
      setBusy(false);
    }
  }, []);

  useEffect(() => {
    void runExclusive(async () => {
      const current = await api.getSession();
      setSession(current);
      if (current.isAuthenticated) {
        await refreshHostState(current);
        setStatusMessage("Local Host connected.");
      } else {
        setStatusMessage("Sign in to the Salryn Cafe Local Host to begin.");
      }
    });
  }, [api, refreshHostState, runExclusive]);

  const sendIntent = useCallback(
    async (intent: PendingHostIntent, activeSession: HostSession, activeMenu: MenuPublication | null) => {
      if (!activeSession.user || intent.actor !== activeSession.user.userId) {
        throw new Error("The original operator must sign in to retry this request.");
      }
      if (!activeMenu?.storeId || intent.storeId !== activeMenu.storeId) {
        throw new Error(
          "The retained request belongs to another Host store. Preserve it for administrator reconciliation.",
        );
      }

      let result: Record<string, unknown>;
      try {
        result = await api.sendRawIntent(intent.path, intent.body);
      } catch (error) {
        if (error instanceof HostApiError && error.isDeterministicRejection) {
          const rejected: PendingHostIntent = { ...intent, rejection: error.message };
          savePendingHostIntent(rejected);
          setPending(rejected);
        }
        throw error;
      }

      if (/\/orders\/[^/]+\/complete$/.test(intent.path)) {
        const orderId = typeof result.orderId === "string" ? result.orderId : "";
        const saleId = typeof result.saleId === "string" ? result.saleId : "";
        const receiptNumber = typeof result.receiptNumber === "string" ? result.receiptNumber : "";
        if (!orderId || !saleId || !receiptNumber || !intent.path.includes(`/${orderId}/`)) {
          throw new Error(
            "Host did not return matching sale and receipt evidence. The original payment request is retained.",
          );
        }
        setLastCompletion({ orderId, saleId, receiptNumber });
        setStatusMessage(`Payment confirmed. Receipt ${receiptNumber}.`);
        notify("success", `Payment confirmed · ${receiptNumber}`);
      } else if (intent.path === "/api/shift/close") {
        const shiftInfo = (result.shift ?? null) as HostShiftInfo | null;
        if (shiftInfo) {
          setBlindCloseResult(shiftInfo);
          setShift(shiftInfo);
        }
        setStatusMessage("Blind shift close committed.");
      } else {
        setStatusMessage("Host confirmed the action.");
      }

      savePendingHostIntent(null);
      setPending(null);

      const orderIdFromResponse = typeof result.orderId === "string" ? result.orderId : null;
      if (orderIdFromResponse) {
        const detail = await api.getOrderDetail(orderIdFromResponse);
        setActiveOrder(detail);
      }
      await refreshHostState(activeSession);
    },
    [api, notify, refreshHostState],
  );

  const dispatchIntent = useCallback(
    async (path: string, body: Record<string, unknown>): Promise<boolean> =>
      runExclusive(async () => {
        if (!session.user) throw new Error("Sign in before sending a request.");
        if (pending) throw new Error("Retry the retained request before creating another action.");
        if (!menu?.storeId) throw new Error("A Host menu with store authority is required before submitting actions.");
        const intent: PendingHostIntent = {
          storeId: menu.storeId,
          actor: session.user.userId,
          path,
          body,
        };
        savePendingHostIntent(intent);
        setPending(intent);
        await sendIntent(intent, session, menu);
      }),
    [menu, pending, runExclusive, sendIntent, session],
  );

  async function handleLogin(e: React.FormEvent) {
    e.preventDefault();
    await runExclusive(async () => {
      const current = await api.login(username, password);
      setPassword("");
      setSession(current);
      await refreshHostState(current);
      setStatusMessage("Signed in to local Host.");
    });
  }

  async function handleLogout() {
    await runExclusive(async () => {
      await api.logout();
      setSession({ isAuthenticated: false });
      setMenu(null);
      setOrders([]);
      setActiveOrder(null);
      setCustomers([]);
      setPrintStations([]);
      setShift(null);
      setStatusMessage("Signed out.");
    });
  }

  async function handleArchiveRejected() {
    if (!pending || !rejectionReason.trim() || pending.actor !== session.user?.userId) return;
    appendRejectedIntentHistory({
      request: pending,
      reason: rejectionReason.trim(),
      archivedAtUtc: new Date().toISOString(),
    });
    savePendingHostIntent(null);
    setPending(null);
    setRejectionReason("");
    setStatusMessage("Rejected request retained in local rejection history. Correct the order before retrying.");
  }

  async function handleSelectOrder(orderId: string) {
    await runExclusive(async () => {
      const detail = await api.getOrderDetail(orderId);
      setActiveOrder(detail);
      setActiveNav("cashier");
    });
  }

  async function handleOpenShift() {
    await runExclusive(async () => {
      const cents = parsePhpToCents(openingCashPhp);
      const res = await api.openShift(cents);
      setShift(res.shift);
      setOpenShiftOpen(false);
      notify("success", "Cashier shift opened");
    });
  }

  const navItems: CafeShellNavItem[] = useMemo(
    () => [
      { id: "cashier", label: "Cafe Cashier", group: "Front Counter", Icon: Coffee },
      {
        id: "orders",
        label: "Orders & Lifecycle",
        group: "Front Counter",
        Icon: ClipboardList,
        badge: orders.filter((o) => !["Completed", "Cancelled"].includes(o.state)).length || undefined,
      },
      {
        id: "kitchen",
        label: "Kitchen & Barista",
        group: "Preparation",
        Icon: UtensilsCrossed,
        badge: orders.filter((o) => ["Submitted", "Preparing"].includes(o.state)).length || undefined,
      },
      { id: "shift-sync", label: "Shift & Central Sync", group: "Station Controls", Icon: ShieldCheck },
    ],
    [orders],
  );

  if (!session.isAuthenticated) {
    return (
      <div
        style={{
          minHeight: "100vh",
          display: "grid",
          placeItems: "center",
          padding: 20,
          background: "var(--salryn-cream)",
          color: "var(--salryn-espresso)",
        }}
      >
        <div style={{ width: "100%", maxWidth: 440, display: "grid", gap: 16 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
            <BrandMark variant="logo" height={44} alt="Salryn Cafe logo" />
            <div>
              <div style={{ fontSize: "0.75rem", color: "var(--salryn-mocha)", textTransform: "uppercase", letterSpacing: "0.06em", fontWeight: 700 }}>
                Salryn Cafe · Local Host
              </div>
              <h1 style={{ margin: 0, fontSize: "1.6rem" }}>Cafe workstation</h1>
            </div>
          </div>

          <p role="status" aria-live="polite" style={{ margin: 0, color: "var(--salryn-mocha)" }}>
            {statusMessage}
          </p>

          <Card>
            <CardHeader title="Operator sign in" subtitle="Sign in against local Salryn Cafe Host authority." />
            <CardBody>
              <form onSubmit={(e) => void handleLogin(e)} style={{ display: "grid", gap: 12 }}>
                <FormField label="Username" required>
                  <TextInput
                    autoComplete="username"
                    value={username}
                    onChange={(e) => setUsername(e.target.value)}
                    required
                  />
                </FormField>
                <FormField label="Password" required>
                  <TextInput
                    type="password"
                    autoComplete="current-password"
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    required
                  />
                </FormField>
                <Button type="submit" variant="primary" size="lg" loading={busy}>
                  Sign in
                </Button>
              </form>
            </CardBody>
          </Card>
        </div>
      </div>
    );
  }

  return (
    <CafeShell
      storeName="Salryn Cafe"
      stationLabel="Local Host Workstation (Salryn.Cafe)"
      connectionStatus={pending ? { label: "Pending Confirmation", tone: "warning" } : { label: "Host Connected", tone: "success" }}
      operatorName={session.user?.displayName ?? "Operator"}
      roleName={session.user?.roleName ?? "Staff"}
      navItems={navItems}
      activeNav={activeNav}
      onNavigate={setActiveNav}
      onSignOut={() => void handleLogout()}
      banner={
        <div style={{ display: "grid", gap: 12 }}>
          <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center", justifyContent: "space-between", gap: 10 }}>
            <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center", gap: 8 }}>
              <h1 style={{ margin: 0, fontSize: "1.25rem" }}>Cafe workstation</h1>
              <StatusBadge tone="success">Host SQLite Live</StatusBadge>
              <StatusBadge tone={shift?.shiftState === "Open" ? "info" : "neutral"}>
                {shift?.shiftState === "Open" ? "Shift Open" : "Shift Closed / Unopened"}
              </StatusBadge>
              <span role="status" aria-live="polite" style={{ fontSize: "0.875rem", color: "var(--salryn-mocha)" }}>
                {statusMessage}
              </span>
            </div>
            <div style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
              {onOpenBackoffice ? (
                <Button size="sm" variant="secondary" onClick={onOpenBackoffice}>
                  Back Office &amp; Reports
                </Button>
              ) : null}
              <Button
                size="sm"
                variant="secondary"
                leftIcon={<RefreshCw size={14} />}
                disabled={busy}
                onClick={() => void runExclusive(() => refreshHostState(session))}
              >
                Refresh from Host
              </Button>
              {permitted("Shift.OpenClose") ? (
                shift?.shiftState === "Open" ? (
                  <Button
                    size="sm"
                    variant="secondary"
                    leftIcon={<Wallet size={14} />}
                    disabled={blocked}
                    onClick={() => {
                      setBlindCloseResult(null);
                      setBlindCloseError(null);
                      setBlindCountPhp("");
                      setBlindCloseOpen(true);
                    }}
                  >
                    Blind Shift Close
                  </Button>
                ) : (
                  <Button
                    size="sm"
                    variant="secondary"
                    leftIcon={<Wallet size={14} />}
                    disabled={blocked}
                    onClick={() => setOpenShiftOpen(true)}
                  >
                    Open Shift
                  </Button>
                )
              ) : null}
            </div>
          </div>

          {pending ? (
            <Card>
              <CardHeader
                title="Action needs confirmation"
                subtitle={`Original request retained across reload · ${pending.path}`}
                action={<StatusBadge tone="warning">Retained Intent</StatusBadge>}
              />
              <CardBody>
                <div style={{ display: "grid", gap: 12 }}>
                  <p style={{ margin: 0 }}>
                    The original request is retained across reload. Confirm or retry it before creating another action.
                  </p>
                  {pending.rejection ? (
                    <Alert tone="danger" title="Host rejected this request" description={pending.rejection} />
                  ) : null}
                  <div style={{ display: "flex", flexWrap: "wrap", gap: 10 }}>
                    <Button
                      variant="accent"
                      loading={busy}
                      disabled={pending.actor !== session.user?.userId}
                      onClick={() => void runExclusive(() => sendIntent(pending, session, menu))}
                    >
                      Retry retained request
                    </Button>
                  </div>
                  {pending.rejection ? (
                    <div style={{ display: "grid", gap: 10, paddingTop: 8, borderTop: "1px solid var(--salryn-border)" }}>
                      <FormField label="Reason to archive rejected request" required>
                        <TextInput
                          value={rejectionReason}
                          onChange={(e) => setRejectionReason(e.target.value)}
                          placeholder="Reason for local audit history..."
                        />
                      </FormField>
                      <div>
                        <Button
                          variant="danger"
                          disabled={busy || !rejectionReason.trim() || pending.actor !== session.user?.userId}
                          onClick={() => void handleArchiveRejected()}
                        >
                          Archive rejected request
                        </Button>
                      </div>
                    </div>
                  ) : null}
                </div>
              </CardBody>
            </Card>
          ) : null}
        </div>
      }
    >
      {activeNav === "cashier" ? (
        <CashierWorkstation
          api={api}
          menu={menu}
          customers={customers}
          printStations={printStations}
          permissions={permissions}
          activeOrder={activeOrder}
          lastCompletion={lastCompletion}
          blocked={blocked}
          onDispatchIntent={dispatchIntent}
          onClearActiveOrder={() => setActiveOrder(null)}
        />
      ) : null}

      {activeNav === "orders" ? (
        <OrdersBoard
          api={api}
          orders={orders}
          printStations={printStations}
          permissions={permissions}
          blocked={blocked}
          onDispatchIntent={dispatchIntent}
          onSelectOrder={handleSelectOrder}
        />
      ) : null}

      {activeNav === "kitchen" ? (
        <KitchenDisplay
          api={api}
          orders={orders}
          printStations={printStations}
          permissions={permissions}
          blocked={blocked}
          onDispatchIntent={dispatchIntent}
        />
      ) : null}

      {activeNav === "shift-sync" ? (
        <div style={{ display: "grid", gap: 16 }}>
          {permitted("Shift.OpenClose") ? (
            <Card>
              <CardHeader
                title="Drawer & Physical Count"
                subtitle={shift?.shiftState === "Open" ? `Active Shift ${shift.shiftId?.slice(0, 8) ?? ""}` : "No shift currently open."}
              />
              <CardBody>
                <div style={{ display: "flex", flexWrap: "wrap", gap: 10 }}>
                  {shift?.shiftState !== "Open" ? (
                    <Button variant="primary" disabled={blocked} onClick={() => setOpenShiftOpen(true)}>
                      Open Shift
                    </Button>
                  ) : (
                    <Button
                      variant="primary"
                      disabled={blocked}
                      onClick={() => {
                        setBlindCloseResult(null);
                        setBlindCloseError(null);
                        setBlindCountPhp("");
                        setBlindCloseOpen(true);
                      }}
                    >
                      Blind Shift Close
                    </Button>
                  )}
                </div>
              </CardBody>
            </Card>
          ) : null}

          <Card>
            <CardHeader
              title="Central Pairing & Synchronization"
              subtitle="Bind this Windows Host to Salryn Cafe Central HTTPS and synchronize confirmed operations."
            />
            <CardBody>
              <div style={{ display: "grid", gap: 12 }}>
                {permitted("StoreTerminal.Manage") ? (
                  <>
                    <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: 10 }}>
                      <FormField label="Approved Central HTTPS Origin">
                        <TextInput value={centralOrigin} onChange={(e) => setCentralOrigin(e.target.value)} />
                      </FormField>
                      <FormField label="Central Tenant ID">
                        <TextInput value={centralTenant} onChange={(e) => setCentralTenant(e.target.value)} />
                      </FormField>
                      <FormField label="Central Store ID">
                        <TextInput value={centralStore} onChange={(e) => setCentralStore(e.target.value)} />
                      </FormField>
                      <FormField label="Independent Server Signing Public Key">
                        <TextInput value={centralPin} onChange={(e) => setCentralPin(e.target.value)} />
                      </FormField>
                      <FormField label="One-Time Central Join Code">
                        <TextInput value={joinCode} onChange={(e) => setJoinCode(e.target.value)} />
                      </FormField>
                    </div>
                    <div>
                      <Button
                        variant="secondary"
                        disabled={busy || !joinCode.trim()}
                        onClick={() =>
                          void runExclusive(async () => {
                            await api.enrollCentral({
                              configuration: {
                                httpsOrigin: centralOrigin,
                                tenantId: centralTenant,
                                storeId: centralStore,
                                pinnedServerKey: centralPin,
                              },
                              joinCode,
                              deviceName: "Windows Cafe Host",
                            });
                            setJoinCode("");
                            setStatusMessage(
                              "Enrollment requested. Administrator must approve the exact Windows device before synchronization.",
                            );
                          })
                        }
                      >
                        Request Windows Device Enrollment
                      </Button>
                    </div>
                  </>
                ) : null}

                <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: 10, alignItems: "end" }}>
                  <FormField label="Current operator password for Central">
                    <TextInput
                      type="password"
                      autoComplete="current-password"
                      value={syncPassword}
                      onChange={(e) => setSyncPassword(e.target.value)}
                    />
                  </FormField>
                  <div>
                    <Button
                      variant="primary"
                      disabled={busy || !syncPassword}
                      onClick={() =>
                        void runExclusive(async () => {
                          const supplied = syncPassword;
                          setSyncPassword("");
                          const res = await api.synchronizeCentral(supplied);
                          setStatusMessage(
                            `Central confirmed ${res.applied} operations; ${res.pending} remain pending.`,
                          );
                        })
                      }
                    >
                      Connect and synchronize Windows operations
                    </Button>
                  </div>
                </div>
              </div>
            </CardBody>
          </Card>
        </div>
      ) : null}

      <Dialog
        open={openShiftOpen}
        title="Open Cashier Shift"
        description="Enter verified opening float before starting shift sales."
        onClose={() => setOpenShiftOpen(false)}
        footer={
          <>
            <Button variant="secondary" onClick={() => setOpenShiftOpen(false)}>
              Cancel
            </Button>
            <Button variant="primary" loading={busy} onClick={() => void handleOpenShift()}>
              Confirm Shift Open
            </Button>
          </>
        }
      >
        <FormField label="Opening Float Cash (PHP)" required>
          <TextInput
            inputMode="decimal"
            placeholder="0.00"
            value={openingCashPhp}
            onChange={(e) => setOpeningCashPhp(e.target.value)}
          />
        </FormField>
      </Dialog>

      <Dialog
        open={blindCloseOpen}
        title="Blind Shift Close"
        description="Enter your physical drawer cash count. Expected totals are revealed only after server close."
        onClose={() => setBlindCloseOpen(false)}
        footer={
          <>
            <Button variant="secondary" onClick={() => setBlindCloseOpen(false)}>
              Close
            </Button>
            {!blindCloseResult ? (
              <Button
                variant="primary"
                loading={busy}
                onClick={() => {
                  setBlindCloseError(null);
                  let countedCashCents: number;
                  try {
                    countedCashCents = parsePhpToCents(blindCountPhp || "0");
                  } catch (error) {
                    setBlindCloseError(error instanceof Error ? error.message : "Enter a valid PHP cash count.");
                    return;
                  }
                  void dispatchIntent("/api/shift/close", {
                    countedCashCents,
                    physicalCountConfirmed: true,
                    clientRequestId: crypto.randomUUID(),
                    note: "Blind physical count from Cafe workstation",
                  });
                }}
              >
                Submit Blind Count
              </Button>
            ) : null}
          </>
        }
      >
        {!blindCloseResult ? (
          <div style={{ display: "grid", gap: 12 }}>
            {blindCloseError ? <Alert tone="danger" title="Invalid Cash Count" description={blindCloseError} /> : null}
            <FormField label="Counted Drawer Cash (PHP)" required hint="Expected cash is hidden until count submission.">
              <TextInput
                inputMode="decimal"
                placeholder="0.00"
                value={blindCountPhp}
                onChange={(e) => setBlindCountPhp(e.target.value)}
              />
            </FormField>
          </div>
        ) : (
          <div className="salryn-tabular" style={{ display: "grid", gap: 6 }}>
            <Alert
              tone={(blindCloseResult.varianceCents ?? 0) === 0 ? "success" : "warning"}
              title="Shift Variance Computed by Server"
              description={`Counted ${formatPhp(blindCloseResult.countedCashCents ?? 0)} vs Expected ${formatPhp(blindCloseResult.expectedCashCents ?? 0)}`}
            />
            <div>
              <strong>Variance:</strong> {formatPhp(blindCloseResult.varianceCents ?? 0)}
            </div>
          </div>
        )}
      </Dialog>
    </CafeShell>
  );
}

export function WorkstationApp({ api, initialNav, onOpenBackoffice }: WorkstationAppProps) {
  const resolvedApi = useMemo(() => api ?? createLiveHostApi(), [api]);
  return (
    <ErrorBoundary>
      <ToastProvider>
        <WorkstationInner api={resolvedApi} initialNav={initialNav} onOpenBackoffice={onOpenBackoffice} />
      </ToastProvider>
    </ErrorBoundary>
  );
}
