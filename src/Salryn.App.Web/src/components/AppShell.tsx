import { useQuery } from "@tanstack/react-query";
import type { ReactNode } from "react";
import {
  BarChart3,
  Boxes,
  FileText,
  History,
  LayoutDashboard,
  Network,
  ShieldCheck,
  ShoppingCart,
  Store,
  Tags,
  Users,
  type LucideIcon,
} from "lucide-react";
import { fetchRemotePosPairingHostStatus } from "../api/remoteReadOnlyClient";
import type { SessionState } from "../api/session";
import type { LocalLicenseStatus } from "../api/license";
import { fetchStoreBranding } from "../api/storeSettings";
import { getNavItems, getPageLocationMeta, type AppPage } from "../navigation";
import { hasPermission, Permissions } from "../security/permissions";
import { RoleAwareAppShell, type RoleAwareShellNavItem } from "./RoleAwareAppShell";
import { StatusPill } from "./ui";

const navIcons: Record<string, LucideIcon> = {
  Dashboard: LayoutDashboard,
  "POS Checkout": ShoppingCart,
  "Product Catalog": Tags,
  "Stock Movements": Boxes,
  "Sales & Receipts": History,
  "Utang Ledger": FileText,
  "Accounts Receivables": FileText,
  "Accounts Payable": FileText,
  "Price Canvass / Quote": FileText,
  "Customer Orders": FileText,
  "Procurement & Payables": FileText,
  Reports: BarChart3,
  Users,
  Audit: ShieldCheck,
  "Store Settings": Store,
  License: ShieldCheck,
  "Backup & Restore": ShieldCheck,
  "LAN Terminals · Future Feature": Network,
  "Terminal Management": Network,
  "Report Artifacts": BarChart3,
};

type AppShellProps = {
  session: SessionState;
  licenseStatus: LocalLicenseStatus;
  currentPage: AppPage;
  onNavigate: (page: AppPage) => void;
  onLogout: () => void;
  children: ReactNode;
};

export function AppShell({ session, licenseStatus, currentPage, onNavigate, onLogout, children }: AppShellProps) {
  const visibleNavItems = getNavItems(session, licenseStatus.enabledFeatures);
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
  const shellNav: RoleAwareShellNavItem[] = visibleNavItems.map((item) => ({
    id: item.id,
    label: item.label,
    group: item.group,
    domain: item.domain,
    Icon: navIcons[item.label] ?? FileText,
    badge: item.id === "host-lan-control" ? pendingJoinRequestCount : undefined,
  }));
  const locationMeta = getPageLocationMeta(currentPage);

  const pendingBanner = pendingJoinRequestCount > 0 ? (
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
  ) : null;

  return (
    <RoleAwareAppShell
      storageKey="salryn.sidebar.collapsed"
      storeName={brandingQuery.data?.storeName}
      logoUrl={brandingQuery.data?.hasLogo ? brandingQuery.data.logoUrl : null}
      modeLabel="System Ready"
      userName={session.user?.displayName ?? "Signed in"}
      roleName={session.user?.roleName ?? "Operator"}
      navItems={shellNav}
      activeNav={currentPage}
      activeDomainLabel={locationMeta.domain}
      activePageLabel={locationMeta.label}
      onNavigate={(id) => onNavigate(id as AppPage)}
      onSignOut={onLogout}
      signOutTitle="Sign out of Salryn?"
      signOutDescription="You will need to sign in again to continue."
      footerLabel="Salryn POS"
      banner={pendingBanner}
    >
      {children}
    </RoleAwareAppShell>
  );
}
