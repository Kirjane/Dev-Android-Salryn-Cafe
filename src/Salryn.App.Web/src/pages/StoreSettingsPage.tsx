import { useEffect, useState } from "react";
import { fetchStoreSettings, saveStoreSettings, type StoreSettings } from "../api/storeSettings";
import { Alert, Button, Card, CardBody, CardHeader, CardTitle, CommandBar, ErrorState, LoadingState, TextInput } from "../components/ui";

const supportedCurrencies = new Set(["PHP", "₱"]);

export function StoreSettingsPage() {
  const [value, setValue] = useState<StoreSettings | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    fetchStoreSettings()
      .then(setValue)
      .catch((error) => setLoadError(error instanceof Error ? error.message : "Store settings could not be loaded."));
  }, []);

  function update<K extends keyof StoreSettings>(key: K, next: StoreSettings[K]) {
    setValue((current) => current ? { ...current, [key]: next } : current);
    setMessage(null);
  }

  function validate(): string | null {
    if (!value?.storeName.trim()) return "Store name is required.";
    if (!supportedCurrencies.has(value.currencyLabel.trim())) return "Choose PHP or ₱ as the currency label.";
    if (value.receiptWidthMm !== 58 && value.receiptWidthMm !== 80) return "Receipt width must be 58 mm or 80 mm.";
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
      const { id: _id, ...updateRequest } = value;
      await saveStoreSettings(updateRequest);
      setMessage("Store settings saved. New receipts will use the updated store and receipt details.");
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Store settings could not be saved.");
    } finally {
      setBusy(false);
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
        title="Configure Store"
        subtitle="Manage store identity and receipt appearance. Completed receipt snapshots remain unchanged."
        actions={<Button onClick={() => void save()} disabled={busy}>{busy ? "Saving..." : "Save settings"}</Button>}
      />

      {message ? <Alert tone={message.startsWith("Store settings saved") ? "success" : "danger"}>{message}</Alert> : null}

      <div className="grid gap-4 xl:grid-cols-2">
        <Card>
          <CardHeader><CardTitle>Store profile</CardTitle></CardHeader>
          <CardBody className="grid gap-4 md:grid-cols-2">
            {field("storeName", "Store name", "The customer-facing store identity used on new receipts.")}
            {field("businessName", "Registered business name", "Optional legal business name; this does not claim regulatory approval.")}
            <div className="md:col-span-2">{field("address", "Store address")}</div>
            {field("contactNumber", "Contact number")}
            {field("tin", "TIN", "Stored as identity text only. Salryn does not validate tax registration here.")}
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
            <label className="space-y-1 text-sm font-medium text-slate-700 md:col-span-2">
              <span>Receipt width</span>
              <select className="min-h-9 w-full rounded-md border border-slate-300 px-3" value={value.receiptWidthMm} onChange={(event) => update("receiptWidthMm", Number(event.target.value))}>
                <option value={58}>58 mm</option>
                <option value={80}>80 mm</option>
              </select>
            </label>
            <Alert tone="info" title="Historical receipts stay unchanged">
              Store identity, header, footer, and width changes apply to newly completed receipts only. Saved receipts keep their original snapshot.
            </Alert>
            <Alert tone="neutral" title="Store logo deferred">
              Store logo support is planned but is not active in this build. No upload or placeholder image is stored.
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
                <option value="WindowsPrinter" disabled={value.printerMode !== "WindowsPrinter"}>Windows printer (not active in this build)</option>
              </select>
            </label>
            <Alert tone="warning" title="Physical printing is not configured">
              Disabled mode is safe. Windows printer support requires the separate printer-adapter implementation and device proof. Saving store settings does not prove that a printer is connected or compatible.
            </Alert>
            <p className="text-xs text-slate-500">Receipt text files and saved receipt snapshots continue to work when physical printing is disabled.</p>
          </CardBody>
        </Card>
      </div>

      <Alert tone="neutral" title="Planned administration surfaces">
        Controlled backup and licensing controls will be added in their own approved lanes. This page does not expose raw backup paths or license files.
      </Alert>
    </div>
  );
}
