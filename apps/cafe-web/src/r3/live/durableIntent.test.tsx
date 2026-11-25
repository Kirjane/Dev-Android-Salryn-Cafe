import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, renderHook } from "@testing-library/react";
import { HostApiError } from "../../api/hostClient";
import {
  DurableIntentFenceError,
  durableIntentStorageKey,
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

describe("useDurableIntent P1 authority, corruption, 403-retry & multi-tab hardening", () => {
  beforeEach(() => {
    window.localStorage.clear();
  });

  afterEach(() => {
    cleanup();
  });

  it("blocks cross-login execution/retry/discard when User B signs in while User A has an unresolved intent", async () => {
    const scope = "test-cross-actor";
    const dispatchA = vi.fn().mockRejectedValue(new Error("Network dropped"));
    const { result: hookA, unmount } = renderHook(() =>
      useDurableIntent<{ opId: string }, string>(scope, AUTH_A),
    );

    await act(async () => {
      const res = await hookA.current.execute({ delta: 5 }, () => ({ opId: "op-a-1" }), dispatchA);
      expect(res.ok).toBe(false);
      if (!res.ok) expect(res.outcome).toBe("unknown");
    });
    unmount();

    // User B signs in on the same browser and attempts execute, retry, and discard -> ALL MUST FAIL CLOSED!
    const dispatchB = vi.fn().mockResolvedValue("should-not-run");
    const { result: hookB } = renderHook(() =>
      useDurableIntent<{ opId: string }, string>(scope, AUTH_B),
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
      useDurableIntent<{ opId: string }, string>(scope, AUTH_A),
    );

    await act(async () => {
      await hookA.current.execute({ amount: 100 }, () => ({ opId: "op-host-a" }), dispatch);
    });
    unmount();

    const dispatchOther = vi.fn();
    const { result: hookOther } = renderHook(() =>
      useDurableIntent<{ opId: string }, string>(scope, AUTH_OTHER_HOST),
    );

    expect(hookOther.current.authorityMismatch).toBe(true);
    await act(async () => {
      const retryRes = await hookOther.current.retryPending(dispatchOther);
      expect(retryRes.ok).toBe(false);
      if (!retryRes.ok) expect(retryRes.outcome).toBe("authority_mismatch");
    });
    expect(dispatchOther).not.toHaveBeenCalled();
  });

  it("does NOT relabel an unresolved intent as rejected/discardable when retry receives 401 or 403", async () => {
    const scope = "test-retry-403";
    const dispatch = vi
      .fn()
      .mockRejectedValueOnce(new Error("Socket hangup post-commit"))
      .mockRejectedValueOnce(new HostApiError("Permission revoked.", 403, "Auth.PermissionDenied"));

    const { result } = renderHook(() => useDurableIntent<{ opId: string }, string>(scope, AUTH_A));

    await act(async () => {
      const first = await result.current.execute({ qty: 10 }, () => ({ opId: "op-403-1" }), dispatch);
      expect(first.ok).toBe(false);
      if (!first.ok) expect(first.outcome).toBe("unknown");
    });

    // Retry receives 403 -> MUST stay status="unknown", retryAuthBlocked=true, and clearPending(false) MUST throw!
    await act(async () => {
      const retry = await result.current.retryPending(dispatch);
      expect(retry.ok).toBe(false);
      if (!retry.ok) expect(retry.outcome).toBe("unknown");
    });

    expect(result.current.pending?.status).toBe("unknown");
    expect(result.current.pending?.retryAuthBlocked).toBe(true);
    expect(() => result.current.clearPending(false)).toThrow(DurableIntentFenceError);
  });

  it("fails closed on corrupt localStorage JSON or invalid schema and refuses new writes until explicit audit clear", async () => {
    const scope = "test-corrupt";
    window.localStorage.setItem(durableIntentStorageKey(scope), "{not-valid-json");

    const dispatch = vi.fn();
    const { result } = renderHook(() => useDurableIntent<{ opId: string }, string>(scope, AUTH_A));

    expect(result.current.corruptReason).toMatch(/corrupt JSON/i);

    await act(async () => {
      const res = await result.current.execute({ x: 1 }, () => ({ opId: "new-op" }), dispatch);
      expect(res.ok).toBe(false);
      if (!res.ok) expect(res.outcome).toBe("corrupt");
    });

    expect(dispatch).not.toHaveBeenCalled();
    expect(() => result.current.clearPending(false)).toThrow(DurableIntentFenceError);

    // Explicit audit clear (allowUnsafeClear = true) unlocks after operator checks Host state.
    act(() => {
      result.current.clearPending(true);
    });
    expect(result.current.corruptReason).toBeNull();
  });

  it("synchronizes pending state across tabs via window storage events", async () => {
    const scope = "test-multitab";
    const { result } = renderHook(() => useDurableIntent<{ opId: string }, string>(scope, AUTH_A));
    expect(result.current.pending).toBeNull();

    const key = durableIntentStorageKey(scope);
    const otherTabRecord = {
      version: 2,
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
      useDurableIntent<{ opId: string }, string>(scope, AUTH_A),
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

    // Before authority is verified (authority === null), even clearing a rejected intent must fail closed!
    const { result: unverifiedResult } = renderHook(() =>
      useDurableIntent<{ opId: string }, string>(scope, null),
    );
    expect(() => unverifiedResult.current.clearPending(false)).toThrow(DurableIntentFenceError);
  });
});
