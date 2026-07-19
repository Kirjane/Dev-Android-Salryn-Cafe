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
  sessionResilience?: RemoteReadOnlySessionResilience | null;
};

export type RemoteReadOnlyCapabilitiesResponse = {
  allowedCapabilities: RemoteReadOnlyCapability[];
  disabledMutations: string[];
  message: string;
};

export type RemoteReadOnlySessionResilience = {
  lifecycleState: string;
  guidance: string;
  readOnlyRefreshSafe: boolean;
  repairRequired: boolean;
  automaticRepairDisabled: boolean;
  staleDataWarning: string;
  denialCode?: string | null;
  denialMessage?: string | null;
};

export type RemoteReadOnlyTerminalStatus = {
  connectionStatus: RemoteReadOnlyConnectionStatus;
  terminalStatus: string;
  message: string;
  terminalId?: string | null;
  terminalName?: string | null;
  terminalRole?: string | null;
  sessionExpiresAtUtc?: string | null;
  sessionResilience?: RemoteReadOnlySessionResilience | null;
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

export type RemoteReadOnlyPreflightCheck = {
  code: string;
  status: string;
  category: string;
  message: string;
};

export type RemoteReadOnlyPreflight = {
  overallReadiness: string;
  message: string;
  readOnlyOnly: boolean;
  checks: RemoteReadOnlyPreflightCheck[];
  safeNextSteps: string[];
  blockedReasons: string[];
};

export type LocalLanControlTerminal = {
  terminalId: string;
  terminalName: string;
  terminalRole: string;
  status: string;
  trusted: boolean;
  trustState: string;
  registeredAtUtc: string;
  lastSeenAtUtc?: string | null;
  disabledAtUtc?: string | null;
  revokedAtUtc?: string | null;
};

export type RemotePosLimits = {
  maxPairedTerminals: number;
  maxActiveSessions: number;
  hardCap: number;
  currentPairedCount: number;
  currentActiveSessions: number;
  remainingPairingCapacity: number;
  remainingActiveSessionCapacity: number;
  hostLanEntitled?: boolean;
  hostLicenseId?: string | null;
  storeId?: string | null;
  licenseMessage?: string | null;
  licenseContactEmail?: string | null;
};

export type LocalLanControlRuntimeStatus = {
  configuredBindAddress: string | null;
  port: number;
  actualListenUrls: string[];
  lanTransportEnabled: boolean;
  clientUrl: string | null;
  clientUrlReady: boolean;
  readinessState: string;
  restartRequired: boolean;
  bindIpOnHost: boolean;
  problem: string | null;
  remediation: string | null;
  hostLocalUrl: string;
  firewallGuidance: string;
  configuredBindAddressRaw?: string | null;
  configuredBindAddressNormalized?: string | null;
  configuredBindAddressRepairSuggestion?: string | null;
  detectedLanListenIps?: string[] | null;
  configuredBindMalformed?: boolean;
  detectedHostIpv4Addresses?: string[] | null;
};

export type LocalLanControlStatus = {
  localOperatorOnly: boolean;
  remoteTransportEnabled: boolean;
  remotePosRuntimeEnabled: boolean;
  hostLocalUrl: string;
  hostLanUrl: string;
  safety: string;
  bindMode: string;
  configuredBindAddress: string;
  bindPolicy: RemoteReadOnlyBindPolicy;
  terminalVisibilityAvailable: boolean;
  terminalVisibilityMessage: string;
  terminals: LocalLanControlTerminal[];
  remotePosLimits?: RemotePosLimits | null;
  warnings: string[];
  message: string;
};

export type LocalLanControlTerminalAction = {
  terminalId: string;
  status: string;
  trustState: string;
  message: string;
};

export type RemotePosPendingJoinRequest = {
  requestId: string;
  deviceName: string;
  observedOrigin: string;
  pairingState: string;
  requestedAtUtc: string;
  expiresAtUtc: string;
  pairingCodeExpiresAtUtc?: string | null;
};

export type RemotePosPairingHostStatus = {
  joinCodeIssued: boolean;
  joinCodeExpiresAtUtc?: string | null;
  pendingRequestCount: number;
  pendingRequests: RemotePosPendingJoinRequest[];
  message: string;
};

export type RemotePosJoinCodeRotateResponse = {
  joinCode: string;
  issuedAtUtc: string;
  expiresAtUtc: string;
  message: string;
};

export type RemotePosApproveJoinResponse = {
  requestId: string;
  deviceName: string;
  pairingCode: string;
  pairingCodeExpiresAtUtc: string;
  message: string;
};

export type RemotePosRejectJoinResponse = {
  requestId: string;
  pairingState: string;
  message: string;
};
