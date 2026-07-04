// LAN-M16: types for the host-owned remote POS command surface (/api/remote-pos). These mirror
// the C# contracts in Salryn.Api.Local/RemotePos/RemotePosContracts.cs. Structural only; no
// runtime validation. Secrets (session token) are only present on the login response.

export type RemotePosRole = "Cashier" | "Manager" | "Admin" | string;

export type RemotePosSession = {
  sessionState: string;
  sessionId: string | null;
  sessionToken: string | null;
  terminalId: string | null;
  hostId: string | null;
  storeId: string | null;
  userId: string | null;
  username: string | null;
  displayName: string | null;
  roleName: RemotePosRole | null;
  permissions: string[];
  issuedAtUtc: string | null;
  expiresAtUtc: string | null;
  remotePosRuntimeEnabled: boolean;
};

export type RemotePosCapability = {
  command: string;
  category: string;
  allowed: boolean;
  decision: string;
  description: string;
};

export type RemotePosCapabilities = {
  roleName: string;
  capabilities: RemotePosCapability[];
  hostLocalOnlyActions: string[];
  warnings: string[];
};

export type RemotePosCartLine = {
  productId: string;
  productName: string;
  unitName: string;
  quantityX1000: number;
  unitPriceCents: number;
  lineTotalCents: number;
};

export type RemotePosCart = {
  cartId: string;
  sessionId: string;
  lines: RemotePosCartLine[];
  subtotalCents: number;
  message: string;
};

export type RemotePosShift = {
  shiftState: string;
  shiftId: string | null;
  openedAtUtc: string | null;
  message: string;
};

export type RemotePosCheckoutResult = {
  status: string;
  clientRequestId: string;
  receiptNumber: string | null;
  saleId: string | null;
  totalCents: number | null;
  changeCents: number | null;
  duplicate: boolean;
  message: string;
};

// Terminal identity + handshake proof headers required for every remote POS request.
export type RemotePosTrustHeaders = {
  terminalId: string;
  handshakeSessionId: string;
};
