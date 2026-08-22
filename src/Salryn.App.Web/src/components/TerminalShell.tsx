import { ArrowLeft, Layers3, ShoppingCart } from "lucide-react";
import type { ReactNode } from "react";
import { RoleAwareAppShell, type RoleAwareShellNavItem } from "./RoleAwareAppShell";
import { StatusPill } from "./ui";

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

const terminalNav: RoleAwareShellNavItem[] = [
  { id: "checkout", label: "POS Checkout", group: "Terminal", Icon: ShoppingCart },
];

/**
 * Role-aware terminal configuration of the same application shell used by the Host.
 * Only terminal-safe navigation is supplied; Host routes remain unmounted and backend authorization
 * remains authoritative.
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
  const headerAction = onExitToLanding ? (
    <button
      type="button"
      onClick={onExitToLanding}
      className="hidden items-center gap-2 rounded-md border border-white/20 px-3 py-1.5 text-xs font-semibold text-slate-100 hover:bg-white/10 sm:inline-flex"
    >
      <ArrowLeft aria-hidden="true" size={14} />
      Client entry
    </button>
  ) : null;

  return (
    <RoleAwareAppShell
      storageKey="salryn.terminal.sidebar.collapsed"
      storeName={storeName}
      logoUrl={logoUrl}
      modeLabel="Terminal Ready"
      userName={operatorName || "Signed in"}
      roleName={`${roleName || "Operator"}${terminalLabel ? ` · ${terminalLabel}` : ""}`}
      navItems={terminalNav}
      activeNav={activeNav}
      onNavigate={(id) => onNavigate(id as TerminalShellNavId)}
      onSignOut={onSignOut}
      signOutTitle="Sign out of this terminal?"
      signOutDescription="You will need to sign in again. This terminal stays paired to the Host."
      footerLabel={hostLabel ? `Salryn POS · Host ${hostLabel}` : "Salryn POS · LAN terminal"}
      headerAction={headerAction}
    >
      {children}
    </RoleAwareAppShell>
  );
}

/** Lightweight Salryn frame for pairing/login before the authenticated role-aware shell. */
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
      <main className="mx-auto w-full max-w-5xl flex-1 space-y-4 p-4 sm:p-6">
        <div>
          <h1 className="text-xl font-semibold text-slate-900">{title}</h1>
          {subtitle ? <p className="mt-1 text-sm text-slate-600">{subtitle}</p> : null}
        </div>
        {children}
      </main>
    </div>
  );
}
