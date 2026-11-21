import {
  BarChart3,
  BookOpen,
  CalendarClock,
  HardDrive,
  Laptop,
  Package,
  RefreshCw,
  ShieldCheck,
  Store,
  Wallet,
} from "lucide-react";
import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import {
  Alert,
  BrandMark,
  Button,
  CafeShell,
  Card,
  CardBody,
  CardHeader,
  DataTable,
  Dialog,
  EmptyState,
  ErrorBoundary,
  FormField,
  MetricCard,
  SelectInput,
  StatusBadge,
  TextInput,
  ToastProvider,
  useToast,
  type CafeShellNavItem,
  type StatusTone,
} from "@salryn/cafe-ui";
import { createLiveBackofficeApi } from "../api/backofficeClient";
import {
  createLiveCentralOwnerApi,
  loadSavedCentralOrigin,
  loadSavedCentralSession,
  normalizeCentralBaseUrl,
  saveCentralOrigin,
  saveCentralSession,
} from "../api/centralOwnerClient";
import { HostApiError, createLiveHostApi, formatPhp, parsePhpToCents } from "../api/hostClient";
import type { HostSession, WorkstationApi } from "../cashier/types";
import { DeviceManagementPanel } from "../devices/DeviceManagementPanel";
import type {
  ArCustomerRow,
  AttendanceRow,
  AuditLogRow,
  BackofficeApi,
  BackupPackageRow,
  CafeBookingRow,
  CafeDeliveryRow,
  CafeRefundSummaryRow,
  CashMovementRow,
  CentralAdminSession,
  CentralDeviceRow,
  CentralHealthSnapshot,
  CentralOwnerApi,
  CentralQuarantineRow,
  DeploymentSurface,
  IncidentClassification,
  IncidentRow,
  IngredientRecipeDraft,
  LanControlStatusSnapshot,
  LanPairingSnapshot,
  LicenseStatusSnapshot,
  LowStockItem,
  OwnerReportsSnapshot,
  RefundStockDisposition,
  RefundableSaleRow,
  StoreSettingsSnapshot,
  SupplierSummary,
  UserRoleOption,
  UserRow,
  WasteReason,
} from "./types";
import { BackofficeUnknownOutcomeError, useBackofficeOperation } from "./useBackofficeOperation";

export type BackofficeConsoleAppProps = {
  api?: BackofficeApi;
  centralApi?: CentralOwnerApi;
  hostApi?: WorkstationApi;
  initialSurface?: DeploymentSurface;
  initialNav?: string;
  onOpenWorkstation?: () => void;
};

const REFUND_TONE: Record<string, StatusTone> = {
  Requested: "warning",
  Approved: "info",
  Committed: "success",
  Rejected: "danger",
};

const LICENSE_STATUS_LABELS: Record<number, string> = {
  0: "Unlicensed",
  1: "Licensed",
  2: "Expired",
  3: "Invalid",
  4: "Grace",
  5: "Development",
};

const LICENSE_EDITION_LABELS: Record<number, string> = {
  0: "Trial",
  1: "Local",
  2: "Professional",
  3: "Enterprise",
};

const CASH_MOVEMENT_LABELS: Record<number, string> = {
  1: "SaleCashReceipt",
  2: "ReceivableCashCollection",
  3: "CashIn",
  4: "InventoryPurchase",
  5: "OperationalExpense",
  6: "CashRefund",
  7: "CashOut",
  8: "Reversal",
};

function OperationAlerts({ op, title }: { op: ReturnType<typeof useBackofficeOperation>; title: string }) {
  return (
    <>
      {op.error ? <Alert tone="danger" title={`${title} Failed`} description={op.error} /> : null}
      {op.unknownOutcome ? (
        <Alert
          tone="warning"
          title={`${title} Outcome Unknown · Operation ${op.unknownOutcome.operationId}`}
          description={op.unknownOutcome.message}
        />
      ) : null}
    </>
  );
}

function formatLicenseStatus(status: number | string): string {
  return typeof status === "number" ? (LICENSE_STATUS_LABELS[status] ?? `Status ${status}`) : status;
}

function formatLicenseEdition(edition: number | string | null | undefined): string {
  if (edition === null || edition === undefined) return "Unassigned";
  return typeof edition === "number" ? (LICENSE_EDITION_LABELS[edition] ?? `Edition ${edition}`) : edition;
}

function formatMovementType(type: string | number): string {
  return typeof type === "number" ? (CASH_MOVEMENT_LABELS[type] ?? `Movement ${type}`) : type;
}

function rethrowUnknownIfAmbiguous(operationId: string, err: unknown): never {
  if (
    err instanceof HostApiError &&
    (err.status === 0 || err.status >= 500 || err.code === "ERR_IDEMPOTENCY_OUTCOME_UNKNOWN")
  ) {
    throw new BackofficeUnknownOutcomeError(operationId, err.message);
  }
  throw err;
}

function formatQty(x1000: number): string {
  return (x1000 / 1000).toFixed(x1000 % 1000 === 0 ? 0 : 2);
}

function parseQtyX1000(raw: string): number {
  const num = Number(raw.trim());
  if (!Number.isFinite(num) || num <= 0) {
    throw new Error("Enter a positive quantity greater than zero.");
  }
  return Math.round(num * 1000);
}

function BackofficeConsoleInner({
  api,
  centralApi,
  hostApi,
  initialSurface,
  initialNav = "reports",
  onOpenWorkstation,
}: {
  api: BackofficeApi;
  centralApi: CentralOwnerApi;
  hostApi: WorkstationApi | null;
  initialSurface: DeploymentSurface;
  initialNav: string;
  onOpenWorkstation?: () => void;
}) {
  const { notify } = useToast();
  const [surface, setSurface] = useState<DeploymentSurface>(initialSurface);
  const [activeNav, setActiveNav] = useState(initialNav);

  // Host session (only checked when hostApi is active and WINDOWS_HOST surface is opened)
  const [hostSession, setHostSession] = useState<HostSession>({ isAuthenticated: hostApi === null });
  const [hostLoginUser, setHostLoginUser] = useState("");
  const [hostLoginPass, setHostLoginPass] = useState("");
  const [hostAuthError, setHostAuthError] = useState<string | null>(null);
  const [hostLoading, setHostLoading] = useState(false);
  const [hostLoadError, setHostLoadError] = useState<string | null>(null);

  // Live Host state (C1-C11)
  const [reports, setReports] = useState<OwnerReportsSnapshot | null>(null);
  const [draft, setDraft] = useState<IngredientRecipeDraft>({ ingredients: [], recipes: [], lines: [] });
  const [lowStock, setLowStock] = useState<LowStockItem[]>([]);
  const [suppliers, setSuppliers] = useState<SupplierSummary[]>([]);
  const [arRows, setArRows] = useState<ArCustomerRow[]>([]);
  const [cashMovements, setCashMovements] = useState<CashMovementRow[]>([]);
  const [refunds, setRefunds] = useState<CafeRefundSummaryRow[]>([]);
  const [refundableSales, setRefundableSales] = useState<RefundableSaleRow[]>([]);
  const [bookings, setBookings] = useState<CafeBookingRow[]>([]);
  const [deliveries, setDeliveries] = useState<CafeDeliveryRow[]>([]);
  const [attendance, setAttendance] = useState<AttendanceRow[]>([]);
  const [incidents, setIncidents] = useState<IncidentRow[]>([]);
  const [auditLogs, setAuditLogs] = useState<AuditLogRow[]>([]);
  const [users, setUsers] = useState<UserRow[]>([]);
  const [userRoles, setUserRoles] = useState<UserRoleOption[]>([]);
  const [settingsDraft, setSettingsDraft] = useState<StoreSettingsSnapshot | null>(null);
  const [backups, setBackups] = useState<BackupPackageRow[]>([]);
  const [licenseStatus, setLicenseStatus] = useState<LicenseStatusSnapshot | null>(null);
  const [lanStatus, setLanStatus] = useState<LanControlStatusSnapshot | null>(null);
  const [lanPairing, setLanPairing] = useState<LanPairingSnapshot | null>(null);

  // Live Central Portal state
  const [centralOrigin, setCentralOrigin] = useState<string>(() => loadSavedCentralOrigin());
  const [centralHealth, setCentralHealth] = useState<CentralHealthSnapshot | null>(null);
  const [centralAuth, setCentralAuth] = useState<CentralAdminSession | null>(() => loadSavedCentralSession()?.auth ?? null);
  const [centralDevices, setCentralDevices] = useState<CentralDeviceRow[]>([]);
  const [centralQuarantine, setCentralQuarantine] = useState<CentralQuarantineRow[]>([]);

  // Forms: Inventory Purchase & Waste
  const [purchaseSupplierId, setPurchaseSupplierId] = useState("");
  const [purchaseIngredientId, setPurchaseIngredientId] = useState("");
  const [purchaseQty, setPurchaseQty] = useState("1");
  const [purchaseCostPhp, setPurchaseCostPhp] = useState("250.00");
  const [wasteIngredientId, setWasteIngredientId] = useState("");
  const [wasteQty, setWasteQty] = useState("1");
  const [wasteReason, setWasteReason] = useState<WasteReason>("Spoilage");
  const [wasteNotes, setWasteNotes] = useState("");

  // Forms: AR Collection & Cash Movement
  const [selectedCustomerId, setSelectedCustomerId] = useState("");
  const [arCollectPhp, setArCollectPhp] = useState("100.00");
  const [arCollectNote, setArCollectNote] = useState("");
  const [cashType, setCashType] = useState<"CashIn" | "CashOut" | "OperationalExpense">("OperationalExpense");
  const [cashAmountPhp, setCashAmountPhp] = useState("100.00");
  const [cashReason, setCashReason] = useState("");

  // Forms: C5 Refund Request & Commit
  const [selectedSaleId, setSelectedSaleId] = useState("");
  const [selectedSaleItemId, setSelectedSaleItemId] = useState("");
  const [refundQty, setRefundQty] = useState("1");
  const [refundDisposition, setRefundDisposition] = useState<RefundStockDisposition>("Restock");
  const [refundReason, setRefundReason] = useState("");
  const [commitTarget, setCommitTarget] = useState<CafeRefundSummaryRow | null>(null);
  const [commitCashPhp, setCommitCashPhp] = useState("0.00");
  const [commitArPhp, setCommitArPhp] = useState("0.00");

  // Forms: Incidents, Users, Settings, Backup Restore
  const [incidentClassification, setIncidentClassification] = useState<IncidentClassification>("Operational");
  const [incidentSeverity, setIncidentSeverity] = useState("Medium");
  const [incidentTitle, setIncidentTitle] = useState("");
  const [incidentDescription, setIncidentDescription] = useState("");
  const [newUsername, setNewUsername] = useState("");
  const [newDisplayName, setNewDisplayName] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [newRoleId, setNewRoleId] = useState("");
  const [restoreTarget, setRestoreTarget] = useState<BackupPackageRow | null>(null);
  const [restoreConfirmText, setRestoreConfirmText] = useState("");

  // Operations (double-submit lock + durable operationId)
  const publishOp = useBackofficeOperation("publish");
  const purchaseOp = useBackofficeOperation("purchase");
  const wasteOp = useBackofficeOperation("waste");
  const arOp = useBackofficeOperation("ar-collect");
  const cashOp = useBackofficeOperation("cash-move");
  const refundReqOp = useBackofficeOperation("refund-req");
  const refundResOp = useBackofficeOperation("refund-res");
  const refundCommitOp = useBackofficeOperation("refund-commit");
  const bookingOp = useBackofficeOperation("booking");
  const deliveryOp = useBackofficeOperation("delivery");
  const attendanceOp = useBackofficeOperation("attendance");
  const incidentOp = useBackofficeOperation("incident");
  const adminOp = useBackofficeOperation("admin");
  const hostOpsOp = useBackofficeOperation("host-ops");

  const safeFetch = async <T,>(fn: () => Promise<T>, fallback: T): Promise<T> => {
    try {
      return await fn();
    } catch {
      return fallback;
    }
  };

  const refreshHostData = useCallback(async () => {
    setHostLoading(true);
    setHostLoadError(null);
    try {
      const [
        rep,
        ing,
        low,
        sup,
        ar,
        cash,
        refList,
        refSales,
        book,
        deliv,
        att,
        inc,
        aud,
        usr,
        roles,
        settings,
        bkps,
        lic,
        lan,
        pair,
      ] = await Promise.all([
        api.getOwnerReports(),
        safeFetch(() => api.getIngredientsAndRecipes(), { ingredients: [], recipes: [], lines: [] }),
        safeFetch(() => api.getLowStock(), []),
        safeFetch(() => api.getSuppliers(), []),
        safeFetch(() => api.getReceivableCustomers(), []),
        safeFetch(() => api.getCashMovements(), []),
        safeFetch(() => api.getRefunds(), []),
        safeFetch(() => api.getRefundableSales(), []),
        safeFetch(() => api.getBookings(), []),
        safeFetch(() => api.getDeliveries(), []),
        safeFetch(() => api.getAttendance(), []),
        safeFetch(() => api.getIncidents(), []),
        safeFetch(() => api.getAuditLogs(), []),
        safeFetch(() => api.getUsers(), []),
        safeFetch(() => api.getUserRoles(), []),
        safeFetch(() => api.getStoreSettings(), null),
        safeFetch(() => api.getBackups(), []),
        safeFetch(() => api.getLicenseStatus(), null),
        safeFetch(() => api.getLanStatus(), null),
        safeFetch(() => api.getLanPairing(), null),
      ]);

      setReports(rep);
      setDraft(ing);
      setLowStock(low);
      setSuppliers(sup);
      setArRows(ar);
      setCashMovements(cash);
      setRefunds(refList);
      setRefundableSales(refSales);
      setBookings(book);
      setDeliveries(deliv);
      setAttendance(att);
      setIncidents(inc);
      setAuditLogs(aud);
      setUsers(usr);
      setUserRoles(roles);
      setSettingsDraft((prev) => prev ?? settings);
      setBackups(bkps);
      setLicenseStatus(lic);
      setLanStatus(lan);
      setLanPairing(pair);

      if (ing.ingredients[0]) {
        setPurchaseIngredientId((prev) => prev || ing.ingredients[0].id);
        setWasteIngredientId((prev) => prev || ing.ingredients[0].id);
      }
      if (sup[0]) setPurchaseSupplierId((prev) => prev || sup[0].id);
      if (ar[0]) setSelectedCustomerId((prev) => prev || ar[0].customer.id);
      if (roles[0]) setNewRoleId((prev) => prev || roles[0].roleId);
      if (refSales[0]) {
        setSelectedSaleId((prev) => prev || refSales[0].saleId);
        if (refSales[0].items[0]) {
          setSelectedSaleItemId((prev) => prev || refSales[0].items[0].saleItemId);
        }
      }
    } catch (err) {
      setHostLoadError(err instanceof Error ? err.message : "Failed to load Host back-office data.");
    } finally {
      setHostLoading(false);
    }
  }, [api]);

  const refreshCentralData = useCallback(
    async (originOverride?: string, authOverride?: CentralAdminSession | null) => {
      const origin = originOverride ?? centralOrigin;
      if (!origin.trim()) return;
      try {
        const normalized = normalizeCentralBaseUrl(origin);
        const health = await centralApi.getHealth(normalized);
        setCentralHealth(health);
        const auth = authOverride !== undefined ? authOverride : centralAuth;
        if (auth?.accessToken) {
          const [devs, quar] = await Promise.all([
            centralApi.listDevices(normalized, auth.accessToken),
            centralApi.listQuarantine(normalized, auth.accessToken),
          ]);
          setCentralDevices(devs);
          setCentralQuarantine(quar);
        }
      } catch (err) {
        notify("warning", "Central refresh incomplete", err instanceof Error ? err.message : "Central unavailable.");
      }
    },
    [centralApi, centralAuth, centralOrigin, notify],
  );

  // Strict surface isolation: in live runtime (hostApi !== null), only probe local Host endpoints
  // when WINDOWS_HOST surface is active.
  useEffect(() => {
    let active = true;
    async function initSurface() {
      if (surface === "WINDOWS_HOST" || hostApi === null) {
        if (hostApi) {
          try {
            const s = await hostApi.getSession();
            if (!active) return;
            setHostSession(s);
            if (s.isAuthenticated) {
              await refreshHostData();
            }
          } catch (err) {
            if (!active) return;
            setHostAuthError(err instanceof Error ? err.message : "Unable to reach local Salryn Host.");
          }
        } else if (!reports) {
          await refreshHostData();
        }
      }

      if (centralOrigin) {
        await refreshCentralData();
      }
    }
    void initSurface();
    return () => {
      active = false;
    };
  }, [surface, hostApi]); // eslint-disable-line react-hooks/exhaustive-deps

  async function handleHostSignIn(e: FormEvent) {
    e.preventDefault();
    if (!hostApi) return;
    setHostAuthError(null);
    setHostLoading(true);
    try {
      const s = await hostApi.login(hostLoginUser, hostLoginPass);
      setHostSession(s);
      setHostLoginPass("");
      await refreshHostData();
      notify("success", "Signed in to Host", s.user?.displayName ?? hostLoginUser);
    } catch (err) {
      setHostAuthError(err instanceof Error ? err.message : "Host sign-in failed.");
    } finally {
      setHostLoading(false);
    }
  }

  async function handleHostSignOut() {
    if (hostApi) {
      await hostApi.logout();
      setHostSession({ isAuthenticated: false });
    }
    notify("info", "Signed out", "Host session closed.");
  }

  async function handleCentralLogin(username: string, password: string) {
    const normalized = normalizeCentralBaseUrl(centralOrigin);
    const session = await centralApi.login(normalized, username, password);
    setCentralAuth(session);
    saveCentralOrigin(normalized);
    saveCentralSession({ origin: normalized, auth: session });
    await refreshCentralData(normalized, session);
    notify("success", "Central Admin signed in", session.session.displayName);
  }

  async function handleCentralLogout() {
    if (centralAuth && centralOrigin) {
      try {
        await centralApi.logout(normalizeCentralBaseUrl(centralOrigin), centralAuth.accessToken);
      } catch {
        // Clear local token cleanly even if expired.
      }
    }
    setCentralAuth(null);
    setCentralDevices([]);
    setCentralQuarantine([]);
    saveCentralSession(null);
  }

  const selectedSale = useMemo(
    () => refundableSales.find((s) => s.saleId === selectedSaleId) ?? refundableSales[0] ?? null,
    [refundableSales, selectedSaleId],
  );

  const pendingRefundCount = refunds.filter((r) => r.state === "Requested" || r.state === "Approved").length;
  const pendingDeviceCount = centralDevices.filter((d) => d.status === "PendingApproval").length;

  const navItems: CafeShellNavItem[] = [
    { id: "reports", label: "Owner Reports & COGS", group: "Overview", Icon: BarChart3 },
    { id: "devices", label: "Device Trust & Central", group: "Overview", Icon: Laptop, badge: pendingDeviceCount || undefined },
    { id: "recipes", label: "Menu, Recipes & BOM", group: "Cafe Operations", Icon: BookOpen },
    { id: "inventory", label: "Inventory & Waste", group: "Cafe Operations", Icon: Package, badge: lowStock.length || undefined },
    { id: "cash-ar", label: "Cash, Expense, AR & Refunds", group: "Cafe Operations", Icon: Wallet, badge: pendingRefundCount || undefined },
    { id: "bookings", label: "Bookings, Delivery & Staff", group: "Cafe Operations", Icon: CalendarClock },
    { id: "audit", label: "Audit, Users & Settings", group: "Admin", Icon: ShieldCheck },
    { id: "host-ops", label: "Host Backup & License", group: "Windows Host Only", Icon: HardDrive },
  ];

  // Unauthenticated gate for Windows Host surface when hostApi is present
  if (surface === "WINDOWS_HOST" && hostApi && !hostSession.isAuthenticated) {
    return (
      <div style={{ minHeight: "100vh", display: "grid", placeItems: "center", padding: 20, background: "var(--salryn-cream)" }}>
        <div style={{ width: "100%", maxWidth: 460, display: "grid", gap: 16 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
            <BrandMark variant="logo" height={44} alt="Salryn Cafe logo" />
            <div>
              <div style={{ fontSize: "0.75rem", color: "var(--salryn-mocha)", textTransform: "uppercase", fontWeight: 700 }}>
                Salryn Cafe · Management Console
              </div>
              <h1 style={{ margin: 0, fontSize: "1.5rem" }}>Back-Office Sign In</h1>
            </div>
          </div>

          {hostAuthError ? <Alert tone="danger" title="Sign-in Failed" description={hostAuthError} /> : null}

          <Card>
            <CardHeader title="Local Windows Host Sign In" subtitle="Authenticate with an authorized manager or owner account." />
            <CardBody>
              <form onSubmit={(e) => void handleHostSignIn(e)} style={{ display: "grid", gap: 12 }}>
                <FormField label="Username" required>
                  <TextInput autoComplete="username" value={hostLoginUser} onChange={(e) => setHostLoginUser(e.target.value)} required />
                </FormField>
                <FormField label="Password" required>
                  <TextInput type="password" autoComplete="current-password" value={hostLoginPass} onChange={(e) => setHostLoginPass(e.target.value)} required />
                </FormField>
                <div style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
                  <Button type="submit" variant="primary" loading={hostLoading}>
                    Sign In to Host Console
                  </Button>
                  <Button type="button" variant="secondary" onClick={() => { setSurface("CENTRAL_OWNER"); setActiveNav("devices"); }}>
                    Open Central Owner Portal
                  </Button>
                  {onOpenWorkstation ? (
                    <Button type="button" variant="ghost" onClick={onOpenWorkstation}>
                      Back to Workstation
                    </Button>
                  ) : null}
                </div>
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
      stationLabel={
        surface === "CENTRAL_OWNER"
          ? "Central Owner & Admin Portal (Independent HTTPS Origin)"
          : "Local Windows Host Back-Office (127.0.0.1:5399 / LAN HTTPS 5400)"
      }
      connectionStatus={
        surface === "CENTRAL_OWNER"
          ? centralAuth
            ? { label: "Central Authenticated", tone: "success" }
            : { label: "Central Ready", tone: "info" }
          : { label: "Host Live SQLite", tone: "success" }
      }
      operatorName={hostSession.user?.displayName ?? centralAuth?.session.displayName ?? "Cafe Manager"}
      roleName={hostSession.user?.roleName ?? centralAuth?.session.role ?? "Back-Office"}
      navItems={navItems}
      activeNav={activeNav}
      onNavigate={setActiveNav}
      onSignOut={() => void handleHostSignOut()}
      banner={
        <div style={{ display: "grid", gap: 10 }}>
          <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center", justifyContent: "space-between", gap: 10 }}>
            <div style={{ display: "flex", flexWrap: "wrap", gap: 8, alignItems: "center" }}>
              <StatusBadge tone={surface === "CENTRAL_OWNER" ? "info" : "success"}>
                Surface: {surface === "CENTRAL_OWNER" ? "Central Owner Portal" : "Windows Host Console"}
              </StatusBadge>
              {reports ? (
                <StatusBadge tone="neutral">{reports.officialBoundary}</StatusBadge>
              ) : null}
            </div>
            <div role="group" aria-label="Deployment surface switcher" style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
              {onOpenWorkstation ? (
                <Button size="sm" variant="secondary" leftIcon={<Store size={14} />} onClick={onOpenWorkstation}>
                  POS Workstation
                </Button>
              ) : null}
              <Button
                size="sm"
                variant="secondary"
                leftIcon={<RefreshCw size={14} />}
                loading={hostLoading}
                onClick={() => void (surface === "CENTRAL_OWNER" && hostApi !== null ? refreshCentralData() : refreshHostData())}
              >
                Refresh Live Data
              </Button>
              <Button
                size="sm"
                variant={surface === "CENTRAL_OWNER" ? "primary" : "secondary"}
                aria-pressed={surface === "CENTRAL_OWNER"}
                onClick={() => setSurface("CENTRAL_OWNER")}
              >
                Central Owner Portal
              </Button>
              <Button
                size="sm"
                variant={surface === "WINDOWS_HOST" ? "primary" : "secondary"}
                aria-pressed={surface === "WINDOWS_HOST"}
                onClick={() => setSurface("WINDOWS_HOST")}
              >
                Windows Host Console
              </Button>
            </div>
          </div>
          {hostLoadError ? <Alert tone="danger" title="Host Sync Error" description={hostLoadError} /> : null}
        </div>
      }
    >
      {activeNav === "reports" ? (
        <div style={{ display: "grid", gap: 16 }}>
          {reports ? (
            <>
              <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(210px, 1fr))", gap: 14 }}>
                <MetricCard
                  label="Daily Sales"
                  value={formatPhp(reports.daily.salesAmountCents)}
                  detail={`${reports.daily.salesCount} completed orders · Avg ${formatPhp(reports.daily.averageSaleCents)}`}
                />
                <MetricCard
                  label="Monthly Net Revenue"
                  value={formatPhp(reports.cafeManagement?.profitability.recognizedNetRevenueCents ?? reports.monthly.salesAmountCents)}
                  detail={`Historical COGS ${formatPhp(reports.cafeManagement?.profitability.historicalCogsCents ?? 0)}`}
                />
                <MetricCard
                  label="Operational Contribution"
                  value={formatPhp(reports.cafeManagement?.profitability.operationalContributionCents ?? 0)}
                  detail={`Expenses ${formatPhp(reports.cafeManagement?.profitability.operationalExpenseCents ?? 0)} · Waste ${formatPhp(reports.cafeManagement?.profitability.recognizedWasteCostCents ?? 0)}`}
                />
                <MetricCard
                  label="Outstanding AR"
                  value={formatPhp(reports.receivables?.outstandingCents ?? arRows.reduce((s, r) => s + r.outstandingBalanceCents, 0))}
                  detail={`Collections ${formatPhp(reports.receivables?.collectionsCents ?? 0)}`}
                />
              </div>

              {reports.cafeManagement ? (
                <Card>
                  <CardHeader
                    title="Cost Coverage & Recipe BOM Margin"
                    subtitle={`Cost state: ${reports.cafeManagement.costCoverage.state} (${reports.cafeManagement.costCoverage.costCompleteSaleCount}/${reports.cafeManagement.costCoverage.revenueSaleCount} complete)`}
                  />
                  <CardBody>
                    <DataTable
                      caption="Recipe Cost Coverage"
                      rows={reports.cafeManagement.recipeCosts}
                      getRowKey={(r) => r.recipeId}
                      columns={[
                        { key: "name", header: "Recipe", render: (r) => <strong>{r.recipeName}</strong> },
                        { key: "yield", header: "Yield", align: "right", tabular: true, render: (r) => formatQty(r.yieldX1000) },
                        { key: "cost", header: "Known BOM Cost", align: "right", tabular: true, render: (r) => formatPhp(r.knownCostCents) },
                        { key: "cov", header: "Cost Coverage", render: (r) => <StatusBadge tone={r.costCoverage === "Complete" ? "success" : "warning"}>{r.costCoverage}</StatusBadge> },
                      ]}
                    />
                  </CardBody>
                </Card>
              ) : null}
            </>
          ) : (
            <EmptyState title="No owner report loaded" description="Sign in to Windows Host or connect to Central Owner Portal to view reports." />
          )}
        </div>
      ) : null}

      {activeNav === "devices" ? (
        <DeviceManagementPanel
          centralApi={centralApi}
          surface={surface}
          centralOrigin={centralOrigin}
          onCentralOriginChange={(origin) => {
            setCentralOrigin(origin);
            saveCentralOrigin(origin);
          }}
          centralHealth={centralHealth}
          centralAuth={centralAuth}
          devices={centralDevices}
          quarantine={centralQuarantine}
          lanStatus={lanStatus}
          lanPairing={lanPairing}
          onCentralLogin={handleCentralLogin}
          onCentralLogout={handleCentralLogout}
          onRefreshCentral={() => refreshCentralData()}
          onRotateLanJoinCode={async () => {
            const next = await api.rotateLanJoinCode();
            setLanPairing(next);
            notify("success", "LAN join code rotated", next.activeJoinCode ?? "");
          }}
        />
      ) : null}

      {activeNav === "recipes" ? (
        <div style={{ display: "grid", gap: 16 }}>
          <OperationAlerts op={publishOp} title="Publish" />
          <Card>
            <CardHeader
              title="Menu, Ingredients & Recipe BOM"
              subtitle="Live Host menu & recipe draft snapshot. Publish pushes updates to Cashier and Kitchen stations."
              action={
                <Button
                  variant="primary"
                  loading={publishOp.inFlight}
                  onClick={() =>
                    void publishOp.execute(async (operationId) => {
                      try {
                        await api.publishMenuAndRecipes();
                        await refreshHostData();
                        notify("success", "Menu & Recipes published", "New revision is live for Cashiers.");
                      } catch (err) {
                        rethrowUnknownIfAmbiguous(operationId, err);
                      }
                    })
                  }
                >
                  Publish Menu &amp; Recipes
                </Button>
              }
            />
            <CardBody>
              <div style={{ display: "grid", gap: 16 }}>
                <DataTable
                  caption="Ingredients & Stock Units"
                  rows={draft.ingredients}
                  getRowKey={(i) => i.id}
                  columns={[
                    { key: "name", header: "Ingredient", render: (i) => <strong>{i.name}</strong> },
                    { key: "unit", header: "Stock Unit", render: (i) => i.stockUnit },
                    { key: "stock", header: "On Hand", align: "right", tabular: true, render: (i) => `${formatQty(i.stockOnHandX1000)} ${i.stockUnit}` },
                    { key: "reorder", header: "Reorder Level", align: "right", tabular: true, render: (i) => `${formatQty(i.reorderLevelX1000)} ${i.stockUnit}` },
                  ]}
                />
                <DataTable
                  caption="Recipes & Yield"
                  rows={draft.recipes}
                  getRowKey={(r) => r.id}
                  columns={[
                    { key: "name", header: "Recipe", render: (r) => <strong>{r.name}</strong> },
                    { key: "yield", header: "Yield", align: "right", tabular: true, render: (r) => formatQty(r.yieldX1000) },
                    {
                      key: "lines",
                      header: "BOM Lines",
                      align: "right",
                      tabular: true,
                      render: (r) => draft.lines.filter((l) => l.recipeId === r.id).length,
                    },
                    {
                      key: "active",
                      header: "Status",
                      render: (r) => <StatusBadge tone={r.isActive ? "success" : "neutral"}>{r.isActive ? "Active" : "Inactive"}</StatusBadge>,
                    },
                  ]}
                />
              </div>
            </CardBody>
          </Card>
        </div>
      ) : null}

      {activeNav === "inventory" ? (
        <div style={{ display: "grid", gap: 16 }}>
          <OperationAlerts op={purchaseOp} title="Purchase Receipt" />
          <OperationAlerts op={wasteOp} title="Waste Recording" />

          <Card>
            <CardHeader title="Receive Supplier Purchase" subtitle="Updates stock-on-hand and weighted-average ingredient acquisition cost." />
            <CardBody>
              <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))", gap: 12, alignItems: "end" }}>
                <FormField label="Supplier" required>
                  <SelectInput value={purchaseSupplierId} onChange={(e) => setPurchaseSupplierId(e.target.value)}>
                    {suppliers.map((s) => (
                      <option key={s.id} value={s.id}>{s.name}</option>
                    ))}
                  </SelectInput>
                </FormField>
                <FormField label="Ingredient" required>
                  <SelectInput value={purchaseIngredientId} onChange={(e) => setPurchaseIngredientId(e.target.value)}>
                    {draft.ingredients.map((i) => (
                      <option key={i.id} value={i.id}>{i.name} ({i.stockUnit})</option>
                    ))}
                  </SelectInput>
                </FormField>
                <FormField label="Quantity" required>
                  <TextInput inputMode="decimal" value={purchaseQty} onChange={(e) => setPurchaseQty(e.target.value)} />
                </FormField>
                <FormField label="Line Cost (PHP)" required>
                  <TextInput inputMode="decimal" value={purchaseCostPhp} onChange={(e) => setPurchaseCostPhp(e.target.value)} />
                </FormField>
                <Button
                  variant="primary"
                  loading={purchaseOp.inFlight}
                  disabled={!purchaseSupplierId || !purchaseIngredientId}
                  onClick={() =>
                    void purchaseOp.execute(async (operationId) => {
                      try {
                        const qty = parseQtyX1000(purchaseQty);
                        const costCents = parsePhpToCents(purchaseCostPhp);
                        await api.receivePurchase({
                          operationId,
                          supplierId: purchaseSupplierId,
                          stockTargetId: purchaseIngredientId,
                          quantityX1000: qty,
                          lineAcquisitionCostCents: costCents,
                        });
                        await refreshHostData();
                        notify("success", "Purchase received", `Cost ${formatPhp(costCents)} recorded in stock ledger.`);
                      } catch (err) {
                        rethrowUnknownIfAmbiguous(operationId, err);
                      }
                    })
                  }
                >
                  Receive Purchase
                </Button>
              </div>
            </CardBody>
          </Card>

          <Card>
            <CardHeader title="Record Inventory Waste / Spoilage" subtitle="Appends an audited negative ledger movement using approved C2 waste reasons." />
            <CardBody>
              <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(170px, 1fr))", gap: 12, alignItems: "end" }}>
                <FormField label="Ingredient" required>
                  <SelectInput value={wasteIngredientId} onChange={(e) => setWasteIngredientId(e.target.value)}>
                    {draft.ingredients.map((i) => (
                      <option key={i.id} value={i.id}>{i.name} ({i.stockUnit})</option>
                    ))}
                  </SelectInput>
                </FormField>
                <FormField label="Waste Quantity" required>
                  <TextInput inputMode="decimal" value={wasteQty} onChange={(e) => setWasteQty(e.target.value)} />
                </FormField>
                <FormField label="Waste Reason" required>
                  <SelectInput value={wasteReason} onChange={(e) => setWasteReason(e.target.value as WasteReason)}>
                    <option value="Spoilage">Spoilage</option>
                    <option value="Waste">Waste</option>
                    <option value="Damage">Damage</option>
                    <option value="Expired">Expired</option>
                  </SelectInput>
                </FormField>
                <FormField label="Notes">
                  <TextInput placeholder="Optional note" value={wasteNotes} onChange={(e) => setWasteNotes(e.target.value)} />
                </FormField>
                <Button
                  variant="secondary"
                  loading={wasteOp.inFlight}
                  disabled={!wasteIngredientId}
                  onClick={() =>
                    void wasteOp.execute(async (operationId) => {
                      try {
                        const qty = parseQtyX1000(wasteQty);
                        await api.recordWaste({
                          operationId,
                          stockTargetId: wasteIngredientId,
                          quantityWastedX1000: qty,
                          reason: wasteReason,
                          notes: wasteNotes.trim() || undefined,
                        });
                        setWasteNotes("");
                        await refreshHostData();
                        notify("success", "Waste recorded", `${wasteReason} posted to stock ledger.`);
                      } catch (err) {
                        rethrowUnknownIfAmbiguous(operationId, err);
                      }
                    })
                  }
                >
                  Record Waste
                </Button>
              </div>
            </CardBody>
          </Card>

          <Card>
            <CardHeader title="Low-Stock Alerts" subtitle="Items at or below reorder threshold." />
            <CardBody>
              <DataTable
                caption="Low-Stock Inventory"
                rows={lowStock}
                getRowKey={(r) => r.stockTargetId}
                emptyTitle="Stock levels healthy"
                emptyDescription="No tracked ingredients or products are currently below reorder threshold."
                columns={[
                  { key: "name", header: "Item", render: (r) => <strong>{r.name}</strong> },
                  { key: "type", header: "Type", render: (r) => r.stockTargetType },
                  { key: "onHand", header: "On Hand", align: "right", tabular: true, render: (r) => `${formatQty(r.stockOnHandX1000)} ${r.stockUnit}` },
                  { key: "reorder", header: "Reorder Level", align: "right", tabular: true, render: (r) => `${formatQty(r.reorderLevelX1000)} ${r.stockUnit}` },
                ]}
              />
            </CardBody>
          </Card>
        </div>
      ) : null}

      {activeNav === "cash-ar" ? (
        <div style={{ display: "grid", gap: 16 }}>
          <OperationAlerts op={arOp} title="AR Collection" />
          <OperationAlerts op={cashOp} title="Cash Movement" />
          <OperationAlerts op={refundReqOp} title="Refund Request" />
          <OperationAlerts op={refundResOp} title="Refund Resolution" />
          <OperationAlerts op={refundCommitOp} title="Refund Commit" />

          <Card>
            <CardHeader title="Accounts Receivable Collection (Cash Only)" subtitle="Receivables collections are restricted to verified Cash until a separate noncash contract is approved." />
            <CardBody>
              <div style={{ display: "grid", gap: 12 }}>
                <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))", gap: 12, alignItems: "end" }}>
                  <FormField label="Customer" required>
                    <SelectInput value={selectedCustomerId} onChange={(e) => setSelectedCustomerId(e.target.value)}>
                      {arRows.map((row) => (
                        <option key={row.customer.id} value={row.customer.id}>
                          {row.customer.fullName} (Bal: {formatPhp(row.outstandingBalanceCents)})
                        </option>
                      ))}
                    </SelectInput>
                  </FormField>
                  <FormField label="Cash Collected (PHP)" required>
                    <TextInput inputMode="decimal" value={arCollectPhp} onChange={(e) => setArCollectPhp(e.target.value)} />
                  </FormField>
                  <FormField label="Reference Note">
                    <TextInput placeholder="OR / Receipt reference" value={arCollectNote} onChange={(e) => setArCollectNote(e.target.value)} />
                  </FormField>
                  <Button
                    variant="primary"
                    loading={arOp.inFlight}
                    disabled={!selectedCustomerId}
                    onClick={() =>
                      void arOp.execute(async (operationId) => {
                        try {
                          const amountCents = parsePhpToCents(arCollectPhp);
                          await api.collectArCash({
                            clientRequestId: operationId,
                            customerId: selectedCustomerId,
                            amountCents,
                            note: arCollectNote.trim() || undefined,
                          });
                          setArCollectNote("");
                          await refreshHostData();
                          notify("success", "Cash AR collection posted", formatPhp(amountCents));
                        } catch (err) {
                          rethrowUnknownIfAmbiguous(operationId, err);
                        }
                      })
                    }
                  >
                    Post Cash AR Collection
                  </Button>
                </div>

                <DataTable
                  caption="Customer AR Balances"
                  rows={arRows}
                  getRowKey={(r) => r.customer.id}
                  columns={[
                    { key: "name", header: "Customer", render: (r) => <strong>{r.customer.fullName}</strong> },
                    { key: "phone", header: "Phone", render: (r) => r.customer.phone ?? "—" },
                    { key: "balance", header: "Outstanding Balance", align: "right", tabular: true, render: (r) => formatPhp(r.outstandingBalanceCents) },
                    { key: "overdue", header: "Overdue", align: "right", tabular: true, render: (r) => formatPhp(r.overdueBalanceCents) },
                  ]}
                />
              </div>
            </CardBody>
          </Card>

          <Card>
            <CardHeader title="Cash Drawer & Operational Expense" subtitle="Requires an open cashier shift for drawer cash movements." />
            <CardBody>
              <div style={{ display: "grid", gap: 12 }}>
                <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))", gap: 12, alignItems: "end" }}>
                  <FormField label="Movement Type" required>
                    <SelectInput value={cashType} onChange={(e) => setCashType(e.target.value as "CashIn" | "CashOut" | "OperationalExpense")}>
                      <option value="OperationalExpense">Operational Expense</option>
                      <option value="CashIn">Cash In (Float Top-up)</option>
                      <option value="CashOut">Cash Out (Safe Drop)</option>
                    </SelectInput>
                  </FormField>
                  <FormField label="Amount (PHP)" required>
                    <TextInput inputMode="decimal" value={cashAmountPhp} onChange={(e) => setCashAmountPhp(e.target.value)} />
                  </FormField>
                  <FormField label="Reason" required>
                    <TextInput placeholder="Ice delivery / Petty cash drop" value={cashReason} onChange={(e) => setCashReason(e.target.value)} />
                  </FormField>
                  <Button
                    variant="secondary"
                    loading={cashOp.inFlight}
                    disabled={!cashReason.trim()}
                    onClick={() =>
                      void cashOp.execute(async (operationId) => {
                        try {
                          const amountCents = parsePhpToCents(cashAmountPhp);
                          await api.postCashMovement({
                            operationId,
                            movementType: cashType,
                            amountCents,
                            reason: cashReason.trim(),
                          });
                          setCashReason("");
                          await refreshHostData();
                          notify("success", `${cashType} recorded`, formatPhp(amountCents));
                        } catch (err) {
                          rethrowUnknownIfAmbiguous(operationId, err);
                        }
                      })
                    }
                  >
                    Record Cash Movement
                  </Button>
                </div>

                <DataTable
                  caption="Recent Cash Movements"
                  rows={cashMovements}
                  getRowKey={(m) => m.movementId}
                  columns={[
                    { key: "type", header: "Type", render: (m) => <StatusBadge tone="neutral">{formatMovementType(m.movementType)}</StatusBadge> },
                    { key: "amount", header: "Delta", align: "right", tabular: true, render: (m) => formatPhp(m.deltaCents) },
                    { key: "reason", header: "Reason", render: (m) => m.reason ?? m.referenceType },
                    { key: "when", header: "Recorded", render: (m) => new Date(m.createdAtUtc).toLocaleString() },
                  ]}
                />
              </div>
            </CardBody>
          </Card>

          <Card>
            <CardHeader title="C5 Audited Refunds & Reversals" subtitle="Two-step request and manager approval, committed to Cash and/or Accounts Receivable only." />
            <CardBody>
              <div style={{ display: "grid", gap: 14 }}>
                <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(170px, 1fr))", gap: 12, alignItems: "end" }}>
                  <FormField label="Completed Sale Receipt" required>
                    <SelectInput
                      value={selectedSale?.saleId ?? ""}
                      onChange={(e) => {
                        const next = refundableSales.find((s) => s.saleId === e.target.value);
                        setSelectedSaleId(e.target.value);
                        setSelectedSaleItemId(next?.items[0]?.saleItemId ?? "");
                      }}
                    >
                      {refundableSales.map((s) => (
                        <option key={s.saleId} value={s.saleId}>
                          {s.receiptNumber} · Total {formatPhp(s.totalCents)}
                        </option>
                      ))}
                    </SelectInput>
                  </FormField>
                  <FormField label="Sale Line" required>
                    <SelectInput value={selectedSaleItemId} onChange={(e) => setSelectedSaleItemId(e.target.value)}>
                      {(selectedSale?.items ?? []).map((it) => (
                        <option key={it.saleItemId} value={it.saleItemId}>
                          {it.productName} (Rem: {formatQty(it.remainingQuantityX1000)})
                        </option>
                      ))}
                    </SelectInput>
                  </FormField>
                  <FormField label="Refund Qty" required>
                    <TextInput inputMode="decimal" value={refundQty} onChange={(e) => setRefundQty(e.target.value)} />
                  </FormField>
                  <FormField label="Stock Disposition" required>
                    <SelectInput value={refundDisposition} onChange={(e) => setRefundDisposition(e.target.value as RefundStockDisposition)}>
                      <option value="Restock">Restock (returnable inventory)</option>
                      <option value="DamagedNonRestockable">Damaged / Non-Restockable</option>
                      <option value="ServiceNoStock">Prepared Drink / No Stock Reversal</option>
                    </SelectInput>
                  </FormField>
                  <FormField label="Refund Reason" required>
                    <TextInput placeholder="Wrong preparation / spill" value={refundReason} onChange={(e) => setRefundReason(e.target.value)} />
                  </FormField>
                  <Button
                    variant="primary"
                    loading={refundReqOp.inFlight}
                    disabled={!selectedSale || !selectedSaleItemId || !refundReason.trim()}
                    onClick={() =>
                      void refundReqOp.execute(async (operationId) => {
                        try {
                          const qty = parseQtyX1000(refundQty);
                          const created = await api.requestRefund({
                            operationId,
                            saleId: selectedSale!.saleId,
                            reason: refundReason.trim(),
                            lines: [
                              {
                                saleItemId: selectedSaleItemId,
                                quantityX1000: qty,
                                stockDisposition: refundDisposition,
                              },
                            ],
                          });
                          setRefundReason("");
                          await refreshHostData();
                          notify("success", "Refund requested", `State: ${created.state}`);
                        } catch (err) {
                          rethrowUnknownIfAmbiguous(operationId, err);
                        }
                      })
                    }
                  >
                    Request Refund
                  </Button>
                </div>

                <DataTable
                  caption="Sale Refunds"
                  rows={refunds}
                  getRowKey={(r) => r.refundId}
                  columns={[
                    { key: "receipt", header: "Receipt #", render: (r) => <strong>{r.receiptNumber ?? r.saleId.slice(0, 8)}</strong> },
                    { key: "state", header: "State", render: (r) => <StatusBadge tone={REFUND_TONE[r.state] ?? "neutral"}>{r.state}</StatusBadge> },
                    { key: "total", header: "Refund Total", align: "right", tabular: true, render: (r) => (r.totalRefundCents !== null ? formatPhp(r.totalRefundCents) : "—") },
                    { key: "reason", header: "Reason", render: (r) => r.reason },
                    {
                      key: "actions",
                      header: "Actions",
                      render: (r) => (
                        <div style={{ display: "flex", flexWrap: "wrap", gap: 6 }}>
                          {r.state === "Requested" ? (
                            <>
                              <Button
                                size="sm"
                                variant="primary"
                                loading={refundResOp.inFlight}
                                onClick={() =>
                                  void refundResOp.execute(async (operationId) => {
                                    try {
                                      await api.resolveRefund({
                                        operationId,
                                        refundId: r.refundId,
                                        saleId: r.saleId,
                                        expectedVersion: r.version,
                                        approve: true,
                                        reason: "Manager approved from Back-Office",
                                      });
                                      await refreshHostData();
                                      notify("success", "Refund approved", r.receiptNumber ?? r.refundId);
                                    } catch (err) {
                                      rethrowUnknownIfAmbiguous(operationId, err);
                                    }
                                  }, `approve-${r.refundId}`)
                                }
                              >
                                Approve
                              </Button>
                              <Button
                                size="sm"
                                variant="danger"
                                loading={refundResOp.inFlight}
                                onClick={() =>
                                  void refundResOp.execute(async (operationId) => {
                                    try {
                                      await api.resolveRefund({
                                        operationId,
                                        refundId: r.refundId,
                                        saleId: r.saleId,
                                        expectedVersion: r.version,
                                        approve: false,
                                        reason: "Rejected from Back-Office",
                                      });
                                      await refreshHostData();
                                      notify("info", "Refund rejected", r.receiptNumber ?? r.refundId);
                                    } catch (err) {
                                      rethrowUnknownIfAmbiguous(operationId, err);
                                    }
                                  }, `reject-${r.refundId}`)
                                }
                              >
                                Reject
                              </Button>
                            </>
                          ) : null}
                          {r.state === "Approved" ? (
                            <Button
                              size="sm"
                              variant="accent"
                              onClick={() => {
                                const sale = refundableSales.find((s) => s.saleId === r.saleId);
                                const refundCents = r.totalRefundCents ?? 0;
                                const maxCash = sale ? Math.min(sale.cashNetCents, refundCents) : refundCents;
                                const arPart = Math.max(0, refundCents - maxCash);
                                setCommitCashPhp((maxCash / 100).toFixed(2));
                                setCommitArPhp((arPart / 100).toFixed(2));
                                setCommitTarget(r);
                              }}
                            >
                              Commit Settlement
                            </Button>
                          ) : null}
                        </div>
                      ),
                    },
                  ]}
                />
              </div>
            </CardBody>
          </Card>

          <Dialog
            open={commitTarget !== null}
            title={`Commit Refund · ${commitTarget?.receiptNumber ?? ""}`}
            description="Only Cash and Accounts Receivable reversals are permitted. Requires an open cashier shift for Cash refunds."
            onClose={() => setCommitTarget(null)}
            footer={
              <>
                <Button variant="secondary" onClick={() => setCommitTarget(null)}>
                  Cancel
                </Button>
                <Button
                  variant="primary"
                  loading={refundCommitOp.inFlight}
                  onClick={() => {
                    if (!commitTarget) return;
                    void refundCommitOp.execute(async (operationId) => {
                      try {
                        const cashCents = parsePhpToCents(commitCashPhp || "0");
                        const arCents = parsePhpToCents(commitArPhp || "0");
                        const settlements: Array<{ method: "Cash" | "AccountsReceivable"; amountCents: number }> = [];
                        if (cashCents > 0) settlements.push({ method: "Cash", amountCents: cashCents });
                        if (arCents > 0) settlements.push({ method: "AccountsReceivable", amountCents: arCents });
                        if (settlements.length === 0) {
                          throw new Error("Enter at least one non-zero Cash or Accounts Receivable refund amount.");
                        }
                        const res = await api.commitRefund({
                          operationId,
                          refundId: commitTarget.refundId,
                          saleId: commitTarget.saleId,
                          expectedVersion: commitTarget.version,
                          settlements,
                        });
                        setCommitTarget(null);
                        await refreshHostData();
                        notify("success", "Refund committed", `Total ${formatPhp(res.totalRefundCents)}`);
                      } catch (err) {
                        rethrowUnknownIfAmbiguous(operationId, err);
                      }
                    }, `commit-${commitTarget.refundId}`);
                  }}
                >
                  Confirm &amp; Commit Refund
                </Button>
              </>
            }
          >
            <div style={{ display: "grid", gap: 12 }}>
              <FormField label="Cash Refund Reversal (PHP)">
                <TextInput inputMode="decimal" value={commitCashPhp} onChange={(e) => setCommitCashPhp(e.target.value)} />
              </FormField>
              <FormField label="Accounts Receivable Credit Reversal (PHP)">
                <TextInput inputMode="decimal" value={commitArPhp} onChange={(e) => setCommitArPhp(e.target.value)} />
              </FormField>
            </div>
          </Dialog>
        </div>
      ) : null}

      {activeNav === "bookings" ? (
        <div style={{ display: "grid", gap: 16 }}>
          <OperationAlerts op={bookingOp} title="Booking Update" />
          <OperationAlerts op={deliveryOp} title="Delivery Update" />
          <OperationAlerts op={attendanceOp} title="Attendance" />
          <OperationAlerts op={incidentOp} title="Incident Log" />

          <Card>
            <CardHeader title="Coffee Cart & Event Bookings" subtitle="Confirm, mark ready, or cancel upcoming event bookings." />
            <CardBody>
              <DataTable
                caption="Bookings"
                rows={bookings}
                getRowKey={(b) => b.bookingId}
                columns={[
                  { key: "cust", header: "Customer / Package", render: (b) => <strong>{b.packageLabel ? `${b.customerNameSnapshot} · ${b.packageLabel}` : b.customerNameSnapshot}</strong> },
                  { key: "mode", header: "Mode", render: (b) => b.bookingMode },
                  { key: "loc", header: "Location", render: (b) => b.location ?? "—" },
                  { key: "status", header: "Status", render: (b) => <StatusBadge tone="info">{b.status}</StatusBadge> },
                  { key: "total", header: "Quoted Total", align: "right", tabular: true, render: (b) => formatPhp(b.quotedTotalCents) },
                  {
                    key: "act",
                    header: "Transitions",
                    render: (b) => (
                      <div style={{ display: "flex", flexWrap: "wrap", gap: 6 }}>
                        {b.status === "Draft" || b.status === "Requested" ? (
                          <Button
                            size="sm"
                            variant="primary"
                            loading={bookingOp.inFlight}
                            onClick={() =>
                              void bookingOp.execute(async (operationId) => {
                                try {
                                  await api.advanceBookingStatus({ operationId, bookingId: b.bookingId, expectedVersion: b.bookingVersion, action: "confirm" });
                                  await refreshHostData();
                                } catch (err) {
                                  rethrowUnknownIfAmbiguous(operationId, err);
                                }
                              }, `b-conf-${b.bookingId}`)
                            }
                          >
                            Confirm
                          </Button>
                        ) : null}
                        {b.status === "Confirmed" ? (
                          <Button
                            size="sm"
                            variant="accent"
                            loading={bookingOp.inFlight}
                            onClick={() =>
                              void bookingOp.execute(async (operationId) => {
                                try {
                                  await api.advanceBookingStatus({ operationId, bookingId: b.bookingId, expectedVersion: b.bookingVersion, action: "ready-for-fulfillment" });
                                  await refreshHostData();
                                } catch (err) {
                                  rethrowUnknownIfAmbiguous(operationId, err);
                                }
                              }, `b-ready-${b.bookingId}`)
                            }
                          >
                            Ready
                          </Button>
                        ) : null}
                      </div>
                    ),
                  },
                ]}
              />
            </CardBody>
          </Card>

          <Card>
            <CardHeader title="Deliveries" subtitle="Track active dispatch and delivery completion." />
            <CardBody>
              <DataTable
                caption="Deliveries"
                rows={deliveries}
                getRowKey={(d) => d.deliveryId}
                columns={[
                  { key: "deliv", header: "Delivery ID", render: (d) => <strong className="salryn-tabular">{d.deliveryId.slice(0, 8)}</strong> },
                  { key: "order", header: "Order ID", render: (d) => <span className="salryn-tabular">{d.orderId.slice(0, 8)}</span> },
                  { key: "status", header: "Status", render: (d) => <StatusBadge tone="info">{d.status}</StatusBadge> },
                  { key: "updated", header: "Updated", render: (d) => new Date(d.updatedAtUtc).toLocaleString() },
                  {
                    key: "act",
                    header: "Actions",
                    render: (d) => (
                      <div style={{ display: "flex", gap: 6 }}>
                        {d.status === "Pending" ? (
                          <Button
                            size="sm"
                            variant="primary"
                            onClick={() =>
                              void deliveryOp.execute(async (operationId) => {
                                try {
                                  await api.advanceDeliveryStatus({ operationId, deliveryId: d.deliveryId, expectedVersion: d.version, action: "dispatch" });
                                  await refreshHostData();
                                } catch (err) {
                                  rethrowUnknownIfAmbiguous(operationId, err);
                                }
                              }, `deliv-disp-${d.deliveryId}`)
                            }
                          >
                            Dispatch
                          </Button>
                        ) : null}
                        {d.status === "Dispatched" ? (
                          <Button
                            size="sm"
                            variant="accent"
                            onClick={() =>
                              void deliveryOp.execute(async (operationId) => {
                                try {
                                  await api.advanceDeliveryStatus({ operationId, deliveryId: d.deliveryId, expectedVersion: d.version, action: "deliver" });
                                  await refreshHostData();
                                } catch (err) {
                                  rethrowUnknownIfAmbiguous(operationId, err);
                                }
                              }, `deliv-done-${d.deliveryId}`)
                            }
                          >
                            Mark Delivered
                          </Button>
                        ) : null}
                      </div>
                    ),
                  },
                ]}
              />
            </CardBody>
          </Card>

          <Card>
            <CardHeader
              title="Staff Attendance & Incident Log"
              subtitle="Signed-in operator clock events and operational incident reporting."
              action={
                <div style={{ display: "flex", gap: 8 }}>
                  <Button
                    size="sm"
                    variant="primary"
                    loading={attendanceOp.inFlight}
                    onClick={() =>
                      void attendanceOp.execute(async (operationId) => {
                        try {
                          await api.recordAttendance({ operationId, eventType: "ClockIn" });
                          await refreshHostData();
                          notify("success", "Clocked in", "Attendance event saved.");
                        } catch (err) {
                          rethrowUnknownIfAmbiguous(operationId, err);
                        }
                      })
                    }
                  >
                    Clock In
                  </Button>
                  <Button
                    size="sm"
                    variant="secondary"
                    loading={attendanceOp.inFlight}
                    onClick={() =>
                      void attendanceOp.execute(async (operationId) => {
                        try {
                          await api.recordAttendance({ operationId, eventType: "ClockOut" });
                          await refreshHostData();
                          notify("success", "Clocked out", "Attendance event saved.");
                        } catch (err) {
                          rethrowUnknownIfAmbiguous(operationId, err);
                        }
                      })
                    }
                  >
                    Clock Out
                  </Button>
                </div>
              }
            />
            <CardBody>
              <div style={{ display: "grid", gap: 16 }}>
                <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(170px, 1fr))", gap: 12, alignItems: "end" }}>
                  <FormField label="Classification" required>
                    <SelectInput value={incidentClassification} onChange={(e) => setIncidentClassification(e.target.value as IncidentClassification)}>
                      <option value="Operational">Operational</option>
                      <option value="Equipment">Equipment</option>
                      <option value="Safety">Safety</option>
                      <option value="Conduct">Conduct</option>
                      <option value="Other">Other</option>
                    </SelectInput>
                  </FormField>
                  <FormField label="Severity" required>
                    <SelectInput value={incidentSeverity} onChange={(e) => setIncidentSeverity(e.target.value)}>
                      <option value="Low">Low</option>
                      <option value="Medium">Medium</option>
                      <option value="High">High</option>
                    </SelectInput>
                  </FormField>
                  <FormField label="Incident Title" required>
                    <TextInput placeholder="Espresso grinder calibration needed" value={incidentTitle} onChange={(e) => setIncidentTitle(e.target.value)} />
                  </FormField>
                  <FormField label="Description" required>
                    <TextInput placeholder="Details for shift handover" value={incidentDescription} onChange={(e) => setIncidentDescription(e.target.value)} />
                  </FormField>
                  <Button
                    variant="secondary"
                    loading={incidentOp.inFlight}
                    disabled={!incidentTitle.trim() || !incidentDescription.trim()}
                    onClick={() =>
                      void incidentOp.execute(async (operationId) => {
                        try {
                          await api.createIncident({
                            operationId,
                            classification: incidentClassification,
                            severity: incidentSeverity,
                            title: incidentTitle.trim(),
                            description: incidentDescription.trim(),
                          });
                          setIncidentTitle("");
                          setIncidentDescription("");
                          await refreshHostData();
                          notify("success", "Incident logged", "Saved to Host incident journal.");
                        } catch (err) {
                          rethrowUnknownIfAmbiguous(operationId, err);
                        }
                      })
                    }
                  >
                    Log Incident
                  </Button>
                </div>

                <DataTable
                  caption="Recent Attendance Events"
                  rows={attendance.slice(0, 10)}
                  getRowKey={(a) => a.attendanceEventId}
                  columns={[
                    { key: "ev", header: "Event", render: (a) => <StatusBadge tone="info">{a.eventType}</StatusBadge> },
                    { key: "user", header: "Operator ID", render: (a) => a.subjectUserId.slice(0, 8) },
                    { key: "time", header: "Timestamp", render: (a) => new Date(a.occurredAtUtc).toLocaleString() },
                  ]}
                />

                <DataTable
                  caption="Operational Incidents"
                  rows={incidents}
                  getRowKey={(i) => i.incidentId}
                  columns={[
                    { key: "sev", header: "Severity", render: (i) => <StatusBadge tone={i.severity === "High" ? "danger" : "warning"}>{i.severity}</StatusBadge> },
                    { key: "cls", header: "Classification", render: (i) => i.classification },
                    { key: "title", header: "Incident", render: (i) => <strong>{i.title}</strong> },
                    { key: "st", header: "Status", render: (i) => i.status },
                    { key: "at", header: "Created", render: (i) => new Date(i.createdAtUtc).toLocaleString() },
                  ]}
                />
              </div>
            </CardBody>
          </Card>
        </div>
      ) : null}

      {activeNav === "audit" ? (
        <div style={{ display: "grid", gap: 16 }}>
          <OperationAlerts op={adminOp} title="Admin Operation" />

          <Card>
            <CardHeader title="Operator Accounts & Role Management" subtitle="Create new POS or kitchen accounts with explicit Host roles." />
            <CardBody>
              <div style={{ display: "grid", gap: 12 }}>
                <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(160px, 1fr))", gap: 10, alignItems: "end" }}>
                  <FormField label="Username" required>
                    <TextInput value={newUsername} onChange={(e) => setNewUsername(e.target.value)} />
                  </FormField>
                  <FormField label="Display Name" required>
                    <TextInput value={newDisplayName} onChange={(e) => setNewDisplayName(e.target.value)} />
                  </FormField>
                  <FormField label="Temporary Password" required>
                    <TextInput type="password" value={newPassword} onChange={(e) => setNewPassword(e.target.value)} />
                  </FormField>
                  <FormField label="Role" required>
                    <SelectInput value={newRoleId} onChange={(e) => setNewRoleId(e.target.value)}>
                      {userRoles.map((role) => (
                        <option key={role.roleId} value={role.roleId}>{role.roleName}</option>
                      ))}
                    </SelectInput>
                  </FormField>
                  <Button
                    variant="primary"
                    loading={adminOp.inFlight}
                    disabled={!newUsername.trim() || !newDisplayName.trim() || !newPassword || !newRoleId}
                    onClick={() =>
                      void adminOp.execute(async (operationId) => {
                        try {
                          await api.createUser({
                            username: newUsername.trim(),
                            displayName: newDisplayName.trim(),
                            password: newPassword,
                            roleId: newRoleId,
                          });
                          setNewUsername("");
                          setNewDisplayName("");
                          setNewPassword("");
                          await refreshHostData();
                          notify("success", "Operator created", "Account is active on local Host.");
                        } catch (err) {
                          rethrowUnknownIfAmbiguous(operationId, err);
                        }
                      })
                    }
                  >
                    Create User
                  </Button>
                </div>

                <DataTable
                  caption="Host Operators"
                  rows={users}
                  getRowKey={(u) => u.id}
                  columns={[
                    { key: "user", header: "Username", render: (u) => <strong>{u.username}</strong> },
                    { key: "disp", header: "Display Name", render: (u) => u.displayName },
                    { key: "role", header: "Role", render: (u) => u.roleName },
                    { key: "status", header: "Status", render: (u) => <StatusBadge tone={u.status === "Active" ? "success" : "neutral"}>{u.status}</StatusBadge> },
                  ]}
                />
              </div>
            </CardBody>
          </Card>

          {settingsDraft ? (
            <Card>
              <CardHeader title="Cafe Store & Receipt Settings" subtitle="Identity, receipt copy, and printer width configuration." />
              <CardBody>
                <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(190px, 1fr))", gap: 10, alignItems: "end" }}>
                  <FormField label="Store Name" required>
                    <TextInput value={settingsDraft.storeName} onChange={(e) => setSettingsDraft({ ...settingsDraft, storeName: e.target.value })} />
                  </FormField>
                  <FormField label="Business Name" required>
                    <TextInput value={settingsDraft.businessName} onChange={(e) => setSettingsDraft({ ...settingsDraft, businessName: e.target.value })} />
                  </FormField>
                  <FormField label="Address">
                    <TextInput value={settingsDraft.address ?? ""} onChange={(e) => setSettingsDraft({ ...settingsDraft, address: e.target.value })} />
                  </FormField>
                  <FormField label="Receipt Footer">
                    <TextInput value={settingsDraft.receiptFooter ?? ""} onChange={(e) => setSettingsDraft({ ...settingsDraft, receiptFooter: e.target.value })} />
                  </FormField>
                  <Button
                    variant="secondary"
                    loading={adminOp.inFlight}
                    onClick={() =>
                      void adminOp.execute(async (operationId) => {
                        try {
                          await api.updateStoreSettings(settingsDraft);
                          await refreshHostData();
                          notify("success", "Store settings updated", settingsDraft.storeName);
                        } catch (err) {
                          rethrowUnknownIfAmbiguous(operationId, err);
                        }
                      })
                    }
                  >
                    Save Store Settings
                  </Button>
                </div>
              </CardBody>
            </Card>
          ) : null}

          <Card>
            <CardHeader title="Immutable Audit Trail" subtitle="Latest security, financial, and operational audit entries." />
            <CardBody>
              <DataTable
                caption="Audit Log"
                rows={auditLogs.slice(0, 25)}
                getRowKey={(a) => a.id}
                columns={[
                  { key: "act", header: "Action", render: (a) => <strong>{a.action}</strong> },
                  { key: "ent", header: "Entity", render: (a) => `${a.entityType}${a.entityId ? ` (${a.entityId.slice(0, 8)})` : ""}` },
                  { key: "actor", header: "Actor", render: (a) => a.userDisplayName ?? "System" },
                  { key: "reason", header: "Reason", render: (a) => a.reason ?? "—" },
                  { key: "when", header: "Timestamp", render: (a) => new Date(a.createdAt).toLocaleString() },
                ]}
              />
            </CardBody>
          </Card>
        </div>
      ) : null}

      {activeNav === "host-ops" ? (
        surface === "CENTRAL_OWNER" ? (
          <Card>
            <CardHeader
              title="Windows Host Controls Disabled on Central Owner Portal"
              subtitle="Strict separation between hosted Central control plane and physical store SQLite host."
            />
            <CardBody>
              <Alert
                tone="warning"
                title="Local Windows Host Only"
                description="Database backups, cryptographic restore verification, and offline host licensing execute exclusively on the local Windows Host console (http://127.0.0.1:5399/cafe/). Switch to Windows Host View on the physical host machine to run these controls."
              />
            </CardBody>
          </Card>
        ) : (
          <div style={{ display: "grid", gap: 16 }}>
            <OperationAlerts op={hostOpsOp} title="Host Maintenance" />

            <Card>
              <CardHeader title="Salryn Cafe License Status" subtitle="Local host license posture under ProductId = Salryn.Cafe." />
              <CardBody>
                {licenseStatus ? (
                  <div style={{ display: "grid", gap: 8 }}>
                    <div style={{ display: "flex", gap: 8, alignItems: "center", flexWrap: "wrap" }}>
                      {(() => {
                        const label = formatLicenseStatus(licenseStatus.status);
                        const tone: StatusTone = label === "Licensed" || label === "Active" || label === "Valid" ? "success" : "warning";
                        return <StatusBadge tone={tone}>{label}</StatusBadge>;
                      })()}
                      <span>Edition: <strong>{formatLicenseEdition(licenseStatus.edition)}</strong></span>
                      <span>Machine Matched: <strong>{licenseStatus.machineMatched ? "Yes" : "No"}</strong></span>
                      {licenseStatus.expiresAt ? (
                        <span>Expires: <strong>{new Date(licenseStatus.expiresAt).toLocaleDateString()}</strong></span>
                      ) : null}
                    </div>
                    <p style={{ margin: 0, color: "var(--salryn-mocha)" }}>{licenseStatus.message}</p>
                  </div>
                ) : (
                  <EmptyState title="License status unavailable" description="Sign in with Backup/License permissions to inspect license state." />
                )}
              </CardBody>
            </Card>

            <Card>
              <CardHeader
                title="Host SQLite Backup & Restore"
                subtitle="Verified backup archives preserve C1–C11 ledgers. Restore requires explicit confirmation."
                action={
                  <Button
                    variant="primary"
                    loading={hostOpsOp.inFlight}
                    onClick={() =>
                      void hostOpsOp.execute(async (operationId) => {
                        try {
                          const created = await api.createBackup();
                          await refreshHostData();
                          notify("success", "Backup created", created.backupId);
                        } catch (err) {
                          rethrowUnknownIfAmbiguous(operationId, err);
                        }
                      })
                    }
                  >
                    Create Verified Backup
                  </Button>
                }
              />
              <CardBody>
                <DataTable
                  caption="Host Backup Archives"
                  rows={backups}
                  getRowKey={(b) => b.backupId}
                  columns={[
                    { key: "file", header: "Backup ID", render: (b) => <strong className="salryn-tabular">{b.backupId}</strong> },
                    { key: "size", header: "Size", align: "right", tabular: true, render: (b) => `${(b.sizeBytes / 1024).toFixed(1)} KB` },
                    { key: "at", header: "Created", render: (b) => new Date(b.createdAt).toLocaleString() },
                    {
                      key: "act",
                      header: "Actions",
                      render: (b) => (
                        <div style={{ display: "flex", gap: 6 }}>
                          <Button
                            size="sm"
                            variant="secondary"
                            onClick={() =>
                              void hostOpsOp.execute(async (operationId) => {
                                try {
                                  const res = await api.verifyBackup(b.backupId);
                                  notify(res.valid ? "success" : "danger", res.valid ? "Backup integrity verified" : "Backup verification failed", res.summary ?? b.backupId);
                                } catch (err) {
                                  rethrowUnknownIfAmbiguous(operationId, err);
                                }
                              })
                            }
                          >
                            Verify
                          </Button>
                          <Button
                            size="sm"
                            variant="danger"
                            onClick={() => {
                              setRestoreConfirmText("");
                              setRestoreTarget(b);
                            }}
                          >
                            Restore
                          </Button>
                        </div>
                      ),
                    },
                  ]}
                />
              </CardBody>
            </Card>

            <Dialog
              open={restoreTarget !== null}
              title={`Restore Backup · ${restoreTarget?.backupId ?? ""}`}
              description="Type RESTORE in uppercase to replace the active local database with this verified package."
              onClose={() => setRestoreTarget(null)}
              footer={
                <>
                  <Button variant="secondary" onClick={() => setRestoreTarget(null)}>
                    Cancel
                  </Button>
                  <Button
                    variant="danger"
                    disabled={restoreConfirmText.trim() !== "RESTORE" || hostOpsOp.inFlight}
                    loading={hostOpsOp.inFlight}
                    onClick={() => {
                      if (!restoreTarget) return;
                      void hostOpsOp.execute(async (operationId) => {
                        try {
                          await api.restoreBackup(restoreTarget.backupId, restoreConfirmText.trim());
                          setRestoreTarget(null);
                          await refreshHostData();
                          notify("warning", "Backup restored", "Local SQLite state restored from package.");
                        } catch (err) {
                          rethrowUnknownIfAmbiguous(operationId, err);
                        }
                      });
                    }}
                  >
                    Restore Database
                  </Button>
                </>
              }
            >
              <FormField label='Confirmation ("RESTORE")' required>
                <TextInput value={restoreConfirmText} onChange={(e) => setRestoreConfirmText(e.target.value)} placeholder="RESTORE" />
              </FormField>
            </Dialog>
          </div>
        )
      ) : null}
    </CafeShell>
  );
}

export function BackofficeConsoleApp({
  api,
  centralApi,
  hostApi,
  initialSurface = "WINDOWS_HOST",
  initialNav = "reports",
  onOpenWorkstation,
}: BackofficeConsoleAppProps) {
  const resolvedApi = useMemo(() => api ?? createLiveBackofficeApi(), [api]);
  const resolvedCentral = useMemo(() => centralApi ?? createLiveCentralOwnerApi(), [centralApi]);
  const resolvedHost = useMemo(() => {
    if (hostApi !== undefined) return hostApi;
    return api ? null : createLiveHostApi();
  }, [api, hostApi]);

  return (
    <ErrorBoundary>
      <ToastProvider>
        <BackofficeConsoleInner
          api={resolvedApi}
          centralApi={resolvedCentral}
          hostApi={resolvedHost}
          initialSurface={initialSurface}
          initialNav={initialNav}
          onOpenWorkstation={onOpenWorkstation}
        />
      </ToastProvider>
    </ErrorBoundary>
  );
}
