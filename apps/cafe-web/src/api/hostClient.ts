import type {
  CafeOrderDetail,
  CafeOrderMutationResult,
  CafeOrderSummary,
  CafePrintJobResult,
  CafePrintStation,
  CentralEnrollRequest,
  CentralSyncResult,
  CompleteOrderPayload,
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
      return hostRequest<CafePrintJobResult>(`/api/cafe/print/jobs/${created.job.jobId}/attempt`, {});
    },
    printKitchenTicket: async ({ operationId, stationId, orderId, orderRevisionId, copyKind }) => {
      const created = await hostRequest<CafePrintJobResult>("/api/cafe/print/jobs/kitchen", {
        operationId,
        stationId,
        orderId,
        orderRevisionId,
        copyKind,
      });
      return hostRequest<CafePrintJobResult>(`/api/cafe/print/jobs/${created.job.jobId}/attempt`, {});
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
