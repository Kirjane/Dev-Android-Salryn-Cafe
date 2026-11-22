import type {
  CafeOrderDetail,
  CafeOrderMutationResult,
  CafeOrderSummary,
  CafePrintJobResult,
  CafePrintStation,
  CentralEnrollRequest,
  CentralSyncResult,
  CompleteOrderPayload,
  FirstAdminBootstrapPayload,
  HostBootstrapStatus,
  HostLicenseBootstrapStatus,
  HostSession,
  HostShiftOperationResult,
  MenuPublication,
  OrderRevisionPayload,
  PendingHostIntent,
  RejectedIntentRecord,
  WorkstationApi,
} from "../cashier/types";

export const PENDING_INTENT_STORAGE_KEY = "salryn.cafe.host.pending.v1";
export const REJECTION_HISTORY_STORAGE_KEY = "salryn.cafe.host.rejection-history.v1";
export const PENDING_PRINT_OPS_STORAGE_KEY = "salryn.cafe.host.pending-print-ops.v1";

/**
 * Derives a deterministic RFC-4122 v5-formatted GUID from a durable client operationId
 * and step scope so multi-step calls (e.g. create job -> attempt, menu -> recipes publish)
 * retain stable, distinct operationIds across retries and reloads without collisions.
 */
export function deriveScopedOperationId(baseOperationId: string, scope: string): string {
  const input = `${baseOperationId.trim().toLowerCase()}|${scope}`;
  const seeds = [0x811c9dc5, 0x9e3779b9, 0x85ebca6b, 0xc2b2ae35];
  const bytes = new Uint8Array(16);
  for (let s = 0; s < 4; s++) {
    let h = seeds[s] >>> 0;
    for (let i = 0; i < input.length; i++) {
      h ^= input.charCodeAt(i);
      h = Math.imul(h, 0x01000193) >>> 0;
    }
    h ^= h >>> 16;
    h = Math.imul(h, 0x7feb352d) >>> 0;
    h ^= h >>> 15;
    bytes[s * 4] = (h >>> 24) & 0xff;
    bytes[s * 4 + 1] = (h >>> 16) & 0xff;
    bytes[s * 4 + 2] = (h >>> 8) & 0xff;
    bytes[s * 4 + 3] = h & 0xff;
  }
  bytes[6] = (bytes[6] & 0x0f) | 0x50;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join("");
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

export function getOrCreatePendingPrintOp(opKey: string): string {
  try {
    const raw = window.localStorage.getItem(PENDING_PRINT_OPS_STORAGE_KEY);
    const map = raw ? (JSON.parse(raw) as Record<string, string>) : {};
    if (map[opKey]) return map[opKey];
    const id = crypto.randomUUID();
    map[opKey] = id;
    window.localStorage.setItem(PENDING_PRINT_OPS_STORAGE_KEY, JSON.stringify(map));
    return id;
  } catch {
    return crypto.randomUUID();
  }
}

export function clearPendingPrintOp(opKey: string): void {
  try {
    const raw = window.localStorage.getItem(PENDING_PRINT_OPS_STORAGE_KEY);
    if (!raw) return;
    const map = JSON.parse(raw) as Record<string, string>;
    delete map[opKey];
    if (Object.keys(map).length === 0) {
      window.localStorage.removeItem(PENDING_PRINT_OPS_STORAGE_KEY);
    } else {
      window.localStorage.setItem(PENDING_PRINT_OPS_STORAGE_KEY, JSON.stringify(map));
    }
  } catch {
    // Ignore storage failures cleanly.
  }
}

const DETERMINISTIC_HTTP_STATUSES = new Set([400, 403, 404, 409, 422]);
const NON_ARCHIVABLE_CONFLICT_CODES = new Set([
  "ERR_IDEMPOTENCY_OUTCOME_UNKNOWN",
  "ERR_IDEMPOTENCY_IN_FLIGHT",
]);

export class HostApiError extends Error {
  readonly status: number;
  readonly code?: string;
  readonly isDeterministicRejection: boolean;

  constructor(message: string, status: number, code?: string) {
    super(message);
    this.name = "HostApiError";
    this.status = status;
    this.code = code;
    this.isDeterministicRejection =
      DETERMINISTIC_HTTP_STATUSES.has(status) &&
      (!code || !NON_ARCHIVABLE_CONFLICT_CODES.has(code));
  }
}

export function formatPhp(cents: number): string {
  return new Intl.NumberFormat("en-PH", {
    style: "currency",
    currency: "PHP",
  }).format(cents / 100);
}

export function parsePhpToCents(raw: string): number {
  const trimmed = raw.trim();
  if (!/^\d+(\.\d{1,2})?$/.test(trimmed)) {
    throw new Error("Enter a nonnegative PHP amount with at most two decimal places.");
  }
  const [whole, fraction = ""] = trimmed.split(".");
  const amount = Number(whole) * 100 + Number(fraction.padEnd(2, "0"));
  if (!Number.isSafeInteger(amount)) {
    throw new Error("Amount is too large.");
  }
  return amount;
}

export function loadPendingHostIntent(): PendingHostIntent | null {
  try {
    const raw = window.localStorage.getItem(PENDING_INTENT_STORAGE_KEY);
    return raw ? (JSON.parse(raw) as PendingHostIntent) : null;
  } catch {
    return null;
  }
}

export function savePendingHostIntent(intent: PendingHostIntent | null): void {
  try {
    if (intent === null) {
      window.localStorage.removeItem(PENDING_INTENT_STORAGE_KEY);
    } else {
      window.localStorage.setItem(PENDING_INTENT_STORAGE_KEY, JSON.stringify(intent));
    }
  } catch {
    // Ignore storage quota / private-mode failures without crashing operator UI.
  }
}

export function appendRejectedIntentHistory(record: RejectedIntentRecord): void {
  try {
    const existingRaw = window.localStorage.getItem(REJECTION_HISTORY_STORAGE_KEY);
    const existing = existingRaw ? (JSON.parse(existingRaw) as RejectedIntentRecord[]) : [];
    window.localStorage.setItem(REJECTION_HISTORY_STORAGE_KEY, JSON.stringify([...existing, record]));
  } catch {
    // Ignore storage failures safely.
  }
}

export async function hostRequest<T>(path: string, body?: unknown, method?: "GET" | "POST" | "PUT" | "PATCH"): Promise<T> {
  const effectiveMethod = method ?? (body !== undefined ? "POST" : "GET");
  let response: Response;
  try {
    response = await fetch(path, {
      method: effectiveMethod,
      credentials: "same-origin",
      headers: body !== undefined ? { "Content-Type": "application/json" } : undefined,
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });
  } catch (err) {
    throw new HostApiError(
      err instanceof Error ? err.message : "Network request failed; operation outcome is unknown.",
      0,
      "ERR_NETWORK_UNKNOWN",
    );
  }

  if (response.status === 204) {
    return undefined as T;
  }

  const text = await response.text();
  let parsed: Record<string, unknown> | unknown = {};
  if (text.length > 0) {
    try {
      parsed = JSON.parse(text) as Record<string, unknown>;
    } catch {
      throw new HostApiError(
        response.ok
          ? `Host returned invalid non-JSON response (HTTP ${response.status}).`
          : `Host returned HTTP ${response.status}`,
        response.status,
      );
    }
  }

  if (!response.ok) {
    const obj = (typeof parsed === "object" && parsed !== null ? parsed : {}) as Record<string, unknown>;
    const msg =
      (typeof obj.error === "string" && obj.error) ||
      (typeof obj.detail === "string" && obj.detail) ||
      (typeof obj.message === "string" && obj.message) ||
      `Host returned HTTP ${response.status}`;
    const code = typeof obj.code === "string" ? obj.code : typeof obj.errorCode === "string" ? obj.errorCode : undefined;
    throw new HostApiError(msg, response.status, code);
  }

  return parsed as T;
}

export function createLiveHostApi(): WorkstationApi {
  return {
    getSession: () => hostRequest<HostSession>("/api/session"),
    getBootstrapStatus: () => hostRequest<HostBootstrapStatus>("/api/bootstrap/status"),
    getLicenseStatus: () => hostRequest<HostLicenseBootstrapStatus>("/api/license/status"),
    installLicense: (licenseJson) =>
      hostRequest<{ installStatus?: string; message?: string; status?: HostLicenseBootstrapStatus }>(
        "/api/license/install",
        { licenseJson },
      ),
    createFirstAdmin: (payload: FirstAdminBootstrapPayload) =>
      hostRequest<{ adminUserId: string; status: HostBootstrapStatus }>("/api/bootstrap/first-admin", payload),
    login: (username, password) => hostRequest<HostSession>("/api/auth/login", { username, password }),
    logout: () => hostRequest<{ ok?: boolean }>("/api/auth/logout", {}).then(() => undefined),
    getPublishedMenu: () => hostRequest<MenuPublication | null>("/api/menu/publication/current"),
    getOrders: () => hostRequest<CafeOrderSummary[]>("/api/cafe/orders"),
    getOrderDetail: (orderId) => hostRequest<CafeOrderDetail>(`/api/cafe/orders/${orderId}`),
    getCustomers: async () => {
      const rows = await hostRequest<Array<{ customer: { id: string; fullName: string; isActive: boolean }; balanceCents?: number; outstandingBalanceCents?: number }>>(
        "/api/customers",
      );
      return rows
        .map((r) => ({
          id: r.customer.id,
          fullName: r.customer.fullName,
          isActive: r.customer.isActive,
          outstandingBalanceCents: r.balanceCents ?? r.outstandingBalanceCents ?? 0,
        }))
        .filter((c) => c.isActive);
    },
    getShift: () => hostRequest<HostShiftOperationResult>("/api/shift"),
    openShift: (openingCashCents) => hostRequest<HostShiftOperationResult>("/api/shift/open", { openingCashCents }),
    getPrintStations: () => hostRequest<CafePrintStation[]>("/api/cafe/print/stations"),
    printReceipt: async ({ operationId, stationId, saleId, copyKind }) => {
      const created = await hostRequest<CafePrintJobResult>("/api/cafe/print/jobs/receipt", {
        operationId,
        stationId,
        saleId,
        copyKind,
      });
      return hostRequest<CafePrintJobResult>(`/api/cafe/print/jobs/${created.job.jobId}/attempt`, {
        operationId: deriveScopedOperationId(operationId, "receipt-attempt"),
      });
    },
    printKitchenTicket: async ({ operationId, stationId, orderId, orderRevisionId, copyKind }) => {
      const created = await hostRequest<CafePrintJobResult>("/api/cafe/print/jobs/kitchen", {
        operationId,
        stationId,
        orderId,
        orderRevisionId,
        copyKind,
      });
      return hostRequest<CafePrintJobResult>(`/api/cafe/print/jobs/${created.job.jobId}/attempt`, {
        operationId: deriveScopedOperationId(operationId, "kitchen-attempt"),
      });
    },
    enrollCentral: (req: CentralEnrollRequest) => hostRequest<{ deviceId: string; status: string }>("/api/cafe/central/enroll", req),
    synchronizeCentral: (password: string) => hostRequest<CentralSyncResult>("/api/cafe/central/synchronize", { password }),
    saveOrderRevision: (payload: OrderRevisionPayload) =>
      hostRequest<CafeOrderMutationResult>("/api/cafe/orders/revision", payload),
    submitOrder: (orderId, payload) =>
      hostRequest<CafeOrderMutationResult>(`/api/cafe/orders/${orderId}/submit`, payload),
    setPreparationStatus: (orderId, payload) =>
      hostRequest<CafeOrderMutationResult>(`/api/cafe/orders/${orderId}/preparation`, payload),
    parkOrder: (orderId, payload) =>
      hostRequest<CafeOrderMutationResult>(`/api/cafe/orders/${orderId}/park`, payload),
    resumeOrder: (orderId, payload) =>
      hostRequest<CafeOrderMutationResult>(`/api/cafe/orders/${orderId}/resume`, payload),
    cancelOrder: (orderId, payload) =>
      hostRequest<CafeOrderMutationResult>(`/api/cafe/orders/${orderId}/cancel`, payload),
    completeOrder: (orderId, payload: CompleteOrderPayload) =>
      hostRequest<CafeOrderCompletionResponse>(`/api/cafe/orders/${orderId}/complete`, payload),
    closeShift: (payload) => hostRequest<HostShiftOperationResult>("/api/shift/close", payload),
    sendRawIntent: (path, body) => hostRequest<Record<string, unknown>>(path, body),
  };
}

export type CafeOrderCompletionResponse = {
  orderId: string;
  saleId: string;
  receiptNumber: string;
  idempotent?: boolean;
};
