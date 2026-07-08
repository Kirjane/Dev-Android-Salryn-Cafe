import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { ApiError } from "../api/http";
import {
  fetchRemotePosCapabilities,
  fetchRemotePosPairingStatus,
  fetchRemotePosSession,
  fetchRemotePosTerminalTrust,
  remotePosActivatePairing,
  remotePosAddLine,
  remotePosClearCart,
  remotePosLogin,
  remotePosLogout,
  remotePosOpenShift,
  remotePosRefreshTerminalHandshake,
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
  clearRemotePosOperatorSessionToken,
  hasPairedTerminalMaterial,
  readLanClientPairing,
  readRemotePosOperatorSessionToken,
  writeLanClientPairing,
  writeRemotePosOperatorSessionToken,
  type LanClientLastKnownState,
  type LanClientPairingRecord,
} from "../utils/lanClientStorage";

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

export type RemotePosFlowState =
  | "S0_NO_HOST"
  | "S1_UNPAIRED"
  | "S2_REQUEST_PENDING"
  | "S3_HOST_APPROVED_NEEDS_ACTIVATION"
  | "S4_TERMINAL_TRUSTED"
  | "S5_REMOTE_POS_OPERATOR_AUTH"
  | "S6_REMOTE_POS_READY"
  | "S_REVOKED"
  | "S_OFFLINE"
  | "S_RESTORING";

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

function isNetworkFailure(error: unknown): boolean {
  return !(error instanceof ApiError);
}

function isRevokedTrustStatus(status: string): boolean {
  return status === "Revoked" || status === "Disabled";
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
  const [flowState, setFlowState] = useState<RemotePosFlowState>("S1_UNPAIRED");
  const [pairingRestored, setPairingRestored] = useState(false);
  const [trustProbeResult, setTrustProbeResult] = useState<string | null>(null);
  const [storageRestored, setStorageRestored] = useState(false);
  const restoreAttemptedRef = useRef(false);

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
    clearRemotePosOperatorSessionToken();
  }, []);

  const persistLanRecord = useCallback(
    (partial: Partial<LanClientPairingRecord> & { lastKnownState: LanClientLastKnownState }) => {
      const normalizedHost = normalizeHostBaseUrl(hostConnection.hostBaseUrl);
      if (!normalizedHost) {
        return;
      }

      const existing = readLanClientPairing("remote-pos", normalizedHost);
      writeLanClientPairing({
        version: 1,
        mode: "remote-pos",
        hostBaseUrl: normalizedHost,
        terminalId: partial.terminalId ?? existing?.terminalId,
        handshakeSessionId: partial.handshakeSessionId ?? existing?.handshakeSessionId,
        displayName: partial.displayName ?? existing?.displayName,
        requestId: partial.requestId ?? existing?.requestId,
        pairedAt: partial.pairedAt ?? existing?.pairedAt,
        lastSeenAt: new Date().toISOString(),
        lastKnownState: partial.lastKnownState,
      });
    },
    [hostConnection.hostBaseUrl],
  );

  const clearPairing = useCallback(() => {
    clearLanClientPairing();
    resetSession();
    setPairingRequest(null);
    setPairingStatus(null);
    setPairingActivation(null);
    setTerminalId("");
    setHandshakeSessionId("");
    setPairingRestored(false);
    setStorageRestored(false);
    setTrustProbeResult(null);
    setFlowState("S1_UNPAIRED");
    setErrorMessage(null);
    setStatusMessage("Device pairing cleared. Request access to this host when ready.");
    restoreAttemptedRef.current = false;
  }, [resetSession]);

  const resetPairing = clearPairing;

  const applyTrustedTerminal = useCallback(
    (
      trustedTerminalId: string,
      trustedHandshakeSessionId: string,
      displayName: string,
      requestId: string | null,
      restored: boolean,
      operatorSessionValid: boolean,
    ) => {
      setTerminalId(trustedTerminalId);
      setHandshakeSessionId(trustedHandshakeSessionId);
      setPairingRequest(null);
      setPairingStatus({
        pairingState: "Paired",
        requestId,
        deviceName: displayName,
        message: restored ? "Paired terminal restored." : "This terminal is paired.",
        pairingCodeRequired: false,
        paired: true,
      });
      persistLanRecord({
        terminalId: trustedTerminalId,
        handshakeSessionId: trustedHandshakeSessionId,
        displayName,
        requestId: requestId ?? undefined,
        pairedAt: new Date().toISOString(),
        lastKnownState: "S4_TERMINAL_TRUSTED",
      });
      setPairingRestored(restored);
      setFlowState(operatorSessionValid ? "S6_REMOTE_POS_READY" : "S4_TERMINAL_TRUSTED");
      setStatusMessage(restored ? "Paired terminal restored." : "This terminal is paired.");
    },
    [persistLanRecord],
  );

  const restoreOperatorSession = useCallback(
    async (trustedTerminalId: string, trustedHandshakeSessionId: string) => {
      const storedToken = readRemotePosOperatorSessionToken(hostConnection.hostBaseUrl);
      if (!storedToken) {
        return false;
      }

      const trust = await fetchRemotePosTerminalTrust(
        {
          hostBaseUrl: hostConnection.hostBaseUrl,
          terminalId: trustedTerminalId,
          handshakeSessionId: trustedHandshakeSessionId,
        },
        null,
        storedToken,
      );

      if (!trust.operatorSessionValid) {
        clearRemotePosOperatorSessionToken();
        return false;
      }

      const authed: RemotePosAuthedConnection = {
        hostBaseUrl: hostConnection.hostBaseUrl,
        terminalId: trustedTerminalId,
        handshakeSessionId: trustedHandshakeSessionId,
        sessionToken: storedToken,
      };

      const [sessionResult, caps] = await Promise.all([
        fetchRemotePosSession(authed).catch(() => null),
        fetchRemotePosCapabilities(authed).catch(() => null),
      ]);

      if (!sessionResult?.sessionToken) {
        clearRemotePosOperatorSessionToken();
        return false;
      }

      setSession(sessionResult);
      setSessionToken(storedToken);
      setCapabilities(caps);
      setFlowState("S6_REMOTE_POS_READY");
      setStatusMessage(`Signed in as ${sessionResult.displayName ?? sessionResult.username ?? "operator"}.`);
      return true;
    },
    [hostConnection.hostBaseUrl],
  );

  const resolveTrustStatus = useCallback(
    async (stored: LanClientPairingRecord | null) => {
      const normalizedHost = normalizeHostBaseUrl(hostBaseUrl);
      if (!normalizedHost) {
        setFlowState("S0_NO_HOST");
        return;
      }

      setFlowState("S_RESTORING");
      setErrorMessage(null);

      const operatorToken = readRemotePosOperatorSessionToken(normalizedHost);
      const trustConnection = {
        hostBaseUrl: normalizedHost,
        terminalId: stored?.terminalId ?? "",
        handshakeSessionId: stored?.handshakeSessionId ?? "",
      };

      try {
        let trust = await fetchRemotePosTerminalTrust(
          trustConnection,
          stored?.requestId ?? null,
          operatorToken,
        );
        setTrustProbeResult(trust.status);

        if (trust.status === "HandshakeExpired" && trust.terminalId) {
          const refreshed = await remotePosRefreshTerminalHandshake({
            hostBaseUrl: normalizedHost,
            terminalId: trust.terminalId,
          });
          trust = await fetchRemotePosTerminalTrust(
            {
              hostBaseUrl: normalizedHost,
              terminalId: refreshed.terminalId,
              handshakeSessionId: refreshed.handshakeSessionId,
            },
            stored?.requestId ?? null,
            operatorToken,
          );
          setTrustProbeResult(`${trust.status} (handshake refreshed)`);
        }

        if (trust.status === "Trusted") {
          const trustedTerminalId = trust.terminalId ?? stored?.terminalId ?? "";
          const trustedHandshakeSessionId = trust.handshakeSessionId ?? stored?.handshakeSessionId ?? "";
          if (!trustedTerminalId || !trustedHandshakeSessionId) {
            setFlowState("S1_UNPAIRED");
            return;
          }

          applyTrustedTerminal(
            trustedTerminalId,
            trustedHandshakeSessionId,
            trust.displayName ?? stored?.displayName ?? "Paired terminal",
            trust.requestId ?? stored?.requestId ?? null,
            true,
            trust.operatorSessionValid,
          );

          if (trust.operatorSessionValid) {
            const restoredSession = await restoreOperatorSession(trustedTerminalId, trustedHandshakeSessionId);
            if (!restoredSession) {
              setFlowState("S5_REMOTE_POS_OPERATOR_AUTH");
            }
          } else {
            setFlowState("S5_REMOTE_POS_OPERATOR_AUTH");
          }
          return;
        }

        if (trust.status === "Approved") {
          const requestId = trust.requestId ?? stored?.requestId ?? "";
          setPairingRequest({
            requestId,
            deviceName: trust.displayName ?? stored?.displayName ?? "Pending device",
            pairingState: "Accepted",
            requestedAtUtc: "",
            expiresAtUtc: "",
            message: trust.message,
          });
          setPairingStatus({
            pairingState: "Accepted",
            requestId,
            deviceName: trust.displayName ?? stored?.displayName ?? "Pending device",
            message: trust.message,
            pairingCodeRequired: true,
            paired: false,
          });
          persistLanRecord({
            requestId,
            displayName: trust.displayName ?? stored?.displayName,
            lastKnownState: "S3_HOST_APPROVED_NEEDS_ACTIVATION",
          });
          setFlowState("S3_HOST_APPROVED_NEEDS_ACTIVATION");
          setStatusMessage("Host approved this device. Enter the one-time pairing code.");
          return;
        }

        if (trust.status === "Pending") {
          const requestId = trust.requestId ?? stored?.requestId ?? "";
          setPairingRequest({
            requestId,
            deviceName: trust.displayName ?? stored?.displayName ?? "Pending device",
            pairingState: "Pending",
            requestedAtUtc: "",
            expiresAtUtc: "",
            message: trust.message,
          });
          setPairingStatus({
            pairingState: "Pending",
            requestId,
            deviceName: trust.displayName ?? stored?.displayName ?? "Pending device",
            message: trust.message,
            pairingCodeRequired: false,
            paired: false,
          });
          persistLanRecord({
            requestId,
            displayName: trust.displayName ?? stored?.displayName,
            lastKnownState: "S2_REQUEST_PENDING",
          });
          setFlowState("S2_REQUEST_PENDING");
          setStatusMessage("Waiting for host approval.");
          return;
        }

        if (isRevokedTrustStatus(trust.status) || trust.status === "Unknown") {
          if (stored) {
            clearLanClientPairing();
          }
          setPairingRequest(null);
          setPairingStatus(null);
          setTerminalId("");
          setHandshakeSessionId("");
          setFlowState(trust.status === "Unknown" ? "S1_UNPAIRED" : "S_REVOKED");
          setStatusMessage(
            trust.status === "Unknown"
              ? "Previous pairing request is no longer active on the host. Request access again."
              : "Pairing revoked or expired. Re-pair this device.",
          );
          return;
        }
      } catch (error) {
        if (isNetworkFailure(error)) {
          if (stored && hasPairedTerminalMaterial(stored)) {
            setTerminalId(stored.terminalId);
            setHandshakeSessionId(stored.handshakeSessionId);
            setFlowState("S_OFFLINE");
            setTrustProbeResult("offline");
            setErrorMessage("Host unavailable. Saved pairing kept.");
            return;
          }

          if (stored?.requestId) {
            setFlowState("S_OFFLINE");
            setTrustProbeResult("offline");
            setErrorMessage("Host unavailable. Pending join request kept.");
            return;
          }
        }

        if (stored) {
          clearLanClientPairing();
        }
        setFlowState("S_REVOKED");
        setStatusMessage("Pairing revoked or expired. Re-pair this device.");
      }
    },
    [applyTrustedTerminal, hostBaseUrl, persistLanRecord, restoreOperatorSession],
  );

  useEffect(() => {
    if (!hostAddressConfirmed || restoreAttemptedRef.current) {
      return;
    }

    const normalizedHost = normalizeHostBaseUrl(hostBaseUrl);
    if (!normalizedHost) {
      setFlowState("S0_NO_HOST");
      return;
    }

    restoreAttemptedRef.current = true;
    const stored = readLanClientPairing("remote-pos", normalizedHost);
    setStorageRestored(Boolean(stored));
    void resolveTrustStatus(stored);
  }, [hostAddressConfirmed, hostBaseUrl, resolveTrustStatus]);

  const run = useCallback(
    async <T,>(action: RemotePosBusyAction, work: () => Promise<T>): Promise<T | null> => {
      setBusy(action);
      setErrorMessage(null);
      try {
        return await work();
      } catch (error) {
        const message = describeError(error);
        setErrorMessage(message);
        if (error instanceof ApiError && error.status === 401) {
          resetSession();
          if (isPaired) {
            setFlowState("S5_REMOTE_POS_OPERATOR_AUTH");
          }
        }
        return null;
      } finally {
        setBusy("idle");
      }
    },
    [isPaired, resetSession],
  );

  const confirmHostAddress = useCallback(() => {
    restoreAttemptedRef.current = false;
    clearPairing();
    setHostAddressConfirmed(true);
    setStatusMessage("Use the host LAN address shown on the host. Do not use localhost from another device.");
  }, [clearPairing]);

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
        setFlowState("S2_REQUEST_PENDING");
        persistLanRecord({
          requestId: result.requestId,
          displayName: result.deviceName,
          lastKnownState: "S2_REQUEST_PENDING",
        });
        setStatusMessage(result.message);
      }
    },
    [hostConnection, persistLanRecord, run],
  );

  const refreshPairingStatus = useCallback(async () => {
    if (!pairingRequest?.requestId) return;
    const result = await run("pairing", () => fetchRemotePosPairingStatus(hostConnection, pairingRequest.requestId));
    if (result) {
      setPairingStatus(result);
      setStatusMessage(result.message);
      if (result.pairingCodeRequired) {
        setFlowState("S3_HOST_APPROVED_NEEDS_ACTIVATION");
        persistLanRecord({ lastKnownState: "S3_HOST_APPROVED_NEEDS_ACTIVATION" });
      } else if (result.paired) {
        setFlowState("S4_TERMINAL_TRUSTED");
      } else {
        setFlowState("S2_REQUEST_PENDING");
        persistLanRecord({ lastKnownState: "S2_REQUEST_PENDING" });
      }
    }
  }, [hostConnection, pairingRequest?.requestId, persistLanRecord, run]);

  const activatePairing = useCallback(
    async (pairingCode: string) => {
      if (!pairingRequest?.requestId) return;
      const result = await run("pairing", () =>
        remotePosActivatePairing(hostConnection, { requestId: pairingRequest.requestId, pairingCode }),
      );
      if (result?.terminalId && result.handshakeSessionId) {
        setPairingActivation(result);
        applyTrustedTerminal(
          result.terminalId,
          result.handshakeSessionId,
          result.terminalName ?? pairingRequest.deviceName,
          pairingRequest.requestId,
          false,
          false,
        );
        setFlowState("S5_REMOTE_POS_OPERATOR_AUTH");
        setStatusMessage(result.message);
      }
    },
    [applyTrustedTerminal, hostConnection, pairingRequest, run],
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
        const issuedSessionToken = result.issued.sessionToken;
        if (!issuedSessionToken) {
          clearRemotePosOperatorSessionToken();
          setErrorMessage("Remote POS login did not return a session token. Please try signing in again.");
          return;
        }

        setSession(result.issued);
        setSessionToken(issuedSessionToken);
        setCapabilities(result.caps);
        writeRemotePosOperatorSessionToken(hostConnection.hostBaseUrl, issuedSessionToken);
        setFlowState("S6_REMOTE_POS_READY");
        setStatusMessage(`Signed in as ${result.issued.displayName ?? result.issued.username ?? "operator"}.`);
      }
    },
    [connection, hostConnection.hostBaseUrl, run],
  );

  const logout = useCallback(async () => {
    if (authedConnection) {
      await run("logout", async () => {
        await remotePosLogout(authedConnection).catch(() => undefined);
        return true;
      });
    }
    resetSession();
    setFlowState(isPaired ? "S5_REMOTE_POS_OPERATOR_AUTH" : "S1_UNPAIRED");
    setStatusMessage("Signed out of the remote POS host.");
  }, [authedConnection, isPaired, resetSession, run]);

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

  const effectiveFlowState = useMemo<RemotePosFlowState>(() => {
    if (isConnected) return "S6_REMOTE_POS_READY";
    if (isPaired && (flowState === "S4_TERMINAL_TRUSTED" || flowState === "S5_REMOTE_POS_OPERATOR_AUTH")) {
      return "S5_REMOTE_POS_OPERATOR_AUTH";
    }
    return flowState;
  }, [flowState, isConnected, isPaired]);

  return {
    hostBaseUrl,
    setHostBaseUrl,
    hostAddressConfirmed,
    confirmHostAddress,
    pairingRequest,
    pairingStatus,
    pairingActivation,
    isPaired,
    flowState: effectiveFlowState,
    pairingRestored,
    terminalIdSuffix,
    trustProbeResult,
    storageRestored,
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