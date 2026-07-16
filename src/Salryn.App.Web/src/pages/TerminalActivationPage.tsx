import { useMemo, useState } from "react";
import { validateRemotePosTerminalLicense } from "../api/remotePosClient";
import { Alert, Button, Card, CardBody, CardHeader, CardTitle, CommandBar, StatusPill } from "../components/ui";
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

type TerminalActivationPageProps = {
  onActivated: () => void;
};

export function TerminalActivationPage({ onActivated }: TerminalActivationPageProps) {
  const machineCode = useMemo(() => getOrCreateTerminalMachineCode(), []);
  const [licenseJson, setLicenseJson] = useState("");
  const [installed, setInstalled] = useState<InstalledTerminalLicense | null>(() => readInstalledTerminalLicense());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);

  async function copyMachineCode() {
    await navigator.clipboard.writeText(machineCode);
    setCopied(true);
  }

  async function importLicense() {
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
  }

  return (
    <div className="min-h-screen bg-[#f6f7f9] p-6">
      <div className="mx-auto max-w-4xl space-y-5">
        <CommandBar
          title="Terminal Activation"
          subtitle="Activate this LAN terminal before connecting it to the Salryn POS Host."
          status={<StatusPill tone={installed ? "success" : "neutral"}>{installed ? "Active" : "Activation required"}</StatusPill>}
        />

        {error ? <Alert tone="danger" title="Terminal license not accepted">{error}</Alert> : null}

        <div className="grid gap-5 lg:grid-cols-2">
          <Card>
            <CardHeader><CardTitle>This Terminal Machine Code</CardTitle></CardHeader>
            <CardBody className="space-y-4">
              <p className="break-all rounded-md bg-slate-100 px-4 py-3 font-mono text-sm font-semibold text-slate-900">
                {machineCode}
              </p>
              <Button variant="secondary" onClick={() => void copyMachineCode()}>{copied ? "Copied" : "Copy Machine Code"}</Button>
              <div className="space-y-2 text-sm text-slate-600">
                <p>Send this code to <strong className="text-slate-900">{ACTIVATION_CONTACT}</strong>.</p>
                <p>Include your store name, Host License ID, and terminal name or number.</p>
                <p>Each LAN terminal needs its own signed Terminal license.</p>
              </div>
            </CardBody>
          </Card>

          <Card>
            <CardHeader><CardTitle>{installed ? "Terminal License" : "Import Terminal License"}</CardTitle></CardHeader>
            <CardBody className="space-y-4">
              {installed ? (
                <>
                  <Alert tone="success" title="Status: Active">
                    This signed Terminal license was verified by the Host and matches this terminal device.
                  </Alert>
                  <dl className="grid gap-3 text-sm sm:grid-cols-2">
                    <div><dt className="text-slate-500">Licensed to</dt><dd className="font-medium text-slate-900">{installed.customerName}</dd></div>
                    <div><dt className="text-slate-500">Terminal ID</dt><dd className="font-medium text-slate-900">{installed.terminalId}</dd></div>
                    <div><dt className="text-slate-500">Store ID</dt><dd className="font-medium text-slate-900">{installed.storeId}</dd></div>
                    <div><dt className="text-slate-500">Host License ID</dt><dd className="break-all font-medium text-slate-900">{installed.hostLicenseId}</dd></div>
                  </dl>
                  <div className="flex flex-wrap gap-2">
                    <Button onClick={onActivated}>Continue to Terminal Setup</Button>
                    <Button variant="secondary" onClick={replaceLicense}>Replace Terminal License</Button>
                  </div>
                </>
              ) : (
                <>
                  <p className="text-sm text-slate-600">
                    Paste only the signed Terminal license JSON. Host licenses are rejected and cannot activate this device.
                  </p>
                  <label className="space-y-2 text-sm font-medium text-slate-700">
                    <span>Signed Terminal license JSON</span>
                    <textarea
                      className="min-h-56 w-full rounded-md border border-slate-300 bg-white px-3 py-2 font-mono text-xs text-slate-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-200"
                      value={licenseJson}
                      onChange={(event) => setLicenseJson(event.target.value)}
                      placeholder="Paste the signed Terminal license you received"
                      spellCheck={false}
                    />
                  </label>
                  <Button onClick={() => void importLicense()} disabled={busy || !licenseJson.trim()}>
                    {busy ? "Verifying..." : "Import Terminal License"}
                  </Button>
                </>
              )}
            </CardBody>
          </Card>
        </div>
      </div>
    </div>
  );
}
