import { useState, type ReactNode } from "react";
import {
  Layers3,
  LogOut,
  ShoppingCart,
  type LucideIcon,
} from "lucide-react";
import { ConfirmDialog, StatusPill } from "./ui";

export type TerminalShellNavId = "checkout";

type TerminalShellProps = {
  operatorName: string;
  roleName: string;
  terminalLabel?: string | null;
  hostLabel?: string | null;
  storeName?: string | null;
  logoUrl?: string | null;
  activeNav: TerminalShellNavId;
  onNavigate: (nav: TerminalShellNavId) => void;
  onSignOut: () => void;
  onExitToLanding?: () => void;
  children: ReactNode;
};

const terminalNav: Array<{ id: TerminalShellNavId; label: string; Icon: LucideIcon }> = [
  { id: "checkout", label: "POS Checkout", Icon: ShoppingCart },
];

/**
 * Salryn-style terminal shell for LAN Cashier/Manager after login.
 * Same visual language as Host AppShell; nav is terminal-safe only.
 * Does not expose Host admin pages.
 */
export function TerminalShell({
  operatorName,
  roleName,
  terminalLabel,
  hostLabel,
  storeName,
  logoUrl,
  activeNav,
  onNavigate,
  onSignOut,
  onExitToLanding,
  children,
}: TerminalShellProps) {
  const [confirmLogout, setConfirmLogout] = useState(false);

  return (
    <div className="flex h-screen min-w-0 flex-col bg-slate-100">
      <header className="flex h-[60px] shrink-0 items-center justify-between gap-4 border-b border-white/10 bg-navy-900 px-5 text-white shadow-sm">
        <div className="flex min-w-0 items-center gap-3">
          <div className="flex h-9 w-9 shrink-0 items-center justify-center overflow-hidden rounded-md bg-white text-navy-900 shadow-sm">
            {logoUrl ? (
              <img src={logoUrl} alt="" className="h-full w-full object-contain" />
            ) : (
              <Layers3 aria-hidden="true" size={22} strokeWidth={2.25} />
            )}
          </div>
          <div className="min-w-0 leading-tight">
            <span className="block truncate text-sm font-bold">{storeName?.trim() || "Salryn POS"}</span>
            <span className="mt-0.5 flex flex-wrap items-center gap-1.5 text-xs text-slate-400">
              <span className="h-1.5 w-1.5 rounded-full bg-green-400" aria-hidden="true" />
              Terminal Mode
              {terminalLabel ? <span className="text-slate-500">· {terminalLabel}</span> : null}
            </span>
          </div>
        </div>
        <div className="flex items-center gap-3">
          <StatusPill tone="primary">Terminal Mode</StatusPill>
          <StatusPill tone="info">{roleName || "Operator"}</StatusPill>
          <div className="hidden text-right leading-tight md:block">
            <p className="text-sm font-medium text-slate-100">{operatorName || "Signed in"}</p>
            <p className="text-xs text-slate-400">{hostLabel ? `Host ${hostLabel}` : "LAN terminal"}</p>
          </div>
          {onExitToLanding ? (
            <button
              type="button"
              onClick={onExitToLanding}
              className="hidden rounded-md border border-white/20 px-3 py-1.5 text-xs font-semibold text-slate-100 hover:bg-white/10 sm:inline-flex"
            >
              Client entry
            </button>
          ) : null}
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
          <nav className="flex-1 space-y-1 overflow-y-auto px-2 py-3">
            <div className="px-3 pb-1 pt-3 text-[10px] font-semibold uppercase text-slate-500">Terminal</div>
            {terminalNav.map((item) => {
              const isActive = item.id === activeNav;
              const Icon = item.Icon;
              return (
                <button
                  key={item.id}
                  type="button"
                  onClick={() => onNavigate(item.id)}
                  className={`flex min-h-10 w-full items-center gap-3 rounded-md px-3 py-2 text-left text-sm transition-colors ${
                    isActive
                      ? "bg-blue-600 font-semibold text-white shadow-sm"
                      : "text-slate-300 hover:bg-white/5 hover:text-white"
                  }`}
                >
                  <Icon aria-hidden="true" size={17} />
                  {item.label}
                </button>
              );
            })}
            <div className="mt-6 border-t border-white/10 px-3 pt-4 text-[11px] leading-relaxed text-slate-500">
              Host admin pages are not available on LAN terminals.
            </div>
          </nav>
        </aside>

        <div className="flex min-w-0 flex-1 flex-col">
          <main className="flex-1 overflow-y-auto bg-[#f6f7f9] p-6">{children}</main>
          <footer className="flex min-h-10 items-center border-t border-slate-200 bg-white px-6 py-2 text-xs text-slate-500">
            <span className="flex items-center gap-2">
              <StatusPill tone="success">Terminal Ready</StatusPill>
              Salryn POS · Cashier / Manager terminal shell
            </span>
          </footer>
        </div>
      </div>

      <ConfirmDialog
        open={confirmLogout}
        title="Sign out of this terminal?"
        description="You will need to sign in again. This terminal stays paired to the Host."
        confirmLabel="Sign out"
        cancelLabel="Stay signed in"
        onCancel={() => setConfirmLogout(false)}
        onConfirm={() => {
          setConfirmLogout(false);
          onSignOut();
        }}
      />
    </div>
  );
}

/** Lightweight Salryn frame for pairing/login before operator shell. */
export function TerminalEntryFrame({
  title,
  subtitle,
  onBack,
  children,
}: {
  title: string;
  subtitle?: string;
  onBack?: () => void;
  children: ReactNode;
}) {
  return (
    <div className="flex min-h-screen flex-col bg-slate-100">
      <header className="flex h-[60px] shrink-0 items-center justify-between gap-4 border-b border-white/10 bg-navy-900 px-5 text-white shadow-sm">
        <div className="flex min-w-0 items-center gap-3">
          <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-md bg-white text-navy-900 shadow-sm">
            <Layers3 aria-hidden="true" size={22} strokeWidth={2.25} />
          </div>
          <div className="min-w-0 leading-tight">
            <span className="block truncate text-sm font-bold">Salryn POS</span>
            <span className="mt-0.5 flex items-center gap-1.5 text-xs text-slate-400">
              <span className="h-1.5 w-1.5 rounded-full bg-amber-400" aria-hidden="true" />
              Terminal setup
            </span>
          </div>
        </div>
        <div className="flex items-center gap-2">
          <StatusPill tone="primary">Terminal Mode</StatusPill>
          {onBack ? (
            <button
              type="button"
              onClick={onBack}
              className="rounded-md border border-white/20 px-3 py-1.5 text-xs font-semibold text-slate-100 hover:bg-white/10"
            >
              Client entry
            </button>
          ) : null}
        </div>
      </header>
      <main className="mx-auto w-full max-w-5xl flex-1 space-y-4 p-6">
        <div>
          <h1 className="text-xl font-semibold text-slate-900">{title}</h1>
          {subtitle ? <p className="mt-1 text-sm text-slate-600">{subtitle}</p> : null}
        </div>
        {children}
      </main>
    </div>
  );
}
