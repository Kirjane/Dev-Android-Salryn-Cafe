import { useState, type ReactNode } from "react";
import type { SessionState } from "../api/session";
import { navItems, type AppPage } from "../navigation";
import { ConfirmDialog, StatusPill } from "./ui";

type AppShellProps = {
  session: SessionState;
  currentPage: AppPage;
  onNavigate: (page: AppPage) => void;
  onLogout: () => void;
  children: ReactNode;
};

export function AppShell({
  session,
  currentPage,
  onNavigate,
  onLogout,
  children,
}: AppShellProps) {
  const [confirmLogout, setConfirmLogout] = useState(false);

  return (
    <div className="flex h-screen flex-col bg-slate-100">
      <header className="flex h-14 items-center justify-between gap-4 bg-navy-900 px-5 text-white shadow">
        <div className="flex items-center gap-3">
          <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-navy-700 text-xs font-bold">
            SL
          </div>
          <div className="leading-tight">
            <span className="text-sm font-semibold tracking-wide">SALRYN LEDGER CONSOLE</span>
            <span className="ml-2 text-xs text-slate-400">Local Mode</span>
          </div>
        </div>
        <div className="flex items-center gap-4">
          <div className="text-right leading-tight">
            <p className="text-sm font-medium text-slate-100">{session.user?.displayName}</p>
            <p className="text-xs text-slate-400">{session.user?.roleName}</p>
          </div>
          <button
            type="button"
            onClick={() => setConfirmLogout(true)}
            className="rounded-lg border border-white/20 px-3 py-1.5 text-xs font-medium text-slate-100 hover:bg-white/10"
          >
            Sign out
          </button>
        </div>
      </header>

      <div className="flex min-h-0 flex-1">
        <aside className="flex w-60 flex-col bg-navy-800 text-slate-200">
          <nav className="flex-1 space-y-0.5 overflow-y-auto px-2 py-3">
            {navItems.map((item) => {
              const isActive = item.enabled && item.id === currentPage;

              if (!item.enabled) {
                return (
                  <div
                    key={item.label}
                    aria-disabled
                    className="flex cursor-not-allowed items-center justify-between rounded-md border-l-2 border-transparent px-3 py-2 text-sm text-slate-400"
                  >
                    <span>{item.label}</span>
                    <span className="text-[0.6rem] uppercase tracking-wide text-slate-500">
                      Later
                    </span>
                  </div>
                );
              }

              return (
                <button
                  key={item.label}
                  type="button"
                  onClick={() => onNavigate(item.id)}
                  className={`flex w-full items-center justify-between rounded-md px-3 py-2 text-left text-sm ${
                    isActive
                      ? "border-l-2 border-blue-500 bg-navy-700 font-medium text-white"
                      : "border-l-2 border-transparent text-slate-300 hover:bg-navy-700/50"
                  }`}
                >
                  <span>{item.label}</span>
                </button>
              );
            })}
          </nav>
          <div className="border-t border-white/5 px-4 py-3 text-[0.7rem] text-slate-400">
            Local API · 127.0.0.1:5299
          </div>
        </aside>

        <div className="flex min-w-0 flex-1 flex-col">
          <main className="flex-1 overflow-y-auto p-6">{children}</main>
          <footer className="flex items-center justify-between border-t border-slate-200 bg-white px-6 py-2 text-xs text-slate-500">
            <span className="flex items-center gap-2">
              <StatusPill tone="success">System Ready</StatusPill>
              Salryn read-only operator console
            </span>
            <span>WEB-B0 read-only surfaces</span>
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
