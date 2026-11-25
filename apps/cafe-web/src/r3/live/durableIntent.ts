import { useCallback, useEffect, useRef, useState } from "react";
import { HostApiError, hostRequest } from "../../api/hostClient";

export type DurableIntentStatus = "prepared" | "unknown" | "rejected";

export type DurableIntentAuthority = {
  hostIdentityKey: string;
  actorUserId: string;
};

export type StoredDurableIntent<TPayload> = {
  version: 2;
  scope: string;
  hostIdentityKey: string;
  actorUserId: string;
  fingerprint: string;
  payload: TPayload;
  status: DurableIntentStatus;
  hadUncertainOutcome: boolean;
  attemptCount: number;
  preparedAtUtc: string;
  lastErrorMessage?: string;
  lastErrorCode?: string;
  lastHttpStatus?: number;
  retryAuthBlocked?: boolean;
};

export type StoredIntentReadResult<TPayload> =
  | { kind: "empty" }
  | { kind: "ok"; intent: StoredDurableIntent<TPayload> }
  | { kind: "corrupt"; reason: string };

const STORAGE_PREFIX = "salryn.cafe.r3.pending.";

export function durableIntentStorageKey(scope: string): string {
  return `${STORAGE_PREFIX}${scope}.v2`;
}

type HostSystemIdentityDto = {
  productId?: string;
  service?: string;
  dataRoot?: string | null;
  databasePath?: string | null;
};

export async function fetchHostIdentityKey(): Promise<string | null> {
  const origin = typeof window !== "undefined" && window.location ? window.location.origin : "local";
  try {
    const id = await hostRequest<HostSystemIdentityDto>("/api/system/identity", undefined, "GET");
    const product = id?.productId?.trim();
    const dbPart = id?.databasePath?.trim() || id?.dataRoot?.trim() || id?.service?.trim();
    if (!product || !dbPart) {
      return null;
    }
    return `${origin}|${product}|${dbPart}`;
  } catch {
    return null;
  }
}

export function readStoredIntentChecked<TPayload>(scope: string): StoredIntentReadResult<TPayload> {
  let raw: string | null = null;
  try {
    raw = window.localStorage.getItem(durableIntentStorageKey(scope));
  } catch {
    return { kind: "corrupt", reason: "Browser localStorage could not be read." };
  }
  if (raw === null) return { kind: "empty" };
  try {
    const parsed = JSON.parse(raw) as Partial<StoredDurableIntent<TPayload>>;
    const validStatus =
      parsed.status === "prepared" || parsed.status === "unknown" || parsed.status === "rejected";
    if (
      !parsed ||
      typeof parsed !== "object" ||
      parsed.version !== 2 ||
      parsed.scope !== scope ||
      typeof parsed.hostIdentityKey !== "string" ||
      !parsed.hostIdentityKey.trim() ||
      typeof parsed.actorUserId !== "string" ||
      !parsed.actorUserId.trim() ||
      typeof parsed.fingerprint !== "string" ||
      !validStatus ||
      typeof parsed.hadUncertainOutcome !== "boolean" ||
      typeof parsed.attemptCount !== "number" ||
      !("payload" in parsed)
    ) {
      return {
        kind: "corrupt",
        reason: `Stored durable intent for "${scope}" failed schema validation and is fenced fail-closed.`,
      };
    }
    return { kind: "ok", intent: parsed as StoredDurableIntent<TPayload> };
  } catch {
    return {
      kind: "corrupt",
      reason: `Stored durable intent for "${scope}" contains corrupt JSON and is fenced fail-closed.`,
    };
  }
}

export function readStoredIntent<TPayload>(scope: string): StoredDurableIntent<TPayload> | null {
  const res = readStoredIntentChecked<TPayload>(scope);
  return res.kind === "ok" ? res.intent : null;
}

export function writeStoredIntent<TPayload>(intent: StoredDurableIntent<TPayload>): void {
  window.localStorage.setItem(durableIntentStorageKey(intent.scope), JSON.stringify(intent));
}

export function clearStoredIntent(scope: string): void {
  window.localStorage.removeItem(durableIntentStorageKey(scope));
}

/**
 * Deterministic JSON fingerprint for semantic comparison so changed intents
 * are blocked while an unresolved or rejected intent is fenced.
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
  | {
      ok: false;
      outcome: "fenced" | "rejected" | "unknown" | "authority_mismatch" | "corrupt";
      message: string;
      code?: string;
      status?: number;
    };

function isAuthorityMatch(
  intent: StoredDurableIntent<unknown>,
  authority: DurableIntentAuthority,
): boolean {
  return (
    intent.hostIdentityKey === authority.hostIdentityKey &&
    intent.actorUserId.toLowerCase() === authority.actorUserId.toLowerCase()
  );
}

/**
 * Reusable durable pending-intent hook for RISK-3 financial/operational mutations:
 * - Binds every prepared payload to { hostIdentityKey, actorUserId }.
 * - Persists payload + stable GUIDs + fixed timestamps to localStorage BEFORE HTTP dispatch.
 * - Fails closed if localStorage contains corrupt JSON or invalid schema.
 * - Prevents cross-actor or cross-Host execution/discard of another operator's pending intent.
 * - Prevents post-commit 401/403 retry failures from relabeling an unresolved intent as discardable.
 * - Synchronizes across browser tabs via the window "storage" event.
 * - Guards against synchronous double-clicks with a ref lock.
 */
export function useDurableIntent<TPayload, TResult>(
  scope: string,
  authority: DurableIntentAuthority | null,
) {
  const [readState, setReadState] = useState<StoredIntentReadResult<TPayload>>(() =>
    readStoredIntentChecked<TPayload>(scope),
  );
  const [inFlight, setInFlight] = useState(false);
  const inFlightRef = useRef(false);

  const syncFromStorage = useCallback(() => {
    const next = readStoredIntentChecked<TPayload>(scope);
    setReadState(next);
    return next;
  }, [scope]);

  // Multi-tab synchronization so two tabs cannot race duplicate writes.
  useEffect(() => {
    const key = durableIntentStorageKey(scope);
    const onStorage = (ev: StorageEvent) => {
      if (ev.key === null || ev.key === key) {
        setReadState(readStoredIntentChecked<TPayload>(scope));
      }
    };
    window.addEventListener("storage", onStorage);
    return () => window.removeEventListener("storage", onStorage);
  }, [scope]);

  const pending = readState.kind === "ok" ? readState.intent : null;
  const corruptReason = readState.kind === "corrupt" ? readState.reason : null;
  const authorityMismatch =
    pending !== null && authority !== null && !isAuthorityMatch(pending, authority);

  const clearPending = useCallback(
    (allowUnsafeClear = false) => {
      const current = readStoredIntentChecked<TPayload>(scope);
      if (current.kind === "corrupt") {
        if (!allowUnsafeClear) {
          throw new DurableIntentFenceError(
            "Corrupt pending-intent storage requires explicit audit confirmation before clearing.",
          );
        }
        clearStoredIntent(scope);
        setReadState({ kind: "empty" });
        return;
      }
      if (current.kind === "ok") {
        if ((!authority || !isAuthorityMatch(current.intent, authority)) && !allowUnsafeClear) {
          throw new DurableIntentFenceError(
            "Verified matching operator and Host identity are required before discarding a pending request.",
          );
        }
        if (
          (current.intent.status === "unknown" ||
            current.intent.status === "prepared" ||
            current.intent.hadUncertainOutcome) &&
          !allowUnsafeClear
        ) {
          throw new DurableIntentFenceError(
            "Cannot discard an unresolved request without explicit verification. Retry the exact prepared request first.",
          );
        }
      }
      clearStoredIntent(scope);
      setReadState({ kind: "empty" });
    },
    [scope, authority],
  );

  const classifyAndPersistFailure = useCallback(
    (
      intent: StoredDurableIntent<TPayload>,
      err: unknown,
      isRetryOfUnresolved: boolean,
    ): DurableIntentDispatchResult<TResult> => {
      const isAuthFailure =
        err instanceof HostApiError && (err.status === 401 || err.status === 403);

      // CRITICAL P1 GUARD: A post-commit/retry 401 or 403 on an unresolved intent MUST NOT
      // relabel the intent as deterministically rejected or enable discard!
      if (isAuthFailure && (isRetryOfUnresolved || intent.hadUncertainOutcome)) {
        const fenced: StoredDurableIntent<TPayload> = {
          ...intent,
          status: "unknown",
          hadUncertainOutcome: true,
          retryAuthBlocked: true,
          lastErrorMessage:
            "Authorization/session failure during retry of an unresolved request. The original Host outcome is still unknown — restore original operator authority and retry instead of discarding.",
          lastErrorCode: err.code ?? `HTTP_${err.status}`,
          lastHttpStatus: err.status,
        };
        writeStoredIntent(fenced);
        setReadState({ kind: "ok", intent: fenced });
        return {
          ok: false,
          outcome: "unknown",
          message: fenced.lastErrorMessage!,
          code: fenced.lastErrorCode,
          status: err.status,
        };
      }

      if (err instanceof HostApiError && err.isDeterministicRejection && !isRetryOfUnresolved) {
        const rejected: StoredDurableIntent<TPayload> = {
          ...intent,
          status: "rejected",
          retryAuthBlocked: false,
          lastErrorMessage: err.message,
          lastErrorCode: err.code,
          lastHttpStatus: err.status,
        };
        writeStoredIntent(rejected);
        setReadState({ kind: "ok", intent: rejected });
        return {
          ok: false,
          outcome: "rejected",
          message: err.message,
          code: err.code,
          status: err.status,
        };
      }

      // Deterministic business 400/404/409/422 on retry (not 401/403 and not ERR_IDEMPOTENCY_OUTCOME_UNKNOWN)
      // means the Host definitively evaluated the same actor's request and rejected it without committing.
      if (err instanceof HostApiError && err.isDeterministicRejection && !isAuthFailure) {
        const rejected: StoredDurableIntent<TPayload> = {
          ...intent,
          status: "rejected",
          hadUncertainOutcome: false,
          retryAuthBlocked: false,
          lastErrorMessage: err.message,
          lastErrorCode: err.code,
          lastHttpStatus: err.status,
        };
        writeStoredIntent(rejected);
        setReadState({ kind: "ok", intent: rejected });
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
        hadUncertainOutcome: true,
        retryAuthBlocked: false,
        lastErrorMessage: message,
        lastErrorCode: code,
        lastHttpStatus: status,
      };
      writeStoredIntent(unknown);
      setReadState({ kind: "ok", intent: unknown });
      return { ok: false, outcome: "unknown", message, code, status };
    },
    [],
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
      if (!authority || !authority.actorUserId || !authority.hostIdentityKey) {
        return {
          ok: false,
          outcome: "authority_mismatch",
          message: "Verified Host and operator identity are required before dispatching writes.",
        };
      }

      const checked = readStoredIntentChecked<TPayload>(scope);
      if (checked.kind === "corrupt") {
        setReadState(checked);
        return { ok: false, outcome: "corrupt", message: checked.reason };
      }

      const nextFingerprint = canonicalFingerprint(semanticInput);
      const existing = checked.kind === "ok" ? checked.intent : null;

      if (existing) {
        if (!isAuthorityMatch(existing, authority)) {
          setReadState({ kind: "ok", intent: existing });
          return {
            ok: false,
            outcome: "authority_mismatch",
            message:
              "A pending request belongs to a different operator or Host instance. Sign back in as the original operator on the same Host before continuing.",
          };
        }
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

      const isRetry = existing !== null;
      const intent: StoredDurableIntent<TPayload> = existing
        ? {
            ...existing,
            attemptCount: existing.attemptCount + 1,
            hadUncertainOutcome: true,
          }
        : {
            version: 2,
            scope,
            hostIdentityKey: authority.hostIdentityKey,
            actorUserId: authority.actorUserId,
            fingerprint: nextFingerprint,
            payload: preparePayload(),
            status: "prepared",
            hadUncertainOutcome: false,
            attemptCount: 1,
            preparedAtUtc: new Date().toISOString(),
          };

      // Persist BEFORE dispatching so reloads or crashes retain exact IDs, timestamps, and authority binding.
      writeStoredIntent(intent);
      setReadState({ kind: "ok", intent });

      inFlightRef.current = true;
      setInFlight(true);
      try {
        const value = await dispatch(intent.payload);
        clearStoredIntent(scope);
        setReadState({ kind: "empty" });
        return { ok: true, value };
      } catch (err) {
        return classifyAndPersistFailure(intent, err, isRetry);
      } finally {
        inFlightRef.current = false;
        setInFlight(false);
      }
    },
    [scope, authority, classifyAndPersistFailure],
  );

  const retryPending = useCallback(
    async (
      dispatch: (payload: TPayload) => Promise<TResult>,
    ): Promise<DurableIntentDispatchResult<TResult>> => {
      if (!authority || !authority.actorUserId || !authority.hostIdentityKey) {
        return {
          ok: false,
          outcome: "authority_mismatch",
          message: "Verified Host and operator identity are required to retry.",
        };
      }
      const checked = readStoredIntentChecked<TPayload>(scope);
      if (checked.kind === "corrupt") {
        setReadState(checked);
        return { ok: false, outcome: "corrupt", message: checked.reason };
      }
      if (checked.kind === "empty") {
        return { ok: false, outcome: "fenced", message: "No pending request to retry." };
      }
      const existing = checked.intent;
      if (!isAuthorityMatch(existing, authority)) {
        setReadState({ kind: "ok", intent: existing });
        return {
          ok: false,
          outcome: "authority_mismatch",
          message:
            "Cannot retry a pending request created by a different operator or Host instance.",
        };
      }
      if (existing.status === "rejected") {
        return {
          ok: false,
          outcome: "fenced",
          message:
            "Rejected requests cannot be retried as-is. Clear the rejected request and prepare a new one.",
          code: existing.lastErrorCode,
          status: existing.lastHttpStatus,
        };
      }
      if (inFlightRef.current) {
        return { ok: false, outcome: "fenced", message: "Request is already in flight." };
      }

      const nextIntent: StoredDurableIntent<TPayload> = {
        ...existing,
        attemptCount: existing.attemptCount + 1,
        hadUncertainOutcome: true,
      };
      writeStoredIntent(nextIntent);
      setReadState({ kind: "ok", intent: nextIntent });

      inFlightRef.current = true;
      setInFlight(true);
      try {
        const value = await dispatch(nextIntent.payload);
        clearStoredIntent(scope);
        setReadState({ kind: "empty" });
        return { ok: true, value };
      } catch (err) {
        return classifyAndPersistFailure(nextIntent, err, true);
      } finally {
        inFlightRef.current = false;
        setInFlight(false);
      }
    },
    [scope, authority, classifyAndPersistFailure],
  );

  return {
    pending,
    corruptReason,
    authorityMismatch,
    inFlight,
    execute,
    retryPending,
    clearPending,
    syncFromStorage,
  };
}
