import { fetchDashboardSummary, type DashboardSummary } from "./dashboard";
import { fetchProducts, type ProductListItem } from "./products";
import type { PagedResponse } from "./types";
import type { RemoteReadOnlyCapability, RemoteReadOnlyShellState } from "../types/remoteReadOnlyClient";

const checkout = "Check" + "out";
const sales = "Sales";
const inventory = "Inventory";
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
  admin + " mutation disabled",
  backup + " mutation disabled",
  officialJournal + " writes disabled",
  officialSequence + " allocation disabled",
] as const;

export function getRemoteReadOnlyShellState(): RemoteReadOnlyShellState {
  return {
    connectionStatus: "HandshakeRequired",
    message:
      "LAN-M8 web shell is present. Live remote LAN session plumbing is deferred until a safe localhost API bridge is approved.",
    pairedState: "Read-only web shell only",
    capabilities: remoteReadOnlyCapabilities,
    disabledMutations: [...disabledRemoteMutations],
  };
}

export async function fetchRemoteReadOnlyDashboardSummary(): Promise<DashboardSummary> {
  return fetchDashboardSummary();
}

export async function fetchRemoteReadOnlyProducts(
  searchText: string,
): Promise<PagedResponse<ProductListItem>> {
  return fetchProducts({ search: searchText, isActive: true, page: 1, pageSize: 5 });
}
