export type RemoteReadOnlyConnectionStatus =
  | "NotConfigured"
  | "NotPaired"
  | "HandshakeRequired"
  | "ConnectedReadOnly"
  | "Expired"
  | "Blocked"
  | "Error"
  | "LocalOnlyReadMode";

export type RemoteReadOnlyCapability =
  | "Status read"
  | "Product read"
  | "Dashboard read"
  | "Terminal status read";

export type RemoteReadOnlyBindPolicy = {
  defaultMode: string;
  effectiveMode: string;
  disabledByDefault: boolean;
  localhostOnly: boolean;
  explicitHostBindRequired: boolean;
  specificIpOnly: boolean;
  noWildcardBind?: boolean;
  noDiscovery?: boolean;
  message: string;
};

export type RemoteReadOnlyShellState = {
  connectionStatus: RemoteReadOnlyConnectionStatus;
  mode: string;
  storeId?: string | null;
  hostId?: string | null;
  terminalId?: string | null;
  terminalName?: string | null;
  terminalRole?: string | null;
  pairedState?: string | null;
  sessionExpiresAtUtc?: string | null;
  sessionExpiresAt?: string | null;
  message: string;
  bindPolicy: RemoteReadOnlyBindPolicy;
  allowedCapabilities: RemoteReadOnlyCapability[];
  capabilities: RemoteReadOnlyCapability[];
  disabledMutations: string[];
};

export type RemoteReadOnlyCapabilitiesResponse = {
  allowedCapabilities: RemoteReadOnlyCapability[];
  disabledMutations: string[];
  message: string;
};

export type RemoteReadOnlyTerminalStatus = {
  connectionStatus: RemoteReadOnlyConnectionStatus;
  terminalStatus: string;
  message: string;
  terminalId?: string | null;
  terminalName?: string | null;
  terminalRole?: string | null;
  sessionExpiresAtUtc?: string | null;
};

export type RemoteReadOnlyAuditItem = {
  occurredAtUtc: string;
  category: string;
  message: string;
  scope: string;
};

export type RemoteReadOnlyAuditVisibility = {
  status: string;
  message: string;
  page: number;
  pageSize: number;
  totalCount: number;
  items: RemoteReadOnlyAuditItem[];
};

export type RemoteReadOnlyDiagnosticItem = {
  code: string;
  category: string;
  severity: string;
  message: string;
};

export type RemoteReadOnlyDiagnostics = {
  mode: string;
  overallStatus: string;
  message: string;
  readOnlyOnly: boolean;
  lanDisabledByDefault: boolean;
  terminalIdentityRequired: boolean;
  handshakeProofRequired: boolean;
  cookieTrustAcceptedForRemoteLan: boolean;
  corsEnabled: boolean;
  auditPolicyAvailable: boolean;
  lastDenialCode?: string | null;
  lastDenialMessage?: string | null;
  items: RemoteReadOnlyDiagnosticItem[];
};
