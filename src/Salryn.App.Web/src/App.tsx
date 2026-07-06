import { useEffect, useState, type ReactNode } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { logout, type SessionState } from "./api/session";
import { AppShell } from "./components/AppShell";
import { Button, Card, CardBody, CommandBar } from "./components/ui";
import { useAppSession } from "./hooks/useAppSession";
import { type AppPage } from "./navigation";
import { AuditPage } from "./pages/AuditPage";
import { DashboardPage } from "./pages/DashboardPage";
import { HostLanControlPage } from "./pages/HostLanControlPage";
import { InventoryPage } from "./pages/InventoryPage";
import { LoginPage } from "./pages/LoginPage";
import { ProductsPage } from "./pages/ProductsPage";
import { RemoteClientLandingPage } from "./pages/RemoteClientLandingPage";
import { RemotePosClientPage } from "./pages/RemotePosClientPage";
import { RemoteReadOnlyClientPage } from "./pages/RemoteReadOnlyClientPage";
import { SalesHistoryPage } from "./pages/SalesHistoryPage";
import { UsersPage } from "./pages/UsersPage";
import { canAccessPage, getFirstAllowedPage } from "./security/permissions";
import { isRemoteClientBrowserLocation } from "./utils/lanEntry";

function FullScreenCenter({ children }: { children: ReactNode }) {
  return <div className="flex min-h-screen items-center justify-center bg-navy-900 px-4">{children}</div>;
}

function AccessDenied({ currentPage, onGoHome }: { currentPage: AppPage; onGoHome: () => void }) {
  return (
    <div className="space-y-6">
      <CommandBar
        title="Access denied"
        subtitle="Your current operator permissions do not allow this page."
      />
      <Card>
        <CardBody className="space-y-4">
          <p className="text-sm text-slate-600">
            The page request was blocked before rendering: <span className="font-medium">{currentPage}</span>.
          </p>
          <Button variant="secondary" onClick={onGoHome}>
            Go to allowed page
          </Button>
        </CardBody>
      </Card>
    </div>
  );
}

function renderPage(page: AppPage, session: SessionState, onLogout: () => void, onNavigate: (page: AppPage) => void) {
  switch (page) {
    case "dashboard":
      return <DashboardPage session={session} onLogout={onLogout} onOpenHostLanControl={() => onNavigate("host-lan-control")} />;
    case "remote-readonly":
      return <RemoteReadOnlyClientPage />;
    case "remote-pos":
      return <RemotePosClientPage />;
    case "host-lan-control":
      return <HostLanControlPage />;
    case "products":
      return <ProductsPage />;
    case "inventory":
      return <InventoryPage />;
    case "sales":
      return <SalesHistoryPage />;
    case "users":
      return <UsersPage />;
    case "audit":
      return <AuditPage />;
    default: {
      const exhaustiveCheck: never = page;
      return exhaustiveCheck;
    }
  }
}

type RemoteClientMode = "landing" | "remote-pos" | "remote-readonly";

function RemoteClientApp() {
  const [mode, setMode] = useState<RemoteClientMode>("landing");

  if (mode === "landing") {
    return (
      <RemoteClientLandingPage
        onOpenRemotePos={() => setMode("remote-pos")}
        onOpenReadOnly={() => setMode("remote-readonly")}
      />
    );
  }

  return (
    <div className="min-h-screen bg-slate-100 p-6">
      <div className="mx-auto max-w-7xl space-y-4">
        <Button variant="secondary" onClick={() => setMode("landing")}>
          Back to remote client entry
        </Button>
        {mode === "remote-pos" ? <RemotePosClientPage /> : <RemoteReadOnlyClientPage />}
      </div>
    </div>
  );
}

function ConsoleApp() {
  const queryClient = useQueryClient();
  const sessionQuery = useAppSession();
  const [currentPage, setCurrentPage] = useState<AppPage>("host-lan-control");

  useEffect(() => {
    const session = sessionQuery.data;

    if (!session?.isAuthenticated) {
      return;
    }

    if (canAccessPage(session, currentPage)) {
      return;
    }

    const firstAllowedPage = getFirstAllowedPage(session);

    if (firstAllowedPage && firstAllowedPage !== currentPage) {
      setCurrentPage(firstAllowedPage);
    }
  }, [currentPage, sessionQuery.data]);

  async function handleLogout() {
    try {
      await logout();
    } catch {
    }
    queryClient.removeQueries();
    void sessionQuery.refetch();
  }

  if (sessionQuery.isLoading) {
    return <FullScreenCenter><p className="text-sm text-slate-300">Connecting to the Salryn local API...</p></FullScreenCenter>;
  }

  if (sessionQuery.isError) {
    return (
      <FullScreenCenter>
        <div className="w-full max-w-md rounded-2xl border border-slate-200 bg-white p-8 shadow-card">
          <h1 className="text-xl font-semibold text-slate-900">Salryn local API unavailable</h1>
          <p className="mt-2 text-sm text-slate-500">The console could not reach the Salryn local API on this device.</p>
          <div className="mt-6"><Button onClick={() => void sessionQuery.refetch()}>Try again</Button></div>
        </div>
      </FullScreenCenter>
    );
  }

  if (!sessionQuery.data?.isAuthenticated) {
    return <LoginPage onLoggedIn={() => void queryClient.invalidateQueries({ queryKey: ["session"] })} />;
  }

  const session = sessionQuery.data;
  const firstAllowedPage = getFirstAllowedPage(session);
  const pageContent = canAccessPage(session, currentPage) ? (
    renderPage(currentPage, session, () => void handleLogout(), setCurrentPage)
  ) : (
    <AccessDenied currentPage={currentPage} onGoHome={() => firstAllowedPage && setCurrentPage(firstAllowedPage)} />
  );

  return (
    <AppShell session={session} currentPage={currentPage} onNavigate={setCurrentPage} onLogout={() => void handleLogout()}>
      {pageContent}
    </AppShell>
  );
}

export default function App() {
  if (isRemoteClientBrowserLocation()) {
    return <RemoteClientApp />;
  }

  return <ConsoleApp />;
}
