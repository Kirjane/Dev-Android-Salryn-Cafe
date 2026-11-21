import { HostApiError } from "./hostClient";
import type {
  CentralAdminSession,
  CentralDeviceRow,
  CentralHealthSnapshot,
  CentralInviteTicket,
  CentralOwnerApi,
  CentralQuarantineRow,
} from "../backoffice/types";

const PRODUCT_ID = "Salryn.Cafe";
const CENTRAL_SESSION_KEY = "salryn.cafe.central.admin.session.v1";
const CENTRAL_ORIGIN_KEY = "salryn.cafe.central.origin.v1";

export function normalizeCentralBaseUrl(rawUrl: string): string {
  const trimmed = rawUrl.trim().replace(/\/+$/, "");
  if (!trimmed) {
    return "";
  }
  const parsed = new URL(trimmed);
  const isLoopback = parsed.hostname === "127.0.0.1" || parsed.hostname === "localhost";
  if (parsed.protocol !== "https:" && !isLoopback) {
    throw new Error("Central Owner Portal requires HTTPS for non-loopback servers.");
  }
  return `${parsed.origin}${parsed.pathname.replace(/\/+$/, "")}`;
}

export function loadSavedCentralOrigin(): string {
  try {
    return window.localStorage.getItem(CENTRAL_ORIGIN_KEY) ?? "";
  } catch {
    return "";
  }
}

export function saveCentralOrigin(origin: string): void {
  try {
    window.localStorage.setItem(CENTRAL_ORIGIN_KEY, origin);
  } catch {
    // Ignore storage failures cleanly.
  }
}

export function loadSavedCentralSession(): { origin: string; auth: CentralAdminSession } | null {
  try {
    const raw = window.localStorage.getItem(CENTRAL_SESSION_KEY);
    return raw ? (JSON.parse(raw) as { origin: string; auth: CentralAdminSession }) : null;
  } catch {
    return null;
  }
}

export function saveCentralSession(value: { origin: string; auth: CentralAdminSession } | null): void {
  try {
    if (!value) {
      window.localStorage.removeItem(CENTRAL_SESSION_KEY);
    } else {
      window.localStorage.setItem(CENTRAL_SESSION_KEY, JSON.stringify(value));
    }
  } catch {
    // Ignore storage failures cleanly.
  }
}

async function centralRequest<T>(
  baseUrl: string,
  path: string,
  options: { method?: "GET" | "POST"; accessToken?: string; body?: unknown; includeProductHeader?: boolean } = {},
): Promise<T> {
  const normalized = normalizeCentralBaseUrl(baseUrl);
  const url = `${normalized}${path}`;
  const headers: Record<string, string> = {};
  if (options.includeProductHeader !== false) {
    headers["X-Salryn-Product-Id"] = PRODUCT_ID;
  }
  if (options.accessToken) {
    headers["Authorization"] = `Bearer ${options.accessToken}`;
  }
  if (options.body !== undefined) {
    headers["Content-Type"] = "application/json";
  }

  const response = await fetch(url, {
    method: options.method ?? (options.body !== undefined ? "POST" : "GET"),
    headers,
    body: options.body !== undefined ? JSON.stringify(options.body) : undefined,
  });

  const text = await response.text();
  let parsed: Record<string, unknown> | unknown = {};
  if (text.length > 0) {
    try {
      parsed = JSON.parse(text) as Record<string, unknown>;
    } catch {
      throw new HostApiError(
        response.ok
          ? `Central server returned invalid non-JSON response (HTTP ${response.status}).`
          : `Central server returned HTTP ${response.status}`,
        response.status,
      );
    }
  }

  if (!response.ok) {
    const obj = (typeof parsed === "object" && parsed !== null ? parsed : {}) as Record<string, unknown>;
    const msg =
      (typeof obj.errorMessage === "string" && obj.errorMessage) ||
      (typeof obj.error === "string" && obj.error) ||
      `Central server returned HTTP ${response.status}`;
    const code = typeof obj.errorCode === "string" ? obj.errorCode : typeof obj.code === "string" ? obj.code : undefined;
    throw new HostApiError(msg, response.status, code);
  }

  return parsed as T;
}

export function createLiveCentralOwnerApi(): CentralOwnerApi {
  return {
    getHealth: (baseUrl) =>
      centralRequest<CentralHealthSnapshot>(baseUrl, "/api/sync/v1/health", { includeProductHeader: false }),
    login: (baseUrl, username, password) =>
      centralRequest<CentralAdminSession>(baseUrl, "/api/sync/v1/admin/auth/login", {
        method: "POST",
        body: { username, password },
      }),
    logout: (baseUrl, accessToken) =>
      centralRequest<void>(baseUrl, "/api/sync/v1/admin/auth/logout", {
        method: "POST",
        accessToken,
        body: {},
      }),
    listDevices: (baseUrl, accessToken) =>
      centralRequest<CentralDeviceRow[]>(baseUrl, "/api/sync/v1/admin/devices", { accessToken }),
    createInvite: (baseUrl, accessToken, deviceRole) =>
      centralRequest<CentralInviteTicket>(baseUrl, "/api/sync/v1/admin/devices/invites", {
        method: "POST",
        accessToken,
        body: { deviceRole },
      }),
    approveDevice: (baseUrl, accessToken, deviceId) =>
      centralRequest<CentralDeviceRow>(baseUrl, `/api/sync/v1/admin/devices/${deviceId}/approve`, {
        method: "POST",
        accessToken,
        body: {},
      }),
    revokeDevice: (baseUrl, accessToken, deviceId, reason) =>
      centralRequest<CentralDeviceRow>(baseUrl, `/api/sync/v1/admin/devices/${deviceId}/revoke`, {
        method: "POST",
        accessToken,
        body: { reason: reason ?? "Revoked from Central Owner Portal" },
      }),
    listQuarantine: (baseUrl, accessToken) =>
      centralRequest<CentralQuarantineRow[]>(baseUrl, "/api/sync/v1/admin/quarantine", { accessToken }),
    reviewQuarantine: (baseUrl, accessToken, quarantineId, reviewStatus, reviewNote) =>
      centralRequest<CentralQuarantineRow>(baseUrl, `/api/sync/v1/admin/quarantine/${quarantineId}/review`, {
        method: "POST",
        accessToken,
        body: { reviewStatus, reviewNote },
      }),
  };
}
