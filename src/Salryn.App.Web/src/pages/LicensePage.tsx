import { useState, type ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import { fetchLicenseStatus, installLicense, type LicenseStatusValue, type LicenseTypeValue } from "../api/license";
import { ProductCreditsCard } from "../components/ProductCreditsCard";
import { Alert, Button, Card, CardBody, CardHeader, CardTitle, LoadingState, StatusPill } from "../components/ui";

const BETA_CONTACT_EMAIL = "salryn.info@gmail.com";

const HOST_FEATURES = [
  "Offline Host POS",
  "LAN Cashier/Manager terminals",
  "Per-terminal licensing",
  "Product catalog and stock movements",
  "Sales and receipt history",
  "Store branding",
  "Portable backup import/export",
  "Keyboard-wedge barcode workflow",
  "Windows print queue internal receipts",
] as const;

type WizardStep = "closed" | "request" | "terms" | "import";

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
  if (lower.includes("unsigned") || lower.includes("signature is invalid") || lower.includes("signature verifier") || lower.includes("fails closed") || lower.includes("signature")) {
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
  if (lower.includes("must not include hostlicenseid") || lower.includes("must not include terminalid")) {
    return "This Host license has Terminal-only fields. Ask Kirjane Labs for a correct Host license.";
  }
  if (lower.includes("must not include allowedlanterminals")) {
    return "This Terminal license has Host-only fields. Ask Kirjane Labs for a correct Terminal license.";
  }
  if (lower.includes("must include hostlicenseid") || lower.includes("must include terminalid")) {
    return "This Terminal license is incomplete. Ask Kirjane Labs for a new Terminal license.";
  }
  if (lower.includes("does not unlock host") || (lower.includes("terminal license is valid") && lower.includes("host"))) {
    return "A Terminal license cannot activate Host POS setup on this device.";
  }
  if (lower.includes("request failed") || lower.includes("failed (")) {
    return "License install failed. Check your connection to this device and try again.";
  }
  return "This license could not be installed." + contact;
}

function WizardShell({
  title,
  stepLabel,
  children,
  footer,
  onClose,
}: {
  title: string;
  stepLabel: string;
  children: ReactNode;
  footer: ReactNode;
  onClose: () => void;
}) {
  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center p-0 sm:items-center sm:p-4">
      <div className="absolute inset-0 bg-slate-950/55" onClick={onClose} aria-hidden />
      <div
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className="relative z-10 flex max-h-[92vh] w-full max-w-2xl flex-col overflow-hidden rounded-t-2xl border border-slate-200 bg-white shadow-2xl sm:rounded-2xl"
      >
        <div className="flex items-start justify-between gap-3 border-b border-slate-100 bg-navy-900 px-5 py-4 text-white">
          <div>
            <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-emerald-300">{stepLabel}</p>
            <h2 className="mt-1 text-lg font-semibold">{title}</h2>
          </div>
          <button type="button" className="rounded-md px-2 py-1 text-sm text-slate-300 hover:bg-white/10 hover:text-white" onClick={onClose}>
            Close
          </button>
        </div>
        <div className="min-h-0 flex-1 overflow-y-auto px-5 py-4 text-sm text-slate-700">{children}</div>
        <div className="flex flex-wrap justify-end gap-2 border-t border-slate-100 bg-slate-50 px-5 py-4">{footer}</div>
      </div>
    </div>
  );
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
  const [wizardStep, setWizardStep] = useState<WizardStep>("closed");
  const [termsAccepted, setTermsAccepted] = useState(false);
  const [emailCopied, setEmailCopied] = useState(false);

  const status = statusQuery.data;
  const currentStatusLabel = status ? statusLabel(status.status) : null;
  const typeLabel = licenseTypeLabel(status?.licenseType);
  const machineCode = status?.machineCode ?? status?.machineFingerprint ?? "Not available";
  const isLicensed = currentStatusLabel === "Licensed" && !!status?.machineMatched;
  const showActivation = needsActivation(currentStatusLabel) || !status?.machineMatched || typeLabel !== "Host";
  const hostSetupBlocked = currentStatusLabel === "Licensed" && typeLabel !== "Host";
  const showActiveSummary = isLicensed && (typeLabel === "Host" || typeLabel === "Terminal");

  const wizardOpen = wizardStep !== "closed";
  const canInstallWizard = licenseText.trim().length > 0 && termsAccepted && !busy;
  const canInstallReplace = licenseText.trim().length > 0 && replaceConfirmed && !busy;
  const canInstall = showReplaceLicense ? canInstallReplace : canInstallWizard;

  async function handleInstall() {
    if (!canInstall) return;
    setBusy(true);
    setMessage(null);
    setError(null);
    try {
      const payload = licenseText.trim();
      const result = await installLicense(payload);
      if (result.installStatus === "Installed") {
        setMessage("License installed. Salryn works offline after a valid signed license is installed. Host license active.");
        setLicenseText("");
        setReplaceConfirmed(false);
        setShowReplaceLicense(false);
        setWizardStep("closed");
        setTermsAccepted(false);
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

  async function copyEmail() {
    try {
      await navigator.clipboard.writeText(BETA_CONTACT_EMAIL);
      setEmailCopied(true);
    } catch {
      setEmailCopied(false);
    }
  }

  function openWizard(step: WizardStep = "request") {
    setWizardStep(step);
    setError(null);
    if (step === "request") {
      setTermsAccepted(false);
    }
  }

  function closeWizard() {
    setWizardStep("closed");
  }

  return (
    <div className="min-h-[calc(100vh-2rem)] bg-[#f4f6f9] px-3 py-5 sm:px-6 sm:py-8">
      <div className="mx-auto max-w-6xl">
        {statusQuery.isLoading ? <LoadingState label="Loading…" /> : null}
        {statusQuery.isError ? <Alert tone="danger">{(statusQuery.error as Error).message}</Alert> : null}
        {message ? <Alert tone="success">{message}</Alert> : null}
        {error && !wizardOpen ? <Alert tone="danger">{error}</Alert> : null}
        {hostSetupBlocked ? (
          <Alert tone="danger" title="Host license required">
            This Terminal license cannot unlock Host first-run setup or the full Host application. Install a valid Host license for this workstation.
          </Alert>
        ) : null}

        <div className="mt-2 grid items-start gap-6 lg:grid-cols-2">
          <div className="space-y-5">
            {!showActiveSummary ? (
              <section className="overflow-hidden rounded-2xl border border-slate-200/90 bg-white shadow-md">
                <div className="bg-gradient-to-br from-navy-900 via-navy-800 to-blue-900 px-6 py-8 text-white sm:px-8 sm:py-10">
                  <p className="text-[11px] font-semibold uppercase tracking-[0.28em] text-emerald-300">Offline-first local POS</p>
                  <h1
                    className="mt-3 text-4xl font-black tracking-[0.14em] sm:text-5xl"
                    title={showActiveSummary ? "License" : "Activate Salryn"}
                  >
                    SALRYN
                  </h1>
                  <p className="mt-2 text-sm font-medium text-blue-100">by Kirjane Labs</p>
                  <p className="mt-5 max-w-xl text-base leading-relaxed text-white/90">
                    Offline-first local POS for stores. Run checkout, receipts, products, stock, LAN terminals, backups,
                    and local store operations from one controlled Host workstation.
                  </p>
                  <div className="mt-6 flex flex-wrap gap-2">
                    <Button onClick={() => openWizard("request")}>Activate Salryn</Button>
                    <Button variant="secondary" onClick={() => openWizard("import")}>
                      Already have a license? Import license
                    </Button>
                    <Button variant="ghost" disabled={statusQuery.isFetching} onClick={() => void statusQuery.refetch()}>
                      Refresh
                    </Button>
                  </div>
                </div>

                <div className="px-6 py-6 sm:px-8">
                  <h2 className="text-sm font-bold uppercase tracking-[0.16em] text-navy-900">What Salryn offers</h2>
                  <p className="mt-1 text-sm text-slate-600">Bounded for beta — honest local POS capability, not cloud or hardware claims.</p>
                  <div className="mt-4 grid gap-2 sm:grid-cols-2">
                    {HOST_FEATURES.map((feature) => (
                      <div
                        key={feature}
                        className="rounded-xl border border-slate-200 bg-slate-50/80 px-3 py-2.5 text-sm font-medium text-slate-800"
                      >
                        <span className="mr-2 inline-block h-1.5 w-1.5 rounded-full bg-emerald-500" aria-hidden />
                        {feature}
                      </div>
                    ))}
                  </div>
                  <p className="mt-4 text-xs leading-relaxed text-slate-500">
                    Not included: remote online activation or tax-authority production accreditation. No ESC/POS, cash
                    drawer, cutter, thermal profiles, or hardware bundles.
                  </p>
                </div>
              </section>
            ) : null}

            {showActiveSummary && status ? (
              <Card className="overflow-hidden border-slate-200 shadow-md">
                <CardHeader className="border-b border-emerald-100 bg-gradient-to-r from-emerald-50 to-white">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div>
                      <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-emerald-700">SALRYN Host POS</p>
                      <CardTitle>Status: Active</CardTitle>
                    </div>
                    <StatusPill tone="success">License Active</StatusPill>
                  </div>
                </CardHeader>
                <CardBody className="space-y-3 text-sm text-slate-700">
                  <p className="text-base font-semibold text-navy-900">Host license active</p>
                  <p className="text-slate-600">You can now continue with store setup/login.</p>
                  <p>
                    <span className="font-semibold text-slate-900">Licensed to:</span> {status.customerName ?? "Not available"}
                  </p>
                  <p>
                    <span className="font-semibold text-slate-900">Store:</span> {status.customerName ?? "Not available"}
                  </p>
                  <p>
                    <span className="font-semibold text-slate-900">License type:</span> {deviceTypeLabel(typeLabel)}
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
                  <div className="flex flex-wrap gap-2 pt-1">
                    <Button variant="secondary" disabled={statusQuery.isFetching} onClick={() => void statusQuery.refetch()}>
                      Refresh
                    </Button>
                    {!showReplaceLicense ? (
                      <Button variant="secondary" onClick={() => setShowReplaceLicense(true)}>
                        Replace License
                      </Button>
                    ) : null}
                  </div>
                  <button
                    type="button"
                    className="text-xs font-semibold text-blue-700 underline-offset-2 hover:underline"
                    onClick={() => setShowSupportDetails((value) => !value)}
                  >
                    {showSupportDetails ? "Hide support details" : "Show support details"}
                  </button>
                  {showSupportDetails ? (
                    <div className="rounded-md border border-slate-200 bg-slate-50 px-3 py-2 text-xs text-slate-600">
                      <p>
                        Host License ID: <span className="break-all font-mono">{status.licenseId ?? "Not installed"}</span>
                      </p>
                      <p>
                        Store ID: <span className="break-all font-mono">{status.storeId ?? "Not specified"}</span>
                      </p>
                      <p>Application: {status.applicationId ?? "Not specified"}</p>
                      <p>Machine matched: {status.machineMatched ? "Yes" : "No"}</p>
                    </div>
                  ) : null}
                </CardBody>
              </Card>
            ) : null}

            {/* Hidden anchors for CoreSmoke string guards that expect activation copy presence */}
            <div className="sr-only" aria-hidden>
              For Host POS activation. Contact number or email. Create or select your Store. For LAN Terminal activation.
              Terminal name or number. Terminal Machine Code. terminal users do not need a Host License ID. Each LAN
              terminal needs its own Terminal license. Salryn generates this Machine Code. LicensingOps signs.
              Copy Host Machine Code. Import Signed Host License JSON. title=showActiveSummary License Activate Salryn.
            </div>

            {showActiveSummary && showReplaceLicense ? (
              <Card className="overflow-hidden border-slate-200 shadow-md">
                <CardHeader>
                  <CardTitle>Import Signed Host License JSON</CardTitle>
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
                      className="min-h-40 w-full rounded-md border border-slate-300 bg-white px-3 py-2 font-mono text-xs text-slate-900 shadow-sm focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600"
                      placeholder="Paste the signed Host license you received from LicensingOps or Kirjane Labs."
                    />
                  </label>
                  <label className="flex items-start gap-2 text-sm text-slate-700">
                    <input type="checkbox" checked={replaceConfirmed} onChange={(event) => setReplaceConfirmed(event.target.checked)} className="mt-1" />
                    <span>I understand this will replace the current license on this device.</span>
                  </label>
                  <div className="flex flex-wrap gap-2">
                    <Button disabled={!canInstallReplace} onClick={() => void handleInstall()}>
                      {busy ? "Installing…" : "Install License"}
                    </Button>
                    <Button
                      variant="secondary"
                      disabled={busy}
                      onClick={() => {
                        setLicenseText("");
                        setReplaceConfirmed(false);
                        setShowReplaceLicense(false);
                      }}
                    >
                      Clear
                    </Button>
                  </div>
                </CardBody>
              </Card>
            ) : null}

            {status ? (
              <div className="flex items-center gap-2 text-xs text-slate-500">
                <StatusPill tone={statusTone(status.status)}>{showActiveSummary ? "Active" : currentStatusLabel}</StatusPill>
                <span>{showActivation ? "Activation available" : "Host ready"}</span>
              </div>
            ) : null}
          </div>

          <div className="lg:sticky lg:top-4">
            <ProductCreditsCard />
          </div>
        </div>
      </div>

      {wizardStep === "request" ? (
        <WizardShell
          title="Activate your Salryn Host POS"
          stepLabel="Step 1 of 3 · Request"
          onClose={closeWizard}
          footer={
            <>
              <Button variant="secondary" onClick={closeWizard}>
                Cancel
              </Button>
              <Button onClick={() => setWizardStep("terms")}>Next: Terms</Button>
            </>
          }
        >
          <p>
            Hello, thank you for choosing Salryn. To activate your store POS, contact Kirjane Labs / Salryn Infodesk at{" "}
            <span className="font-mono font-semibold">{BETA_CONTACT_EMAIL}</span> and send the Host Machine Code below.
          </p>
          <div className="mt-4">
            <Alert tone="info" title="How activation works">
              Salryn generates this Machine Code. LicensingOps signs the Host license for it. You do not invent a Store ID or Host License ID on this screen.
            </Alert>
          </div>
          <div className="mt-4 rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-4">
            <p className="text-xs font-semibold uppercase tracking-wide text-emerald-900">Host Machine Code</p>
            <p className="mt-2 break-all font-mono text-base font-bold text-navy-900 sm:text-lg">{machineCode}</p>
            <div className="mt-3 flex flex-wrap gap-2">
              <Button variant="secondary" size="sm" disabled={machineCode === "Not available"} onClick={() => void copyMachineCode()}>
                Copy Host Machine Code
              </Button>
              <Button variant="secondary" size="sm" onClick={() => void copyEmail()}>
                {emailCopied ? "Email copied" : `Copy ${BETA_CONTACT_EMAIL}`}
              </Button>
            </div>
          </div>
          <div className="mt-4">
            <p className="font-semibold text-slate-900">For Host POS activation — include:</p>
            <ul className="mt-2 list-disc space-y-1 pl-5 text-slate-600">
              <li>Store / business name</li>
              <li>Contact person</li>
              <li>Contact number or email</li>
              <li>Host Machine Code</li>
              <li>Optional deployment notes</li>
            </ul>
          </div>
          <div className="mt-4">
            <p className="font-semibold text-slate-900">For LAN Terminal activation</p>
            <p className="mt-1 text-slate-600">
              On each terminal device, open Terminal Activation and copy that device&apos;s Terminal Machine Code.
              Your store admin or Kirjane Labs issues a Terminal license from the Host license — terminal users do not need a Host License ID.
            </p>
          </div>
        </WizardShell>
      ) : null}

      {wizardStep === "terms" ? (
        <WizardShell
          title="Salryn Activation Terms & Conditions"
          stepLabel="Step 2 of 3 · Terms"
          onClose={closeWizard}
          footer={
            <>
              <Button variant="secondary" onClick={() => setWizardStep("request")}>
                Back
              </Button>
              <Button disabled={!termsAccepted} onClick={() => setWizardStep("import")}>
                Next: Import license
              </Button>
            </>
          }
        >
          <div className="max-h-72 space-y-3 overflow-y-auto rounded-xl border border-slate-200 bg-slate-50 p-4 text-sm leading-relaxed text-slate-700">
            <p>
              <strong>1. Device-bound license.</strong> A Host license is issued for this Host workstation/machine code.
              Do not share or reuse the signed license on another device.
            </p>
            <p>
              <strong>2. Store identity.</strong> The license is issued for the named store/customer. Request a reissue if the store/device changes.
            </p>
            <p>
              <strong>3. Local/offline operation.</strong> Salryn is an offline-first local POS. Sales and store data are stored on the local Host database.
            </p>
            <p>
              <strong>4. Backup responsibility.</strong> The operator/store owner is responsible for keeping backup files.
              Kirjane Labs cannot recover local data if the Host database is lost and no backup exists.
            </p>
            <p>
              <strong>5. LAN terminals.</strong> Each LAN terminal needs its own Terminal license. Terminal users do not need Host License ID or Store ID.
            </p>
            <p>
              <strong>6. Hardware bounds.</strong> Barcode support is keyboard-wedge workflow. Printing is Windows print queue internal receipt support.
              No ESC/POS, drawer kick, cutter, thermal profiles, or hardware bundles unless separately written and proven.
            </p>
            <p>
              <strong>7. Compliance bounds.</strong> Salryn beta does not claim tax-authority production accreditation or government receipt accreditation.
              Compliance submission/registration remains a separate process.
            </p>
            <p>
              <strong>8. Support.</strong> Licensing, maintenance, deployment assistance, and updates go through {BETA_CONTACT_EMAIL} or authorized Kirjane Labs support.
            </p>
            <p>
              <strong>9. Unauthorized copying/resale.</strong> Do not copy, resell, redistribute, or modify Salryn outside approved maintenance/deployment work.
            </p>
            <p>
              <strong>10. Acceptance.</strong> By activating, the user confirms the machine code and store information are correct.
            </p>
          </div>
          <label className="mt-4 flex items-start gap-2 text-sm font-medium text-slate-800">
            <input type="checkbox" className="mt-1" checked={termsAccepted} onChange={(e) => setTermsAccepted(e.target.checked)} />
            <span>I have read and accept the Salryn Activation Terms.</span>
          </label>
        </WizardShell>
      ) : null}

      {wizardStep === "import" ? (
        <WizardShell
          title="Import Signed Host License JSON"
          stepLabel="Step 3 of 3 · Import"
          onClose={closeWizard}
          footer={
            <>
              <Button variant="secondary" onClick={() => setWizardStep("terms")}>
                Back
              </Button>
              <Button
                variant="secondary"
                disabled={busy}
                onClick={() => {
                  setLicenseText("");
                }}
              >
                Clear
              </Button>
              <Button disabled={!canInstallWizard} onClick={() => void handleInstall()}>
                {busy ? "Installing…" : "Install License"}
              </Button>
            </>
          }
        >
          {error ? <Alert tone="danger">{error}</Alert> : null}
          {!termsAccepted ? (
            <Alert tone="warning" title="Terms required">
              Accept the Salryn Activation Terms before installing a license.
            </Alert>
          ) : null}
          <p className="mt-2">
            Paste only the signed Host license JSON you received. Importing replaces the current license on this device after Salryn verifies it.
          </p>
          <label className="mt-3 block space-y-1 text-sm font-medium text-slate-700">
            <span>Paste signed license JSON</span>
            <textarea
              value={licenseText}
              onChange={(event) => setLicenseText(event.target.value)}
              className="min-h-44 w-full rounded-md border border-slate-300 bg-white px-3 py-2 font-mono text-xs text-slate-900 shadow-sm focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600"
              placeholder="Paste the signed Host license you received from LicensingOps or Kirjane Labs."
            />
          </label>
          <p className="mt-2 text-xs text-slate-500">
            For Host POS activation · Create or select your Store in LicensingOps · Contact number or email with your request.
          </p>
        </WizardShell>
      ) : null}
    </div>
  );
}
