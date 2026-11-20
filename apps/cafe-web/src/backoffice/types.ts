export const UX_C_DATA_SOURCE = "MOCKED_UI" as const;

export type DeploymentSurface = "CENTRAL_OWNER" | "WINDOWS_HOST";

export type DeviceRole =
  | "LAN_THIN_TERMINAL"
  | "OFFLINE_ANDROID_CASHIER"
  | "OWNER_ADMIN_CLIENT"
  | "STANDBY_NODE";

export type DeviceStatus = "PENDING_APPROVAL" | "ACTIVE" | "OFFLINE_LEASE_EXPIRED" | "REVOKED";

export type EnrolledDevice = {
  deviceId: string;
  displayName: string;
  role: DeviceRole;
  productId: "Salryn.Cafe";
  keyThumbprint: string;
  status: DeviceStatus;
  syncCursor: number;
  pendingOutboxCount: number;
  offlineLeaseExpiresLabel: string;
  lastSeenLabel: string;
};

export type JoinCodeTicket = {
  dataSource: typeof UX_C_DATA_SOURCE;
  operationId: string;
  code: string;
  productId: "Salryn.Cafe";
  expiresInMinutes: number;
};

export type RecipeBomItem = {
  id: string;
  menuName: string;
  sellingPriceCents: number;
  bomSummary: string;
  unitCogsCents: number;
};

export type IngredientStock = {
  id: string;
  name: string;
  unit: string;
  onHand: number;
  reorderLevel: number;
  avgCostCents: number;
};

export type ArLedgerEntry = {
  id: string;
  customerName: string;
  balanceCents: number;
  agingBucket: "0-15d" | "16-30d" | "31+d";
  lastPaymentLabel: string;
};

export type CafeBooking = {
  id: string;
  clientName: string;
  eventDate: string;
  packageName: string;
  totalCents: number;
  depositPaidCents: number;
  status: "CONFIRMED" | "PENDING_DEPOSIT" | "COMPLETED";
};

export type AuditLogEntry = {
  id: string;
  timestamp: string;
  actor: string;
  action: string;
  operationId: string;
  surface: DeploymentSurface;
};

export function formatPhp(cents: number): string {
  const sign = cents < 0 ? "-" : "";
  return `${sign}₱${(Math.abs(cents) / 100).toLocaleString("en-PH", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })}`;
}
