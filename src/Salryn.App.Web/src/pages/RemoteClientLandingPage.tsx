import { useMemo, useState } from "react";
import { Layers3 } from "lucide-react";
import { Button, Card, CardBody, CardHeader, CardTitle, StatusPill } from "../components/ui";
import { currentBrowserOrigin } from "../utils/lanEntry";
import {
  clearAllLanClientDeviceState,
  hasPairedTerminalMaterial,
  readLanClientPairing,
} from "../utils/lanClientStorage";

type RemoteClientLandingPageProps = {
  onOpenRemotePos: () => void;
  onOpenReadOnly: () => void;
  onManageTerminalLicense: () => void;
};

/**
 * Terminal client entry after license activation.
 * Primary path: Salryn POS terminal (pair + Cashier/Manager login).
 * Remote Read-Only is support/internal only — not a normal beta choice.
 */
export function RemoteClientLandingPage({
  onOpenRemotePos,
  onOpenReadOnly,
  onManageTerminalLicense,
}: RemoteClientLandingPageProps) {
  const [clearedNotice, setClearedNotice] = useState(false);
  const [hasPairingMaterial, setHasPairingMaterial] = useState(true);

  const storedRemotePosPairing = useMemo(() => {
    if (!hasPairingMaterial) {
      return null;
    }
    const hostOrigin = currentBrowserOrigin();
    if (!hostOrigin) {
      return null;
    }

    const stored = readLanClientPairing("remote-pos", hostOrigin);
    return hasPairedTerminalMaterial(stored) ? stored : null;
  }, [hasPairingMaterial]);

  function clearThisDeviceState() {
    if (
      !window.confirm(
        "Clear this device pairing and saved cashier session? This device will need to request access from the host again.",
      )
    ) {
      return;
    }
    clearAllLanClientDeviceState();
    setHasPairingMaterial(false);
    setClearedNotice(true);
  }

  return (
    <div className="min-h-screen bg-slate-100">
      <header className="flex h-[60px] items-center justify-between border-b border-white/10 bg-navy-900 px-5 text-white shadow-sm">
        <div className="flex items-center gap-3">
          <div className="flex h-9 w-9 items-center justify-center rounded-md bg-white text-navy-900">
            <Layers3 aria-hidden="true" size={21} />
          </div>
          <div>
            <p className="text-sm font-bold">Salryn POS</p>
            <p className="text-xs text-slate-400">Terminal Mode · Client entry</p>
          </div>
        </div>
        <StatusPill tone="primary">Terminal Mode</StatusPill>
      </header>

      <main className="mx-auto flex w-full max-w-3xl flex-col px-4 py-8">
        <div className="mb-6">
          <h1 className="text-3xl font-semibold text-slate-950">Open Salryn on this terminal</h1>
          <p className="mt-2 text-base text-slate-600">
            Same Salryn POS product language as the Host workstation. Cashier and Manager only — Host admin stays on the Host PC.
          </p>
        </div>

        <Card>
          <CardHeader>
            <CardTitle>Terminal workstation</CardTitle>
          </CardHeader>
          <CardBody className="space-y-6">
            <div className="grid gap-3 text-sm text-slate-700 md:grid-cols-2">
              <p className="rounded-lg border border-slate-200 bg-slate-50 px-4 py-3">
                The Host owns the database, approvals, and admin tools
              </p>
              <p className="rounded-lg border border-slate-200 bg-slate-50 px-4 py-3">
                Use the Host LAN address shown on the Host PC
              </p>
              <p className="rounded-lg border border-slate-200 bg-slate-50 px-4 py-3">
                Pair this device, then sign in as Cashier or Manager
              </p>
              <p className="rounded-lg border border-slate-200 bg-slate-50 px-4 py-3">
                Admin and Owner accounts remain host-local
              </p>
            </div>

            {storedRemotePosPairing ? (
              <button
                type="button"
                onClick={onOpenRemotePos}
                className="w-full rounded-lg border border-emerald-200 bg-emerald-50 px-5 py-4 text-left shadow-sm hover:border-emerald-300 hover:bg-emerald-100"
              >
                <div className="flex items-center justify-between gap-3">
                  <span className="text-base font-semibold text-slate-950">Resume Salryn terminal</span>
                  <StatusPill tone="success">Paired device</StatusPill>
                </div>
                <p className="mt-2 text-sm text-slate-600">
                  This device already has a trusted terminal pairing for{" "}
                  <span className="font-mono text-slate-700">{storedRemotePosPairing.hostBaseUrl}</span>
                  {storedRemotePosPairing.displayName ? ` (${storedRemotePosPairing.displayName})` : ""}. Open the terminal shell
                  to restore pairing. If a saved cashier session is still valid, it resumes after Host validation.
                </p>
              </button>
            ) : null}

            <button
              type="button"
              onClick={onOpenRemotePos}
              className="w-full rounded-lg border border-blue-200 bg-white px-5 py-4 text-left shadow-sm hover:border-blue-300 hover:bg-blue-50"
            >
              <div className="flex items-center justify-between gap-3">
                <span className="text-base font-semibold text-slate-950">Open Salryn POS terminal</span>
                <StatusPill tone="primary">{storedRemotePosPairing ? "Open paired terminal" : "Pair first"}</StatusPill>
              </div>
              <p className="mt-2 text-sm text-slate-600">
                Request approval with the Host join code and device name, enter the one-time pairing code, then sign in.
                After login you get the Salryn terminal shell (POS Checkout).
              </p>
            </button>

            <div className="space-y-3 rounded-lg border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-700">
              <p className="font-medium text-slate-900">Device tools</p>
              <div className="flex flex-wrap gap-2">
                <Button variant="secondary" onClick={onManageTerminalLicense}>
                  Terminal License
                </Button>
                <Button variant="secondary" onClick={clearThisDeviceState}>
                  Clear this device state
                </Button>
                <Button variant="secondary" onClick={() => window.location.reload()}>
                  Reload
                </Button>
              </div>
              {clearedNotice ? (
                <p className="text-emerald-700">Device state cleared. Open Salryn POS terminal to request access again.</p>
              ) : null}
            </div>

            <details className="rounded-lg border border-dashed border-slate-300 bg-white px-4 py-3 text-sm text-slate-600">
              <summary className="cursor-pointer font-medium text-slate-800">Internal support surfaces</summary>
              <p className="mt-2">
                Remote Read-Only Client is support/diagnostic only. It is not the normal beta terminal experience.
              </p>
              <div className="mt-3">
                <Button variant="ghost" size="sm" onClick={onOpenReadOnly}>
                  Open Remote Read-Only (support only)
                </Button>
              </div>
            </details>
          </CardBody>
        </Card>
      </main>
    </div>
  );
}
