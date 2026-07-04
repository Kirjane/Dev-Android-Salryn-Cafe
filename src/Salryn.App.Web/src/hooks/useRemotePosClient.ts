import { useCallback, useMemo, useState } from "react";
import { ApiError } from "../api/http";
import {
  fetchRemotePosCapabilities,
  remotePosAddLine,
  remotePosClearCart,
  remotePosLogin,
  remotePosLogout,
  remotePosOpenShift,
  remotePosRemoveLine,
  remotePosStartCart,
  remotePosSubmitCheckout,
  remotePosUpdateLine,
  type RemotePosAuthedConnection,
  type RemotePosConnection,
} from "../api/remotePosClient";
import type {
  RemotePosCapabilities,
  RemotePosCart,
  RemotePosCheckoutResult,
  RemotePosSession,
} from "../types/remotePosClient";

// LAN-M16: stateful hook for the remote POS client shell. The opaque operator session token is
// held only in memory (never persisted / never logged) and is cleared on logout or failure.

function newClientRequestId(): string {
  const cryptoObj = globalThis.crypto;
  if (cryptoObj && typeof cryptoObj.randomUUID === "function") {
    return `rpos-${cryptoObj.randomUUID()}`;
  }
  return `rpos-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

function describeError(error: unknown): string {
  if (error instanceof ApiError) {
    return error.message;
  }
  if (error instanceof Error) {
    return error.message;
  }
  return "Request failed.";
}

export type RemotePosBusyAction =
  | "idle"
  | "login"
  | "logout"
  | "cart"
  | "shift"
  | "checkout";

export function useRemotePosClient() {
  const [hostBaseUrl, setHostBaseUrl] = useState("");
  const [terminalId, setTerminalId] = useState("");
  const [handshakeSessionId, setHandshakeSessionId] = useState("");

  const [session, setSession] = useState<RemotePosSession | null>(null);
  const [sessionToken, setSessionToken] = useState<string | null>(null);
  const [capabilities, setCapabilities] = useState<RemotePosCapabilities | null>(null);
  const [cart, setCart] = useState<RemotePosCart | null>(null);
  const [lastCheckout, setLastCheckout] = useState<RemotePosCheckoutResult | null>(null);

  const [busy, setBusy] = useState<RemotePosBusyAction>("idle");
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [statusMessage, setStatusMessage] = useState<string | null>(null);

  const connection = useMemo<RemotePosConnection>(
    () => ({ hostBaseUrl, terminalId, handshakeSessionId }),
    [hostBaseUrl, terminalId, handshakeSessionId],
  );

  const authedConnection = useMemo<RemotePosAuthedConnection | null>(
    () => (sessionToken ? { ...connection, sessionToken } : null),
    [connection, sessionToken],
  );

  const isConnected = session !== null && sessionToken !== null;

  const resetSession = useCallback(() => {
    setSession(null);
    setSessionToken(null);
    setCapabilities(null);
    setCart(null);
    setLastCheckout(null);
  }, []);

  const run = useCallback(
    async <T,>(action: RemotePosBusyAction, work: () => Promise<T>): Promise<T | null> => {
      setBusy(action);
      setErrorMessage(null);
      try {
        return await work();
      } catch (error) {
        const message = describeError(error);
        setErrorMessage(message);
        // A 401 means the operator session is no longer valid; fail closed by clearing it.
        if (error instanceof ApiError && error.status === 401) {
          resetSession();
        }
        return null;
      } finally {
        setBusy("idle");
      }
    },
    [resetSession],
  );

  const login = useCallback(
    async (username: string, password: string) => {
      const result = await run("login", async () => {
        const issued = await remotePosLogin(connection, { username, password });
        if (!issued.sessionToken) {
          throw new ApiError("Host did not return an operator session token.", 500);
        }
        const authed: RemotePosAuthedConnection = { ...connection, sessionToken: issued.sessionToken };
        const caps = await fetchRemotePosCapabilities(authed).catch(() => null);
        return { issued, caps };
      });

      if (result) {
        setSession(result.issued);
        setSessionToken(result.issued.sessionToken);
        setCapabilities(result.caps);
        setStatusMessage(`Signed in as ${result.issued.displayName ?? result.issued.username ?? "operator"}.`);
      }
    },
    [connection, run],
  );

  const logout = useCallback(async () => {
    if (authedConnection) {
      await run("logout", async () => {
        await remotePosLogout(authedConnection).catch(() => undefined);
        return true;
      });
    }
    resetSession();
    setStatusMessage("Signed out of the remote POS host.");
  }, [authedConnection, resetSession, run]);

  const startCart = useCallback(async () => {
    if (!authedConnection) return;
    const result = await run("cart", () => remotePosStartCart(authedConnection));
    if (result) {
      setCart(result);
      setStatusMessage("New cart started on host.");
    }
  }, [authedConnection, run]);

  const addLine = useCallback(
    async (productId: string, quantityX1000: number) => {
      if (!authedConnection) return;
      const result = await run("cart", () => remotePosAddLine(authedConnection, { productId, quantityX1000 }));
      if (result) setCart(result);
    },
    [authedConnection, run],
  );

  const updateLine = useCallback(
    async (productId: string, quantityX1000: number) => {
      if (!authedConnection) return;
      const result = await run("cart", () => remotePosUpdateLine(authedConnection, { productId, quantityX1000 }));
      if (result) setCart(result);
    },
    [authedConnection, run],
  );

  const removeLine = useCallback(
    async (productId: string) => {
      if (!authedConnection) return;
      const result = await run("cart", () => remotePosRemoveLine(authedConnection, productId));
      if (result) setCart(result);
    },
    [authedConnection, run],
  );

  const clearCart = useCallback(async () => {
    if (!authedConnection) return;
    const result = await run("cart", () => remotePosClearCart(authedConnection));
    if (result) setCart(result);
  }, [authedConnection, run]);

  const openShift = useCallback(
    async (openingCashCents: number) => {
      if (!authedConnection) return;
      const result = await run("shift", () => remotePosOpenShift(authedConnection, openingCashCents));
      if (result) setStatusMessage(result.message || "Shift opened.");
    },
    [authedConnection, run],
  );

  const submitCheckout = useCallback(
    async (payments: { method: string; amountCents: number }[]) => {
      if (!authedConnection) return;
      const clientRequestId = newClientRequestId();
      const result = await run("checkout", () =>
        remotePosSubmitCheckout(authedConnection, { clientRequestId, payments }),
      );
      if (result) {
        setLastCheckout(result);
        if (result.status === "Completed" || result.status === "DuplicateRequest") {
          setCart(null);
          setStatusMessage(
            result.duplicate
              ? `Duplicate submit safely returned original receipt ${result.receiptNumber ?? ""}.`
              : `Sale completed on host. Receipt ${result.receiptNumber ?? ""}.`,
          );
        } else {
          setStatusMessage(`Checkout responded: ${result.status}. ${result.message}`);
        }
      }
    },
    [authedConnection, run],
  );

  return {
    // connection settings
    hostBaseUrl,
    setHostBaseUrl,
    terminalId,
    setTerminalId,
    handshakeSessionId,
    setHandshakeSessionId,
    // state
    session,
    capabilities,
    cart,
    lastCheckout,
    isConnected,
    busy,
    errorMessage,
    statusMessage,
    // actions
    login,
    logout,
    startCart,
    addLine,
    updateLine,
    removeLine,
    clearCart,
    openShift,
    submitCheckout,
  };
}
