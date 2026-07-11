import { useState, type ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  BarChart3,
  Boxes,
  ClipboardCheck,
  DatabaseBackup,
  FileText,
  History,
  Layers3,
  LayoutDashboard,
  LogOut,
  Network,
  ReceiptText,
  Settings,
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
import { getNavItems, type AppPage } from "../navigation";
import { hasPermission, Permissions } from "../security/permissions";
import { ConfirmDialog, StatusPill } from "./ui";

const navIcons: Record<string, LucideIcon> = {
  Dashboard: LayoutDashboard,
  "POS Checkout": ShoppingCart,
  Products: Tags,
  Inventory: Boxes,
  "Sales History": History,
  "Official Output Text File": FileText,
  Settings,
  Users,
  Backup: DatabaseBackup,
  Audit: ShieldCheck,
  "Host LAN Control": Network,
  "Official Setup": ClipboardCheck,
  "Traditional Reporting": ReceiptText,
  "Production Reports": BarChart3,
};

type AppShellProps = {
  session: SessionState;
  currentPage: AppPage;
  onNavigate: (page: AppPage) => void;
  onLogout: () => void;
  children: ReactNode;
};

export function AppShell({ session, currentPage, onNavigate, onLogout, children }: AppShellProps) {
  const [confirmLogout, setConfirmLogout] = useState(false);
  const visibleNavItems = getNavItems(session);
  const canViewPendingJoinRequests = hasPermission(session, Permissions.StoreTerminalView);
  const pendingJoinRequestsQuery = useQuery({
    queryKey: ["host-lan-control-pending-join-requests"],
    queryFn: fetchRemotePosPairingHostStatus,
    enabled: canViewPendingJoinRequests,
    retry: false,
    refetchInterval: 5000,
  });
  const pendingJoinRequestCount = pendingJoinRequestsQuery.data?.pendingRequestCount ?? 0;

  return (
    <div className="flex h-screen min-w-[48rem] flex-col bg-slate-100">
      <header className="flex h-[60px] shrink-0 items-center justify-between gap-4 border-b border-white/10 bg-navy-900 px-5 text-white shadow-sm">
        <div className="flex min-w-0 items-center gap-3">
          <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-md bg-white text-navy-900 shadow-sm">
            <Layers3 aria-hidden="true" size={22} strokeWidth={2.25} />
          </div>
          <div className="min-w-0 leading-tight">
            <span className="block truncate text-sm font-bold">SALRYN LEDGER CONSOLE</span>
            <span className="mt-0.5 flex items-center gap-1.5 text-xs text-slate-400">
              <span className="h-1.5 w-1.5 rounded-full bg-green-400" aria-hidden="true" />
              Local host mode
            </span>
          </div>
        </div>

        <div className="flex items-center gap-4">
          <div className="hidden items-center gap-2 border-r border-white/15 pr-4 text-xs text-slate-300 md:flex">
            <Store aria-hidden="true" size={17} />
            This device
          </div>
          <div className="flex items-center gap-2 text-right leading-tight">
            <span className="flex h-8 w-8 items-center justify-center rounded-full bg-slate-700 text-slate-200">
              <UserRound aria-hidden="true" size={17} />
            </span>
            <div>
              <p className="text-sm font-medium text-slate-100">{session.user?.displayName}</p>
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

      <div className="flex min-h-0 flex-1">
        <aside className="flex w-60 shrink-0 flex-col border-r border-white/5 bg-navy-800 text-slate-200 shadow-rail">
          <div className="border-b border-white/5 px-4 py-3 text-[11px] font-semibold text-slate-500">
            OPERATIONS
          </div>
          <nav className="flex-1 space-y-1 overflow-y-auto px-2 py-3">
            {visibleNavItems.map((item) => {
              const Icon = navIcons[item.label] ?? FileText;
              const isActive = item.enabled && item.id === currentPage;
              const showPendingJoinBadge =
                item.enabled && item.id === "host-lan-control" && pendingJoinRequestCount > 0;

              if (!item.enabled) {
                return (
                  <div
                    key={item.label}
                    aria-disabled="true"
                    title={item.reason}
                    className="flex min-h-10 cursor-not-allowed items-center justify-between gap-2 rounded-md px-3 py-2 text-sm text-slate-500"
                  >
                    <span className="flex min-w-0 items-center gap-3">
                      <Icon aria-hidden="true" className="shrink-0" size={17} />
                      <span className="truncate">{item.label}</span>
                    </span>
                    <span className="shrink-0 rounded border border-slate-600 px-1.5 py-0.5 text-[0.55rem] font-semibold text-slate-500">
                      Unsupported
                    </span>
                  </div>
                );
              }

              return (
                <button
                  key={item.label}
                  type="button"
                  onClick={() => onNavigate(item.id)}
                  className={`flex min-h-10 w-full items-center justify-between rounded-md px-3 py-2 text-left text-sm transition-colors ${
                    isActive
                      ? "bg-blue-600 font-semibold text-white shadow-sm"
                      : "text-slate-300 hover:bg-white/5 hover:text-white"
                  }`}
                >
                  <span className="flex items-center gap-3">
                    <Icon aria-hidden="true" size={17} />
                    {item.label}
                  </span>
                  {showPendingJoinBadge ? (
                    <span className="rounded bg-amber-300 px-2 py-0.5 text-[0.65rem] font-semibold text-amber-950">
                      {pendingJoinRequestCount}
                    </span>
                  ) : null}
                </button>
              );
            })}
          </nav>
          <div className="border-t border-white/5 px-4 py-3 text-[0.7rem] text-slate-400">
            <p className="font-semibold text-slate-300">Local API</p>
            <p className="mt-0.5 font-mono">127.0.0.1:5299</p>
          </div>
        </aside>

        <div className="flex min-w-0 flex-1 flex-col">
          <main className="flex-1 overflow-y-auto bg-[#f6f7f9] p-6">
            {pendingJoinRequestCount > 0 ? (
              <button
                type="button"
                onClick={() => onNavigate("host-lan-control")}
                className="mb-4 flex w-full flex-wrap items-center justify-between gap-3 rounded-md border border-amber-300 bg-amber-50 px-4 py-3 text-left text-sm text-amber-950 shadow-sm hover:bg-amber-100"
              >
                <span>
                  <span className="font-semibold">Device wants to join</span>
                  <span className="ml-2 text-amber-900">
                    Open Host LAN Control to accept or reject the pending request.
                  </span>
                </span>
                <StatusPill tone="warning">{pendingJoinRequestCount} pending</StatusPill>
              </button>
            ) : null}
            {children}
          </main>
          <footer className="flex min-h-10 items-center justify-between border-t border-slate-200 bg-white px-6 py-2 text-xs text-slate-500">
            <span className="flex items-center gap-2">
              <StatusPill tone="success">System Ready</StatusPill>
              Salryn host operator console
            </span>
            <span className="font-mono">Host-local API surfaces</span>
          </footer>
        </div>
      </div>

      <ConfirmDialog
        open={confirmLogout}
        title="Sign out of Salryn?"
        description="You will need to sign in again to continue using the console."
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
