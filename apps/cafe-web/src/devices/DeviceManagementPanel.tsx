import { Bluetooth, KeyRound, RefreshCw, ShieldAlert, ShieldCheck } from "lucide-react";
import { useState } from "react";
import {
  Alert,
  Button,
  Card,
  CardBody,
  CardHeader,
  ConfirmDialog,
  DataTable,
  FormField,
  SelectInput,
  StatusBadge,
  TextInput,
  useToast,
  type StatusTone,
} from "@salryn/cafe-ui";
import type {
  CentralAdminSession,
  CentralDeviceRow,
  CentralHealthSnapshot,
  CentralInviteTicket,
  CentralOwnerApi,
  CentralQuarantineRow,
  DeploymentSurface,
  LanControlStatusSnapshot,
  LanPairingSnapshot,
} from "../backoffice/types";
import { HostApiError } from "../api/hostClient";
import { BackofficeUnknownOutcomeError, useBackofficeOperation } from "../backoffice/useBackofficeOperation";

const STATUS_TONE: Record<string, StatusTone> = {
  Active: "success",
  Pending: "warning",
  Revoked: "danger",
};

export type DeviceManagementPanelProps = {
  centralApi: CentralOwnerApi;
  surface: DeploymentSurface;
  centralOrigin: string;
  onCentralOriginChange: (origin: string) => void;
  centralHealth: CentralHealthSnapshot | null;
  centralAuth: CentralAdminSession | null;
  devices: CentralDeviceRow[];
  quarantine: CentralQuarantineRow[];
  lanStatus: LanControlStatusSnapshot | null;
  lanPairing: LanPairingSnapshot | null;
  onCentralLogin: (username: string, password: string) => Promise<void>;
  onCentralLogout: () => Promise<void>;
  onRefreshCentral: () => Promise<void>;
  onRotateLanJoinCode?: () => Promise<void>;
};

export function DeviceManagementPanel({
  centralApi,
  surface,
  centralOrigin,
  onCentralOriginChange,
  centralHealth,
  centralAuth,
  devices,
  quarantine,
  lanStatus,
  lanPairing,
  onCentralLogin,
  onCentralLogout,
  onRefreshCentral,
  onRotateLanJoinCode,
}: DeviceManagementPanelProps) {
  const { notify } = useToast();
  const [adminUser, setAdminUser] = useState("");
  const [adminPass, setAdminPass] = useState("");
  const [inviteRole, setInviteRole] = useState("OfflineAndroidCashier");
  const [joinTicket, setJoinTicket] = useState<CentralInviteTicket | null>(null);
  const [revokeTarget, setRevokeTarget] = useState<CentralDeviceRow | null>(null);
  const [pendingRevokeDeviceId, setPendingRevokeDeviceId] = useState<string | null>(null);
  const [authError, setAuthError] = useState<string | null>(null);

  const joinOp = useBackofficeOperation("join-code");
  const approveOp = useBackofficeOperation("dev-approve");
  const revokeOp = useBackofficeOperation("dev-revoke");
  const reviewOp = useBackofficeOperation("quarantine-review");

  async function handleCentralSignIn(e: React.FormEvent) {
    e.preventDefault();
    setAuthError(null);
    try {
      await onCentralLogin(adminUser, adminPass);
      setAdminPass("");
      notify("success", "Signed in to Central Owner Authority");
    } catch (error) {
      setAuthError(error instanceof Error ? error.message : "Central sign in failed.");
    }
  }

  async function handleIssueJoinCode() {
    if (!centralAuth) return;
    const ticket = await joinOp.execute(() =>
      centralApi.createInvite(centralOrigin, centralAuth.accessToken, inviteRole),
    );
    if (ticket) {
      setJoinTicket(ticket);
      notify("info", `Join code issued: ${ticket.joinCode}`, `Role: ${ticket.deviceRole}`);
    }
  }

  async function handleApprove(device: CentralDeviceRow) {
    if (!centralAuth) return;
    const updated = await approveOp.execute(
      () => centralApi.approveDevice(centralOrigin, centralAuth.accessToken, device.deviceId),
      device.deviceId,
    );
    if (updated) {
      notify("success", `Device approved · ${updated.deviceName}`);
      await onRefreshCentral();
    }
  }

  async function handleRevoke(deviceId: string) {
    if (!centralAuth) return;
    setPendingRevokeDeviceId(deviceId);
    const updated = await revokeOp.execute(
      async (operationId) => {
        try {
          return await centralApi.revokeDevice(centralOrigin, centralAuth.accessToken, deviceId);
        } catch (err) {
          if ((err instanceof HostApiError && (err.status === 0 || err.status >= 500 || err.code === "ERR_NETWORK_UNKNOWN")) || err instanceof TypeError || (err instanceof Error && err.name === "AbortError")) {
            throw new BackofficeUnknownOutcomeError(operationId, err instanceof Error ? err.message : "Network outcome unknown.");
          }
          throw err;
        }
      },
      deviceId,
    );
    if (updated) {
      setPendingRevokeDeviceId(null);
      notify("warning", `Device revoked · ${updated.deviceName}`, "Offline lease fenced; sync tokens invalidated.");
      await onRefreshCentral();
    }
  }

  async function handleReviewQuarantine(item: CentralQuarantineRow, status: "ApprovedForManualAudit" | "Discarded") {
    if (!centralAuth) return;
    const res = await reviewOp.execute(
      () =>
        centralApi.reviewQuarantine(
          centralOrigin,
          centralAuth.accessToken,
          item.quarantineId,
          status,
          `Reviewed (${status}) from Central Owner Console`,
        ),
      `${item.quarantineId}:${status}`,
    );
    if (res) {
      notify("info", `Quarantine ${status}`, item.operationType);
      await onRefreshCentral();
    }
  }

  return (
    <div style={{ display: "grid", gap: 16 }}>
      <Alert
        tone="info"
        title="Device Trust vs. Peripheral Separation (ADR-I4)"
        description={
          surface === "CENTRAL_OWNER"
            ? "Central Owner view manages cross-store device approvals, offline leases, and revocations over HTTPS. Local LAN port 5400 HTTPS & Windows host controls remain Host-only."
            : "Windows Host Console manages local LAN Thin Terminals (HTTPS port 5400) and synced Central device trust. Bluetooth receipt/kitchen printers are isolated local peripherals."
        }
      />

      {surface === "WINDOWS_HOST" ? (
        <Card>
          <CardHeader
            title="Windows Host LAN Thin-Client Pairing (HTTPS 5400)"
            subtitle={
              lanStatus
                ? `Transport: ${lanStatus.remoteTransportEnabled ? "Enabled" : "Disabled"} · Remote POS: ${lanStatus.remotePosRuntimeEnabled ? "Ready" : "Off"} · ${lanStatus.hostLanUrl}`
                : "Local LAN pairing controls"
            }
            action={
              onRotateLanJoinCode ? (
                <Button size="sm" variant="secondary" onClick={() => void onRotateLanJoinCode()}>
                  Rotate LAN Join Code
                </Button>
              ) : null
            }
          />
          <CardBody>
            {lanPairing ? (
              <div style={{ display: "grid", gap: 8 }}>
                <div>
                  <strong>Active LAN Join Code:</strong>{" "}
                  <span className="salryn-tabular">
                    {lanPairing.activeJoinCode ?? (lanPairing.joinCodeIssued ? "Issued (active)" : "None active")}
                  </span>
                </div>
                <div style={{ fontSize: "0.875rem", color: "var(--salryn-mocha)" }}>
                  Pending Join Requests: {lanPairing.pendingRequestCount} · {lanPairing.message}
                </div>
              </div>
            ) : (
              <div style={{ fontSize: "0.875rem", color: "var(--salryn-mocha)" }}>LAN pairing status unavailable.</div>
            )}
          </CardBody>
        </Card>
      ) : null}

      <Card>
        <CardHeader
          title="Central Server Connection & Admin Sign-In"
          subtitle="Operates directly against Central /api/sync/v1/* over HTTPS (independent of Windows localhost)."
          action={
            centralHealth ? (
              <StatusBadge tone="success">
                Central {centralHealth.productId} · Epoch {centralHealth.serverEpoch}
              </StatusBadge>
            ) : (
              <StatusBadge tone="neutral">Disconnected</StatusBadge>
            )
          }
        />
        <CardBody>
          <div style={{ display: "grid", gap: 12 }}>
            <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: 10, alignItems: "end" }}>
              <FormField label="Central HTTPS Server URL" hint="e.g. https://127.0.0.1:7443">
                <TextInput value={centralOrigin} onChange={(e) => onCentralOriginChange(e.target.value)} />
              </FormField>
              <div>
                <Button variant="secondary" onClick={() => void onRefreshCentral()}>
                  Check Central Health &amp; Refresh
                </Button>
              </div>
            </div>

            {authError ? <Alert tone="danger" title="Central Admin Sign-In Failed" description={authError} /> : null}

            {!centralAuth ? (
              <form
                onSubmit={(e) => void handleCentralSignIn(e)}
                style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))", gap: 10, alignItems: "end" }}
              >
                <FormField label="Central Admin Username" required>
                  <TextInput value={adminUser} onChange={(e) => setAdminUser(e.target.value)} required />
                </FormField>
                <FormField label="Central Admin Password" required>
                  <TextInput type="password" value={adminPass} onChange={(e) => setAdminPass(e.target.value)} required />
                </FormField>
                <div>
                  <Button type="submit" variant="primary">
                    Sign In to Central
                  </Button>
                </div>
              </form>
            ) : (
              <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center", justifyContent: "space-between", gap: 10 }}>
                <div>
                  Signed in as <strong>{centralAuth.session.displayName}</strong> ({centralAuth.session.role}) · Store{" "}
                  <span className="salryn-tabular">{centralAuth.session.storeId.slice(0, 8)}</span>
                </div>
                <Button size="sm" variant="ghost" onClick={() => void onCentralLogout()}>
                  Sign Out of Central
                </Button>
              </div>
            )}
          </div>
        </CardBody>
      </Card>

      {approveOp.error ? <Alert tone="danger" title="Device Approval Failed" description={approveOp.error} /> : null}
      {revokeOp.error ? <Alert tone="danger" title="Device Revocation Failed" description={revokeOp.error} /> : null}

      {revokeOp.unknownOutcome && pendingRevokeDeviceId ? (
        <Card>
          <CardBody>
            <Alert
              tone="warning"
              title={`Unknown Revocation Outcome · Operation ${revokeOp.unknownOutcome.operationId}`}
              description={revokeOp.unknownOutcome.message}
            />
            <div style={{ marginTop: 12 }}>
              <Button
                variant="accent"
                leftIcon={<RefreshCw size={16} />}
                loading={revokeOp.inFlight}
                onClick={() => void handleRevoke(pendingRevokeDeviceId)}
              >
                Retry &amp; Confirm Revocation
              </Button>
            </div>
          </CardBody>
        </Card>
      ) : null}

      {centralAuth ? (
        <>
          <Card>
            <CardHeader
              title="Central Device Enrollment, Approval & Trust"
              subtitle="Asymmetric hardware-protected keys bound to ProductId = Salryn.Cafe."
              action={
                <div style={{ display: "flex", flexWrap: "wrap", gap: 8, alignItems: "center" }}>
                  <StatusBadge tone="success">Central Live</StatusBadge>
                  <SelectInput aria-label="Invite Device Role" value={inviteRole} onChange={(e) => setInviteRole(e.target.value)}>
                    <option value="OfflineAndroidCashier">Offline Android Cashier</option>
                    <option value="LanThinTerminal">LAN Thin Terminal</option>
                    <option value="OwnerAdminClient">Owner Admin Client</option>
                    <option value="StandbyBackupNode">Standby Backup Node</option>
                  </SelectInput>
                  <Button
                    variant="primary"
                    leftIcon={<KeyRound size={16} />}
                    loading={joinOp.inFlight}
                    onClick={() => void handleIssueJoinCode()}
                  >
                    Issue Join Code
                  </Button>
                </div>
              }
            />
            <CardBody>
              <div style={{ display: "grid", gap: 14 }}>
                {joinTicket ? (
                  <Alert
                    tone="success"
                    title={`Active Join Code: ${joinTicket.joinCode}`}
                    description={`Role: ${joinTicket.deviceRole} · Expires: ${new Date(joinTicket.expiresAtUtc).toLocaleTimeString()}`}
                  />
                ) : null}

                <DataTable
                  caption="Enrolled Salryn Cafe Devices"
                  rows={devices}
                  getRowKey={(d) => d.deviceId}
                  columns={[
                    {
                      key: "name",
                      header: "Device",
                      render: (d) => (
                        <div>
                          <strong>{d.deviceName}</strong>
                          <div className="salryn-tabular" style={{ fontSize: "0.75rem", color: "var(--salryn-mocha)" }}>
                            {d.deviceId.slice(0, 8)} · {d.publicKeyThumbprint.slice(0, 16)}
                          </div>
                        </div>
                      ),
                    },
                    { key: "role", header: "Role", render: (d) => d.deviceRole },
                    {
                      key: "status",
                      header: "Trust Status",
                      render: (d) => <StatusBadge tone={STATUS_TONE[d.status] ?? "neutral"}>{d.status}</StatusBadge>,
                    },
                    {
                      key: "seen",
                      header: "Last Seen",
                      render: (d) => (d.lastSeenAtUtc ? new Date(d.lastSeenAtUtc).toLocaleString() : "Never"),
                    },
                    {
                      key: "actions",
                      header: "Actions",
                      render: (d) => (
                        <div style={{ display: "flex", flexWrap: "wrap", gap: 6 }}>
                          {d.status === "Pending" ? (
                            <Button
                              size="sm"
                              variant="accent"
                              leftIcon={<ShieldCheck size={14} />}
                              disabled={approveOp.inFlight}
                              onClick={() => void handleApprove(d)}
                            >
                              Approve
                            </Button>
                          ) : null}
                          {d.status !== "Revoked" ? (
                            <Button
                              size="sm"
                              variant="danger"
                              leftIcon={<ShieldAlert size={14} />}
                              disabled={revokeOp.inFlight}
                              onClick={() => setRevokeTarget(d)}
                            >
                              Revoke
                            </Button>
                          ) : null}
                        </div>
                      ),
                    },
                  ]}
                />

                <div style={{ display: "flex", alignItems: "center", gap: 8, fontSize: "0.8125rem", color: "var(--salryn-mocha)" }}>
                  <Bluetooth aria-hidden="true" size={15} />
                  <span>Bluetooth Receipt &amp; Kitchen Printers are paired locally per station and never share terminal trust keys.</span>
                </div>
              </div>
            </CardBody>
          </Card>

          <Card>
            <CardHeader
              title="Central Sync Quarantine Review"
              subtitle="Operations pushed by fenced or revoked terminals require administrator disposition."
            />
            <CardBody>
              <DataTable
                caption="Central Quarantine Queue"
                rows={quarantine}
                getRowKey={(q) => q.quarantineId}
                columns={[
                  { key: "op", header: "Operation", render: (q) => <strong>{q.operationType}</strong> },
                  { key: "id", header: "Operation ID", tabular: true, render: (q) => q.operationId.slice(0, 8) },
                  { key: "status", header: "Review Status", render: (q) => q.reviewStatus },
                  {
                    key: "actions",
                    header: "Actions",
                    render: (q) =>
                      q.reviewStatus === "AdminReviewRequired" ? (
                        <div style={{ display: "flex", gap: 6 }}>
                          <Button size="sm" variant="accent" onClick={() => void handleReviewQuarantine(q, "ApprovedForManualAudit")}>
                            Approve for Audit
                          </Button>
                          <Button size="sm" variant="danger" onClick={() => void handleReviewQuarantine(q, "Discarded")}>
                            Discard
                          </Button>
                        </div>
                      ) : (
                        <span>{q.reviewNote ?? q.reviewStatus}</span>
                      ),
                  },
                ]}
              />
            </CardBody>
          </Card>
        </>
      ) : null}

      <ConfirmDialog
        open={Boolean(revokeTarget)}
        title={revokeTarget ? `Revoke ${revokeTarget.deviceName}?` : "Revoke Device?"}
        description="Immediately revokes sync credentials and fences offline outbox replay on reconnect."
        confirmLabel="Revoke device"
        destructive
        onCancel={() => setRevokeTarget(null)}
        onConfirm={() => {
          if (!revokeTarget) return;
          const id = revokeTarget.deviceId;
          setRevokeTarget(null);
          void handleRevoke(id);
        }}
      />
    </div>
  );
}
