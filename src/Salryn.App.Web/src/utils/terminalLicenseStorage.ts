export const TERMINAL_LICENSE_STORAGE_KEY = "salryn.terminalLicense.v1";
export const TERMINAL_MACHINE_CODE_STORAGE_KEY = "salryn.terminalMachineCode.v1";
const LEGACY_TERMINAL_LICENSE_STORAGE_KEY = "salryn.terminalLicenseJson";

export type InstalledTerminalLicense = {
  version: 1;
  licenseJson: string;
  licenseId: string;
  customerName: string;
  storeId: string;
  hostLicenseId: string;
  terminalId: string;
  machineCode: string;
  expiresAt: string | null;
  installedAt: string;
};

export type TerminalLicenseCandidate = {
  licenseId: string;
  customerName: string;
  storeId: string;
  hostLicenseId: string;
  terminalId: string;
  machineBinding: string;
  expiresAt: string | null;
};

type UnknownLicenseDocument = {
  LicenseId?: unknown;
  CustomerName?: unknown;
  ApplicationId?: unknown;
  StoreId?: unknown;
  LicenseType?: unknown;
  HostLicenseId?: unknown;
  TerminalId?: unknown;
  MachineBinding?: unknown;
  Signature?: unknown;
  Features?: unknown;
  ExpiresAt?: unknown;
};

function requiredString(value: unknown): string | null {
  return typeof value === "string" && value.trim() ? value.trim() : null;
}

function isTerminalLicenseType(value: unknown): boolean {
  return value === "Terminal" || value === 1;
}

function formatMachineCode(bytes: Uint8Array): string {
  const hex = Array.from(bytes, (value) => value.toString(16).padStart(2, "0")).join("").toUpperCase();
  return `SALRYN-MC-${hex.slice(0, 4)}-${hex.slice(4, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}`;
}

export function getOrCreateTerminalMachineCode(): string {
  if (typeof window === "undefined") return "";

  const existing = window.localStorage.getItem(TERMINAL_MACHINE_CODE_STORAGE_KEY)?.trim();
  if (existing && /^SALRYN-MC-[A-F0-9]{4}(?:-[A-F0-9]{4}){3}$/.test(existing)) {
    return existing;
  }

  const bytes = new Uint8Array(8);
  window.crypto.getRandomValues(bytes);
  const created = formatMachineCode(bytes);
  window.localStorage.setItem(TERMINAL_MACHINE_CODE_STORAGE_KEY, created);
  return created;
}

export function inspectTerminalLicenseCandidate(
  licenseJson: string,
  machineCode: string,
): { candidate: TerminalLicenseCandidate | null; error: string | null } {
  let document: UnknownLicenseDocument;
  try {
    const parsed: unknown = JSON.parse(licenseJson);
    if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) {
      return { candidate: null, error: "Paste the signed Terminal license JSON you received." };
    }
    document = parsed as UnknownLicenseDocument;
  } catch {
    return { candidate: null, error: "The Terminal license JSON is malformed." };
  }

  if (!isTerminalLicenseType(document.LicenseType)) {
    return { candidate: null, error: "This device requires a Terminal license. A Host license cannot activate a terminal." };
  }

  if (requiredString(document.ApplicationId) !== "Salryn.POS") {
    return { candidate: null, error: "This license is not for Salryn POS." };
  }

  const licenseId = requiredString(document.LicenseId);
  const customerName = requiredString(document.CustomerName);
  const storeId = requiredString(document.StoreId);
  const hostLicenseId = requiredString(document.HostLicenseId);
  const terminalId = requiredString(document.TerminalId);
  const machineBinding = requiredString(document.MachineBinding);
  const signature = requiredString(document.Signature);
  const features = Array.isArray(document.Features)
    ? document.Features.filter((value): value is string => typeof value === "string")
    : [];

  if (!licenseId || !customerName || !storeId || !hostLicenseId || !terminalId || !machineBinding || !signature) {
    return { candidate: null, error: "The Terminal license is missing required identity or signature fields." };
  }

  if (!features.includes("LanTerminals")) {
    return { candidate: null, error: "The Terminal license does not enable LAN terminal access." };
  }

  if (machineBinding.toUpperCase() !== machineCode.trim().toUpperCase()) {
    return { candidate: null, error: "This Terminal license was issued for a different device." };
  }

  const expiresAt = requiredString(document.ExpiresAt);
  if (expiresAt) {
    const parsedExpiry = new Date(expiresAt);
    if (!Number.isNaN(parsedExpiry.getTime()) && parsedExpiry.getTime() <= Date.now()) {
      return { candidate: null, error: "This Terminal license has expired." };
    }
  }

  return {
    candidate: {
      licenseId,
      customerName,
      storeId,
      hostLicenseId,
      terminalId,
      machineBinding,
      expiresAt,
    },
    error: null,
  };
}

export function installTerminalLicense(
  licenseJson: string,
  validated: Omit<InstalledTerminalLicense, "version" | "licenseJson" | "installedAt">,
): InstalledTerminalLicense {
  const record: InstalledTerminalLicense = {
    version: 1,
    licenseJson: licenseJson.trim(),
    ...validated,
    installedAt: new Date().toISOString(),
  };
  window.localStorage.setItem(TERMINAL_LICENSE_STORAGE_KEY, JSON.stringify(record));
  window.localStorage.removeItem(LEGACY_TERMINAL_LICENSE_STORAGE_KEY);
  return record;
}

export function readInstalledTerminalLicense(): InstalledTerminalLicense | null {
  if (typeof window === "undefined") return null;

  const raw = window.localStorage.getItem(TERMINAL_LICENSE_STORAGE_KEY);
  if (!raw) return null;

  try {
    const record = JSON.parse(raw) as Partial<InstalledTerminalLicense>;
    const machineCode = getOrCreateTerminalMachineCode();
    if (
      record.version !== 1 ||
      typeof record.licenseJson !== "string" ||
      typeof record.licenseId !== "string" ||
      typeof record.customerName !== "string" ||
      typeof record.storeId !== "string" ||
      typeof record.hostLicenseId !== "string" ||
      typeof record.terminalId !== "string" ||
      typeof record.machineCode !== "string" ||
      record.machineCode.toUpperCase() !== machineCode.toUpperCase()
    ) {
      return null;
    }

    const inspected = inspectTerminalLicenseCandidate(record.licenseJson, machineCode);
    return inspected.candidate ? (record as InstalledTerminalLicense) : null;
  } catch {
    return null;
  }
}

export function clearInstalledTerminalLicense(): void {
  if (typeof window === "undefined") return;
  window.localStorage.removeItem(TERMINAL_LICENSE_STORAGE_KEY);
  window.localStorage.removeItem(LEGACY_TERMINAL_LICENSE_STORAGE_KEY);
}
