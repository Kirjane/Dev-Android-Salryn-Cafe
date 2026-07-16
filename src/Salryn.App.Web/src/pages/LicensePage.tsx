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

function deviceTypeLabel(typeLabel: string): string {
  if (typeLabel === "Host") return "Host POS";
  if (typeLabel === "Terminal") return "LAN Terminal";
  return typeLabel;
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

function friendlyInstallError(raw: string | null | undefined): string {
  if (!raw) return "License was rejected.";
  const lower = raw.toLowerCase();
  if (lower.includes("machine")) return "This license does not match this device. Contact " + BETA_CONTACT_EMAIL + " if you need a reissue.";
  if (lower.includes("signature") || lower.includes("unsigned")) return "This license could not be verified. Ask Kirjane Labs for a new signed license file.";
  if (lower.includes("expired")) return "This license has expired. Contact " + BETA_CONTACT_EMAIL + " for help.";
  if (lower.includes("terminal") && lower.includes("host")) return "A Terminal license cannot activate Host POS setup on this device.";
  if (lower.includes("host") && lower.includes("terminal")) return "A Host license cannot be used as a Terminal license.";
  if (lower.includes("malformed") || lower.includes("json")) return "The license text is not valid. Paste the full signed license file you received.";
  return "This license could not be installed. Contact " + BETA_CONTACT_EMAIL + " for help.";
}

export function LicensePage() {
  const statusQuery = useQuery({ queryKey: ["license-status"], queryFn: fetchLicenseStatus, retry: false });
  const [licenseText, setLicenseText] = useState("");
  const [replaceConfirmed, setReplaceConfirmed] = useState(false);
  const [showSupportDetails, setShowSupportDetails] = useState(false);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const status = statusQuery.data;
  const canInstall = licenseText.trim().length > 0 && replaceConfirmed && !busy;
  const currentStatusLabel = status ? statusLabel(status.status) : null;
  const typeLabel = licenseTypeLabel(status?.licenseType);
  const machineCode = status?.machineCode ?? status?.machineFingerprint ?? "Not available";
  const isLicensed = currentStatusLabel === "Licensed" && !!status?.machineMatched;
  const showActivation = needsActivation(currentStatusLabel) || !status?.machineMatched || typeLabel !== "Host";
  const hostSetupBlocked = currentStatusLabel === "Licensed" && typeLabel !== "Host";
  const showActiveSummary = isLicensed && (typeLabel === "Host" || typeLabel === "Terminal");

  async function handleInstall() {
    if (!canInstall) return;
    setBusy(true);
    setMessage(null);
    setError(null);
    try {
      const payload = licenseText.trim();
      const result = await installLicense(payload);
      if (result.installStatus === "Installed") {
        setMessage("License installed. Salryn works offline after a valid signed license is installed.");
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
      } else {
        setError(friendlyInstallError(result.errorMessage));
      }
      await statusQuery.refetch();
    } catch (e) {
      setError(friendlyInstallError((e as Error).message));
    } finally {
      setBusy(false);
    }
  }

  async function copyMachineCode() {
    if (!machineCode || machineCode === "Not available") return;
    try {
      await navigator.clipboard.writeText(machineCode);
      setMessage("Machine Code copied.");
    } catch {
      setError("Could not copy Machine Code. Select and copy it manually.");
    }
  }

  return (
    <div className="mx-auto max-w-3xl space-y-5">
      <CommandBar
        title="Activate Salryn"
        subtitle="Salryn needs a valid license before this device can be used."
        status={status ? <StatusPill tone={statusTone(status.status)}>{showActiveSummary ? "Licensed" : currentStatusLabel}</StatusPill> : null}
        actions={
          <Button variant="secondary" disabled={statusQuery.isFetching || busy} onClick={() => void statusQuery.refetch()}>
            Refresh
          </Button>
        }
      />

      {statusQuery.isLoading ? <LoadingState label="Loading…" /> : null}
      {statusQuery.isError ? <Alert tone="danger">{(statusQuery.error as Error).message}</Alert> : null}
      {message ? <Alert tone="success">{message}</Alert> : null}
      {error ? <Alert tone="danger">{error}</Alert> : null}
      {hostSetupBlocked ? (
        <Alert tone="danger" title="Host license required">
          This Terminal license cannot unlock Host first-run setup or the full Host application. Install a valid Host license for this workstation.
        </Alert>
      ) : null}

      {showActiveSummary && status ? (
        <Card>
          <CardHeader>
            <CardTitle>License Active</CardTitle>
          </CardHeader>
          <CardBody className="space-y-3 text-sm text-slate-700">
            <p>
              <span className="font-semibold text-slate-900">Licensed to:</span> {status.customerName ?? "Not available"}
            </p>
            <p>
              <span className="font-semibold text-slate-900">Device type:</span> {deviceTypeLabel(typeLabel)}
            </p>
            <p>
              <span className="font-semibold text-slate-900">Machine Code:</span>{" "}
              <span className="break-all font-mono text-xs font-semibold text-navy-900">{machineCode}</span>
            </p>
            <p>
              <span className="font-semibold text-slate-900">Support until:</span> {formatDate(status.supportExpiresAt)}
            </p>
            <p>
              <span className="font-semibold text-slate-900">Updates until:</span> {formatDate(status.updatesUntil)}
            </p>
            {typeLabel === "Terminal" ? (
              <>
                <p>
                  <span className="font-semibold text-slate-900">Host License ID:</span>{" "}
                  <span className="font-mono text-xs">{status.hostLicenseId ?? "Not set"}</span>
                </p>
                <p>
                  <span className="font-semibold text-slate-900">Terminal ID:</span>{" "}
                  <span className="font-mono text-xs">{status.terminalId ?? "Not set"}</span>
                </p>
              </>
            ) : null}
            <p className="text-xs text-slate-500">
              Salryn works offline after a valid signed license is installed. Each operational LAN terminal uses its own machine-bound Terminal license.
            </p>
            <button
              type="button"
              className="text-xs font-semibold text-blue-700 underline-offset-2 hover:underline"
              onClick={() => setShowSupportDetails((value) => !value)}
            >
              {showSupportDetails ? "Hide support details" : "Show support details"}
            </button>
            {showSupportDetails ? (
              <div className="rounded-md border border-slate-200 bg-slate-50 px-3 py-2 text-xs text-slate-600">
                <p>License ID: <span className="font-mono">{status.licenseId ?? "Not installed"}</span></p>
                <p>Store ID: <span className="font-mono">{status.storeId ?? "Not specified"}</span></p>
                <p>Application: {status.applicationId ?? "Not specified"}</p>
                <p>Machine matched: {status.machineMatched ? "Yes" : "No"}</p>
              </div>
            ) : null}
          </CardBody>
        </Card>
      ) : null}

      {showActivation ? (
        <Card>
          <CardHeader>
            <CardTitle>Send this Machine Code to:</CardTitle>
          </CardHeader>
          <CardBody className="space-y-4 text-sm text-slate-700">
            <p className="font-mono text-base font-bold text-navy-900">{BETA_CONTACT_EMAIL}</p>

            <div className="rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3">
              <p className="text-xs font-semibold uppercase tracking-wide text-emerald-900">Machine Code</p>
              <p className="mt-1 break-all font-mono text-lg font-bold tracking-wide text-navy-900">{machineCode}</p>
              <div className="mt-3">
                <Button variant="secondary" size="sm" disabled={machineCode === "Not available"} onClick={() => void copyMachineCode()}>
                  Copy Machine Code
                </Button>
              </div>
            </div>

            <div>
              <p className="font-semibold text-slate-900">What to send</p>
              <div className="mt-3 grid gap-3 md:grid-cols-2">
                <div className="rounded-md border border-slate-200 bg-white p-3">
                  <p className="font-semibold text-slate-900">For Host POS activation</p>
                  <ul className="mt-2 list-disc space-y-1 pl-5">
                    <li>Store / business name</li>
                    <li>Contact person</li>
                    <li>Contact number or email</li>
                    <li>Host Machine Code</li>
                  </ul>
                </div>
                <div className="rounded-md border border-slate-200 bg-white p-3">
                  <p className="font-semibold text-slate-900">For LAN Terminal activation</p>
                  <ul className="mt-2 list-disc space-y-1 pl-5">
                    <li>Store / business name</li>
                    <li>Host License ID, if available</li>
                    <li>Terminal name or number</li>
                    <li>Terminal Machine Code</li>
                  </ul>
                </div>
              </div>
            </div>

            <p>
              After your license is issued, paste the signed license below. Each LAN terminal needs its own Terminal license — not a shared Host license.
            </p>
          </CardBody>
        </Card>
      ) : null}

      <Card>
        <CardHeader>
          <CardTitle>Import License</CardTitle>
        </CardHeader>
        <CardBody className="space-y-4">
          <p className="text-sm text-slate-600">
            Importing a license replaces the current license on this device only after Salryn verifies it.
          </p>
          <label className="space-y-1 text-sm font-medium text-slate-700">
            <span>Paste signed license JSON</span>
            <textarea
              value={licenseText}
              onChange={(event) => setLicenseText(event.target.value)}
              className="min-h-40 w-full rounded-md border border-slate-300 bg-white px-3 py-2 font-mono text-xs text-slate-900 shadow-sm focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 focus-visible:ring-offset-2"
              placeholder="Paste the signed license you received from Kirjane Labs."
            />
          </label>
          <label className="flex items-start gap-2 text-sm text-slate-700">
            <input
              type="checkbox"
              checked={replaceConfirmed}
              onChange={(event) => setReplaceConfirmed(event.target.checked)}
              className="mt-1"
            />
            <span>I understand this will replace the current license on this device.</span>
          </label>
          <div className="flex flex-wrap gap-2">
            <Button disabled={!canInstall} onClick={() => void handleInstall()}>
              {busy ? "Installing…" : "Install License"}
            </Button>
            <Button
              variant="secondary"
              disabled={busy || (!licenseText && !replaceConfirmed)}
              onClick={() => {
                setLicenseText("");
                setReplaceConfirmed(false);
              }}
            >
              Clear
            </Button>
          </div>
        </CardBody>
      </Card>
    </div>
  );
}
