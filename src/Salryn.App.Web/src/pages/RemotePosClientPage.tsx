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
  EmptyState,
  FormField,
  PasswordInput,
  StatusPill,
  TextInput,
  type DataTableColumn,
} from "../components/ui";
import { remotePosClientWarnings } from "../api/remotePosClient";
import { useRemotePosClient } from "../hooks/useRemotePosClient";
import type { RemotePosCapability, RemotePosCartLine } from "../types/remotePosClient";

// LAN-M16: Remote POS client shell. This page is the operator-facing surface a remote client
// (Computer B/C/D) uses to authenticate to the POS host (Computer A) and drive host-owned POS
// commands. It never opens the host database and never exposes host-local LAN controls.

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

  const capabilityColumns: DataTableColumn<RemotePosCapability>[] = [
    { key: "command", header: "Command", render: (cap) => <span className="font-mono text-xs">{cap.command}</span> },
    { key: "category", header: "Category", render: (cap) => cap.category },
    {
      key: "allowed",
      header: "Access",
      render: (cap) => (
        <StatusPill tone={cap.allowed ? "success" : "neutral"}>{cap.allowed ? "Allowed" : cap.decision}</StatusPill>
      ),
    },
    { key: "description", header: "Description", render: (cap) => <span className="text-slate-500">{cap.description}</span> },
  ];

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
        subtitle="Connect to the POS host over its explicit LAN address and operate as a host-authenticated cashier, manager, or admin."
      />

      <Alert tone="warning" title="This is a remote POS client, not the POS host">
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

      {!client.isConnected ? (
        <Card>
          <CardHeader>
            <CardTitle>Connect to POS host</CardTitle>
          </CardHeader>
          <CardBody className="space-y-4">
            <p className="text-sm text-slate-500">
              Enter the POS host LAN address and this terminal's identity + handshake proof, then sign in with your host
              cashier / manager / admin credentials. The host issues an operator session; cookies are not accepted as LAN
              trust.
            </p>
            <FormField label="POS host LAN address" htmlFor="rpos-host" hint="e.g. http://192.168.1.10:5005 (leave blank only when this app is served by the host)">
              <TextInput
                id="rpos-host"
                value={client.hostBaseUrl}
                onChange={(event) => client.setHostBaseUrl(event.target.value)}
                placeholder="http://192.168.1.10:5005"
                autoComplete="off"
              />
            </FormField>
            <div className="grid gap-4 sm:grid-cols-2">
              <FormField label="Terminal ID" htmlFor="rpos-terminal">
                <TextInput
                  id="rpos-terminal"
                  value={client.terminalId}
                  onChange={(event) => client.setTerminalId(event.target.value)}
                  placeholder="terminal id"
                  autoComplete="off"
                />
              </FormField>
              <FormField label="Handshake session ID" htmlFor="rpos-handshake">
                <TextInput
                  id="rpos-handshake"
                  value={client.handshakeSessionId}
                  onChange={(event) => client.setHandshakeSessionId(event.target.value)}
                  placeholder="handshake session id"
                  autoComplete="off"
                />
              </FormField>
            </div>
            <div className="grid gap-4 sm:grid-cols-2">
              <FormField label="Username" htmlFor="rpos-user">
                <TextInput
                  id="rpos-user"
                  value={username}
                  onChange={(event) => setUsername(event.target.value)}
                  autoComplete="off"
                />
              </FormField>
              <FormField label="Password" htmlFor="rpos-pass">
                <PasswordInput
                  id="rpos-pass"
                  value={password}
                  onChange={(event) => setPassword(event.target.value)}
                  autoComplete="off"
                />
              </FormField>
            </div>
            <Button
              onClick={handleLogin}
              disabled={client.busy === "login" || !username.trim() || !client.terminalId.trim() || !client.handshakeSessionId.trim()}
            >
              {client.busy === "login" ? "Signing in..." : "Sign in to host"}
            </Button>
          </CardBody>
        </Card>
      ) : (
        <>
          <Card>
            <CardHeader>
              <CardTitle>Remote client identity</CardTitle>
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
                  <dt className="text-slate-400">Host ID</dt>
                  <dd className="font-mono text-xs text-slate-800">{client.session?.hostId ?? "-"}</dd>
                </div>
                <div>
                  <dt className="text-slate-400">Store ID</dt>
                  <dd className="font-mono text-xs text-slate-800">{client.session?.storeId ?? "-"}</dd>
                </div>
                <div>
                  <dt className="text-slate-400">Terminal ID</dt>
                  <dd className="font-mono text-xs text-slate-800">{client.session?.terminalId ?? "-"}</dd>
                </div>
                <div>
                  <dt className="text-slate-400">Session expires</dt>
                  <dd className="text-slate-800">{formatExpiry(client.session?.expiresAtUtc ?? null)}</dd>
                </div>
                <div>
                  <dt className="text-slate-400">Host LAN address</dt>
                  <dd className="text-slate-800">{client.hostBaseUrl.trim() || "same-origin (served by host)"}</dd>
                </div>
              </dl>
              <div>
                <Button variant="secondary" size="sm" onClick={() => void client.logout()} disabled={client.busy === "logout"}>
                  {client.busy === "logout" ? "Signing out..." : "Sign out"}
                </Button>
              </div>
            </CardBody>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>Cashier shift &amp; cart</CardTitle>
            </CardHeader>
            <CardBody className="space-y-4">
              <div className="flex flex-wrap items-end gap-3">
                <FormField label="Opening cash" htmlFor="rpos-open-cash">
                  <TextInput
                    id="rpos-open-cash"
                    value={openingCash}
                    onChange={(event) => setOpeningCash(event.target.value)}
                    inputMode="decimal"
                    className="w-32"
                  />
                </FormField>
                <Button variant="secondary" onClick={handleOpenShift} disabled={client.busy !== "idle"}>
                  Open shift
                </Button>
                <Button variant="secondary" onClick={() => void client.startCart()} disabled={client.busy !== "idle"}>
                  Start cart
                </Button>
                {client.cart ? (
                  <Button variant="ghost" onClick={() => void client.clearCart()} disabled={client.busy !== "idle"}>
                    Clear cart
                  </Button>
                ) : null}
              </div>

              <div className="grid gap-3 sm:grid-cols-[2fr,1fr,auto] sm:items-end">
                <FormField label="Product ID" htmlFor="rpos-product" hint="Host validates product reference and pricing authoritatively">
                  <TextInput
                    id="rpos-product"
                    value={productId}
                    onChange={(event) => setProductId(event.target.value)}
                    placeholder="product guid"
                    autoComplete="off"
                  />
                </FormField>
                <FormField label="Quantity" htmlFor="rpos-qty">
                  <TextInput
                    id="rpos-qty"
                    value={quantity}
                    onChange={(event) => setQuantity(event.target.value)}
                    inputMode="decimal"
                  />
                </FormField>
                <Button onClick={handleAddLine} disabled={client.busy !== "idle" || !client.cart}>
                  Add line
                </Button>
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
                <EmptyState title="No cart lines" description="Start a cart and add product lines. Pricing is determined by the host." />
              )}

              <div className="flex flex-wrap items-end gap-3 border-t border-slate-200 pt-4">
                <FormField label="Cash tendered" htmlFor="rpos-tender">
                  <TextInput
                    id="rpos-tender"
                    value={tenderAmount}
                    onChange={(event) => setTenderAmount(event.target.value)}
                    inputMode="decimal"
                    className="w-32"
                  />
                </FormField>
                <Button
                  onClick={handleSubmitCheckout}
                  disabled={client.busy !== "idle" || !client.cart || client.cart.lines.length === 0}
                >
                  {client.busy === "checkout" ? "Submitting..." : "Submit sale to host"}
                </Button>
              </div>

              {client.lastCheckout ? (
                <Alert tone={checkoutTone(client.lastCheckout.status)} title={`Checkout: ${client.lastCheckout.status}`}>
                  <div className="space-y-1">
                    <p>{client.lastCheckout.message}</p>
                    {client.lastCheckout.receiptNumber ? (
                      <p>
                        Receipt <span className="font-mono">{client.lastCheckout.receiptNumber}</span> · Total{" "}
                        {formatCents(client.lastCheckout.totalCents)} · Change {formatCents(client.lastCheckout.changeCents)}
                        {client.lastCheckout.duplicate ? " · (idempotent duplicate)" : ""}
                      </p>
                    ) : null}
                  </div>
                </Alert>
              ) : null}
            </CardBody>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>Role capabilities</CardTitle>
            </CardHeader>
            <CardBody className="space-y-4">
              {client.capabilities ? (
                <>
                  <DataTable
                    columns={capabilityColumns}
                    rows={client.capabilities.capabilities}
                    getRowKey={(cap) => cap.command}
                  />
                  <div>
                    <p className="text-sm font-medium text-slate-700">Host-local only (never available over LAN)</p>
                    <div className="mt-2 flex flex-wrap gap-2">
                      {client.capabilities.hostLocalOnlyActions.map((action) => (
                        <StatusPill key={action} tone="neutral">
                          {action}
                        </StatusPill>
                      ))}
                    </div>
                  </div>
                  {client.capabilities.warnings.map((warning) => (
                    <p key={warning} className="text-xs text-slate-400">
                      {warning}
                    </p>
                  ))}
                </>
              ) : (
                <EmptyState title="Capabilities unavailable" description="The host did not return a capability set for this session." />
              )}
            </CardBody>
          </Card>
        </>
      )}
    </div>
  );
}
