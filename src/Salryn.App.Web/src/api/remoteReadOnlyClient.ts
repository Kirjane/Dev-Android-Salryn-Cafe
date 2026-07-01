import { readJson } from "./http";
import type { DashboardSummary } from "./dashboard";
import type { ProductListItem } from "./products";
import type { PagedResponse } from "./types";
import type {
  RemoteReadOnlyAuditVisibility,
  RemoteReadOnlyBindPolicy,
  RemoteReadOnlyCapability,
  RemoteReadOnlyCapabilitiesResponse,
  RemoteReadOnlyShellState,
  RemoteReadOnlyTerminalStatus,
} from "../types/remoteReadOnlyClient";

const checkout = "Check" + "out";
const sales = "Sales";
const inventory = "Inventory";
const product = "Product";
const admin = "Admin";
const backup = "Backup";
const officialJournal = "Official journal";
const officialSequence = "Official sequence";

export const remoteReadOnlyWarnings = [
  "Remote read-only client only. Remote cashier/admin execution is not enabled.",
  "Clients must not open the SQLite database directly.",
  checkout + ", sales, inventory, official journal, and official sequence actions are disabled on remote clients.",
] as const;

export const remoteReadOnlyCapabilities: RemoteReadOnlyCapability[] = [
  "Status read",
  "Product read",
  "Dashboard read",
  "Terminal status read",
];

export const disabledRemoteMutations = [
  checkout + " disabled",
  sales + " mutation disabled",
  inventory + " mutation disabled",
  product + " mutation disabled",
  admin + " mutation disabled",
  backup + " mutation disabled",
  officialJournal + " writes disabled",
  officialSequence + " allocation disabled",
] as const;

const fallbackBindPolicy: RemoteReadOnlyBindPolicy = {
  defaultMode: "LocalhostOnly",
  effectiveMode: "LocalhostOnly",
  disabledByDefault: true,
  localhostOnly: true,
  explicitHostBindRequired: true,
  specificIpOnly: true,
  message: "Api.Local remains bound to 127.0.0.1 and does not enable LAN bind by default.",
};

export function getRemoteReadOnlyShellState(): RemoteReadOnlyShellState {
  return {
    connectionStatus: "LocalOnlyReadMode",
    mode: "LocalOnlyReadMode",
    message:
      "LAN-M9-M13 web shell is using authenticated localhost API reads. Live remote transport session plumbing is deferred until a safe backend composition path exists.",
    pairedState: "LocalOnlyReadMode",
    terminalRole: "Read-only client",
    bindPolicy: fallbackBindPolicy,
    allowedCapabilities: remoteReadOnlyCapabilities,
    capabilities: remoteReadOnlyCapabilities,
    disabledMutations: [...disabledRemoteMutations],
  };
}

export async function fetchRemoteReadOnlyStatus(): Promise<RemoteReadOnlyShellState> {
  const response = await fetch("/api/remote-readonly/status", { credentials: "include" });
  return readJson<RemoteReadOnlyShellState>(response);
}

export async function fetchRemoteReadOnlyCapabilities(): Promise<RemoteReadOnlyCapabilitiesResponse> {
  const response = await fetch("/api/remote-readonly/capabilities", { credentials: "include" });
  return readJson<RemoteReadOnlyCapabilitiesResponse>(response);
}

export async function fetchRemoteReadOnlyDashboardSummary(): Promise<DashboardSummary> {
  const response = await fetch("/api/remote-readonly/dashboard-summary", { credentials: "include" });
  return readJson<DashboardSummary>(response);
}

export async function fetchRemoteReadOnlyProducts(
  searchText: string,
  page: number,
  pageSize: number,
): Promise<PagedResponse<ProductListItem>> {
  const query = new URLSearchParams();
  query.set("searchText", searchText);
  query.set("page", String(page));
  query.set("pageSize", String(pageSize));

  const response = await fetch(`/api/remote-readonly/products?${query.toString()}`, {
    credentials: "include",
  });
  return readJson<PagedResponse<ProductListItem>>(response);
}

export async function fetchRemoteReadOnlyTerminalStatus(): Promise<RemoteReadOnlyTerminalStatus> {
  const response = await fetch("/api/remote-readonly/terminal-status", { credentials: "include" });
  return readJson<RemoteReadOnlyTerminalStatus>(response);
}

export async function fetchRemoteReadOnlyBindPolicy(): Promise<RemoteReadOnlyBindPolicy> {
  const response = await fetch("/api/remote-readonly/bind-policy", { credentials: "include" });
  return readJson<RemoteReadOnlyBindPolicy>(response);
}

export async function fetchRemoteReadOnlyAuditVisibility(
  page: number,
  pageSize: number,
): Promise<RemoteReadOnlyAuditVisibility> {
  const query = new URLSearchParams();
  query.set("page", String(page));
  query.set("pageSize", String(pageSize));

  const response = await fetch(`/api/remote-readonly/audit?${query.toString()}`, {
    credentials: "include",
  });
  return readJson<RemoteReadOnlyAuditVisibility>(response);
}
