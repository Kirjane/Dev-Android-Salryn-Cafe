import { useEffect, useRef, useState, type KeyboardEvent } from "react";
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
import { TerminalEntryFrame, TerminalShell } from "../components/TerminalShell";
import {
  fetchRemotePosStoreBranding,
  type RemotePosStoreBranding,
} from "../api/remotePosClient";
import { useRemotePosClient } from "../hooks/useRemotePosClient";
import type { RemotePosCartLine } from "../types/remotePosClient";
import { clearAllLanClientDeviceState } from "../utils/lanClientStorage";

// LAN-M16-QA-B: Remote POS field UX. Pairing approves the device; login approves the person.
// PRE-BETA-E: After operator login, Salryn TerminalShell (Host visual language, terminal-safe nav).

const REMOTE_POS_CLEAR_PAIRING_CONFIRM =
  "Clear this device pairing and saved cashier session? This device will need to request access from the host again.";

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

type RemotePosClientPageProps = {
  onExitToLanding?: () => void;
};

export function RemotePosClientPage({ onExitToLanding }: RemotePosClientPageProps = {}) {
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
  const [scanSearch, setScanSearch] = useState("");
  const [scanStatus, setScanStatus] = useState<string | null>(null);
  const [isScanAdding, setIsScanAdding] = useState(false);
  const [quantity, setQuantity] = useState("1");
  const [tenderAmount, setTenderAmount] = useState("");
  const [branding, setBranding] = useState<RemotePosStoreBranding | null>(null);
  const scanInputRef = useRef<HTMLInputElement | null>(null);

  const subtotalCents = client.cart?.subtotalCents ?? 0;

  useEffect(() => {
    if (!client.isConnected || !client.authedConnection) {
      setBranding(null);
      return;
    }
    let cancelled = false;
    void fetchRemotePosStoreBranding(client.authedConnection)
      .then((result) => {
        if (!cancelled) setBranding(result);
      })
      .catch(() => {
        if (!cancelled) setBranding(null);
      });
    return () => {
      cancelled = true;
    };
  }, [client.isConnected, client.authedConnection]);

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

  async function handleScanSubmit() {
    const barcode = scanSearch.trim();
    if (!barcode || isScanAdding || client.busy !== "idle") return;
    if (!client.cart) {
      setScanStatus("Start a cart before scanning.");
      return;
    }
    setIsScanAdding(true);
    setScanStatus(`Looking up barcode ${barcode}…`);
    try {
      const qtyX1000 = Math.round(Number.parseFloat(quantity || "1") * 1000);
      const qty = Number.isFinite(qtyX1000) && qtyX1000 > 0 ? qtyX1000 : 1000;
      const ok = await client.addLineByBarcode(barcode, qty);
      if (ok) {
        setScanStatus(`Barcode ${barcode} added to cart.`);
        setScanSearch("");
      } else {
        setScanStatus(client.errorMessage ?? "Barcode was not added.");
      }
    } finally {
      setIsScanAdding(false);
      window.setTimeout(() => scanInputRef.current?.focus(), 0);
    }
  }

  function handleScanKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === "Enter" || event.key === "Tab") {
      event.preventDefault();
      event.stopPropagation();
      void handleScanSubmit();
    }
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

  function confirmClearPairingOrState() {
    if (!window.confirm(REMOTE_POS_CLEAR_PAIRING_CONFIRM)) {
      return;
    }
    clearAllLanClientDeviceState();
    client.clearPairing();
  }

  const checkoutBody = (
    <div className="space-y-6">
      <CommandBar
        title="POS Checkout"
        subtitle="Terminal workstation checkout. Host validates products, pricing, and sales."
        status={<StatusPill tone="success">Terminal Mode</StatusPill>}
      />

      {client.errorMessage ? (
        <Alert tone="danger" title="Request failed">
          {client.errorMessage}
        </Alert>
      ) : null}

      {client.statusMessage ? <Alert tone="info">{client.statusMessage}</Alert> : null}

      {client.operatorSessionRestored ? (
        <Alert tone="success" title="Operator session restored">
          Operator session restored. Signed in as {client.session?.displayName ?? client.session?.username ?? "operator"}.
        </Alert>
      ) : null}

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

          <div className="space-y-2">
            <FormField
              label="Scan / search"
              htmlFor="rpos-scan"
              hint="Keyboard-wedge barcode input. Press Enter or Tab to add. Host validates the barcode."
            >
              <TextInput
                ref={scanInputRef}
                id="rpos-scan"
                className="min-h-12 text-base"
                value={scanSearch}
                onChange={(event) => {
                  setScanSearch(event.target.value);
                  setScanStatus(null);
                }}
                onKeyDown={handleScanKeyDown}
                placeholder="Scan barcode or type barcode…"
                autoComplete="off"
                autoFocus
                inputMode="search"
              />
            </FormField>
            {scanStatus ? <p className="text-xs text-slate-600">{scanStatus}</p> : null}
            <div className="grid gap-3 sm:grid-cols-[1fr,auto] sm:items-end">
              <FormField label="Quantity" htmlFor="rpos-qty">
                <TextInput id="rpos-qty" value={quantity} onChange={(event) => setQuantity(event.target.value)} inputMode="decimal" />
              </FormField>
              <Button onClick={() => void handleScanSubmit()} disabled={client.busy !== "idle" || !client.cart || isScanAdding || !scanSearch.trim()}>
                {isScanAdding ? "Adding…" : "Add from barcode"}
              </Button>
            </div>
          </div>

          <details className="rounded-lg border border-slate-200 bg-slate-50 px-3 py-2 text-xs text-slate-600">
            <summary className="cursor-pointer font-medium text-slate-700">Advanced: add by product ID</summary>
            <div className="mt-3 grid gap-3 sm:grid-cols-[2fr,1fr,auto] sm:items-end">
              <FormField label="Product ID" htmlFor="rpos-product" hint="Support/debug path only — not the normal cashier flow">
                <TextInput id="rpos-product" value={productId} onChange={(event) => setProductId(event.target.value)} placeholder="product guid" autoComplete="off" />
              </FormField>
              <FormField label="Quantity" htmlFor="rpos-qty-id">
                <TextInput id="rpos-qty-id" value={quantity} onChange={(event) => setQuantity(event.target.value)} inputMode="decimal" />
              </FormField>
              <Button onClick={handleAddLine} disabled={client.busy !== "idle" || !client.cart}>Add line</Button>
            </div>
          </details>

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
            <FormField label="Cash tender" htmlFor="rpos-tender" hint="Cash-only tender in this build">
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

      <details className="rounded-lg border border-slate-200 bg-white px-4 py-3 text-xs text-slate-500">
        <summary className="cursor-pointer font-medium text-slate-700">Diagnostics</summary>
        <div className="mt-3 space-y-1">
          <p>
            Role: <span className="font-mono text-slate-700">{client.session?.roleName ?? "-"}</span>
            {" · "}
            Terminal: <span className="font-mono text-slate-700">{client.session?.terminalId ?? "-"}</span>
            {" · "}
            Host: <span className="font-mono text-slate-700">{client.hostBaseUrl.trim() || "-"}</span>
          </p>
          <p>
            Session expires: <span className="font-mono text-slate-700">{formatExpiry(client.session?.expiresAtUtc ?? null)}</span>
          </p>
        </div>
      </details>
    </div>
  );

  if (client.isConnected) {
    const logoUrl = branding?.logoDataUrl || branding?.logoUrl || null;
    return (
      <TerminalShell
        operatorName={client.session?.displayName ?? client.session?.username ?? "Operator"}
        roleName={client.session?.roleName ?? "Cashier"}
        terminalLabel={client.pairingStatus?.deviceName ?? (client.terminalIdSuffix ? `…${client.terminalIdSuffix}` : null)}
        hostLabel={client.hostBaseUrl.trim() || null}
        storeName={branding?.storeName ?? null}
        logoUrl={logoUrl}
        activeNav="checkout"
        onNavigate={() => undefined}
        onSignOut={() => {
          void client.logout();
        }}
        onExitToLanding={onExitToLanding}
      >
        {checkoutBody}
      </TerminalShell>
    );
  }

  return (
    <TerminalEntryFrame
      title="Connect terminal to Host"
      subtitle="Pair this licensed terminal, then sign in as Cashier or Manager. Admin and Owner accounts remain host-local."
      onBack={onExitToLanding}
    >
    <div className="space-y-6">
      <Alert tone="info" title="Terminal access">
        This licensed terminal must be approved by the POS Host before a Cashier or Manager can sign in. Admin and Owner
        accounts remain host-local.
      </Alert>

      {client.errorMessage ? (
        <Alert tone="danger" title="Terminal request failed">
          {client.errorMessage}
        </Alert>
      ) : null}

      {client.statusMessage ? <Alert tone="info">{client.statusMessage}</Alert> : null}

      {client.hostAddressConfirmed ? (
        <details className="rounded-lg border border-slate-200 bg-white px-4 py-3 text-xs text-slate-500">
          <summary className="cursor-pointer font-medium text-slate-700">Diagnostics</summary>
          <div className="mt-3">
            <p>
              State: <span className="font-mono text-slate-700">{client.flowState}</span>
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
          </div>
        </details>
      ) : null}

      {client.flowState === "S_REVOKED" ? (
        <Alert tone="warning" title="Pairing revoked or expired">
          {client.lastPairingNotice ?? "Pairing revoked or expired. Re-pair this device."}
          <div className="mt-3">
            <Button variant="secondary" onClick={confirmClearPairingOrState} disabled={client.busy !== "idle"}>
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

      {client.flowState === "S_HOST_PAUSED" ? (
        <Alert tone="warning" title="Remote POS is paused on the Host">
          <p>Your trusted terminal pairing is still saved. Ask a Host Admin to enable Remote POS in Terminal Management.</p>
          <div className="mt-3">
            <Button variant="secondary" onClick={client.retryHostConnection} disabled={client.busy !== "idle"}>
              Retry Host connection
            </Button>
          </div>
        </Alert>
      ) : null}

      {client.flowState === "S_RESTORING" ? (
        <Alert tone="info">Restoring saved terminal state from this device...</Alert>
      ) : null}

      {!client.hostAddressConfirmed ? (
        <Card>
          <CardHeader>
            <CardTitle>Connect to POS Host</CardTitle>
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
              <FormField label="Terminal display name" htmlFor="rpos-device-name" hint="Shown to the host during approval.">
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
              <Button variant="ghost" onClick={confirmClearPairingOrState}>
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
              <Button variant="ghost" onClick={confirmClearPairingOrState}>
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
                This device restored its trusted terminal pairing. Operator sign-in is required only if no valid saved
                cashier session remains on the host.
              </Alert>
            ) : (
              <Alert tone="success" title="This terminal is paired">
                Pairing is complete for this device. Sign in as Cashier or Manager to open the Salryn terminal shell.
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
              Cashier / Manager login only — passwords are never stored on this device.
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
              A saved token is restored only after the host validates it.
            </p>
            <div className="flex flex-wrap gap-2">
              <Button onClick={handleLogin} disabled={client.busy === "login" || !username.trim()}>
                {client.busy === "login" ? "Signing in..." : "Sign in to host"}
              </Button>
              <Button variant="ghost" onClick={confirmClearPairingOrState} disabled={client.busy !== "idle"}>
                Clear this device pairing and saved cashier session
              </Button>
              <Button variant="ghost" disabled={client.busy !== "idle"} onClick={confirmClearPairingOrState}>
                Clear this device state
              </Button>
            </div>
          </CardBody>
        </Card>
      ) : null}

    </div>
    </TerminalEntryFrame>
  );
}
