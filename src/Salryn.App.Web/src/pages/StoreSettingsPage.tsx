import { useEffect, useState } from "react";
import {
  fetchStoreSettings,
  removeStoreLogo,
  saveStoreSettings,
  uploadStoreLogo,
  type StoreSettings,
} from "../api/storeSettings";
import { Alert, Button, Card, CardBody, CardHeader, CardTitle, CommandBar, ErrorState, LoadingState, TextInput } from "../components/ui";

const supportedCurrencies = new Set(["PHP", "₱"]);
const logoAccept = "image/png,image/jpeg,image/webp";

export function StoreSettingsPage() {
  const [value, setValue] = useState<StoreSettings | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [logoBusy, setLogoBusy] = useState(false);
  const [logoPreviewKey, setLogoPreviewKey] = useState(0);

  useEffect(() => {
    fetchStoreSettings()
      .then((settings) => setValue({ ...settings, windowsPrinterName: settings.windowsPrinterName ?? "" }))
      .catch((error) => setLoadError(error instanceof Error ? error.message : "Store settings could not be loaded."));
  }, []);

  function update<K extends keyof StoreSettings>(key: K, next: StoreSettings[K]) {
    setValue((current) => current ? { ...current, [key]: next } : current);
    setMessage(null);
  }

  function validate(): string | null {
    if (!value?.storeName.trim()) return "Store name is required.";
    if (!supportedCurrencies.has(value.currencyLabel.trim())) return "Choose PHP or ₱ as the currency label.";
    if (value.receiptWidthMm !== 58 && value.receiptWidthMm !== 80) return "Receipt text width setting is invalid.";
    if (value.printerMode === "WindowsPrinter" && !value.windowsPrinterName?.trim()) return "Windows printer name is required when Windows Printer mode is selected.";
    return null;
  }

  async function save() {
    if (!value) return;
    const validation = validate();
    if (validation) {
      setMessage(validation);
      return;
    }

    setBusy(true);
    setMessage(null);
    try {
      const { id: _id, hasLogo: _hasLogo, logoContentType: _logoType, ...updateRequest } = value;
      await saveStoreSettings({ ...updateRequest, windowsPrinterName: updateRequest.windowsPrinterName?.trim() || null });
      setMessage("Store settings saved. New receipts will use the updated store, receipt, and printer settings.");
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Store settings could not be saved.");
    } finally {
      setBusy(false);
    }
  }

  async function onLogoSelected(file: File | null) {
    if (!file || !value) return;
    if (file.size > 512 * 1024) {
      setMessage("Logo must be 512 KB or smaller.");
      return;
    }
    if (!["image/png", "image/jpeg", "image/webp"].includes(file.type)) {
      setMessage("Logo must be PNG, JPEG, or WebP. SVG is not accepted.");
      return;
    }
    setLogoBusy(true);
    setMessage(null);
    try {
      const result = await uploadStoreLogo(file);
      setValue({ ...value, hasLogo: result.hasLogo, logoContentType: result.logoContentType });
      setLogoPreviewKey((k) => k + 1);
      setMessage("Store logo saved. Host and LAN shells will show the new branding after refresh.");
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Logo could not be uploaded.");
    } finally {
      setLogoBusy(false);
    }
  }

  async function onRemoveLogo() {
    if (!value) return;
    setLogoBusy(true);
    setMessage(null);
    try {
      await removeStoreLogo();
      setValue({ ...value, hasLogo: false, logoContentType: null });
      setLogoPreviewKey((k) => k + 1);
      setMessage("Store logo removed.");
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Logo could not be removed.");
    } finally {
      setLogoBusy(false);
    }
  }

  if (loadError) {
    return <ErrorState message={loadError} />;
  }

  if (!value) {
    return <LoadingState label="Loading store settings..." />;
  }

  const field = (key: keyof StoreSettings, label: string, hint?: string) => (
    <label className="space-y-1 text-sm font-medium text-slate-700">
      <span>{label}</span>
      <TextInput value={String(value[key] ?? "")} onChange={(event) => update(key, event.target.value as never)} />
      {hint ? <span className="block text-xs font-normal text-slate-500">{hint}</span> : null}
    </label>
  );

  return (
    <div className="space-y-4">
      <CommandBar
        title="Store Settings"
        subtitle="Manage store details, receipts, and printing."
        actions={<Button onClick={() => void save()} disabled={busy}>{busy ? "Saving..." : "Save settings"}</Button>}
      />

      {message ? <Alert tone={message.startsWith("Store settings saved") ? "success" : "danger"}>{message}</Alert> : null}

      <div className="grid gap-4 xl:grid-cols-2">
        <Card>
          <CardHeader><CardTitle>Store profile</CardTitle></CardHeader>
          <CardBody className="grid gap-4 md:grid-cols-2">
            {field("storeName", "Store name", "The customer-facing store identity used on new receipts.")}
            {field("businessName", "Registered business name", "Optional business name shown on new receipts.")}
            <div className="md:col-span-2">{field("address", "Store address")}</div>
            {field("contactNumber", "Contact number")}
            {field("tin", "TIN")}
            <div className="md:col-span-2 space-y-3 rounded-md border border-slate-200 bg-slate-50 p-3">
              <p className="text-sm font-semibold text-slate-900">Store logo / branding</p>
              <p className="text-xs text-slate-500">
                PNG, JPEG, or WebP only · max 512 KB · shown on Host and LAN terminal shells · not official BIR output
              </p>
              {value.hasLogo ? (
                <div className="flex items-center gap-3">
                  <img
                    key={logoPreviewKey}
                    src={`/api/branding/store/logo?v=${logoPreviewKey}`}
                    alt="Store logo preview"
                    className="h-14 w-14 rounded-md border border-slate-200 bg-white object-contain"
                  />
                  <Button variant="secondary" size="sm" disabled={logoBusy} onClick={() => void onRemoveLogo()}>
                    {logoBusy ? "Working…" : "Remove logo"}
                  </Button>
                </div>
              ) : (
                <p className="text-xs text-slate-500">No logo uploaded yet.</p>
              )}
              <label className="block text-sm font-medium text-slate-700">
                <span>Upload logo</span>
                <input
                  type="file"
                  accept={logoAccept}
                  className="mt-1 block w-full text-xs"
                  disabled={logoBusy}
                  onChange={(event) => void onLogoSelected(event.target.files?.[0] ?? null)}
                />
              </label>
            </div>
          </CardBody>
        </Card>

        <Card>
          <CardHeader><CardTitle>Regional / Currency</CardTitle></CardHeader>
          <CardBody className="space-y-3">
            <label className="space-y-1 text-sm font-medium text-slate-700">
              <span>Currency label</span>
              <select className="min-h-9 w-full rounded-md border border-slate-300 px-3" value={value.currencyLabel} onChange={(event) => update("currencyLabel", event.target.value)}>
                {!supportedCurrencies.has(value.currencyLabel) ? <option value={value.currencyLabel}>Unsupported current value: {value.currencyLabel}</option> : null}
                <option value="PHP">PHP</option>
                <option value="₱">₱</option>
              </select>
              <span className="block text-xs font-normal text-slate-500">This label appears on POS amounts. Use PHP or ₱ for Philippine peso.</span>
            </label>
            <Alert tone="info">Changing the display label does not convert prices or historical sales.</Alert>
          </CardBody>
        </Card>

        <Card>
          <CardHeader><CardTitle>Receipt appearance</CardTitle></CardHeader>
          <CardBody className="grid gap-4 md:grid-cols-2">
            {field("receiptHeader", "Receipt header", "Optional message shown near the top of new receipts.")}
            {field("receiptFooter", "Receipt footer", "Optional message shown at the bottom of new receipts.")}
            <Alert tone="info" title="Saved receipts stay unchanged">
              Changes apply to new receipts. Saved receipts keep their original details.
            </Alert>
          </CardBody>
        </Card>

        <Card>
          <CardHeader><CardTitle>Printer</CardTitle></CardHeader>
          <CardBody className="space-y-4">
            <label className="space-y-1 text-sm font-medium text-slate-700">
              <span>Printer mode</span>
              <select className="min-h-9 w-full rounded-md border border-slate-300 px-3" value={value.printerMode} onChange={(event) => update("printerMode", event.target.value as StoreSettings["printerMode"])}>
                <option value="Disabled">Disabled</option>
                <option value="WindowsPrinter">Windows Printer</option>
              </select>
            </label>
            <label className="space-y-1 text-sm font-medium text-slate-700">
              <span>Windows printer name</span>
              <TextInput value={value.windowsPrinterName ?? ""} onChange={(event) => update("windowsPrinterName", event.target.value)} placeholder="Exact Windows printer name" />
              <span className="block text-xs font-normal text-slate-500">Use the exact printer name shown in Windows. Leave disabled mode selected if the printer is not ready.</span>
            </label>
            <Alert tone={value.printerMode === "WindowsPrinter" ? "info" : "warning"} title="Windows printer">
              Enter the printer name exactly as it appears in Windows. Leave printing disabled if it is not configured.
            </Alert>
            <p className="text-xs text-slate-500">Saved receipts remain available when printing is disabled.</p>
          </CardBody>
        </Card>
      </div>

    </div>
  );
}
