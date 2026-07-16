import { useEffect, useState, type ReactNode } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Layers3 } from "lucide-react";
import { fetchBootstrapStatus, type BootstrapStatus } from "./api/bootstrap";
import { fetchLicenseStatus, type LocalLicenseStatus } from "./api/license";
import { logout, type SessionState } from "./api/session";
import { AppShell } from "./components/AppShell";
import { Button, Card, CardBody, CommandBar } from "./components/ui";
import { useAppSession } from "./hooks/useAppSession";
import { type AppPage } from "./navigation";
import { AuditPage } from "./pages/AuditPage";
import { BackupRestorePage } from "./pages/BackupRestorePage";
import { BootstrapSetupPage } from "./pages/BootstrapSetupPage";
import { CheckoutPage } from "./pages/CheckoutPage";
import { DashboardPage } from "./pages/DashboardPage";
import { HostLanControlPage } from "./pages/HostLanControlPage";
import { InventoryPage } from "./pages/InventoryPage";
import { LicensePage } from "./pages/LicensePage";
import { LoginPage } from "./pages/LoginPage";
import { OwnerReportsPage } from "./pages/OwnerReportsPage";
import { StoreSettingsPage } from "./pages/StoreSettingsPage";
import { ProductsPage } from "./pages/ProductsPage";
import { ProductionReportsPage } from "./pages/ProductionReportsPage";
import { RemoteClientLandingPage } from "./pages/RemoteClientLandingPage";
import { RemotePosClientPage } from "./pages/RemotePosClientPage";
import { RemoteReadOnlyClientPage } from "./pages/RemoteReadOnlyClientPage";
import { TerminalActivationPage } from "./pages/TerminalActivationPage";
import { SalesHistoryPage } from "./pages/SalesHistoryPage";
import { OfficialSetupPage } from "./pages/OfficialSetupPage";
import { TraditionalReportingPage } from "./pages/TraditionalReportingPage";
import { UsersPage } from "./pages/UsersPage";
import { canAccessPage, getFirstAllowedPage } from "./security/permissions";
import { isRemoteClientBrowserLocation } from "./utils/lanEntry";
import { readInstalledTerminalLicense } from "./utils/terminalLicenseStorage";

function FullScreenCenter({ children }: { children: ReactNode }) {
  return <div className="flex min-h-screen items-center justify-center bg-navy-900 px-4">{children}</div>;
}

function AccessDenied({ currentPage, onGoHome }: { currentPage: AppPage; onGoHome: () => void }) {
  return (
    <div className="space-y-6">
      <CommandBar title="Access denied" subtitle="Your current operator permissions do not allow this page." />
      <Card><CardBody className="space-y-4"><p className="text-sm text-slate-600">The page request was blocked before rendering: <span className="font-medium">{currentPage}</span>.</p><Button variant="secondary" onClick={onGoHome}>Go to allowed page</Button></CardBody></Card>
    </div>
  );
}

function renderPage(page: AppPage, session: SessionState, onLogout: () => void, onNavigate: (page: AppPage) => void) {
  switch (page) {
    case "dashboard":
      return <DashboardPage session={session} onLogout={onLogout} onOpenSales={() => onNavigate("sales")} onOpenInventory={() => onNavigate("inventory")} onOpenReports={() => onNavigate("owner-reports")} canOpenReports={canAccessPage(session, "owner-reports")} />;
    case "checkout":
      return <CheckoutPage />;
    case "host-lan-control":
      return <HostLanControlPage />;
    case "products":
      return <ProductsPage />;
    case "inventory":
      return <InventoryPage />;
    case "sales":
      return <SalesHistoryPage canVoid={session.user?.permissions.includes("Sales.Void") ?? false} />;
    case "owner-reports":
      return <OwnerReportsPage />;
    case "settings":
      return <StoreSettingsPage />;
    case "license":
      return <LicensePage />;
    case "backup-restore":
      return <BackupRestorePage />;
    case "users":
      return <UsersPage />;
    case "audit":
      return <AuditPage />;
    case "official-setup":
      return <OfficialSetupPage />;
    case "traditional-reporting":
      return <TraditionalReportingPage />;
    case "production-reports":
      return <ProductionReportsPage />;
    default: {
      const exhaustiveCheck: never = page;
      return exhaustiveCheck;
    }
  }
}

type RemoteClientMode = "activation" | "landing" | "remote-pos" | "remote-readonly";

function RemoteClientApp() {
  const [mode, setMode] = useState<RemoteClientMode>(() =>
    readInstalledTerminalLicense() ? "landing" : "activation",
  );
  if (mode === "activation") return <TerminalActivationPage onActivated={() => setMode("landing")} />;
  if (mode === "landing") {
    return (
      <RemoteClientLandingPage
        onOpenRemotePos={() => setMode("remote-pos")}
        onOpenReadOnly={() => setMode("remote-readonly")}
        onManageTerminalLicense={() => setMode("activation")}
      />
    );
  }
  // PRE-BETA-E: logged-in Remote POS uses TerminalShell (Host visual language, terminal-safe nav).
  // Remote Read-Only remains support-only (not normal landing CTA).
  if (mode === "remote-readonly") {
    return (
      <div className="min-h-screen bg-[#f6f7f9]">
        <header className="flex h-[60px] items-center justify-between bg-navy-900 px-5 text-white shadow-sm">
          <div className="flex items-center gap-3">
            <span className="flex h-9 w-9 items-center justify-center rounded-md bg-white text-navy-900">
              <Layers3 aria-hidden="true" size={21} />
            </span>
            <div>
              <p className="text-sm font-bold">Salryn POS</p>
              <p className="text-xs text-slate-400">Support · Remote Read-Only</p>
            </div>
          </div>
          <Button variant="secondary" onClick={() => setMode("landing")}>
            <ArrowLeft aria-hidden="true" size={16} />
            Client entry
          </Button>
        </header>
        <div className="mx-auto max-w-7xl space-y-4 p-6">
          <RemoteReadOnlyClientPage />
        </div>
      </div>
    );
  }
  return <RemotePosClientPage onExitToLanding={() => setMode("landing")} />;
}

function ConsoleApp() {
  const queryClient = useQueryClient();
  const sessionQuery = useAppSession();
  const licenseStatusQuery = useQuery({ queryKey: ["license-status"], queryFn: fetchLicenseStatus, retry: false });
  const [currentPage, setCurrentPage] = useState<AppPage>("dashboard");
  const [bootstrapStatus, setBootstrapStatus] = useState<BootstrapStatus | null>(null);
  const [bootstrapError, setBootstrapError] = useState<string | null>(null);
  const [isBootstrapLoading, setIsBootstrapLoading] = useState(true);

  async function refreshBootstrapStatus() {
    setIsBootstrapLoading(true); setBootstrapError(null);
    try { setBootstrapStatus(await fetchBootstrapStatus()); } catch (error) { setBootstrapError((error as Error).message || "Could not read first-run setup status."); } finally { setIsBootstrapLoading(false); }
  }

  useEffect(() => { void refreshBootstrapStatus(); }, []);
  useEffect(() => {
    if (bootstrapStatus?.firstRunRequired && window.location.pathname !== "/bootstrap") window.history.replaceState(null, "", "/bootstrap");
    if (bootstrapStatus && !bootstrapStatus.firstRunRequired && window.location.pathname === "/bootstrap") window.history.replaceState(null, "", "/");
  }, [bootstrapStatus]);
  useEffect(() => {
    const session = sessionQuery.data;
    if (!session?.isAuthenticated || canAccessPage(session, currentPage)) return;
    const firstAllowedPage = getFirstAllowedPage(session);
    if (firstAllowedPage && firstAllowedPage !== currentPage) setCurrentPage(firstAllowedPage);
  }, [currentPage, sessionQuery.data]);

  async function handleLogout() { try { await logout(); } catch {} queryClient.removeQueries(); void sessionQuery.refetch(); }

  if (isBootstrapLoading || sessionQuery.isLoading || licenseStatusQuery.isLoading) return <FullScreenCenter><p className="text-sm text-slate-300">Starting Salryn POS...</p></FullScreenCenter>;
  if (bootstrapError || sessionQuery.isError || licenseStatusQuery.isError) return <FullScreenCenter><div className="w-full max-w-md rounded-lg border border-slate-200 bg-white p-8 shadow-xl"><h1 className="text-xl font-semibold text-slate-900">Salryn POS unavailable</h1><p className="mt-2 text-sm text-slate-500">{bootstrapError || (licenseStatusQuery.error as Error | null)?.message || "Salryn could not start on this device."}</p><div className="mt-6"><Button onClick={() => { void refreshBootstrapStatus(); void licenseStatusQuery.refetch(); }}>Try again</Button></div></div></FullScreenCenter>;
  if (!isHostLicenseReady(licenseStatusQuery.data)) return <div className="min-h-screen bg-[#f6f7f9] p-6"><div className="mx-auto max-w-6xl"><LicensePage /></div></div>;
  if (bootstrapStatus?.firstRunRequired) return <BootstrapSetupPage status={bootstrapStatus} onCompleted={() => { void refreshBootstrapStatus(); void queryClient.invalidateQueries({ queryKey: ["session"] }); }} />;
  if (!sessionQuery.data?.isAuthenticated) return <LoginPage onLoggedIn={() => void queryClient.invalidateQueries({ queryKey: ["session"] })} />;

  const session = sessionQuery.data;
  const firstAllowedPage = getFirstAllowedPage(session);
  const pageContent = canAccessPage(session, currentPage) ? renderPage(currentPage, session, () => void handleLogout(), setCurrentPage) : <AccessDenied currentPage={currentPage} onGoHome={() => firstAllowedPage && setCurrentPage(firstAllowedPage)} />;
  return <AppShell session={session} currentPage={currentPage} onNavigate={setCurrentPage} onLogout={() => void handleLogout()}>{pageContent}</AppShell>;
}

function isHostLicenseReady(status: LocalLicenseStatus | undefined): boolean {
  if (!status?.machineMatched) return false;
  if (status.status === "Development" || status.status === 5) return true;
  const licensed = status.status === "Licensed" || status.status === 1;
  const host = status.licenseType === "Host" || status.licenseType === 0;
  return licensed && host;
}

export default function App() {
  if (isRemoteClientBrowserLocation()) return <RemoteClientApp />;
  return <ConsoleApp />;
}
