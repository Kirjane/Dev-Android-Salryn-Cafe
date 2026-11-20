import { useCallback, useRef, useState } from "react";

let operationCounter = 0;

function createOperationId(prefix: string): string {
  operationCounter += 1;
  const randomPart =
    typeof crypto !== "undefined" && typeof crypto.randomUUID === "function"
      ? crypto.randomUUID().slice(0, 8)
      : Math.random().toString(36).slice(2, 10);
  return `${prefix}-${operationCounter}-${randomPart}`;
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
 * Enforces durable client operationId reuse across retries and a synchronous ref-based in-flight guard.
 */
export function useDurableOperation(prefix: string) {
  const inFlightRef = useRef(false);
  const intentOperationIdRef = useRef<string | null>(null);
  const [state, setState] = useState<DurableOperationState>({
    pendingOperationId: null,
    inFlight: false,
    unknownOutcome: null,
    error: null,
  });

  const resetIntent = useCallback(() => {
    intentOperationIdRef.current = null;
    setState({
      pendingOperationId: null,
      inFlight: false,
      unknownOutcome: null,
      error: null,
    });
  }, []);

  const execute = useCallback(
    async <T,>(runner: (operationId: string) => Promise<T>): Promise<T | null> => {
      // Synchronous guard blocks rapid double-click / double-Enter before React re-renders.
      if (inFlightRef.current) {
        return null;
      }

      const operationId = intentOperationIdRef.current ?? createOperationId(prefix);
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
        // Committed cleanly -> clear intent so next distinct user action gets a new operationId.
        intentOperationIdRef.current = null;
        setState({
          pendingOperationId: null,
          inFlight: false,
          unknownOutcome: null,
          error: null,
        });
        return result;
      } catch (err) {
        if (err instanceof UnknownOutcomeError) {
          // Keep the EXACT same operationId for safe idempotent retry/reconciliation.
          setState({
            pendingOperationId: operationId,
            inFlight: false,
            unknownOutcome: { operationId, message: err.message },
            error: null,
          });
          return null;
        }

        // Validation/domain rejection keeps intent clean or allows correction while preserving ID if retried.
        const message = err instanceof Error ? err.message : "Operation failed.";
        setState({
          pendingOperationId: operationId,
          inFlight: false,
          unknownOutcome: null,
          error: message,
        });
        return null;
      } finally {
        inFlightRef.current = false;
      }
    },
    [prefix],
  );

  return {
    ...state,
    execute,
    resetIntent,
  };
}
