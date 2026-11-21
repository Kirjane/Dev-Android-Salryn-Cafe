import { useCallback, useEffect, useState } from "react";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BackofficeConsoleApp } from "../backoffice/BackofficeConsoleApp";
import { WorkstationApp } from "./WorkstationApp";

function resolveCafeRoute(pathname: string): {
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
