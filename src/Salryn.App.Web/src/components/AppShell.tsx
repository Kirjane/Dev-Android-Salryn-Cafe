import { useState, type ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  BarChart3,
  Boxes,
  FileText,
  History,
  Layers3,
  LayoutDashboard,
  LogOut,
  Network,
  PanelLeftClose,
  PanelLeftOpen,
  ShieldCheck,
  ShoppingCart,
  Store,
  Tags,
  UserRound,
  Users,
  type LucideIcon,
} from "lucide-react";
import { fetchRemotePosPairingHostStatus } from "../api/remoteReadOnlyClient";
import type { SessionState } from "../api/session";
import { fetchStoreBranding } from "../api/storeSettings";
import { getNavItems, type AppPage } from "../navigation";
import { hasPermission, Permissions } from "../security/permissions";
import { ConfirmDialog, StatusPill } from "./ui";

const navIcons: Record<string, LucideIcon> = {
  Dashboard: LayoutDashboard,
  "POS Checkout": ShoppingCart,
  "Product Catalog": Tags,
  "Stock Movements": Boxes,
  "Sales & Receipts": History,
  Reports: BarChart3,
  Users,
  Audit: ShieldCheck,
  "Store Settings": Store,
  License: ShieldCheck,
  "Backup & Restore": ShieldCheck,
  "Terminal Management": Network,
  "Report Artifacts": BarChart3,
};

type AppShellProps = {
  session: SessionState;
  currentPage: AppPage;
  onNavigate: (page: AppPage) => void;
  onLogout: () => void;
  children: ReactNode;
};

const sidebarStorageKey = "salryn.sidebar.collapsed";

function readInitialSidebarState(): boolean {
  if (typeof window === "undefined") return false;
  try {
    const saved = window.localStorage.getItem(sidebarStorageKey);
    if (saved !== null) return saved === "true";
  } catch {
    // Storage can be unavailable in hardened browser contexts; responsive fallback remains safe.
  }
  return window.matchMedia("(max-width: 1023px)").matches;
}

export function AppShell({ session, currentPage, onNavigate, onLogout, children }: AppShellProps) {
  const [confirmLogout, setConfirmLogout] = useState(false);
  const [sidebarCollapsed, setSidebarCollapsed] = useState(readInitialSidebarState);
  const visibleNavItems = getNavItems(session);
  const terminalManagementVisible = visibleNavItems.some((item) => item.id === "host-lan-control");
  const canViewPendingJoinRequests =
    terminalManagementVisible && hasPermission(session, Permissions.StoreTerminalView);
  const brandingQuery = useQuery({
    queryKey: ["app-shell-store-branding"],
    queryFn: fetchStoreBranding,
    retry: false,
  });
  const pendingJoinRequestsQuery = useQuery({
    queryKey: ["host-lan-control-pending-join-requests"],
    queryFn: fetchRemotePosPairingHostStatus,
    enabled: canViewPendingJoinRequests,
    retry: false,
    refetchInterval: 5000,
  });
  const pendingJoinRequestCount = pendingJoinRequestsQuery.data?.pendingRequestCount ?? 0;
  const storeName = brandingQuery.data?.storeName?.trim();
  const logoUrl = brandingQuery.data?.hasLogo ? brandingQuery.data.logoUrl : null;

  function toggleSidebar() {
    setSidebarCollapsed((current) => {
      const next = !current;
      try {
        window.localStorage.setItem(sidebarStorageKey, String(next));
      } catch {
        // Keep the in-memory control usable even when browser storage is unavailable.
      }
      return next;
    });
  }

  return (
    <div className="flex h-screen min-w-0 max-w-full flex-col overflow-hidden bg-slate-100">
      <header className="flex h-[60px] min-w-0 shrink-0 items-center justify-between gap-2 border-b border-white/10 bg-navy-900 px-3 text-white shadow-sm sm:gap-4 sm:px-5">
        <div className="flex min-w-0 items-center gap-3">
          <div className="flex h-9 w-9 shrink-0 items-center justify-center overflow-hidden rounded-md bg-white text-navy-900 shadow-sm">
            {logoUrl ? (
              <img src={logoUrl} alt="" className="h-full w-full object-contain" />
            ) : (
              <Layers3 aria-hidden="true" size={22} strokeWidth={2.25} />
            )}
          </div>
          <div className="min-w-0 leading-tight">
            <span className="block truncate text-sm font-bold">{storeName || "Salryn POS"}</span>
            <span className="mt-0.5 flex items-center gap-1.5 text-xs text-slate-400">
              <span className="h-1.5 w-1.5 rounded-full bg-green-400" aria-hidden="true" />
              System Ready
            </span>
          </div>
        </div>
        <div className="flex min-w-0 items-center gap-2 sm:gap-4">
          <div className="flex min-w-0 items-center gap-2 text-right leading-tight">
            <span className="flex h-8 w-8 items-center justify-center rounded-full bg-slate-700 text-slate-200">
              <UserRound aria-hidden="true" size={17} />
            </span>
            <div className="hidden min-w-0 sm:block">
              <p className="max-w-52 truncate text-sm font-medium text-slate-100">Signed in as {session.user?.displayName}</p>
              <p className="text-xs text-slate-400">{session.user?.roleName}</p>
            </div>
          </div>
          <button
            type="button"
            onClick={() => setConfirmLogout(true)}
            className="inline-flex min-h-9 items-center gap-2 rounded-md border border-white/20 px-3 py-1.5 text-xs font-semibold text-slate-100 hover:bg-white/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-white"
          >
            <LogOut aria-hidden="true" size={15} />
            <span className="hidden lg:inline">Sign out</span>
          </button>
        </div>
      </header>
      <div className="flex min-h-0 min-w-0 flex-1 overflow-hidden">
        <aside
          className={`flex shrink-0 flex-col border-r border-white/5 bg-navy-800 text-slate-200 shadow-rail transition-[width] duration-200 ${sidebarCollapsed ? "w-16" : "w-60"}`}
          aria-label="Primary navigation"
        >
          <div className={`flex shrink-0 border-b border-white/5 p-2 ${sidebarCollapsed ? "justify-center" : "justify-end"}`}>
            <button
              type="button"
              onClick={toggleSidebar}
              aria-expanded={!sidebarCollapsed}
              aria-label={sidebarCollapsed ? "Expand sidebar" : "Collapse sidebar"}
              title={sidebarCollapsed ? "Expand sidebar" : "Collapse sidebar"}
              className="flex h-9 w-9 items-center justify-center rounded-md text-slate-400 hover:bg-white/10 hover:text-white focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-400"
            >
              {sidebarCollapsed ? <PanelLeftOpen aria-hidden="true" size={18} /> : <PanelLeftClose aria-hidden="true" size={18} />}
            </button>
          </div>
          <nav className="flex-1 space-y-1 overflow-y-auto overflow-x-hidden px-2 py-2">
            {visibleNavItems.map((item) => {
              const Icon = navIcons[item.label] ?? FileText;
              const isActive = item.id === currentPage;
              const showPendingJoinBadge = item.id === "host-lan-control" && pendingJoinRequestCount > 0;
              const isFirstInGroup =
                visibleNavItems.findIndex((candidate) => candidate.group === item.group) ===
                visibleNavItems.indexOf(item);
              return (
                <div key={item.label}>
                  {isFirstInGroup ? (
                    sidebarCollapsed ? (
                      <div className="mx-2 my-2 border-t border-white/10" aria-hidden="true" />
                    ) : (
                      <div className="px-3 pb-1 pt-3 text-[10px] font-semibold uppercase text-slate-500">
                        {item.group}
                      </div>
                    )
                  ) : null}
                  <button
                    type="button"
                    onClick={() => onNavigate(item.id)}
                    aria-label={item.label}
                    title={sidebarCollapsed ? item.label : undefined}
                    className={`relative flex min-h-10 w-full items-center rounded-md py-2 text-left text-sm transition-colors ${sidebarCollapsed ? "justify-center px-2" : "justify-between px-3"} ${
                      isActive
                        ? "bg-blue-600 font-semibold text-white shadow-sm"
                        : "text-slate-300 hover:bg-white/5 hover:text-white"
                    }`}
                  >
                    <span className="flex min-w-0 items-center gap-3">
                      <Icon className="shrink-0" aria-hidden="true" size={17} />
                      {!sidebarCollapsed ? <span className="truncate">{item.label}</span> : null}
                    </span>
                    {showPendingJoinBadge ? (
                      <span className={sidebarCollapsed ? "absolute right-1 top-1 h-2 w-2 rounded-full bg-amber-300" : "rounded bg-amber-300 px-2 py-0.5 text-[0.65rem] font-semibold text-amber-950"}>
                        {!sidebarCollapsed ? pendingJoinRequestCount : null}
                      </span>
                    ) : null}
                  </button>
                </div>
              );
            })}
          </nav>
        </aside>
        <div className="flex min-w-0 flex-1 flex-col overflow-hidden">
          <main className="min-w-0 flex-1 overflow-x-hidden overflow-y-auto bg-[#f6f7f9] p-4 sm:p-6">
            {pendingJoinRequestCount > 0 ? (
              <button
                type="button"
                onClick={() => onNavigate("host-lan-control")}
                className="mb-4 flex w-full flex-wrap items-center justify-between gap-3 rounded-md border border-amber-300 bg-amber-50 px-4 py-3 text-left text-sm text-amber-950 shadow-sm hover:bg-amber-100"
              >
                <span>
                  <span className="font-semibold">Terminal request pending</span>
                  <span className="ml-2 text-amber-900">Open Terminal Management to review it.</span>
                </span>
                <StatusPill tone="warning">{pendingJoinRequestCount} pending</StatusPill>
              </button>
            ) : null}
            {children}
          </main>
          <footer className="flex min-h-10 min-w-0 items-center border-t border-slate-200 bg-white px-4 py-2 text-xs text-slate-500 sm:px-6">
            <span className="flex items-center gap-2">
              <StatusPill tone="success">System Ready</StatusPill>
              Salryn POS
            </span>
          </footer>
        </div>
      </div>
      <ConfirmDialog
        open={confirmLogout}
        title="Sign out of Salryn?"
        description="You will need to sign in again to continue."
        confirmLabel="Sign out"
        cancelLabel="Stay signed in"
        onCancel={() => setConfirmLogout(false)}
        onConfirm={() => {
          setConfirmLogout(false);
          onLogout();
        }}
      />
    </div>
  );
}
