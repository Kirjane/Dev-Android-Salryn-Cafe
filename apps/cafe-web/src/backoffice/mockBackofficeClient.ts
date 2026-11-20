import {
  UX_C_DATA_SOURCE,
  type ArLedgerEntry,
  type AuditLogEntry,
  type CafeBooking,
  type EnrolledDevice,
  type IngredientStock,
  type JoinCodeTicket,
  type RecipeBomItem,
} from "./types";
import { BackofficeUnknownOutcomeError } from "./useBackofficeOperation";

export const INITIAL_RECIPES: RecipeBomItem[] = [
  {
    id: "rec-1",
    menuName: "Spanish Latte (12oz)",
    sellingPriceCents: 16500,
    bomSummary: "18g Espresso Beans · 150mL Full Cream · 35mL Condensed Milk",
    unitCogsCents: 4850,
  },
  {
    id: "rec-2",
    menuName: "Iced Americano (16oz)",
    sellingPriceCents: 13000,
    bomSummary: "18g Espresso Beans · Filtered Water · Cup & Lid",
    unitCogsCents: 2600,
  },
  {
    id: "rec-3",
    menuName: "Sourdough Tuna Melt",
    sellingPriceCents: 21000,
    bomSummary: "2 slices Sourdough · 90g Tuna Mix · 30g Cheddar",
    unitCogsCents: 7400,
  },
];

export const INITIAL_STOCK: IngredientStock[] = [
  { id: "ing-1", name: "House Espresso Blend", unit: "kg", onHand: 8.5, reorderLevel: 4.0, avgCostCents: 98000 },
  { id: "ing-2", name: "Oat Milk Barista 1L", unit: "carton", onHand: 11, reorderLevel: 12, avgCostCents: 18500 },
  { id: "ing-3", name: "Condensed Milk 390g", unit: "can", onHand: 24, reorderLevel: 10, avgCostCents: 6800 },
];

export const INITIAL_AR_LEDGER: ArLedgerEntry[] = [
  { id: "ar-1", customerName: "Kirjane Office Tab", balanceCents: 218000, agingBucket: "0-15d", lastPaymentLabel: "Nov 14" },
  { id: "ar-2", customerName: "Cebu Tech Co-Work", balanceCents: 95000, agingBucket: "16-30d", lastPaymentLabel: "Nov 02" },
];

export const INITIAL_BOOKINGS: CafeBooking[] = [
  {
    id: "bk-1",
    clientName: "Ayala Design Summit",
    eventDate: "2026-11-28",
    packageName: "Mobile Espresso Cart (150 cups)",
    totalCents: 2800000,
    depositPaidCents: 1400000,
    status: "CONFIRMED",
  },
  {
    id: "bk-2",
    clientName: "Ramos Wedding Reception",
    eventDate: "2026-12-05",
    packageName: "Coffee & Pastry Bar (100 pax)",
    totalCents: 2200000,
    depositPaidCents: 0,
    status: "PENDING_DEPOSIT",
  },
];

export const INITIAL_DEVICES: EnrolledDevice[] = [
  {
    deviceId: "dev-android-01",
    displayName: "Counter Tablet #1 (Android)",
    role: "OFFLINE_ANDROID_CASHIER",
    productId: "Salryn.Cafe",
    keyThumbprint: "SHA256:9f4a...c10e",
    status: "ACTIVE",
    syncCursor: 4821,
    pendingOutboxCount: 0,
    offlineLeaseExpiresLabel: "21h remaining",
    lastSeenLabel: "1m ago",
  },
  {
    deviceId: "dev-lan-02",
    displayName: "Barista Thin Terminal",
    role: "LAN_THIN_TERMINAL",
    productId: "Salryn.Cafe",
    keyThumbprint: "SHA256:3b8c...77d2",
    status: "ACTIVE",
    syncCursor: 4821,
    pendingOutboxCount: 0,
    offlineLeaseExpiresLabel: "Host-authoritative LAN",
    lastSeenLabel: "Just now",
  },
  {
    deviceId: "dev-android-03",
    displayName: "Coffee Cart Tablet #2",
    role: "OFFLINE_ANDROID_CASHIER",
    productId: "Salryn.Cafe",
    keyThumbprint: "SHA256:aa12...88f0",
    status: "PENDING_APPROVAL",
    syncCursor: 0,
    pendingOutboxCount: 0,
    offlineLeaseExpiresLabel: "Awaiting approval",
    lastSeenLabel: "Just requested",
  },
];

export const INITIAL_AUDIT: AuditLogEntry[] = [
  {
    id: "aud-1",
    timestamp: "2026-11-20 23:10",
    actor: "Owner (Central)",
    action: "Approved device dev-android-01 (Salryn.Cafe)",
    operationId: "dev-approve-101",
    surface: "CENTRAL_OWNER",
  },
  {
    id: "aud-2",
    timestamp: "2026-11-20 22:40",
    actor: "Host Admin",
    action: "Verified SQLite backup snapshot (C:\\ProgramData\\SalrynCafe)",
    operationId: "backup-verify-44",
    surface: "WINDOWS_HOST",
  },
];

export interface BackofficeClient {
  readonly dataSource: typeof UX_C_DATA_SOURCE;
  issueJoinCode(operationId: string): Promise<JoinCodeTicket>;
  approveDevice(input: { operationId: string; deviceId: string }): Promise<EnrolledDevice>;
  revokeDevice(input: { operationId: string; deviceId: string }): Promise<EnrolledDevice>;
  adjustStock(input: {
    operationId: string;
    ingredientId: string;
    kind: "PURCHASE" | "WASTE" | "COUNT";
    deltaOrCount: number;
  }): Promise<IngredientStock>;
  settleArCash(input: { operationId: string; accountId: string; amountCents: number }): Promise<ArLedgerEntry>;
}

export function createMockBackofficeClient(options: {
  simulateNextRevokeUnknownOutcome?: boolean;
  delayMs?: number;
} = {}): BackofficeClient & { getSubmittedOpIds: () => string[] } {
  let simulateRevokeUnknown = Boolean(options.simulateNextRevokeUnknownOutcome);
  const delayMs = options.delayMs ?? 0;
  const submittedOpIds: string[] = [];

  const devices = new Map<string, EnrolledDevice>(INITIAL_DEVICES.map((d) => [d.deviceId, { ...d }]));
  const stock = new Map<string, IngredientStock>(INITIAL_STOCK.map((s) => [s.id, { ...s }]));
  const ar = new Map<string, ArLedgerEntry>(INITIAL_AR_LEDGER.map((a) => [a.id, { ...a }]));
  const revokedByOpId = new Map<string, EnrolledDevice>();

  async function maybeDelay() {
    if (delayMs > 0) await new Promise((r) => setTimeout(r, delayMs));
  }

  return {
    dataSource: UX_C_DATA_SOURCE,
    getSubmittedOpIds: () => [...submittedOpIds],

    async issueJoinCode(operationId) {
      submittedOpIds.push(operationId);
      await maybeDelay();
      return {
        dataSource: UX_C_DATA_SOURCE,
        operationId,
        code: "CAFE-7492-X9",
        productId: "Salryn.Cafe",
        expiresInMinutes: 10,
      };
    },

    async approveDevice({ operationId, deviceId }) {
      submittedOpIds.push(operationId);
      await maybeDelay();
      const target = devices.get(deviceId);
      if (!target) throw new Error(`Device ${deviceId} not found.`);
      const updated: EnrolledDevice = {
        ...target,
        status: "ACTIVE",
        offlineLeaseExpiresLabel: "24h remaining",
        lastSeenLabel: "Approved just now",
      };
      devices.set(deviceId, updated);
      return updated;
    },

    async revokeDevice({ operationId, deviceId }) {
      submittedOpIds.push(operationId);
      await maybeDelay();

      const existing = revokedByOpId.get(operationId);
      if (existing) return existing;

      const target = devices.get(deviceId);
      if (!target) throw new Error(`Device ${deviceId} not found.`);

      const updated: EnrolledDevice = {
        ...target,
        status: "REVOKED",
        offlineLeaseExpiresLabel: "Revoked",
      };
      devices.set(deviceId, updated);
      revokedByOpId.set(operationId, updated);

      if (simulateRevokeUnknown) {
        simulateRevokeUnknown = false;
        throw new BackofficeUnknownOutcomeError(
          operationId,
          "Revocation response timed out. Retry with the same operation ID to confirm device revocation.",
        );
      }

      return updated;
    },

    async adjustStock({ operationId, ingredientId, kind, deltaOrCount }) {
      submittedOpIds.push(operationId);
      await maybeDelay();
      const target = stock.get(ingredientId);
      if (!target) throw new Error(`Ingredient ${ingredientId} not found.`);
      const nextOnHand =
        kind === "COUNT"
          ? Math.max(0, deltaOrCount)
          : kind === "WASTE"
            ? Math.max(0, Number((target.onHand - Math.abs(deltaOrCount)).toFixed(2)))
            : Number((target.onHand + Math.abs(deltaOrCount)).toFixed(2));
      const updated = { ...target, onHand: nextOnHand };
      stock.set(ingredientId, updated);
      return updated;
    },

    async settleArCash({ operationId, accountId, amountCents }) {
      submittedOpIds.push(operationId);
      await maybeDelay();
      if (amountCents <= 0) throw new Error("Cash settlement amount must be positive.");
      const target = ar.get(accountId);
      if (!target) throw new Error(`AR account ${accountId} not found.`);
      const updated = {
        ...target,
        balanceCents: Math.max(0, target.balanceCents - amountCents),
        lastPaymentLabel: "Just now (Cash)",
      };
      ar.set(accountId, updated);
      return updated;
    },
  };
}
