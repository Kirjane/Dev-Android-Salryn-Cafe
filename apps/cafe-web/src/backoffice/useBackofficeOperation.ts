import { useCallback, useRef, useState } from "react";

let seq = 0;

function nextOpId(prefix: string): string {
  seq += 1;
  const suffix =
    typeof crypto !== "undefined" && typeof crypto.randomUUID === "function"
      ? crypto.randomUUID().slice(0, 8)
      : Math.random().toString(36).slice(2, 10);
  return `${prefix}-${seq}-${suffix}`;
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
  const inFlightRef = useRef(false);
  const intentIdRef = useRef<string | null>(null);
  const [inFlight, setInFlight] = useState(false);
  const [unknownOutcome, setUnknownOutcome] = useState<{ operationId: string; message: string } | null>(null);
  const [error, setError] = useState<string | null>(null);

  const reset = useCallback(() => {
    intentIdRef.current = null;
    setUnknownOutcome(null);
    setError(null);
  }, []);

  const execute = useCallback(
    async <T,>(runner: (operationId: string) => Promise<T>): Promise<T | null> => {
      if (inFlightRef.current) return null;

      const operationId = intentIdRef.current ?? nextOpId(prefix);
      intentIdRef.current = operationId;
      inFlightRef.current = true;
      setInFlight(true);
      setUnknownOutcome(null);
      setError(null);

      try {
        const res = await runner(operationId);
        intentIdRef.current = null;
        return res;
      } catch (err) {
        if (err instanceof BackofficeUnknownOutcomeError) {
          setUnknownOutcome({ operationId, message: err.message });
          return null;
        }
        setError(err instanceof Error ? err.message : "Operation failed.");
        return null;
      } finally {
        inFlightRef.current = false;
        setInFlight(false);
      }
    },
    [prefix],
  );

  return { inFlight, unknownOutcome, error, execute, reset };
}
