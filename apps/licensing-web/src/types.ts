export type IssuerAdminUser = {
  id: string;
  username: string;
  displayName: string;
  role: string;
  isActive: boolean;
  createdAtUtc: string;
};

export type IssuerSessionInfo = {
  isAuthenticated: boolean;
  csrfToken: string | null;
  user: IssuerAdminUser | null;
  permissions?: string[];
};

export type IssuerAuditChainVerification = {
  isValid: boolean;
  checkedEntries: number;
  firstBrokenId: number | null;
  message: string;
};

export type SignerStatusSnapshot = {
  isConfigured: boolean;
  canIssue: boolean;
  signerMode: string;
  publicKeySpkiSha256: string | null;
  trustedRuntimeSpkiSha256: string;
  matchesTrustedRuntimeKey: boolean;
  message: string;
};

export type IssuerDashboardSummary = {
  draftCount: number;
  awaitingApprovalCount: number;
  approvedCount: number;
  issuedCount: number;
  expiredIssuedCount: number;
  supersededCount: number;
  rejectedCount: number;
  customerCount: number;
  storeCount: number;
  signer: SignerStatusSnapshot;
};

export type IssuerMetadata = {
  products: string[];
  licenseTypes: string[];
  editions: string[];
  allFeatures: string[];
  terminalAllowedFeatures: string[];
  roles?: string[];
  independentApprovalEnforced?: boolean;
  signer: SignerStatusSnapshot;
};

export type IssuerCustomerRecord = {
  id: string;
  customerName: string;
  contactEmail: string | null;
  notes: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type IssuerStoreRecord = {
  id: string;
  customerId: string;
  storeCode: string;
  storeName: string;
  productId: string;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type IssuerLicenseRequestRecord = {
  id: string;
  licenseId: string;
  idempotencyKey: string | null;
  status: string;
  customerId: string;
  customerName: string;
  storeId: string;
  storeCode: string;
  storeName: string;
  applicationId: string;
  licenseType: string;
  edition: string;
  machineBinding: string;
  features: string[];
  allowedLanTerminals: number | null;
  hostLicenseId: string | null;
  terminalId: string | null;
  issuedAtUtc: string;
  expiresAtUtc: string | null;
  supportExpiresAtUtc: string | null;
  updatesUntilUtc: string | null;
  supersedesRequestId: string | null;
  canonicalSigningPayload: string;
  signedLicenseJson: string | null;
  signedLicenseSha256: string | null;
  signerKeySpkiSha256: string | null;
  signerMode: string | null;
  verifierPassed: boolean;
  reviewNote: string | null;
  createdBy: string;
  approvedBy: string | null;
  issuedBy: string | null;
  createdAtUtc: string;
  approvedAtUtc: string | null;
  issuedAtServerUtc: string | null;
};

export type IssuerAuditEntry = {
  id: number;
  actorUsername: string;
  actorRole: string;
  action: string;
  entityType: string;
  entityId: string;
  detailJson: string;
  createdAtUtc: string;
  prevHash?: string;
  entryHash?: string;
};

export type CreateLicenseDraftInput = {
  customerId: string;
  storeId: string;
  applicationId: string;
  licenseType: string;
  edition: string;
  machineBinding: string;
  features: string[];
  allowedLanTerminals: number | null;
  hostLicenseId: string | null;
  terminalId: string | null;
  expiresAtUtc: string | null;
  supportExpiresAtUtc: string | null;
  updatesUntilUtc: string | null;
  idempotencyKey?: string;
};

export type DraftCreateResponse = {
  request: IssuerLicenseRequestRecord;
  warnings: string[];
  isReplay: boolean;
};

export type IssueResponse = {
  request: IssuerLicenseRequestRecord;
  alreadyIssued: boolean;
};
