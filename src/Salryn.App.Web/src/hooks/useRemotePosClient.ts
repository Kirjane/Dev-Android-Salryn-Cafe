import { useCallback, useEffect, useMemo, useState } from "react";
import { ApiError } from "../api/http";
import {
  fetchRemotePosCapabilities,
  fetchRemotePosPairingStatus,
  fetchRemotePosTerminalTrust,
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
import {
  clearLanClientPairing,
  hasPairedTerminalMaterial,
  readLanClientPairing,
  touchLanClientPairing,
  writeLanClientPairing,
} from "../utils/lanClientStorage";

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

export type LanClientRestoreState = "idle" | "restoring" | "restored" | "pending" | "revoked" | "offline";

function normalizeHostBaseUrl(hostBaseUrl: string): string {
  return hostBaseUrl.trim().replace(/\/+$/, "");
}

function formatTerminalIdSuffix(terminalId: string): string {
  const trimmed = terminalId.trim();
  if (trimmed.length <= 8) {
    return trimmed;
  }
  return trimmed.slice(-8);
}

function isTrustRevoked(error: unknown): boolean {
  return error instanceof ApiError && error.status === 403;
}

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
  const [restoreState, setRestoreState] = useState<LanClientRestoreState>("idle");
  const [pairingRestored, setPairingRestored] = useState(false);

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
  const terminalIdSuffix = useMemo(() => formatTerminalIdSuffix(terminalId), [terminalId]);

  const resetSession = useCallback(() => {
    setSession(null);
    setSessionToken(null);
    setCapabilities(null);
    setCart(null);
    setLastCheckout(null);
  }, []);

  const clearPairing = useCallback(() => {
    clearLanClientPairing();
    resetSession();
    setPairingRequest(null);
    setPairingStatus(null);
    setPairingActivation(null);
    setTerminalId("");
    setHandshakeSessionId("");
    setPairingRestored(false);
    setRestoreState("idle");
    setErrorMessage(null);
    setStatusMessage("Device pairing cleared. Request to join again when ready.");
  }, [resetSession]);

  const resetPairing = clearPairing;

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
    clearPairing();
    setHostAddressConfirmed(true);
    setStatusMessage("Use the host LAN address shown on the host. Do not use localhost from another device.");
  }, [clearPairing]);

  useEffect(() => {
    if (!hostAddressConfirmed) {
      return;
    }

    const normalizedHost = normalizeHostBaseUrl(hostBaseUrl);
    if (!normalizedHost) {
      return;
    }

    let cancelled = false;

    async function restoreStoredPairing() {
      const stored = readLanClientPairing("remote-pos", normalizedHost);
      if (!stored) {
        if (!cancelled) {
          setRestoreState("idle");
        }
        return;
      }

      if (!cancelled) {
        setRestoreState("restoring");
        setErrorMessage(null);
      }

      if (hasPairedTerminalMaterial(stored)) {
        const connectionForTrust: RemotePosConnection = {
          hostBaseUrl: normalizedHost,
          terminalId: stored.terminalId,
          handshakeSessionId: stored.handshakeSessionId,
        };

        try {
          const trust = await fetchRemotePosTerminalTrust(connectionForTrust);
          if (cancelled) {
            return;
          }

          if (trust.ok) {
            setTerminalId(stored.terminalId);
            setHandshakeSessionId(stored.handshakeSessionId);
            setPairingStatus({
              pairingState: trust.state || "Paired",
              requestId: stored.requestId ?? "",
              deviceName: stored.displayName ?? "Paired terminal",
              message: "Paired terminal restored.",
              pairingCodeRequired: false,
              paired: true,
            });
            touchLanClientPairing("remote-pos", normalizedHost);
            setPairingRestored(true);
            setRestoreState("restored");
            setStatusMessage("Paired terminal restored.");
            return;
          }
        } catch (error) {
          if (cancelled) {
            return;
          }

          if (isTrustRevoked(error)) {
            clearLanClientPairing();
            setPairingRestored(false);
            setRestoreState("revoked");
            setStatusMessage("Pairing revoked or expired. Re-pair this device.");
            return;
          }

          setTerminalId(stored.terminalId);
          setHandshakeSessionId(stored.handshakeSessionId);
          setPairingRestored(false);
          setRestoreState("offline");
          setErrorMessage("Host unreachable. Stored pairing kept; retry when the host is available.");
          return;
        }
      }

      const pendingRequestId = stored.requestId?.trim();
      if (pendingRequestId) {
        try {
          const status = await fetchRemotePosPairingStatus({ hostBaseUrl: normalizedHost }, pendingRequestId);
          if (cancelled) {
            return;
          }

          setPairingRequest({
            requestId: pendingRequestId,
            deviceName: status.deviceName ?? stored.displayName ?? "Pending device",
            pairingState: status.pairingState,
            requestedAtUtc: "",
            expiresAtUtc: "",
            message: status.message,
          });
          setPairingStatus(status);
          setRestoreState("pending");
          setStatusMessage(status.message);
          return;
        } catch (error) {
          if (cancelled) {
            return;
          }

          if (isTrustRevoked(error)) {
            clearLanClientPairing();
            setRestoreState("revoked");
            setStatusMessage("Pairing revoked or expired. Re-pair this device.");
            return;
          }

          setRestoreState("offline");
          setErrorMessage("Host unreachable. Pending join request kept.");
          return;
        }
      }

      if (!cancelled) {
        setRestoreState("idle");
      }
    }

    void restoreStoredPairing();

    return () => {
      cancelled = true;
    };
  }, [hostAddressConfirmed, hostBaseUrl]);

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
        setPairingRestored(false);
        setRestoreState("pending");
        writeLanClientPairing({
          version: 1,
          mode: "remote-pos",
          hostBaseUrl: normalizeHostBaseUrl(hostConnection.hostBaseUrl),
          requestId: result.requestId,
          displayName: result.deviceName,
          lastSeenAt: new Date().toISOString(),
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
          setPairingRestored(false);
          setRestoreState("restored");
          writeLanClientPairing({
            version: 1,
            mode: "remote-pos",
            hostBaseUrl: normalizeHostBaseUrl(hostConnection.hostBaseUrl),
            terminalId: result.terminalId,
            handshakeSessionId: result.handshakeSessionId,
            displayName: result.terminalName ?? pairingRequest.deviceName,
            requestId: pairingRequest.requestId,
            pairedAt: new Date().toISOString(),
            lastSeenAt: new Date().toISOString(),
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
    restoreState,
    pairingRestored,
    terminalIdSuffix,
    resetPairing,
    clearPairing,
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
