import { useState } from "react";
import {
  Alert,
  Button,
  Card,
  CardBody,
  CardHeader,
  CardTitle,
  CommandBar,
  DataTable,
  FormField,
  PasswordInput,
  StatusPill,
  TextInput,
  type DataTableColumn,
} from "../components/ui";
import { remotePosClientWarnings } from "../api/remotePosClient";
import { useRemotePosClient } from "../hooks/useRemotePosClient";
import type { RemotePosCartLine } from "../types/remotePosClient";

// LAN-M16-QA-B: Remote POS field UX. Pairing approves the device; login approves the person.
// The normal path never asks the operator to type terminal identity or handshake proof manually.

function formatCents(cents: number | null | undefined): string {
  if (cents === null || cents === undefined) return "-";
  return (cents / 100).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function formatQuantity(quantityX1000: number): string {
  return (quantityX1000 / 1000).toLocaleString(undefined, { maximumFractionDigits: 3 });
}

function formatExpiry(iso: string | null): string {
  if (!iso) return "-";
  const parsed = new Date(iso);
  return Number.isNaN(parsed.getTime()) ? iso : parsed.toLocaleString();
}

function checkoutTone(status: string): "success" | "warning" | "danger" | "info" {
  switch (status) {
    case "Completed":
    case "DuplicateRequest":
      return "success";
    case "StockChanged":
    case "ProductUnavailable":
    case "PriceChanged":
    case "OpenShiftRequired":
    case "PaymentInsufficient":
      return "warning";
    case "SessionExpired":
    case "TerminalRevoked":
    case "PermissionDenied":
    case "ValidationFailed":
      return "danger";
    default:
      return "info";
  }
}

export function RemotePosClientPage() {
  const client = useRemotePosClient();
  const activePairingRequest = client.pairingStatus ?? client.pairingRequest;

  const [joinCode, setJoinCode] = useState("");
  const [deviceName, setDeviceName] = useState("");
  const [pairingCode, setPairingCode] = useState("");
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [rememberOperatorSession, setRememberOperatorSession] = useState(true);
  const [openingCash, setOpeningCash] = useState("0.00");
  const [productId, setProductId] = useState("");
  const [quantity, setQuantity] = useState("1");
  const [tenderAmount, setTenderAmount] = useState("");

  const subtotalCents = client.cart?.subtotalCents ?? 0;

  const cartColumns: DataTableColumn<RemotePosCartLine>[] = [
    { key: "product", header: "Product", render: (line) => line.productName },
    { key: "qty", header: "Qty", render: (line) => formatQuantity(line.quantityX1000) },
    { key: "unit", header: "Unit", render: (line) => line.unitName },
    { key: "price", header: "Unit price", render: (line) => formatCents(line.unitPriceCents) },
    { key: "total", header: "Line total", render: (line) => formatCents(line.lineTotalCents) },
    {
      key: "actions",
      header: "",
      render: (line) => (
        <Button variant="ghost" size="sm" onClick={() => void client.removeLine(line.productId)} disabled={client.busy !== "idle"}>
          Remove
        </Button>
      ),
    },
  ];

  function handleJoinRequest() {
    void client.requestJoin(joinCode.trim(), deviceName.trim());
  }

  function handleActivatePairing() {
    void client.activatePairing(pairingCode.trim());
  }

  function handleLogin() {
    void client.login(username.trim(), password, rememberOperatorSession);
  }

  function handleAddLine() {
    const qtyX1000 = Math.round(Number.parseFloat(quantity || "0") * 1000);
    if (!productId.trim() || !Number.isFinite(qtyX1000) || qtyX1000 <= 0) {
      return;
    }
    void client.addLine(productId.trim(), qtyX1000);
    setProductId("");
    setQuantity("1");
  }

  function handleOpenShift() {
    const cents = Math.round(Number.parseFloat(openingCash || "0") * 100);
    void client.openShift(Number.isFinite(cents) && cents > 0 ? cents : 0);
  }

  function handleSubmitCheckout() {
    const cents = Math.round(Number.parseFloat(tenderAmount || "0") * 100);
    const payments = Number.isFinite(cents) && cents > 0 ? [{ method: "Cash", amountCents: cents }] : [];
    void client.submitCheckout(payments);
  }

  return (
    <div className="space-y-6">
      <CommandBar
        title="Remote POS client"
        subtitle="Connect to the POS host over its explicit LAN address. Pair this device first, then sign in as Cashier or Manager."
      />

      <Alert tone="warning" title="This device is a Remote POS client">
        <ul className="list-disc space-y-1 pl-5">
          {remotePosClientWarnings.map((warning) => (
            <li key={warning}>{warning}</li>
          ))}
        </ul>
      </Alert>

      {client.errorMessage ? (
        <Alert tone="danger" title="Remote POS request failed">
          {client.errorMessage}
        </Alert>
      ) : null}

      {client.statusMessage ? <Alert tone="info">{client.statusMessage}</Alert> : null}

      {client.hostAddressConfirmed ? (
        <Card>
          <CardBody className="text-xs text-slate-500">
            <p>
              Flow: <span className="font-mono text-slate-700">{client.flowState}</span>
              {" · "}
              Storage restored: <span className="font-mono text-slate-700">{client.storageRestored ? "yes" : "no"}</span>
              {" · "}
              Has stored pairing: <span className="font-mono text-slate-700">{client.hasStoredPairing ? "yes" : "no"}</span>
              {" · "}
              Has terminal id: <span className="font-mono text-slate-700">{client.hasTerminalId ? "yes" : "no"}</span>
              {" · "}
              Trust probe: <span className="font-mono text-slate-700">{client.trustProbeResult ?? "none"}</span>
              {" · "}
              Has operator token: <span className="font-mono text-slate-700">{client.hasOperatorSessionToken ? "yes" : "no"}</span>
              {" · "}
              Operator session valid: <span className="font-mono text-slate-700">{client.operatorSessionValid ?? "unknown"}</span>
              {" · "}
              Operator session storage: <span className="font-mono text-slate-700">{client.operatorSessionStorage}</span>
              {client.terminalIdSuffix ? (
                <>
                  {" · "}
                  Terminal: <span className="font-mono text-slate-700">…{client.terminalIdSuffix}</span>
                </>
              ) : null}
              {" · "}
              Host: <span className="font-mono text-slate-700">{client.hostBaseUrl.trim() || "-"}</span>
            </p>
          </CardBody>
        </Card>
      ) : null}

      {client.flowState === "S_REVOKED" ? (
        <Alert tone="warning" title="Pairing revoked or expired">
          {client.lastPairingNotice ?? "Pairing revoked or expired. Re-pair this device."}
          <div className="mt-3">
            <Button variant="secondary" onClick={client.startOver} disabled={client.busy !== "idle"}>
              Start over / request access again
            </Button>
          </div>
        </Alert>
      ) : null}

      {client.flowState === "S_OFFLINE" ? (
        <Alert tone="warning" title="Host unavailable">
          Host unavailable. Saved pairing kept.
        </Alert>
      ) : null}

      {client.flowState === "S_RESTORING" ? (
        <Alert tone="info">Restoring saved terminal state from this device...</Alert>
      ) : null}

      {!client.hostAddressConfirmed ? (
        <Card>
          <CardHeader>
            <CardTitle>State A — Host address</CardTitle>
          </CardHeader>
          <CardBody className="space-y-4">
            <p className="text-sm text-slate-500">
              Use the host LAN address shown on the host. Do not use localhost from another device.
            </p>
            <FormField label="POS host LAN address" htmlFor="rpos-host" hint="Example: http://192.168.1.10:5005">
              <TextInput
                id="rpos-host"
                value={client.hostBaseUrl}
                onChange={(event) => client.setHostBaseUrl(event.target.value)}
                placeholder="http://192.168.1.10:5005"
                autoComplete="off"
              />
            </FormField>
            <Button onClick={client.confirmHostAddress} disabled={!client.hostBaseUrl.trim()}>
              Continue to join host
            </Button>
          </CardBody>
        </Card>
      ) : null}

      {client.hostAddressConfirmed && client.flowState === "S1_UNPAIRED" ? (
        <Card>
          <CardHeader>
            <CardTitle>Request access to this host</CardTitle>
          </CardHeader>
          <CardBody className="space-y-4">
            {client.lastPairingNotice ? (
              <Alert tone="warning" title="Previous pairing is no longer active">
                {client.lastPairingNotice}
              </Alert>
            ) : null}
            <p className="text-sm text-slate-500">
              Ask the host operator for the join code. This only opens a pending request; it does not pair or log in this device.
            </p>
            <div className="grid gap-4 sm:grid-cols-2">
              <FormField label="Host join code" htmlFor="rpos-join-code">
                <TextInput id="rpos-join-code" value={joinCode} onChange={(event) => setJoinCode(event.target.value)} autoComplete="off" />
              </FormField>
              <FormField label="Device display name" htmlFor="rpos-device-name" hint="Display-only. The host never trusts this as identity.">
                <TextInput id="rpos-device-name" value={deviceName} onChange={(event) => setDeviceName(event.target.value)} autoComplete="off" maxLength={40} />
              </FormField>
            </div>
            <Button onClick={handleJoinRequest} disabled={client.busy === "pairing" || !joinCode.trim() || !deviceName.trim()}>
              {client.busy === "pairing" ? "Requesting..." : "Request access to this host"}
            </Button>
          </CardBody>
        </Card>
      ) : null}

      {client.flowState === "S2_REQUEST_PENDING" ? (
        <Card>
          <CardHeader>
            <CardTitle>Waiting for host approval</CardTitle>
          </CardHeader>
          <CardBody className="space-y-4">
            <div className="flex flex-wrap items-center gap-2">
              <StatusPill tone={client.pairingStatus?.pairingCodeRequired ? "success" : "warning"}>
                {activePairingRequest?.pairingState ?? "Pending"}
              </StatusPill>
              <StatusPill tone="info">{activePairingRequest?.deviceName ?? "Remote POS client"}</StatusPill>
            </div>
            <p className="text-sm text-slate-500">
              {client.pairingStatus?.message ?? "Waiting for host approval. Ask the host operator to accept this device."}
            </p>
            <div className="flex flex-wrap gap-2">
              <Button variant="secondary" onClick={() => void client.refreshPairingStatus()} disabled={client.busy === "pairing"}>
                Refresh approval status
              </Button>
              <Button variant="ghost" onClick={client.clearPairing}>
                Cancel / reset
              </Button>
            </div>
          </CardBody>
        </Card>
      ) : null}

      {client.flowState === "S3_HOST_APPROVED_NEEDS_ACTIVATION" ? (
        <Card>
          <CardHeader>
            <CardTitle>Host approved this device</CardTitle>
          </CardHeader>
          <CardBody className="space-y-4">
            <p className="text-sm text-slate-500">
              Enter the one-time pairing code from the host. After activation, this device stays paired across refresh.
            </p>
            <FormField label="Pairing code" htmlFor="rpos-pairing-code" hint="One-time code shown by the host after approval">
              <TextInput id="rpos-pairing-code" value={pairingCode} onChange={(event) => setPairingCode(event.target.value)} autoComplete="off" />
            </FormField>
            <div className="flex flex-wrap gap-2">
              <Button onClick={handleActivatePairing} disabled={client.busy === "pairing" || !pairingCode.trim()}>
                {client.busy === "pairing" ? "Activating..." : "Activate pairing"}
              </Button>
              <Button variant="ghost" onClick={client.clearPairing}>
                Cancel / reset
              </Button>
            </div>
          </CardBody>
        </Card>
      ) : null}

      {(client.flowState === "S4_TERMINAL_TRUSTED" || client.flowState === "S5_REMOTE_POS_OPERATOR_AUTH") && !client.isConnected ? (
        <Card>
          <CardHeader>
            <CardTitle>This terminal is paired — Cashier / Manager login</CardTitle>
          </CardHeader>
          <CardBody className="space-y-4">
            {client.pairingRestored ? (
              <Alert tone="success" title="Paired terminal restored">
                This device restored its trusted terminal pairing. Sign in as Cashier or Manager to continue.
              </Alert>
            ) : (
              <Alert tone="success" title="This terminal is paired">
                Pairing is complete for this device. Sign in as Cashier or Manager to use Remote POS.
              </Alert>
            )}
            <div className="flex flex-wrap items-center gap-2 text-sm text-slate-600">
              <StatusPill tone="success">Trusted terminal</StatusPill>
              {client.pairingStatus?.deviceName ? (
                <StatusPill tone="info">{client.pairingStatus.deviceName}</StatusPill>
              ) : null}
              {client.terminalIdSuffix ? (
                <span className="font-mono text-xs text-slate-500">Terminal …{client.terminalIdSuffix}</span>
              ) : null}
            </div>
            <Alert tone="info">
              After pairing, sign in as Cashier or Manager. Admin and Owner accounts are host-local only for this build.
            </Alert>
            <div className="grid gap-4 sm:grid-cols-2">
              <FormField label="Username" htmlFor="rpos-user">
                <TextInput id="rpos-user" value={username} onChange={(event) => setUsername(event.target.value)} autoComplete="off" />
              </FormField>
              <FormField label="Password" htmlFor="rpos-pass">
                <PasswordInput id="rpos-pass" value={password} onChange={(event) => setPassword(event.target.value)} autoComplete="off" />
              </FormField>
            </div>
            <label className="flex items-start gap-3 text-sm text-slate-700">
              <input
                type="checkbox"
                className="mt-1"
                checked={rememberOperatorSession}
                onChange={(event) => setRememberOperatorSession(event.target.checked)}
              />
              <span>
                Keep this cashier signed in on this paired terminal until the session expires
              </span>
            </label>
            <p className="text-xs text-slate-500">
              Passwords are never stored on this device. Only the host-issued session token is saved when this option is enabled.
            </p>
            <div className="flex flex-wrap gap-2">
              <Button onClick={handleLogin} disabled={client.busy === "login" || !username.trim()}>
                {client.busy === "login" ? "Signing in..." : "Sign in to host"}
              </Button>
              <Button variant="ghost" onClick={client.clearPairing} disabled={client.busy !== "idle"}>
                Clear this device pairing
              </Button>
            </div>
          </CardBody>
        </Card>
      ) : null}

      {client.isConnected ? (
        <>
          <Card>
            <CardHeader>
              <CardTitle>State F — Remote POS</CardTitle>
            </CardHeader>
            <CardBody className="space-y-3">
              {client.operatorSessionRestored ? (
                <Alert tone="success" title="Operator session restored">
                  Operator session restored. Signed in as {client.session?.displayName ?? client.session?.username ?? "operator"}.
                </Alert>
              ) : null}
              <div className="flex flex-wrap items-center gap-2">
                <StatusPill tone="primary">Remote POS client</StatusPill>
                <StatusPill tone="info">Role: {client.session?.roleName ?? "-"}</StatusPill>
                <StatusPill tone={client.session?.remotePosRuntimeEnabled ? "success" : "warning"}>
                  Host runtime {client.session?.remotePosRuntimeEnabled ? "enabled" : "disabled"}
                </StatusPill>
              </div>
              <dl className="grid gap-3 text-sm sm:grid-cols-2 lg:grid-cols-3">
                <div>
                  <dt className="text-slate-400">Connected operator</dt>
                  <dd className="text-slate-800">{client.session?.displayName ?? client.session?.username ?? "-"}</dd>
                </div>
                <div>
                  <dt className="text-slate-400">Paired terminal</dt>
                  <dd className="font-mono text-xs text-slate-800">{client.session?.terminalId ?? "-"}</dd>
                </div>
                <div>
                  <dt className="text-slate-400">Session expires</dt>
                  <dd className="text-slate-800">{formatExpiry(client.session?.expiresAtUtc ?? null)}</dd>
                </div>
                <div>
                  <dt className="text-slate-400">Host LAN address</dt>
                  <dd className="text-slate-800">{client.hostBaseUrl.trim()}</dd>
                </div>
              </dl>
              <Button variant="secondary" size="sm" onClick={() => void client.logout()} disabled={client.busy === "logout"}>
                {client.busy === "logout" ? "Signing out..." : "Sign out"}
              </Button>
            </CardBody>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>Cashier shift &amp; cart</CardTitle>
            </CardHeader>
            <CardBody className="space-y-4">
              <div className="flex flex-wrap items-end gap-3">
                <FormField label="Opening cash" htmlFor="rpos-open-cash">
                  <TextInput id="rpos-open-cash" value={openingCash} onChange={(event) => setOpeningCash(event.target.value)} inputMode="decimal" className="w-32" />
                </FormField>
                <Button variant="secondary" onClick={handleOpenShift} disabled={client.busy !== "idle"}>Open shift</Button>
                <Button variant="secondary" onClick={() => void client.startCart()} disabled={client.busy !== "idle"}>Start cart</Button>
                {client.cart ? <Button variant="ghost" onClick={() => void client.clearCart()} disabled={client.busy !== "idle"}>Clear cart</Button> : null}
              </div>

              <div className="grid gap-3 sm:grid-cols-[2fr,1fr,auto] sm:items-end">
                <FormField label="Product ID" htmlFor="rpos-product" hint="Host validates product reference and pricing authoritatively">
                  <TextInput id="rpos-product" value={productId} onChange={(event) => setProductId(event.target.value)} placeholder="product guid" autoComplete="off" />
                </FormField>
                <FormField label="Quantity" htmlFor="rpos-qty">
                  <TextInput id="rpos-qty" value={quantity} onChange={(event) => setQuantity(event.target.value)} inputMode="decimal" />
                </FormField>
                <Button onClick={handleAddLine} disabled={client.busy !== "idle" || !client.cart}>Add line</Button>
              </div>

              {client.cart && client.cart.lines.length > 0 ? (
                <div className="space-y-3">
                  <DataTable columns={cartColumns} rows={client.cart.lines} getRowKey={(line) => line.productId} />
                  <div className="flex items-center justify-between border-t border-slate-200 pt-3">
                    <span className="text-sm text-slate-500">Subtotal (host-priced)</span>
                    <span className="text-lg font-semibold text-slate-900">{formatCents(subtotalCents)}</span>
                  </div>
                </div>
              ) : (
                <p className="text-sm text-slate-500">Start a cart to add host-priced items.</p>
              )}

              <div className="flex flex-wrap items-end gap-3 border-t border-slate-200 pt-4">
                <FormField label="Cash tender" htmlFor="rpos-tender" hint="Remote tender is Cash-only in this build">
                  <TextInput id="rpos-tender" value={tenderAmount} onChange={(event) => setTenderAmount(event.target.value)} inputMode="decimal" className="w-36" />
                </FormField>
                <Button onClick={handleSubmitCheckout} disabled={client.busy !== "idle" || !client.cart}>Submit checkout</Button>
              </div>
            </CardBody>
          </Card>

          {client.lastCheckout ? (
            <Alert tone={checkoutTone(client.lastCheckout.status)} title={`Checkout: ${client.lastCheckout.status}`}>
              {client.lastCheckout.message} {client.lastCheckout.receiptNumber ? `Receipt ${client.lastCheckout.receiptNumber}` : ""}
            </Alert>
          ) : null}
        </>
      ) : null}
    </div>
  );
}
