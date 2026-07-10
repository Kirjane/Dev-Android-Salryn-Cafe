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

export type OfficialIntegrityStatus = {
  contractVersion: number;
  liveOfficialPostingAllowed: boolean;
  checkoutIntegrated: boolean;
  remotePosOfficialAllowed: boolean;
  officialOutputImplemented: boolean;
  complianceClaimed: boolean;
  officialCheckoutAllocationEnabled: boolean;
  checkoutUsesAllocator: boolean;
  summary: string;
  warnings: string[];
  journal: {
    skeletonPresent: boolean;
    schemaPresent: boolean;
    liveBound: boolean;
    liveAppendEnabled: boolean;
    checkoutIntegrated: boolean;
    summary: string;
    blockers: { code: string; message: string }[];
  };
  voidRefund: {
    skeletonPresent: boolean;
    liveIntegrated: boolean;
    checkoutIntegrated: boolean;
    summary: string;
    blockers: { code: string; message: string }[];
    placeholderReasons: { reasonCode: string; displayLabel: string; isPlaceholder: boolean }[];
  };
};

export async function fetchOfficialIntegrityStatus(): Promise<OfficialIntegrityStatus> {
  const response = await fetch("/api/official/integrity/status", { credentials: "include" });
  return readJson<OfficialIntegrityStatus>(response);
}

export type OfficialSpineStatus = {
  contractVersion: number;
  dryRunOnly: boolean;
  nonOfficial: boolean;
  liveOfficialPostingAllowed: boolean;
  officialCheckoutAllocationEnabled: boolean;
  checkoutUsesAllocator: boolean;
  officialOutputImplemented: boolean;
  complianceClaimed: boolean;
  summary: string;
  components: { component: string; level: string; present: boolean; liveBound: boolean; summary: string }[];
  blockers: { code: string; message: string }[];
  warnings: { code: string; message: string }[];
};

export type OfficialDryRunPostingPlan = {
  contractVersion: number;
  status: string;
  dryRunOnly: boolean;
  nonOfficial: boolean;
  liveOfficialPostingAllowed: boolean;
  officialCheckoutAllocationEnabled: boolean;
  checkoutUsesAllocator: boolean;
  sequenceWillMutate: boolean;
  salesWillMutate: boolean;
  ejWillAppendLive: boolean;
  outputWillGenerate: boolean;
  complianceClaimed: boolean;
  documentKind: string;
  summary: string;
  numbering: {
    previewOnly: boolean;
    sequenceWillMutate: boolean;
    formattedPreviewNumber: string | null;
    summary: string;
  };
  journal: { liveBound: boolean; ejWillAppendLive: boolean; skeletonPresent: boolean; summary: string };
  voidRefund: { liveIntegrated: boolean; skeletonPresent: boolean; summary: string };
  reportPackage: {
    reportEngineSelected: boolean;
    outputWillGenerate: boolean;
    summary: string;
  };
  blockers: { code: string; message: string }[];
  warnings: { code: string; message: string }[];
};

export async function fetchOfficialSpineStatus(): Promise<OfficialSpineStatus> {
  const response = await fetch("/api/official/spine/status", { credentials: "include" });
  return readJson<OfficialSpineStatus>(response);
}

export async function buildOfficialDryRunPostingPlan(body?: Record<string, unknown>): Promise<OfficialDryRunPostingPlan> {
  const response = await fetch("/api/official/dry-run/posting-plan", {
    method: "POST",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(
      body ?? {
        documentKind: "OFFICIAL_INVOICE",
        operationalPosNumberHint: null,
        taxpayerNameHint: null,
        lines: [],
        totals: null,
      },
    ),
  });
  return readJson<OfficialDryRunPostingPlan>(response);
}

export type OfficialDryRunPackageSummary = {
  id: string;
  publicId: string;
  createdAtUtc: string;
  sourceMode: string;
  status: string;
  summary: string | null;
};

export type OfficialDryRunPackage = {
  id: string;
  publicId: string;
  summary: string;
  dryRunOnly: boolean;
  officialReceiptIssued: boolean;
  liveOfficialPostingAllowed: boolean;
  sequenceWillMutate: boolean;
  salesWillMutate: boolean;
  ejWillAppendLive: boolean;
  outputWillGenerate: boolean;
  complianceClaimed: boolean;
  reportPreview: {
    watermark: string;
    receiptPlainText: string;
    xReadingPlainText: string;
    zReadingPlainText: string;
    journalPlainText: string;
    voidRefundPlainText: string;
    summary: string;
  } | null;
};

export async function listOfficialDryRunPackages(): Promise<{ packages: OfficialDryRunPackageSummary[] }> {
  const response = await fetch("/api/official/dry-run/packages", { credentials: "include" });
  return readJson<{ packages: OfficialDryRunPackageSummary[] }>(response);
}

export async function createOfficialDryRunPackage(): Promise<OfficialDryRunPackage> {
  const response = await fetch("/api/official/dry-run/packages", {
    method: "POST",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ postingRequest: null }),
  });
  return readJson<OfficialDryRunPackage>(response);
}

export async function createOfficialDryRunPackageFromSale(saleId: string): Promise<OfficialDryRunPackage> {
  const response = await fetch("/api/official/dry-run/packages/from-sale", {
    method: "POST",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ saleId }),
  });
  return readJson<OfficialDryRunPackage>(response);
}

export async function fetchOfficialSourceSaleDryRunReadonly(saleId: string): Promise<{
  summary: string;
  sourceSaleReadOnly: boolean;
  liveOfficialPostingAllowed: boolean;
  salesWillMutate: boolean;
  sequenceWillMutate: boolean;
  snapshot: { operationalReceiptNo: string; status: string; lineCount: number } | null;
}> {
  const response = await fetch(`/api/official/source-sale/${encodeURIComponent(saleId)}/dry-run-readonly`, {
    credentials: "include",
  });
  return readJson(response);
}

export async function fetchOfficialInternalReports(packageId: string): Promise<{
  watermark: string;
  summary?: string;
  note: string;
  reportPreview: {
    watermark: string;
    receiptPlainText: string;
    xReadingPlainText: string;
    zReadingPlainText: string;
    journalPlainText: string;
    voidRefundPlainText: string;
    salesSummaryPlainText?: string;
    summary: string;
  } | null;
}> {
  const response = await fetch(`/api/official/internal-reports/${encodeURIComponent(packageId)}`, {
    credentials: "include",
  });
  return readJson(response);
}

export async function buildOfficialReportPreview(): Promise<{
  watermark: string;
  summary: string;
  officialReceiptIssued: boolean;
  receipt: { plainTextPreview: string };
  xReading: { plainTextPreview: string };
  zReading: { plainTextPreview: string };
  journalExport: { plainTextPreview: string };
  voidRefund: { plainTextPreview: string };
}> {
  const response = await fetch("/api/official/dry-run/report-preview", {
    method: "POST",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      documentKind: "OFFICIAL_INVOICE",
      lines: [],
      totals: null,
    }),
  });
  return readJson(response);
}
