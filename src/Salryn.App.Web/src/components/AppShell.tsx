import type { ReactNode } from "react";
import type { SessionState } from "../api/session";
import { StatusPill } from "./ui";

type NavItem = {
  label: string;
  // WEB-A3: only Dashboard is active. Every other module is a visual placeholder
  // and intentionally has no route/page/API wiring yet.
  active?: boolean;
};

const navItems: NavItem[] = [
  { label: "Dashboard", active: true },
  { label: "POS Checkout" },
  { label: "Products" },
  { label: "Inventory" },
  { label: "Sales History" },
  { label: "Official Output Text File" },
  { label: "Settings" },
  { label: "Users" },
  { label: "Backup" },
  { label: "Audit" },
];

type AppShellProps = {
  session: SessionState;
  onLogout: () => void;
  children: ReactNode;
};

export function AppShell({ session, onLogout, children }: AppShellProps) {
  return (
    <div className="flex h-screen flex-col bg-slate-100">
      <header className="flex items-center justify-between gap-4 bg-navy-900 px-6 py-3 text-white shadow">
        <div className="flex items-center gap-3">
          <div className="flex h-9 w-9 items-center justify-center rounded-lg bg-navy-700 text-sm font-bold">
            SL
          </div>
          <div>
            <p className="text-[0.65rem] uppercase tracking-[0.25em] text-slate-300">Salryn</p>
            <h1 className="text-lg font-semibold leading-tight">Ledger Console</h1>
          </div>
        </div>
        <div className="flex items-center gap-4">
          <div className="text-right">
            <p className="text-sm font-medium text-slate-100">{session.user?.displayName}</p>
            <p className="text-xs text-slate-400">{session.user?.roleName}</p>
          </div>
          <button
            type="button"
            onClick={onLogout}
            className="rounded-lg border border-white/20 px-3 py-1.5 text-xs font-medium text-slate-100 hover:bg-white/10"
          >
            Sign out
          </button>
        </div>
      </header>

      <div className="flex min-h-0 flex-1">
        <aside className="flex w-60 flex-col bg-navy-800 text-slate-200 shadow-rail">
          <nav className="flex-1 space-y-1 overflow-y-auto px-3 py-4">
            {navItems.map((item) => (
              <div
                key={item.label}
                aria-disabled={!item.active}
                className={`flex items-center justify-between rounded-lg px-3 py-2 text-sm ${
                  item.active
                    ? "bg-navy-700 font-medium text-white"
                    : "cursor-not-allowed text-slate-400"
                }`}
              >
                <span>{item.label}</span>
                {!item.active ? (
                  <span className="text-[0.6rem] uppercase tracking-wide text-slate-500">
                    Later
                  </span>
                ) : null}
              </div>
            ))}
          </nav>
          <div className="border-t border-white/5 px-4 py-3 text-[0.7rem] text-slate-400">
            Local API · 127.0.0.1
          </div>
        </aside>

        <div className="flex min-w-0 flex-1 flex-col">
          <main className="flex-1 overflow-y-auto p-6">{children}</main>
          <footer className="flex items-center justify-between border-t border-slate-200 bg-white px-6 py-2 text-xs text-slate-500">
            <span className="flex items-center gap-2">
              <StatusPill tone="success">Connected</StatusPill>
              Salryn local web console
            </span>
            <span>WEB-A3 shell foundation</span>
          </footer>
        </div>
      </div>
    </div>
  );
}
