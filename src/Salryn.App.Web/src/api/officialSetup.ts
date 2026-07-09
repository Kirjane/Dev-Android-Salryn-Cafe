import { readJson } from "./http";

export type OfficialSetupSnapshot = {
  contractVersion: number;
  liveOfficialPostingAllowed: boolean;
  summary: string;
  warnings: string[];
  business: {
    registeredTaxpayerName: string;
    businessName: string | null;
    registeredAddress: string;
    branchAddress: string | null;
    taxpayerIdentificationNumber: string;
    branchCode: string;
    vatRegistrationMode: string | null;
    contactNote: string | null;
  } | null;
  machine: {
    profileId: string | null;
    machineSerialNumber: string;
    machineIdentificationNumber: string | null;
    posTerminalNumber: string | null;
    accreditationNumber: string | null;
    permitToUseNumber: string | null;
    softwareVersion: string | null;
    softwareBuild: string | null;
    complianceMode: string;
    officialOutputEnabledInConfig: boolean;
    isActive: boolean;
  } | null;
  receipt: {
    profileId: string | null;
    documentTitle: string;
    documentKind: string;
    footerLegalText: string | null;
    supplementaryDisclaimerText: string | null;
    reprintMarkerText: string | null;
    trainingMarkerText: string | null;
    voidRefundMarkerText: string | null;
    invoiceOrTransitionMode: string | null;
    scPwdPlaceholderEnabled: boolean;
    dryRunOnly: boolean;
  } | null;
  series: {
    seriesId: string | null;
    documentKind: string;
    prefix: string;
    year: number;
    startNumber: number;
    currentNumber: number;
    endNumber: number | null;
    paddingDigits: number;
    isActive: boolean;
    isExhausted: boolean;
    dryRunOnly: boolean;
  } | null;
  notes: string[];
};

export type OfficialReadinessSnapshot = {
  contractVersion: number;
  status: string;
  officialOutputEnabledInConfig: boolean;
  liveOfficialPostingAllowed: boolean;
  complianceMode: string;
  summary: string;
  allowedActions: string[];
  blockers: { code: string; message: string }[];
  warnings: { code: string; message: string }[];
  notes: string[];
};

export type OfficialNumberingPreview = {
  contractVersion: number;
  documentType: string;
  prefix: string;
  currentNumber: number;
  nextNumber: number;
  formattedNextNumber: string;
  rangeStart: number | null;
  rangeEnd: number | null;
  dryRunOnly: boolean;
  liveOfficialPostingAllowed: boolean;
  officialCheckoutAllocationEnabled: boolean;
  checkoutUsesAllocator: boolean;
  previewMutatesSequence: boolean;
  allocationAllowed: boolean;
  mode: string;
  blockers: { code: string; message: string }[];
  warnings: string[];
  summary: string;
  blockedReason: string | null;
};

export type OfficialNumberingValidation = {
  contractVersion: number;
  isValidForPreview: boolean;
  liveAllocationAllowed: boolean;
  officialCheckoutAllocationEnabled: boolean;
  checkoutUsesAllocator: boolean;
  liveOfficialPostingAllowed: boolean;
  previewNextNumber: number | null;
  formattedNextNumber: string | null;
  blockers: { code: string; message: string }[];
  warnings: string[];
  summary: string;
};

export async function fetchOfficialSetup(): Promise<OfficialSetupSnapshot> {
  const response = await fetch("/api/official/setup", { credentials: "include" });
  return readJson<OfficialSetupSnapshot>(response);
}

export async function fetchOfficialReadiness(): Promise<OfficialReadinessSnapshot> {
  const response = await fetch("/api/official/readiness", { credentials: "include" });
  return readJson<OfficialReadinessSnapshot>(response);
}

export async function saveOfficialBusiness(body: Record<string, unknown>): Promise<OfficialSetupSnapshot> {
  const response = await fetch("/api/official/setup/business", {
    method: "PUT",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  return readJson<OfficialSetupSnapshot>(response);
}

export async function saveOfficialMachine(body: Record<string, unknown>): Promise<OfficialSetupSnapshot> {
  const response = await fetch("/api/official/setup/machine", {
    method: "PUT",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  return readJson<OfficialSetupSnapshot>(response);
}

export async function saveOfficialReceipt(body: Record<string, unknown>): Promise<OfficialSetupSnapshot> {
  const response = await fetch("/api/official/setup/receipt", {
    method: "PUT",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  return readJson<OfficialSetupSnapshot>(response);
}

export async function saveOfficialSeries(body: Record<string, unknown>): Promise<OfficialSetupSnapshot> {
  const response = await fetch("/api/official/setup/series", {
    method: "PUT",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  return readJson<OfficialSetupSnapshot>(response);
}

export async function fetchOfficialNumberingPreview(): Promise<OfficialNumberingPreview> {
  const response = await fetch("/api/official/numbering/preview", { credentials: "include" });
  return readJson<OfficialNumberingPreview>(response);
}

export async function validateOfficialNumbering(): Promise<OfficialNumberingValidation> {
  const response = await fetch("/api/official/numbering/validate", {
    method: "POST",
    credentials: "include",
  });
  return readJson<OfficialNumberingValidation>(response);
}
