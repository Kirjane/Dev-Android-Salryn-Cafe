import { readJson } from "./http";

export type StoreSettings = {
  id: string;
  storeName: string;
  businessName: string | null;
  address: string | null;
  contactNumber: string | null;
  tin: string | null;
  currencyLabel: string;
  receiptWidthMm: number;
  receiptHeader: string | null;
  receiptFooter: string | null;
  printerMode: "Disabled" | "WindowsPrinter";
  windowsPrinterName: string | null;
  hasLogo?: boolean;
  logoContentType?: string | null;
};

export type StoreSettingsUpdate = Omit<StoreSettings, "id" | "hasLogo" | "logoContentType">;

export type StoreBranding = {
  storeName: string;
  hasLogo: boolean;
  logoContentType: string | null;
  logoUrl: string | null;
};

export const fetchStoreSettings = () =>
  fetch("/api/settings/store", { credentials: "include" }).then(readJson<StoreSettings>);

export async function saveStoreSettings(value: StoreSettingsUpdate) {
  const response = await fetch("/api/settings/store", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    credentials: "include",
    body: JSON.stringify(value),
  });
  if (!response.ok) await readJson(response);
}

export const fetchStoreBranding = () =>
  fetch("/api/branding/store", { credentials: "include" }).then(readJson<StoreBranding>);

export async function uploadStoreLogo(file: File) {
  const body = new FormData();
  body.append("logo", file);
  const response = await fetch("/api/settings/store/logo", {
    method: "POST",
    credentials: "include",
    body,
  });
  return readJson<{ hasLogo: boolean; logoContentType: string; logoUrl: string }>(response);
}

export async function removeStoreLogo() {
  const response = await fetch("/api/settings/store/logo", {
    method: "DELETE",
    credentials: "include",
  });
  if (!response.ok && response.status !== 204) await readJson(response);
}
