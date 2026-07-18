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
  ConfirmDialog,
  ErrorState,
  LoadingState,
  MetricCard,
  StatusPill,
  TextInput,
  useNotifications,
} from "../components/ui";
import { fetchLicenseStatus } from "../api/license";
import { useLocalLanControl } from "../hooks/useLocalLanControl";
import type { LocalLanControlRuntimeStatus, LocalLanControlTerminal } from "../types/remoteReadOnlyClient";
import { looksLikeBareIpv4, normalizeConfiguredLanBindInput } from "../utils/lanBindNormalize";
import {
  canClearTerminalOperatorSessions,
  canDisableTerminal,
  canEnableDisabledTerminal,
  canHideTerminalFromList,
  canRemoveTerminal,
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
import { safeNotificationDescription } from "../utils/notificationError";

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
      return "LAN ready";
    case "LoopbackOnly":
      return "Restart required";
    case "BindIpNotOnHost":
      return "Address needs repair";
    case "Misconfigured":
      return "Address needs repair";
    case "Disabled":
      return "LAN not started";
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
      return "Enter this Host PC's LAN address, then start LAN access.";
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
  const { notify } = useNotifications();
  const {
    statusQuery,
    runtimeStatusQuery,
    pairingStatusQuery,
    remoteTransportMutation,
    repairBindMutation,
    remotePosRuntimeMutation,
    disableTerminalMutation,
    enableDisabledTerminalMutation,
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
  const [pendingDisableTerminal, setPendingDisableTerminal] = useState<LocalLanControlTerminal | null>(null);
  const [pendingEnableTerminal, setPendingEnableTerminal] = useState<LocalLanControlTerminal | null>(null);
  const [pendingRemoveTerminal, setPendingRemoveTerminal] = useState<LocalLanControlTerminal | null>(null);
  const [pendingClearSessionsTerminal, setPendingClearSessionsTerminal] = useState<LocalLanControlTerminal | null>(null);
  const [pendingStopLan, setPendingStopLan] = useState(false);
  const [pendingStopTerminalPos, setPendingStopTerminalPos] = useState(false);
  const [pendingRejectRequestId, setPendingRejectRequestId] = useState<string | null>(null);
  const [pendingCreateJoinCode, setPendingCreateJoinCode] = useState(false);
  const [pendingRepairLanAddress, setPendingRepairLanAddress] = useState(false);

  const status = statusQuery.data;
  const runtime = runtimeStatusQuery.data;
  const pairing = pairingStatusQuery.data;
  const limits = status?.remotePosLimits ?? null;
  const pendingRequests = pairing?.pendingRequests ?? [];
  const pendingRequestCount = pairing?.pendingRequestCount ?? pendingRequests.length;
  const hostLanEntitled = limits?.hostLanEntitled === true && (limits?.maxPairedTerminals ?? 0) > 0;
  const capacitySummary = `${limits?.currentPairedCount ?? 0} / ${limits?.maxPairedTerminals ?? 0} paired, ${limits?.currentActiveSessions ?? 0} / ${limits?.maxActiveSessions ?? 0} active`;
  const detectedHostIps = useMemo(
    () => Array.from(new Set((runtime?.detectedLanListenIps ?? []).filter(looksLikeBareIpv4))),
    [runtime?.detectedLanListenIps],
  );
  const recommendedHostIp = detectedHostIps[0] ?? "";

  const repairIp =
    runtime?.configuredBindAddressRepairSuggestion?.trim() ||
    runtime?.configuredBindAddressNormalized?.trim() ||
    recommendedHostIp ||
    "";

  const configuredHostIp = looksLikeBareIpv4(status?.configuredBindAddress)
    ? status?.configuredBindAddress.trim() ?? ""
    : "";
  const terminalIp = repairIp || configuredHostIp;
  const terminalLink =
    runtime?.clientUrlReady && runtime.clientUrl
      ? runtime.clientUrl
      : terminalIp && looksLikeBareIpv4(terminalIp) && runtime?.port
        ? `http://${terminalIp}:${runtime.port}/remote-client`
        : "";

  const canCopyTerminalLink = Boolean(runtime?.clientUrlReady && runtime.clientUrl);
  const busy =
    remoteTransportMutation.isPending ||
    repairBindMutation.isPending ||
    remotePosRuntimeMutation.isPending ||
    disableTerminalMutation.isPending ||
    enableDisabledTerminalMutation.isPending ||
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
    (enableDisabledTerminalMutation.error as Error | null)?.message ??
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

  async function copyTerminalLink() {
    if (!canCopyTerminalLink || !runtime?.clientUrl) return;
    setCopied(false);
    try {
      await navigator.clipboard.writeText(runtime.clientUrl);
      setCopied(true);
      notify.info("Terminal link copied", "Open it on the terminal device.");
    } catch (error) {
      notify.error("Terminal link was not copied", safeNotificationDescription(error, "Select the terminal link and copy it manually."));
    }
  }

  function acceptRequest(requestId: string) {
    acceptJoinRequestMutation.mutate(requestId, {
      onSuccess: (response) => {
        setAcceptedCodes((current) => ({ ...current, [response.requestId]: response.pairingCode }));
        notify.success("Pairing request accepted", "Enter the one-time pairing code on the terminal device.");
      },
      onError: (error) => notify.error("Pairing request was not accepted", safeNotificationDescription(error, "The request remains pending.")),
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
      notify.warning("LAN access is not ready", normalized.error || "Enter a valid Host PC LAN address.");
      return;
    }
    remoteTransportMutation.mutate(
      { enable: true, bindAddress: normalized.ip },
      {
        onSuccess: () => notify.success("LAN access started", "Terminal devices can use the terminal link."),
        onError: (error) => notify.error("LAN access was not started", safeNotificationDescription(error, "Review the Host PC LAN address and try again.")),
      },
    );
  }

  function repairLanAddress() {
    const target = repairIp || bindAddressInput.trim();
    repairBindMutation.mutate(target || null, {
      onSuccess: () => notify.success("LAN address repaired", "Use the updated terminal link on terminal devices."),
      onError: (error) => notify.error("LAN address was not repaired", safeNotificationDescription(error, "Review the detected Host PC address and try again.")),
    });
  }

  function requestRepairLanAddress() {
    const target = repairIp || bindAddressInput.trim();
    const currentRaw = runtime?.configuredBindAddressRaw?.trim() || status?.configuredBindAddress?.trim() || "";
    const current = normalizeConfiguredLanBindInput(currentRaw);
    if (currentRaw && (!current.ok || current.ip !== target)) {
      setPendingRepairLanAddress(true);
      return;
    }
    repairLanAddress();
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
  // Admin/support request block for license desk issuance (collapsed by default).
  const requestBlock = [
    "Salryn — Terminal License Request",
    "",
    "Terminal licenses are issued from the Host license context.",
    "Copy this request for the license desk.",
    "Send to Kirjane Labs / Salryn Infodesk (salryn.info@gmail.com) or your license desk.",
    "Each LAN terminal needs its own Terminal license.",
    "",
    "Store: " + (hostCustomer || "(set after Host license)"),
    "Store ID: " + (hostStoreId || "(from Host license)"),
    "Host License ID: " + (hostLicenseId || "(from Host license)"),
    "Host Machine Code: " + (hostMachineCode || "(from Host license)"),
    "Terminal name/location: " + (pendingTerminal?.terminalName || pendingTerminal?.terminalId || "(e.g. Counter 1)"),
    "Terminal Machine Code: (copy from that terminal device activation screen)",
    "",
    "Salryn generates Machine Codes. Kirjane Labs / Salryn Infodesk issues Terminal licenses from the Host license.",
  ].join("\n");

  async function copyTerminalRequest() {
    try {
      await navigator.clipboard.writeText(requestBlock);
      setRequestCopied(true);
      notify.info("Terminal license request copied", "Send it to the license desk or Kirjane Labs support.");
    } catch (error) {
      setRequestCopied(false);
      notify.error("License request was not copied", safeNotificationDescription(error, "Select the request details and copy them manually."));
    }
  }

  return (
    <div className="space-y-6">
      <CommandBar
        title="Terminal Management"
        subtitle="Connect terminal devices to this Host PC over the same local network."
        status={<StatusPill tone="success">Available on this host</StatusPill>}
        actions={
          <Button variant="secondary" onClick={refreshAll}>
            <RefreshCw aria-hidden="true" size={16} />
            Refresh
          </Button>
        }
      />

      {statusQuery.isLoading ? <LoadingState label="Loading host LAN control..." /> : null}
      {statusQuery.isError ? <ErrorState message={(statusQuery.error as Error).message} /> : null}
      {actionError ? <ErrorState message={actionError} /> : null}

      {status ? (
        <>
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            <MetricCard
              title="LAN status"
              value={readinessTitle(runtime?.readinessState ?? "Disabled")}
              tone={readinessTone(runtime?.readinessState ?? "Disabled")}
              badge={readinessBadge(runtime?.readinessState ?? "Disabled")}
              subtitle={terminalLink || "Enter a Host PC LAN address to create the terminal link."}
            />
            <MetricCard
              title="Terminal POS access"
              value={status.remotePosRuntimeEnabled ? "Allowed" : "Stopped"}
              tone={status.remotePosRuntimeEnabled ? "success" : "neutral"}
              badge={status.remotePosRuntimeEnabled ? "Ready" : "Off"}
              subtitle="Controls operator access on paired terminal devices"
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
              title="Terminal devices"
              value={visibleTerminals.length.toLocaleString()}
              tone="info"
              badge="This Host PC"
              subtitle={capacitySummary}
            />
          </div>

          <LanConnectionCard
            runtime={runtime}
            hostPcAddress={terminalIp}
            bindAddressInput={bindAddressInput}
            onBindAddressChange={setBindAddressInput}
            busy={busy}
            canCopyTerminalLink={canCopyTerminalLink}
            terminalLink={terminalLink}
            copied={copied}
            onCopyTerminalLink={() => void copyTerminalLink()}
            onActivate={activateLanConnection}
            onRepair={requestRepairLanAddress}
            onDisableLan={() => setPendingStopLan(true)}
            onToggleTroubleshooting={() => setShowTroubleshooting((value) => !value)}
            showTroubleshooting={showTroubleshooting}
            remotePosRuntimeEnabled={status.remotePosRuntimeEnabled}
            onEnableRemotePos={() => remotePosRuntimeMutation.mutate(
              { enable: true },
              {
                onSuccess: () => notify.success("Terminal POS access allowed", "Paired terminal devices can use POS."),
                onError: (error) => notify.error("Terminal POS access was not allowed", safeNotificationDescription(error, "Review LAN status and try again.")),
              },
            )}
            onDisableRemotePos={() => setPendingStopTerminalPos(true)}
            capacitySummary={capacitySummary}
            repairIp={repairIp}
            detectedHostIps={detectedHostIps}
            recommendedHostIp={recommendedHostIp}
          />

          <Alert tone="info" title="One Terminal license per terminal device">
            Each terminal device needs its own Terminal license. The terminal device only needs to send its Terminal
            Machine Code and a name/location.
          </Alert>

          <details className="rounded-lg border border-slate-200 bg-white px-5 py-4 text-sm">
            <summary className="cursor-pointer font-medium text-slate-900">License desk details</summary>
            <div className="mt-4 space-y-4 text-slate-700">
              <p className="text-slate-600">
                Send this support request to the license desk or Kirjane Labs support at salryn.info@gmail.com. The
                technical IDs below are support details, not operator tasks.
              </p>
              <dl className="grid gap-3 rounded-xl border border-slate-200 bg-slate-50/80 p-3 sm:grid-cols-2">
                <div>
                  <dt className="text-xs font-semibold uppercase tracking-wide text-slate-500">Store</dt>
                  <dd className="mt-0.5 font-medium text-navy-900">{hostCustomer || "—"}</dd>
                </div>
                <div>
                  <dt className="text-xs font-semibold uppercase tracking-wide text-slate-500">Store ID</dt>
                  <dd className="mt-0.5 break-all font-mono text-xs text-slate-800">{hostStoreId || "—"}</dd>
                </div>
                <div>
                  <dt className="text-xs font-semibold uppercase tracking-wide text-slate-500">Host License ID</dt>
                  <dd className="mt-0.5 break-all font-mono text-xs text-slate-800">{hostLicenseId || "—"}</dd>
                </div>
                <div>
                  <dt className="text-xs font-semibold uppercase tracking-wide text-slate-500">Host Machine Code</dt>
                  <dd className="mt-0.5 break-all font-mono text-xs text-slate-800">{hostMachineCode || "—"}</dd>
                </div>
              </dl>
              <div>
                <p className="mb-1 text-xs font-semibold uppercase tracking-wide text-slate-500">Copy for license desk / support</p>
                <pre className="overflow-auto rounded-xl border border-slate-200 bg-white p-3 text-xs leading-relaxed text-slate-800 whitespace-pre-wrap">
                  {requestBlock}
                </pre>
              </div>
              <Button variant="secondary" size="sm" onClick={() => void copyTerminalRequest()}>
                {requestCopied ? "Copied request block" : "Copy Terminal License Request"}
              </Button>
            </div>
          </details>

          <Card>
            <CardHeader>
              <div className="flex flex-wrap items-center justify-between gap-3">
                <CardTitle>Pair a terminal device</CardTitle>
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
                  Review the terminal device request here, then accept or reject it from this Host PC.
                </Alert>
              ) : null}
              {pairingStatusQuery.isLoading ? <LoadingState label="Loading pending device requests..." /> : null}
              {pairingStatusQuery.isError ? <ErrorState message={(pairingStatusQuery.error as Error).message} /> : null}
              <div className="my-4 text-sm text-slate-600">
                <p className="font-medium text-slate-800">Pair a terminal device:</p>
                <ol className="mt-2 list-decimal space-y-1 pl-5">
                  <li>Create a Host join code.</li>
                  <li>Open the terminal link on the terminal device.</li>
                  <li>The terminal requests access.</li>
                  <li>Approve the request on this Host PC.</li>
                  <li>Enter the one-time pairing code on the terminal device.</li>
                </ol>
              </div>
              <div className="mb-4">
                <Button variant="secondary" disabled={busy || !hostLanEntitled} onClick={() => setPendingCreateJoinCode(true)}>
                  Create Host join code
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
                          <p className="text-slate-600">Requested: {formatDate(request.requestedAtUtc)}</p>
                          <details className="mt-1 text-xs text-slate-500">
                            <summary className="cursor-pointer">Request details</summary>
                            <p className="mt-1">Observed origin/IP: {request.observedOrigin || "Unavailable"}</p>
                          </details>
                        </div>
                        <StatusPill tone={request.pairingState === "Pending" ? "warning" : request.pairingState === "Accepted" ? "success" : "info"}>
                          {request.pairingState === "Pending"
                            ? "Pending approval"
                            : request.pairingState === "Accepted"
                              ? "Accepted — awaiting terminal activation"
                              : request.pairingState}
                        </StatusPill>
                      </div>
                      {acceptedCodes[request.requestId] ? (
                        <div className="mt-3 rounded-lg border border-emerald-200 bg-emerald-50 px-3 py-2 text-emerald-950">
                          <p className="font-medium">One-time pairing code</p>
                          <p className="font-mono text-lg">{acceptedCodes[request.requestId]}</p>
                          <p className="mt-1">Enter this code on the terminal device.</p>
                        </div>
                      ) : null}
                      {request.pairingState === "Pending" ? (
                        <div className="mt-3 flex flex-wrap gap-2">
                          <Button size="sm" disabled={busy || !hostLanEntitled} onClick={() => acceptRequest(request.requestId)}>
                            Accept
                          </Button>
                          <Button size="sm" variant="secondary" disabled={busy} onClick={() => setPendingRejectRequestId(request.requestId)}>
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
                        onRequestDisable={() => setPendingDisableTerminal(terminal)}
                        onRequestEnable={() => setPendingEnableTerminal(terminal)}
                        onRequestRemove={() => setPendingRemoveTerminal(terminal)}
                        onRequestClearSessions={() => setPendingClearSessionsTerminal(terminal)}
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
                <li>Open the terminal link on the phone.</li>
                <li>Tap <strong>Clear this device state</strong>.</li>
                <li>Request access again with a fresh host join code.</li>
              </ol>
              <p>
                Every terminal device must activate with its own signed Terminal license before pairing. Host license
                terminal-count fields do not grant or limit Terminal licenses.
              </p>
              <p>
                Removed or archived old rows are history. New pairing creates a <strong>new</strong> terminal — do not
                try to reactivate a removed connection.
              </p>
              <Alert tone="info" title="Connection behavior">
                <ul className="list-disc space-y-1 pl-5">
                  <li>This Host PC owns the database and approvals.</li>
                  <li>The terminal device only holds pairing plus an optional operator session token.</li>
                  <li>Password is never stored on the terminal device.</li>
                  <li>This Host PC validates any saved token before resuming Terminal POS.</li>
                  <li>Revoked means re-pair — operator UI says Remove terminal; do not reactivate the same trust row.</li>
                  <li>Disable terminal blocks access; Enable terminal restores access when the terminal is only disabled (not removed).</li>
                  <li>Remove terminal is available only after Disable. It permanently ends Host trust for that device.</li>
                </ul>
              </Alert>
            </CardBody>
          </Card>

          <details className="rounded-lg border border-slate-200 bg-white px-5 py-4 text-sm">
            <summary className="cursor-pointer font-medium text-slate-900">Advanced diagnostics</summary>
            <div className="mt-4 space-y-4">
              <dl className="grid gap-3 md:grid-cols-2">
                <Row label="Host local URL" value={status.hostLocalUrl} />
                <Row label="Host LAN URL" value={status.hostLanUrl} />
                <Row label="Client URL ready" value={runtime?.clientUrlReady ? "Yes" : "No"} />
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

          <ConfirmDialog
            open={pendingStopLan}
            title="Stop LAN access?"
            description="Terminal devices will no longer be able to connect to this Host PC until LAN access is started again."
            confirmLabel="Stop LAN access"
            cancelLabel="Keep LAN access on"
            destructive
            onConfirm={() => {
              setPendingStopLan(false);
              remoteTransportMutation.mutate(
                { enable: false, bindAddress: null },
                {
                  onSuccess: () => notify.warning("LAN access stopped", "Terminal devices can no longer connect."),
                  onError: (error) => notify.error("LAN access was not stopped", safeNotificationDescription(error, "LAN access remains in its previous state.")),
                },
              );
            }}
            onCancel={() => setPendingStopLan(false)}
          />
          <ConfirmDialog
            open={pendingStopTerminalPos}
            title="Stop terminal POS access?"
            description="Paired terminal devices will remain registered, but operators cannot use Terminal POS until access is allowed again."
            confirmLabel="Stop terminal POS access"
            cancelLabel="Keep terminal POS access on"
            destructive
            onConfirm={() => {
              setPendingStopTerminalPos(false);
              remotePosRuntimeMutation.mutate(
                { enable: false },
                {
                  onSuccess: () => notify.warning("Terminal POS access stopped", "Paired terminals remain registered but cannot use POS."),
                  onError: (error) => notify.error("Terminal POS access was not stopped", safeNotificationDescription(error, "Terminal POS access remains in its previous state.")),
                },
              );
            }}
            onCancel={() => setPendingStopTerminalPos(false)}
          />
          <ConfirmDialog
            open={pendingRejectRequestId !== null}
            title="Reject pairing request?"
            description="This device request will be rejected. The terminal must request access again if needed."
            confirmLabel="Reject request"
            cancelLabel="Keep request pending"
            destructive
            onConfirm={() => {
              if (!pendingRejectRequestId) return;
              const requestId = pendingRejectRequestId;
              setPendingRejectRequestId(null);
              rejectJoinRequestMutation.mutate(requestId, {
                onSuccess: () => notify.info("Pairing request rejected", "The terminal device must request access again if needed."),
                onError: (error) => notify.error("Pairing request was not rejected", safeNotificationDescription(error, "The request remains pending.")),
              });
            }}
            onCancel={() => setPendingRejectRequestId(null)}
          />
          <ConfirmDialog
            open={pendingCreateJoinCode}
            title="Create a new join code?"
            description="Use this when pairing a terminal device. If an older join code exists, use the latest code shown here."
            confirmLabel="Create join code"
            cancelLabel="Cancel"
            onConfirm={() => {
              setPendingCreateJoinCode(false);
              rotateJoinCodeMutation.mutate(undefined, {
                onSuccess: () => notify.success("Host join code created", "Use the latest code shown when pairing a terminal device."),
                onError: (error) => notify.error("Join code was not created", safeNotificationDescription(error, "Check LAN access and try again.")),
              });
            }}
            onCancel={() => setPendingCreateJoinCode(false)}
          />
          <ConfirmDialog
            open={pendingRepairLanAddress}
            title="Repair LAN address?"
            description="Salryn will save the detected Host PC address. Terminal devices may need the updated terminal link."
            confirmLabel="Repair address"
            cancelLabel="Keep current address"
            onConfirm={() => {
              setPendingRepairLanAddress(false);
              repairLanAddress();
            }}
            onCancel={() => setPendingRepairLanAddress(false)}
          />
          <ConfirmDialog
            open={pendingDisableTerminal !== null}
            title="Disable terminal?"
            description="This blocks this terminal from using Terminal POS. The terminal can be enabled again later from this Host PC."
            confirmLabel="Disable terminal"
            cancelLabel="Keep active"
            onConfirm={() => {
              if (!pendingDisableTerminal) return;
              const id = pendingDisableTerminal.terminalId;
              setPendingDisableTerminal(null);
              disableTerminalMutation.mutate(id, {
                onSuccess: () => notify.warning("Terminal disabled", "This terminal cannot use POS until it is enabled again."),
                onError: (error) => notify.error("Terminal was not disabled", safeNotificationDescription(error, "The terminal remains in its previous state.")),
              });
            }}
            onCancel={() => setPendingDisableTerminal(null)}
          />
          <ConfirmDialog
            open={pendingEnableTerminal !== null}
            title="Enable terminal?"
            description="This restores Terminal POS access for this paired terminal without re-pairing. Only use this if the terminal was disabled, not removed."
            confirmLabel="Enable terminal"
            cancelLabel="Keep disabled"
            onConfirm={() => {
              if (!pendingEnableTerminal) return;
              const id = pendingEnableTerminal.terminalId;
              setPendingEnableTerminal(null);
              enableDisabledTerminalMutation.mutate(id, {
                onSuccess: () => notify.success("Terminal enabled", "This paired terminal can use POS again."),
                onError: (error) => notify.error("Terminal was not enabled", safeNotificationDescription(error, "The terminal remains disabled.")),
              });
            }}
            onCancel={() => setPendingEnableTerminal(null)}
          />
          <ConfirmDialog
            open={pendingRemoveTerminal !== null}
            title="Remove terminal?"
            description="This permanently removes this terminal trust from the Host. The device must request access again before it can be used."
            confirmLabel="Remove terminal"
            cancelLabel="Keep disabled"
            destructive
            onConfirm={() => {
              if (!pendingRemoveTerminal) return;
              const id = pendingRemoveTerminal.terminalId;
              setPendingRemoveTerminal(null);
              revokeTerminalMutation.mutate(id, {
                onSuccess: () => notify.success("Terminal removed", "This device must request access again."),
                onError: (error) => notify.error("Terminal was not removed", safeNotificationDescription(error, "The terminal remains registered.")),
              });
            }}
            onCancel={() => setPendingRemoveTerminal(null)}
          />
          <ConfirmDialog
            open={pendingClearSessionsTerminal !== null}
            title="Clear operator sessions?"
            description="This signs out active operator sessions on this terminal. The terminal remains paired."
            confirmLabel="Clear sessions"
            cancelLabel="Keep sessions"
            onConfirm={() => {
              if (!pendingClearSessionsTerminal) return;
              const id = pendingClearSessionsTerminal.terminalId;
              setPendingClearSessionsTerminal(null);
              clearTerminalSessionsMutation.mutate(id, {
                onSuccess: () => notify.info("Operator sessions cleared", "The terminal remains paired."),
                onError: (error) => notify.error("Operator sessions were not cleared", safeNotificationDescription(error, "The existing sessions remain unchanged.")),
              });
            }}
            onCancel={() => setPendingClearSessionsTerminal(null)}
          />
        </>
      ) : null}
    </div>
  );
}

function LanConnectionCard({
  runtime,
  hostPcAddress,
  bindAddressInput,
  onBindAddressChange,
  busy,
  canCopyTerminalLink,
  terminalLink,
  copied,
  onCopyTerminalLink,
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
  detectedHostIps,
  recommendedHostIp,
}: {
  runtime: LocalLanControlRuntimeStatus | undefined;
  hostPcAddress: string;
  bindAddressInput: string;
  onBindAddressChange: (value: string) => void;
  busy: boolean;
  canCopyTerminalLink: boolean;
  terminalLink: string;
  copied: boolean;
  onCopyTerminalLink: () => void;
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
  detectedHostIps: string[];
  recommendedHostIp: string;
}) {
  const readiness = runtime?.readinessState ?? "Disabled";
  const malformed = Boolean(runtime?.configuredBindMalformed);
  const activateNormalized = normalizeConfiguredLanBindInput(
    bindAddressInput.trim() || repairIp,
  );

  return (
    <Card>
      <CardHeader>
        <CardTitle>Connect terminals over LAN</CardTitle>
      </CardHeader>
      <CardBody className="space-y-4">
        <ol className="list-decimal space-y-2 pl-5 text-sm text-slate-700">
          <li>Enter this Host PC's LAN address. Example: 192.168.1.161.</li>
          <li>Start LAN access.</li>
          <li>Open the terminal link below on each terminal device.</li>
          <li>Pair the terminal from this Host PC.</li>
          <li>Each terminal device still needs its own Terminal license.</li>
        </ol>

        <div className="flex flex-wrap items-center gap-2">
          <StatusPill tone={readinessTone(readiness)}>{readinessTitle(readiness)}</StatusPill>
          {runtime?.restartRequired ? <StatusPill tone="warning">Restart required</StatusPill> : null}
          {malformed ? <StatusPill tone="danger">Saved address needs repair</StatusPill> : null}
        </div>

        {recommendedHostIp ? (
          <div className="rounded-lg border border-blue-200 bg-blue-50 p-3">
            <p className="text-xs font-semibold uppercase tracking-wide text-blue-700">Recommended Host PC address</p>
            <p className="mt-1 font-mono text-base font-semibold text-slate-950">{recommendedHostIp}</p>
          </div>
        ) : (
          <Alert tone="warning" title="Host PC address not detected">
            Ask support to check this Host PC’s network address.
          </Alert>
        )}

        {detectedHostIps.length > 1 ? (
          <div className="space-y-2 rounded-lg border border-slate-200 bg-slate-50 p-3">
            <p className="text-sm font-semibold text-slate-900">Detected addresses</p>
            <p className="text-xs text-slate-500">Choose the address used by devices on the same local network.</p>
            <div className="flex flex-wrap gap-2">
              {detectedHostIps.map((ip) => (
                <Button
                  key={ip}
                  type="button"
                  size="sm"
                  variant={bindAddressInput.trim() === ip ? "primary" : "secondary"}
                  onClick={() => onBindAddressChange(ip)}
                  disabled={busy}
                >
                  {ip}{ip === recommendedHostIp ? " (recommended)" : ""}
                </Button>
              ))}
            </div>
          </div>
        ) : null}

        {readiness === "Ready" && terminalLink ? (
          <Alert tone="success" title="Terminal devices can open this link">
            <p>Use this terminal link on other devices:</p>
            <p className="mt-1 break-all font-mono text-base font-semibold">{terminalLink}</p>
          </Alert>
        ) : null}

        {readiness !== "Ready" && terminalLink ? (
          <Alert tone="warning" title="After starting LAN access, open this link on the terminal device">
            <p>Use this terminal link on other devices:</p>
            <p className="mt-1 break-all font-mono text-base font-semibold">{terminalLink}</p>
          </Alert>
        ) : null}

        {malformed ? (
          <Alert tone="danger" title="Saved Host PC address needs repair">
            The saved value is not a valid Host PC LAN address. Repair it to an IPv4 address
            {repairIp ? ` such as ${repairIp}` : ""}.
          </Alert>
        ) : null}

        {readiness !== "Ready" && !terminalLink ? (
          <Alert tone="warning" title="LAN setup needed">
            {readinessGuidance(readiness)}
          </Alert>
        ) : null}

        <dl className="grid gap-3 text-sm sm:grid-cols-2">
          <Row label="Host PC address" value={hostPcAddress || "Not set"} />
          <Row label="Terminal link" value={terminalLink || "Enter a valid address to create the link"} />
          <Row label="Status" value={readinessTitle(readiness)} />
          <Row label="Capacity" value={capacitySummary} />
        </dl>

        <div className="space-y-2">
          <label htmlFor="host-lan-control-bind-address" className="block text-sm font-medium text-slate-700">
            Host PC LAN address
          </label>
          <p className="text-xs text-slate-500">Use a detected address above, or enter the IPv4 address used by devices on the same network.</p>
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
            <p className="text-xs text-amber-700">Salryn will save the Host PC address as {activateNormalized.ip}.</p>
          ) : null}
        </div>

        <div className="flex flex-wrap gap-2">
          <Button disabled={busy || !activateNormalized.ok} onClick={onActivate}>
            Start LAN access
          </Button>
          {malformed || runtime?.problem === "ConfiguredBindMalformed" || runtime?.problem === "BindIpNotOnHost" ? (
            <Button variant="secondary" disabled={busy || !repairIp} onClick={onRepair}>
              Repair LAN address{repairIp ? ` to ${repairIp}` : ""}
            </Button>
          ) : null}
          <Button variant="secondary" disabled={!canCopyTerminalLink || busy} onClick={onCopyTerminalLink}>
            Copy terminal link
          </Button>
          <Button variant="secondary" disabled={busy || !runtime?.lanTransportEnabled} onClick={onDisableLan}>
            Stop LAN access
          </Button>
          <Button variant="ghost" onClick={onToggleTroubleshooting}>
            {showTroubleshooting ? "Hide connection help" : "Show connection help"}
          </Button>
        </div>
        {copied ? <p className="text-xs text-slate-500">Terminal link copied.</p> : null}
        {!canCopyTerminalLink && terminalLink ? (
          <p className="text-xs text-slate-500">
            Copy becomes available after LAN access is ready. The terminal link above is the link to use after
            starting or restarting Salryn.
          </p>
        ) : null}

        <div className="space-y-3 border-t border-slate-200 pt-4">
          <div>
            <p className="text-sm font-medium text-slate-900">Terminal POS access</p>
            <p className="text-xs text-slate-500">Allow or stop operator use on paired terminal devices.</p>
          </div>
          <div className="flex flex-wrap gap-2">
            <Button
              variant="secondary"
              disabled={busy || remotePosRuntimeEnabled}
              onClick={onEnableRemotePos}
            >
              Allow terminal POS access
            </Button>
            <Button
              variant="secondary"
              disabled={busy || !remotePosRuntimeEnabled}
              onClick={onDisableRemotePos}
            >
              Stop terminal POS access
            </Button>
          </div>
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
  onRequestDisable,
  onRequestEnable,
  onRequestRemove,
  onRequestClearSessions,
  onHide,
}: {
  terminal: LocalLanControlTerminal;
  busy: boolean;
  onRequestDisable: () => void;
  onRequestEnable: () => void;
  onRequestRemove: () => void;
  onRequestClearSessions: () => void;
  onHide: () => void;
}) {
  const explanation = terminalLifecycleExplanation(terminal.status);
  const showDisable = canDisableTerminal(terminal.status, terminal.terminalRole);
  const showEnable = canEnableDisabledTerminal(terminal.status, terminal.terminalRole);
  // canRevokeTerminal is an alias of canRemoveTerminal (permanent trust removal via revoke API).
  const showRemove = canRemoveTerminal(terminal.status, terminal.terminalRole) || canRevokeTerminal(terminal.status, terminal.terminalRole);
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
            <p className="text-slate-500">Removed: {formatDate(terminal.revokedAtUtc)}</p>
          ) : null}
          {terminal.disabledAtUtc ? (
            <p className="text-slate-500">Disabled: {formatDate(terminal.disabledAtUtc)}</p>
          ) : null}
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <StatusPill tone={terminalStatusTone(terminal.status)}>{formatTerminalStatusLabel(terminal.status)}</StatusPill>
          {showDisable ? (
            <Button size="sm" variant="secondary" disabled={busy} onClick={onRequestDisable}>
              Disable terminal
            </Button>
          ) : null}
          {showEnable ? (
            <Button size="sm" variant="secondary" disabled={busy} onClick={onRequestEnable}>
              Enable terminal
            </Button>
          ) : null}
          {showRemove ? (
            <Button size="sm" variant="danger" disabled={busy} onClick={onRequestRemove}>
              Remove terminal
            </Button>
          ) : null}
          {showClearSessions ? (
            <Button size="sm" variant="ghost" disabled={busy} onClick={onRequestClearSessions}>
              Clear operator sessions
            </Button>
          ) : null}
          {showHide ? (
            <Button size="sm" variant="ghost" disabled={busy} onClick={onHide}>
              Hide from list
            </Button>
          ) : null}
        </div>
      </div>
      {hostLocal ? (
        <Alert tone="info" className="mt-3">
          This Host PC/admin connection is read-only here. It is not a terminal device connection.
        </Alert>
      ) : null}
      {readOnlyRevoked ? (
        <Alert tone="warning" className="mt-3">
          Removed connection. The terminal device must request access again. Hide removes it from this Host PC list only (no hard
          delete of audit or sales history).
        </Alert>
      ) : null}
      {explanation && !readOnlyRevoked ? (
        <Alert tone="info" className="mt-3">
          {explanation}
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
