import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { logout } from "./api/session";
import { AppShell } from "./components/AppShell";
import { Button } from "./components/ui";
import { useAppSession } from "./hooks/useAppSession";
import { type AppPage } from "./navigation";
import { AuditPage } from "./pages/AuditPage";
import { DashboardPage } from "./pages/DashboardPage";
import { InventoryPage } from "./pages/InventoryPage";
import { LoginPage } from "./pages/LoginPage";
import { ProductsPage } from "./pages/ProductsPage";
import { RemoteReadOnlyClientPage } from "./pages/RemoteReadOnlyClientPage";
import { SalesHistoryPage } from "./pages/SalesHistoryPage";
import { UsersPage } from "./pages/UsersPage";

function FullScreenCenter({ children }: { children: React.ReactNode }) {
  return <div className="flex min-h-screen items-center justify-center bg-navy-900 px-4">{children}</div>;
}

function ConsoleApp() {
  const queryClient = useQueryClient();
  const sessionQuery = useAppSession();
  const [currentPage, setCurrentPage] = useState<AppPage>("dashboard");

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

  return (
    <AppShell session={session} currentPage={currentPage} onNavigate={setCurrentPage} onLogout={() => void handleLogout()}>
      {currentPage === "dashboard" ? <DashboardPage session={session} onLogout={() => void handleLogout()} /> : null}
      {currentPage === "remote-readonly" ? <RemoteReadOnlyClientPage /> : null}
      {currentPage === "products" ? <ProductsPage /> : null}
      {currentPage === "inventory" ? <InventoryPage /> : null}
      {currentPage === "sales" ? <SalesHistoryPage /> : null}
      {currentPage === "users" ? <UsersPage /> : null}
      {currentPage === "audit" ? <AuditPage /> : null}
    </AppShell>
  );
}

export default function App() {
  return <ConsoleApp />;
}
