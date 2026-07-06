import { useCallback, useMemo, useState } from "react";
import { ApiError } from "../api/http";
import {
  fetchRemotePosCapabilities,
  fetchRemotePosPairingStatus,
  remotePosActivatePairing,
  remotePosAddLine,
  remotePosClearCart,
  remotePosLogin,
  remotePosLogout,
  remotePosOpenShift,
  remotePosRemoveLine,
  remotePosRequestPairing,
  remotePosStartCart,
  remotePosSubmitCheckout,
  remotePosUpdateLine,
  type RemotePosAuthedConnection,
  type RemotePosConnection,
  type RemotePosHostConnection,
} from "../api/remotePosClient";
import type {
  RemotePosCapabilities,
  RemotePosCart,
  RemotePosCheckoutResult,
  RemotePosJoinResponse,
  RemotePosPairingActivation,
  RemotePosPairingStatus,
  RemotePosSession,
} from "../types/remotePosClient";
import { currentBrowserOrigin, isRemoteClientBrowserLocation } from "../utils/lanEntry";

// LAN-M16-QA-B: stateful hook for the field-safe Remote POS pairing/login flow.
// Pairing approves the device first; login approves the person second.

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
  | "pairing"
  | "login"
  | "logout"
  | "cart"
  | "shift"
  | "checkout";

export function useRemotePosClient() {
  const startsFromRemoteEntry = isRemoteClientBrowserLocation();
  const [hostBaseUrl, setHostBaseUrl] = useState(startsFromRemoteEntry ? currentBrowserOrigin() : "");
  const [hostAddressConfirmed, setHostAddressConfirmed] = useState(startsFromRemoteEntry);
  const [pairingRequest, setPairingRequest] = useState<RemotePosJoinResponse | null>(null);
  const [pairingStatus, setPairingStatus] = useState<RemotePosPairingStatus | null>(null);
  const [pairingActivation, setPairingActivation] = useState<RemotePosPairingActivation | null>(null);
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

  const hostConnection = useMemo<RemotePosHostConnection>(() => ({ hostBaseUrl }), [hostBaseUrl]);

  const connection = useMemo<RemotePosConnection>(
    () => ({ hostBaseUrl, terminalId, handshakeSessionId }),
    [hostBaseUrl, terminalId, handshakeSessionId],
  );

  const authedConnection = useMemo<RemotePosAuthedConnection | null>(
    () => (sessionToken ? { ...connection, sessionToken } : null),
    [connection, sessionToken],
  );

  const isPaired = terminalId.trim().length > 0 && handshakeSessionId.trim().length > 0;
  const isConnected = session !== null && sessionToken !== null;

  const resetSession = useCallback(() => {
    setSession(null);
    setSessionToken(null);
    setCapabilities(null);
    setCart(null);
    setLastCheckout(null);
  }, []);

  const resetPairing = useCallback(() => {
    resetSession();
    setPairingRequest(null);
    setPairingStatus(null);
    setPairingActivation(null);
    setTerminalId("");
    setHandshakeSessionId("");
  }, [resetSession]);

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

  const confirmHostAddress = useCallback(() => {
    resetPairing();
    setHostAddressConfirmed(true);
    setStatusMessage("Use the host LAN address shown on the host. Do not use localhost from another device.");
  }, [resetPairing]);

  const requestJoin = useCallback(
    async (joinCode: string, deviceName: string) => {
      const result = await run("pairing", () => remotePosRequestPairing(hostConnection, { joinCode, deviceName }));
      if (result) {
        setPairingRequest(result);
        setPairingStatus({
          pairingState: result.pairingState,
          requestId: result.requestId,
          deviceName: result.deviceName,
          message: result.message,
          pairingCodeRequired: false,
          paired: false,
        });
        setStatusMessage(result.message);
      }
    },
    [hostConnection, run],
  );

  const refreshPairingStatus = useCallback(async () => {
    if (!pairingRequest?.requestId) return;
    const result = await run("pairing", () => fetchRemotePosPairingStatus(hostConnection, pairingRequest.requestId));
    if (result) {
      setPairingStatus(result);
      setStatusMessage(result.message);
    }
  }, [hostConnection, pairingRequest?.requestId, run]);

  const activatePairing = useCallback(
    async (pairingCode: string) => {
      if (!pairingRequest?.requestId) return;
      const result = await run("pairing", () =>
        remotePosActivatePairing(hostConnection, { requestId: pairingRequest.requestId, pairingCode }),
      );
      if (result) {
        setPairingActivation(result);
        if (result.terminalId && result.handshakeSessionId) {
          setTerminalId(result.terminalId);
          setHandshakeSessionId(result.handshakeSessionId);
          setPairingStatus({
            pairingState: result.pairingState,
            requestId: pairingRequest.requestId,
            deviceName: result.terminalName ?? pairingRequest.deviceName,
            message: result.message,
            pairingCodeRequired: false,
            paired: true,
          });
          setStatusMessage(result.message);
        }
      }
    },
    [hostConnection, pairingRequest, run],
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
    hostBaseUrl,
    setHostBaseUrl,
    hostAddressConfirmed,
    confirmHostAddress,
    pairingRequest,
    pairingStatus,
    pairingActivation,
    isPaired,
    resetPairing,
    session,
    capabilities,
    cart,
    lastCheckout,
    isConnected,
    busy,
    errorMessage,
    statusMessage,
    requestJoin,
    refreshPairingStatus,
    activatePairing,
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
