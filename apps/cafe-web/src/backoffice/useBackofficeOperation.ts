import { useCallback, useRef, useState } from "react";

type PersistedPendingOp = {
  operationId: string;
  intentKey?: string;
  message?: string;
};

function createGuidOperationId(): string {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
    return crypto.randomUUID();
  }
  return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    const v = c === "x" ? r : (r & 0x3) | 0x8;
    return v.toString(16);
  });
}

function readPersistedOp(storageKey: string): PersistedPendingOp | null {
  try {
    const raw = window.localStorage.getItem(storageKey);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as PersistedPendingOp;
    return parsed?.operationId ? parsed : null;
  } catch {
    return null;
  }
}

function writePersistedOp(storageKey: string, value: PersistedPendingOp | null): void {
  try {
    if (!value) {
      window.localStorage.removeItem(storageKey);
    } else {
      window.localStorage.setItem(storageKey, JSON.stringify(value));
    }
  } catch {
    // Ignore storage unavailability cleanly.
  }
}

export class BackofficeUnknownOutcomeError extends Error {
  readonly operationId: string;

  constructor(operationId: string, message: string) {
    super(message);
    this.name = "BackofficeUnknownOutcomeError";
    this.operationId = operationId;
  }
}

export function useBackofficeOperation(prefix: string) {
  const storageKey = `salryn.cafe.backoffice.op.${prefix}.v1`;
  const initialRef = useRef<PersistedPendingOp | null | undefined>(undefined);
  if (initialRef.current === undefined) {
    initialRef.current = readPersistedOp(storageKey);
  }

  const inFlightRef = useRef(false);
  const intentIdRef = useRef<string | null>(initialRef.current?.operationId ?? null);
  const intentKeyRef = useRef<string | undefined>(initialRef.current?.intentKey);
  const [inFlight, setInFlight] = useState(false);
  const [unknownOutcome, setUnknownOutcome] = useState<{ operationId: string; message: string } | null>(
    initialRef.current?.operationId && initialRef.current?.message
      ? { operationId: initialRef.current.operationId, message: initialRef.current.message }
      : null,
  );
  const [error, setError] = useState<string | null>(null);

  const reset = useCallback(() => {
    intentIdRef.current = null;
    intentKeyRef.current = undefined;
    writePersistedOp(storageKey, null);
    setUnknownOutcome(null);
    setError(null);
  }, [storageKey]);

  const execute = useCallback(
    async <T,>(runner: (operationId: string) => Promise<T>, intentKey?: string): Promise<T | null> => {
      if (inFlightRef.current) return null;

      // A previous write may already have committed. Never silently replace its
      // durable operation identity when the operator edits the submitted intent.
      // Only an identical intent may retry the ambiguous operation.
      if (unknownOutcome && intentKey !== undefined && intentKeyRef.current !== intentKey) {
        setError("Resolve the previous uncertain operation before submitting changed details. Check the saved record first.");
        return null;
      }

      if (intentKey !== undefined && intentKeyRef.current !== intentKey) {
        intentIdRef.current = null;
        intentKeyRef.current = intentKey;
      }

      const operationId = intentIdRef.current ?? createGuidOperationId();
      intentIdRef.current = operationId;
      writePersistedOp(storageKey, { operationId, intentKey });
      inFlightRef.current = true;
      setInFlight(true);
      setUnknownOutcome(null);
      setError(null);

      try {
        const res = await runner(operationId);
        intentIdRef.current = null;
        intentKeyRef.current = undefined;
        writePersistedOp(storageKey, null);
        return res;
      } catch (err) {
        if (err instanceof BackofficeUnknownOutcomeError) {
          writePersistedOp(storageKey, { operationId, intentKey, message: err.message });
          setUnknownOutcome({ operationId, message: err.message });
          return null;
        }
        intentIdRef.current = null;
        intentKeyRef.current = undefined;
        writePersistedOp(storageKey, null);
        setError(err instanceof Error ? err.message : "Operation failed.");
        return null;
      } finally {
        inFlightRef.current = false;
        setInFlight(false);
      }
    },
    [storageKey, unknownOutcome],
  );

  return { inFlight, unknownOutcome, error, execute, reset };
}
