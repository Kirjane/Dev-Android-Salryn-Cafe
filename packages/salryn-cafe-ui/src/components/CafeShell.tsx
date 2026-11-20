import {
  LogOut,
  Menu,
  PanelLeftClose,
  PanelLeftOpen,
  X,
  type LucideIcon,
} from "lucide-react";
import { useState, type ReactNode } from "react";
import { BrandMark } from "./BrandMark";
import { ConfirmDialog } from "./Dialog";
import { StatusBadge, type StatusTone } from "./StatusBadge";

export type CafeShellNavItem = {
  id: string;
  label: string;
  group: string;
  Icon: LucideIcon;
  badge?: number;
};

export type CafeShellProps = {
  storeName?: string;
  stationLabel: string;
  connectionStatus?: { label: string; tone: StatusTone };
  operatorName: string;
  roleName: string;
  navItems: CafeShellNavItem[];
  activeNav: string;
  onNavigate: (id: string) => void;
  onSignOut: () => void;
  banner?: ReactNode;
  children: ReactNode;
};

export function CafeShell({
  storeName = "Salryn Cafe",
  stationLabel,
  connectionStatus = { label: "Local Ready", tone: "success" },
  operatorName,
  roleName,
  navItems,
  activeNav,
  onNavigate,
  onSignOut,
  banner,
  children,
}: CafeShellProps) {
  const [collapsed, setCollapsed] = useState(false);
  const [mobileOpen, setMobileOpen] = useState(false);
  const [confirmSignOut, setConfirmSignOut] = useState(false);

  return (
    <div className="salryn-shell">
      <a href="#salryn-main-content" className="salryn-skip-link">
        Skip to main content
      </a>

      <header className="salryn-shell-header">
        <div style={{ display: "flex", alignItems: "center", gap: 12, minWidth: 0 }}>
          <button
            type="button"
            className="salryn-btn salryn-btn--ghost salryn-focusable-dark salryn-mobile-menu-btn"
            style={{ color: "var(--salryn-cream)", padding: "0 8px" }}
            aria-label={mobileOpen ? "Close navigation menu" : "Open navigation menu"}
            aria-expanded={mobileOpen}
            onClick={() => setMobileOpen((v) => !v)}
          >
            {mobileOpen ? <X aria-hidden="true" size={20} /> : <Menu aria-hidden="true" size={20} />}
          </button>

          <BrandMark variant="logo" height={40} decorative />

          <div style={{ minWidth: 0 }}>
            <div style={{ fontWeight: 800, fontSize: "1rem", lineHeight: 1.2 }}>{storeName}</div>
            <div style={{ fontSize: "0.75rem", color: "var(--salryn-latte)" }}>{stationLabel}</div>
          </div>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
          <StatusBadge tone={connectionStatus.tone}>{connectionStatus.label}</StatusBadge>
          <div style={{ textAlign: "right", lineHeight: 1.2 }}>
            <div style={{ fontSize: "0.875rem", fontWeight: 700 }}>{operatorName}</div>
            <div style={{ fontSize: "0.75rem", color: "var(--salryn-latte)" }}>{roleName}</div>
          </div>
          <button
            type="button"
            onClick={() => setConfirmSignOut(true)}
            className="salryn-btn salryn-btn--ghost salryn-focusable-dark"
            style={{
              color: "var(--salryn-cream)",
              borderColor: "rgba(255, 244, 225, 0.28)",
              padding: "0 12px",
            }}
          >
            <LogOut aria-hidden="true" size={16} />
            <span>Sign out</span>
          </button>
        </div>
      </header>

      <div className="salryn-shell-body">
        <aside
          aria-label="Primary navigation"
          className={[
            "salryn-shell-sidebar",
            collapsed ? "salryn-shell-sidebar--collapsed" : "",
            mobileOpen ? "salryn-shell-sidebar--mobile-open" : "",
          ]
            .filter(Boolean)
            .join(" ")}
        >
          <div
            style={{
              display: "flex",
              justifyContent: collapsed ? "center" : "flex-end",
              padding: 8,
              borderBottom: "1px solid rgba(233, 213, 187, 0.14)",
            }}
          >
            <button
              type="button"
              onClick={() => setCollapsed((v) => !v)}
              aria-expanded={!collapsed}
              aria-label={collapsed ? "Expand sidebar" : "Collapse sidebar"}
              className="salryn-btn salryn-btn--ghost salryn-focusable-dark"
              style={{ color: "var(--salryn-latte)", padding: "0 8px" }}
            >
              {collapsed ? (
                <PanelLeftOpen aria-hidden="true" size={18} />
              ) : (
                <PanelLeftClose aria-hidden="true" size={18} />
              )}
            </button>
          </div>

          <nav style={{ flex: 1, overflowY: "auto", padding: 8, display: "flex", flexDirection: "column", gap: 4 }}>
            {navItems.map((item, idx) => {
              const firstInGroup = navItems.findIndex((c) => c.group === item.group) === idx;
              const isActive = item.id === activeNav;
              return (
                <div key={item.id}>
                  {firstInGroup && !collapsed ? (
                    <div
                      style={{
                        padding: "10px 12px 4px",
                        fontSize: "0.6875rem",
                        fontWeight: 700,
                        letterSpacing: "0.06em",
                        textTransform: "uppercase",
                        color: "var(--salryn-latte)",
                      }}
                    >
                      {item.group}
                    </div>
                  ) : null}
                  <button
                    type="button"
                    onClick={() => {
                      onNavigate(item.id);
                      setMobileOpen(false);
                    }}
                    aria-current={isActive ? "page" : undefined}
                    aria-label={collapsed ? item.label : undefined}
                    title={collapsed ? item.label : undefined}
                    className="salryn-shell-nav-btn salryn-focusable-dark"
                  >
                    <span style={{ display: "inline-flex", alignItems: "center", gap: 10, minWidth: 0 }}>
                      <item.Icon aria-hidden="true" size={18} />
                      {!collapsed ? <span>{item.label}</span> : null}
                    </span>
                    {!collapsed && item.badge ? (
                      <span
                        className="salryn-tabular"
                        style={{
                          background: "var(--salryn-cream)",
                          color: "var(--salryn-espresso)",
                          borderRadius: 9999,
                          padding: "1px 7px",
                          fontSize: "0.75rem",
                          fontWeight: 700,
                        }}
                      >
                        {item.badge}
                      </span>
                    ) : null}
                  </button>
                </div>
              );
            })}
          </nav>
        </aside>

        <main id="salryn-main-content" tabIndex={-1} className="salryn-shell-main">
          {banner ? <div style={{ marginBottom: 16 }}>{banner}</div> : null}
          {children}
        </main>
      </div>

      <footer className="salryn-shell-footer">
        <span>{storeName} · Kirjane Information Technology Services - Cafe</span>
        <span>Touch &amp; Keyboard Ready (WCAG AA)</span>
      </footer>

      <ConfirmDialog
        open={confirmSignOut}
        title="Sign out of Salryn Cafe?"
        description="Your current station session will close. Unsubmitted drafts should be saved first."
        confirmLabel="Sign out"
        cancelLabel="Stay signed in"
        onCancel={() => setConfirmSignOut(false)}
        onConfirm={() => {
          setConfirmSignOut(false);
          onSignOut();
        }}
      />
    </div>
  );
}
