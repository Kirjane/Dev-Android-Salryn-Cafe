import { useCallback, useEffect, useMemo, useState } from "react";
import {
  Building2,
  CheckCircle2,
  Copy,
  Download,
  FileKey2,
  History,
  LayoutDashboard,
  RefreshCw,
  ShieldAlert,
  ShieldCheck,
} from "lucide-react";
import {
  Alert,
  BrandMark,
  Button,
  CafeShell,
  Card,
  CardBody,
  CardHeader,
  EmptyState,
  FormField,
  LoadingState,
  MetricCard,
  SelectInput,
  StatusBadge,
  TextInput,
  TextareaInput,
  type CafeShellNavItem,
  type StatusTone,
} from "@salryn/cafe-ui";
import { createBrowserIssuerApi, type LicensingIssuerApi } from "./api/issuerClient";
import type {
  IssuerAuditEntry,
  IssuerCustomerRecord,
  IssuerDashboardSummary,
  IssuerLicenseRequestRecord,
  IssuerMetadata,
  IssuerSessionInfo,
  IssuerStoreRecord,
  SignerStatusSnapshot,
} from "./types";

type NavId = "dashboard" | "customers" | "draft" | "requests" | "audit";

function statusTone(status: string): StatusTone {
  switch (status) {
    case "Issued":
      return "success";
    case "Approved":
      return "info";
    case "AwaitingApproval":
    case "Draft":
      return "warning";
    case "Superseded":
      return "neutral";
    case "Rejected":
      return "danger";
    default:
      return "neutral";
  }
}

function signerBanner(signer: SignerStatusSnapshot) {
  if (signer.matchesTrustedRuntimeKey && signer.canIssue) {
    return (
      <Alert
        tone="success"
        title="Authorized Offline Signer Ready"
        description={`Server signing key matches trusted Salryn runtime SPKI ${signer.publicKeySpkiSha256}. Private keys remain strictly server-side.`}
      />
    );
  }
  if (signer.signerMode === "TestUntrustedKey" && signer.canIssue) {
    return (
      <Alert
        tone="warning"
        title="Non-Production Test Signer Active (TestUntrustedKey)"
        description={`${signer.message} Active SPKI: ${signer.publicKeySpkiSha256}`}
      />
    );
  }
  return (
    <Alert
      tone="danger"
      title={`Signing Blocked — ${signer.signerMode}`}
      description={`${signer.message} Expected runtime SPKI: ${signer.trustedRuntimeSpkiSha256}`}
    />
  );
}

export function IssuerApp({ api: providedApi }: { api?: LicensingIssuerApi }) {
  const api = useMemo(() => providedApi ?? createBrowserIssuerApi(), [providedApi]);

  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const [session, setSession] = useState<IssuerSessionInfo>({
    isAuthenticated: false,
    csrfToken: null,
    user: null,
  });
  const [activeNav, setActiveNav] = useState<NavId>("dashboard");

  // Login form state
  const [loginUsername, setLoginUsername] = useState("");
  const [loginPassword, setLoginPassword] = useState("");

  // Live issuer data
  const [dashboard, setDashboard] = useState<IssuerDashboardSummary | null>(null);
  const [metadata, setMetadata] = useState<IssuerMetadata | null>(null);
  const [customers, setCustomers] = useState<IssuerCustomerRecord[]>([]);
  const [stores, setStores] = useState<IssuerStoreRecord[]>([]);
  const [requests, setRequests] = useState<IssuerLicenseRequestRecord[]>([]);
  const [auditLog, setAuditLog] = useState<IssuerAuditEntry[]>([]);
  const [selectedRequestId, setSelectedRequestId] = useState<string | null>(null);
  const [draftWarnings, setDraftWarnings] = useState<string[]>([]);

  // Customer & store creation state
  const [customerName, setCustomerName] = useState("");
  const [customerEmail, setCustomerEmail] = useState("");
  const [customerNotes, setCustomerNotes] = useState("");

  const [storeCustomerId, setStoreCustomerId] = useState("");
  const [storeCode, setStoreCode] = useState("");
  const [storeName, setStoreName] = useState("");
  const [storeProductId, setStoreProductId] = useState("Salryn.Cafe");

  // License draft creation state
  const [draftCustomerId, setDraftCustomerId] = useState("");
  const [draftStoreId, setDraftStoreId] = useState("");
  const [draftLicenseType, setDraftLicenseType] = useState("Host");
  const [draftEdition, setDraftEdition] = useState("Professional");
  const [draftMachineBinding, setDraftMachineBinding] = useState("");
  const [draftAllowedLan, setDraftAllowedLan] = useState("2");
  const [draftHostLicenseId, setDraftHostLicenseId] = useState("");
  const [draftTerminalId, setDraftTerminalId] = useState("");
  const [draftExpiresDate, setDraftExpiresDate] = useState(() => {
    const d = new Date();
    d.setFullYear(d.getFullYear() + 1);
    return d.toISOString().slice(0, 10);
  });
  const [draftPerpetual, setDraftPerpetual] = useState(false);
  const [draftFeatures, setDraftFeatures] = useState<string[]>([
    "Pos",
    "Rmo",
    "Reports",
    "Users",
    "Backup",
    "Licensing",
    "LanTerminals",
    "ReceivablesUtangLedger",
  ]);

  // Review / Reissue state
  const [reviewNote, setReviewNote] = useState("");
  const [reissueMachine, setReissueMachine] = useState("");
  const [reissueNote, setReissueNote] = useState("");

  const refreshAll = useCallback(async () => {
    const [dash, meta, custs, strs, reqs, audits] = await Promise.all([
      api.getDashboard(),
      api.getMetadata(),
      api.listCustomers(),
      api.listStores(),
      api.listRequests(),
      api.listAudit(100).catch(() => []),
    ]);
    setDashboard(dash);
    setMetadata(meta);
    setCustomers(custs);
    setStores(strs);
    setRequests(reqs);
    setAuditLog(audits);
    if (custs.length > 0 && !storeCustomerId) {
      setStoreCustomerId(custs[0].id);
    }
    if (custs.length > 0 && !draftCustomerId) {
      setDraftCustomerId(custs[0].id);
    }
    if (reqs.length > 0 && !selectedRequestId) {
      setSelectedRequestId(reqs[0].id);
    }
  }, [api, draftCustomerId, selectedRequestId, storeCustomerId]);

  useEffect(() => {
    let active = true;
    (async () => {
      try {
        const s = await api.getSession();
        if (!active) return;
        setSession(s);
        if (s.isAuthenticated) {
          await refreshAll();
        }
      } catch (err) {
        if (active) setError(err instanceof Error ? err.message : "Failed to contact Licensing Issuer API.");
      } finally {
        if (active) setLoading(false);
      }
    })();
    return () => {
      active = false;
    };
  }, [api, refreshAll]);

  const storesForDraftCustomer = useMemo(
    () => stores.filter((s) => s.customerId === draftCustomerId),
    [stores, draftCustomerId],
  );

  const selectedDraftStore = useMemo(
    () => storesForDraftCustomer.find((s) => s.id === draftStoreId) ?? storesForDraftCustomer[0] ?? null,
    [storesForDraftCustomer, draftStoreId],
  );

  useEffect(() => {
    if (selectedDraftStore && selectedDraftStore.id !== draftStoreId) {
      setDraftStoreId(selectedDraftStore.id);
    }
  }, [selectedDraftStore, draftStoreId]);

  const selectedRequest = useMemo(
    () => requests.find((r) => r.id === selectedRequestId) ?? requests[0] ?? null,
    [requests, selectedRequestId],
  );

  async function runAction(fn: () => Promise<void>) {
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      await fn();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Operation failed.");
    } finally {
      setBusy(false);
    }
  }

  async function handleLogin(e: React.FormEvent) {
    e.preventDefault();
    await runAction(async () => {
      const s = await api.login(loginUsername.trim(), loginPassword);
      setLoginPassword("");
      setSession(s);
      await refreshAll();
      setNotice("Signed in to Kirjane Labs Licensing Issuer.");
    });
  }

  async function handleSignOut() {
    await runAction(async () => {
      await api.logout();
      setSession({ isAuthenticated: false, csrfToken: null, user: null });
      setDashboard(null);
      setRequests([]);
      setNotice("Signed out.");
    });
  }

  async function handleCreateCustomer(e: React.FormEvent) {
    e.preventDefault();
    await runAction(async () => {
      const created = await api.createCustomer({
        customerName: customerName.trim(),
        contactEmail: customerEmail.trim() || undefined,
        notes: customerNotes.trim() || undefined,
      });
      setCustomerName("");
      setCustomerEmail("");
      setCustomerNotes("");
      setStoreCustomerId(created.id);
      setDraftCustomerId(created.id);
      await refreshAll();
      setNotice(`Registered customer "${created.customerName}".`);
    });
  }

  async function handleCreateStore(e: React.FormEvent) {
    e.preventDefault();
    await runAction(async () => {
      const created = await api.createStore({
        customerId: storeCustomerId,
        storeCode: storeCode.trim(),
        storeName: storeName.trim(),
        productId: storeProductId,
      });
      setStoreCode("");
      setStoreName("");
      setDraftCustomerId(created.customerId);
      setDraftStoreId(created.id);
      await refreshAll();
      setNotice(`Registered store "${created.storeCode}" (${created.productId}).`);
    });
  }

  function toggleFeature(feature: string) {
    setDraftFeatures((prev) =>
      prev.includes(feature) ? prev.filter((f) => f !== feature) : [...prev, feature],
    );
  }

  function handleLicenseTypeChange(nextType: string) {
    setDraftLicenseType(nextType);
    if (nextType === "Terminal") {
      setDraftFeatures(["LanTerminals"]);
    } else if (metadata) {
      setDraftFeatures([...metadata.allFeatures]);
    }
  }

  async function handleCreateDraft(e: React.FormEvent) {
    e.preventDefault();
    if (!selectedDraftStore) {
      setError("Register and select a customer store first.");
      return;
    }
    await runAction(async () => {
      const res = await api.createDraft({
        customerId: selectedDraftStore.customerId,
        storeId: selectedDraftStore.id,
        applicationId: selectedDraftStore.productId,
        licenseType: draftLicenseType,
        edition: draftEdition,
        machineBinding: draftMachineBinding.trim(),
        features: draftFeatures,
        allowedLanTerminals: draftLicenseType === "Host" ? Number(draftAllowedLan || 0) : null,
        hostLicenseId: draftLicenseType === "Terminal" ? draftHostLicenseId.trim() : null,
        terminalId: draftLicenseType === "Terminal" ? draftTerminalId.trim() : null,
        expiresAtUtc: draftPerpetual || !draftExpiresDate ? null : new Date(`${draftExpiresDate}T23:59:59Z`).toISOString(),
        supportExpiresAtUtc: null,
        updatesUntilUtc: null,
      });
      setDraftWarnings(res.warnings);
      setSelectedRequestId(res.request.id);
      setDraftMachineBinding("");
      await refreshAll();
      setActiveNav("requests");
      setNotice(`Created license draft ${res.request.licenseId} (Awaiting Approval).`);
    });
  }

  async function handleCopyJson(jsonText: string) {
    try {
      await navigator.clipboard.writeText(jsonText);
      setNotice("Signed offline license JSON copied to clipboard.");
    } catch {
      setError("Clipboard copy unavailable in this browser context; use Download Signed JSON instead.");
    }
  }

  if (loading) {
    return (
      <div className="issuer-login-wrap">
        <LoadingState label="Connecting to Kirjane Labs Licensing Issuer..." />
      </div>
    );
  }

  if (!session.isAuthenticated || !session.user) {
    return (
      <main className="issuer-login-wrap">
        <Card className="issuer-login-card">
          <CardHeader
            title="Kirjane Labs Licensing Issuer"
            subtitle="Standalone offline license administration (isolated from Salryn Cafe POS)"
            action={<BrandMark variant="icon" height={36} />}
          />
          <CardBody>
            {error ? (
              <div style={{ marginBottom: 12 }}>
                <Alert tone="danger" title="Authentication Error" description={error} />
              </div>
            ) : null}
            {notice ? (
              <div style={{ marginBottom: 12 }}>
                <Alert tone="info" title="Notice" description={notice} />
              </div>
            ) : null}
            <form onSubmit={handleLogin} className="issuer-grid">
              <FormField id="issuer-user" label="Administrator Username" required>
                <TextInput
                  id="issuer-user"
                  autoComplete="username"
                  value={loginUsername}
                  onChange={(e) => setLoginUsername(e.target.value)}
                  required
                />
              </FormField>
              <FormField id="issuer-pass" label="Password" required>
                <TextInput
                  id="issuer-pass"
                  type="password"
                  autoComplete="current-password"
                  value={loginPassword}
                  onChange={(e) => setLoginPassword(e.target.value)}
                  required
                />
              </FormField>
              <Button type="submit" disabled={busy}>
                {busy ? "Signing in..." : "Sign In to Licensing Issuer"}
              </Button>
            </form>
            <p style={{ marginTop: 14, fontSize: "0.8rem", color: "#735447" }}>
              Private RSA signing keys are never stored in browser storage, Git, or SQLite. Issuance fails closed
              unless an authorized server-side key is configured.
            </p>
          </CardBody>
        </Card>
      </main>
    );
  }

  const navItems: CafeShellNavItem[] = [
    { id: "dashboard", label: "Overview", group: "Licensing", Icon: LayoutDashboard },
    { id: "customers", label: "Customers & Stores", group: "Licensing", Icon: Building2, badge: stores.length },
    { id: "draft", label: "New License Draft", group: "Issuance", Icon: FileKey2 },
    {
      id: "requests",
      label: "Review & History",
      group: "Issuance",
      Icon: History,
      badge: dashboard ? dashboard.awaitingApprovalCount + dashboard.approvedCount : undefined,
    },
    { id: "audit", label: "Audit Log", group: "Governance", Icon: ShieldCheck },
  ];

  const connectionBadge = dashboard?.signer.canIssue
    ? dashboard.signer.matchesTrustedRuntimeKey
      ? { label: "Trusted Signer Ready", tone: "success" as const }
      : { label: "Test Signer (Non-Prod)", tone: "warning" as const }
    : { label: "Signer Unconfigured", tone: "danger" as const };

  return (
    <CafeShell
      storeName="Kirjane Labs"
      stationLabel="Standalone Licensing Issuer"
      connectionStatus={connectionBadge}
      operatorName={session.user.displayName}
      roleName={session.user.role}
      navItems={navItems}
      activeNav={activeNav}
      onNavigate={(id) => setActiveNav(id as NavId)}
      onSignOut={() => void handleSignOut()}
      banner={dashboard ? signerBanner(dashboard.signer) : undefined}
    >
      <div className="issuer-grid">
        <div className="issuer-actions" style={{ justifyContent: "space-between" }}>
          <h1 style={{ margin: 0, fontSize: "1.35rem", color: "#3B1204" }}>
            {navItems.find((n) => n.id === activeNav)?.label}
          </h1>
          <Button
            variant="secondary"
            size="sm"
            leftIcon={<RefreshCw size={14} />}
            disabled={busy}
            onClick={() => void runAction(refreshAll)}
          >
            Refresh
          </Button>
        </div>

        {error ? <Alert tone="danger" title="Action Error" description={error} /> : null}
        {notice ? <Alert tone="success" title="Confirmed" description={notice} /> : null}

        {activeNav === "dashboard" && dashboard ? (
          <div className="issuer-grid">
            <div className="issuer-metrics">
              <MetricCard label="Customers" value={String(dashboard.customerCount)} />
              <MetricCard label="Registered Stores" value={String(dashboard.storeCount)} />
              <MetricCard label="Awaiting Approval" value={String(dashboard.awaitingApprovalCount)} />
              <MetricCard label="Approved (Ready to Sign)" value={String(dashboard.approvedCount)} />
              <MetricCard label="Issued Licenses" value={String(dashboard.issuedCount)} />
              <MetricCard label="Superseded" value={String(dashboard.supersededCount)} />
              <MetricCard label="Expired Issued" value={String(dashboard.expiredIssuedCount)} />
              <MetricCard label="Rejected" value={String(dashboard.rejectedCount)} />
            </div>

            <Card>
              <CardHeader title="Key Custody & Offline Verification Boundary" />
              <CardBody>
                <p style={{ marginTop: 0 }}>
                  Mode: <strong>{dashboard.signer.signerMode}</strong> · Can Issue:{" "}
                  <strong>{dashboard.signer.canIssue ? "Yes" : "No (Fail-Closed)"}</strong>
                </p>
                <p>
                  Trusted Salryn Runtime SPKI SHA-256:{" "}
                  <span className="issuer-mono">{dashboard.signer.trustedRuntimeSpkiSha256}</span>
                </p>
                <p style={{ marginBottom: 0, color: "#735447" }}>
                  Offline licenses generated here are verified offline by Salryn Cafe using RSA-2048 SHA-256 PKCS#1 v1.5
                  against the embedded public key. Once distributed, offline licenses cannot be remotely revoked without
                  a future runtime revocation mechanism; use Reissue to supersede records in audit history.
                </p>
              </CardBody>
            </Card>
          </div>
        ) : null}

        {activeNav === "customers" ? (
          <div className="issuer-two-col">
            <Card>
              <CardHeader title="Register Customer" />
              <CardBody>
                <form onSubmit={handleCreateCustomer} className="issuer-grid">
                  <FormField id="cust-name" label="Customer / Organization Name" required>
                    <TextInput
                      id="cust-name"
                      value={customerName}
                      onChange={(e) => setCustomerName(e.target.value)}
                      placeholder="Cafe Yza Holdings"
                      required
                    />
                  </FormField>
                  <FormField id="cust-email" label="Contact Email">
                    <TextInput
                      id="cust-email"
                      type="email"
                      value={customerEmail}
                      onChange={(e) => setCustomerEmail(e.target.value)}
                      placeholder="owner@cafeyza.ph"
                    />
                  </FormField>
                  <FormField id="cust-notes" label="Notes">
                    <TextareaInput
                      id="cust-notes"
                      rows={2}
                      value={customerNotes}
                      onChange={(e) => setCustomerNotes(e.target.value)}
                    />
                  </FormField>
                  <Button type="submit" disabled={busy}>
                    Register Customer
                  </Button>
                </form>
              </CardBody>
            </Card>

            <Card>
              <CardHeader title="Register Store" />
              <CardBody>
                <form onSubmit={handleCreateStore} className="issuer-grid">
                  <FormField id="store-cust" label="Customer" required>
                    <SelectInput
                      id="store-cust"
                      value={storeCustomerId}
                      onChange={(e) => setStoreCustomerId(e.target.value)}
                      required
                    >
                      <option value="">Select customer...</option>
                      {customers.map((c) => (
                        <option key={c.id} value={c.id}>
                          {c.customerName}
                        </option>
                      ))}
                    </SelectInput>
                  </FormField>
                  <FormField id="store-code" label="Store Code (unique)" required>
                    <TextInput
                      id="store-code"
                      value={storeCode}
                      onChange={(e) => setStoreCode(e.target.value)}
                      placeholder="CAFE-YZA-01"
                      required
                    />
                  </FormField>
                  <FormField id="store-name" label="Store Display Name" required>
                    <TextInput
                      id="store-name"
                      value={storeName}
                      onChange={(e) => setStoreName(e.target.value)}
                      placeholder="Cafe Yza Main Branch"
                      required
                    />
                  </FormField>
                  <FormField id="store-prod" label="Authorized Product" required>
                    <SelectInput
                      id="store-prod"
                      value={storeProductId}
                      onChange={(e) => setStoreProductId(e.target.value)}
                    >
                      {(metadata?.products ?? ["Salryn.Cafe", "Salryn.POS"]).map((p) => (
                        <option key={p} value={p}>
                          {p}
                        </option>
                      ))}
                    </SelectInput>
                  </FormField>
                  <Button type="submit" disabled={busy || customers.length === 0}>
                    Register Store
                  </Button>
                </form>
              </CardBody>
            </Card>

            <Card>
              <CardHeader title="Registered Customers & Stores" />
              <CardBody>
                {stores.length === 0 ? (
                  <EmptyState
                    title="No stores registered yet"
                    description="Register a customer and store to prepare machine-bound offline licenses."
                  />
                ) : (
                  <div className="issuer-table-wrap">
                    <table className="issuer-table">
                      <thead>
                        <tr>
                          <th>Store Code</th>
                          <th>Store Name</th>
                          <th>Product</th>
                          <th>Customer</th>
                        </tr>
                      </thead>
                      <tbody>
                        {stores.map((s) => {
                          const cust = customers.find((c) => c.id === s.customerId);
                          return (
                            <tr key={s.id}>
                              <td className="issuer-mono">{s.storeCode}</td>
                              <td>{s.storeName}</td>
                              <td>
                                <StatusBadge tone={s.productId === "Salryn.Cafe" ? "success" : "info"}>
                                  {s.productId}
                                </StatusBadge>
                              </td>
                              <td>{cust?.customerName ?? s.customerId}</td>
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                  </div>
                )}
              </CardBody>
            </Card>
          </div>
        ) : null}

        {activeNav === "draft" ? (
          <Card>
            <CardHeader title="Create Machine-Bound License Draft" />
            <CardBody>
              {stores.length === 0 ? (
                <Alert
                  tone="warning"
                  title="Register a Store First"
                  description="Switch to Customers & Stores to register at least one customer and store before drafting a license."
                />
              ) : (
                <form onSubmit={handleCreateDraft} className="issuer-grid">
                  <div className="issuer-two-col">
                    <FormField id="draft-cust" label="Customer" required>
                      <SelectInput
                        id="draft-cust"
                        value={draftCustomerId}
                        onChange={(e) => setDraftCustomerId(e.target.value)}
                      >
                        {customers.map((c) => (
                          <option key={c.id} value={c.id}>
                            {c.customerName}
                          </option>
                        ))}
                      </SelectInput>
                    </FormField>

                    <FormField id="draft-store" label="Store (locks Product ID)" required>
                      <SelectInput
                        id="draft-store"
                        value={selectedDraftStore?.id ?? ""}
                        onChange={(e) => setDraftStoreId(e.target.value)}
                      >
                        {storesForDraftCustomer.map((s) => (
                          <option key={s.id} value={s.id}>
                            {s.storeCode} — {s.storeName} ({s.productId})
                          </option>
                        ))}
                      </SelectInput>
                    </FormField>
                  </div>

                  <FormField
                    id="draft-machine"
                    label="Customer Workstation Machine Code"
                    hint="Copied directly from the Salryn Cafe Activation screen (e.g. SALRYN-MC-F36B-76CD-77FB-03EC)."
                    required
                  >
                    <TextInput
                      id="draft-machine"
                      value={draftMachineBinding}
                      onChange={(e) => setDraftMachineBinding(e.target.value)}
                      placeholder="SALRYN-MC-XXXX-XXXX-XXXX-XXXX"
                      required
                    />
                  </FormField>

                  <div className="issuer-two-col">
                    <FormField id="draft-type" label="License Type" required>
                      <SelectInput
                        id="draft-type"
                        value={draftLicenseType}
                        onChange={(e) => handleLicenseTypeChange(e.target.value)}
                      >
                        <option value="Host">Host (Windows Local Host)</option>
                        <option value="Terminal">Terminal (Paired LAN Device)</option>
                      </SelectInput>
                    </FormField>

                    <FormField id="draft-edition" label="Edition" required>
                      <SelectInput
                        id="draft-edition"
                        value={draftEdition}
                        onChange={(e) => setDraftEdition(e.target.value)}
                      >
                        {(metadata?.editions ?? ["Trial", "Local", "Professional", "Enterprise"]).map((ed) => (
                          <option key={ed} value={ed}>
                            {ed}
                          </option>
                        ))}
                      </SelectInput>
                    </FormField>
                  </div>

                  {draftLicenseType === "Host" ? (
                    <FormField id="draft-lan" label="Allowed LAN Terminals (0–100)" required>
                      <TextInput
                        id="draft-lan"
                        type="number"
                        min={0}
                        max={100}
                        value={draftAllowedLan}
                        onChange={(e) => setDraftAllowedLan(e.target.value)}
                      />
                    </FormField>
                  ) : (
                    <div className="issuer-two-col">
                      <FormField id="draft-host-id" label="Parent Host License ID" required>
                        <TextInput
                          id="draft-host-id"
                          value={draftHostLicenseId}
                          onChange={(e) => setDraftHostLicenseId(e.target.value)}
                          placeholder="SLRN-CAFE-20261123-XXXXXXXX"
                          required
                        />
                      </FormField>
                      <FormField id="draft-term-id" label="Terminal ID" required>
                        <TextInput
                          id="draft-term-id"
                          value={draftTerminalId}
                          onChange={(e) => setDraftTerminalId(e.target.value)}
                          placeholder="TERM-CASHIER-01"
                          required
                        />
                      </FormField>
                    </div>
                  )}

                  <div className="issuer-two-col">
                    <FormField id="draft-exp" label="Expiry Date (UTC)">
                      <TextInput
                        id="draft-exp"
                        type="date"
                        disabled={draftPerpetual}
                        value={draftExpiresDate}
                        onChange={(e) => setDraftExpiresDate(e.target.value)}
                      />
                    </FormField>
                    <label className="issuer-checkbox-label" style={{ marginTop: 26 }}>
                      <input
                        type="checkbox"
                        checked={draftPerpetual}
                        onChange={(e) => setDraftPerpetual(e.target.checked)}
                      />
                      Perpetual license (no expiry date)
                    </label>
                  </div>

                  <div>
                    <div style={{ fontWeight: 600, marginBottom: 8 }}>Enabled Features</div>
                    <div className="issuer-feature-grid">
                      {(metadata?.allFeatures ?? draftFeatures).map((feat) => {
                        const disabled = draftLicenseType === "Terminal" && feat !== "LanTerminals";
                        return (
                          <label key={feat} className="issuer-checkbox-label" style={{ opacity: disabled ? 0.5 : 1 }}>
                            <input
                              type="checkbox"
                              checked={draftFeatures.includes(feat)}
                              disabled={disabled}
                              onChange={() => toggleFeature(feat)}
                            />
                            <span>{feat}</span>
                          </label>
                        );
                      })}
                    </div>
                  </div>

                  <div className="issuer-actions">
                    <Button type="submit" disabled={busy}>
                      Create Draft for Review
                    </Button>
                  </div>
                </form>
              )}
            </CardBody>
          </Card>
        ) : null}

        {activeNav === "requests" ? (
          <div className="issuer-two-col">
            <Card>
              <CardHeader title={`Issuance & Reissue History (${requests.length})`} />
              <CardBody>
                {requests.length === 0 ? (
                  <EmptyState
                    title="No license requests yet"
                    description="Create a license draft to begin review and offline signing."
                  />
                ) : (
                  <div className="issuer-table-wrap">
                    <table className="issuer-table">
                      <thead>
                        <tr>
                          <th>License ID</th>
                          <th>Store / Product</th>
                          <th>Type</th>
                          <th>Status</th>
                        </tr>
                      </thead>
                      <tbody>
                        {requests.map((r) => (
                          <tr
                            key={r.id}
                            className={selectedRequest?.id === r.id ? "issuer-row-selected" : undefined}
                            style={{ cursor: "pointer" }}
                            onClick={() => setSelectedRequestId(r.id)}
                          >
                            <td className="issuer-mono">{r.licenseId}</td>
                            <td>
                              <div>{r.storeCode}</div>
                              <div style={{ fontSize: "0.8rem", color: "#735447" }}>{r.applicationId}</div>
                            </td>
                            <td>{r.licenseType}</td>
                            <td>
                              <StatusBadge tone={statusTone(r.status)}>{r.status}</StatusBadge>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </CardBody>
            </Card>

            <Card>
              <CardHeader
                title={selectedRequest ? `Review & Issue · ${selectedRequest.licenseId}` : "Select a Request"}
              />
              <CardBody>
                {!selectedRequest ? (
                  <EmptyState title="No request selected" description="Select a license request from the table." />
                ) : (
                  <div className="issuer-grid">
                    {draftWarnings.length > 0 ? (
                      <Alert tone="warning" title="Policy Notices" description={draftWarnings.join(" ")} />
                    ) : null}

                    <div style={{ display: "flex", gap: 8, flexWrap: "wrap", alignItems: "center" }}>
                      <StatusBadge tone={statusTone(selectedRequest.status)}>{selectedRequest.status}</StatusBadge>
                      <StatusBadge tone="info">{selectedRequest.applicationId}</StatusBadge>
                      <StatusBadge tone="neutral">{`${selectedRequest.licenseType} · ${selectedRequest.edition}`}</StatusBadge>
                    </div>

                    <div>
                      <strong>Customer / Store:</strong> {selectedRequest.customerName} — {selectedRequest.storeName} (
                      <span className="issuer-mono">{selectedRequest.storeCode}</span>)
                    </div>
                    <div>
                      <strong>Machine Binding:</strong>{" "}
                      <span className="issuer-mono">{selectedRequest.machineBinding}</span>
                    </div>
                    <div>
                      <strong>Expires:</strong>{" "}
                      {selectedRequest.expiresAtUtc ? new Date(selectedRequest.expiresAtUtc).toISOString() : "Perpetual"}
                    </div>
                    <div>
                      <strong>Audit Lineage:</strong> Created by {selectedRequest.createdBy}
                      {selectedRequest.approvedBy ? ` · Approved by ${selectedRequest.approvedBy}` : ""}
                      {selectedRequest.issuedBy ? ` · Issued by ${selectedRequest.issuedBy}` : ""}
                    </div>

                    <div>
                      <div style={{ fontWeight: 600, marginBottom: 4 }}>Canonical Signing Payload</div>
                      <pre className="issuer-code-box">{selectedRequest.canonicalSigningPayload}</pre>
                    </div>

                    {selectedRequest.status === "AwaitingApproval" || selectedRequest.status === "Draft" ? (
                      <div className="issuer-grid">
                        {selectedRequest.createdBy.toLowerCase() === session.user.username.toLowerCase() &&
                        metadata?.independentApprovalEnforced !== false ? (
                          <Alert
                            tone="warning"
                            title="Two-Person Rule Enforced"
                            description={`This draft was created by ${selectedRequest.createdBy}. Independent approval by a separate IssuerReviewer or IssuerAdmin account is required before issuance.`}
                          />
                        ) : null}
                        <FormField id="review-note" label="Review / Authorization Note">
                          <TextInput
                            id="review-note"
                            value={reviewNote}
                            onChange={(e) => setReviewNote(e.target.value)}
                            placeholder="Verified machine code and customer purchase record"
                          />
                        </FormField>
                        <div className="issuer-actions">
                          <Button
                            leftIcon={<CheckCircle2 size={15} />}
                            disabled={busy}
                            onClick={() =>
                              void runAction(async () => {
                                await api.approveRequest(selectedRequest.id, reviewNote);
                                setReviewNote("");
                                await refreshAll();
                                setNotice(`Approved ${selectedRequest.licenseId}. Ready for signing.`);
                              })
                            }
                          >
                            Approve for Issuance
                          </Button>
                          <Button
                            variant="danger"
                            disabled={busy}
                            onClick={() =>
                              void runAction(async () => {
                                await api.rejectRequest(selectedRequest.id, reviewNote || "Rejected by reviewer");
                                setReviewNote("");
                                await refreshAll();
                                setNotice(`Rejected ${selectedRequest.licenseId}.`);
                              })
                            }
                          >
                            Reject
                          </Button>
                        </div>
                      </div>
                    ) : null}

                    {selectedRequest.status === "Approved" ? (
                      <div className="issuer-grid">
                        <Alert
                          tone={dashboard?.signer.canIssue ? "info" : "danger"}
                          title="Offline Issuance Authorization"
                          description={
                            dashboard?.signer.canIssue
                              ? "Server signer is ready. Issuing runs RSA-SHA256 signing and pre-release runtime verification before returning the file."
                              : "Server signer is not configured (SIGNER_NOT_CONFIGURED). Clicking Issue will fail closed cleanly and record an audit event."
                          }
                        />
                        <div className="issuer-actions">
                          <Button
                            leftIcon={<ShieldAlert size={15} />}
                            disabled={busy}
                            onClick={() =>
                              void runAction(async () => {
                                const res = await api.issueRequest(selectedRequest.id);
                                await refreshAll();
                                setNotice(
                                  res.alreadyIssued
                                    ? `${res.request.licenseId} was already issued (idempotent replay).`
                                    : `Signed & verified ${res.request.licenseId} (${res.request.signerMode}).`,
                                );
                              })
                            }
                          >
                            Issue &amp; Sign Offline License
                          </Button>
                        </div>
                      </div>
                    ) : null}

                    {(selectedRequest.status === "Issued" || selectedRequest.status === "Superseded") &&
                    selectedRequest.signedLicenseJson ? (
                      <div className="issuer-grid">
                        <Alert
                          tone={selectedRequest.signerMode === "ProductionTrustedSigner" ? "success" : "warning"}
                          title={`Signed Artifact (${selectedRequest.signerMode ?? "Signed"})`}
                          description={`SHA-256: ${selectedRequest.signedLicenseSha256 ?? ""} · Signer SPKI: ${selectedRequest.signerKeySpkiSha256 ?? ""}`}
                        />

                        <div className="issuer-actions">
                          <a
                            href={api.downloadUrl(selectedRequest.id)}
                            download={`${selectedRequest.licenseId}.license.json`}
                            style={{ textDecoration: "none" }}
                          >
                            <Button type="button" leftIcon={<Download size={15} />}>
                              Download Signed JSON
                            </Button>
                          </a>
                          <Button
                            type="button"
                            variant="secondary"
                            leftIcon={<Copy size={15} />}
                            onClick={() => void handleCopyJson(selectedRequest.signedLicenseJson!)}
                          >
                            Copy Signed JSON
                          </Button>
                        </div>

                        <pre className="issuer-code-box">{selectedRequest.signedLicenseJson}</pre>

                        <Card>
                          <CardHeader title="Reissue License (Supersedes Prior Record)" />
                          <CardBody>
                            <p style={{ marginTop: 0, fontSize: "0.85rem", color: "#735447" }}>
                              Creating and issuing a reissue marks this record <strong>Superseded</strong> in history.
                              Offline files already copied to a customer PC cannot be remotely revoked.
                            </p>
                            <div className="issuer-grid">
                              <FormField id="reissue-mc" label="Replacement Machine Code (optional)">
                                <TextInput
                                  id="reissue-mc"
                                  value={reissueMachine}
                                  onChange={(e) => setReissueMachine(e.target.value)}
                                  placeholder={selectedRequest.machineBinding}
                                />
                              </FormField>
                              <FormField id="reissue-note" label="Reissue Reason">
                                <TextInput
                                  id="reissue-note"
                                  value={reissueNote}
                                  onChange={(e) => setReissueNote(e.target.value)}
                                  placeholder="Replacement workstation hardware"
                                />
                              </FormField>
                              <Button
                                type="button"
                                variant="secondary"
                                disabled={busy}
                                onClick={() =>
                                  void runAction(async () => {
                                    const created = await api.reissueRequest(selectedRequest.id, {
                                      machineBinding: reissueMachine.trim() || undefined,
                                      note: reissueNote.trim() || undefined,
                                    });
                                    setReissueMachine("");
                                    setReissueNote("");
                                    setSelectedRequestId(created.request.id);
                                    await refreshAll();
                                    setNotice(`Created reissue draft ${created.request.licenseId}.`);
                                  })
                                }
                              >
                                Create Reissue Draft
                              </Button>
                            </div>
                          </CardBody>
                        </Card>
                      </div>
                    ) : null}
                  </div>
                )}
              </CardBody>
            </Card>
          </div>
        ) : null}

        {activeNav === "audit" ? (
          <Card>
            <CardHeader title="Append-Only Issuance Audit Trail" />
            <CardBody>
              {auditLog.length === 0 ? (
                <EmptyState title="No audit events" description="Actions taken in the issuer will appear here." />
              ) : (
                <div className="issuer-table-wrap">
                  <table className="issuer-table">
                    <thead>
                      <tr>
                        <th>Time (UTC)</th>
                        <th>Actor</th>
                        <th>Action</th>
                        <th>Entity</th>
                        <th>Details</th>
                      </tr>
                    </thead>
                    <tbody>
                      {auditLog.map((entry) => (
                        <tr key={entry.id}>
                          <td className="issuer-mono">{entry.createdAtUtc}</td>
                          <td>
                            {entry.actorUsername} ({entry.actorRole})
                          </td>
                          <td>
                            <strong>{entry.action}</strong>
                          </td>
                          <td className="issuer-mono">
                            {entry.entityType} · {entry.entityId.slice(0, 8)}
                          </td>
                          <td className="issuer-mono">{entry.detailJson}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </CardBody>
          </Card>
        ) : null}
      </div>
    </CafeShell>
  );
}
