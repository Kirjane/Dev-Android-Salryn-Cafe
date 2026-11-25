import { lazy, Suspense, useCallback, useEffect, useState } from "react";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BackofficeConsoleApp } from "../backoffice/BackofficeConsoleApp";
import { WorkstationApp } from "./WorkstationApp";

export const C1_RECOVERY_ROUTE_ENABLED =
  import.meta.env.DEV || import.meta.env.VITE_ENABLE_C1_RECOVERY === "1";

// Explicit development-only integration routes; existing default routes are unchanged.
const R3_LIVE_REPORTS_ENABLED =
  import.meta.env.DEV &&
  (import.meta.env.VITE_ENABLE_R3_LIVE_REPORTS === "1" || import.meta.env.VITE_ENABLE_R3_LIVE_MODULES === "1");
const R3LiveReportsPage = R3_LIVE_REPORTS_ENABLED
  ? lazy(() => import("../r3/live/R3LiveReportsPage").then(m => ({default:m.R3LiveReportsPage})))
  : null;

const R3_LIVE_INVENTORY_ENABLED =
  import.meta.env.DEV &&
  (import.meta.env.VITE_ENABLE_R3_LIVE_INVENTORY === "1" || import.meta.env.VITE_ENABLE_R3_LIVE_MODULES === "1");
const R3LiveInventoryPage = R3_LIVE_INVENTORY_ENABLED
  ? lazy(() => import("../r3/live/R3LiveInventoryPage").then(m => ({default:m.R3LiveInventoryPage})))
  : null;

const R3_LIVE_BOOKINGS_ENABLED =
  import.meta.env.DEV &&
  (import.meta.env.VITE_ENABLE_R3_LIVE_BOOKINGS === "1" || import.meta.env.VITE_ENABLE_R3_LIVE_MODULES === "1");
const R3LiveBookingsDeliveriesPage = R3_LIVE_BOOKINGS_ENABLED
  ? lazy(() => import("../r3/live/R3LiveBookingsDeliveriesPage").then(m => ({default:m.R3LiveBookingsDeliveriesPage})))
  : null;

const R3_LIVE_CASH_ENABLED =
  import.meta.env.DEV &&
  (import.meta.env.VITE_ENABLE_R3_LIVE_CASH === "1" || import.meta.env.VITE_ENABLE_R3_LIVE_MODULES === "1");
const R3LiveCashShiftsPage = R3_LIVE_CASH_ENABLED
  ? lazy(() => import("../r3/live/R3LiveCashShiftsPage").then(m => ({default:m.R3LiveCashShiftsPage})))
  : null;

const CafeMenuManagerRecoveryPage = C1_RECOVERY_ROUTE_ENABLED
  ? lazy(() =>
      import("../recovery/CafeMenuManagerRecovery").then((m) => ({
        default: m.CafeMenuManagerRecoveryPage,
      })),
    )
  : null;

export function isC1RecoveryRoute(
  pathname: string,
  recoveryEnabled = C1_RECOVERY_ROUTE_ENABLED,
): boolean {
  return Boolean(recoveryEnabled) && pathname.replace(/\/+$/, "") === "/cafe/recovery/c1";
}

export function resolveCafeRoute(pathname: string): {
  mode: "workstation" | "backoffice" | "central";
  initialNav?: string;
} {
  const normalized = pathname.replace(/\/+$/, "");
  if (normalized === "/cafe/backoffice" || normalized.startsWith("/cafe/backoffice/")) {
    return { mode: "backoffice", initialNav: "reports" };
  }
  if (normalized === "/cafe/central" || normalized.startsWith("/cafe/central/")) {
    return { mode: "central", initialNav: "devices" };
  }
  if (normalized === "/cafe/orders") {
    return { mode: "workstation", initialNav: "orders" };
  }
  if (normalized === "/cafe/kitchen") {
    return { mode: "workstation", initialNav: "kitchen" };
  }
  return { mode: "workstation", initialNav: "cashier" };
}

function CafeRootApp() {
  const [pathname, setPathname] = useState(() => window.location.pathname);

  useEffect(() => {
    const onPopState = () => setPathname(window.location.pathname);
    window.addEventListener("popstate", onPopState);
    return () => window.removeEventListener("popstate", onPopState);
  }, []);

  const navigate = useCallback((nextPath: string) => {
    if (window.location.pathname !== nextPath) {
      window.history.pushState({}, "", nextPath);
      setPathname(nextPath);
    }
  }, []);

  if (C1_RECOVERY_ROUTE_ENABLED && CafeMenuManagerRecoveryPage && pathname.replace(/\/+$/, "") === "/cafe/recovery/c1") {
    return (
      <Suspense fallback={<div style={{ padding: 24 }}>Loading C1 recovery workspace...</div>}>
        <CafeMenuManagerRecoveryPage onOpenWorkstation={() => navigate("/cafe/")} />
      </Suspense>
    );
  }

  if (R3_LIVE_REPORTS_ENABLED && R3LiveReportsPage && pathname.replace(/\/+$/, "") === "/cafe/integration/r3/reports") {
    return <Suspense fallback={<div style={{padding:24}}>Loading live R3 reports...</div>}><R3LiveReportsPage /></Suspense>;
  }

  if (R3_LIVE_INVENTORY_ENABLED && R3LiveInventoryPage && pathname.replace(/\/+$/, "") === "/cafe/integration/r3/inventory") {
    return <Suspense fallback={<div style={{padding:24}}>Loading live R3 inventory...</div>}><R3LiveInventoryPage /></Suspense>;
  }

  if (R3_LIVE_BOOKINGS_ENABLED && R3LiveBookingsDeliveriesPage && pathname.replace(/\/+$/, "") === "/cafe/integration/r3/bookings") {
    return <Suspense fallback={<div style={{padding:24}}>Loading live R3 bookings...</div>}><R3LiveBookingsDeliveriesPage initialTab="bookings" /></Suspense>;
  }

  if (R3_LIVE_BOOKINGS_ENABLED && R3LiveBookingsDeliveriesPage && pathname.replace(/\/+$/, "") === "/cafe/integration/r3/deliveries") {
    return <Suspense fallback={<div style={{padding:24}}>Loading live R3 deliveries...</div>}><R3LiveBookingsDeliveriesPage initialTab="deliveries" /></Suspense>;
  }

  if (R3_LIVE_CASH_ENABLED && R3LiveCashShiftsPage && pathname.replace(/\/+$/, "") === "/cafe/integration/r3/cash-shifts") {
    return <Suspense fallback={<div style={{padding:24}}>Loading live R3 cash & shifts...</div>}><R3LiveCashShiftsPage /></Suspense>;
  }

  const route = resolveCafeRoute(pathname);
  if (route.mode === "backoffice") {
    return (
      <BackofficeConsoleApp
        initialSurface="WINDOWS_HOST"
        initialNav={route.initialNav}
        onOpenWorkstation={() => navigate("/cafe/")}
      />
    );
  }
  if (route.mode === "central") {
    return (
      <BackofficeConsoleApp
        initialSurface="CENTRAL_OWNER"
        initialNav={route.initialNav}
        onOpenWorkstation={() => navigate("/cafe/")}
      />
    );
  }

  return (
    <WorkstationApp
      initialNav={route.initialNav}
      onOpenBackoffice={() => navigate("/cafe/backoffice")}
    />
  );
}

const rootElement = document.getElementById("root");
if (rootElement) {
  createRoot(rootElement).render(
    <StrictMode>
      <CafeRootApp />
    </StrictMode>,
  );
}
