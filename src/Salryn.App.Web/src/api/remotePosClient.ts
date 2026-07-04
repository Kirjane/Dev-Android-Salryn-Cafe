import { readJson } from "./http";
import type {
  RemotePosCapabilities,
  RemotePosCart,
  RemotePosCheckoutResult,
  RemotePosSession,
  RemotePosShift,
} from "../types/remotePosClient";

// LAN-M16: client for the host-owned remote POS command surface (/api/remote-pos).
//
// This surface is SEPARATE from the read-only bridge. Every request carries terminal identity
// and handshake proof headers; authenticated commands also carry the opaque operator session
// token issued by the host at login. Cookies are NOT used for LAN trust here.
//
// Host names / addresses are supplied by the operator (the remote client must be pointed at the
// POS host's explicit LAN address). This module never performs discovery or network scanning.

export const remotePosHeaderNames = {
  terminalId: "X-Salryn-Terminal-Id",
  handshakeSessionId: "X-Salryn-Handshake-Session-Id",
  operatorSession: "X-Salryn-Remote-Pos-Session",
} as const;

export type RemotePosConnection = {
  // Optional explicit base URL for the POS host (e.g. "http://192.168.1.10:5005"). When empty the
  // client uses same-origin requests, which is only meaningful when this app is served BY the host.
  hostBaseUrl: string;
  terminalId: string;
  handshakeSessionId: string;
};

export type RemotePosAuthedConnection = RemotePosConnection & {
  sessionToken: string;
};

export const remotePosClientWarnings = [
  "This is a remote POS client. It connects to the POS host's explicit LAN address; it does not open the host database.",
  "Opening this app on localhost of a second computer makes THAT computer its own local host, not a client of the main POS host.",
  "Installing the same app on two computers does not synchronize data. Remote clients must connect to the POS host LAN address.",
  "Terminal identity and handshake proof are required in addition to a host-issued operator session. Cookies are not accepted as LAN trust.",
  "Host-local LAN control, terminal trust management, backup/recovery, and settings are never available over LAN.",
] as const;

function buildUrl(connection: RemotePosConnection, path: string): string {
  const base = connection.hostBaseUrl.trim().replace(/\/+$/, "");
  return base.length > 0 ? `${base}${path}` : path;
}

function trustHeaders(connection: RemotePosConnection): Record<string, string> {
  return {
    [remotePosHeaderNames.terminalId]: connection.terminalId.trim(),
    [remotePosHeaderNames.handshakeSessionId]: connection.handshakeSessionId.trim(),
  };
}

function authHeaders(connection: RemotePosAuthedConnection): Record<string, string> {
  return {
    ...trustHeaders(connection),
    [remotePosHeaderNames.operatorSession]: connection.sessionToken,
  };
}

async function getJson<T>(url: string, headers: Record<string, string>): Promise<T> {
  const response = await fetch(url, { method: "GET", headers });
  return readJson<T>(response);
}

async function postJson<T>(url: string, headers: Record<string, string>, body?: unknown): Promise<T> {
  const response = await fetch(url, {
    method: "POST",
    headers: { ...headers, "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  return readJson<T>(response);
}

// ---- Session ------------------------------------------------------------------------------

export async function remotePosLogin(
  connection: RemotePosConnection,
  credentials: { username: string; password: string },
): Promise<RemotePosSession> {
  return postJson<RemotePosSession>(buildUrl(connection, "/api/remote-pos/session/login"), trustHeaders(connection), {
    username: credentials.username,
    password: credentials.password,
  });
}

export async function remotePosLogout(connection: RemotePosAuthedConnection): Promise<void> {
  await postJson(buildUrl(connection, "/api/remote-pos/session/logout"), authHeaders(connection));
}

export async function fetchRemotePosSession(connection: RemotePosAuthedConnection): Promise<RemotePosSession> {
  return getJson<RemotePosSession>(buildUrl(connection, "/api/remote-pos/session"), authHeaders(connection));
}

export async function fetchRemotePosCapabilities(
  connection: RemotePosAuthedConnection,
): Promise<RemotePosCapabilities> {
  return getJson<RemotePosCapabilities>(buildUrl(connection, "/api/remote-pos/capabilities"), authHeaders(connection));
}

// ---- Cart ---------------------------------------------------------------------------------

export async function remotePosStartCart(connection: RemotePosAuthedConnection): Promise<RemotePosCart> {
  return postJson<RemotePosCart>(buildUrl(connection, "/api/remote-pos/cart/start"), authHeaders(connection));
}

export async function fetchRemotePosCart(connection: RemotePosAuthedConnection): Promise<RemotePosCart> {
  return getJson<RemotePosCart>(buildUrl(connection, "/api/remote-pos/cart"), authHeaders(connection));
}

export async function remotePosAddLine(
  connection: RemotePosAuthedConnection,
  line: { productId: string; quantityX1000: number },
): Promise<RemotePosCart> {
  return postJson<RemotePosCart>(buildUrl(connection, "/api/remote-pos/cart/add-line"), authHeaders(connection), line);
}

export async function remotePosUpdateLine(
  connection: RemotePosAuthedConnection,
  line: { productId: string; quantityX1000: number },
): Promise<RemotePosCart> {
  return postJson<RemotePosCart>(buildUrl(connection, "/api/remote-pos/cart/update-line"), authHeaders(connection), line);
}

export async function remotePosRemoveLine(
  connection: RemotePosAuthedConnection,
  productId: string,
): Promise<RemotePosCart> {
  return postJson<RemotePosCart>(buildUrl(connection, "/api/remote-pos/cart/remove-line"), authHeaders(connection), {
    productId,
  });
}

export async function remotePosClearCart(connection: RemotePosAuthedConnection): Promise<RemotePosCart> {
  return postJson<RemotePosCart>(buildUrl(connection, "/api/remote-pos/cart/clear"), authHeaders(connection));
}

// ---- Shift --------------------------------------------------------------------------------

export async function remotePosOpenShift(
  connection: RemotePosAuthedConnection,
  openingCashCents: number,
): Promise<RemotePosShift> {
  return postJson<RemotePosShift>(buildUrl(connection, "/api/remote-pos/shift/open"), authHeaders(connection), {
    openingCashCents,
  });
}

export async function fetchRemotePosShift(connection: RemotePosAuthedConnection): Promise<RemotePosShift> {
  return getJson<RemotePosShift>(buildUrl(connection, "/api/remote-pos/shift"), authHeaders(connection));
}

// ---- Checkout -----------------------------------------------------------------------------

export async function remotePosSubmitCheckout(
  connection: RemotePosAuthedConnection,
  request: { clientRequestId: string; payments: { method: string; amountCents: number }[] },
): Promise<RemotePosCheckoutResult> {
  const response = await fetch(buildUrl(connection, "/api/remote-pos/checkout/submit"), {
    method: "POST",
    headers: { ...authHeaders(connection), "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });

  // Checkout intentionally returns 200 (completed/duplicate) or 4xx/5xx (conflict/failure) while
  // still carrying a structured RemotePosCheckoutResult body. Read the body either way so the UI
  // can present conflict codes (StockChanged, DuplicateRequest, ...) without throwing.
  const body = (await response.json().catch(() => null)) as RemotePosCheckoutResult | null;
  if (body && typeof body.status === "string") {
    return body;
  }

  return {
    status: "RetryableUnknown",
    clientRequestId: request.clientRequestId,
    receiptNumber: null,
    saleId: null,
    totalCents: null,
    changeCents: null,
    duplicate: false,
    message: `Checkout request failed (${response.status}).`,
  };
}

export async function fetchRemotePosCheckoutStatus(
  connection: RemotePosAuthedConnection,
  clientRequestId: string,
): Promise<RemotePosCheckoutResult> {
  return getJson<RemotePosCheckoutResult>(
    buildUrl(connection, `/api/remote-pos/checkout/status/${encodeURIComponent(clientRequestId)}`),
    authHeaders(connection),
  );
}
