import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { fetchLicenseStatus, installLicense, type LicenseStatusValue, type LicenseTypeValue } from "../api/license";
import { Alert, Button, Card, CardBody, CardHeader, CardTitle, CommandBar, LoadingState, StatusPill } from "../components/ui";

const BETA_CONTACT_EMAIL = "salryn.info@gmail.com";

function formatDate(value: string | null | undefined): string {
  if (!value) return "Not set";
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString();
}

function statusLabel(status: LicenseStatusValue): string {
  if (typeof status === "string") return status;
  return ["Unlicensed", "Licensed", "Expired", "Invalid", "Grace", "Development"][status] ?? String(status);
}

function licenseTypeLabel(value: LicenseTypeValue | null | undefined): string {
  if (value === null || value === undefined) return "Not installed";
  if (typeof value === "string") return value;
  return ["Host", "Terminal"][value] ?? String(value);
}

function statusTone(status: LicenseStatusValue): "success" | "warning" | "danger" | "neutral" | "info" {
  const label = statusLabel(status);
  if (label === "Licensed") return "success";
  if (label === "Grace" || label === "Development") return "warning";
  if (label === "Invalid" || label === "Expired") return "danger";
  if (label === "Unlicensed") return "neutral";
  return "info";
}

function needsActivation(statusLabelValue: string | null): boolean {
  return !statusLabelValue || statusLabelValue === "Unlicensed" || statusLabelValue === "Invalid" || statusLabelValue === "Expired";
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
  const currentStatusLabel = status ? statusLabel(status.status) : null;
  const typeLabel = licenseTypeLabel(status?.licenseType);
  const machineCode = status?.machineCode ?? status?.machineFingerprint ?? "Not available";
  const showActivation = needsActivation(currentStatusLabel) || !status?.machineMatched || typeLabel !== "Host";
  const hostSetupBlocked = currentStatusLabel === "Licensed" && typeLabel !== "Host";

  async function handleInstall() {
    if (!canInstall) return;
    setBusy(true);
    setMessage(null);
    setError(null);
    try {
      const payload = licenseText.trim();
      const result = await installLicense(payload);
      setMessage(result.installStatus === "Installed" ? "License installed and verified offline." : result.errorMessage ?? "License was rejected.");
      if (result.installStatus === "Installed") {
        try {
          const parsed = JSON.parse(payload) as { licenseType?: string | number; LicenseType?: string | number };
          const typeValue = parsed.licenseType ?? parsed.LicenseType;
          const isTerminal = typeValue === "Terminal" || typeValue === 1;
          if (isTerminal && typeof window !== "undefined") {
            window.localStorage.setItem("salryn.terminalLicenseJson", payload);
          }
        } catch {
          /* ignore parse for local cache */
        }
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
        subtitle="Admin-only offline signed license status and import. Salryn works offline after a valid signed license is installed."
        status={status ? <StatusPill tone={statusTone(status.status)}>{currentStatusLabel}</StatusPill> : null}
        actions={<Button variant="secondary" disabled={statusQuery.isFetching || busy} onClick={() => void statusQuery.refetch()}>Refresh</Button>}
      />

      <Alert tone="info" title="Offline-first license boundary">
        Salryn uses a local signed license file with public-key verification. Salryn works offline after a valid signed license is installed. This page is not cloud licensing, not subscription billing, not payment processing, not installer proof, not BIR/RMO compliance, not official receipt/invoice readiness, and not eSales readiness.
      </Alert>
      <Alert tone="warning" title="LAN entitlement boundary">
        LAN terminal production use is not claimed here. Host and Terminal licenses are separate. A Host license is not valid as a terminal license. Each LAN operational terminal requires its own Terminal license bound to that machine, and the Host enforces its licensed LAN entitlement.
      </Alert>

      {statusQuery.isLoading ? <LoadingState label="Loading license status…" /> : null}
      {statusQuery.isError ? <Alert tone="danger">{(statusQuery.error as Error).message}</Alert> : null}
      {message ? <Alert tone="success">{message}</Alert> : null}
      {error ? <Alert tone="danger">{error}</Alert> : null}
      {hostSetupBlocked ? <Alert tone="danger" title="Host license required">This Terminal license cannot unlock Host first-run setup or the full Host application. Install a valid machine-bound Host license on this workstation.</Alert> : null}

      {showActivation ? (
        <Card>
          <CardHeader><CardTitle>Activation required</CardTitle></CardHeader>
          <CardBody className="space-y-3 text-sm text-slate-700">
            <p className="font-semibold text-slate-900">Manual beta activation (no cloud activation server)</p>
            <p>Email <span className="font-mono font-semibold">{BETA_CONTACT_EMAIL}</span> with the details for this device:</p>
            <div className="grid gap-3 md:grid-cols-2">
              <div className="rounded-md border border-slate-200 p-3">
                <p className="font-semibold text-slate-900">Host activation</p>
                <ul className="mt-1 list-disc space-y-0.5 pl-5">
                  <li>Store/business name</li>
                  <li>Contact person</li>
                  <li>Contact number/email</li>
                  <li>Host Machine Code</li>
                </ul>
              </div>
              <div className="rounded-md border border-slate-200 p-3">
                <p className="font-semibold text-slate-900">Terminal activation</p>
                <ul className="mt-1 list-disc space-y-0.5 pl-5">
                  <li>Store/business name</li>
                  <li>Host License ID, if available</li>
                  <li>Terminal name/number</li>
                  <li>Terminal Machine Code</li>
                </ul>
              </div>
            </div>
            <p>Kirjane Labs will generate the matching signed license outside this app. Each LAN terminal uses one separate Terminal license. Paste the signed license JSON below to import and verify offline.</p>
            <p>
              <span className="font-semibold">Machine code:</span>{" "}
              <span className="break-all font-mono text-base font-bold tracking-wide text-navy-900">{machineCode}</span>
            </p>
            <p className="text-xs text-slate-500">Only the hashed machine code is shown. Raw hardware identifiers are not displayed.</p>
          </CardBody>
        </Card>
      ) : null}

      {status ? (
        <div className="grid gap-4 xl:grid-cols-3">
          <Card>
            <CardHeader><CardTitle>Current license</CardTitle></CardHeader>
            <CardBody className="space-y-2 text-sm text-slate-700">
              <p><span className="font-semibold">Status:</span> {currentStatusLabel}</p>
              <p><span className="font-semibold">License type:</span> {typeLabel}</p>
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
              <p><span className="font-semibold">Machine code:</span> <span className="break-all font-mono text-xs">{machineCode}</span></p>
              {typeLabel === "Terminal" ? (
                <>
                  <p><span className="font-semibold">Host license ID:</span> <span className="font-mono text-xs">{status.hostLicenseId ?? "Not set"}</span></p>
                  <p><span className="font-semibold">Terminal ID:</span> <span className="font-mono text-xs">{status.terminalId ?? "Not set"}</span></p>
                </>
              ) : null}
            </CardBody>
          </Card>

          <Card>
            <CardHeader><CardTitle>Entitlements</CardTitle></CardHeader>
            <CardBody className="space-y-2 text-sm text-slate-700">
              <p><span className="font-semibold">Features:</span> {status.enabledFeatures.length > 0 ? status.enabledFeatures.join(", ") : "None"}</p>
              <p><span className="font-semibold">LAN licensing:</span> Each operational LAN terminal uses its own machine-bound Terminal license.</p>
              {typeLabel === "Terminal" ? (
                <p className="text-xs text-amber-800">Terminal mode does not unlock full host POS, reports admin, backup host controls, or user management as host features.</p>
              ) : null}
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
            Import replaces the local license file only after the backend verifies the signed Host or Terminal document. Invalid, unsigned, expired, unsupported, path-bearing, empty-binding, type-invalid, or machine-mismatched licenses are rejected. Contact {BETA_CONTACT_EMAIL} for issuance.
          </Alert>
          <label className="space-y-1 text-sm font-medium text-slate-700">
            <span>Paste signed license JSON</span>
            <textarea
              value={licenseText}
              onChange={(event) => setLicenseText(event.target.value)}
              className="min-h-48 w-full rounded-md border border-slate-300 bg-white px-3 py-2 font-mono text-xs text-slate-900 shadow-sm focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 focus-visible:ring-offset-2"
              placeholder="Paste the signed Host or Terminal license JSON here. No cloud activation or file path is required."
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
