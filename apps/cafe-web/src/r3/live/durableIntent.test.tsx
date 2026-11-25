import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, renderHook } from "@testing-library/react";
import { HostApiError } from "../../api/hostClient";
import {
  DurableIntentFenceError,
  durableIntentStorageKey,
  legacyDurableIntentV1Key,
  useDurableIntent,
  type DurableIntentAuthority,
} from "./durableIntent";

const HOST_A = "http://127.0.0.1:5399|Salryn.Cafe|C:\\store-a\\salryn.db";
const HOST_B = "http://127.0.0.1:5399|Salryn.Cafe|C:\\store-b\\salryn.db";
const USER_A = "11111111-1111-1111-1111-111111111111";
const USER_B = "22222222-2222-2222-2222-222222222222";

const AUTH_A: DurableIntentAuthority = { hostIdentityKey: HOST_A, actorUserId: USER_A };
const AUTH_B: DurableIntentAuthority = { hostIdentityKey: HOST_A, actorUserId: USER_B };
const AUTH_OTHER_HOST: DurableIntentAuthority = { hostIdentityKey: HOST_B, actorUserId: USER_A };

describe("useDurableIntent P1 authority, concurrency, v1 upgrade, conflict fence & storage failure hardening", () => {
  beforeEach(() => {
    window.localStorage.clear();
  });

  afterEach(() => {
    vi.restoreAllMocks();
    cleanup();
  });

  it("blocks cross-login execution/retry/discard when User B signs in while User A has an unresolved intent", async () => {
    const scope = "test-cross-actor";
    const dispatchA = vi.fn().mockRejectedValue(new Error("Network dropped"));
    const { result: hookA, unmount } = renderHook(() =>
      useDurableIntent<{ opId: string }, string>(scope, AUTH_A, { tabId: "tab-a" }),
    );

    await act(async () => {
      const res = await hookA.current.execute({ delta: 5 }, () => ({ opId: "op-a-1" }), dispatchA);
      expect(res.ok).toBe(false);
      if (!res.ok) expect(res.outcome).toBe("unknown");
    });
    unmount();

    const dispatchB = vi.fn().mockResolvedValue("should-not-run");
    const { result: hookB } = renderHook(() =>
      useDurableIntent<{ opId: string }, string>(scope, AUTH_B, { tabId: "tab-b" }),
    );

    expect(hookB.current.authorityMismatch).toBe(true);

    await act(async () => {
      const execRes = await hookB.current.execute({ delta: 5 }, () => ({ opId: "op-b-2" }), dispatchB);
      expect(execRes.ok).toBe(false);
      if (!execRes.ok) expect(execRes.outcome).toBe("authority_mismatch");

      const retryRes = await hookB.current.retryPending(dispatchB);
      expect(retryRes.ok).toBe(false);
      if (!retryRes.ok) expect(retryRes.outcome).toBe("authority_mismatch");
    });

    expect(dispatchB).not.toHaveBeenCalled();
    expect(() => hookB.current.clearPending(false)).toThrow(DurableIntentFenceError);
  });

  it("blocks cross-Host replay when database/Host identity changes", async () => {
    const scope = "test-cross-host";
    const dispatch = vi.fn().mockRejectedValue(new Error("Timeout"));
    const { result: hookA, unmount } = renderHook(() =>
      useDurableIntent<{ opId: string }, string>(scope, AUTH_A, { tabId: "tab-host-a" }),
    );

    await act(async () => {
      await hookA.current.execute({ amount: 100 }, () => ({ opId: "op-host-a" }), dispatch);
    });
    unmount();

    const dispatchOther = vi.fn();
    const { result: hookOther } = renderHook(() =>
      useDurableIntent<{ opId: string }, string>(scope, AUTH_OTHER_HOST, { tabId: "tab-host-b" }),
    );

    expect(hookOther.current.authorityMismatch).toBe(true);
    await act(async () => {
      const retryRes = await hookOther.current.retryPending(dispatchOther);
      expect(retryRes.ok).toBe(false);
      if (!retryRes.ok) expect(retryRes.outcome).toBe("authority_mismatch");
    });
    expect(dispatchOther).not.toHaveBeenCalled();
  });

  it("does NOT relabel an unresolved intent as rejected/discardable when retry receives 401/403 OR 409/400/ERR_IDEMPOTENCY_CONFLICT", async () => {
    const scope = "test-retry-403-and-409";
    const dispatch = vi
      .fn()
      .mockRejectedValueOnce(new Error("Socket hangup post-commit"))
      .mockRejectedValueOnce(new HostApiError("Permission revoked.", 403, "Auth.PermissionDenied"))
      .mockRejectedValueOnce(new HostApiError("Payload conflict", 409, "ERR_IDEMPOTENCY_CONFLICT"))
      .mockRejectedValueOnce(new HostApiError("Shift already closed", 409, "Shift.AlreadyClosed"));

    const { result } = renderHook(() =>
      useDurableIntent<{ opId: string }, string>(scope, AUTH_A, { tabId: "tab-retry" }),
    );

    await act(async () => {
      const first = await result.current.execute({ qty: 10 }, () => ({ opId: "op-403-1" }), dispatch);
      expect(first.ok).toBe(false);
      if (!first.ok) expect(first.outcome).toBe("unknown");
    });

    // Retry receives 403 -> stays status="unknown", retryAuthBlocked=true, clearPending(false) throws!
    await act(async () => {
      const retry403 = await result.current.retryPending(dispatch);
      expect(retry403.ok).toBe(false);
      if (!retry403.ok) expect(retry403.outcome).toBe("unknown");
    });
    expect(result.current.pending?.status).toBe("unknown");
    expect(result.current.pending?.retryAuthBlocked).toBe(true);
    expect(() => result.current.clearPending(false)).toThrow(DurableIntentFenceError);

    // Retry receives 409 ERR_IDEMPOTENCY_CONFLICT -> MUST STILL stay status="unknown", retryConflictBlocked=true!
    await act(async () => {
      const retryConflict = await result.current.retryPending(dispatch);
      expect(retryConflict.ok).toBe(false);
      if (!retryConflict.ok) expect(retryConflict.outcome).toBe("unknown");
    });
    expect(result.current.pending?.status).toBe("unknown");
    expect(result.current.pending?.hadUncertainOutcome).toBe(true);
    expect(result.current.pending?.retryConflictBlocked).toBe(true);
    expect(() => result.current.clearPending(false)).toThrow(DurableIntentFenceError);

    // Retry receives generic 409 after uncertain outcome -> MUST STILL keep unresolved fence!
    await act(async () => {
      const retryGeneric409 = await result.current.retryPending(dispatch);
      expect(retryGeneric409.ok).toBe(false);
      if (!retryGeneric409.ok) expect(retryGeneric409.outcome).toBe("unknown");
    });
    expect(result.current.pending?.status).toBe("unknown");
    expect(result.current.pending?.retryConflictBlocked).toBe(true);
    expect(() => result.current.clearPending(false)).toThrow(DurableIntentFenceError);
  });

  it("keeps first-attempt ERR_IDEMPOTENCY_CONFLICT fenced instead of safely discardable", async () => {
    const scope = "test-first-idemp-conflict";
    const { result } = renderHook(() =>
      useDurableIntent<{ opId: string }, string>(scope, AUTH_A, { tabId: "tab-c1" }),
    );
    await act(async () => {
      const res = await result.current.execute(
        { amt: 500 },
        () => ({ opId: "op-conflict" }),
        async () => {
          throw new HostApiError("Idempotency conflict", 409, "ERR_IDEMPOTENCY_CONFLICT");
        },
      );
      expect(res.ok).toBe(false);
      if (!res.ok) expect(res.outcome).toBe("unknown");
    });
    expect(result.current.pending?.status).toBe("unknown");
    expect(() => result.current.clearPending(false)).toThrow(DurableIntentFenceError);
  });

  it("fences legacy pre-upgrade (.v1) pending records fail-closed until explicit audited clear", async () => {
    const scope = "test-v1-upgrade";
    window.localStorage.setItem(
      legacyDurableIntentV1Key(scope),
      JSON.stringify({ version: 1, scope, payload: { operationId: "legacy-v1-op" }, status: "unknown" }),
    );

    const dispatch = vi.fn();
    const { result } = renderHook(() =>
      useDurableIntent<{ opId: string }, string>(scope, AUTH_A, { tabId: "tab-v1" }),
    );

    expect(result.current.corruptReason).toMatch(/pre-upgrade \(v1\)/i);
    await act(async () => {
      const res = await result.current.execute({ x: 1 }, () => ({ opId: "new-v2" }), dispatch);
      expect(res.ok).toBe(false);
      if (!res.ok) expect(res.outcome).toBe("corrupt");
    });
    expect(dispatch).not.toHaveBeenCalled();
    expect(() => result.current.clearPending(false)).toThrow(DurableIntentFenceError);

    // Explicit audited clear removes the legacy .v1 key and unlocks the scope.
    act(() => {
      result.current.clearPending(true);
    });
    expect(window.localStorage.getItem(legacyDurableIntentV1Key(scope))).toBeNull();
    expect(result.current.corruptReason).toBeNull();
  });

  it("enforces cross-tab single-writer exclusion so Tab 2 cannot overwrite or clear Tab 1's in-flight intent", async () => {
    const scope = "test-cross-tab-race";
    let resolveTab1!: (v: string) => void;
    const dispatchTab1 = vi.fn(
      () =>
        new Promise<string>((resolve) => {
          resolveTab1 = resolve;
        }),
    );
    const dispatchTab2 = vi.fn().mockResolvedValue("tab2-should-never-run");

    const { result: tab1 } = renderHook(() =>
      useDurableIntent<{ opId: string }, string>(scope, AUTH_A, { tabId: "tab-1" }),
    );
    const { result: tab2 } = renderHook(() =>
      useDurableIntent<{ opId: string }, string>(scope, AUTH_A, { tabId: "tab-2" }),
    );

    let tab1Promise!: Promise<unknown>;
    act(() => {
      tab1Promise = tab1.current.execute({ order: 1 }, () => ({ opId: "op-tab-1" }), dispatchTab1);
    });

    // While Tab 1 holds the lease in flight, Tab 2 MUST be fenced and MUST NOT call dispatchTab2 or overwrite localStorage.
    await act(async () => {
      const tab2Res = await tab2.current.execute({ order: 2 }, () => ({ opId: "op-tab-2" }), dispatchTab2);
      expect(tab2Res.ok).toBe(false);
      if (!tab2Res.ok) expect(tab2Res.outcome).toBe("fenced");
    });
    expect(dispatchTab2).not.toHaveBeenCalled();
    expect(() => tab2.current.clearPending(false)).toThrow(DurableIntentFenceError);

    const storedMidFlight = JSON.parse(window.localStorage.getItem(durableIntentStorageKey(scope))!);
    expect(storedMidFlight.payload.opId).toBe("op-tab-1");
    expect(storedMidFlight.ownerTabId).toBe("tab-1");

    await act(async () => {
      resolveTab1("tab1-done");
      await tab1Promise;
    });
    expect(window.localStorage.getItem(durableIntentStorageKey(scope))).toBeNull();
  });

  it("fails closed BEFORE dispatching HTTP if localStorage.setItem throws (quota/storage failure)", async () => {
    const scope = "test-storage-quota";
    const dispatch = vi.fn().mockResolvedValue("should-not-dispatch");
    const { result } = renderHook(() =>
      useDurableIntent<{ opId: string }, string>(scope, AUTH_A, { tabId: "tab-quota" }),
    );

    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new DOMException("QuotaExceededError");
    });

    await act(async () => {
      const res = await result.current.execute({ delta: 1 }, () => ({ opId: "op-quota" }), dispatch);
      expect(res.ok).toBe(false);
      if (!res.ok) expect(res.outcome).toBe("corrupt");
    });

    expect(dispatch).not.toHaveBeenCalled();
  });

  it("fails closed on corrupt localStorage JSON or invalid schema and refuses new writes until explicit audit clear", async () => {
    const scope = "test-corrupt";
    window.localStorage.setItem(durableIntentStorageKey(scope), "{not-valid-json");

    const dispatch = vi.fn();
    const { result } = renderHook(() =>
      useDurableIntent<{ opId: string }, string>(scope, AUTH_A, { tabId: "tab-corrupt" }),
    );

    expect(result.current.corruptReason).toMatch(/corrupt JSON/i);

    await act(async () => {
      const res = await result.current.execute({ x: 1 }, () => ({ opId: "new-op" }), dispatch);
      expect(res.ok).toBe(false);
      if (!res.ok) expect(res.outcome).toBe("corrupt");
    });

    expect(dispatch).not.toHaveBeenCalled();
    expect(() => result.current.clearPending(false)).toThrow(DurableIntentFenceError);

    act(() => {
      result.current.clearPending(true);
    });
    expect(result.current.corruptReason).toBeNull();
  });

  it("synchronizes pending state across tabs via window storage events", async () => {
    const scope = "test-multitab";
    const { result } = renderHook(() =>
      useDurableIntent<{ opId: string }, string>(scope, AUTH_A, { tabId: "tab-sync" }),
    );
    expect(result.current.pending).toBeNull();

    const key = durableIntentStorageKey(scope);
    const otherTabRecord = {
      version: 2,
      intentId: "intent-tab-2",
      ownerTabId: "tab-other",
      scope,
      hostIdentityKey: HOST_A,
      actorUserId: USER_A,
      fingerprint: '{"x":1}',
      payload: { opId: "tab-2-op" },
      status: "unknown",
      hadUncertainOutcome: true,
      attemptCount: 1,
      preparedAtUtc: "2026-11-25T09:00:00.000Z",
    };
    window.localStorage.setItem(key, JSON.stringify(otherTabRecord));

    act(() => {
      window.dispatchEvent(new StorageEvent("storage", { key }));
    });

    expect(result.current.pending?.payload.opId).toBe("tab-2-op");
  });

  it("refuses clearPending(false) when authority is null (fail-closed before Host identity is verified)", async () => {
    const scope = "test-null-auth-clear";
    const { result: userAResult } = renderHook(() =>
      useDurableIntent<{ opId: string }, string>(scope, AUTH_A, { tabId: "tab-auth" }),
    );
    await act(async () => {
      await userAResult.current.execute(
        { amt: 1 },
        () => ({ opId: "rejected-op" }),
        async () => {
          throw new HostApiError("Validation failed", 400, "Validation.Invalid");
        },
      );
    });
    expect(userAResult.current.pending?.status).toBe("rejected");

    const { result: unverifiedResult } = renderHook(() =>
      useDurableIntent<{ opId: string }, string>(scope, null, { tabId: "tab-unverified" }),
    );
    expect(() => unverifiedResult.current.clearPending(false)).toThrow(DurableIntentFenceError);
  });
});
