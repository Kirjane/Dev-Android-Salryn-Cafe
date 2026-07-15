import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { fetchLicenseStatus, installLicense, type LicenseStatusValue } from "../api/license";
import { Alert, Button, Card, CardBody, CardHeader, CardTitle, CommandBar, LoadingState, StatusPill } from "../components/ui";

function formatDate(value: string | null | undefined): string {
  if (!value) return "Not set";
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString();
}

function statusLabel(status: LicenseStatusValue): string {
  if (typeof status === "string") return status;
  return ["Unlicensed", "Licensed", "Expired", "Invalid", "Grace", "Development"][status] ?? String(status);
}

function statusTone(status: LicenseStatusValue): "success" | "warning" | "danger" | "neutral" | "info" {
  const label = statusLabel(status);
  if (label === "Licensed") return "success";
  if (label === "Grace" || label === "Development") return "warning";
  if (label === "Invalid" || label === "Expired") return "danger";
  if (label === "Unlicensed") return "neutral";
  return "info";
}

export function LicensePage() {
  const statusQuery = useQuery({ queryKey: ["license-status"], queryFn: fetchLicenseStatus, retry: false });
  const [licenseText, setLicenseText] = useState("");
  const [replaceConfirmed, setReplaceConfirmed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const status = statusQuery.data;
  const canInstall = licenseText.trim().length > 0 && replaceConfirmed && !busy;
  const lanCount = status?.allowedLanTerminals;
  const currentStatusLabel = status ? statusLabel(status.status) : null;

  async function handleInstall() {
    if (!canInstall) return;
    setBusy(true);
    setMessage(null);
    setError(null);
    try {
      const result = await installLicense(licenseText.trim());
      setMessage(result.installStatus === "Installed" ? "License installed and verified." : result.errorMessage ?? "License was rejected.");
      if (result.installStatus === "Installed") {
        setLicenseText("");
        setReplaceConfirmed(false);
      }
      await statusQuery.refetch();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="space-y-6">
      <CommandBar
        title="License"
        subtitle="Admin-only offline signed license status and import. Normal use does not require cloud activation."
        status={status ? <StatusPill tone={statusTone(status.status)}>{currentStatusLabel}</StatusPill> : null}
        actions={<Button variant="secondary" disabled={statusQuery.isFetching || busy} onClick={() => void statusQuery.refetch()}>Refresh</Button>}
      />

      <Alert tone="info" title="Offline-first license boundary">
        Salryn uses a local signed license file with public-key verification. This page is not cloud licensing, not subscription billing, not payment processing, not installer proof, not BIR/RMO compliance, not official receipt/invoice readiness, and not eSales readiness.
      </Alert>
      <Alert tone="warning" title="LAN entitlement boundary">
        LAN terminal production use is not claimed here. Host LAN Control remains Coming Soon / internal preview unless a verified license entitlement and separate LAN release proof exist.
      </Alert>

      {statusQuery.isLoading ? <LoadingState label="Loading license status…" /> : null}
      {statusQuery.isError ? <Alert tone="danger">{(statusQuery.error as Error).message}</Alert> : null}
      {message ? <Alert tone="success">{message}</Alert> : null}
      {error ? <Alert tone="danger">{error}</Alert> : null}

      {status ? (
        <div className="grid gap-4 xl:grid-cols-3">
          <Card>
            <CardHeader><CardTitle>Current license</CardTitle></CardHeader>
            <CardBody className="space-y-2 text-sm text-slate-700">
              <p><span className="font-semibold">Status:</span> {currentStatusLabel}</p>
              <p><span className="font-semibold">Message:</span> {status.message}</p>
              <p><span className="font-semibold">License ID:</span> <span className="font-mono text-xs">{status.licenseId ?? "Not installed"}</span></p>
              <p><span className="font-semibold">Customer:</span> {status.customerName ?? "Not available"}</p>
              <p><span className="font-semibold">Edition:</span> {status.edition ?? "Not available"}</p>
              <p><span className="font-semibold">Expires:</span> {formatDate(status.expiresAt)}</p>
            </CardBody>
          </Card>

          <Card>
            <CardHeader><CardTitle>Store and device binding</CardTitle></CardHeader>
            <CardBody className="space-y-2 text-sm text-slate-700">
              <p><span className="font-semibold">Application:</span> {status.applicationId ?? "Not specified"}</p>
              <p><span className="font-semibold">Store:</span> {status.storeId ?? "Not specified"}</p>
              <p><span className="font-semibold">Machine matched:</span> {status.machineMatched ? "Yes" : "No"}</p>
              <p><span className="font-semibold">This machine fingerprint:</span> <span className="break-all font-mono text-xs">{status.machineFingerprint ?? "Not available"}</span></p>
            </CardBody>
          </Card>

          <Card>
            <CardHeader><CardTitle>Entitlements</CardTitle></CardHeader>
            <CardBody className="space-y-2 text-sm text-slate-700">
              <p><span className="font-semibold">Features:</span> {status.enabledFeatures.length > 0 ? status.enabledFeatures.join(", ") : "None"}</p>
              <p><span className="font-semibold">LAN terminals:</span> {typeof lanCount === "number" ? lanCount : "Not entitled for production LAN"}</p>
              <p><span className="font-semibold">Support until:</span> {formatDate(status.supportExpiresAt)}</p>
              <p><span className="font-semibold">Updates until:</span> {formatDate(status.updatesUntil)}</p>
            </CardBody>
          </Card>
        </div>
      ) : null}

      <Card>
        <CardHeader><CardTitle>Import signed license</CardTitle></CardHeader>
        <CardBody className="space-y-4">
          <Alert tone="warning" title="Replace local license">
            Import replaces the local license file only after the backend verifies the signed document. Invalid, unsigned, expired, unsupported, path-bearing, or machine-mismatched licenses are rejected.
          </Alert>
          <label className="space-y-1 text-sm font-medium text-slate-700">
            <span>Paste signed license JSON</span>
            <textarea
              value={licenseText}
              onChange={(event) => setLicenseText(event.target.value)}
              className="min-h-48 w-full rounded-md border border-slate-300 bg-white px-3 py-2 font-mono text-xs text-slate-900 shadow-sm focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 focus-visible:ring-offset-2"
              placeholder="Paste the signed local license JSON here. No cloud activation or file path is required."
            />
          </label>
          <label className="flex items-start gap-2 text-sm text-slate-700">
            <input type="checkbox" checked={replaceConfirmed} onChange={(event) => setReplaceConfirmed(event.target.checked)} className="mt-1" />
            <span>I understand this replaces the current local license file if the signature and license checks pass.</span>
          </label>
          <div className="flex flex-wrap gap-2">
            <Button disabled={!canInstall} onClick={() => void handleInstall()}>{busy ? "Installing…" : "Install verified license"}</Button>
            <Button variant="secondary" disabled={busy || (!licenseText && !replaceConfirmed)} onClick={() => { setLicenseText(""); setReplaceConfirmed(false); }}>Clear</Button>
          </div>
        </CardBody>
      </Card>
    </div>
  );
}
