export const LAN_CLIENT_STORAGE_KEY = "salryn.lanClient.v1";

export type LanClientMode = "remote-pos" | "remote-readonly";

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
};

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