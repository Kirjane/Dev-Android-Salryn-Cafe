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

  const [joinCode, setJoinCode] = useState("");
  const [deviceName, setDeviceName] = useState("");
  const [pairingCode, setPairingCode] = useState("");
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
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
    void client.login(username.trim(), password);
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

      {client.restoreState === "revoked" ? (
        <Alert tone="warning" title="Pairing revoked or expired">
          Pairing revoked or expired. Re-pair this device.
        </Alert>
      ) : null}

      {client.restoreState === "offline" ? (
        <Alert tone="warning" title="Host unreachable">
          The host is unreachable. Stored pairing was kept on this device. Retry when the host is available or clear pairing to start over.
        </Alert>
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

      {client.hostAddressConfirmed && !client.pairingRequest && !client.isPaired ? (
        <Card>
          <CardHeader>
            <CardTitle>State B — Join Salryn Host</CardTitle>
          </CardHeader>
          <CardBody className="space-y-4">
            <p className="text-sm text-slate-500">
              Ask the host operator for the join code. The join code only opens a pending request; it does not pair or log in this device.
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
              {client.busy === "pairing" ? "Requesting..." : "Request to join"}
            </Button>
          </CardBody>
        </Card>
      ) : null}

      {client.pairingRequest && !client.isPaired ? (
        <Card>
          <CardHeader>
            <CardTitle>{client.pairingStatus?.pairingCodeRequired ? "State D — Pairing code" : "State C — Waiting for host"}</CardTitle>
          </CardHeader>
          <CardBody className="space-y-4">
            <div className="flex flex-wrap items-center gap-2">
              <StatusPill tone={client.pairingStatus?.pairingCodeRequired ? "success" : "warning"}>
                {client.pairingStatus?.pairingState ?? client.pairingRequest.pairingState}
              </StatusPill>
              <StatusPill tone="info">{client.pairingStatus?.deviceName ?? client.pairingRequest.deviceName}</StatusPill>
            </div>
            <p className="text-sm text-slate-500">
              {client.pairingStatus?.message ?? "Waiting for host approval. Ask the host operator to accept this device."}
            </p>
            <div className="flex flex-wrap gap-2">
              <Button variant="secondary" onClick={() => void client.refreshPairingStatus()} disabled={client.busy === "pairing"}>
                Refresh approval status
              </Button>
              <Button variant="ghost" onClick={client.resetPairing}>
                Start over
              </Button>
            </div>
            {client.pairingStatus?.pairingCodeRequired ? (
              <div className="space-y-3 border-t border-slate-200 pt-4">
                <FormField label="Pairing code" htmlFor="rpos-pairing-code" hint="One-time code shown by the host after approval">
                  <TextInput id="rpos-pairing-code" value={pairingCode} onChange={(event) => setPairingCode(event.target.value)} autoComplete="off" />
                </FormField>
                <Button onClick={handleActivatePairing} disabled={client.busy === "pairing" || !pairingCode.trim()}>
                  {client.busy === "pairing" ? "Activating..." : "Activate pairing"}
                </Button>
              </div>
            ) : null}
          </CardBody>
        </Card>
      ) : null}

      {client.isPaired && !client.isConnected ? (
        <Card>
          <CardHeader>
            <CardTitle>State E — Cashier / Manager login only</CardTitle>
          </CardHeader>
          <CardBody className="space-y-4">
            {client.pairingRestored ? (
              <Alert tone="success" title="Paired terminal restored">
                This device restored its approved terminal pairing from local storage. Sign in as Cashier or Manager to continue.
              </Alert>
            ) : null}
            <div className="flex flex-wrap items-center gap-2 text-sm text-slate-600">
              <StatusPill tone="success">Paired terminal</StatusPill>
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
