import { useCallback, useEffect, useRef, useState } from "react";
import { HostApiError, hostRequest } from "../../api/hostClient";

export type DurableIntentStatus = "prepared" | "unknown" | "rejected";

export type DurableIntentAuthority = {
  hostIdentityKey: string;
  actorUserId: string;
};

export type StoredDurableIntent<TPayload> = {
  version: 2;
  intentId?: string;
  ownerTabId?: string;
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
  retryConflictBlocked?: boolean;
};

export type StoredIntentReadResult<TPayload> =
  | { kind: "empty" }
  | { kind: "ok"; intent: StoredDurableIntent<TPayload> }
  | { kind: "corrupt"; reason: string; legacyV1Detected?: boolean };

export type DurableIntentHookOptions = {
  tabId?: string;
  leaseTtlMs?: number;
};

const STORAGE_PREFIX = "salryn.cafe.r3.pending.";
const LOCK_PREFIX = "salryn.cafe.r3.lock.";
const DEFAULT_LEASE_TTL_MS = 30_000;

const DEFAULT_TAB_INSTANCE_ID: string =
  typeof crypto !== "undefined" && typeof crypto.randomUUID === "function"
    ? crypto.randomUUID()
    : `tab-${Date.now()}-${Math.random().toString(16).slice(2)}`;

const NON_DISCARDABLE_CONFLICT_CODES = new Set([
  "ERR_IDEMPOTENCY_OUTCOME_UNKNOWN",
  "ERR_IDEMPOTENCY_IN_FLIGHT",
  "ERR_IDEMPOTENCY_CONFLICT",
  "Idempotency.Conflict",
  "Idempotency.PayloadMismatch",
]);

export function durableIntentStorageKey(scope: string): string {
  return `${STORAGE_PREFIX}${scope}.v2`;
}

export function legacyDurableIntentV1Key(scope: string): string {
  return `${STORAGE_PREFIX}${scope}.v1`;
}

export function durableIntentLockKey(scope: string): string {
  return `${LOCK_PREFIX}${scope}.v2`;
}

type StoredScopeLease = {
  ownerTabId: string;
  leaseToken: string;
  expiresAtMs: number;
};

function generateToken(): string {
  return typeof crypto !== "undefined" && typeof crypto.randomUUID === "function"
    ? crypto.randomUUID()
    : `tok-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

function readScopeLease(scope: string): { kind: "empty" } | { kind: "ok"; lease: StoredScopeLease } | { kind: "corrupt" } {
  let raw: string | null = null;
  try {
    raw = window.localStorage.getItem(durableIntentLockKey(scope));
  } catch {
    return { kind: "corrupt" };
  }
  if (raw === null) return { kind: "empty" };
  try {
    const parsed = JSON.parse(raw) as Partial<StoredScopeLease>;
    if (
      !parsed ||
      typeof parsed !== "object" ||
      typeof parsed.ownerTabId !== "string" ||
      !parsed.ownerTabId.trim() ||
      typeof parsed.leaseToken !== "string" ||
      !parsed.leaseToken.trim() ||
      typeof parsed.expiresAtMs !== "number" ||
      !Number.isFinite(parsed.expiresAtMs)
    ) {
      return { kind: "corrupt" };
    }
    return { kind: "ok", lease: parsed as StoredScopeLease };
  } catch {
    return { kind: "corrupt" };
  }
}

function tryAcquireStorageLease(
  scope: string,
  ownerTabId: string,
  leaseTtlMs: number,
): { acquired: true; leaseToken: string } | { acquired: false; reason: string; storageError?: boolean } {
  const existing = readScopeLease(scope);
  const now = Date.now();
  if (existing.kind === "corrupt") {
    return {
      acquired: false,
      reason: `Scope lock storage for "${scope}" could not be validated. Refresh and verify Host state.`,
      storageError: true,
    };
  }
  if (existing.kind === "ok" && existing.lease.expiresAtMs > now && existing.lease.ownerTabId !== ownerTabId) {
    return {
      acquired: false,
      reason: "Another browser tab currently holds the execution lease for this request scope. Wait for it to finish and refresh.",
    };
  }
  const leaseToken = generateToken();
  const nextLease: StoredScopeLease = {
    ownerTabId,
    leaseToken,
    expiresAtMs: now + leaseTtlMs,
  };
  try {
    window.localStorage.setItem(durableIntentLockKey(scope), JSON.stringify(nextLease));
  } catch {
    return {
      acquired: false,
      reason: "Browser storage failed while acquiring cross-tab execution lease. No request was sent.",
      storageError: true,
    };
  }
  // Compare-owner read-back verification against non-WebLocks concurrent writer.
  const verify = readScopeLease(scope);
  if (verify.kind !== "ok" || verify.lease.ownerTabId !== ownerTabId || verify.lease.leaseToken !== leaseToken) {
    return {
      acquired: false,
      reason: "Another browser tab acquired the execution lease concurrently. Refresh before retrying.",
    };
  }
  return { acquired: true, leaseToken };
}

function releaseStorageLeaseIfOwned(scope: string, ownerTabId: string, leaseToken: string): void {
  try {
    const current = readScopeLease(scope);
    if (current.kind === "ok" && current.lease.ownerTabId === ownerTabId && current.lease.leaseToken === leaseToken) {
      window.localStorage.removeItem(durableIntentLockKey(scope));
    }
  } catch {
    // Best-effort lease release; TTL bounds stale leases safely.
  }
}

async function withCrossTabExclusiveScope<T>(
  scope: string,
  ownerTabId: string,
  leaseTtlMs: number,
  work: () => Promise<T>,
  onBusy: (reason: string, storageError: boolean) => T,
): Promise<T> {
  const runWithStorageLease = async (): Promise<T> => {
    const lease = tryAcquireStorageLease(scope, ownerTabId, leaseTtlMs);
    if (!lease.acquired) {
      return onBusy(lease.reason, Boolean(lease.storageError));
    }
    try {
      return await work();
    } finally {
      releaseStorageLeaseIfOwned(scope, ownerTabId, lease.leaseToken);
    }
  };

  if (
    typeof navigator !== "undefined" &&
    navigator.locks &&
    typeof navigator.locks.request === "function"
  ) {
    const lockName = `salryn.cafe.r3.lock.${scope}`;
    return navigator.locks.request(
      lockName,
      { mode: "exclusive", ifAvailable: true },
      async (lock) => {
        if (!lock) {
          return onBusy(
            "Another browser tab is currently executing or modifying a request for this scope. Wait for it to settle and refresh.",
            false,
          );
        }
        return runWithStorageLease();
      },
    );
  }

  return runWithStorageLease();
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
  let legacyV1Raw: string | null = null;
  try {
    raw = window.localStorage.getItem(durableIntentStorageKey(scope));
    if (raw === null) {
      legacyV1Raw = window.localStorage.getItem(legacyDurableIntentV1Key(scope));
    }
  } catch {
    return { kind: "corrupt", reason: "Browser localStorage could not be read." };
  }
  if (raw === null) {
    if (legacyV1Raw !== null) {
      return {
        kind: "corrupt",
        legacyV1Detected: true,
        reason: `Legacy pre-upgrade (v1) pending request detected for "${scope}" without Host/operator authority binding. Verify Host ledger state before clearing this upgrade fence.`,
      };
    }
    return { kind: "empty" };
  }
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

function tryWriteStoredIntentOwned<TPayload>(
  intent: StoredDurableIntent<TPayload>,
): { ok: true } | { ok: false; reason: string } {
  try {
    window.localStorage.setItem(durableIntentStorageKey(intent.scope), JSON.stringify(intent));
  } catch {
    return {
      ok: false,
      reason: `Browser storage failed while persisting durable request for "${intent.scope}". No HTTP mutation was dispatched.`,
    };
  }
  const verified = readStoredIntentChecked<TPayload>(intent.scope);
  if (
    verified.kind !== "ok" ||
    (intent.intentId && verified.intent.intentId !== intent.intentId) ||
    (intent.ownerTabId && verified.intent.ownerTabId !== intent.ownerTabId)
  ) {
    return {
      ok: false,
      reason: `Durable request ownership check failed for "${intent.scope}" after storage write. Refresh before retrying.`,
    };
  }
  return { ok: true };
}

function clearStoredIntentIfOwned<TPayload>(scope: string, expectedIntentId?: string): void {
  try {
    if (expectedIntentId) {
      const current = readStoredIntentChecked<TPayload>(scope);
      if (current.kind === "ok" && current.intent.intentId && current.intent.intentId !== expectedIntentId) {
        return;
      }
    }
    window.localStorage.removeItem(durableIntentStorageKey(scope));
  } catch {
    // Storage failures on post-success clear leave the banner visible for safe operator inspection.
  }
}

export function clearStoredIntent(scope: string): void {
  try {
    window.localStorage.removeItem(durableIntentStorageKey(scope));
    window.localStorage.removeItem(legacyDurableIntentV1Key(scope));
    window.localStorage.removeItem(durableIntentLockKey(scope));
  } catch {
    // Ignore storage removal errors cleanly.
  }
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
 * - Binds every prepared payload to { hostIdentityKey, actorUserId } and unique intentId/ownerTabId.
 * - Enforces cross-tab single-writer exclusion via Web Locks API + localStorage compare-owner leases.
 * - Detects legacy pre-upgrade (.v1) pending intents and fences fail-closed until Host audit.
 * - Fails closed BEFORE HTTP dispatch if browser storage write or read-back verification fails.
 * - Prevents cross-actor or cross-Host execution/discard of another operator's pending intent.
 * - Keeps unresolved fence on ANY retry failure (401/403/400/404/409/422/5xx/ERR_IDEMPOTENCY_CONFLICT)
 *   after an uncertain outcome, never relabeling uncertain post-commit errors as safely discardable.
 * - Synchronizes across browser tabs via the window "storage" event.
 */
export function useDurableIntent<TPayload, TResult>(
  scope: string,
  authority: DurableIntentAuthority | null,
  options?: DurableIntentHookOptions,
) {
  const ownerTabId = options?.tabId ?? DEFAULT_TAB_INSTANCE_ID;
  const leaseTtlMs = options?.leaseTtlMs ?? DEFAULT_LEASE_TTL_MS;

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

  // Multi-tab synchronization across v2, legacy v1, and lock keys.
  useEffect(() => {
    const v2Key = durableIntentStorageKey(scope);
    const v1Key = legacyDurableIntentV1Key(scope);
    const onStorage = (ev: StorageEvent) => {
      if (ev.key === null || ev.key === v2Key || ev.key === v1Key) {
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
      if (!allowUnsafeClear) {
        const activeLease = readScopeLease(scope);
        if (
          activeLease.kind === "ok" &&
          activeLease.lease.expiresAtMs > Date.now() &&
          activeLease.lease.ownerTabId !== ownerTabId
        ) {
          throw new DurableIntentFenceError(
            "Cannot clear pending request while another browser tab holds an active execution lease.",
          );
        }
      }
      if (current.kind === "corrupt") {
        if (!allowUnsafeClear) {
          throw new DurableIntentFenceError(
            "Corrupt or legacy pre-upgrade pending-intent storage requires explicit audit confirmation before clearing.",
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
            current.intent.hadUncertainOutcome ||
            current.intent.retryAuthBlocked ||
            current.intent.retryConflictBlocked) &&
          !allowUnsafeClear
        ) {
          throw new DurableIntentFenceError(
            "Cannot discard an unresolved request without explicit Host verification. Retry the exact prepared request first.",
          );
        }
      }
      clearStoredIntent(scope);
      setReadState({ kind: "empty" });
    },
    [scope, authority, ownerTabId],
  );

  const classifyAndPersistFailure = useCallback(
    (
      intent: StoredDurableIntent<TPayload>,
      err: unknown,
      isRetryOfUnresolved: boolean,
    ): DurableIntentDispatchResult<TResult> => {
      const isAuthFailure =
        err instanceof HostApiError && (err.status === 401 || err.status === 403);
      const errorCode = err instanceof HostApiError ? err.code : undefined;
      const isNonDiscardableConflict =
        Boolean(errorCode && NON_DISCARDABLE_CONFLICT_CODES.has(errorCode));

      const persistIfOwned = (next: StoredDurableIntent<TPayload>) => {
        const current = readStoredIntentChecked<TPayload>(intent.scope);
        // If another tab already resolved/cleared this intent (e.g. after TTL expiry in a
        // non-Web-Locks environment) or replaced it with a different intentId, do NOT resurrect
        // or overwrite storage from this stale attempt.
        if (current.kind === "empty") {
          setReadState(current);
          return;
        }
        if (
          current.kind === "ok" &&
          intent.intentId &&
          current.intent.intentId &&
          current.intent.intentId !== intent.intentId
        ) {
          setReadState(current);
          return;
        }
        try {
          writeStoredIntent(next);
          setReadState({ kind: "ok", intent: next });
        } catch {
          setReadState({
            kind: "corrupt",
            reason: `Failed to persist failure state for "${intent.scope}". Verify Host state before retrying.`,
          });
        }
      };

      // CRITICAL P1 GUARD: After an uncertain outcome (network drop / timeout / reload),
      // ANY failure on retry (401/403 auth, 400/404/409/422 business/idempotency conflict, 5xx)
      // MUST preserve the unresolved fence because a subsequent 4xx does NOT prove the original
      // command failed to commit (e.g., Attempt 1 committed and changed state, Attempt 2 got 409).
      if (isRetryOfUnresolved || intent.hadUncertainOutcome) {
        const status = err instanceof HostApiError ? err.status : undefined;
        const isClientStatus = typeof status === "number" && status >= 400 && status < 500;
        const message = isAuthFailure
          ? "Authorization/session failure during retry of an unresolved request. The original Host outcome is still unknown — restore original operator authority and retry instead of discarding."
          : isClientStatus
            ? `Host returned HTTP ${status}${errorCode ? ` (${errorCode})` : ""} while retrying an unresolved request. Because an earlier attempt had an uncertain outcome, this response does NOT prove the original command failed to commit. Verify Host ledger state before clearing this fence.`
            : err instanceof Error
              ? err.message
              : "Network or server outcome is still unknown. Retry the exact prepared request.";

        const fenced: StoredDurableIntent<TPayload> = {
          ...intent,
          status: "unknown",
          hadUncertainOutcome: true,
          retryAuthBlocked: isAuthFailure,
          retryConflictBlocked: !isAuthFailure && isClientStatus,
          lastErrorMessage: message,
          lastErrorCode: errorCode ?? (status ? `HTTP_${status}` : undefined),
          lastHttpStatus: status,
        };
        persistIfOwned(fenced);
        return {
          ok: false,
          outcome: "unknown",
          message,
          code: fenced.lastErrorCode,
          status,
        };
      }

      // First synchronous attempt receiving a clean deterministic rejection (not idempotency conflict)
      // proves the Host evaluated and rejected the command before committing.
      if (
        err instanceof HostApiError &&
        err.isDeterministicRejection &&
        !isNonDiscardableConflict
      ) {
        const rejected: StoredDurableIntent<TPayload> = {
          ...intent,
          status: "rejected",
          hadUncertainOutcome: false,
          retryAuthBlocked: false,
          retryConflictBlocked: false,
          lastErrorMessage: err.message,
          lastErrorCode: err.code,
          lastHttpStatus: err.status,
        };
        persistIfOwned(rejected);
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
      const status = err instanceof HostApiError ? err.status : undefined;
      const unknown: StoredDurableIntent<TPayload> = {
        ...intent,
        status: "unknown",
        hadUncertainOutcome: true,
        retryAuthBlocked: false,
        retryConflictBlocked: isNonDiscardableConflict,
        lastErrorMessage: message,
        lastErrorCode: errorCode,
        lastHttpStatus: status,
      };
      persistIfOwned(unknown);
      return { ok: false, outcome: "unknown", message, code: errorCode, status };
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

      return withCrossTabExclusiveScope<DurableIntentDispatchResult<TResult>>(
        scope,
        ownerTabId,
        leaseTtlMs,
        async () => {
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
                intentId: existing.intentId ?? generateToken(),
                ownerTabId,
                attemptCount: existing.attemptCount + 1,
                hadUncertainOutcome: true,
              }
            : {
                version: 2,
                intentId: generateToken(),
                ownerTabId,
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

          // Persist and verify ownership BEFORE dispatching so storage errors or cross-tab races never send unbacked writes.
          const persisted = tryWriteStoredIntentOwned(intent);
          if (!persisted.ok) {
            setReadState({ kind: "corrupt", reason: persisted.reason });
            return { ok: false, outcome: "corrupt", message: persisted.reason };
          }
          setReadState({ kind: "ok", intent });

          inFlightRef.current = true;
          setInFlight(true);
          try {
            const value = await dispatch(intent.payload);
            clearStoredIntentIfOwned<TPayload>(scope, intent.intentId);
            setReadState(readStoredIntentChecked<TPayload>(scope));
            return { ok: true, value };
          } catch (err) {
            return classifyAndPersistFailure(intent, err, isRetry);
          } finally {
            inFlightRef.current = false;
            setInFlight(false);
          }
        },
        (reason, storageError) => ({
          ok: false,
          outcome: storageError ? "corrupt" : "fenced",
          message: reason,
        }),
      );
    },
    [scope, authority, ownerTabId, leaseTtlMs, classifyAndPersistFailure],
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
      if (inFlightRef.current) {
        return { ok: false, outcome: "fenced", message: "Request is already in flight." };
      }

      return withCrossTabExclusiveScope<DurableIntentDispatchResult<TResult>>(
        scope,
        ownerTabId,
        leaseTtlMs,
        async () => {
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

          const nextIntent: StoredDurableIntent<TPayload> = {
            ...existing,
            intentId: existing.intentId ?? generateToken(),
            ownerTabId,
            attemptCount: existing.attemptCount + 1,
            hadUncertainOutcome: true,
          };
          const persisted = tryWriteStoredIntentOwned(nextIntent);
          if (!persisted.ok) {
            setReadState({ kind: "corrupt", reason: persisted.reason });
            return { ok: false, outcome: "corrupt", message: persisted.reason };
          }
          setReadState({ kind: "ok", intent: nextIntent });

          inFlightRef.current = true;
          setInFlight(true);
          try {
            const value = await dispatch(nextIntent.payload);
            clearStoredIntentIfOwned<TPayload>(scope, nextIntent.intentId);
            setReadState(readStoredIntentChecked<TPayload>(scope));
            return { ok: true, value };
          } catch (err) {
            return classifyAndPersistFailure(nextIntent, err, true);
          } finally {
            inFlightRef.current = false;
            setInFlight(false);
          }
        },
        (reason, storageError) => ({
          ok: false,
          outcome: storageError ? "corrupt" : "fenced",
          message: reason,
        }),
      );
    },
    [scope, authority, ownerTabId, leaseTtlMs, classifyAndPersistFailure],
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
