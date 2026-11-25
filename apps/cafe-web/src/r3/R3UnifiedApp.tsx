import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import {
  BarChart3,
  BookOpen,
  CalendarDays,
  ChefHat,
  ChevronDown,
  ChevronRight,
  ClipboardList,
  Clock,
  Coffee,
  FileSearch,
  HardDrive,
  LayoutDashboard,
  LogOut,
  Menu,
  Package,
  Printer,
  Settings,
  Shield,
  ShoppingBag,
  Smartphone,
  Truck,
  UserCheck,
  Users,
  Wallet,
  X,
} from "lucide-react";
import { createLiveBackofficeApi } from "../api/backofficeClient";
import { createLiveHostApi } from "../api/hostClient";
import {
  describeLicenseGateBadge,
  isLicenseActiveForBootstrap,
  type HostBootstrapStatus,
  type HostLicenseBootstrapStatus,
  type HostSession,
  type HostShiftInfo,
} from "../cashier/types";
import "./styles/r3-design-system.css";
import { R3ToastProvider, StatusPill } from "./components/R3Primitives";
import { fetchHostIdentityKey, type DurableIntentAuthority } from "./live/durableIntent";
import { R3LiveBookingsDeliveriesPage } from "./live/R3LiveBookingsDeliveriesPage";
import { R3LiveCashShiftsPage } from "./live/R3LiveCashShiftsPage";
import { R3LiveInventoryPage } from "./live/R3LiveInventoryPage";
import { R3LiveReportsPage } from "./live/R3LiveReportsPage";
import { R3LiveSalesServicePanel } from "./live/R3LiveSalesServicePanel";
import {
  R3LiveCustomersArPanel,
  R3LiveMenuPanel,
  R3LiveRecipesPanel,
  R3LiveStaffIncidentsPanel,
  R3TodayPanel,
} from "./live/R3LiveOperationsPanels";
import {
  R3LiveAuditLogPanel,
  R3LiveBackupLicensePanel,
  R3LiveDevicesPanel,
  R3LivePrintStationsPanel,
  R3LiveSettingsPanel,
  R3LiveUsersPanel,
} from "./live/R3LiveAdminPanels";
import {
  R3_ROUTES,
  R3_ROUTE_BY_KEY,
  canAccessR3Route,
  parseR3RouteFromPathname,
  resolveDefaultR3Route,
  type R3NavGroupKey,
  type R3RouteKey,
} from "./live/r3RouteMap";

const ROUTE_ICONS: Record<R3RouteKey, typeof LayoutDashboard> = {
  today: LayoutDashboard,
  pos: ShoppingBag,
  orders: ClipboardList,
  kitchen: ChefHat,
  cash: Wallet,
  menu: Coffee,
  recipes: BookOpen,
  inventory: Package,
  customers: Users,
  bookings: CalendarDays,
  deliveries: Truck,
  staff: UserCheck,
  reports: BarChart3,
  users: Shield,
  settings: Settings,
  printers: Printer,
  devices: Smartphone,
  backup: HardDrive,
  audit: FileSearch,
};

const GROUP_ORDER: readonly { key: R3NavGroupKey; collapsible?: boolean }[] = [
  { key: "Overview" },
  { key: "Sales & Service" },
  { key: "Menu & Stock" },
  { key: "Operations" },
  { key: "Insights" },
  { key: "Administration", collapsible: true },
];

function useManilaClock(): string {
  const format = () =>
    new Intl.DateTimeFormat("en-PH", {
      timeZone: "Asia/Manila",
      weekday: "short",
      month: "short",
      day: "numeric",
      hour: "numeric",
      minute: "2-digit",
    }).format(new Date());

  const [nowText, setNowText] = useState(format);
  useEffect(() => {
    const id = window.setInterval(() => setNowText(format()), 30_000);
    return () => window.clearInterval(id);
  }, []);
  return nowText;
}

function getInitials(name: string | undefined): string {
  if (!name) return "OP";
  const parts = name.trim().split(/\s+/);
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
  return `${parts[0][0]}${parts[parts.length - 1][0]}`.toUpperCase();
}

function R3UnifiedInner({
  pathname,
  onNavigatePath,
}: {
  pathname: string;
  onNavigatePath: (path: string) => void;
}) {
  const hostApi = useMemo(() => createLiveHostApi(), []);
  const backofficeApi = useMemo(() => createLiveBackofficeApi(), []);

  const [gateLoading, setGateLoading] = useState(true);
  const [gateError, setGateError] = useState<string | null>(null);
  const [licenseStatus, setLicenseStatus] = useState<HostLicenseBootstrapStatus | null>(null);
  const [bootstrapStatus, setBootstrapStatus] = useState<HostBootstrapStatus | null>(null);
  const [session, setSession] = useState<HostSession>({ isAuthenticated: false });
  const [hostIdentityKey, setHostIdentityKey] = useState<string>("");

  // License activation form
  const [licenseJson, setLicenseJson] = useState("");
  const [licenseMsg, setLicenseMsg] = useState<string | null>(null);

  // First-admin bootstrap form
  const [storeName, setStoreName] = useState("Salryn Cafe");
  const [firstAdminUser, setFirstAdminUser] = useState("admin");
  const [firstAdminName, setFirstAdminName] = useState("Cafe Administrator");
  const [firstAdminPass, setFirstAdminPass] = useState("");
  const [bootstrapMsg, setBootstrapMsg] = useState<string | null>(null);

  // Operator login form
  const [loginUser, setLoginUser] = useState("");
  const [loginPass, setLoginPass] = useState("");
  const [loginError, setLoginError] = useState<string | null>(null);
  const [authBusy, setAuthBusy] = useState(false);

  // Shell live telemetry
  const [shift, setShift] = useState<HostShiftInfo | null>(null);
  const [kitchenBadge, setKitchenBadge] = useState(0);
  const [lowStockBadge, setLowStockBadge] = useState(0);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [adminOpen, setAdminOpen] = useState(false);
  const [dataVersion, setDataVersion] = useState(0);
  const manilaClock = useManilaClock();

  const loadGateState = useCallback(async () => {
    setGateLoading(true);
    setGateError(null);
    try {
      const lic = hostApi.getLicenseStatus
        ? await hostApi.getLicenseStatus()
        : { status: "Unlicensed", machineMatched: false };
      setLicenseStatus(lic);
      if (!isLicenseActiveForBootstrap(lic)) {
        setBootstrapStatus(null);
        setSession({ isAuthenticated: false });
        return;
      }

      const [boot, sess, idKey] = await Promise.all([
        hostApi.getBootstrapStatus ? hostApi.getBootstrapStatus() : Promise.resolve(null),
        hostApi.getSession().catch(() => ({ isAuthenticated: false })),
        fetchHostIdentityKey(),
      ]);
      setBootstrapStatus(boot);
      setSession(sess);
      setHostIdentityKey(idKey);
    } catch (err) {
      setGateError(err instanceof Error ? err.message : "Could not connect to local Salryn Host.");
    } finally {
      setGateLoading(false);
    }
  }, [hostApi]);

  useEffect(() => {
    void loadGateState();
  }, [loadGateState]);

  const permissions = session.user?.permissions ?? [];
  const authority: DurableIntentAuthority | null = useMemo(
    () =>
      session.isAuthenticated && session.user?.userId && hostIdentityKey
        ? { hostIdentityKey, actorUserId: session.user.userId }
        : null,
    [session.isAuthenticated, session.user?.userId, hostIdentityKey],
  );

  // Live shell badges
  useEffect(() => {
    if (!session.isAuthenticated || !session.user) return;
    let active = true;
    const perms = session.user.permissions;
    Promise.all([
      perms.includes("Shift.OpenClose") ? hostApi.getShift().then((r) => r.shift).catch(() => null) : Promise.resolve(null),
      perms.includes("Orders.View") ? hostApi.getOrders().catch(() => []) : Promise.resolve([]),
      perms.includes("Inventory.View") ? backofficeApi.getLowStock().catch(() => []) : Promise.resolve([]),
    ]).then(([sh, orders, low]) => {
      if (!active) return;
      setShift(sh);
      setKitchenBadge(orders.filter((o) => o.state === "Submitted" || o.state === "Preparing").length);
      setLowStockBadge(low.length);
    });
    return () => {
      active = false;
    };
  }, [session, hostApi, backofficeApi, dataVersion]);

  const requestedRoute = parseR3RouteFromPathname(pathname);
  const defaultRoute = resolveDefaultR3Route(permissions);
  const activeRoute: R3RouteKey | null =
    requestedRoute && canAccessR3Route(permissions, requestedRoute)
      ? requestedRoute
      : defaultRoute;

  const navigateRoute = useCallback(
    (nextKey: R3RouteKey) => {
      if (!canAccessR3Route(permissions, nextKey)) return;
      const targetPath = R3_ROUTE_BY_KEY[nextKey].path;
      onNavigatePath(targetPath);
      setDrawerOpen(false);
    },
    [permissions, onNavigatePath],
  );

  const handleInstallLicense = async (e: FormEvent) => {
    e.preventDefault();
    if (!hostApi.installLicense || !licenseJson.trim()) {
      setLicenseMsg("Paste a signed Salryn Cafe Host license JSON payload.");
      return;
    }
    setAuthBusy(true);
    setLicenseMsg(null);
    try {
      const res = await hostApi.installLicense(licenseJson.trim());
      setLicenseMsg(res.message ?? "License submitted. Verifying Host status…");
      setLicenseJson("");
      await loadGateState();
    } catch (err) {
      setLicenseMsg(err instanceof Error ? err.message : "License installation failed.");
    } finally {
      setAuthBusy(false);
    }
  };

  const handleCreateFirstAdmin = async (e: FormEvent) => {
    e.preventDefault();
    if (!hostApi.createFirstAdmin) return;
    setAuthBusy(true);
    setBootstrapMsg(null);
    try {
      await hostApi.createFirstAdmin({
        storeName: storeName.trim(),
        receiptWidthMm: 80,
        adminUsername: firstAdminUser.trim(),
        adminDisplayName: firstAdminName.trim(),
        adminPassword: firstAdminPass,
      });
      setFirstAdminPass("");
      setBootstrapMsg("Initial administrator created. Sign in below.");
      await loadGateState();
    } catch (err) {
      setBootstrapMsg(err instanceof Error ? err.message : "First-admin setup failed.");
    } finally {
      setAuthBusy(false);
    }
  };

  const handleLogin = async (e: FormEvent) => {
    e.preventDefault();
    setAuthBusy(true);
    setLoginError(null);
    try {
      const sess = await hostApi.login(loginUser.trim(), loginPass);
      setLoginPass("");
      setSession(sess);
      const idKey = await fetchHostIdentityKey();
      setHostIdentityKey(idKey);
      const nextDefault = resolveDefaultR3Route(sess.user?.permissions ?? []);
      if (nextDefault) {
        onNavigatePath(R3_ROUTE_BY_KEY[nextDefault].path);
      }
    } catch (err) {
      setLoginError(err instanceof Error ? err.message : "Sign-in failed.");
    } finally {
      setAuthBusy(false);
    }
  };

  const handleLogout = async () => {
    setAuthBusy(true);
    try {
      await hostApi.logout();
    } catch {
      // Proceed to clear local session view.
    } finally {
      setSession({ isAuthenticated: false });
      setShift(null);
      setAuthBusy(false);
    }
  };

  // ---------- GATE 1: Loading / Host Unreachable ----------
  if (gateLoading) {
    return (
      <main className="r3-app" style={{ display: "grid", placeItems: "center", padding: 24 }}>
        <div className="r3-card" style={{ maxWidth: 460, width: "100%", padding: 28, textAlign: "center" }}>
          <div className="r3-sidebar-brand-mark" style={{ margin: "0 auto 12px" }} aria-hidden="true">S</div>
          <h1 className="r3-page-title" style={{ marginBottom: 6 }}>Salryn Cafe</h1>
          <p className="r3-text-muted">Connecting to local Host SQLite authority…</p>
          <progress aria-label="Connecting to Host" style={{ width: "100%", marginTop: 12 }} />
        </div>
      </main>
    );
  }

  if (gateError) {
    return (
      <main className="r3-app" style={{ display: "grid", placeItems: "center", padding: 24 }}>
        <div className="r3-card" style={{ maxWidth: 500, width: "100%", padding: 28 }}>
          <h1 className="r3-page-title" style={{ marginBottom: 8 }}>Local Host Unavailable</h1>
          <div className="r3-alert r3-alert--danger" role="alert" style={{ marginBottom: 14 }}>
            <div className="r3-alert-body">{gateError}</div>
          </div>
          <button type="button" className="r3-btn r3-btn--primary" onClick={() => void loadGateState()}>
            Retry Host Connection
          </button>
        </div>
      </main>
    );
  }

  // ---------- GATE 2: Fail-Closed Host License Gate ----------
  if (!isLicenseActiveForBootstrap(licenseStatus)) {
    const badge = describeLicenseGateBadge(licenseStatus);
    return (
      <main className="r3-app" style={{ display: "grid", placeItems: "center", padding: 24 }}>
        <div className="r3-card" style={{ maxWidth: 560, width: "100%", padding: 28 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 12, marginBottom: 14 }}>
            <div className="r3-sidebar-brand-mark" aria-hidden="true">S</div>
            <div>
              <h1 className="r3-page-title" style={{ margin: 0 }}>Salryn Cafe — Host License Required</h1>
              <div style={{ marginTop: 4 }}>
                <StatusPill tone={badge.tone === "danger" ? "danger" : "warning"}>{badge.label}</StatusPill>
              </div>
            </div>
          </div>
          <p className="r3-text-sm r3-text-muted" style={{ marginBottom: 14 }}>
            {licenseStatus?.message ?? "Install a valid machine-bound Salryn.Cafe Host license before initializing or signing in."}
          </p>
          {licenseStatus?.machineCode && (
            <div className="r3-alert r3-alert--info" style={{ marginBottom: 14 }}>
              <div className="r3-alert-body">
                Machine Code: <code>{licenseStatus.machineCode}</code>
              </div>
            </div>
          )}
          <form onSubmit={(e) => void handleInstallLicense(e)} style={{ display: "grid", gap: 12 }}>
            <label>
              <div className="r3-text-xs r3-text-muted" style={{ marginBottom: 4 }}>Signed Host License JSON</div>
              <textarea
                className="r3-input"
                aria-label="Signed Host License JSON"
                rows={5}
                value={licenseJson}
                onChange={(e) => setLicenseJson(e.target.value)}
                placeholder='{"payload": ..., "signature": ...}'
                disabled={authBusy}
              />
            </label>
            <div style={{ display: "flex", gap: 8 }}>
              <button type="submit" className="r3-btn r3-btn--primary" disabled={authBusy}>
                {authBusy ? "Verifying…" : "Install Host License"}
              </button>
              <button type="button" className="r3-btn r3-btn--outline" onClick={() => void loadGateState()} disabled={authBusy}>
                Re-Check License
              </button>
            </div>
          </form>
          {licenseMsg && <p role="status" className="r3-text-sm" style={{ marginTop: 12 }}>{licenseMsg}</p>}
        </div>
      </main>
    );
  }

  // ---------- GATE 3: First-Admin Bootstrap ----------
  if (bootstrapStatus?.firstRunRequired && !bootstrapStatus?.hasUsers) {
    return (
      <main className="r3-app" style={{ display: "grid", placeItems: "center", padding: 24 }}>
        <div className="r3-card" style={{ maxWidth: 520, width: "100%", padding: 28 }}>
          <h1 className="r3-page-title" style={{ marginBottom: 6 }}>First-Run Cafe Setup</h1>
          <p className="r3-text-sm r3-text-muted" style={{ marginBottom: 16 }}>
            Create the initial store profile and Administrator account on this licensed Host.
          </p>
          <form onSubmit={(e) => void handleCreateFirstAdmin(e)} style={{ display: "grid", gap: 12 }}>
            <label>
              <div className="r3-text-xs r3-text-muted">Store Name</div>
              <input className="r3-input" aria-label="Store Name" value={storeName} onChange={(e) => setStoreName(e.target.value)} disabled={authBusy} />
            </label>
            <label>
              <div className="r3-text-xs r3-text-muted">Admin Username</div>
              <input className="r3-input" aria-label="Admin Username" value={firstAdminUser} onChange={(e) => setFirstAdminUser(e.target.value)} disabled={authBusy} />
            </label>
            <label>
              <div className="r3-text-xs r3-text-muted">Admin Display Name</div>
              <input className="r3-input" aria-label="Admin Display Name" value={firstAdminName} onChange={(e) => setFirstAdminName(e.target.value)} disabled={authBusy} />
            </label>
            <label>
              <div className="r3-text-xs r3-text-muted">Admin Password</div>
              <input type="password" className="r3-input" aria-label="Admin Password" value={firstAdminPass} onChange={(e) => setFirstAdminPass(e.target.value)} disabled={authBusy} />
            </label>
            <button type="submit" className="r3-btn r3-btn--primary" disabled={authBusy}>
              {authBusy ? "Initializing…" : "Initialize Cafe Host"}
            </button>
          </form>
          {bootstrapMsg && <p role="status" className="r3-text-sm" style={{ marginTop: 12 }}>{bootstrapMsg}</p>}
        </div>
      </main>
    );
  }

  // ---------- GATE 4: Real Operator Sign-In ----------
  if (!session.isAuthenticated || !session.user) {
    return (
      <main className="r3-app" style={{ display: "grid", placeItems: "center", padding: 24 }}>
        <div className="r3-card" style={{ maxWidth: 440, width: "100%", padding: 28 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 12, marginBottom: 16 }}>
            <div className="r3-sidebar-brand-mark" aria-hidden="true">S</div>
            <div>
              <h1 className="r3-page-title" style={{ margin: 0 }}>Salryn Cafe Sign In</h1>
              <p className="r3-text-xs r3-text-muted" style={{ margin: "2px 0 0" }}>
                Authenticated local Host operator session
              </p>
            </div>
          </div>

          {loginError && (
            <div className="r3-alert r3-alert--danger" role="alert" style={{ marginBottom: 12 }}>
              <div className="r3-alert-body">{loginError}</div>
            </div>
          )}

          <form onSubmit={(e) => void handleLogin(e)} style={{ display: "grid", gap: 12 }}>
            <label>
              <div className="r3-text-xs r3-text-muted">Username</div>
              <input
                className="r3-input"
                aria-label="Username"
                autoComplete="username"
                value={loginUser}
                onChange={(e) => setLoginUser(e.target.value)}
                disabled={authBusy}
              />
            </label>
            <label>
              <div className="r3-text-xs r3-text-muted">Password</div>
              <input
                type="password"
                className="r3-input"
                aria-label="Password"
                autoComplete="current-password"
                value={loginPass}
                onChange={(e) => setLoginPass(e.target.value)}
                disabled={authBusy}
              />
            </label>
            <button type="submit" className="r3-btn r3-btn--primary" disabled={authBusy}>
              {authBusy ? "Signing in…" : "Sign In"}
            </button>
          </form>
        </div>
      </main>
    );
  }

  // ---------- GATE 5: Unified Role-Aware R3 Application Shell ----------
  const visibleGroups = GROUP_ORDER.map((g) => ({
    ...g,
    items: R3_ROUTES.filter((r) => r.group === g.key && canAccessR3Route(permissions, r.key)),
  })).filter((g) => g.items.length > 0);

  const isAdminRoute = activeRoute !== null && R3_ROUTE_BY_KEY[activeRoute].group === "Administration";
  const effectiveAdminOpen = adminOpen || isAdminRoute;
  const activeDef = activeRoute ? R3_ROUTE_BY_KEY[activeRoute] : null;
  const operatorName = session.user.displayName || session.user.username;
  const operatorRole = session.user.roleName || "Operator";

  return (
    <div className="r3-app">
      <a href="#r3-main-content" className="r3-skip-link">
        Skip to main content
      </a>

      <div
        className={`r3-nav-backdrop ${drawerOpen ? "r3-nav-backdrop--open" : ""}`}
        onClick={() => setDrawerOpen(false)}
        aria-hidden="true"
      />

      <div className="r3-shell">
        <aside className={`r3-sidebar ${drawerOpen ? "r3-sidebar--open" : ""}`} aria-label="Primary navigation">
          <div className="r3-sidebar-brand">
            <div className="r3-sidebar-brand-mark" aria-hidden="true">S</div>
            <div style={{ minWidth: 0, flex: 1 }}>
              <div className="r3-sidebar-brand-title">Salryn Cafe</div>
              <div className="r3-sidebar-brand-sub">Live Host Operations</div>
            </div>
            <button
              type="button"
              className="r3-menu-toggle"
              style={{
                background: "transparent",
                borderColor: "var(--r3-nav-border)",
                color: "var(--r3-nav-ink)",
                width: 34,
                height: 34,
              }}
              onClick={() => setDrawerOpen(false)}
              aria-label="Close navigation menu"
            >
              <X size={18} />
            </button>
          </div>

          <nav className="r3-nav-scroll" aria-label="Cafe sections">
            {visibleGroups.map((group) => {
              const isCollapsed = Boolean(group.collapsible) && !effectiveAdminOpen;
              return (
                <div key={group.key}>
                  {group.collapsible ? (
                    <button
                      type="button"
                      className="r3-nav-group-toggle"
                      aria-expanded={!isCollapsed}
                      onClick={() => setAdminOpen((v) => !v)}
                    >
                      <span>{group.key}</span>
                      {isCollapsed ? <ChevronRight size={14} /> : <ChevronDown size={14} />}
                    </button>
                  ) : (
                    <div className="r3-nav-group-label">{group.key}</div>
                  )}

                  {!isCollapsed && (
                    <div className="r3-nav-list">
                      {group.items.map((item) => {
                        const Icon = ROUTE_ICONS[item.key];
                        const isActive = activeRoute === item.key;
                        return (
                          <button
                            key={item.key}
                            type="button"
                            className="r3-nav-item"
                            aria-current={isActive ? "page" : undefined}
                            onClick={() => navigateRoute(item.key)}
                          >
                            <Icon size={17} aria-hidden="true" style={{ flexShrink: 0 }} />
                            <span>{item.label}</span>
                            {item.key === "kitchen" && kitchenBadge > 0 && (
                              <span className="r3-nav-badge r3-nav-badge--accent r3-tabular">{kitchenBadge}</span>
                            )}
                            {item.key === "inventory" && lowStockBadge > 0 && (
                              <span className="r3-nav-badge r3-nav-badge--warn r3-tabular">{lowStockBadge}</span>
                            )}
                          </button>
                        );
                      })}
                    </div>
                  )}
                </div>
              );
            })}
          </nav>

          <div className="r3-sidebar-footer">
            <div className="r3-avatar-chip" style={{ justifyContent: "space-between", width: "100%" }}>
              <div style={{ display: "flex", alignItems: "center", gap: 8, minWidth: 0 }}>
                <span className="r3-avatar-circle" aria-hidden="true">{getInitials(operatorName)}</span>
                <div style={{ minWidth: 0 }}>
                  <div className="r3-avatar-name">{operatorName}</div>
                  <div className="r3-avatar-role">{operatorRole}</div>
                </div>
              </div>
              <button
                type="button"
                className="r3-btn r3-btn--ghost r3-btn--sm"
                style={{ color: "var(--r3-nav-ink)", padding: "6px 8px" }}
                onClick={() => void handleLogout()}
                disabled={authBusy}
                aria-label="Sign out"
                title="Sign out"
              >
                <LogOut size={15} aria-hidden="true" />
              </button>
            </div>
          </div>
        </aside>

        <div className="r3-main-col">
          <header className="r3-topbar" aria-label="Operational status bar">
            <div className="r3-topbar-left">
              <button
                type="button"
                className="r3-menu-toggle"
                onClick={() => setDrawerOpen(true)}
                aria-label="Open navigation menu"
                aria-expanded={drawerOpen}
              >
                <Menu size={20} />
              </button>

              <div className="r3-breadcrumb" aria-label="Current location">
                <span className="r3-breadcrumb-store">Salryn Cafe</span>
                <span className="r3-breadcrumb-sep" aria-hidden="true">/</span>
                <span className="r3-breadcrumb-group">{activeDef?.group ?? "Workspace"}</span>
                <span className="r3-breadcrumb-sep r3-hide-sm" aria-hidden="true">/</span>
                <span className="r3-breadcrumb-page">{activeDef?.label ?? "Restricted"}</span>
              </div>
            </div>

            <div className="r3-topbar-right">
              <div className="r3-status-clock r3-hide-md" title="Asia/Manila local store time">
                <Clock size={14} aria-hidden="true" />
                <span className="r3-tabular">{manilaClock} PHT</span>
              </div>

              {shift && canAccessR3Route(permissions, "cash") && (
                <button
                  type="button"
                  className="r3-status-btn"
                  onClick={() => navigateRoute("cash")}
                  title="Open Cash Drawer & Shifts"
                >
                  <StatusPill tone={shift.shiftState === "Open" ? "success" : "neutral"}>
                    <span
                      className={`r3-status-dot ${shift.shiftState === "Open" ? "r3-status-dot--live" : ""}`}
                      aria-hidden="true"
                    />
                    Shift {shift.shiftState === "Open" ? "Open" : "Closed"}
                  </StatusPill>
                </button>
              )}

              <button
                type="button"
                className="r3-btn r3-btn--outline r3-btn--sm"
                onClick={() => void handleLogout()}
                disabled={authBusy}
              >
                Sign Out
              </button>
            </div>
          </header>

          <main id="r3-main-content" className="r3-content" tabIndex={-1}>
            {!activeRoute && (
              <div className="r3-alert r3-alert--danger" role="alert">
                <div className="r3-alert-body">
                  <div className="r3-alert-title">No Permitted Modules</div>
                  <div>Your account does not hold permissions for any Salryn Cafe module. Contact an Administrator.</div>
                </div>
              </div>
            )}

            {activeRoute === "today" && <R3TodayPanel session={session} onNavigate={navigateRoute} />}
            {(activeRoute === "pos" || activeRoute === "orders" || activeRoute === "kitchen") && (
              <R3LiveSalesServicePanel
                view={activeRoute}
                session={session}
                authority={authority}
                onNavigateView={(next) => navigateRoute(next)}
                onDataChanged={() => setDataVersion((v) => v + 1)}
              />
            )}
            {activeRoute === "cash" && <R3LiveCashShiftsPage embedded />}
            {activeRoute === "menu" && <R3LiveMenuPanel />}
            {activeRoute === "recipes" && <R3LiveRecipesPanel authority={authority} />}
            {activeRoute === "inventory" && <R3LiveInventoryPage embedded />}
            {activeRoute === "customers" && <R3LiveCustomersArPanel session={session} authority={authority} />}
            {activeRoute === "bookings" && <R3LiveBookingsDeliveriesPage embedded initialTab="bookings" />}
            {activeRoute === "deliveries" && <R3LiveBookingsDeliveriesPage embedded initialTab="deliveries" />}
            {activeRoute === "staff" && <R3LiveStaffIncidentsPanel session={session} authority={authority} />}
            {activeRoute === "reports" && <R3LiveReportsPage embedded />}
            {activeRoute === "users" && <R3LiveUsersPanel />}
            {activeRoute === "settings" && <R3LiveSettingsPanel />}
            {activeRoute === "printers" && <R3LivePrintStationsPanel authority={authority} />}
            {activeRoute === "devices" && <R3LiveDevicesPanel />}
            {activeRoute === "backup" && <R3LiveBackupLicensePanel />}
            {activeRoute === "audit" && <R3LiveAuditLogPanel />}
          </main>
        </div>
      </div>
    </div>
  );
}

export function R3UnifiedApp({
  pathname,
  onNavigatePath,
}: {
  pathname: string;
  onNavigatePath: (path: string) => void;
}) {
  return (
    <R3ToastProvider>
      <R3UnifiedInner pathname={pathname} onNavigatePath={onNavigatePath} />
    </R3ToastProvider>
  );
}
