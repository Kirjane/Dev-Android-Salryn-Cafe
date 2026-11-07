import { useState, type ReactNode } from "react";
import {
  ChevronRight,
  Layers3,
  LogOut,
  PanelLeftClose,
  PanelLeftOpen,
  UserRound,
  type LucideIcon,
} from "lucide-react";
import { ConfirmDialog, StatusPill } from "./ui";

export type RoleAwareShellNavItem = {
  id: string;
  label: string;
  group: string;
  domain?: string;
  Icon: LucideIcon;
  badge?: number;
};

type RoleAwareAppShellProps = {
  storageKey: string;
  storeName?: string | null;
  logoUrl?: string | null;
  modeLabel: string;
  userName: string;
  roleName: string;
  navItems: RoleAwareShellNavItem[];
  activeNav: string;
  activeDomainLabel?: string;
  activePageLabel?: string;
  onNavigate: (id: string) => void;
  onSignOut: () => void;
  signOutTitle: string;
  signOutDescription: string;
  footerLabel: string;
  headerAction?: ReactNode;
  banner?: ReactNode;
  children: ReactNode;
};

function readInitialSidebarState(storageKey: string): boolean {
  if (typeof window === "undefined") return false;
  try {
    const saved = window.localStorage.getItem(storageKey);
    if (saved !== null) return saved === "true";
  } catch {
    // Hardened browser contexts may reject storage; responsive fallback remains usable.
  }
  return window.matchMedia("(max-width: 1023px)").matches;
}

export function RoleAwareAppShell({
  storageKey,
  storeName,
  logoUrl,
  modeLabel,
  userName,
  roleName,
  navItems,
  activeNav,
  activeDomainLabel,
  activePageLabel,
  onNavigate,
  onSignOut,
  signOutTitle,
  signOutDescription,
  footerLabel,
  headerAction,
  banner,
  children,
}: RoleAwareAppShellProps) {
  const [confirmLogout, setConfirmLogout] = useState(false);
  const [sidebarCollapsed, setSidebarCollapsed] = useState(() => readInitialSidebarState(storageKey));

  const activeItem = navItems.find((item) => item.id === activeNav);
  const resolvedDomainLabel = activeDomainLabel ?? activeItem?.domain ?? activeItem?.group ?? "";
  const resolvedPageLabel = activePageLabel ?? activeItem?.label ?? "";

  function toggleSidebar() {
    setSidebarCollapsed((current) => {
      const next = !current;
      try {
        window.localStorage.setItem(storageKey, String(next));
      } catch {
        // Keep the in-memory control usable when storage is unavailable.
      }
      return next;
    });
  }

  function handleNavClick(id: string) {
    onNavigate(id);
    if (typeof window !== "undefined" && window.matchMedia("(max-width: 767px)").matches) {
      setSidebarCollapsed(true);
    }
  }

  return (
    <div className="flex h-screen min-w-0 max-w-full flex-col overflow-hidden bg-slate-100">
      <header className="flex h-[60px] min-w-0 shrink-0 items-center justify-between gap-2 border-b border-white/10 bg-navy-900 px-3 text-white shadow-sm sm:gap-4 sm:px-5">
        <div className="flex min-w-0 items-center gap-2.5 sm:gap-3">
          {sidebarCollapsed ? (
            <button
              type="button"
              onClick={toggleSidebar}
              aria-expanded={!sidebarCollapsed}
              aria-label="Expand sidebar"
              title="Expand sidebar"
              className="flex h-9 w-9 shrink-0 items-center justify-center rounded-md border border-white/15 text-slate-200 hover:bg-white/10 hover:text-white focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-400 md:hidden"
            >
              <PanelLeftOpen aria-hidden="true" size={18} />
            </button>
          ) : null}
          <div className="flex h-9 w-9 shrink-0 items-center justify-center overflow-hidden rounded-md bg-white text-navy-900 shadow-sm">
            {logoUrl ? <img src={logoUrl} alt="" className="h-full w-full object-contain" /> : <Layers3 aria-hidden="true" size={22} strokeWidth={2.25} />}
          </div>
          <div className="min-w-0 leading-tight">
            <div className="flex min-w-0 items-center gap-2">
              <span className="block truncate text-sm font-bold">{storeName?.trim() || "Salryn POS"}</span>
              {resolvedPageLabel ? (
                <span className="hidden items-center gap-1 rounded bg-white/10 px-2 py-0.5 text-[11px] font-medium text-slate-200 md:inline-flex">
                  {resolvedDomainLabel ? (
                    <>
                      <span className="text-slate-400">{resolvedDomainLabel}</span>
                      <ChevronRight aria-hidden="true" size={12} className="text-slate-400" />
                    </>
                  ) : null}
                  <span className="font-semibold text-white">{resolvedPageLabel}</span>
                </span>
              ) : null}
            </div>
            <span className="mt-0.5 flex items-center gap-1.5 text-xs text-slate-400">
              <span className="h-1.5 w-1.5 rounded-full bg-green-400" aria-hidden="true" />
              {modeLabel}
            </span>
          </div>
        </div>
        <div className="flex min-w-0 items-center gap-2 sm:gap-4">
          {headerAction}
          <div className="flex min-w-0 items-center gap-2 text-right leading-tight">
            <span className="flex h-8 w-8 items-center justify-center rounded-full bg-slate-700 text-slate-200">
              <UserRound aria-hidden="true" size={17} />
            </span>
            <div className="hidden min-w-0 sm:block">
              <p className="max-w-52 truncate text-sm font-medium text-slate-100">Signed in as {userName}</p>
              <p className="text-xs text-slate-400">{roleName}</p>
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

      <div className="relative flex min-h-0 min-w-0 flex-1 overflow-hidden">
        {!sidebarCollapsed ? (
          <div
            className="fixed inset-0 top-[60px] z-30 bg-slate-950/50 md:hidden"
            onClick={toggleSidebar}
            aria-hidden="true"
          />
        ) : null}
        <aside
          className={`flex shrink-0 flex-col border-r border-white/5 bg-navy-800 text-slate-200 shadow-rail transition-[width] duration-200 ${sidebarCollapsed ? "w-16" : "w-60"} ${sidebarCollapsed ? "max-md:hidden" : "max-md:fixed max-md:inset-y-[60px] max-md:left-0 max-md:z-40 max-md:shadow-2xl"}`}
          aria-label="Primary navigation"
        >
          <div className={`flex shrink-0 border-b border-white/5 p-2 ${sidebarCollapsed ? "justify-center" : "justify-between items-center"}`}>
            {!sidebarCollapsed ? (
              <span className="px-2 text-[10px] font-semibold uppercase tracking-wider text-slate-400">
                Workspace Menu
              </span>
            ) : null}
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
            {navItems.map((item, index) => {
              const firstInGroup = navItems.findIndex((candidate) => candidate.group === item.group) === index;
              const active = item.id === activeNav;
              return (
                <div key={item.id}>
                  {firstInGroup ? sidebarCollapsed
                    ? <div className="mx-2 my-2 border-t border-white/10" aria-hidden="true" />
                    : <div className="px-3 pb-1 pt-3 text-[10px] font-semibold uppercase tracking-wider text-slate-400">{item.group}</div>
                    : null}
                  <button
                    type="button"
                    onClick={() => handleNavClick(item.id)}
                    aria-label={item.label}
                    aria-current={active ? "page" : undefined}
                    title={sidebarCollapsed ? item.label : undefined}
                    className={`relative flex min-h-10 w-full items-center rounded-md py-2 text-left text-sm transition-colors ${sidebarCollapsed ? "justify-center px-2" : "justify-between px-3"} ${active ? "bg-blue-600 font-semibold text-white shadow-sm" : "text-slate-300 hover:bg-white/5 hover:text-white"}`}
                  >
                    <span className="flex min-w-0 items-center gap-3">
                      <item.Icon className="shrink-0" aria-hidden="true" size={17} />
                      {!sidebarCollapsed ? <span className="truncate">{item.label}</span> : null}
                    </span>
                    {item.badge ? (
                      <span className={sidebarCollapsed ? "absolute right-1 top-1 h-2 w-2 rounded-full bg-amber-300" : "rounded bg-amber-300 px-2 py-0.5 text-[0.65rem] font-semibold text-amber-950"}>
                        {!sidebarCollapsed ? item.badge : null}
                      </span>
                    ) : null}
                  </button>
                </div>
              );
            })}
          </nav>
        </aside>

        <div className="flex min-w-0 flex-1 flex-col overflow-hidden">
          <main className="min-w-0 flex-1 overflow-x-hidden overflow-y-auto bg-[#f6f7f9] p-3.5 sm:p-6">
            {banner}
            {children}
          </main>
          <footer className="flex min-h-10 min-w-0 items-center justify-between border-t border-slate-200 bg-white px-4 py-2 text-xs text-slate-500 sm:px-6">
            <span className="flex items-center gap-2">
              <StatusPill tone="success">{modeLabel}</StatusPill>
              <span>{footerLabel}</span>
            </span>
            {resolvedPageLabel ? (
              <span className="hidden truncate font-medium text-slate-600 sm:inline">
                {resolvedDomainLabel ? `${resolvedDomainLabel} · ` : ""}{resolvedPageLabel}
              </span>
            ) : null}
          </footer>
        </div>
      </div>

      <ConfirmDialog
        open={confirmLogout}
        title={signOutTitle}
        description={signOutDescription}
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
