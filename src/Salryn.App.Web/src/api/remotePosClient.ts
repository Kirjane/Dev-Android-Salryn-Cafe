import { readJson } from "./http";
import type {
  RemotePosCapabilities,
  RemotePosCart,
  RemotePosCheckoutResult,
  RemotePosJoinResponse,
  RemotePosPairingActivation,
  RemotePosPairingStatus,
  RemotePosSession,
  RemotePosShift,
} from "../types/remotePosClient";

// LAN-M16: client for the host-owned remote POS command surface (/api/remote-pos).
//
// LAN-M16-QA-B: field clients no longer type terminal ids or handshake session ids during the
// normal path. Pairing approves the device first; login approves the person second.

export const remotePosHeaderNames = {
  terminalId: "X-Salryn-Terminal-Id",
  handshakeSessionId: "X-Salryn-Handshake-Session-Id",
  operatorSession: "X-Salryn-Remote-Pos-Session",
} as const;

export type RemotePosHostConnection = {
  hostBaseUrl: string;
};

export type RemotePosConnection = RemotePosHostConnection & {
  terminalId: string;
  handshakeSessionId: string;
};

export type RemotePosAuthedConnection = RemotePosConnection & {
  sessionToken: string;
};

export const remotePosClientWarnings = [
  "This device is a Remote POS client.",
  "The host owns the database and sales.",
  "Use the host LAN address shown on the host.",
  "Do not use localhost from another device.",
  "Ask the host operator for the join code.",
  "Wait for the host to approve this device.",
  "After pairing, sign in as Cashier or Manager.",
  "Admin and Owner accounts are host-local only for this build.",
] as const;

function buildUrl(connection: RemotePosHostConnection, path: string): string {
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

async function getJson<T>(url: string, headers: Record<string, string> = {}): Promise<T> {
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

// ---- Pairing -------------------------------------------------------------------------------

export async function fetchRemotePosPairingStatus(
  connection: RemotePosHostConnection,
  requestId?: string | null,
): Promise<RemotePosPairingStatus> {
  const query = requestId ? `?requestId=${encodeURIComponent(requestId)}` : "";
  return getJson<RemotePosPairingStatus>(buildUrl(connection, `/api/remote-pos/pairing/status${query}`));
}

export async function remotePosRequestPairing(
  connection: RemotePosHostConnection,
  request: { joinCode: string; deviceName: string },
): Promise<RemotePosJoinResponse> {
  return postJson<RemotePosJoinResponse>(buildUrl(connection, "/api/remote-pos/pairing/request"), {}, request);
}

export async function remotePosActivatePairing(
  connection: RemotePosHostConnection,
  request: { requestId: string; pairingCode: string },
): Promise<RemotePosPairingActivation> {
  return postJson<RemotePosPairingActivation>(buildUrl(connection, "/api/remote-pos/pairing/activate"), {}, request);
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
