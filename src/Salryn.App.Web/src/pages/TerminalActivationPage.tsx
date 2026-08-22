import { useMemo, useState, type ReactNode } from "react";
import { validateRemotePosTerminalLicense } from "../api/remotePosClient";
import { ProductCreditsCard } from "../components/ProductCreditsCard";
import { Alert, Button, Card, CardBody, CardHeader, CardTitle, StatusPill } from "../components/ui";
import { currentBrowserOrigin } from "../utils/lanEntry";
import {
  clearInstalledTerminalLicense,
  getOrCreateTerminalMachineCode,
  inspectTerminalLicenseCandidate,
  installTerminalLicense,
  readInstalledTerminalLicense,
  type InstalledTerminalLicense,
} from "../utils/terminalLicenseStorage";

const ACTIVATION_CONTACT = "salryn.info@gmail.com";

const TERMINAL_FEATURES = [
  "Connects to Host POS over LAN",
  "Role-limited Cashier/Manager workflow",
  "Host validates products, pricing, checkout, and sales",
  "No local store database on terminal",
  "Each device has its own Terminal license",
] as const;

type WizardStep = "closed" | "request" | "terms" | "import";

type TerminalActivationPageProps = {
  onActivated: () => void;
};

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

export function TerminalActivationPage({ onActivated }: TerminalActivationPageProps) {
  const machineCode = useMemo(() => getOrCreateTerminalMachineCode(), []);
  const [licenseJson, setLicenseJson] = useState("");
  const [installed, setInstalled] = useState<InstalledTerminalLicense | null>(() => readInstalledTerminalLicense());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const [emailCopied, setEmailCopied] = useState(false);
  const [showSupportDetails, setShowSupportDetails] = useState(false);
  const [wizardStep, setWizardStep] = useState<WizardStep>("closed");
  const [termsAccepted, setTermsAccepted] = useState(false);

  async function copyMachineCode() {
    await navigator.clipboard.writeText(machineCode);
    setCopied(true);
  }

  async function copyEmail() {
    try {
      await navigator.clipboard.writeText(ACTIVATION_CONTACT);
      setEmailCopied(true);
    } catch {
      setEmailCopied(false);
    }
  }

  async function importLicense() {
    if (!termsAccepted && wizardStep === "import") {
      setError("Accept the Terminal Activation Terms before importing a license.");
      return;
    }
    setError(null);
    const inspected = inspectTerminalLicenseCandidate(licenseJson, machineCode);
    if (!inspected.candidate) {
      setError(inspected.error ?? "The Terminal license could not be read.");
      return;
    }

    setBusy(true);
    try {
      const validated = await validateRemotePosTerminalLicense(
        { hostBaseUrl: currentBrowserOrigin() },
        licenseJson,
        machineCode,
      );
      const record = installTerminalLicense(licenseJson, {
        licenseId: validated.licenseId,
        customerName: validated.customerName,
        storeId: validated.storeId,
        hostLicenseId: validated.hostLicenseId,
        terminalId: validated.terminalId,
        machineCode: validated.machineCode,
        expiresAt: validated.expiresAt,
      });
      setInstalled(record);
      setLicenseJson("");
      setWizardStep("closed");
      setTermsAccepted(false);
    } catch (caught) {
      setError((caught as Error).message || "The Host rejected this Terminal license.");
    } finally {
      setBusy(false);
    }
  }

  function replaceLicense() {
    clearInstalledTerminalLicense();
    setInstalled(null);
    setError(null);
    setWizardStep("request");
    setTermsAccepted(false);
  }

  const canImport = termsAccepted && licenseJson.trim().length > 0 && !busy;

  return (
    <div className="min-h-screen bg-[#f4f6f9] px-3 py-5 sm:px-6 sm:py-8">
      <div className="mx-auto max-w-6xl">
        {error && wizardStep === "closed" ? (
          <div className="mb-4">
            <Alert tone="danger" title="Terminal license not accepted">
              {error}
            </Alert>
          </div>
        ) : null}

        <div className="grid items-start gap-6 lg:grid-cols-2">
          <div className="space-y-5">
            {!installed ? (
              <section className="overflow-hidden rounded-2xl border border-slate-200/90 bg-white shadow-md">
                <div className="bg-gradient-to-br from-navy-900 via-navy-800 to-blue-900 px-6 py-8 text-white sm:px-8 sm:py-10">
                  <p className="text-[11px] font-semibold uppercase tracking-[0.28em] text-emerald-300">LAN terminal activation</p>
                  <h1 className="mt-3 text-3xl font-black tracking-[0.12em] sm:text-4xl">SALRYN Terminal</h1>
                  <p className="mt-2 text-sm font-medium text-blue-100">by Kirjane Labs</p>
                  <p className="mt-5 max-w-xl text-base leading-relaxed text-white/90">
                    Connect this device to your Salryn Host as a licensed Cashier/Manager terminal.
                  </p>
                  <div className="mt-6 flex flex-wrap gap-2">
                    <Button onClick={() => { setWizardStep("request"); setTermsAccepted(false); setError(null); }}>
                      Activate this Terminal
                    </Button>
                    <Button variant="secondary" onClick={() => { setWizardStep("import"); setError(null); }}>
                      Import Terminal License
                    </Button>
                  </div>
                </div>
                <div className="px-6 py-6 sm:px-8">
                  <h2 className="text-sm font-bold uppercase tracking-[0.16em] text-navy-900">What Terminal offers</h2>
                  <div className="mt-4 grid gap-2">
                    {TERMINAL_FEATURES.map((feature) => (
                      <div key={feature} className="rounded-xl border border-slate-200 bg-slate-50/80 px-3 py-2.5 text-sm font-medium text-slate-800">
                        <span className="mr-2 inline-block h-1.5 w-1.5 rounded-full bg-emerald-500" aria-hidden />
                        {feature}
                      </div>
                    ))}
                  </div>
                </div>
              </section>
            ) : (
              <Card className="overflow-hidden border-slate-200 shadow-md">
                <CardHeader className="border-b border-emerald-100 bg-emerald-50/80">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <CardTitle>Terminal License</CardTitle>
                    <StatusPill tone="success">Active</StatusPill>
                  </div>
                </CardHeader>
                <CardBody className="space-y-4">
                  <Alert tone="success" title="Status: Active">
                    Terminal License Active. This signed Terminal license was verified by the Host and matches this terminal device.
                  </Alert>
                  <dl className="grid gap-3 text-sm sm:grid-cols-2">
                    <div>
                      <dt className="text-slate-500">Licensed to</dt>
                      <dd className="font-medium text-slate-900">{installed.customerName}</dd>
                    </div>
                    <div>
                      <dt className="text-slate-500">Store</dt>
                      <dd className="font-medium text-slate-900">{installed.customerName}</dd>
                    </div>
                    <div>
                      <dt className="text-slate-500">License type</dt>
                      <dd className="font-medium text-slate-900">LAN Terminal</dd>
                    </div>
                    <div>
                      <dt className="text-slate-500">Terminal ID</dt>
                      <dd className="font-medium text-slate-900">{installed.terminalId}</dd>
                    </div>
                  </dl>
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
                        Store ID: <span className="break-all font-mono">{installed.storeId}</span>
                      </p>
                      <p>
                        Host License ID: <span className="break-all font-mono">{installed.hostLicenseId}</span>
                      </p>
                      <p>
                        Terminal License ID: <span className="break-all font-mono">{installed.licenseId}</span>
                      </p>
                      <p>
                        Terminal Machine Code: <span className="break-all font-mono">{installed.machineCode}</span>
                      </p>
                    </div>
                  ) : null}
                  <div className="flex flex-wrap gap-2">
                    <Button onClick={onActivated}>Continue to Terminal Setup</Button>
                    <Button variant="secondary" onClick={replaceLicense}>
                      Replace Terminal License
                    </Button>
                  </div>
                </CardBody>
              </Card>
            )}

          </div>

          <div className="lg:sticky lg:top-4">
            <ProductCreditsCard />
          </div>
        </div>
      </div>

      {wizardStep === "request" ? (
        <WizardShell
          title="Activate this LAN terminal"
          stepLabel="Step 1 of 3 · Request"
          onClose={() => setWizardStep("closed")}
          footer={
            <>
              <Button variant="secondary" onClick={() => setWizardStep("closed")}>
                Cancel
              </Button>
              <Button onClick={() => setWizardStep("terms")}>Next: Terms</Button>
            </>
          }
        >
          <p>
            Send this Terminal Machine Code and a terminal name/location to your store admin or Kirjane Labs / Salryn Infodesk at{" "}
            <span className="font-mono font-semibold">{ACTIVATION_CONTACT}</span>.
          </p>
          <div className="mt-4">
            <Alert tone="info" title="Simple terminal request">
              Salryn generates this Machine Code. Your store admin or Kirjane Labs will issue a Terminal license from the Host license. You do not need a Host License ID or Store ID.
            </Alert>
          </div>
          <div className="mt-4 rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-4">
            <p className="text-xs font-semibold uppercase tracking-wide text-emerald-900">This Terminal Machine Code</p>
            <p className="mt-2 break-all font-mono text-base font-bold text-navy-900">{machineCode}</p>
            <div className="mt-3 flex flex-wrap gap-2">
              <Button variant="secondary" size="sm" onClick={() => void copyMachineCode()}>
                {copied ? "Copied" : "Copy Terminal Machine Code"}
              </Button>
              <Button variant="secondary" size="sm" onClick={() => void copyEmail()}>
                {emailCopied ? "Email copied" : `Copy ${ACTIVATION_CONTACT}`}
              </Button>
            </div>
          </div>
          <ul className="mt-4 list-disc space-y-1 pl-5 text-slate-600">
            <li>Terminal name/location, e.g. Counter 1</li>
            <li>Terminal Machine Code</li>
          </ul>
          <p className="mt-3 text-slate-600">
            Store admin or Kirjane Labs issues the Terminal license from the Host license context. Each LAN terminal needs its own signed Terminal license for this device.
          </p>
        </WizardShell>
      ) : null}

      {wizardStep === "terms" ? (
        <WizardShell
          title="Terminal Activation Terms"
          stepLabel="Step 2 of 3 · Terms"
          onClose={() => setWizardStep("closed")}
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
          <div className="max-h-72 space-y-3 overflow-y-auto rounded-xl border border-slate-200 bg-slate-50 p-4 text-sm leading-relaxed">
            <p>
              <strong>1.</strong> This is a LAN terminal license for this device/browser. It does not activate the Host POS.
            </p>
            <p>
              <strong>2.</strong> Host owns the store database. Terminal access can be disabled/removed by Host admin.
            </p>
            <p>
              <strong>3.</strong> Do not share the license to another device.
            </p>
            <p>
              <strong>4.</strong> No Host admin pages are available on terminals. Cashier/Manager workflows only.
            </p>
            <p>
              <strong>5.</strong> Terminal licenses are issued from the Host license context by store admin or Kirjane Labs.
            </p>
          </div>
          <label className="mt-4 flex items-start gap-2 text-sm font-medium">
            <input type="checkbox" className="mt-1" checked={termsAccepted} onChange={(e) => setTermsAccepted(e.target.checked)} />
            <span>I have read and accept the Terminal Activation Terms.</span>
          </label>
        </WizardShell>
      ) : null}

      {wizardStep === "import" ? (
        <WizardShell
          title="Import Signed Terminal License JSON"
          stepLabel="Step 3 of 3 · Import"
          onClose={() => setWizardStep("closed")}
          footer={
            <>
              <Button variant="secondary" onClick={() => setWizardStep("terms")}>
                Back
              </Button>
              <Button disabled={!canImport} onClick={() => void importLicense()}>
                {busy ? "Verifying..." : "Import Terminal License"}
              </Button>
            </>
          }
        >
          {error ? <Alert tone="danger">{error}</Alert> : null}
          <p className="mt-2">
            Paste the signed Terminal license JSON you received. Host licenses cannot activate a terminal.
          </p>
          <label className="mt-3 block space-y-2 text-sm font-medium text-slate-700">
            <span>Signed Terminal license JSON</span>
            <textarea
              className="min-h-56 w-full rounded-md border border-slate-300 bg-white px-3 py-2 font-mono text-xs text-slate-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-200"
              value={licenseJson}
              onChange={(event) => setLicenseJson(event.target.value)}
              placeholder="Paste the signed Terminal license you received"
              spellCheck={false}
            />
          </label>
        </WizardShell>
      ) : null}
    </div>
  );
}
