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
  StatusBadge,
  useToast,
  type StatusTone,
} from "@salryn/cafe-ui";
import type { BackofficeClient } from "../backoffice/mockBackofficeClient";
import {
  UX_C_DATA_SOURCE,
  type DeploymentSurface,
  type DeviceStatus,
  type EnrolledDevice,
  type JoinCodeTicket,
} from "../backoffice/types";
import { useBackofficeOperation } from "../backoffice/useBackofficeOperation";

const STATUS_TONE: Record<DeviceStatus, StatusTone> = {
  ACTIVE: "success",
  PENDING_APPROVAL: "warning",
  OFFLINE_LEASE_EXPIRED: "danger",
  REVOKED: "danger",
};

export type DeviceManagementPanelProps = {
  client: BackofficeClient;
  surface: DeploymentSurface;
  devices: EnrolledDevice[];
  onDeviceUpdated: (device: EnrolledDevice) => void;
};

export function DeviceManagementPanel({
  client,
  surface,
  devices,
  onDeviceUpdated,
}: DeviceManagementPanelProps) {
  const { notify } = useToast();
  const [joinTicket, setJoinTicket] = useState<JoinCodeTicket | null>(null);
  const [revokeTarget, setRevokeTarget] = useState<EnrolledDevice | null>(null);
  const [pendingRevokeDeviceId, setPendingRevokeDeviceId] = useState<string | null>(null);

  const joinOp = useBackofficeOperation("join-code");
  const approveOp = useBackofficeOperation("dev-approve");
  const revokeOp = useBackofficeOperation("dev-revoke");

  async function handleIssueJoinCode() {
    const ticket = await joinOp.execute((operationId) => client.issueJoinCode(operationId));
    if (ticket) {
      setJoinTicket(ticket);
      notify("info", `Join code issued: ${ticket.code}`, `Bound to ${ticket.productId} (${ticket.expiresInMinutes}m TTL).`);
    }
  }

  async function handleApprove(device: EnrolledDevice) {
    const updated = await approveOp.execute(
      (operationId) => client.approveDevice({ operationId, deviceId: device.deviceId }),
      device.deviceId,
    );
    if (updated) {
      onDeviceUpdated(updated);
      notify("success", `Device approved · ${updated.displayName}`);
    }
  }

  async function handleRevoke(deviceId: string) {
    setPendingRevokeDeviceId(deviceId);
    const updated = await revokeOp.execute(
      (operationId) => client.revokeDevice({ operationId, deviceId }),
      deviceId,
    );
    if (updated) {
      setPendingRevokeDeviceId(null);
      onDeviceUpdated(updated);
      notify("warning", `Device revoked · ${updated.displayName}`, "Offline lease fenced; sync tokens invalidated.");
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
            : "Windows Host Console manages local LAN Thin Terminals (HTTPS port 5400) and synced device registry. Bluetooth receipt/kitchen printers are isolated local peripherals."
        }
      />

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

      <Card>
        <CardHeader
          title="Device Enrollment, Approval & Health"
          subtitle="Hardware-key asymmetric pairing bound to ProductId = Salryn.Cafe."
          action={
            <div style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
              <StatusBadge tone="copper">{UX_C_DATA_SOURCE}</StatusBadge>
              <Button
                variant="primary"
                leftIcon={<KeyRound size={16} />}
                loading={joinOp.inFlight}
                onClick={() => void handleIssueJoinCode()}
              >
                Issue 10-Min Join Code
              </Button>
            </div>
          }
        />
        <CardBody>
          <div style={{ display: "grid", gap: 14 }}>
            {joinTicket ? (
              <Alert
                tone="success"
                title={`Active Join Code: ${joinTicket.code}`}
                description={`Product: ${joinTicket.productId} · Expires in ${joinTicket.expiresInMinutes} min · Operation ID: ${joinTicket.operationId}`}
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
                      <strong>{d.displayName}</strong>
                      <div style={{ fontSize: "0.75rem", color: "var(--salryn-mocha)" }}>
                        {d.deviceId} · {d.keyThumbprint}
                      </div>
                    </div>
                  ),
                },
                { key: "role", header: "Role", render: (d) => d.role.replace(/_/g, " ") },
                {
                  key: "status",
                  header: "Trust Status",
                  render: (d) => <StatusBadge tone={STATUS_TONE[d.status]}>{d.status.replace(/_/g, " ")}</StatusBadge>,
                },
                { key: "lease", header: "Offline Lease", render: (d) => d.offlineLeaseExpiresLabel },
                {
                  key: "sync",
                  header: "Sync Cursor / Outbox",
                  tabular: true,
                  render: (d) => `#${d.syncCursor} (${d.pendingOutboxCount} pending)`,
                },
                {
                  key: "actions",
                  header: "Actions",
                  render: (d) => (
                    <div style={{ display: "flex", flexWrap: "wrap", gap: 6 }}>
                      {d.status === "PENDING_APPROVAL" ? (
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
                      {d.status !== "REVOKED" ? (
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

      <ConfirmDialog
        open={Boolean(revokeTarget)}
        title={revokeTarget ? `Revoke ${revokeTarget.displayName}?` : "Revoke Device?"}
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
