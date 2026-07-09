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

export type RemotePosOperatorSessionPersistence = "local" | "session";

export type RemotePosOperatorSessionRecord = {
  version: 1;
  hostBaseUrl: string;
  sessionToken: string;
  storedAt: string;
  expiresAtUtc?: string;
  persistence: RemotePosOperatorSessionPersistence;
};

type LegacyRemotePosOperatorSessionRecord = {
  hostBaseUrl?: string;
  sessionToken?: string;
};

function parseRemotePosOperatorSessionRecord(raw: string): RemotePosOperatorSessionRecord | null {
  try {
    const parsed: unknown = JSON.parse(raw);
    if (!parsed || typeof parsed !== "object") {
      return null;
    }

    const record = parsed as Partial<RemotePosOperatorSessionRecord & LegacyRemotePosOperatorSessionRecord>;
    if (!record.sessionToken?.trim() || typeof record.hostBaseUrl !== "string") {
      return null;
    }

    if (record.version === 1 && (record.persistence === "local" || record.persistence === "session")) {
      return {
        version: 1,
        hostBaseUrl: normalizeLanHostBaseUrl(record.hostBaseUrl),
        sessionToken: record.sessionToken,
        storedAt: typeof record.storedAt === "string" ? record.storedAt : new Date().toISOString(),
        expiresAtUtc: typeof record.expiresAtUtc === "string" ? record.expiresAtUtc : undefined,
        persistence: record.persistence,
      };
    }

    return {
      version: 1,
      hostBaseUrl: normalizeLanHostBaseUrl(record.hostBaseUrl),
      sessionToken: record.sessionToken,
      storedAt: new Date().toISOString(),
      persistence: "session",
    };
  } catch {
    return null;
  }
}

function readRemotePosOperatorSessionRecordFromStorage(
  storage: Storage,
  hostBaseUrl: string,
): RemotePosOperatorSessionRecord | null {
  const raw = storage.getItem(REMOTE_POS_SESSION_TOKEN_KEY);
  if (!raw) {
    return null;
  }

  const record = parseRemotePosOperatorSessionRecord(raw);
  if (!record) {
    return null;
  }

  const normalizedHost = normalizeLanHostBaseUrl(hostBaseUrl);
  if (record.hostBaseUrl !== normalizedHost) {
    return null;
  }

  return record;
}

function removeRemotePosOperatorSessionRecordFromStorage(storage: Storage, hostBaseUrl?: string): void {
  if (!hostBaseUrl?.trim()) {
    storage.removeItem(REMOTE_POS_SESSION_TOKEN_KEY);
    return;
  }

  const raw = storage.getItem(REMOTE_POS_SESSION_TOKEN_KEY);
  if (!raw) {
    return;
  }

  const record = parseRemotePosOperatorSessionRecord(raw);
  const normalizedHost = normalizeLanHostBaseUrl(hostBaseUrl);
  if (!record || record.hostBaseUrl === normalizedHost) {
    storage.removeItem(REMOTE_POS_SESSION_TOKEN_KEY);
  }
}

export function normalizeLanHostBaseUrl(hostBaseUrl: string): string {
  return hostBaseUrl.trim().replace(/\/+$/, "");
}

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

    const normalizedHost = normalizeLanHostBaseUrl(hostBaseUrl);
    const storedHost = normalizeLanHostBaseUrl(parsed.hostBaseUrl);

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

  const localRecord = readRemotePosOperatorSessionRecordFromStorage(window.localStorage, hostBaseUrl);
  if (localRecord) {
    return localRecord.sessionToken;
  }

  const sessionRecord = readRemotePosOperatorSessionRecordFromStorage(window.sessionStorage, hostBaseUrl);
  return sessionRecord?.sessionToken ?? null;
}

export function hasRemotePosOperatorSessionToken(hostBaseUrl: string): boolean {
  return readRemotePosOperatorSessionToken(hostBaseUrl) !== null;
}

export function describeRemotePosOperatorSessionStorage(hostBaseUrl: string): RemotePosOperatorSessionPersistence | "none" {
  if (typeof window === "undefined") {
    return "none";
  }

  if (readRemotePosOperatorSessionRecordFromStorage(window.localStorage, hostBaseUrl)) {
    return "local";
  }

  if (readRemotePosOperatorSessionRecordFromStorage(window.sessionStorage, hostBaseUrl)) {
    return "session";
  }

  return "none";
}

export function writeRemotePosOperatorSessionToken(
  hostBaseUrl: string,
  sessionToken: string,
  options?: {
    persistence?: RemotePosOperatorSessionPersistence;
    expiresAtUtc?: string | null;
  },
): void {
  if (typeof window === "undefined") {
    return;
  }

  const persistence = options?.persistence ?? "local";
  const normalizedHost = normalizeLanHostBaseUrl(hostBaseUrl);
  const record: RemotePosOperatorSessionRecord = {
    version: 1,
    hostBaseUrl: normalizedHost,
    sessionToken,
    storedAt: new Date().toISOString(),
    persistence,
    ...(options?.expiresAtUtc?.trim() ? { expiresAtUtc: options.expiresAtUtc.trim() } : {}),
  };

  removeRemotePosOperatorSessionRecordFromStorage(window.localStorage, normalizedHost);
  removeRemotePosOperatorSessionRecordFromStorage(window.sessionStorage, normalizedHost);

  const targetStorage = persistence === "local" ? window.localStorage : window.sessionStorage;
  targetStorage.setItem(REMOTE_POS_SESSION_TOKEN_KEY, JSON.stringify(record));
}

export function clearRemotePosOperatorSessionToken(hostBaseUrl?: string): void {
  if (typeof window === "undefined") {
    return;
  }

  removeRemotePosOperatorSessionRecordFromStorage(window.localStorage, hostBaseUrl);
  removeRemotePosOperatorSessionRecordFromStorage(window.sessionStorage, hostBaseUrl);
}