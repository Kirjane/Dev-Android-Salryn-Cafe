import { useMemo, useState } from "react";
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
      <main className="mx-auto flex min-h-screen w-full max-w-4xl flex-col justify-center px-4 py-8">
        <div className="mb-6">
          <div className="flex items-center gap-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-navy-900 text-xs font-bold text-white">
              SL
            </div>
            <div>
              <p className="text-xs uppercase tracking-[0.25em] text-slate-500">Salryn LAN</p>
              <h1 className="text-3xl font-semibold text-slate-950">Connect to Salryn Host</h1>
            </div>
          </div>
          <p className="mt-3 text-base text-slate-600">This is a remote client device</p>
        </div>

        <Card>
          <CardHeader>
            <CardTitle>Remote client entry</CardTitle>
          </CardHeader>
          <CardBody className="space-y-6">
            <div className="grid gap-3 text-sm text-slate-700 md:grid-cols-2">
              <p className="rounded-lg border border-slate-200 bg-slate-50 px-4 py-3">
                The host owns the database, sales, inventory, and LAN approvals
              </p>
              <p className="rounded-lg border border-slate-200 bg-slate-50 px-4 py-3">
                Do not use localhost from another device
              </p>
              <p className="rounded-lg border border-slate-200 bg-slate-50 px-4 py-3">
                Use the host LAN address shown on the host PC
              </p>
              <p className="rounded-lg border border-slate-200 bg-slate-50 px-4 py-3">
                Remote POS allows Cashier or Manager only after pairing
              </p>
            </div>

            {storedRemotePosPairing ? (
              <button
                type="button"
                onClick={onOpenRemotePos}
                className="w-full rounded-lg border border-emerald-200 bg-emerald-50 px-5 py-4 text-left shadow-sm hover:border-emerald-300 hover:bg-emerald-100"
              >
                <div className="flex items-center justify-between gap-3">
                  <span className="text-base font-semibold text-slate-950">Resume Remote POS terminal</span>
                  <StatusPill tone="success">Paired device</StatusPill>
                </div>
                <p className="mt-2 text-sm text-slate-600">
                  This device already has a trusted terminal pairing for{" "}
                  <span className="font-mono text-slate-700">{storedRemotePosPairing.hostBaseUrl}</span>
                  {storedRemotePosPairing.displayName ? ` (${storedRemotePosPairing.displayName})` : ""}. Open Remote POS to
                  restore this paired terminal. If the saved cashier session is still valid, it will resume automatically
                  after the host validates it; otherwise sign in again without re-pairing.
                </p>
              </button>
            ) : null}

            <div className="grid gap-4 md:grid-cols-2">
              <button
                type="button"
                onClick={onOpenRemotePos}
                className="rounded-lg border border-slate-200 bg-white px-5 py-4 text-left shadow-sm hover:border-blue-300 hover:bg-blue-50"
              >
                <div className="flex items-center justify-between gap-3">
                  <span className="text-base font-semibold text-slate-950">Remote POS Client</span>
                  <StatusPill tone="primary">{storedRemotePosPairing ? "Open paired terminal" : "Pair first"}</StatusPill>
                </div>
                <p className="mt-2 text-sm text-slate-600">
                  Request approval with the host join code and device name, enter the one-time pairing code, then sign in.
                </p>
              </button>

              <button
                type="button"
                onClick={onOpenReadOnly}
                className="rounded-lg border border-slate-200 bg-white px-5 py-4 text-left shadow-sm hover:border-blue-300 hover:bg-blue-50"
              >
                <div className="flex items-center justify-between gap-3">
                  <span className="text-base font-semibold text-slate-950">Remote Read-Only Client</span>
                  <StatusPill tone="info">Read-only</StatusPill>
                </div>
                <p className="mt-2 text-sm text-slate-600">
                  Use only the existing terminal and handshake requirements for safe host-owned reads.
                </p>
              </button>
            </div>

            <div className="space-y-3 rounded-lg border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-700">
              <p className="font-medium text-slate-900">Stuck after a new build or host DB reset?</p>
              <p>
                Clear this device state removes saved terminal pairing and any cashier session token. Passwords are never
                stored. Then request access again.
              </p>
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
                <p className="text-emerald-700">Device state cleared. Open Remote POS to request access again.</p>
              ) : null}
            </div>
          </CardBody>
        </Card>
      </main>
    </div>
  );
}
