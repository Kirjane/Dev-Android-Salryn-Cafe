import { readJson } from "./http";

export type BootstrapStatus = {
  databaseInitialized: boolean;
  databaseFileExists: boolean;
  firstRunRequired: boolean;
  hasUsers: boolean;
  hasStoreSettings: boolean;
  pendingMigrationCount: number;
  configuredMigrationCount: number;
};

export type FirstAdminBootstrapRequest = {
  storeName: string;
  businessName?: string | null;
  address?: string | null;
  contactNumber?: string | null;
  tin?: string | null;
  receiptWidthMm: 58 | 80;
  adminUsername: string;
  adminDisplayName: string;
  adminPassword: string;
};

export type FirstAdminBootstrapResponse = {
  adminUserId: string;
  storeSettingsId: string;
  createdAt: string;
  status: BootstrapStatus;
};

export async function fetchBootstrapStatus(): Promise<BootstrapStatus> {
  const response = await fetch("/api/bootstrap/status", {
    credentials: "include",
  });

  return readJson<BootstrapStatus>(response);
}

export async function createFirstAdmin(
  request: FirstAdminBootstrapRequest,
): Promise<FirstAdminBootstrapResponse> {
  const response = await fetch("/api/bootstrap/first-admin", {
    method: "POST",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(request),
  });

  return readJson<FirstAdminBootstrapResponse>(response);
}
