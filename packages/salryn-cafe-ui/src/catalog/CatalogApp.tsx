import {
  Coffee,
  ClipboardList,
  CreditCard,
  LayoutGrid,
  ShieldCheck,
  UtensilsCrossed,
} from "lucide-react";
import { useState } from "react";
import {
  Alert,
  BrandMark,
  brandColors,
  Button,
  CafeShell,
  Card,
  CardBody,
  CardFooter,
  CardHeader,
  ConfirmDialog,
  DataTable,
  Dialog,
  ErrorBoundary,
  FormField,
  getContrastRatio,
  MetricCard,
  SelectInput,
  semanticColors,
  StatusBadge,
  TextInput,
  ToastProvider,
  useToast,
  type CafeShellNavItem,
} from "../index";

type SampleOrder = {
  id: string;
  ticket: string;
  channel: string;
  items: string;
  totalPhp: string;
  status: "success" | "warning" | "copper" | "info";
  statusLabel: string;
};

const SAMPLE_ORDERS: SampleOrder[] = [
  {
    id: "ord-1",
    ticket: "#CY-1042",
    channel: "Dine-In · Table 4",
    items: "2× Spanish Latte (Oat), 1× Ensaymada",
    totalPhp: "₱495.00",
    status: "copper",
    statusLabel: "Preparing",
  },
  {
    id: "ord-2",
    ticket: "#CY-1043",
    channel: "Takeout",
    items: "1× Iced Americano, 1× Tuna Melt",
    totalPhp: "₱340.00",
    status: "success",
    statusLabel: "Ready for Pickup",
  },
  {
    id: "ord-3",
    ticket: "#CY-1044",
    channel: "Delivery · Rider Pending",
    items: "4× Cold Brew Bottle 500mL",
    totalPhp: "₱880.00",
    status: "warning",
    statusLabel: "AR Partial · ₱280 Due",
  },
];

const NAV_ITEMS: CafeShellNavItem[] = [
  { id: "tokens", label: "Brand & Tokens", group: "Foundation", Icon: Coffee },
  { id: "controls", label: "Controls & Forms", group: "Primitives", Icon: LayoutGrid },
  { id: "orders", label: "Cafe Queue Table", group: "Primitives", Icon: ClipboardList, badge: 3 },
  { id: "kitchen", label: "Kitchen & Status", group: "Workstation", Icon: UtensilsCrossed },
  { id: "settlement", label: "Cash / AR Tender", group: "Workstation", Icon: CreditCard },
  { id: "pairing", label: "Device Trust", group: "Admin", Icon: ShieldCheck },
];

function CatalogContent() {
  const { notify } = useToast();
  const [activeNav, setActiveNav] = useState("tokens");
  const [dialogOpen, setDialogOpen] = useState(false);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [itemName, setItemName] = useState("Spanish Latte");
  const [tenderMode, setTenderMode] = useState("cash");
  const [priceInput, setPriceInput] = useState("");
  const [submitted, setSubmitted] = useState(false);

  const priceError =
    submitted && (!priceInput.trim() || Number.isNaN(Number(priceInput)) || Number(priceInput) <= 0)
      ? "Enter a positive PHP unit price (e.g. 165.00)."
      : null;

  const swatches = [
    { name: "Espresso", hex: brandColors.espresso, textOn: brandColors.cream, usage: "Primary text, nav, primary CTA" },
    { name: "Cream", hex: brandColors.cream, textOn: brandColors.espresso, usage: "Main canvas, text on espresso" },
    { name: "Copper", hex: brandColors.copper, textOn: brandColors.espresso, usage: "Decorative accents & highlights" },
    { name: "Dark Copper", hex: brandColors.darkCopper, textOn: brandColors.cream, usage: "Links & accent buttons on cream" },
    { name: "Latte", hex: brandColors.latte, textOn: brandColors.espresso, usage: "Borders & soft surface fills" },
    { name: "Mocha", hex: brandColors.mocha, textOn: brandColors.cream, usage: "Secondary text on cream" },
  ];

  return (
    <CafeShell
      storeName="Salryn Cafe"
      stationLabel="UX-A Design System Catalog"
      connectionStatus={{ label: "Offline-First Ready", tone: "success" }}
      operatorName="Iris (Design Eng)"
      roleName="System Preview"
      navItems={NAV_ITEMS}
      activeNav={activeNav}
      onNavigate={setActiveNav}
      onSignOut={() => notify("info", "Sign-out confirmed", "Catalog preview remains active.")}
      banner={
        <Alert
          tone="info"
          title="Salryn Cafe Brand Foundation (Issue #10 UX-A)"
          description="Built directly from canonical assets/brand tokens (6b266249), WCAG 2.1 AA contrast verified, touch >= 44px, and keyboard focus-trapped."
        />
      }
    >
      <div style={{ display: "grid", gap: 20 }}>
        <Card>
          <CardHeader
            title="Brand Artwork & Aspect-Ratio Integrity"
            subtitle="Canonical PNGs from assets/brand rendered with object-fit: contain and light framing for transparent logo."
          />
          <CardBody>
            <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center", gap: 20 }}>
              <BrandMark variant="logo" height={56} />
              <BrandMark variant="icon" height={56} />
              <BrandMark variant="banner" height={80} />
            </div>
          </CardBody>
        </Card>

        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(210px, 1fr))", gap: 14 }}>
          <MetricCard label="Shift Cash Sales" value="₱18,450.00" detail="42 completed cafe tickets" />
          <MetricCard label="Open AR Balances" value="₱2,180.00" detail="3 active corporate tabs" />
          <MetricCard label="Kitchen Queue" value="3 Tickets" detail="Avg prep 4m 12s" />
        </div>

        <Card>
          <CardHeader
            title="Canonical Brand Palette & Live WCAG Contrast"
            subtitle="Espresso, Cream, Copper, Dark Copper, Latte, Mocha + warm semantic feedback tokens."
          />
          <CardBody>
            <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))", gap: 12 }}>
              {swatches.map((swatch) => {
                const ratio = getContrastRatio(swatch.textOn, swatch.hex);
                return (
                  <div
                    key={swatch.name}
                    style={{
                      borderRadius: 10,
                      border: "1px solid var(--salryn-border-strong)",
                      overflow: "hidden",
                      background: "var(--salryn-surface)",
                    }}
                  >
                    <div
                      style={{
                        background: swatch.hex,
                        color: swatch.textOn,
                        padding: "16px 14px",
                        fontWeight: 700,
                        display: "flex",
                        justifyContent: "space-between",
                      }}
                    >
                      <span>{swatch.name}</span>
                      <span className="salryn-tabular">{swatch.hex}</span>
                    </div>
                    <div style={{ padding: "10px 14px", fontSize: "0.8125rem", color: "var(--salryn-mocha)" }}>
                      <div>{swatch.usage}</div>
                      <div className="salryn-tabular" style={{ marginTop: 4, fontWeight: 700, color: "var(--salryn-espresso)" }}>
                        Contrast: {ratio}:1
                      </div>
                    </div>
                  </div>
                );
              })}
            </div>

            <div style={{ marginTop: 16, display: "flex", flexWrap: "wrap", gap: 8 }}>
              <StatusBadge tone="success">Synced · {getContrastRatio(semanticColors.success.fg, semanticColors.success.bg)}:1</StatusBadge>
              <StatusBadge tone="warning">Offline Lease · {getContrastRatio(semanticColors.warning.fg, semanticColors.warning.bg)}:1</StatusBadge>
              <StatusBadge tone="danger">Revoked Device · {getContrastRatio(semanticColors.danger.fg, semanticColors.danger.bg)}:1</StatusBadge>
              <StatusBadge tone="info">Kitchen Ready · {getContrastRatio(semanticColors.info.fg, semanticColors.info.bg)}:1</StatusBadge>
              <StatusBadge tone="copper">Brewing · {getContrastRatio(semanticColors.copper.fg, semanticColors.copper.bg)}:1</StatusBadge>
              <StatusBadge tone="neutral">Draft</StatusBadge>
            </div>
          </CardBody>
        </Card>

        <Card>
          <CardHeader
            title="Accessible Forms, Touch Targets & Modals"
            subtitle="Every control meets >= 44px touch target, programmatic <label htmlFor>, aria-invalid, and keyboard focus trapping."
          />
          <CardBody>
            <form
              onSubmit={(event) => {
                event.preventDefault();
                setSubmitted(true);
                if (priceInput.trim() && Number(priceInput) > 0) {
                  notify("success", "Menu item validated", `${itemName} saved at ₱${Number(priceInput).toFixed(2)}.`);
                }
              }}
              style={{ display: "grid", gap: 14, maxWidth: 520 }}
            >
              <FormField label="Menu Item Name" required hint="Displayed on cashier grid and kitchen ticket.">
                <TextInput value={itemName} onChange={(e) => setItemName(e.target.value)} />
              </FormField>

              <FormField label="Unit Price (PHP)" required error={priceError} hint="Server remains authoritative for tax/discount math.">
                <TextInput
                  inputMode="decimal"
                  placeholder="165.00"
                  value={priceInput}
                  onChange={(e) => setPriceInput(e.target.value)}
                />
              </FormField>

              <FormField label="Tender Mode (Salryn Cafe Policy)">
                <SelectInput value={tenderMode} onChange={(e) => setTenderMode(e.target.value)}>
                  <option value="cash">Cash Only</option>
                  <option value="ar">Accounts Receivable (AR)</option>
                  <option value="split">Partial Cash + AR</option>
                </SelectInput>
              </FormField>

              <div style={{ display: "flex", flexWrap: "wrap", gap: 10 }}>
                <Button type="submit" variant="primary" size="lg">
                  Validate Form
                </Button>
                <Button variant="accent" onClick={() => setDialogOpen(true)}>
                  Open Accessible Dialog
                </Button>
                <Button variant="secondary" onClick={() => setConfirmOpen(true)}>
                  Confirm Void
                </Button>
                <Button variant="ghost" onClick={() => notify("warning", "Printer queue paused", "Check Bluetooth kitchen printer.")}>
                  Test Toast
                </Button>
              </div>
            </form>
          </CardBody>
          <CardFooter>
            <span style={{ fontSize: "0.8125rem", color: "var(--salryn-mocha)" }}>
              Press Tab / Shift+Tab / Escape inside dialogs to verify keyboard trap &amp; focus restoration.
            </span>
          </CardFooter>
        </Card>

        <Card>
          <CardHeader title="Responsive Cafe Queue DataTable" subtitle="Tabular numbers, semantic scope='col', horizontal scroll safe at 1024×768." />
          <CardBody>
            <DataTable
              caption="Active Cafe Orders"
              rows={SAMPLE_ORDERS}
              getRowKey={(row) => row.id}
              columns={[
                { key: "ticket", header: "Ticket", tabular: true, render: (r) => <strong>{r.ticket}</strong> },
                { key: "channel", header: "Channel", render: (r) => r.channel },
                { key: "items", header: "Items", render: (r) => r.items },
                { key: "total", header: "Total", align: "right", tabular: true, render: (r) => r.totalPhp },
                {
                  key: "status",
                  header: "Status",
                  render: (r) => <StatusBadge tone={r.status}>{r.statusLabel}</StatusBadge>,
                },
              ]}
            />
          </CardBody>
        </Card>
      </div>

      <Dialog
        open={dialogOpen}
        title="Pair Kitchen Station"
        description="Press Escape or click Close to restore focus back to the trigger button."
        onClose={() => setDialogOpen(false)}
      >
        <FormField label="Station Join Code" hint="Issued by the Salryn Cafe Host.">
          <TextInput placeholder="CAFE-4821" />
        </FormField>
      </Dialog>

      <ConfirmDialog
        open={confirmOpen}
        title="Void Ticket #CY-1044?"
        description="This records an audited void event with a stable operation ID."
        confirmLabel="Void ticket"
        destructive
        onCancel={() => setConfirmOpen(false)}
        onConfirm={() => {
          setConfirmOpen(false);
          notify("danger", "Void recorded", "Ticket #CY-1044 marked void in preview.");
        }}
      />
    </CafeShell>
  );
}

export function CatalogApp() {
  return (
    <ErrorBoundary>
      <ToastProvider>
        <CatalogContent />
      </ToastProvider>
    </ErrorBoundary>
  );
}
