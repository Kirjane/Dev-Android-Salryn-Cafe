export const LAN_CLIENT_STORAGE_KEY = "salryn.lanClient.v1";

export type LanClientMode = "remote-pos" | "remote-readonly";

export type LanClientLastKnownState =
  | "S1_UNPAIRED"
  | "S2_REQUEST_PENDING"
  | "S3_HOST_APPROVED_NEEDS_ACTIVATION"
  | "S4_TERMINAL_TRUSTED"
  | "S_REVOKED"
  | "S_OFFLINE";

export type LanClientPairingRecord = {
  version: 1;
  mode: LanClientMode;
  hostBaseUrl: string;
  terminalId?: string;
  handshakeSessionId?: string;
  displayName?: string;
  requestId?: string;
  pairedAt?: string;
  lastSeenAt: string;
  lastKnownState?: LanClientLastKnownState;
};

const REMOTE_POS_SESSION_TOKEN_KEY = "salryn.remotePos.operatorSession.v1";

function isRecord(value: unknown): value is LanClientPairingRecord {
  if (!value || typeof value !== "object") {
    return false;
  }

  const record = value as Partial<LanClientPairingRecord>;
  return (
    record.version === 1 &&
    (record.mode === "remote-pos" || record.mode === "remote-readonly") &&
    typeof record.hostBaseUrl === "string" &&
    typeof record.lastSeenAt === "string"
  );
}

export function readLanClientPairing(mode: LanClientMode, hostBaseUrl: string): LanClientPairingRecord | null {
  if (typeof window === "undefined") {
    return null;
  }

  try {
    const raw = window.localStorage.getItem(LAN_CLIENT_STORAGE_KEY);
    if (!raw) {
      return null;
    }

    const parsed: unknown = JSON.parse(raw);
    if (!isRecord(parsed)) {
      return null;
    }

    const normalizedHost = hostBaseUrl.trim().replace(/\/+$/, "");
    const storedHost = parsed.hostBaseUrl.trim().replace(/\/+$/, "");

    if (parsed.mode !== mode || storedHost !== normalizedHost) {
      return null;
    }

    return parsed;
  } catch {
    return null;
  }
}

export function writeLanClientPairing(record: LanClientPairingRecord): void {
  if (typeof window === "undefined") {
    return;
  }

  window.localStorage.setItem(LAN_CLIENT_STORAGE_KEY, JSON.stringify(record));
}

export function touchLanClientPairing(mode: LanClientMode, hostBaseUrl: string): LanClientPairingRecord | null {
  const existing = readLanClientPairing(mode, hostBaseUrl);
  if (!existing) {
    return null;
  }

  const updated: LanClientPairingRecord = {
    ...existing,
    lastSeenAt: new Date().toISOString(),
  };
  writeLanClientPairing(updated);
  return updated;
}

export function clearLanClientPairing(): void {
  if (typeof window === "undefined") {
    return;
  }

  window.localStorage.removeItem(LAN_CLIENT_STORAGE_KEY);
}

export function hasPairedTerminalMaterial(record: LanClientPairingRecord | null): record is LanClientPairingRecord & {
  terminalId: string;
  handshakeSessionId: string;
} {
  return Boolean(record?.terminalId?.trim() && record?.handshakeSessionId?.trim());
}

export function readRemotePosOperatorSessionToken(hostBaseUrl: string): string | null {
  if (typeof window === "undefined") {
    return null;
  }

  try {
    const raw = window.sessionStorage.getItem(REMOTE_POS_SESSION_TOKEN_KEY);
    if (!raw) {
      return null;
    }

    const parsed = JSON.parse(raw) as { hostBaseUrl?: string; sessionToken?: string };
    const normalizedHost = hostBaseUrl.trim().replace(/\/+$/, "");
    const storedHost = parsed.hostBaseUrl?.trim().replace(/\/+$/, "");
    if (!parsed.sessionToken?.trim() || storedHost !== normalizedHost) {
      return null;
    }

    return parsed.sessionToken;
  } catch {
    return null;
  }
}

export function writeRemotePosOperatorSessionToken(hostBaseUrl: string, sessionToken: string): void {
  if (typeof window === "undefined") {
    return;
  }

  window.sessionStorage.setItem(
    REMOTE_POS_SESSION_TOKEN_KEY,
    JSON.stringify({
      hostBaseUrl: hostBaseUrl.trim().replace(/\/+$/, ""),
      sessionToken,
    }),
  );
}

export function clearRemotePosOperatorSessionToken(): void {
  if (typeof window === "undefined") {
    return;
  }

  window.sessionStorage.removeItem(REMOTE_POS_SESSION_TOKEN_KEY);
}