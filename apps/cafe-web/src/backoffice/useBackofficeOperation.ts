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

export class BackofficeUnknownOutcomeError extends Error {
  readonly operationId: string;

  constructor(operationId: string, message: string) {
    super(message);
    this.name = "BackofficeUnknownOutcomeError";
    this.operationId = operationId;
  }
}

export function useBackofficeOperation(_prefix: string) {
  const inFlightRef = useRef(false);
  const intentIdRef = useRef<string | null>(null);
  const intentKeyRef = useRef<string | undefined>(undefined);
  const [inFlight, setInFlight] = useState(false);
  const [unknownOutcome, setUnknownOutcome] = useState<{ operationId: string; message: string } | null>(null);
  const [error, setError] = useState<string | null>(null);

  const reset = useCallback(() => {
    intentIdRef.current = null;
    intentKeyRef.current = undefined;
    setUnknownOutcome(null);
    setError(null);
  }, []);

  const execute = useCallback(
    async <T,>(runner: (operationId: string) => Promise<T>, intentKey?: string): Promise<T | null> => {
      if (inFlightRef.current) return null;

      if (intentKey !== undefined && intentKeyRef.current !== intentKey) {
        intentIdRef.current = null;
        intentKeyRef.current = intentKey;
      }

      const operationId = intentIdRef.current ?? createGuidOperationId();
      intentIdRef.current = operationId;
      inFlightRef.current = true;
      setInFlight(true);
      setUnknownOutcome(null);
      setError(null);

      try {
        const res = await runner(operationId);
        intentIdRef.current = null;
        intentKeyRef.current = undefined;
        return res;
      } catch (err) {
        if (err instanceof BackofficeUnknownOutcomeError) {
          setUnknownOutcome({ operationId, message: err.message });
          return null;
        }
        intentIdRef.current = null;
        intentKeyRef.current = undefined;
        setError(err instanceof Error ? err.message : "Operation failed.");
        return null;
      } finally {
        inFlightRef.current = false;
        setInFlight(false);
      }
    },
    [],
  );

  return { inFlight, unknownOutcome, error, execute, reset };
}
