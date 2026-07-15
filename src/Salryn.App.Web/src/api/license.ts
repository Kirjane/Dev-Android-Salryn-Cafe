import { readJson } from "./http";

export type LicenseStatusValue = "Unlicensed" | "Licensed" | "Expired" | "Invalid" | "Grace" | "Development" | number;
export type LicenseEditionValue = "Trial" | "Local" | "Professional" | "Enterprise" | number;

export type LocalLicenseStatus = {
  status: LicenseStatusValue;
  edition: LicenseEditionValue | null;
  enabledFeatures: string[];
  expiresAt: string | null;
  daysRemaining: number | null;
  machineMatched: boolean;
  message: string;
  validationTimestamp: string;
  licenseId: string | null;
  customerName: string | null;
  applicationId: string | null;
  storeId: string | null;
  allowedLanTerminals: number | null;
  supportExpiresAt: string | null;
  updatesUntil: string | null;
  machineFingerprint: string | null;
};

export type LicenseInstallResponse = {
  installStatus: "Installed" | "Rejected" | string;
  validationStatus: LicenseStatusValue;
  errorCode: string | null;
  errorMessage: string | null;
  timestamp: string;
};

export function fetchLicenseStatus() {
  return fetch("/api/license/status", { credentials: "include" }).then(readJson<LocalLicenseStatus>);
}

export function installLicense(licenseJson: string) {
  return fetch("/api/license/install", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    credentials: "include",
    body: JSON.stringify({ licenseJson }),
  }).then(readJson<LicenseInstallResponse>);
}
