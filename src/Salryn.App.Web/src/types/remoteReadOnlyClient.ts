export type RemoteReadOnlyConnectionStatus =
  | "NotConfigured"
  | "NotPaired"
  | "HandshakeRequired"
  | "ConnectedReadOnly"
  | "Expired"
  | "Blocked"
  | "Error";

export type RemoteReadOnlyCapability =
  | "Status read"
  | "Product read"
  | "Dashboard read"
  | "Terminal status read";

export type RemoteReadOnlyShellState = {
  connectionStatus: RemoteReadOnlyConnectionStatus;
  storeId?: string;
  hostId?: string;
  terminalId?: string;
  terminalName?: string;
  terminalRole?: string;
  pairedState?: string;
  sessionExpiresAt?: string;
  message: string;
  capabilities: RemoteReadOnlyCapability[];
  disabledMutations: string[];
};
