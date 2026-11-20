import { useCallback, useRef, useState } from "react";

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

export class UnknownOutcomeError extends Error {
  readonly operationId: string;

  constructor(operationId: string, message: string) {
    super(message);
    this.name = "UnknownOutcomeError";
    this.operationId = operationId;
  }
}

export type DurableOperationState = {
  pendingOperationId: string | null;
  inFlight: boolean;
  unknownOutcome: { operationId: string; message: string } | null;
  error: string | null;
};

/**
 * Enforces RFC-4122 Guid operationId reuse ONLY on UnknownOutcomeError retries of the same intentKey,
 * clears operationId on deterministic rejections, and enforces a synchronous ref-based in-flight guard.
 */
export function useDurableOperation(_prefix: string) {
  const inFlightRef = useRef(false);
  const intentOperationIdRef = useRef<string | null>(null);
  const intentKeyRef = useRef<string | undefined>(undefined);
  const [state, setState] = useState<DurableOperationState>({
    pendingOperationId: null,
    inFlight: false,
    unknownOutcome: null,
    error: null,
  });

  const resetIntent = useCallback(() => {
    intentOperationIdRef.current = null;
    intentKeyRef.current = undefined;
    setState({
      pendingOperationId: null,
      inFlight: false,
      unknownOutcome: null,
      error: null,
    });
  }, []);

  const execute = useCallback(
    async <T,>(runner: (operationId: string) => Promise<T>, intentKey?: string): Promise<T | null> => {
      if (inFlightRef.current) {
        return null;
      }

      if (intentKey !== undefined && intentKeyRef.current !== intentKey) {
        intentOperationIdRef.current = null;
        intentKeyRef.current = intentKey;
      }

      const operationId = intentOperationIdRef.current ?? createGuidOperationId();
      intentOperationIdRef.current = operationId;
      inFlightRef.current = true;

      setState({
        pendingOperationId: operationId,
        inFlight: true,
        unknownOutcome: null,
        error: null,
      });

      try {
        const result = await runner(operationId);
        intentOperationIdRef.current = null;
        intentKeyRef.current = undefined;
        setState({
          pendingOperationId: null,
          inFlight: false,
          unknownOutcome: null,
          error: null,
        });
        return result;
      } catch (err) {
        if (err instanceof UnknownOutcomeError) {
          // Preserve exact Guid operationId for safe idempotent retry of the identical payload.
          setState({
            pendingOperationId: operationId,
            inFlight: false,
            unknownOutcome: { operationId, message: err.message },
            error: null,
          });
          return null;
        }

        // Deterministic rejection: clear operationId so corrected payload never collides with 409.
        intentOperationIdRef.current = null;
        intentKeyRef.current = undefined;
        const message = err instanceof Error ? err.message : "Operation failed.";
        setState({
          pendingOperationId: null,
          inFlight: false,
          unknownOutcome: null,
          error: message,
        });
        return null;
      } finally {
        inFlightRef.current = false;
      }
    },
    [],
  );

  return {
    ...state,
    execute,
    resetIntent,
  };
}
