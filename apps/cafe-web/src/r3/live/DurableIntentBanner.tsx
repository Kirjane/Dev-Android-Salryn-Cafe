import type { StoredDurableIntent } from "./durableIntent";

type Props<TPayload> = {
  label: string;
  operationId: string | null;
  pending: StoredDurableIntent<TPayload> | null;
  corruptReason: string | null;
  authorityMismatch: boolean;
  inFlight: boolean;
  onRetry: () => void;
  onClearRejected: () => void;
  onClearCorrupt: () => void;
};

export function DurableIntentBanner<TPayload>({
  label,
  operationId,
  pending,
  corruptReason,
  authorityMismatch,
  inFlight,
  onRetry,
  onClearRejected,
  onClearCorrupt,
}: Props<TPayload>) {
  if (corruptReason) {
    return (
      <div className="r3-alert r3-alert--danger" role="alert" style={{ marginBottom: 12 }}>
        <div className="r3-alert-body">
          <div className="r3-alert-title">{label}: pending-intent storage corrupted (fail-closed)</div>
          <div>{corruptReason} Verify Host state before clearing.</div>
        </div>
        <button
          type="button"
          className="r3-btn r3-btn--outline r3-btn--sm"
          onClick={onClearCorrupt}
          disabled={inFlight}
        >
          Clear Corrupt Storage
        </button>
      </div>
    );
  }

  if (!pending) return null;

  if (authorityMismatch) {
    return (
      <div className="r3-alert r3-alert--danger" role="alert" style={{ marginBottom: 12 }}>
        <div className="r3-alert-body">
          <div className="r3-alert-title">{label}: fenced for a different operator or Host</div>
          <div>
            Pending request {operationId ? <code>{operationId}</code> : null} belongs to operator{" "}
            <code>{pending.actorUserId.slice(0, 8)}…</code>. Sign back in as the original operator on the
            same Host to resolve it safely. Cross-actor retry is blocked.
          </div>
        </div>
      </div>
    );
  }

  const isRejected = pending.status === "rejected";
  return (
    <div
      className={`r3-alert ${isRejected ? "r3-alert--danger" : "r3-alert--warn"}`}
      role="alert"
      style={{ marginBottom: 12 }}
    >
      <div className="r3-alert-body">
        <div className="r3-alert-title">
          {isRejected
            ? `${label} rejected (${pending.lastErrorCode ?? "HTTP " + (pending.lastHttpStatus ?? "4xx")})`
            : pending.retryAuthBlocked
              ? `${label}: authorization blocked while outcome is still unresolved`
              : `${label}: pending request has an unresolved outcome`}
        </div>
        <div>
          {operationId ? (
            <>
              Request ID: <code>{operationId}</code> ·{" "}
            </>
          ) : null}
          Attempts: {pending.attemptCount}
          {pending.lastErrorMessage ? ` — ${pending.lastErrorMessage}` : ""}
        </div>
      </div>
      <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
        {!isRejected ? (
          <button
            type="button"
            className="r3-btn r3-btn--primary r3-btn--sm"
            onClick={onRetry}
            disabled={inFlight}
          >
            {inFlight ? "Retrying…" : `Retry Exact ${label}`}
          </button>
        ) : (
          <button
            type="button"
            className="r3-btn r3-btn--outline r3-btn--sm"
            onClick={onClearRejected}
            disabled={inFlight}
          >
            Clear Rejected {label}
          </button>
        )}
      </div>
    </div>
  );
}
