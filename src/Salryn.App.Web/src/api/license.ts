import { readJson } from "./http";

export type LicenseStatusValue = "Unlicensed" | "Licensed" | "Expired" | "Invalid" | "Grace" | "Development" | number;
export type LicenseEditionValue = "Trial" | "Local" | "Professional" | "Enterprise" | number;
export type LicenseTypeValue = "Host" | "Terminal" | number;

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
  licenseType: LicenseTypeValue | null;
  hostLicenseId: string | null;
  terminalId: string | null;
  machineCode: string | null;
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

export async function installLicense(licenseJson: string): Promise<LicenseInstallResponse> {
  const response = await fetch("/api/license/install", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    credentials: "include",
    body: JSON.stringify({ licenseJson }),
  });

  // Install returns 400 with a structured InstallResponse (errorMessage/errorCode), not only thrown ApiError.
  // Parse that shape so the License page can map backend rejects to client-facing copy.
  if (response.status === 400) {
    const body = (await response.json().catch(() => null)) as
      | (Partial<LicenseInstallResponse> & { error?: string; message?: string; code?: string })
      | null;
    if (body && (body.installStatus || body.errorMessage || body.errorCode || body.error || body.message)) {
      return {
        installStatus: (body.installStatus as LicenseInstallResponse["installStatus"]) ?? "Rejected",
        validationStatus: (body.validationStatus as LicenseInstallResponse["validationStatus"]) ?? "Invalid",
        errorCode: body.errorCode ?? body.code ?? null,
        errorMessage: body.errorMessage ?? body.error ?? body.message ?? "License was rejected.",
        timestamp: body.timestamp ?? new Date().toISOString(),
      };
    }
  }

  return readJson<LicenseInstallResponse>(response);
}
