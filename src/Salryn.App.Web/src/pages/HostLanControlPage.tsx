import { useState } from "react";
import {
  Alert,
  Button,
  Card,
  CardBody,
  CardHeader,
  CardTitle,
  CommandBar,
  ErrorState,
  LoadingState,
  StatusPill,
  TextInput,
} from "../components/ui";
import { useLocalLanControl } from "../hooks/useLocalLanControl";

function formatDate(value: string | null | undefined): string {
  if (!value) return "-";
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString();
}

function enabledLabel(value: boolean): string {
  return value ? "Enabled" : "Disabled";
}

export function HostLanControlPage() {
  const {
    statusQuery,
    pairingStatusQuery,
    remoteTransportMutation,
    remotePosRuntimeMutation,
    disableTerminalMutation,
    revokeTerminalMutation,
    rotateJoinCodeMutation,
    acceptJoinRequestMutation,
    rejectJoinRequestMutation,
  } = useLocalLanControl();

  const [bindAddressInput, setBindAddressInput] = useState("");
  const [copied, setCopied] = useState(false);
  const [acceptedCodes, setAcceptedCodes] = useState<Record<string, string>>({});

  const status = statusQuery.data;
  const pairing = pairingStatusQuery.data;
  const limits = status?.remotePosLimits ?? null;
  const pendingRequests = pairing?.pendingRequests ?? [];
  const pendingRequestCount = pairing?.pendingRequestCount ?? pendingRequests.length;
  const capacitySummary = `${limits?.currentPairedCount ?? 0} / ${limits?.maxPairedTerminals ?? 5} paired, ${limits?.currentActiveSessions ?? 0} / ${limits?.maxActiveSessions ?? 3} active`;
  const clientUrl = status?.hostLanUrl && status.hostLanUrl !== "Not enabled" ? status.hostLanUrl : "";
  const busy =
    remoteTransportMutation.isPending ||
    remotePosRuntimeMutation.isPending ||
    disableTerminalMutation.isPending ||
    revokeTerminalMutation.isPending ||
    rotateJoinCodeMutation.isPending ||
    acceptJoinRequestMutation.isPending ||
    rejectJoinRequestMutation.isPending;
  const actionError =
    (remoteTransportMutation.error as Error | null)?.message ??
    (remotePosRuntimeMutation.error as Error | null)?.message ??
    (disableTerminalMutation.error as Error | null)?.message ??
    (revokeTerminalMutation.error as Error | null)?.message ??
    (rotateJoinCodeMutation.error as Error | null)?.message ??
    (acceptJoinRequestMutation.error as Error | null)?.message ??
    (rejectJoinRequestMutation.error as Error | null)?.message ??
    null;

  function refreshAll() {
    void statusQuery.refetch();
    void pairingStatusQuery.refetch();
  }

  function copyClientUrl() {
    if (!clientUrl) return;
    setCopied(false);
    void navigator.clipboard?.writeText(clientUrl).then(() => setCopied(true));
  }

  function acceptRequest(requestId: string) {
    acceptJoinRequestMutation.mutate(requestId, {
      onSuccess: (response) => {
        setAcceptedCodes((current) => ({ ...current, [response.requestId]: response.pairingCode }));
      },
    });
  }

  return (
    <div className="space-y-6">
      <CommandBar
        title="Host LAN Control"
        subtitle="Local-only host controls for LAN transport, Remote POS pairing, terminal trust, and capacity."
        status={<StatusPill tone="success">Host control is local-only</StatusPill>}
        actions={
          <Button variant="secondary" onClick={refreshAll}>
            Refresh
          </Button>
        }
      />

      {statusQuery.isLoading ? <LoadingState label="Loading host LAN control..." /> : null}
      {statusQuery.isError ? <ErrorState message={(statusQuery.error as Error).message} /> : null}
      {actionError ? <ErrorState message={actionError} /> : null}

      {status ? (
        <>
          <div className="grid gap-4 xl:grid-cols-3">
            <Card>
              <CardHeader>
                <CardTitle>Top Status</CardTitle>
              </CardHeader>
              <CardBody>
                <dl className="space-y-3 text-sm">
                  <Row label="LAN transport" value={enabledLabel(status.remoteTransportEnabled)} />
                  <Row label="Host local URL" value={status.hostLocalUrl} />
                  <Row label="Host LAN URL" value={status.hostLanUrl} />
                  <Row label="Remote POS runtime" value={enabledLabel(status.remotePosRuntimeEnabled)} />
                  <Row label="Remote Read-Only" value={enabledLabel(status.remoteTransportEnabled)} />
                  <Row label="Capacity" value={capacitySummary} />
                  <Row label="Safety" value={status.safety} />
                </dl>
              </CardBody>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Limits</CardTitle>
              </CardHeader>
              <CardBody>
                <dl className="space-y-3 text-sm">
                  <Row label="Max paired Remote POS terminals" value={String(limits?.maxPairedTerminals ?? 5)} />
                  <Row label="Max active Remote POS sessions" value={String(limits?.maxActiveSessions ?? 3)} />
                  <Row label="Hard cap" value={String(limits?.hardCap ?? 10)} />
                  <Row label="Current paired count" value={String(limits?.currentPairedCount ?? 0)} />
                  <Row label="Current active sessions" value={String(limits?.currentActiveSessions ?? 0)} />
                  <Row label="Remaining capacity" value={`${limits?.remainingPairingCapacity ?? 0} terminals / ${limits?.remainingActiveSessionCapacity ?? 0} sessions`} />
                </dl>
              </CardBody>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Client URL</CardTitle>
              </CardHeader>
              <CardBody className="space-y-3">
                <Alert tone="warning">Client devices use the host LAN IP, not localhost.</Alert>
                <p className="break-all rounded-lg border border-slate-200 bg-slate-50 px-3 py-2 font-mono text-xs text-slate-700">
                  {clientUrl || "Enable LAN transport with an explicit bind address first."}
                </p>
                <Button variant="secondary" disabled={!clientUrl} onClick={copyClientUrl}>
                  Copy client URL
                </Button>
                {copied ? <p className="text-xs text-slate-500">Client URL copied.</p> : null}
              </CardBody>
            </Card>
          </div>

          <Card>
            <CardHeader>
              <div className="flex flex-wrap items-center justify-between gap-3">
                <CardTitle>Pairing Control</CardTitle>
                {pendingRequestCount > 0 ? (
                  <StatusPill tone="warning">{pendingRequestCount} pending</StatusPill>
                ) : (
                  <StatusPill tone="success">No pending requests</StatusPill>
                )}
              </div>
            </CardHeader>
            <CardBody>
              {pendingRequestCount > 0 ? (
                <Alert tone="warning" title="Device wants to join">
                  Review the pending device request here, then accept or reject it from this host PC.
                </Alert>
              ) : null}
              {pairingStatusQuery.isLoading ? <LoadingState label="Loading pending device requests..." /> : null}
              {pairingStatusQuery.isError ? <ErrorState message={(pairingStatusQuery.error as Error).message} /> : null}
              <p className="my-4 text-sm text-slate-600">
                Pending device requests are the approval mechanism. Rotate a join code, have the client submit
                its device name, then accept or reject the request here.
              </p>
              {pendingRequests.length ? (
                <div className="space-y-3">
                  {pendingRequests.map((request) => (
                    <div key={request.requestId} className="rounded-lg border border-slate-200 bg-white px-4 py-3 text-sm">
                      <div className="flex flex-wrap items-start justify-between gap-3">
                        <div>
                          <p className="font-medium text-slate-900">{request.deviceName}</p>
                          <p className="text-slate-600">Observed origin/IP: {request.observedOrigin || "Unavailable"}</p>
                          <p className="text-slate-600">Requested: {formatDate(request.requestedAtUtc)}</p>
                        </div>
                        <StatusPill tone={request.pairingState === "Pending" ? "warning" : "info"}>{request.pairingState}</StatusPill>
                      </div>
                      {acceptedCodes[request.requestId] ? (
                        <div className="mt-3 rounded-lg border border-emerald-200 bg-emerald-50 px-3 py-2 text-emerald-950">
                          <p className="font-medium">One-time pairing code</p>
                          <p className="font-mono text-lg">{acceptedCodes[request.requestId]}</p>
                          <p className="mt-1">Give this code to the client device.</p>
                        </div>
                      ) : null}
                      {request.pairingState === "Pending" ? (
                        <div className="mt-3 flex flex-wrap gap-2">
                          <Button size="sm" disabled={busy} onClick={() => acceptRequest(request.requestId)}>
                            Accept
                          </Button>
                          <Button size="sm" variant="secondary" disabled={busy} onClick={() => rejectJoinRequestMutation.mutate(request.requestId)}>
                            Reject
                          </Button>
                        </div>
                      ) : null}
                    </div>
                  ))}
                </div>
              ) : (
                <p className="text-sm text-slate-500">No pending device requests.</p>
              )}
            </CardBody>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>Main Controls</CardTitle>
            </CardHeader>
            <CardBody className="space-y-5">
              <div className="space-y-2">
                <label htmlFor="host-lan-control-bind-address" className="block text-sm font-medium text-slate-700">
                  Set explicit bind address
                </label>
                <p className="text-xs text-slate-500">
                  Enable LAN transport with this host LAN IP, or disable LAN transport when clients should not connect.
                </p>
                <div className="grid gap-3 lg:grid-cols-[1fr_auto_auto]">
                  <TextInput
                    id="host-lan-control-bind-address"
                    value={bindAddressInput}
                    onChange={(event) => setBindAddressInput(event.target.value)}
                    placeholder="Explicit LAN IP address"
                    disabled={busy}
                  />
                  <Button
                    variant="secondary"
                    disabled={busy || bindAddressInput.trim().length === 0}
                    onClick={() => remoteTransportMutation.mutate({ enable: true, bindAddress: bindAddressInput.trim() })}
                  >
                    Save/apply LAN settings
                  </Button>
                  <Button
                    variant="secondary"
                    disabled={busy}
                    onClick={() => remoteTransportMutation.mutate({ enable: false, bindAddress: null })}
                  >
                    Disable LAN transport
                  </Button>
                </div>
              </div>

              <div className="flex flex-wrap gap-3">
                <Button
                  variant="secondary"
                  disabled={busy || status.remotePosRuntimeEnabled}
                  onClick={() => remotePosRuntimeMutation.mutate({ enable: true })}
                >
                  Enable Remote POS runtime
                </Button>
                <Button
                  variant="secondary"
                  disabled={busy || !status.remotePosRuntimeEnabled}
                  onClick={() => remotePosRuntimeMutation.mutate({ enable: false })}
                >
                  Disable Remote POS runtime
                </Button>
                <Button variant="secondary" disabled={busy} onClick={() => rotateJoinCodeMutation.mutate()}>
                  Rotate/show host join code
                </Button>
              </div>

              {rotateJoinCodeMutation.data ? (
                <div className="rounded-lg border border-blue-200 bg-blue-50 px-4 py-3 text-sm text-blue-950">
                  <p className="font-medium">Host join code</p>
                  <p className="mt-1 font-mono text-lg">{rotateJoinCodeMutation.data.joinCode}</p>
                  <p className="mt-1">Expires {formatDate(rotateJoinCodeMutation.data.expiresAtUtc)}</p>
                </div>
              ) : null}
            </CardBody>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>Terminal Control</CardTitle>
            </CardHeader>
            <CardBody>
              <p className="mb-4 text-sm text-slate-600">{status.terminalVisibilityMessage}</p>
              <p className="mb-4 text-sm font-medium text-slate-700">
                Remaining capacity: {limits?.remainingPairingCapacity ?? 0} terminals / {limits?.remainingActiveSessionCapacity ?? 0} sessions.
              </p>
              {status.terminals.length > 0 ? (
                <div className="space-y-3">
                  {status.terminals.map((terminal) => (
                    <div key={terminal.terminalId} className="rounded-lg border border-slate-200 bg-white px-4 py-3 text-sm">
                      <div className="flex flex-wrap items-center justify-between gap-3">
                        <div>
                          <p className="font-medium text-slate-900">{terminal.terminalName}</p>
                          <p className="text-slate-600">{terminal.terminalRole} / {terminal.trustState}</p>
                          <p className="text-slate-600">Last seen: {formatDate(terminal.lastSeenAtUtc)}</p>
                        </div>
                        <div className="flex flex-wrap items-center gap-2">
                          <StatusPill tone={terminal.trusted ? "success" : "warning"}>{terminal.status}</StatusPill>
                          <Button
                            size="sm"
                            variant="secondary"
                            disabled={busy}
                            onClick={() => disableTerminalMutation.mutate(terminal.terminalId)}
                          >
                            Disable
                          </Button>
                          <Button
                            size="sm"
                            variant="secondary"
                            disabled={busy}
                            onClick={() => revokeTerminalMutation.mutate(terminal.terminalId)}
                          >
                            Revoke
                          </Button>
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              ) : (
                <p className="text-sm text-slate-500">No paired terminals are visible to this operator.</p>
              )}
            </CardBody>
          </Card>

          <details className="rounded-lg border border-slate-200 bg-white px-5 py-4 text-sm">
            <summary className="cursor-pointer font-medium text-slate-900">Advanced diagnostics</summary>
            <div className="mt-4 space-y-4">
              <dl className="grid gap-3 md:grid-cols-2">
                <Row label="Bind mode" value={status.bindMode} />
                <Row label="Configured bind address" value={status.configuredBindAddress} />
                <Row label="Default bind mode" value={status.bindPolicy.defaultMode} />
                <Row label="Effective bind mode" value={status.bindPolicy.effectiveMode} />
                <Row label="Specific IP only" value={status.bindPolicy.specificIpOnly ? "Yes" : "No"} />
                <Row label="No wildcard bind" value={status.bindPolicy.noWildcardBind === false ? "No" : "Yes"} />
              </dl>
              <p className="text-slate-600">{status.bindPolicy.message}</p>
              <div className="space-y-2">
                {status.warnings.map((warning) => (
                  <Alert key={warning} tone="warning">
                    {warning}
                  </Alert>
                ))}
              </div>
            </div>
          </details>
        </>
      ) : null}
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center justify-between gap-4">
      <dt className="text-slate-500">{label}</dt>
      <dd className="break-words text-right font-medium text-slate-900">{value}</dd>
    </div>
  );
}
