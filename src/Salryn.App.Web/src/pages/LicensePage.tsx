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

function friendlyInstallError(raw: string | null | undefined, code?: string | null): string {
  const contact = " Contact " + BETA_CONTACT_EMAIL + " if you need a reissue.";
  const lower = (raw ?? "").trim().toLowerCase();
  const codeLower = (code ?? "").toLowerCase();

  if (codeLower.includes("callerpath") || lower.includes("caller filesystem") || lower.includes("does not accept caller")) {
    return "That license request is not valid. Paste only the signed license text — not a file path.";
  }
  if (codeLower.includes("unsupportedfield") || lower.includes("unsupported field")) {
    return "That license file has unexpected fields. Ask Kirjane Labs for a new signed license.";
  }
  if (codeLower.includes("emptydocument") || lower.includes("content is required")) {
    return "Paste the full signed license you received before installing.";
  }
  if (codeLower.includes("malformed") || lower.includes("malformed") || lower.includes("json did not") || lower.includes("json is")) {
    return "The license text is not valid. Paste the full signed license file you received.";
  }

  if (lower.includes("machinebinding is required") || (lower.includes("machinebinding") && lower.includes("empty"))) {
    return "This license is missing a device binding. Ask Kirjane Labs for a new signed license.";
  }
  if (lower.includes("does not match this installation") || lower.includes("machine binding does not match")) {
    return "This license does not match this device." + contact;
  }

  if (lower.includes("unsigned") || lower.includes("signature is invalid") || lower.includes("signature verifier") || lower.includes("fails closed")) {
    return "This license could not be verified. Ask Kirjane Labs for a new signed license file.";
  }
  if (lower.includes("signature")) {
    return "This license could not be verified. Ask Kirjane Labs for a new signed license file.";
  }

  if (lower.includes("expired") && !lower.includes("grace")) {
    return "This license has expired." + contact;
  }
  if (lower.includes("schema")) {
    return "This license format is not supported by this Salryn version. Ask Kirjane Labs for an updated license.";
  }
  if (lower.includes("applicationid")) {
    return "This license is not for Salryn POS. Ask Kirjane Labs for the correct license.";
  }
  if (lower.includes("storeid") || lower.includes("identity fields")) {
    return "This license is missing required store or customer details. Ask Kirjane Labs for a new signed license.";
  }
  if (lower.includes("edition is not recognized")) {
    return "This license edition is not recognized. Ask Kirjane Labs for a new signed license.";
  }

  if (lower.includes("must not include hostlicenseid") || lower.includes("must not include terminalid")) {
    return "This Host license has Terminal-only fields. Ask Kirjane Labs for a correct Host license.";
  }
  if (lower.includes("must not include allowedlanterminals")) {
    return "This Terminal license has Host-only fields. Ask Kirjane Labs for a correct Terminal license.";
  }
  if (lower.includes("must include hostlicenseid") || lower.includes("must include terminalid")) {
    return "This Terminal license is incomplete. Ask Kirjane Labs for a new Terminal license.";
  }
  if (lower.includes("must include allowedlanterminals") || lower.includes("lan terminal count must not be negative")) {
    return "This Host license is incomplete. Ask Kirjane Labs for a new Host license.";
  }
  if (lower.includes("licensetype") && (lower.includes("missing") || lower.includes("not recognized"))) {
    return "This license does not say whether it is for Host POS or a LAN Terminal. Ask Kirjane Labs for a new signed license.";
  }
  if (lower.includes("terminal license must enable") || lower.includes("does not enable any known features")) {
    return "This license does not include the features needed for this device. Ask Kirjane Labs for a new signed license.";
  }
  if (lower.includes("does not unlock host") || (lower.includes("terminal license is valid") && lower.includes("host"))) {
    return "A Terminal license cannot activate Host POS setup on this device.";
  }

  if (lower.includes("request failed") || lower.includes("failed (")) {
    return "License install failed. Check your connection to this device and try again.";
  }

  return "This license could not be installed." + contact;
}

export function LicensePage() {
  const statusQuery = useQuery({ queryKey: ["license-status"], queryFn: fetchLicenseStatus, retry: false });
  const [licenseText, setLicenseText] = useState("");
  const [replaceConfirmed, setReplaceConfirmed] = useState(false);
  const [showReplaceLicense, setShowReplaceLicense] = useState(false);
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
        setLicenseText("");
        setReplaceConfirmed(false);
        setShowReplaceLicense(false);
      } else {
        setError(friendlyInstallError(result.errorMessage, result.errorCode));
      }
      await statusQuery.refetch();
    } catch (e) {
      const err = e as Error & { code?: string };
      setError(friendlyInstallError(err.message, err.code));
    } finally {
      setBusy(false);
    }
  }

  async function copyMachineCode() {
    if (!machineCode || machineCode === "Not available") return;
    try {
      await navigator.clipboard.writeText(machineCode);
      setMessage("Host Machine Code copied.");
    } catch {
      setError("Could not copy Host Machine Code. Select and copy it manually.");
    }
  }

  return (
    <div className="mx-auto max-w-3xl space-y-5">
      <CommandBar
        title={showActiveSummary ? "License" : "Activate Salryn"}
        subtitle={
          showActiveSummary
            ? "License Active on this Host workstation."
            : "Salryn needs a valid Host license before this device can be used."
        }
        status={status ? <StatusPill tone={statusTone(status.status)}>{showActiveSummary ? "Active" : currentStatusLabel}</StatusPill> : null}
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
            <CardTitle>Status: Active</CardTitle>
          </CardHeader>
          <CardBody className="space-y-3 text-sm text-slate-700">
            <p>
              <span className="font-semibold text-slate-900">Licensed to:</span> {status.customerName ?? "Not available"}
            </p>
            <p>
              <span className="font-semibold text-slate-900">Device type:</span> {deviceTypeLabel(typeLabel)}
            </p>
            <p>
              <span className="font-semibold text-slate-900">Host Machine Code:</span>{" "}
              <span className="break-all font-mono text-xs font-semibold text-navy-900">{machineCode}</span>
            </p>
            <p>
              <span className="font-semibold text-slate-900">Support until:</span> {formatDate(status.supportExpiresAt)}
            </p>
            <p>
              <span className="font-semibold text-slate-900">Updates until:</span> {formatDate(status.updatesUntil)}
            </p>
            <p className="text-xs text-slate-500">Works offline after activation.</p>
            <button
              type="button"
              className="text-xs font-semibold text-blue-700 underline-offset-2 hover:underline"
              onClick={() => setShowSupportDetails((value) => !value)}
            >
              {showSupportDetails ? "Hide support details" : "Show support details"}
            </button>
            {showSupportDetails ? (
              <div className="rounded-md border border-slate-200 bg-slate-50 px-3 py-2 text-xs text-slate-600">
                <p>Host License ID: <span className="font-mono">{status.licenseId ?? "Not installed"}</span></p>
                <p>Store (internal): <span className="font-mono">{status.storeId ?? "Not specified"}</span></p>
                <p>Application: {status.applicationId ?? "Not specified"}</p>
                <p>Machine matched: {status.machineMatched ? "Yes" : "No"}</p>
                {typeLabel === "Terminal" ? (
                  <>
                    <p>Host License ID (linked): <span className="font-mono">{status.hostLicenseId ?? "Not set"}</span></p>
                    <p>Terminal ID: <span className="font-mono">{status.terminalId ?? "Not set"}</span></p>
                  </>
                ) : null}
              </div>
            ) : null}
          </CardBody>
        </Card>
      ) : null}

      {showActivation ? (
        <Card>
          <CardHeader>
            <CardTitle>Host Machine Code</CardTitle>
          </CardHeader>
          <CardBody className="space-y-4 text-sm text-slate-700">
            <Alert tone="info" title="How activation works">
              Salryn generates this Machine Code. LicensingOps signs the Host license for it. You do not invent a Store ID or Host License ID on this screen.
            </Alert>

            <div className="rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3">
              <p className="text-xs font-semibold uppercase tracking-wide text-emerald-900">Host Machine Code</p>
              <p className="mt-1 break-all font-mono text-lg font-bold tracking-wide text-navy-900">{machineCode}</p>
              <div className="mt-3">
                <Button variant="secondary" size="sm" disabled={machineCode === "Not available"} onClick={() => void copyMachineCode()}>
                  Copy Host Machine Code
                </Button>
              </div>
            </div>

            <div>
              <p className="font-semibold text-slate-900">For Host POS activation</p>
              <ol className="mt-2 list-decimal space-y-1 pl-5">
                <li>Copy the Host Machine Code above.</li>
                <li>
                  Open LicensingOps (or email <span className="font-mono font-semibold">{BETA_CONTACT_EMAIL}</span>).
                </li>
                <li>Create or select your Store (Store ID is created for you).</li>
                <li>Paste this Host Machine Code and issue a Host license.</li>
                <li>Paste the signed Host license JSON below.</li>
              </ol>
              <ul className="mt-3 list-disc space-y-1 pl-5 text-slate-600">
                <li>Store / business name</li>
                <li>Contact person</li>
                <li>Contact number or email</li>
                <li>Host Machine Code</li>
              </ul>
            </div>

            <div>
              <p className="font-semibold text-slate-900">For LAN Terminal activation</p>
              <p className="mt-1 text-slate-600">
                On each terminal device, open Terminal Activation and copy that device&apos;s Terminal Machine Code.
                Your store admin or Kirjane Labs issues a Terminal license from the Host license — terminal users do not need a Host License ID.
              </p>
              <ul className="mt-2 list-disc space-y-1 pl-5 text-slate-600">
                <li>Terminal name or number (for example, Counter 1)</li>
                <li>Terminal Machine Code from that device</li>
              </ul>
            </div>

            <p>
              After your license is issued, paste the signed license below. Each LAN terminal needs its own Terminal license — not a shared Host license.
            </p>
          </CardBody>
        </Card>
      ) : null}

      {showActiveSummary && !showReplaceLicense ? (
        <div>
          <Button variant="secondary" onClick={() => setShowReplaceLicense(true)}>Replace License</Button>
        </div>
      ) : null}

      {!showActiveSummary || showReplaceLicense ? (
        <Card>
          <CardHeader>
            <CardTitle>Import License</CardTitle>
          </CardHeader>
          <CardBody className="space-y-4">
            <p className="text-sm text-slate-600">
              Paste only the signed Host license JSON you received. Importing replaces the current license on this device after Salryn verifies it.
            </p>
            <label className="space-y-1 text-sm font-medium text-slate-700">
              <span>Paste signed license JSON</span>
              <textarea
                value={licenseText}
                onChange={(event) => setLicenseText(event.target.value)}
                className="min-h-40 w-full rounded-md border border-slate-300 bg-white px-3 py-2 font-mono text-xs text-slate-900 shadow-sm focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 focus-visible:ring-offset-2"
                placeholder="Paste the signed Host license you received from LicensingOps or Kirjane Labs."
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
      ) : null}
    </div>
  );
}
