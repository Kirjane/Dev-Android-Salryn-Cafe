import { useCallback, useRef, useState } from "react";
import { HostApiError } from "../../api/hostClient";

export type DurableIntentStatus = "prepared" | "unknown" | "rejected";

export type StoredDurableIntent<TPayload> = {
  scope: string;
  fingerprint: string;
  payload: TPayload;
  status: DurableIntentStatus;
  preparedAtUtc: string;
  lastErrorMessage?: string;
  lastErrorCode?: string;
  lastHttpStatus?: number;
};

const STORAGE_PREFIX = "salryn.cafe.r3.pending.";

export function durableIntentStorageKey(scope: string): string {
  return `${STORAGE_PREFIX}${scope}.v1`;
}

export function readStoredIntent<TPayload>(scope: string): StoredDurableIntent<TPayload> | null {
  try {
    const raw = window.localStorage.getItem(durableIntentStorageKey(scope));
    if (!raw) return null;
    const parsed = JSON.parse(raw) as StoredDurableIntent<TPayload>;
    if (
      !parsed ||
      typeof parsed !== "object" ||
      parsed.scope !== scope ||
      typeof parsed.fingerprint !== "string" ||
      !("payload" in parsed)
    ) {
      return null;
    }
    return parsed;
  } catch {
    return null;
  }
}

export function writeStoredIntent<TPayload>(intent: StoredDurableIntent<TPayload>): void {
  window.localStorage.setItem(durableIntentStorageKey(intent.scope), JSON.stringify(intent));
}

export function clearStoredIntent(scope: string): void {
  try {
    window.localStorage.removeItem(durableIntentStorageKey(scope));
  } catch {
    // Ignore storage removal errors.
  }
}

/**
 * Deterministic JSON fingerprint for semantic comparison so changed intents
 * are blocked while an unknown-outcome or rejected intent is fenced.
 */
export function canonicalFingerprint(value: unknown): string {
  if (value === null || typeof value !== "object") {
    return JSON.stringify(value);
  }
  if (Array.isArray(value)) {
    return "[" + value.map(canonicalFingerprint).join(",") + "]";
  }
  const entries = Object.entries(value as Record<string, unknown>).sort(([a], [b]) =>
    a < b ? -1 : a > b ? 1 : 0,
  );
  return "{" + entries.map(([k, v]) => JSON.stringify(k) + ":" + canonicalFingerprint(v)).join(",") + "}";
}

export class DurableIntentFenceError extends Error {
  readonly code = "R3_INTENT_FENCED";
  constructor(message: string) {
    super(message);
    this.name = "DurableIntentFenceError";
  }
}

export type DurableIntentDispatchResult<TResult> =
  | { ok: true; value: TResult }
  | { ok: false; outcome: "fenced" | "rejected" | "unknown"; message: string; code?: string; status?: number };

/**
 * Reusable durable pending-intent hook for RISK-3 financial/inventory mutations:
 * - Persists the prepared payload (including stable GUIDs and fixed timestamps) to localStorage BEFORE sending HTTP.
 * - Survives full page reloads so retries reuse the exact same operationId and payload.
 * - Blocks submitting a different semantic intent while an unknown-outcome or rejected intent is pending.
 * - Guards against synchronous double-clicks with a ref lock.
 */
export function useDurableIntent<TPayload, TResult>(scope: string) {
  const [pending, setPending] = useState<StoredDurableIntent<TPayload> | null>(() =>
    readStoredIntent<TPayload>(scope),
  );
  const [inFlight, setInFlight] = useState(false);
  const inFlightRef = useRef(false);

  const syncFromStorage = useCallback(() => {
    const current = readStoredIntent<TPayload>(scope);
    setPending(current);
    return current;
  }, [scope]);

  const clearPending = useCallback(
    (allowUnknownClear = false) => {
      const current = readStoredIntent<TPayload>(scope);
      if (current && (current.status === "unknown" || current.status === "prepared") && !allowUnknownClear) {
        throw new DurableIntentFenceError(
          "Cannot discard an unresolved request without explicit verification. Retry the exact prepared request first.",
        );
      }
      clearStoredIntent(scope);
      setPending(null);
    },
    [scope],
  );

  const execute = useCallback(
    async (
      semanticInput: unknown,
      preparePayload: () => TPayload,
      dispatch: (payload: TPayload) => Promise<TResult>,
    ): Promise<DurableIntentDispatchResult<TResult>> => {
      if (inFlightRef.current) {
        return { ok: false, outcome: "fenced", message: "Request is already in flight." };
      }

      const nextFingerprint = canonicalFingerprint(semanticInput);
      const existing = readStoredIntent<TPayload>(scope);

      if (existing) {
        if (existing.status === "rejected") {
          return {
            ok: false,
            outcome: "fenced",
            message:
              "A previous request was rejected by the Host (" +
              (existing.lastErrorCode ?? `HTTP ${existing.lastHttpStatus ?? "4xx"}`) +
              "). Clear the rejected request before submitting a new one.",
            code: existing.lastErrorCode,
            status: existing.lastHttpStatus,
          };
        }
        if (existing.fingerprint !== nextFingerprint) {
          return {
            ok: false,
            outcome: "fenced",
            message:
              "A previous request has an unresolved outcome. Retry the exact prepared request instead of sending changed values.",
          };
        }
      }

      const intent: StoredDurableIntent<TPayload> =
        existing ?? {
          scope,
          fingerprint: nextFingerprint,
          payload: preparePayload(),
          status: "prepared",
          preparedAtUtc: new Date().toISOString(),
        };

      // Persist BEFORE dispatching so reloads or crashes retain the exact IDs and timestamps.
      writeStoredIntent(intent);
      setPending(intent);

      inFlightRef.current = true;
      setInFlight(true);
      try {
        const value = await dispatch(intent.payload);
        clearStoredIntent(scope);
        setPending(null);
        return { ok: true, value };
      } catch (err) {
        if (err instanceof HostApiError && err.isDeterministicRejection) {
          const rejected: StoredDurableIntent<TPayload> = {
            ...intent,
            status: "rejected",
            lastErrorMessage: err.message,
            lastErrorCode: err.code,
            lastHttpStatus: err.status,
          };
          writeStoredIntent(rejected);
          setPending(rejected);
          return {
            ok: false,
            outcome: "rejected",
            message: err.message,
            code: err.code,
            status: err.status,
          };
        }
        const message =
          err instanceof Error
            ? err.message
            : "Network or server outcome is unknown. Retry the exact prepared request.";
        const code = err instanceof HostApiError ? err.code : undefined;
        const status = err instanceof HostApiError ? err.status : undefined;
        const unknown: StoredDurableIntent<TPayload> = {
          ...intent,
          status: "unknown",
          lastErrorMessage: message,
          lastErrorCode: code,
          lastHttpStatus: status,
        };
        writeStoredIntent(unknown);
        setPending(unknown);
        return { ok: false, outcome: "unknown", message, code, status };
      } finally {
        inFlightRef.current = false;
        setInFlight(false);
      }
    },
    [scope],
  );

  const retryPending = useCallback(
    async (dispatch: (payload: TPayload) => Promise<TResult>): Promise<DurableIntentDispatchResult<TResult>> => {
      const existing = readStoredIntent<TPayload>(scope);
      if (!existing) {
        return { ok: false, outcome: "fenced", message: "No pending request to retry." };
      }
      if (existing.status === "rejected") {
        return {
          ok: false,
          outcome: "fenced",
          message: "Rejected requests cannot be retried as-is. Clear the rejected request and prepare a new one.",
          code: existing.lastErrorCode,
          status: existing.lastHttpStatus,
        };
      }
      if (inFlightRef.current) {
        return { ok: false, outcome: "fenced", message: "Request is already in flight." };
      }
      inFlightRef.current = true;
      setInFlight(true);
      try {
        const value = await dispatch(existing.payload);
        clearStoredIntent(scope);
        setPending(null);
        return { ok: true, value };
      } catch (err) {
        if (err instanceof HostApiError && err.isDeterministicRejection) {
          const rejected: StoredDurableIntent<TPayload> = {
            ...existing,
            status: "rejected",
            lastErrorMessage: err.message,
            lastErrorCode: err.code,
            lastHttpStatus: err.status,
          };
          writeStoredIntent(rejected);
          setPending(rejected);
          return { ok: false, outcome: "rejected", message: err.message, code: err.code, status: err.status };
        }
        const message = err instanceof Error ? err.message : "Outcome still unknown.";
        const code = err instanceof HostApiError ? err.code : undefined;
        const status = err instanceof HostApiError ? err.status : undefined;
        const unknown: StoredDurableIntent<TPayload> = {
          ...existing,
          status: "unknown",
          lastErrorMessage: message,
          lastErrorCode: code,
          lastHttpStatus: status,
        };
        writeStoredIntent(unknown);
        setPending(unknown);
        return { ok: false, outcome: "unknown", message, code, status };
      } finally {
        inFlightRef.current = false;
        setInFlight(false);
      }
    },
    [scope],
  );

  return {
    pending,
    inFlight,
    execute,
    retryPending,
    clearPending,
    syncFromStorage,
  };
}
