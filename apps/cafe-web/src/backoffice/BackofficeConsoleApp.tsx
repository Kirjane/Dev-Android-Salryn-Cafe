import {
  BarChart3,
  BookOpen,
  CalendarClock,
  HardDrive,
  Laptop,
  Package,
  ShieldCheck,
  Wallet,
} from "lucide-react";
import { useMemo, useState } from "react";
import {
  Alert,
  Button,
  CafeShell,
  Card,
  CardBody,
  CardHeader,
  DataTable,
  ErrorBoundary,
  FormField,
  MetricCard,
  SelectInput,
  StatusBadge,
  TextInput,
  ToastProvider,
  useToast,
  type CafeShellNavItem,
} from "@salryn/cafe-ui";
import {
  createMockBackofficeClient,
  INITIAL_AR_LEDGER,
  INITIAL_AUDIT,
  INITIAL_BOOKINGS,
  INITIAL_DEVICES,
  INITIAL_RECIPES,
  INITIAL_STOCK,
  type BackofficeClient,
} from "./mockBackofficeClient";
import {
  formatPhp,
  UX_C_DATA_SOURCE,
  type ArLedgerEntry,
  type DeploymentSurface,
  type EnrolledDevice,
  type IngredientStock,
} from "./types";
import { useBackofficeOperation } from "./useBackofficeOperation";
import { DeviceManagementPanel } from "../devices/DeviceManagementPanel";

export type BackofficeConsoleAppProps = {
  client?: BackofficeClient;
  initialSurface?: DeploymentSurface;
};

function BackofficeConsoleInner({
  client,
  initialSurface,
}: {
  client: BackofficeClient;
  initialSurface: DeploymentSurface;
}) {
  const { notify } = useToast();
  const [surface, setSurface] = useState<DeploymentSurface>(initialSurface);
  const [activeNav, setActiveNav] = useState("reports");
  const [stock, setStock] = useState<IngredientStock[]>(INITIAL_STOCK);
  const [arLedger, setArLedger] = useState<ArLedgerEntry[]>(INITIAL_AR_LEDGER);
  const [devices, setDevices] = useState<EnrolledDevice[]>(INITIAL_DEVICES);

  const [selectedIngredientId, setSelectedIngredientId] = useState(INITIAL_STOCK[0].id);
  const [stockKind, setStockKind] = useState<"PURCHASE" | "WASTE" | "COUNT">("PURCHASE");
  const [stockQty, setStockQty] = useState("2");

  const [selectedArId, setSelectedArId] = useState(INITIAL_AR_LEDGER[0].id);
  const [arSettlePhp, setArSettlePhp] = useState("500");

  const stockOp = useBackofficeOperation("inv-adjust");
  const arOp = useBackofficeOperation("ar-settle");

  const navItems: CafeShellNavItem[] = [
    { id: "reports", label: "Owner Reports & COGS", group: "Central Owner", Icon: BarChart3 },
    { id: "devices", label: "Device Trust & Health", group: "Central Owner", Icon: Laptop, badge: devices.filter((d) => d.status === "PENDING_APPROVAL").length || undefined },
    { id: "recipes", label: "Menu, Recipes & BOM", group: "Cafe Operations", Icon: BookOpen },
    { id: "inventory", label: "Inventory & Waste", group: "Cafe Operations", Icon: Package },
    { id: "cash-ar", label: "Cash, Expense & AR", group: "Cafe Operations", Icon: Wallet },
    { id: "bookings", label: "Bookings, Delivery & Staff", group: "Cafe Operations", Icon: CalendarClock },
    { id: "audit", label: "Audit, Users & Settings", group: "Admin", Icon: ShieldCheck },
    { id: "host-ops", label: "Host Backup & License", group: "Windows Host Only", Icon: HardDrive },
  ];

  async function handleStockSubmit() {
    const qty = Number(stockQty);
    if (!Number.isFinite(qty) || qty < 0) return;
    const updated = await stockOp.execute((operationId) =>
      client.adjustStock({ operationId, ingredientId: selectedIngredientId, kind: stockKind, deltaOrCount: qty }),
    );
    if (updated) {
      setStock((prev) => prev.map((item) => (item.id === updated.id ? updated : item)));
      notify("success", `Stock updated · ${updated.name}`, `New on-hand: ${updated.onHand} ${updated.unit}`);
    }
  }

  async function handleArSettle() {
    const cents = Math.round(Number(arSettlePhp) * 100);
    const updated = await arOp.execute((operationId) =>
      client.settleArCash({ operationId, accountId: selectedArId, amountCents: cents }),
    );
    if (updated) {
      setArLedger((prev) => prev.map((row) => (row.id === updated.id ? updated : row)));
      notify("success", `Cash AR settlement posted · ${updated.customerName}`, `Remaining: ${formatPhp(updated.balanceCents)}`);
    }
  }

  return (
    <CafeShell
      storeName="Salryn Cafe"
      stationLabel={
        surface === "CENTRAL_OWNER"
          ? "Central Owner & Admin Portal (Independent of Windows Localhost)"
          : "Local Windows Host Console (127.0.0.1:5399 / LAN HTTPS 5400)"
      }
      connectionStatus={
        surface === "CENTRAL_OWNER"
          ? { label: "Central HTTPS", tone: "info" }
          : { label: "Local Host Ready", tone: "success" }
      }
      operatorName="Cafe Owner"
      roleName="Owner / Admin"
      navItems={navItems}
      activeNav={activeNav}
      onNavigate={setActiveNav}
      onSignOut={() => notify("info", "Signed out", "Session closed.")}
      banner={
        <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center", justifyContent: "space-between", gap: 10 }}>
          <div style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
            <StatusBadge tone="copper">{UX_C_DATA_SOURCE}</StatusBadge>
            <StatusBadge tone={surface === "CENTRAL_OWNER" ? "info" : "success"}>
              Surface: {surface === "CENTRAL_OWNER" ? "Central Owner Portal" : "Windows Host Console"}
            </StatusBadge>
          </div>
          <div role="group" aria-label="Deployment surface preview" style={{ display: "flex", gap: 8 }}>
            <Button
              size="sm"
              variant={surface === "CENTRAL_OWNER" ? "primary" : "secondary"}
              aria-pressed={surface === "CENTRAL_OWNER"}
              onClick={() => setSurface("CENTRAL_OWNER")}
            >
              Central Owner View
            </Button>
            <Button
              size="sm"
              variant={surface === "WINDOWS_HOST" ? "primary" : "secondary"}
              aria-pressed={surface === "WINDOWS_HOST"}
              onClick={() => setSurface("WINDOWS_HOST")}
            >
              Windows Host View
            </Button>
          </div>
        </div>
      }
    >
      {activeNav === "reports" ? (
        <div style={{ display: "grid", gap: 16 }}>
          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(210px, 1fr))", gap: 14 }}>
            <MetricCard label="Gross Cafe Sales (MTD)" value="₱412,850.00" detail="Cash 84% · AR 16%" />
            <MetricCard label="Recipe BOM COGS" value="₱128,420.00" detail="31.1% food & beverage cost" />
            <MetricCard label="Gross Profit Margin" value="68.9%" detail="₱284,430.00 contribution" />
            <MetricCard
              label="Outstanding AR"
              value={formatPhp(arLedger.reduce((s, r) => s + r.balanceCents, 0))}
              detail={`${arLedger.length} active accounts`}
            />
          </div>
          <Card>
            <CardHeader title="Owner Profitability Snapshot" subtitle="Available from Central Owner Portal without Windows localhost dependency." />
            <CardBody>
              <DataTable
                caption="Recipe COGS & Margin"
                rows={INITIAL_RECIPES}
                getRowKey={(r) => r.id}
                columns={[
                  { key: "name", header: "Menu Item", render: (r) => <strong>{r.menuName}</strong> },
                  { key: "price", header: "Price", align: "right", tabular: true, render: (r) => formatPhp(r.sellingPriceCents) },
                  { key: "cogs", header: "BOM COGS", align: "right", tabular: true, render: (r) => formatPhp(r.unitCogsCents) },
                  {
                    key: "margin",
                    header: "Margin %",
                    align: "right",
                    tabular: true,
                    render: (r) => `${(((r.sellingPriceCents - r.unitCogsCents) / r.sellingPriceCents) * 100).toFixed(1)}%`,
                  },
                ]}
              />
            </CardBody>
          </Card>
        </div>
      ) : null}

      {activeNav === "devices" ? (
        <DeviceManagementPanel
          client={client}
          surface={surface}
          devices={devices}
          onDeviceUpdated={(updated) =>
            setDevices((prev) => prev.map((d) => (d.deviceId === updated.deviceId ? updated : d)))
          }
        />
      ) : null}

      {activeNav === "recipes" ? (
        <Card>
          <CardHeader title="Menu, Recipes & Ingredient BOM" subtitle="Recipe BOM links directly to inventory depletion and COGS." />
          <CardBody>
            <DataTable
              caption="Cafe Recipes and BOM"
              rows={INITIAL_RECIPES}
              getRowKey={(r) => r.id}
              columns={[
                { key: "name", header: "Item", render: (r) => <strong>{r.menuName}</strong> },
                { key: "bom", header: "Ingredient BOM", render: (r) => r.bomSummary },
                { key: "cogs", header: "Unit COGS", align: "right", tabular: true, render: (r) => formatPhp(r.unitCogsCents) },
              ]}
            />
          </CardBody>
        </Card>
      ) : null}

      {activeNav === "inventory" ? (
        <div style={{ display: "grid", gap: 16 }}>
          <Card>
            <CardHeader title="Record Purchase, Waste, or Physical Count" subtitle="Durable operation ID enforced on every inventory mutation." />
            <CardBody>
              <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))", gap: 12, alignItems: "end" }}>
                <FormField label="Ingredient">
                  <SelectInput value={selectedIngredientId} onChange={(e) => setSelectedIngredientId(e.target.value)}>
                    {stock.map((s) => (
                      <option key={s.id} value={s.id}>
                        {s.name} ({s.onHand} {s.unit})
                      </option>
                    ))}
                  </SelectInput>
                </FormField>
                <FormField label="Movement Type">
                  <SelectInput value={stockKind} onChange={(e) => setStockKind(e.target.value as "PURCHASE" | "WASTE" | "COUNT")}>
                    <option value="PURCHASE">Purchase Receipt (+)</option>
                    <option value="WASTE">Spoilage / Waste (−)</option>
                    <option value="COUNT">Physical Count (=)</option>
                  </SelectInput>
                </FormField>
                <FormField label="Quantity">
                  <TextInput inputMode="decimal" value={stockQty} onChange={(e) => setStockQty(e.target.value)} />
                </FormField>
                <Button variant="primary" loading={stockOp.inFlight} onClick={() => void handleStockSubmit()}>
                  Post Stock Movement
                </Button>
              </div>
            </CardBody>
          </Card>

          <Card>
            <CardHeader title="Ingredient Stock Ledger" />
            <CardBody>
              <DataTable
                caption="Ingredient Inventory"
                rows={stock}
                getRowKey={(s) => s.id}
                columns={[
                  { key: "name", header: "Ingredient", render: (s) => <strong>{s.name}</strong> },
                  { key: "onHand", header: "On Hand", align: "right", tabular: true, render: (s) => `${s.onHand} ${s.unit}` },
                  { key: "reorder", header: "Reorder Level", align: "right", tabular: true, render: (s) => `${s.reorderLevel} ${s.unit}` },
                  {
                    key: "health",
                    header: "Status",
                    render: (s) =>
                      s.onHand <= s.reorderLevel ? (
                        <StatusBadge tone="warning">Low Stock</StatusBadge>
                      ) : (
                        <StatusBadge tone="success">Healthy</StatusBadge>
                      ),
                  },
                ]}
              />
            </CardBody>
          </Card>
        </div>
      ) : null}

      {activeNav === "cash-ar" ? (
        <div style={{ display: "grid", gap: 16 }}>
          <Card>
            <CardHeader title="Cash AR Settlement" subtitle="Salryn Cafe settles AR balances strictly via Cash tender." />
            <CardBody>
              {arOp.error ? <Alert tone="danger" title="Settlement Error" description={arOp.error} /> : null}
              <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))", gap: 12, alignItems: "end" }}>
                <FormField label="AR Account">
                  <SelectInput value={selectedArId} onChange={(e) => setSelectedArId(e.target.value)}>
                    {arLedger.map((a) => (
                      <option key={a.id} value={a.id}>
                        {a.customerName} ({formatPhp(a.balanceCents)})
                      </option>
                    ))}
                  </SelectInput>
                </FormField>
                <FormField label="Cash Payment (PHP)">
                  <TextInput inputMode="decimal" value={arSettlePhp} onChange={(e) => setArSettlePhp(e.target.value)} />
                </FormField>
                <Button variant="primary" loading={arOp.inFlight} onClick={() => void handleArSettle()}>
                  Post Cash Settlement
                </Button>
              </div>
            </CardBody>
          </Card>
          <Card>
            <CardHeader title="AR Customer Aging Ledger" />
            <CardBody>
              <DataTable
                caption="AR Aging Ledger"
                rows={arLedger}
                getRowKey={(r) => r.id}
                columns={[
                  { key: "cust", header: "Account", render: (r) => <strong>{r.customerName}</strong> },
                  { key: "bal", header: "Balance", align: "right", tabular: true, render: (r) => formatPhp(r.balanceCents) },
                  { key: "age", header: "Aging", render: (r) => r.agingBucket },
                  { key: "last", header: "Last Payment", render: (r) => r.lastPaymentLabel },
                ]}
              />
            </CardBody>
          </Card>
        </div>
      ) : null}

      {activeNav === "bookings" ? (
        <Card>
          <CardHeader title="Bookings, Coffee Cart Events & Staff Ops" subtitle="Tracks event deposits, delivery dispatch, and shift attendance/incidents." />
          <CardBody>
            <DataTable
              caption="Coffee Cart and Event Bookings"
              rows={INITIAL_BOOKINGS}
              getRowKey={(b) => b.id}
              columns={[
                { key: "client", header: "Client", render: (b) => <strong>{b.clientName}</strong> },
                { key: "date", header: "Date", tabular: true, render: (b) => b.eventDate },
                { key: "pkg", header: "Package", render: (b) => b.packageName },
                { key: "dep", header: "Deposit / Total", align: "right", tabular: true, render: (b) => `${formatPhp(b.depositPaidCents)} / ${formatPhp(b.totalCents)}` },
                {
                  key: "status",
                  header: "Status",
                  render: (b) => <StatusBadge tone={b.status === "CONFIRMED" ? "success" : "warning"}>{b.status.replace("_", " ")}</StatusBadge>,
                },
              ]}
            />
          </CardBody>
        </Card>
      ) : null}

      {activeNav === "audit" ? (
        <Card>
          <CardHeader title="Audit Log, Users & Store Settings" subtitle="Tracks both Central Owner and Windows Host audited mutations." />
          <CardBody>
            <DataTable
              caption="Salryn Cafe Audit Trail"
              rows={INITIAL_AUDIT}
              getRowKey={(a) => a.id}
              columns={[
                { key: "time", header: "Timestamp", tabular: true, render: (a) => a.timestamp },
                { key: "actor", header: "Actor", render: (a) => a.actor },
                { key: "action", header: "Action", render: (a) => a.action },
                { key: "op", header: "Operation ID", tabular: true, render: (a) => a.operationId },
                { key: "surface", header: "Surface", render: (a) => <StatusBadge tone={a.surface === "CENTRAL_OWNER" ? "info" : "neutral"}>{a.surface}</StatusBadge> },
              ]}
            />
          </CardBody>
        </Card>
      ) : null}

      {activeNav === "host-ops" ? (
        <Card>
          <CardHeader
            title="Windows Host Backup, Restore & Machine License"
            subtitle="Isolated under C:\ProgramData\SalrynCafe (ProductId = Salryn.Cafe)."
          />
          <CardBody>
            {surface === "CENTRAL_OWNER" ? (
              <Alert
                tone="warning"
                title="Host-Only Control — Unavailable in Central Owner View"
                description="Local SQLite backup/restore, machine license binding, and LAN HTTPS listener (port 5400) can only be executed on the physical Windows Host Console."
              />
            ) : (
              <div style={{ display: "grid", gap: 12 }}>
                <Alert
                  tone="success"
                  title="Windows Host Console Active"
                  description="Data root: C:\ProgramData\SalrynCafe · Loopback: 127.0.0.1:5399 · LAN HTTPS: 5400 · ProductId: Salryn.Cafe."
                />
                <div style={{ display: "flex", flexWrap: "wrap", gap: 10 }}>
                  <Button variant="primary" onClick={() => notify("success", "Backup snapshot verified", "Lineage v41 checksum valid.")}>
                    Create &amp; Verify Local Backup
                  </Button>
                  <Button variant="secondary" onClick={() => notify("info", "License verified", "Salryn.Cafe Host entitlement active.")}>
                    Verify Machine License
                  </Button>
                </div>
              </div>
            )}
          </CardBody>
        </Card>
      ) : null}
    </CafeShell>
  );
}

export function BackofficeConsoleApp({
  client: providedClient,
  initialSurface = "CENTRAL_OWNER",
}: BackofficeConsoleAppProps) {
  const client = useMemo(() => providedClient ?? createMockBackofficeClient(), [providedClient]);
  return (
    <ErrorBoundary>
      <ToastProvider>
        <BackofficeConsoleInner client={client} initialSurface={initialSurface} />
      </ToastProvider>
    </ErrorBoundary>
  );
}
