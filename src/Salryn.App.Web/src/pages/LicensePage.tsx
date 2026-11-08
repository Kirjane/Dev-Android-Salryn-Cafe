import { useEffect, useRef, useState, type ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import { fetchLicenseStatus, installLicense, type LicenseStatusValue, type LicenseTypeValue } from "../api/license";
import { Alert, Button, LoadingState, StatusPill, useNotifications } from "../components/ui";

const BETA_CONTACT_EMAIL = "salryn.info@gmail.com";

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

function friendlyInstallError(raw: string | null | undefined, code?: string | null): string {
  const contact = " Contact " + BETA_CONTACT_EMAIL + " if you need a reissue.";
  const lower = (raw ?? "").trim().toLowerCase();
  const codeLower = (code ?? "").toLowerCase();

  if (codeLower.includes("callerpath") || lower.includes("caller filesystem") || lower.includes("does not accept caller")) {
    return "That license request is not valid. Paste only the signed license text - not a file path.";
  }
  if (codeLower.includes("unsupportedfield") || lower.includes("unsupported field")) {
    return "That license file has unexpected fields. Ask Kirjane Information Technology Services for a new signed license.";
  }
  if (codeLower.includes("emptydocument") || lower.includes("content is required")) {
    return "Paste the full signed license you received before installing.";
  }
  if (codeLower.includes("malformed") || lower.includes("malformed") || lower.includes("json did not") || lower.includes("json is")) {
    return "The license text is not valid. Paste the full signed license file you received.";
  }
  if (lower.includes("machinebinding is required") || (lower.includes("machinebinding") && lower.includes("empty"))) {
    return "This license is missing a device binding. Ask Kirjane Information Technology Services for a new signed license.";
  }
  if (lower.includes("does not match this installation") || lower.includes("machine binding does not match")) {
    return "This license does not match this device." + contact;
  }
  if (lower.includes("unsigned") || lower.includes("signature is invalid") || lower.includes("signature verifier") || lower.includes("fails closed") || lower.includes("signature")) {
    return "This license could not be verified. Ask Kirjane Information Technology Services for a new signed license file.";
  }
  if (lower.includes("expired") && !lower.includes("grace")) {
    return "This license has expired." + contact;
  }
  if (lower.includes("schema")) {
    return "This license format is not supported by this Salryn version. Ask Kirjane Information Technology Services for an updated license.";
  }
  if (lower.includes("applicationid")) {
    return "This license is not for Salryn POS. Ask Kirjane Information Technology Services for the correct license.";
  }
  if (lower.includes("storeid") || lower.includes("identity fields")) {
    return "This license is missing required store or customer details. Ask Kirjane Information Technology Services for a new signed license.";
  }
  if (lower.includes("must not include hostlicenseid") || lower.includes("must not include terminalid")) {
    return "This Host license has Terminal-only fields. Ask Kirjane Information Technology Services for a correct Host license.";
  }
  if (lower.includes("must not include allowedlanterminals")) {
    return "This Terminal license has Host-only fields. Ask Kirjane Information Technology Services for a correct Terminal license.";
  }
  if (lower.includes("must include hostlicenseid") || lower.includes("must include terminalid")) {
    return "This Terminal license is incomplete. Ask Kirjane Information Technology Services for a new Terminal license.";
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
  const shellRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    const originalOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        event.stopPropagation();
        onClose();
      } else if (event.key === "Tab" && shellRef.current) {
        const focusable = shellRef.current.querySelectorAll<HTMLElement>(
          'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
        );
        if (focusable.length > 0) {
          const first = focusable[0];
          const last = focusable[focusable.length - 1];
          if (event.shiftKey && document.activeElement === first) {
            event.preventDefault();
            last.focus();
          } else if (!event.shiftKey && document.activeElement === last) {
            event.preventDefault();
            first.focus();
          }
        }
      }
    }

    window.addEventListener("keydown", handleKeyDown);
    return () => {
      document.body.style.overflow = originalOverflow;
      window.removeEventListener("keydown", handleKeyDown);
    };
  }, [onClose]);

  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center p-0 sm:items-center sm:p-6">
      <div className="absolute inset-0 bg-slate-900/40" onClick={onClose} aria-hidden />
      <div
        ref={shellRef}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        tabIndex={-1}
        className="relative z-10 flex max-h-[94vh] w-full max-w-xl flex-col overflow-hidden rounded-t-xl border border-[#E5E7EB] bg-white sm:rounded-xl focus:outline-none"
      >
        <div className="flex items-start justify-between gap-3 border-b border-[#E5E7EB] px-5 py-4 sm:px-6">
          <div>
            <p className="text-xs font-medium text-[#6B7280]">{stepLabel}</p>
            <h2 className="mt-1 text-lg font-semibold text-[#111827]">{title}</h2>
          </div>
          <button
            type="button"
            className="inline-flex h-8 w-8 items-center justify-center rounded-lg border border-[#E5E7EB] text-[#6B7280] focus:outline-none focus-visible:ring-2 focus-visible:ring-[#1F4B7A]"
            onClick={onClose}
            aria-label="Close"
          >
            ×
          </button>
        </div>
        <div className="min-h-0 flex-1 overflow-y-auto px-5 py-5 text-sm leading-relaxed text-[#4B5563] sm:px-6">
          {children}
        </div>
        <div className="flex flex-wrap justify-end gap-2 border-t border-[#E5E7EB] bg-[#F4F5F7] px-5 py-4 sm:px-6">{footer}</div>
      </div>
    </div>
  );
}

function TermsBody() {
  return (
    <div className="max-h-80 space-y-3 overflow-y-auto rounded-lg border border-[#E5E7EB] bg-white p-4 text-sm leading-relaxed text-[#4B5563]">
      <p className="font-semibold text-[#111827]">Terms &amp; Conditions</p>
      <p>These terms apply to the current Salryn beta and explain the product, license, operator, and support boundaries.</p>
      <p>
        <strong className="text-[#111827]">1. Product use.</strong> Salryn is an offline-first POS and ledger console for store operations. Use it only for lawful store operations and within the features enabled by your license.
      </p>
      <p>
        <strong className="text-[#111827]">2. Local license.</strong> Current beta status: cloud activation is not required. Salryn uses signed local license files, and a valid Host license is required before Host use.
      </p>
      <p>
        <strong className="text-[#111827]">3. Device binding.</strong> A license is bound to this device's Machine Code. Reinstalling Windows, replacing hardware, or moving to another device may require a replacement license.
      </p>
      <p>
        <strong className="text-[#111827]">4. Data ownership.</strong> Your store data remains on your device unless you choose to export, back up, or send it for support.
      </p>
      <p>
        <strong className="text-[#111827]">5. Backup responsibility.</strong> You are responsible for keeping verified backup copies of your Salryn data, especially before repair, migration, restore, or major changes.
      </p>
      <p>
        <strong className="text-[#111827]">6. Hardware limits.</strong> Current beta status: ESC/POS commands, paper cutters, cash drawers, dedicated thermal-printer control, hardware bundles, and hardware warranties are not included. Client-supplied hardware is used unless separately agreed. Windows print queue internal receipts are available only when configured and verified.
      </p>
      <p>
        <strong className="text-[#111827]">7. Receipts and compliance.</strong> BIR accreditation: current beta status: not included; target: a future compliance lane. eSales transmission: current beta status: not included; target: a future compliance lane. Current receipts are internal operational receipts, not a representation of accreditation or approval.
      </p>
      <p>
        <strong className="text-[#111827]">8. LAN terminals.</strong> LAN Terminals are a future feature, not included in the current production release. Internal testing requires separate device licensing, explicit Host authorization, and acceptance of network and reconnection limitations.
      </p>
      <p>
        <strong className="text-[#111827]">9. Account and operator responsibility.</strong> You are responsible for operator accounts, passwords, permissions, device access, transaction review, and correcting operator-entered data through the supported workflows.
      </p>
      <p>
        <strong className="text-[#111827]">10. Support boundaries.</strong> Kirjane Information Technology Services may request your Machine Code, license file, logs, screenshots, or backup files to investigate an issue. Do not send signing secrets or unrelated personal data. Contact {BETA_CONTACT_EMAIL}.
      </p>
      <p>
        <strong className="text-[#111827]">11. Beta limitations.</strong> Current beta status: ongoing. Features and operating procedures may change, and client deployment requires the stated manual verification. Not included: guarantees for unsupported hardware, continuous network availability, or features outside the documented beta scope.
      </p>
      <p>
        <strong className="text-[#111827]">12. Rights reservation.</strong> Salryn and its related product materials are products of Kirjane Information Technology Services. © 2026 Kirjane Information Technology Services. All rights reserved. Rights not expressly granted remain reserved.
      </p>
      <p>
        <strong className="text-[#111827]">13. Acceptance.</strong> By installing or replacing a license, you confirm that you have read and accept these Terms &amp; Conditions and the current beta boundaries.
      </p>
    </div>
  );
}

export function LicensePage() {
  const { notify } = useNotifications();
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

  const status = statusQuery.data;
  const currentStatusLabel = status ? statusLabel(status.status) : null;
  const typeLabel = licenseTypeLabel(status?.licenseType);
  const machineCode = status?.machineCode ?? status?.machineFingerprint ?? "Not available";
  const isLicensed = currentStatusLabel === "Licensed" && !!status?.machineMatched;
  const hostSetupBlocked = currentStatusLabel === "Licensed" && typeLabel !== "Host";
  const showActiveSummary = isLicensed && (typeLabel === "Host" || typeLabel === "Terminal");

  const wizardOpen = wizardStep !== "closed";
  const canInstallWizard = licenseText.trim().length > 0 && termsAccepted && !busy;
  // Replace path requires re-acknowledgement of terms + explicit replace confirm (P1-006).
  const canInstallReplace = licenseText.trim().length > 0 && replaceConfirmed && termsAccepted && !busy;
  const canInstall = showReplaceLicense ? canInstallReplace : canInstallWizard;

  async function handleInstall() {
    if (!termsAccepted) {
      notify.warning("Terms required", "Accept the Terms & Conditions before installing a license.");
      return;
    }
    if (!canInstall) return;
    const replacingLicense = showReplaceLicense;
    setBusy(true);
    setMessage(null);
    setError(null);
    try {
      const licenseDocument = licenseText.trim();
      const result = await installLicense(licenseDocument);
      if (result.installStatus === "Installed") {
        setMessage("License installed. Host license active. Salryn works offline after a valid signed license is installed.");
        notify.success(
          replacingLicense ? "License replaced" : "License installed",
          "The signed Host license is active on this device.",
        );
        setLicenseText("");
        setReplaceConfirmed(false);
        setShowReplaceLicense(false);
        setWizardStep("closed");
        setTermsAccepted(false);
      } else {
        const friendlyError = friendlyInstallError(result.errorMessage, result.errorCode);
        setError(friendlyError);
        notify.error("License was rejected", friendlyError);
      }
      await statusQuery.refetch();
    } catch (e) {
      const err = e as Error & { code?: string };
      const friendlyError = friendlyInstallError(err.message, err.code);
      setError(friendlyError);
      notify.error("License was not installed", friendlyError);
    } finally {
      setBusy(false);
    }
  }

  async function copyMachineCode() {
    if (!machineCode || machineCode === "Not available") return;
    try {
      await navigator.clipboard.writeText(machineCode);
      setMessage("Machine Code copied.");
      notify.info("Machine Code copied", "Send it to Kirjane Information Technology Services support.");
    } catch {
      setError("Could not copy Machine Code. Select and copy it manually.");
      notify.error("Machine Code was not copied", "Select the code and copy it manually.");
    }
  }

  function openWizard(step: WizardStep = "request") {
    setWizardStep(step === "import" ? "terms" : step);
    setError(null);
    setTermsAccepted(false);
  }

  function closeWizard() {
    setWizardStep("closed");
  }

  return (
    <div className="min-h-[calc(100vh-2rem)] bg-[#F4F5F7] text-[#111827]">
      <div className="mx-auto max-w-[920px] px-6 py-6 sm:py-8">
        <header className="flex flex-wrap items-center justify-between gap-3 border-b border-[#E5E7EB] pb-4">
          <div>
            <p className="text-base font-semibold tracking-wide text-[#111827]">Salryn</p>
            <p className="mt-0.5 text-xs text-[#6B7280]">by Kirjane Information Technology Services</p>
          </div>
          <StatusPill tone={showActiveSummary ? "success" : statusTone(status?.status ?? "Unlicensed")}>
            {showActiveSummary ? "Activated" : currentStatusLabel === "Development" ? "Development" : "Not activated"}
          </StatusPill>
        </header>

        <div className="mt-4 space-y-3">
          {statusQuery.isLoading ? <LoadingState label="Loading license status..." /> : null}
          {statusQuery.isError ? <Alert tone="danger">{(statusQuery.error as Error).message}</Alert> : null}
          {message ? <Alert tone="success">{message}</Alert> : null}
          {error && !wizardOpen ? <Alert tone="danger">{error}</Alert> : null}
          {hostSetupBlocked ? (
            <Alert tone="danger" title="Host license required">
              This Terminal license cannot unlock Host first-run setup or the full Host application. Install a valid Host license for this workstation.
            </Alert>
          ) : null}
        </div>

        {!showActiveSummary ? (
          <main className="mt-6 grid gap-4 lg:grid-cols-2">
            <section className="rounded-xl border border-[#E5E7EB] bg-white p-5 sm:p-6">
              <h1 title="Activate Salryn" className="text-2xl font-semibold tracking-tight text-[#111827]">
                Activate Salryn
              </h1>
              <p className="mt-3 text-sm leading-6 text-[#4B5563]">
                This device needs a signed local license before setup can continue.
              </p>
              <p className="mt-3 text-sm leading-6 text-[#4B5563]">
                Salryn runs locally on this device. Send this Machine Code to Kirjane Information Technology Services to receive a signed license file.
              </p>
              <div className="mt-6 flex flex-col gap-2 sm:flex-row">
                <Button className="min-h-11 bg-[#1F4B7A] hover:bg-[#183a5f]" onClick={() => openWizard("import")}>
                  Import license
                </Button>
                <Button
                  variant="secondary"
                  className="min-h-11"
                  disabled={machineCode === "Not available"}
                  onClick={() => void copyMachineCode()}
                >
                  Copy Machine Code
                </Button>
              </div>
              <button
                type="button"
                className="mt-4 text-sm font-medium text-[#1F4B7A] underline-offset-2 hover:underline focus:outline-none focus-visible:ring-2 focus-visible:ring-[#1F4B7A]"
                onClick={() => openWizard("request")}
              >
                How activation works
              </button>
              <button
                type="button"
                disabled={statusQuery.isFetching}
                onClick={() => void statusQuery.refetch()}
                className="mt-3 block text-xs font-medium text-[#6B7280] hover:text-[#111827] disabled:opacity-50"
              >
                Refresh license status
              </button>
            </section>

            <aside className="rounded-xl border border-[#E5E7EB] bg-white p-5 sm:p-6">
              <p className="text-xs font-medium uppercase tracking-wide text-[#6B7280]">Machine Code</p>
              <p className="mt-3 break-all font-mono text-sm font-semibold text-[#111827]">{machineCode}</p>
              <Button
                variant="secondary"
                size="sm"
                className="mt-4"
                disabled={machineCode === "Not available"}
                onClick={() => void copyMachineCode()}
              >
                Copy Machine Code
              </Button>
              <p className="mt-5 text-sm leading-6 text-[#4B5563]">
                Send this code to Kirjane Information Technology Services. You will receive a signed local license file for this device.
              </p>
              <p className="mt-4 text-sm text-[#4B5563]">
                Support: <span className="font-mono text-[#111827]">{BETA_CONTACT_EMAIL}</span>
              </p>
              <p className="mt-3 text-xs leading-5 text-[#6B7280]">
                Terminal users do not need a Host License ID. Each LAN terminal needs its own Terminal license.
              </p>
            </aside>
          </main>
        ) : status ? (
          <main className="mt-6">
            <section className="rounded-xl border border-[#E5E7EB] bg-white p-5 sm:p-6">
              <p className="text-xs font-medium text-[#6B7280]">Status: Active</p>
              <h1 className="mt-2 text-2xl font-semibold tracking-tight text-[#111827]">Salryn is activated</h1>
              <p className="mt-3 text-sm leading-6 text-[#4B5563]">
                This Host is licensed for local use on this device.
              </p>
              <p className="mt-1 text-sm text-[#6B7280]">Host license active.</p>

              <dl className="mt-6 grid gap-4 sm:grid-cols-2">
                <Detail label="Licensed to" value={status.customerName ?? "Not available"} />
                <Detail label="License type" value={deviceTypeLabel(typeLabel)} />
                <Detail label="Support until" value={formatDate(status.supportExpiresAt)} />
                <Detail label="Updates until" value={formatDate(status.updatesUntil)} />
                <Detail label="Device binding" value={status.machineMatched ? "Matched" : "Not matched"} />
                <Detail label="Machine Code" value={machineCode} mono />
              </dl>

              <div className="mt-6 flex flex-wrap gap-2">
                <Button
                  variant="secondary"
                  disabled={statusQuery.isFetching}
                  onClick={() => void statusQuery.refetch()}
                >
                  Refresh license status
                </Button>
                {!showReplaceLicense ? (
                  <Button variant="secondary" onClick={() => setShowReplaceLicense(true)}>
                    Replace license
                  </Button>
                ) : null}
              </div>

              <button
                type="button"
                className="mt-5 text-sm font-medium text-[#1F4B7A] underline-offset-2 hover:underline"
                onClick={() => setShowSupportDetails((value) => !value)}
              >
                {showSupportDetails ? "Hide technical details" : "Show technical details"}
              </button>
              {showSupportDetails ? (
                <div className="mt-3 rounded-lg border border-[#E5E7EB] bg-[#F4F5F7] px-4 py-3 text-xs leading-6 text-[#4B5563]">
                  <p>
                    Host License ID:{" "}
                    <span className="break-all font-mono text-[#111827]">{status.licenseId ?? "Not installed"}</span>
                  </p>
                  <p>
                    Store ID:{" "}
                    <span className="break-all font-mono text-[#111827]">{status.storeId ?? "Not specified"}</span>
                  </p>
                  <p>Application: {status.applicationId ?? "Not specified"}</p>
                  <p>Machine matched: {status.machineMatched ? "Yes" : "No"}</p>
                </div>
              ) : null}
            </section>

            {showReplaceLicense ? (
              <section className="mt-4 rounded-xl border border-[#E5E7EB] bg-white p-5 sm:p-6">
                <h2 className="text-lg font-semibold text-[#111827]">Import Signed Host License JSON</h2>
                <p className="mt-2 text-sm leading-6 text-[#4B5563]">
                  Paste only the signed Host license you received. This replaces the active local license for this device after verification.
                </p>
                <label className="mt-4 block space-y-2 text-sm font-medium text-[#111827]">
                  <span>Paste signed license JSON</span>
                  <textarea
                    value={licenseText}
                    onChange={(event) => setLicenseText(event.target.value)}
                    className="min-h-40 w-full rounded-lg border border-[#E5E7EB] bg-white px-3 py-2 font-mono text-xs text-[#111827] focus:outline-none focus-visible:ring-2 focus-visible:ring-[#1F4B7A]"
                    placeholder="Paste the signed Host license you received from Kirjane Information Technology Services."
                  />
                </label>
                <label className="mt-4 flex items-start gap-2 text-sm text-[#4B5563]">
                  <input type="checkbox" checked={termsAccepted} onChange={(event) => setTermsAccepted(event.target.checked)} className="mt-1" />
                  <span>I have re-read and accept the Salryn Terms &amp; Conditions for this replacement.</span>
                </label>
                <label className="mt-3 flex items-start gap-2 text-sm text-[#4B5563]">
                  <input type="checkbox" checked={replaceConfirmed} onChange={(event) => setReplaceConfirmed(event.target.checked)} className="mt-1" />
                  <span>I understand this replaces the active local license for this device.</span>
                </label>
                <div className="mt-4 flex flex-wrap gap-2">
                  <Button disabled={!canInstallReplace} onClick={() => void handleInstall()}>
                    {busy ? "Installing..." : "Install License"}
                  </Button>
                  <Button
                    variant="secondary"
                    disabled={busy}
                    onClick={() => {
                      setLicenseText("");
                      setReplaceConfirmed(false);
                      setTermsAccepted(false);
                      setShowReplaceLicense(false);
                    }}
                  >
                    Cancel
                  </Button>
                </div>
              </section>
            ) : null}
          </main>
        ) : null}

        <section className="mt-10 rounded-xl border border-[#E5E7EB] bg-white p-5 sm:p-6" aria-labelledby="salryn-product-title">
          <p className="text-xs font-semibold uppercase tracking-wide text-[#1F4B7A]">Product information</p>
          <h2 id="salryn-product-title" className="mt-3 text-xl font-semibold text-[#111827]">Salryn POS</h2>
          <p className="mt-2 text-sm font-semibold text-[#111827]">
            A product of Kirjane Information Technology Services.
          </p>
          <p className="mt-3 text-sm leading-6 text-[#4B5563]">
            Salryn helps stores manage point-of-sale transactions, products, stock movements, receipts,
            reporting, and local backups from a single Host computer.
          </p>
          <p className="mt-3 text-sm leading-6 text-[#4B5563]">
            LAN Terminals are a future feature and are not included in the current production release.
          </p>
          <div className="mt-5 border-t border-[#E5E7EB] pt-4 text-xs text-[#6B7280]">
            <p>Product support: {BETA_CONTACT_EMAIL}</p>
            <p className="mt-1">© 2026 Kirjane Information Technology Services. All rights reserved.</p>
          </div>
        </section>
      </div>

      {wizardStep === "request" ? (
        <WizardShell
          title="Machine Code"
          stepLabel="Step 1 of 3"
          onClose={closeWizard}
          footer={
            <>
              <Button variant="secondary" onClick={closeWizard}>
                Cancel
              </Button>
              <Button onClick={() => setWizardStep("terms")}>Continue</Button>
            </>
          }
        >
          <p>
            Send this Machine Code to Kirjane Information Technology Services / Salryn Infodesk at <span className="font-mono font-semibold text-[#111827]">{BETA_CONTACT_EMAIL}</span> with your store details.
          </p>
          <p className="mt-3">
            Salryn generates this Machine Code. Kirjane Information Technology Services returns a signed Host license for import here.
          </p>
          <div className="mt-4 rounded-lg border border-[#E5E7EB] bg-[#F4F5F7] px-4 py-3">
            <p className="text-xs font-medium uppercase tracking-wide text-[#6B7280]">Machine Code</p>
            <p className="mt-2 break-all font-mono text-sm font-semibold text-[#111827]">{machineCode}</p>
            <div className="mt-3">
              <Button
                variant="secondary"
                size="sm"
                disabled={machineCode === "Not available"}
                onClick={() => void copyMachineCode()}
              >
                Copy Machine Code
              </Button>
            </div>
          </div>
          <p className="mt-4 text-xs text-[#6B7280]">
            Terminal users do not need a Host License ID. Each LAN terminal needs its own Terminal license.
          </p>
        </WizardShell>
      ) : null}

      {wizardStep === "terms" ? (
        <WizardShell
          title="Terms & Conditions"
          stepLabel="Step 2 of 3"
          onClose={closeWizard}
          footer={
            <>
              <Button variant="secondary" onClick={() => setWizardStep("request")}>
                Back
              </Button>
              <Button disabled={!termsAccepted} onClick={() => setWizardStep("import")}>
                Continue
              </Button>
            </>
          }
        >
          <TermsBody />
          <label className="mt-4 flex items-start gap-3 rounded-lg border border-[#E5E7EB] bg-white px-4 py-3 text-sm font-medium text-[#111827]">
            <input type="checkbox" className="mt-1" checked={termsAccepted} onChange={(event) => setTermsAccepted(event.target.checked)} />
            <span>I understand and accept these Terms &amp; Conditions.</span>
          </label>
        </WizardShell>
      ) : null}

      {wizardStep === "import" ? (
        <WizardShell
          title="Import Signed Host License JSON"
          stepLabel="Step 3 of 3"
          onClose={closeWizard}
          footer={
            <>
              <Button variant="secondary" onClick={() => setWizardStep("terms")}>
                Back
              </Button>
              <Button variant="secondary" disabled={busy} onClick={() => setLicenseText("")}>
                Clear
              </Button>
              <Button disabled={!canInstallWizard} onClick={() => void handleInstall()}>
                {busy ? "Installing..." : "Install License"}
              </Button>
            </>
          }
        >
          {error ? <Alert tone="danger">{error}</Alert> : null}
          {!termsAccepted ? (
            <Alert tone="warning" title="Terms required">
              Accept the Terms &amp; Conditions before installing a license.
            </Alert>
          ) : null}
          <p className="mt-3">Paste the signed Host license JSON you received from Kirjane Information Technology Services.</p>
          <label className="mt-4 block space-y-2 text-sm font-medium text-[#111827]">
            <span>Signed Host license JSON</span>
            <textarea
              value={licenseText}
              onChange={(event) => setLicenseText(event.target.value)}
              className="min-h-48 w-full rounded-lg border border-[#E5E7EB] bg-white px-3 py-2 font-mono text-xs text-[#111827] focus:outline-none focus-visible:ring-2 focus-visible:ring-[#1F4B7A]"
              placeholder="Paste the signed Host license you received from Kirjane Information Technology Services."
            />
          </label>
        </WizardShell>
      ) : null}
    </div>
  );
}

function Detail({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="min-w-0">
      <dt className="text-xs font-medium text-[#6B7280]">{label}</dt>
      <dd className={`mt-1 break-all text-sm font-medium text-[#111827] ${mono ? "font-mono text-xs" : ""}`}>{value}</dd>
    </div>
  );
}
