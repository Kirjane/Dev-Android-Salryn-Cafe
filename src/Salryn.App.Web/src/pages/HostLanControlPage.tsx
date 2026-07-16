import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { RefreshCw } from "lucide-react";
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
  MetricCard,
  StatusPill,
  TextInput,
} from "../components/ui";
import { fetchLicenseStatus } from "../api/license";
import { useLocalLanControl } from "../hooks/useLocalLanControl";
import type { LocalLanControlRuntimeStatus, LocalLanControlTerminal } from "../types/remoteReadOnlyClient";
import { looksLikeBareIpv4, normalizeConfiguredLanBindInput } from "../utils/lanBindNormalize";
import {
  canClearTerminalOperatorSessions,
  canDisableTerminal,
  canHideTerminalFromList,
  canRestoreDisabledTerminal,
  canRevokeTerminal,
  formatTerminalRoleLabel,
  formatTerminalStatusLabel,
  isHostLocalTerminalRole,
  isRevokedTerminalReadOnly,
  terminalConnectionGroup,
  terminalGroupTitle,
  terminalLifecycleExplanation,
  terminalStatusTone,
  type TerminalConnectionGroup,
} from "../utils/terminalLifecycleUx";

const HIDDEN_TERMINALS_KEY = "salryn.hostLan.hiddenTerminals.v1";

function formatDate(value: string | null | undefined): string {
  if (!value) return "-";
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString();
}

function enabledLabel(value: boolean): string {
  return value ? "Enabled" : "Disabled";
}

function readinessTone(state: string): "success" | "warning" | "danger" | "info" {
  switch (state) {
    case "Ready":
      return "success";
    case "LoopbackOnly":
    case "BindIpNotOnHost":
    case "Misconfigured":
      return "danger";
    case "Disabled":
      return "warning";
    default:
      return "info";
  }
}

function readinessTitle(state: string): string {
  switch (state) {
    case "Ready":
      return "Ready";
    case "LoopbackOnly":
      return "Offline / loopback only — restart required";
    case "BindIpNotOnHost":
      return "Misconfigured — bind IP not on this host";
    case "Misconfigured":
      return "Misconfigured — repair LAN address";
    case "Disabled":
      return "Offline — LAN transport disabled";
    default:
      return state;
  }
}

function readinessBadge(state: string): string {
  switch (state) {
    case "Ready":
      return "Ready";
    case "LoopbackOnly":
      return "Restart required";
    case "BindIpNotOnHost":
    case "Misconfigured":
      return "Needs repair";
    case "Disabled":
      return "Off";
    default:
      return "Check setup";
  }
}

function readinessGuidance(state: string): string {
  switch (state) {
    case "LoopbackOnly":
      return "Restart Salryn to apply the selected LAN address.";
    case "BindIpNotOnHost":
      return "Choose a LAN address detected on this host, then repair the saved address.";
    case "Misconfigured":
      return "Repair the saved LAN address before connecting terminal devices.";
    case "Disabled":
      return "Enter this host's LAN IPv4 address, then activate the LAN connection.";
    default:
      return "Review the LAN setup and try again.";
  }
}

function readHiddenTerminalIds(): string[] {
  if (typeof window === "undefined") return [];
  try {
    const raw = window.localStorage.getItem(HIDDEN_TERMINALS_KEY);
    if (!raw) return [];
    const parsed: unknown = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed.filter((id): id is string => typeof id === "string") : [];
  } catch {
    return [];
  }
}

function writeHiddenTerminalIds(ids: string[]): void {
  if (typeof window === "undefined") return;
  window.localStorage.setItem(HIDDEN_TERMINALS_KEY, JSON.stringify(ids));
}

export function HostLanControlPage() {
  const {
    statusQuery,
    runtimeStatusQuery,
    pairingStatusQuery,
    remoteTransportMutation,
    repairBindMutation,
    remotePosRuntimeMutation,
    disableTerminalMutation,
    revokeTerminalMutation,
    clearTerminalSessionsMutation,
    rotateJoinCodeMutation,
    acceptJoinRequestMutation,
    rejectJoinRequestMutation,
  } = useLocalLanControl();

  const [bindAddressInput, setBindAddressInput] = useState("");
  const [copied, setCopied] = useState(false);
  const [acceptedCodes, setAcceptedCodes] = useState<Record<string, string>>({});
  const [hiddenTerminalIds, setHiddenTerminalIds] = useState<string[]>(() => readHiddenTerminalIds());
  const [requestCopied, setRequestCopied] = useState(false);
  const licenseQuery = useQuery({ queryKey: ["license-status"], queryFn: fetchLicenseStatus, retry: false });
  const [showTroubleshooting, setShowTroubleshooting] = useState(false);

  const status = statusQuery.data;
  const runtime = runtimeStatusQuery.data;
  const pairing = pairingStatusQuery.data;
  const limits = status?.remotePosLimits ?? null;
  const pendingRequests = pairing?.pendingRequests ?? [];
  const pendingRequestCount = pairing?.pendingRequestCount ?? pendingRequests.length;
  const hostLanEntitled = limits?.hostLanEntitled === true && (limits?.maxPairedTerminals ?? 0) > 0;
  const capacitySummary = `${limits?.currentPairedCount ?? 0} / ${limits?.maxPairedTerminals ?? 0} paired, ${limits?.currentActiveSessions ?? 0} / ${limits?.maxActiveSessions ?? 0} active`;

  const repairIp =
    runtime?.configuredBindAddressRepairSuggestion?.trim() ||
    runtime?.configuredBindAddressNormalized?.trim() ||
    runtime?.detectedLanListenIps?.[0] ||
    "";

  const clientUrl =
    runtime?.clientUrlReady && runtime.clientUrl
      ? runtime.clientUrl
      : repairIp && looksLikeBareIpv4(repairIp) && runtime?.port
        ? `http://${repairIp}:${runtime.port}/remote-client`
        : "";

  const canCopyClientUrl = Boolean(runtime?.clientUrlReady && runtime.clientUrl);
  const busy =
    remoteTransportMutation.isPending ||
    repairBindMutation.isPending ||
    remotePosRuntimeMutation.isPending ||
    disableTerminalMutation.isPending ||
    revokeTerminalMutation.isPending ||
    clearTerminalSessionsMutation.isPending ||
    rotateJoinCodeMutation.isPending ||
    acceptJoinRequestMutation.isPending ||
    rejectJoinRequestMutation.isPending;
  const actionError =
    (remoteTransportMutation.error as Error | null)?.message ??
    (repairBindMutation.error as Error | null)?.message ??
    (remotePosRuntimeMutation.error as Error | null)?.message ??
    (disableTerminalMutation.error as Error | null)?.message ??
    (revokeTerminalMutation.error as Error | null)?.message ??
    (clearTerminalSessionsMutation.error as Error | null)?.message ??
    (rotateJoinCodeMutation.error as Error | null)?.message ??
    (acceptJoinRequestMutation.error as Error | null)?.message ??
    (rejectJoinRequestMutation.error as Error | null)?.message ??
    null;

  const visibleTerminals = useMemo(() => {
    const terminals = status?.terminals ?? [];
    return terminals.filter((terminal) => !hiddenTerminalIds.includes(terminal.terminalId));
  }, [status?.terminals, hiddenTerminalIds]);

  const groupedTerminals = useMemo(() => {
    const groups: Record<TerminalConnectionGroup, LocalLanControlTerminal[]> = {
      active: [],
      blocked: [],
      retired: [],
      "host-local": [],
    };
    for (const terminal of visibleTerminals) {
      groups[terminalConnectionGroup(terminal.status, terminal.terminalRole)].push(terminal);
    }
    return groups;
  }, [visibleTerminals]);

  function refreshAll() {
    void statusQuery.refetch();
    void runtimeStatusQuery.refetch();
    void pairingStatusQuery.refetch();
  }

  function copyClientUrl() {
    if (!canCopyClientUrl || !runtime?.clientUrl) return;
    setCopied(false);
    void navigator.clipboard?.writeText(runtime.clientUrl).then(() => setCopied(true));
  }

  function acceptRequest(requestId: string) {
    acceptJoinRequestMutation.mutate(requestId, {
      onSuccess: (response) => {
        setAcceptedCodes((current) => ({ ...current, [response.requestId]: response.pairingCode }));
      },
    });
  }

  function activateLanConnection() {
    const preferred =
      bindAddressInput.trim() ||
      repairIp ||
      runtime?.detectedLanListenIps?.[0] ||
      "";
    const normalized = normalizeConfiguredLanBindInput(preferred);
    if (!normalized.ok || !normalized.ip) {
      return;
    }
    remoteTransportMutation.mutate({ enable: true, bindAddress: normalized.ip });
  }

  function repairLanAddress() {
    const target = repairIp || bindAddressInput.trim();
    repairBindMutation.mutate(target || null);
  }

  function hideTerminal(terminalId: string) {
    const next = Array.from(new Set([...hiddenTerminalIds, terminalId]));
    setHiddenTerminalIds(next);
    writeHiddenTerminalIds(next);
  }

  function showHiddenTerminals() {
    setHiddenTerminalIds([]);
    writeHiddenTerminalIds([]);
  }

  const hostLicense = licenseQuery.data;
  const hostLicenseId = hostLicense?.licenseId ?? "";
  const hostStoreId = hostLicense?.storeId ?? "";
  const hostCustomer = hostLicense?.customerName ?? "";
  const hostMachineCode = hostLicense?.machineCode ?? hostLicense?.machineFingerprint ?? "";
  const pendingTerminal = (status?.terminals ?? []).find(
    (terminal) => String(terminal.status ?? "").toLowerCase().includes("pending")
      || String(terminal.status ?? "").toLowerCase().includes("request"),
  );
  const requestBlock = [
    "Terminal License Request",
    "Store: " + (hostCustomer || "(set after Host license)"),
    "Store ID (internal): " + (hostStoreId || "(from Host license)"),
    "Host License ID (internal): " + (hostLicenseId || "(from Host license)"),
    "Host Machine Code: " + (hostMachineCode || "(from Host license)"),
    "Terminal name/location: " + (pendingTerminal?.terminalName || pendingTerminal?.terminalId || "(e.g. Counter 1)"),
    "Terminal Machine Code: (copy from terminal device activation screen)",
    "",
    "Salryn generates Machine Codes. LicensingOps signs Terminal licenses from the Host license.",
  ].join("\n");

  async function copyTerminalRequest() {
    try {
      await navigator.clipboard.writeText(requestBlock);
      setRequestCopied(true);
    } catch {
      setRequestCopied(false);
    }
  }

  return (
    <div className="space-y-6">
      <CommandBar
        title="Terminal Management"
        subtitle="Manage LAN connection, pairing requests, and terminal access."
        status={<StatusPill tone="success">Available on this host</StatusPill>}
        actions={
          <Button variant="secondary" onClick={refreshAll}>
            <RefreshCw aria-hidden="true" size={16} />
            Refresh
          </Button>
        }
      />

      <Alert tone="info" title="One Terminal license per device">
        Every client device must activate with its own signed Terminal license. Terminal users only send a Terminal Machine Code and a name/location — they do not need a Host License ID. Use the support request block below when issuing Terminal licenses in LicensingOps.
      </Alert>

      <Card>
        <CardHeader>
          <CardTitle>Terminal license request (admin / support)</CardTitle>
        </CardHeader>
        <CardBody className="space-y-3 text-sm text-slate-700">
          <p className="text-slate-600">
            Internal details for store admin or Kirjane Labs. Not shown on the terminal activation screen.
          </p>
          <dl className="grid gap-2 sm:grid-cols-2">
            <div>
              <dt className="text-xs text-slate-500">Store name</dt>
              <dd className="font-medium">{hostCustomer || "—"}</dd>
            </div>
            <div>
              <dt className="text-xs text-slate-500">Store ID (internal)</dt>
              <dd className="break-all font-mono text-xs">{hostStoreId || "—"}</dd>
            </div>
            <div>
              <dt className="text-xs text-slate-500">Host License ID (internal)</dt>
              <dd className="break-all font-mono text-xs">{hostLicenseId || "—"}</dd>
            </div>
            <div>
              <dt className="text-xs text-slate-500">Host Machine Code</dt>
              <dd className="break-all font-mono text-xs">{hostMachineCode || "—"}</dd>
            </div>
          </dl>
          <pre className="overflow-auto rounded-md border border-slate-200 bg-slate-50 p-3 text-xs text-slate-800 whitespace-pre-wrap">
            {requestBlock}
          </pre>
          <Button variant="secondary" size="sm" onClick={() => void copyTerminalRequest()}>
            {requestCopied ? "Copied request block" : "Copy Terminal License Request"}
          </Button>
        </CardBody>
      </Card>

      {statusQuery.isLoading ? <LoadingState label="Loading host LAN control..." /> : null}
      {statusQuery.isError ? <ErrorState message={(statusQuery.error as Error).message} /> : null}
      {actionError ? <ErrorState message={actionError} /> : null}

      {status ? (
        <>
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            <MetricCard
              title="LAN connection"
              value={readinessTitle(runtime?.readinessState ?? "Disabled")}
              tone={readinessTone(runtime?.readinessState ?? "Disabled")}
              badge={readinessBadge(runtime?.readinessState ?? "Disabled")}
              subtitle={runtime?.clientUrl || status.hostLocalUrl}
            />
            <MetricCard
              title="Remote POS runtime"
              value={status.remotePosRuntimeEnabled ? "Enabled" : "Disabled"}
              tone={status.remotePosRuntimeEnabled ? "success" : "neutral"}
              badge={status.remotePosRuntimeEnabled ? "Ready" : "Off"}
              subtitle="Host-controlled runtime state"
            />
            <MetricCard
              title="Pending requests"
              value={pendingRequestCount.toLocaleString()}
              tone={pendingRequestCount > 0 ? "warning" : "success"}
              badge={pendingRequestCount > 0 ? "Review" : "Clear"}
              subtitle="Pairing approval queue"
              emphasis={pendingRequestCount > 0}
            />
            <MetricCard
              title="Visible terminals"
              value={visibleTerminals.length.toLocaleString()}
              tone="info"
              badge="Host view"
              subtitle={capacitySummary}
            />
          </div>

          <LanConnectionCard
            runtime={runtime}
            hostLocalUrl={status.hostLocalUrl}
            hostLanUrl={status.hostLanUrl}
            configuredDisplay={status.configuredBindAddress}
            bindAddressInput={bindAddressInput}
            onBindAddressChange={setBindAddressInput}
            busy={busy}
            canCopyClientUrl={canCopyClientUrl}
            clientUrl={runtime?.clientUrl ?? ""}
            suggestedClientUrl={clientUrl}
            copied={copied}
            onCopyClientUrl={copyClientUrl}
            onActivate={activateLanConnection}
            onRepair={repairLanAddress}
            onDisableLan={() => remoteTransportMutation.mutate({ enable: false, bindAddress: null })}
            onToggleTroubleshooting={() => setShowTroubleshooting((value) => !value)}
            showTroubleshooting={showTroubleshooting}
            remotePosRuntimeEnabled={status.remotePosRuntimeEnabled}
            onEnableRemotePos={() => remotePosRuntimeMutation.mutate({ enable: true })}
            onDisableRemotePos={() => remotePosRuntimeMutation.mutate({ enable: false })}
            capacitySummary={capacitySummary}
            repairIp={repairIp}
          />

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
                Pairing sequence: (1) rotate join code, (2) client requests access, (3) accept here, (4) client
                enters one-time pairing code once, (5) terminal stays trusted across refresh. Revoke only when a
                device must re-pair.
              </p>
              <div className="mb-4">
                <Button variant="secondary" disabled={busy || !hostLanEntitled} onClick={() => rotateJoinCodeMutation.mutate()}>
                  Rotate/show host join code
                </Button>
                {!hostLanEntitled ? (
                  <p className="text-xs text-amber-800">Join codes are unavailable for the current license.</p>
                ) : null}
              </div>
              {rotateJoinCodeMutation.data ? (
                <div className="mb-4 rounded-lg border border-blue-200 bg-blue-50 px-4 py-3 text-sm text-blue-950">
                  <p className="font-medium">Host join code</p>
                  <p className="mt-1 font-mono text-lg">{rotateJoinCodeMutation.data.joinCode}</p>
                  <p className="mt-1">Expires {formatDate(rotateJoinCodeMutation.data.expiresAtUtc)}</p>
                </div>
              ) : null}
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
                        <StatusPill tone={request.pairingState === "Pending" ? "warning" : request.pairingState === "Accepted" ? "success" : "info"}>
                          {request.pairingState === "Pending"
                            ? "Pending approval"
                            : request.pairingState === "Accepted"
                              ? "Accepted — awaiting client activation"
                              : request.pairingState}
                        </StatusPill>
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
                          <Button size="sm" disabled={busy || !hostLanEntitled} onClick={() => acceptRequest(request.requestId)}>
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
              <div className="flex flex-wrap items-center justify-between gap-3">
                <CardTitle>Terminal connections</CardTitle>
                {hiddenTerminalIds.length > 0 ? (
                  <Button size="sm" variant="ghost" onClick={showHiddenTerminals}>
                    Show {hiddenTerminalIds.length} hidden row(s)
                  </Button>
                ) : null}
              </div>
            </CardHeader>
            <CardBody className="space-y-6">
              <p className="text-sm text-slate-600">{status.terminalVisibilityMessage}</p>
              <p className="text-sm font-medium text-slate-700">
                Safety availability: {limits?.remainingPairingCapacity ?? 0} paired devices / {limits?.remainingActiveSessionCapacity ?? 0} active sessions.
              </p>
              {(["active", "blocked", "retired", "host-local"] as TerminalConnectionGroup[]).map((group) => {
                const rows = groupedTerminals[group];
                if (!rows.length) return null;
                return (
                  <div key={group} className="space-y-3">
                    <h3 className="text-sm font-semibold text-slate-800">{terminalGroupTitle(group)}</h3>
                    {rows.map((terminal) => (
                      <TerminalControlRow
                        key={terminal.terminalId}
                        terminal={terminal}
                        busy={busy}
                        onDisable={() => disableTerminalMutation.mutate(terminal.terminalId)}
                        onRevoke={() => revokeTerminalMutation.mutate(terminal.terminalId)}
                        onClearSessions={() => clearTerminalSessionsMutation.mutate(terminal.terminalId)}
                        onHide={() => hideTerminal(terminal.terminalId)}
                      />
                    ))}
                  </div>
                );
              })}
              {!visibleTerminals.length ? (
                <p className="text-sm text-slate-500">No paired terminals are visible to this operator.</p>
              ) : null}
            </CardBody>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>Terminal help</CardTitle>
            </CardHeader>
            <CardBody className="space-y-3 text-sm text-slate-700">
              <p className="font-medium text-slate-900">If a phone is stuck after a new build or DB reset:</p>
              <ol className="list-decimal space-y-1 pl-5">
                <li>Open the host client URL on the phone (/remote-client).</li>
                <li>Tap <strong>Clear this device state</strong>.</li>
                <li>Request access again with a fresh host join code.</li>
              </ol>
              <p>
                Every client device must activate with its own signed Terminal license before pairing. Host license
                terminal-count fields do not grant or limit client licenses.
              </p>
              <p>
                Revoked or archived old rows are history. New pairing creates a <strong>new</strong> terminal — do not
                try to reactivate a revoked connection.
              </p>
              <Alert tone="info" title="Connection behavior">
                <ul className="list-disc space-y-1 pl-5">
                  <li>Host owns the database and approvals.</li>
                  <li>Client only holds terminal pairing plus an optional cashier session token.</li>
                  <li>Password is never stored on the client.</li>
                  <li>Host validates any saved token before resuming Remote POS.</li>
                  <li>Revoked means re-pair, not reactivate.</li>
                  <li>Disabled means blocked; restore is not supported in this build.</li>
                </ul>
              </Alert>
            </CardBody>
          </Card>

          <details className="rounded-lg border border-slate-200 bg-white px-5 py-4 text-sm">
            <summary className="cursor-pointer font-medium text-slate-900">Advanced diagnostics</summary>
            <div className="mt-4 space-y-4">
              <dl className="grid gap-3 md:grid-cols-2">
                <Row label="Bind mode" value={status.bindMode} />
                <Row label="Configured bind (display)" value={status.configuredBindAddress} />
                <Row label="Configured bind (raw)" value={runtime?.configuredBindAddressRaw ?? "-"} />
                <Row label="Configured bind (normalized)" value={runtime?.configuredBindAddressNormalized ?? "-"} />
                <Row label="Default bind mode" value={status.bindPolicy.defaultMode} />
                <Row label="Effective bind mode" value={status.bindPolicy.effectiveMode} />
                <Row label="Specific IP only" value={status.bindPolicy.specificIpOnly ? "Yes" : "No"} />
                <Row label="No wildcard bind" value={status.bindPolicy.noWildcardBind === false ? "No" : "Yes"} />
                <Row label="Remote transport" value={enabledLabel(status.remoteTransportEnabled)} />
                <Row label="Remote POS runtime" value={enabledLabel(status.remotePosRuntimeEnabled)} />
                <Row label="Capacity" value={capacitySummary} />
              </dl>
              <p className="text-slate-600">
                LAN server is actually listening on:{" "}
                <span className="font-mono text-slate-900">
                  {runtime?.actualListenUrls?.length ? runtime.actualListenUrls.join(", ") : "unknown"}
                </span>
              </p>
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

function LanConnectionCard({
  runtime,
  hostLocalUrl,
  hostLanUrl,
  configuredDisplay,
  bindAddressInput,
  onBindAddressChange,
  busy,
  canCopyClientUrl,
  clientUrl,
  suggestedClientUrl,
  copied,
  onCopyClientUrl,
  onActivate,
  onRepair,
  onDisableLan,
  onToggleTroubleshooting,
  showTroubleshooting,
  remotePosRuntimeEnabled,
  onEnableRemotePos,
  onDisableRemotePos,
  capacitySummary,
  repairIp,
}: {
  runtime: LocalLanControlRuntimeStatus | undefined;
  hostLocalUrl: string;
  hostLanUrl: string;
  configuredDisplay: string;
  bindAddressInput: string;
  onBindAddressChange: (value: string) => void;
  busy: boolean;
  canCopyClientUrl: boolean;
  clientUrl: string;
  suggestedClientUrl: string;
  copied: boolean;
  onCopyClientUrl: () => void;
  onActivate: () => void;
  onRepair: () => void;
  onDisableLan: () => void;
  onToggleTroubleshooting: () => void;
  showTroubleshooting: boolean;
  remotePosRuntimeEnabled: boolean;
  onEnableRemotePos: () => void;
  onDisableRemotePos: () => void;
  capacitySummary: string;
  repairIp: string;
}) {
  const readiness = runtime?.readinessState ?? "Disabled";
  const malformed = Boolean(runtime?.configuredBindMalformed);
  const activateNormalized = normalizeConfiguredLanBindInput(
    bindAddressInput.trim() || repairIp,
  );

  return (
    <Card>
      <CardHeader>
        <CardTitle>LAN Connection</CardTitle>
      </CardHeader>
      <CardBody className="space-y-4">
        <div className="flex flex-wrap items-center gap-2">
          <StatusPill tone={readinessTone(readiness)}>{readinessTitle(readiness)}</StatusPill>
          {runtime?.restartRequired ? <StatusPill tone="warning">Restart may be required</StatusPill> : null}
          {malformed ? <StatusPill tone="danger">Configured bind malformed</StatusPill> : null}
        </div>

        {readiness === "Ready" && canCopyClientUrl ? (
          <Alert tone="success" title="LAN is ready">
            Clients can open: <span className="font-mono">{clientUrl}</span>
          </Alert>
        ) : null}

        {malformed ? (
          <Alert tone="danger" title="Configured LAN address is malformed">
            Stored value looks like a URL or otherwise invalid bind. Repair to a bare IPv4
            {repairIp ? ` such as ${repairIp}` : ""}. Never paste a full URL into the bind field.
          </Alert>
        ) : null}

        {readiness !== "Ready" ? (
          <Alert tone={canCopyClientUrl ? "info" : "warning"} title={canCopyClientUrl ? "LAN status" : "LAN setup needed"}>
            {readinessGuidance(readiness)}
          </Alert>
        ) : null}

        <dl className="grid gap-3 text-sm sm:grid-cols-2">
          <Row label="Host local URL" value={hostLocalUrl} />
          <Row label="Host LAN URL" value={hostLanUrl} />
          <Row label="Configured bind (safe display)" value={configuredDisplay} />
          <Row label="Client URL ready" value={canCopyClientUrl ? "Yes" : "No"} />
          <Row label="Capacity" value={capacitySummary} />
        </dl>

        <div className="space-y-2">
          <label htmlFor="host-lan-control-bind-address" className="block text-sm font-medium text-slate-700">
            Explicit LAN IPv4 only (example: 192.168.1.161)
          </label>
          <p className="text-xs text-slate-500">Do not paste a full URL. Scheme and port are not allowed in this field.</p>
          <TextInput
            id="host-lan-control-bind-address"
            value={bindAddressInput}
            onChange={(event) => onBindAddressChange(event.target.value)}
            placeholder={repairIp || "192.168.1.161"}
            disabled={busy}
          />
          {bindAddressInput.trim() && !activateNormalized.ok ? (
            <p className="text-xs text-rose-600">{activateNormalized.error}</p>
          ) : null}
          {activateNormalized.ok && activateNormalized.wasRepaired ? (
            <p className="text-xs text-amber-700">Will save repaired bare IP: {activateNormalized.ip}</p>
          ) : null}
        </div>

        <div className="flex flex-wrap gap-2">
          <Button disabled={busy || !activateNormalized.ok} onClick={onActivate}>
            Activate LAN connection
          </Button>
          {malformed || runtime?.problem === "ConfiguredBindMalformed" || runtime?.problem === "BindIpNotOnHost" ? (
            <Button variant="secondary" disabled={busy || !repairIp} onClick={onRepair}>
              Repair LAN address{repairIp ? ` to ${repairIp}` : ""}
            </Button>
          ) : null}
          <Button variant="secondary" disabled={!canCopyClientUrl || busy} onClick={onCopyClientUrl}>
            Copy client URL
          </Button>
          <Button variant="secondary" disabled={busy} onClick={onDisableLan}>
            Disable LAN connection
          </Button>
          <Button variant="ghost" onClick={onToggleTroubleshooting}>
            {showTroubleshooting ? "Hide troubleshooting" : "Show troubleshooting"}
          </Button>
        </div>
        {copied ? <p className="text-xs text-slate-500">Client URL copied.</p> : null}
        {!canCopyClientUrl && suggestedClientUrl ? (
          <p className="text-xs text-slate-500">
            Suggested client URL after repair/restart: <span className="font-mono">{suggestedClientUrl}</span> (copy
            stays disabled until runtime reports Ready with a clean bind).
          </p>
        ) : null}

        <div className="flex flex-wrap gap-2 border-t border-slate-200 pt-4">
          <Button
            variant="secondary"
            disabled={busy || remotePosRuntimeEnabled}
            onClick={onEnableRemotePos}
          >
            Enable Remote POS runtime
          </Button>
          <Button
            variant="secondary"
            disabled={busy || !remotePosRuntimeEnabled}
            onClick={onDisableRemotePos}
          >
            Disable Remote POS runtime
          </Button>
        </div>

        {showTroubleshooting && runtime?.firewallGuidance ? (
          <Alert tone="info" title="Firewall guidance (manual only)">
            {runtime.firewallGuidance}
          </Alert>
        ) : null}
      </CardBody>
    </Card>
  );
}

function TerminalControlRow({
  terminal,
  busy,
  onDisable,
  onRevoke,
  onClearSessions,
  onHide,
}: {
  terminal: LocalLanControlTerminal;
  busy: boolean;
  onDisable: () => void;
  onRevoke: () => void;
  onClearSessions: () => void;
  onHide: () => void;
}) {
  const explanation = terminalLifecycleExplanation(terminal.status);
  const showDisable = canDisableTerminal(terminal.status, terminal.terminalRole);
  const showRevoke = canRevokeTerminal(terminal.status, terminal.terminalRole);
  const showRestore = canRestoreDisabledTerminal(terminal.status, terminal.terminalRole);
  const showClearSessions = canClearTerminalOperatorSessions(terminal.status, terminal.terminalRole);
  const showHide = canHideTerminalFromList(terminal.status, terminal.terminalRole);
  const readOnlyRevoked = isRevokedTerminalReadOnly(terminal.status);
  const hostLocal = isHostLocalTerminalRole(terminal.terminalRole);

  return (
    <div className="rounded-lg border border-slate-200 bg-white px-4 py-3 text-sm">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="space-y-1">
          <p className="font-medium text-slate-900">{terminal.terminalName}</p>
          <p className="text-slate-600">Type: {formatTerminalRoleLabel(terminal.terminalRole)}</p>
          <p className="text-slate-600">Status: {formatTerminalStatusLabel(terminal.status)}</p>
          <p className="text-slate-500">Last seen: {formatDate(terminal.lastSeenAtUtc)}</p>
          {terminal.revokedAtUtc ? (
            <p className="text-slate-500">Revoked: {formatDate(terminal.revokedAtUtc)}</p>
          ) : null}
          {terminal.disabledAtUtc ? (
            <p className="text-slate-500">Disabled: {formatDate(terminal.disabledAtUtc)}</p>
          ) : null}
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <StatusPill tone={terminalStatusTone(terminal.status)}>{formatTerminalStatusLabel(terminal.status)}</StatusPill>
          {showDisable ? (
            <Button size="sm" variant="secondary" disabled={busy} onClick={onDisable}>
              Disable
            </Button>
          ) : null}
          {showRevoke ? (
            <Button size="sm" variant="secondary" disabled={busy} onClick={onRevoke}>
              Revoke
            </Button>
          ) : null}
          {showClearSessions ? (
            <Button size="sm" variant="ghost" disabled={busy} onClick={onClearSessions}>
              Clear operator sessions
            </Button>
          ) : null}
          {showHide ? (
            <Button size="sm" variant="ghost" disabled={busy} onClick={onHide}>
              Hide from list
            </Button>
          ) : null}
          {showRestore ? (
            <Button size="sm" variant="secondary" disabled>
              Restore (not supported)
            </Button>
          ) : null}
        </div>
      </div>
      {hostLocal ? (
        <Alert tone="info" className="mt-3">
          Host-local admin/console row is read-only. It is not a Remote POS client connection.
        </Alert>
      ) : null}
      {readOnlyRevoked ? (
        <Alert tone="warning" className="mt-3">
          Revoked connection. Client must request access again. Hide removes it from this host list only (no hard
          delete of audit or sales history).
        </Alert>
      ) : null}
      {explanation && !readOnlyRevoked ? (
        <Alert tone="info" className="mt-3">
          {explanation}
        </Alert>
      ) : null}
      {terminal.status === "Disabled" ? (
        <Alert tone="info" className="mt-3">
          Re-enable is not supported in this build. Revoke if the device should re-pair, or hide this row from the list.
        </Alert>
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
