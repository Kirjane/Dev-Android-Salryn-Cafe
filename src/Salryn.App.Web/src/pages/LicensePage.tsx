import { useState, type ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  ArrowRight,
  Check,
  Copy,
  Database,
  HardDriveDownload,
  Layers3,
  RefreshCw,
  ShieldCheck,
  ShoppingBag,
  Sparkles,
  WifiOff,
  X,
} from "lucide-react";
import { fetchLicenseStatus, installLicense, type LicenseStatusValue, type LicenseTypeValue } from "../api/license";
import { ProductCreditsCard } from "../components/ProductCreditsCard";
import { Alert, Button, LoadingState, StatusPill } from "../components/ui";

const BETA_CONTACT_EMAIL = "salryn.info@gmail.com";

const HOST_FEATURES = [
  "Offline Host POS",
  "Checkout and receipts",
  "Product catalog",
  "Stock movements",
  "Portable backup files",
  "LAN Cashier/Manager terminals",
  "Keyboard-wedge barcode workflow",
  "Windows print queue receipts",
] as const;

const ACTIVATION_STEPS = [
  ["Copy", "Salryn generates your device-bound Host Machine Code."],
  ["Request", "Send the code with your store and contact details."],
  ["Import", "Install the signed Host license from Kirjane Labs."],
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
    return "That license request is not valid. Paste only the signed license text - not a file path.";
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
    <div className="fixed inset-0 z-50 flex items-end justify-center p-0 sm:items-center sm:p-5">
      <div className="absolute inset-0 bg-[#02040b]/80 backdrop-blur-sm" onClick={onClose} aria-hidden />
      <div
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className="relative z-10 flex max-h-[94vh] w-full max-w-2xl flex-col overflow-hidden rounded-t-[1.75rem] border border-white/10 bg-white shadow-[0_30px_100px_rgba(2,6,23,0.6)] sm:rounded-[1.75rem]"
      >
        <div className="relative overflow-hidden bg-[#081121] px-5 py-5 text-white sm:px-7 sm:py-6">
          <div className="absolute inset-0 bg-[radial-gradient(circle_at_85%_10%,rgba(124,58,237,0.32),transparent_42%),radial-gradient(circle_at_15%_100%,rgba(14,165,233,0.22),transparent_44%)]" aria-hidden />
          <div className="relative flex items-start justify-between gap-3">
            <div>
              <p className="text-[10px] font-bold uppercase tracking-[0.28em] text-cyan-300">{stepLabel}</p>
              <h2 className="mt-2 text-xl font-semibold tracking-tight sm:text-2xl">{title}</h2>
            </div>
            <button
              type="button"
              className="inline-flex h-9 w-9 items-center justify-center rounded-full border border-white/10 bg-white/5 text-slate-300 transition hover:bg-white/10 hover:text-white focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-300"
              onClick={onClose}
              aria-label="Close activation wizard"
            >
              <X aria-hidden size={17} />
            </button>
          </div>
        </div>
        <div className="min-h-0 flex-1 overflow-y-auto bg-slate-50 px-5 py-5 text-sm leading-relaxed text-slate-700 sm:px-7 sm:py-6">
          {children}
        </div>
        <div className="flex flex-wrap justify-end gap-2 border-t border-slate-200 bg-white px-5 py-4 sm:px-7">{footer}</div>
      </div>
    </div>
  );
}

function LandingBackdrop() {
  return (
    <div className="pointer-events-none absolute inset-0 overflow-hidden" aria-hidden>
      <div className="absolute inset-0 bg-[radial-gradient(circle_at_12%_18%,rgba(37,99,235,0.2),transparent_27%),radial-gradient(circle_at_84%_12%,rgba(124,58,237,0.22),transparent_30%),radial-gradient(circle_at_72%_72%,rgba(14,165,233,0.12),transparent_28%)]" />
      <div className="absolute inset-0 opacity-[0.075] [background-image:linear-gradient(rgba(255,255,255,0.7)_1px,transparent_1px),linear-gradient(90deg,rgba(255,255,255,0.7)_1px,transparent_1px)] [background-size:54px_54px] [mask-image:linear-gradient(to_bottom,black,transparent_72%)]" />
      <div className="absolute -right-32 top-16 h-[32rem] w-[32rem] rounded-[45%] border border-violet-400/15 [transform:rotate(18deg)]" />
      <div className="absolute -right-20 top-28 h-[26rem] w-[26rem] rounded-[44%] border border-cyan-300/10 [transform:rotate(-12deg)]" />
      <div className="absolute -left-44 bottom-16 h-80 w-80 rounded-full border border-blue-400/10" />
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
      const licenseDocument = licenseText.trim();
      const result = await installLicense(licenseDocument);
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
    setWizardStep(step === "import" ? "terms" : step);
    setError(null);
    setTermsAccepted(false);
  }

  function closeWizard() {
    setWizardStep("closed");
  }

  return (
    <div className="relative isolate min-h-[calc(100vh-2rem)] overflow-hidden rounded-[2rem] bg-[#050815] text-white shadow-[0_28px_90px_rgba(2,6,23,0.28)]">
      <LandingBackdrop />
      <div className="relative mx-auto max-w-7xl px-5 pb-8 pt-5 sm:px-8 sm:pb-12 sm:pt-7 lg:px-12">
        <header className="flex flex-wrap items-center justify-between gap-4 border-b border-white/10 pb-5">
          <div className="flex items-center gap-3">
            <span className="relative flex h-10 w-10 items-center justify-center overflow-hidden rounded-xl border border-cyan-300/25 bg-white/[0.07] shadow-[inset_0_0_20px_rgba(56,189,248,0.12)]">
              <Layers3 aria-hidden size={20} className="text-cyan-300" />
            </span>
            <div className="leading-none">
              <p className="text-sm font-black tracking-[0.22em] text-white">SALRYN</p>
              <p className="mt-1.5 text-[10px] font-medium tracking-[0.18em] text-slate-400">by Kirjane Labs</p>
            </div>
          </div>
          <div className="flex items-center gap-2 text-[10px] font-bold uppercase tracking-[0.2em] text-slate-400">
            <span className="h-1.5 w-1.5 rounded-full bg-emerald-400 shadow-[0_0_12px_rgba(52,211,153,0.9)]" />
            Host workstation
          </div>
        </header>

        <div className="mt-5 space-y-3">
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
          <>
            <main className="grid items-center gap-12 pb-16 pt-12 lg:grid-cols-2 lg:gap-16 lg:pb-24 lg:pt-20">
              <section>
                <div className="inline-flex items-center gap-2 rounded-full border border-cyan-300/20 bg-cyan-300/[0.07] px-3 py-1.5 text-[10px] font-bold uppercase tracking-[0.24em] text-cyan-200">
                  <WifiOff aria-hidden size={13} />
                  Offline-first local POS
                </div>
                <h1
                  className="mt-7 bg-gradient-to-r from-white via-blue-100 to-violet-200 bg-clip-text text-5xl font-black leading-[0.86] tracking-[0.06em] text-transparent sm:text-7xl lg:text-[5.6rem]"
                  title="Activate Salryn"
                >
                  SALRYN
                </h1>
                <p className="mt-4 text-sm font-semibold tracking-[0.2em] text-violet-200">by Kirjane Labs</p>
                <h2 className="mt-8 max-w-2xl text-2xl font-semibold leading-tight tracking-[-0.025em] text-white sm:text-3xl">
                  Less manual store work. More control where it matters.
                </h2>
                <p className="mt-5 max-w-2xl text-base leading-7 text-slate-300 sm:text-lg">
                  Reduce manual store work with a local POS built for checkout, products, stock, receipts, backups, and LAN terminals.
                </p>
                <p className="mt-3 max-w-xl text-sm leading-6 text-slate-400">
                  Salryn runs from a controlled Host workstation and keeps daily operations local, clear, and recoverable.
                </p>

                <div className="mt-8 flex flex-col gap-3 sm:flex-row sm:items-center">
                  <button
                    type="button"
                    onClick={() => openWizard("request")}
                    className="group inline-flex min-h-12 items-center justify-center gap-2 rounded-full bg-gradient-to-r from-blue-500 via-blue-600 to-violet-600 px-6 text-sm font-bold text-white shadow-[0_14px_38px_rgba(37,99,235,0.38)] transition hover:-translate-y-0.5 hover:shadow-[0_18px_46px_rgba(124,58,237,0.42)] focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-300"
                  >
                    Activate Salryn
                    <ArrowRight aria-hidden size={17} className="transition-transform group-hover:translate-x-0.5" />
                  </button>
                  <button
                    type="button"
                    onClick={() => openWizard("import")}
                    className="inline-flex min-h-12 items-center justify-center rounded-full border border-white/15 bg-white/[0.06] px-6 text-sm font-semibold text-white transition hover:border-white/30 hover:bg-white/[0.1] focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-300"
                  >
                    Import existing license
                  </button>
                </div>
                <button
                  type="button"
                  disabled={statusQuery.isFetching}
                  onClick={() => void statusQuery.refetch()}
                  className="mt-5 inline-flex items-center gap-2 text-xs font-semibold text-slate-400 transition hover:text-white disabled:opacity-50"
                >
                  <RefreshCw aria-hidden size={13} className={statusQuery.isFetching ? "animate-spin" : ""} />
                  Refresh license status
                </button>
              </section>

              <aside className="relative">
                <div className="absolute -inset-8 rounded-full bg-gradient-to-br from-blue-500/15 via-violet-500/10 to-transparent blur-3xl" aria-hidden />
                <div className="relative overflow-hidden rounded-[1.75rem] border border-white/10 bg-white/[0.065] p-6 shadow-[0_24px_80px_rgba(0,0,0,0.3)] backdrop-blur-xl sm:p-8">
                  <div className="flex items-center justify-between gap-4">
                    <div>
                      <p className="text-[10px] font-bold uppercase tracking-[0.24em] text-cyan-300">Simple local activation</p>
                      <h2 className="mt-2 text-2xl font-semibold tracking-tight">Ready in three steps.</h2>
                    </div>
                    <Sparkles aria-hidden className="text-violet-300" size={24} />
                  </div>

                  <div className="mt-8 space-y-0">
                    {ACTIVATION_STEPS.map(([title, description], index) => (
                      <div key={title} className="relative flex gap-4 pb-6 last:pb-0">
                        {index < ACTIVATION_STEPS.length - 1 ? <span className="absolute left-[15px] top-8 h-[calc(100%-1.25rem)] w-px bg-gradient-to-b from-cyan-300/40 to-violet-400/10" aria-hidden /> : null}
                        <span className="relative z-10 flex h-8 w-8 shrink-0 items-center justify-center rounded-full border border-cyan-300/25 bg-[#0b1729] text-xs font-bold text-cyan-200">
                          {index + 1}
                        </span>
                        <div className="pt-0.5">
                          <p className="font-semibold text-white">{title}</p>
                          <p className="mt-1 text-sm leading-6 text-slate-400">{description}</p>
                        </div>
                      </div>
                    ))}
                  </div>

                  <div className="mt-8 border-t border-white/10 pt-6">
                    <div className="flex flex-wrap items-center justify-between gap-3">
                      <p className="text-[10px] font-bold uppercase tracking-[0.2em] text-slate-400">Host Machine Code</p>
                      <button
                        type="button"
                        disabled={machineCode === "Not available"}
                        onClick={() => void copyMachineCode()}
                        className="inline-flex items-center gap-1.5 text-xs font-semibold text-cyan-300 transition hover:text-cyan-200 disabled:opacity-50"
                      >
                        <Copy aria-hidden size={13} />
                        Copy Host Machine Code
                      </button>
                    </div>
                    <p className="mt-3 break-all font-mono text-sm font-semibold tracking-wide text-white">{machineCode}</p>
                  </div>
                </div>
              </aside>
            </main>

            <section className="border-t border-white/10 py-16 lg:py-20" aria-labelledby="salryn-value-title">
              <div className="grid gap-10 lg:grid-cols-[0.8fr_1.2fr] lg:gap-16">
                <div>
                  <p className="text-[10px] font-bold uppercase tracking-[0.26em] text-violet-300">What Salryn offers</p>
                  <h2 id="salryn-value-title" className="mt-4 text-3xl font-semibold leading-tight tracking-[-0.03em] sm:text-4xl">
                    One Host. Your store&apos;s daily work, together.
                  </h2>
                  <p className="mt-5 max-w-lg text-sm leading-7 text-slate-400">
                    Move from checkout to stock review and recovery without scattering daily operations across disconnected tools.
                  </p>
                </div>
                <div className="grid gap-8 sm:grid-cols-3">
                  <ValueColumn icon={<ShoppingBag aria-hidden size={20} />} title="Sell clearly" copy="Run checkout and keep receipt history close to the work." />
                  <ValueColumn icon={<Database aria-hidden size={20} />} title="Know your stock" copy="Manage products and trace stock movements from the Host." />
                  <ValueColumn icon={<HardDriveDownload aria-hidden size={20} />} title="Stay recoverable" copy="Create portable backup files for controlled local recovery." />
                </div>
              </div>
              <div className="mt-12 flex flex-wrap gap-2.5">
                {HOST_FEATURES.map((feature) => (
                  <span key={feature} className="inline-flex items-center gap-2 rounded-full border border-white/10 bg-white/[0.045] px-3.5 py-2 text-xs font-medium text-slate-300">
                    <Check aria-hidden size={13} className="text-emerald-400" />
                    {feature}
                  </span>
                ))}
              </div>
            </section>

            <ProductCreditsCard variant="integrated" />
          </>
        ) : status ? (
          <main className="py-12 sm:py-16 lg:py-20">
            <div className="grid items-start gap-10 lg:grid-cols-2 lg:gap-16">
              <section>
                <div className="inline-flex items-center gap-2 rounded-full border border-emerald-300/20 bg-emerald-300/[0.08] px-3 py-1.5 text-[10px] font-bold uppercase tracking-[0.24em] text-emerald-300">
                  <ShieldCheck aria-hidden size={14} />
                  Status: Active
                </div>
                <h1 className="mt-7 text-5xl font-black leading-none tracking-[-0.045em] text-white sm:text-6xl">License Active</h1>
                <p className="mt-5 max-w-xl text-lg leading-8 text-slate-300">
                  Host license active. Salryn is ready for local store setup and daily operation.
                </p>
                <p className="mt-4 flex items-center gap-2 text-sm text-slate-400">
                  <WifiOff aria-hidden size={16} className="text-cyan-300" />
                  Works offline after activation.
                </p>
                <div className="mt-8 flex flex-wrap gap-3">
                  <button
                    type="button"
                    disabled={statusQuery.isFetching}
                    onClick={() => void statusQuery.refetch()}
                    className="inline-flex min-h-11 items-center gap-2 rounded-full bg-white px-5 text-sm font-bold text-slate-950 transition hover:bg-blue-50 disabled:opacity-60"
                  >
                    <RefreshCw aria-hidden size={15} className={statusQuery.isFetching ? "animate-spin" : ""} />
                    Refresh license status
                  </button>
                  {!showReplaceLicense ? (
                    <button
                      type="button"
                      onClick={() => setShowReplaceLicense(true)}
                      className="inline-flex min-h-11 items-center rounded-full border border-white/15 bg-white/[0.06] px-5 text-sm font-semibold text-white transition hover:bg-white/[0.1]"
                    >
                      Replace License
                    </button>
                  ) : null}
                </div>
              </section>

              <section className="overflow-hidden rounded-[1.75rem] border border-white/10 bg-white/[0.06] p-6 backdrop-blur-xl sm:p-8" aria-label="Active license summary">
                <div className="flex items-center justify-between gap-3 border-b border-white/10 pb-5">
                  <div>
                    <p className="text-[10px] font-bold uppercase tracking-[0.22em] text-cyan-300">SALRYN Host POS</p>
                    <h2 className="mt-2 text-xl font-semibold">Licensed workstation</h2>
                  </div>
                  <StatusPill tone="success">License Active</StatusPill>
                </div>
                <dl className="grid gap-x-6 gap-y-5 pt-6 text-sm sm:grid-cols-2">
                  <LicenseDetail label="Licensed to" value={status.customerName ?? "Not available"} />
                  <LicenseDetail label="Store" value={status.customerName ?? "Not available"} />
                  <LicenseDetail label="License type" value={deviceTypeLabel(typeLabel)} />
                  <LicenseDetail label="Support until" value={formatDate(status.supportExpiresAt)} />
                  <LicenseDetail label="Updates until" value={formatDate(status.updatesUntil)} />
                  <LicenseDetail label="Host Machine Code" value={machineCode} mono />
                </dl>
                <button
                  type="button"
                  className="mt-6 text-xs font-semibold text-cyan-300 underline-offset-4 hover:underline"
                  onClick={() => setShowSupportDetails((value) => !value)}
                >
                  {showSupportDetails ? "Hide support details" : "Show support details"}
                </button>
                {showSupportDetails ? (
                  <div className="mt-4 rounded-xl border border-white/10 bg-black/20 px-4 py-3 text-xs leading-6 text-slate-400">
                    <p>Host License ID: <span className="break-all font-mono text-slate-300">{status.licenseId ?? "Not installed"}</span></p>
                    <p>Store ID: <span className="break-all font-mono text-slate-300">{status.storeId ?? "Not specified"}</span></p>
                    <p>Application: {status.applicationId ?? "Not specified"}</p>
                    <p>Machine matched: {status.machineMatched ? "Yes" : "No"}</p>
                  </div>
                ) : null}
              </section>
            </div>

            {showReplaceLicense ? (
              <section className="mt-10 rounded-[1.75rem] border border-white/10 bg-white/[0.06] p-6 backdrop-blur-xl sm:p-8">
                <div className="max-w-3xl">
                  <p className="text-[10px] font-bold uppercase tracking-[0.22em] text-violet-300">License maintenance</p>
                  <h2 className="mt-2 text-2xl font-semibold">Import Signed Host License JSON</h2>
                  <p className="mt-3 text-sm leading-6 text-slate-400">
                    Paste only the signed Host license you received. Salryn verifies it before replacing the current license on this device.
                  </p>
                  <label className="mt-5 block space-y-2 text-sm font-medium text-slate-200">
                    <span>Paste signed license JSON</span>
                    <textarea
                      value={licenseText}
                      onChange={(event) => setLicenseText(event.target.value)}
                      className="min-h-40 w-full rounded-xl border border-white/15 bg-[#050815]/70 px-4 py-3 font-mono text-xs text-white shadow-inner focus:outline-none focus-visible:ring-2 focus-visible:ring-cyan-300"
                      placeholder="Paste the signed Host license you received from LicensingOps or Kirjane Labs."
                    />
                  </label>
                  <label className="mt-4 flex items-start gap-2 text-sm text-slate-300">
                    <input type="checkbox" checked={replaceConfirmed} onChange={(event) => setReplaceConfirmed(event.target.checked)} className="mt-1" />
                    <span>I understand this will replace the current license on this device.</span>
                  </label>
                  <div className="mt-5 flex flex-wrap gap-2">
                    <Button disabled={!canInstallReplace} onClick={() => void handleInstall()}>{busy ? "Installing..." : "Install License"}</Button>
                    <Button variant="secondary" disabled={busy} onClick={() => { setLicenseText(""); setReplaceConfirmed(false); setShowReplaceLicense(false); }}>Cancel</Button>
                  </div>
                </div>
              </section>
            ) : null}

            <div className="mt-12">
              <ProductCreditsCard variant="integrated" />
            </div>
          </main>
        ) : null}

        {status ? (
          <div className="border-t border-white/10 pt-5 text-xs text-slate-500">
            <span className="inline-flex items-center gap-2">
              <StatusPill tone={statusTone(status.status)}>{showActiveSummary ? "Active" : currentStatusLabel}</StatusPill>
              {showActivation ? "Activation available" : "Host ready"}
            </span>
          </div>
        ) : null}
      </div>

      {wizardStep === "request" ? (
        <WizardShell
          title="Activate your Salryn Host POS"
          stepLabel="Step 1 of 3 - Request"
          onClose={closeWizard}
          footer={<><Button variant="secondary" onClick={closeWizard}>Cancel</Button><Button onClick={() => setWizardStep("terms")}>Next: Terms</Button></>}
        >
          <p className="text-base font-semibold text-slate-900">Thank you for choosing Salryn.</p>
          <p className="mt-2">
            Contact Kirjane Labs / Salryn Infodesk at <span className="font-mono font-semibold text-slate-900">{BETA_CONTACT_EMAIL}</span> and send the Host Machine Code below with your store details.
          </p>
          <div className="mt-5 rounded-2xl border border-blue-200 bg-blue-50 px-4 py-4">
            <p className="text-xs font-bold uppercase tracking-[0.18em] text-blue-900">Host Machine Code</p>
            <p className="mt-2 break-all font-mono text-base font-bold text-slate-950 sm:text-lg">{machineCode}</p>
            <div className="mt-4 flex flex-wrap gap-2">
              <Button variant="secondary" size="sm" disabled={machineCode === "Not available"} onClick={() => void copyMachineCode()}>Copy Host Machine Code</Button>
              <Button variant="secondary" size="sm" onClick={() => void copyEmail()}>{emailCopied ? "Email copied" : `Copy ${BETA_CONTACT_EMAIL}`}</Button>
            </div>
          </div>
          <div className="mt-5 grid gap-5 sm:grid-cols-2">
            <div>
              <p className="font-semibold text-slate-900">Send with your request</p>
              <ul className="mt-2 list-disc space-y-1.5 pl-5 text-slate-600">
                <li>Store / business name</li>
                <li>Contact person</li>
                <li>Contact number or email</li>
                <li>Host Machine Code</li>
                <li>Optional deployment notes</li>
              </ul>
            </div>
            <div>
              <p className="font-semibold text-slate-900">What happens next</p>
              <p className="mt-2 text-slate-600">
                Salryn generates this Machine Code. LicensingOps signs the Host license. Kirjane Labs sends the signed license back for import here.
              </p>
            </div>
          </div>
          <p className="mt-5 text-xs leading-5 text-slate-500">
            Each LAN terminal needs its own Terminal license. On the terminal device, use its Terminal Machine Code; terminal users do not need a Host License ID.
          </p>
        </WizardShell>
      ) : null}

      {wizardStep === "terms" ? (
        <WizardShell
          title="Salryn Activation Terms"
          stepLabel="Step 2 of 3 - Terms"
          onClose={closeWizard}
          footer={<><Button variant="secondary" onClick={() => setWizardStep("request")}>Back</Button><Button disabled={!termsAccepted} onClick={() => setWizardStep("import")}>Next: Import license</Button></>}
        >
          <div className="max-h-80 space-y-3 overflow-y-auto rounded-2xl border border-slate-200 bg-white p-4 text-sm leading-relaxed text-slate-700 sm:p-5">
            <p><strong>1. Device-bound Host license.</strong> The signed license is issued for this Host workstation and its machine code.</p>
            <p><strong>2. Correct store identity.</strong> Request a reissue if the store or Host device changes.</p>
            <p><strong>3. Local operation.</strong> Salryn keeps the store database on the controlled Host workstation.</p>
            <p><strong>4. Backup responsibility.</strong> The operator is responsible for keeping recoverable backup files.</p>
            <p><strong>5. Separate device licenses.</strong> Host and LAN Terminal licenses have different roles and cannot replace one another.</p>
            <p><strong>6. Bounded hardware workflows.</strong> Supported input and output paths are limited to the documented keyboard-wedge barcode and Windows print queue workflows.</p>
            <p><strong>7. Compliance boundary.</strong> This beta does not claim tax-authority or electronic-sales production readiness.</p>
            <p><strong>8. Use and support.</strong> Do not copy, resell, redistribute, or modify Salryn outside authorized work. Support is provided through Kirjane Labs.</p>
          </div>
          <label className="mt-5 flex items-start gap-3 rounded-xl border border-slate-200 bg-white px-4 py-3 text-sm font-medium text-slate-800">
            <input type="checkbox" className="mt-1" checked={termsAccepted} onChange={(event) => setTermsAccepted(event.target.checked)} />
            <span>I have read and accept the Salryn Activation Terms.</span>
          </label>
        </WizardShell>
      ) : null}

      {wizardStep === "import" ? (
        <WizardShell
          title="Import Signed Host License JSON"
          stepLabel="Step 3 of 3 - Import"
          onClose={closeWizard}
          footer={<><Button variant="secondary" onClick={() => setWizardStep("terms")}>Back</Button><Button variant="secondary" disabled={busy} onClick={() => setLicenseText("")}>Clear</Button><Button disabled={!canInstallWizard} onClick={() => void handleInstall()}>{busy ? "Installing..." : "Install License"}</Button></>}
        >
          {error ? <Alert tone="danger">{error}</Alert> : null}
          {!termsAccepted ? <Alert tone="warning" title="Terms required">Accept the Salryn Activation Terms before installing a license.</Alert> : null}
          <p className="mt-3">Paste the signed Host license JSON you received from Kirjane Labs. Salryn verifies the signed document before installation.</p>
          <label className="mt-4 block space-y-2 text-sm font-medium text-slate-700">
            <span>Signed Host license JSON</span>
            <textarea
              value={licenseText}
              onChange={(event) => setLicenseText(event.target.value)}
              className="min-h-48 w-full rounded-xl border border-slate-300 bg-white px-4 py-3 font-mono text-xs text-slate-900 shadow-sm focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600"
              placeholder="Paste the signed Host license you received from LicensingOps or Kirjane Labs."
            />
          </label>
        </WizardShell>
      ) : null}
    </div>
  );
}

function ValueColumn({ icon, title, copy }: { icon: ReactNode; title: string; copy: string }) {
  return (
    <div className="border-l border-white/10 pl-5">
      <span className="flex h-9 w-9 items-center justify-center rounded-xl bg-gradient-to-br from-blue-500/20 to-violet-500/20 text-cyan-300">{icon}</span>
      <h3 className="mt-4 font-semibold text-white">{title}</h3>
      <p className="mt-2 text-sm leading-6 text-slate-400">{copy}</p>
    </div>
  );
}

function LicenseDetail({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="min-w-0">
      <dt className="text-[10px] font-bold uppercase tracking-[0.16em] text-slate-500">{label}</dt>
      <dd className={`mt-1.5 break-all font-medium text-slate-200 ${mono ? "font-mono text-xs" : ""}`}>{value}</dd>
    </div>
  );
}
