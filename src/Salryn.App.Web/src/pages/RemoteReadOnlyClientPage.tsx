import {
  Alert,
  Button,
  Card,
  CardBody,
  CardHeader,
  CardTitle,
  CommandBar,
  ErrorState,
  LoadingState,
  StatusPill,
  TextInput,
} from "../components/ui";
import { remoteReadOnlyWarnings } from "../api/remoteReadOnlyClient";
import { useRemoteReadOnlyClient } from "../hooks/useRemoteReadOnlyClient";

export function RemoteReadOnlyClientPage() {
  const {
    shellState,
    statusQuery,
    bindPolicyQuery,
    terminalStatusQuery,
    productSearchText,
    setProductSearchText,
    productPage,
    setProductPage,
    productPageSize,
    setProductPageSize,
    dashboardQuery,
    productQuery,
    auditQuery,
  } = useRemoteReadOnlyClient();

  const bindPolicy = bindPolicyQuery.data ?? shellState.bindPolicy;
  const terminalStatus = terminalStatusQuery.data;
  const dashboard = dashboardQuery.data;
  const productPageData = productQuery.data;
  const products = productPageData?.items ?? [];
  const auditVisibility = auditQuery.data;

  function refreshAll() {
    void statusQuery.refetch();
    void bindPolicyQuery.refetch();
    void terminalStatusQuery.refetch();
    void dashboardQuery.refetch();
    void productQuery.refetch();
    void auditQuery.refetch();
  }

  return (
    <div className="space-y-6">
      <CommandBar
        title="Remote Read-Only Client"
        subtitle="React/WebView LAN usability surface for safe status, product, dashboard, terminal, and audit visibility reads."
        status={<StatusPill tone="warning">{shellState.connectionStatus}</StatusPill>}
        actions={
          <Button variant="secondary" onClick={refreshAll}>
            Refresh read-only state
          </Button>
        }
      />

      <div className="space-y-2">
        {remoteReadOnlyWarnings.map((warning) => (
          <Alert key={warning} tone="warning">
            {warning}
          </Alert>
        ))}
      </div>

      <div className="grid gap-4 xl:grid-cols-3">
        <Card>
          <CardHeader>
            <CardTitle>Pairing / Session State</CardTitle>
          </CardHeader>
          <CardBody>
            <dl className="space-y-3 text-sm">
              <Row label="Connection status" value={shellState.connectionStatus} />
              <Row label="Mode" value={shellState.mode ?? "LocalOnlyReadMode"} />
              <Row label="Session expiration" value={shellState.sessionExpiresAtUtc ?? shellState.sessionExpiresAt ?? "Not configured"} />
              <Row label="Operator instruction" value="Use read-only local API status until safe live LAN session plumbing exists." />
            </dl>
            <p className="mt-4 text-sm text-slate-600">{shellState.message}</p>
          </CardBody>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Terminal Identity</CardTitle>
          </CardHeader>
          <CardBody>
            <dl className="space-y-3 text-sm">
              <Row label="Host identity" value={shellState.hostId ?? "Not configured"} />
              <Row label="Terminal id" value={terminalStatus?.terminalId ?? shellState.terminalId ?? "Not available"} />
              <Row label="Terminal name" value={terminalStatus?.terminalName ?? shellState.terminalName ?? "Not available"} />
              <Row label="Terminal role" value={terminalStatus?.terminalRole ?? shellState.terminalRole ?? "Read-only client"} />
              <Row label="Terminal status" value={terminalStatus?.terminalStatus ?? shellState.pairedState ?? "HandshakeRequired"} />
            </dl>
            <p className="mt-4 text-sm text-slate-600">{terminalStatus?.message ?? "Terminal/session status is read-only and live activation is deferred."}</p>
          </CardBody>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Bind Policy / Readiness</CardTitle>
          </CardHeader>
          <CardBody>
            <dl className="space-y-3 text-sm">
              <Row label="Default mode" value={bindPolicy.defaultMode} />
              <Row label="Effective mode" value={bindPolicy.effectiveMode} />
              <Row label="Disabled by default" value={bindPolicy.disabledByDefault ? "Yes" : "No"} />
              <Row label="Localhost only" value={bindPolicy.localhostOnly ? "Yes" : "No"} />
              <Row label="Explicit host bind required" value={bindPolicy.explicitHostBindRequired ? "Yes" : "No"} />
              <Row label="Specific IP only" value={bindPolicy.specificIpOnly ? "Yes" : "No"} />
            </dl>
            <p className="mt-4 text-sm text-slate-600">{bindPolicy.message}</p>
          </CardBody>
        </Card>
      </div>

      <div className="grid gap-4 xl:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle>Allowed Capabilities</CardTitle>
          </CardHeader>
          <CardBody>
            <ul className="grid gap-2 text-sm text-slate-700 sm:grid-cols-2">
              {(shellState.allowedCapabilities ?? shellState.capabilities).map((capability) => (
                <li key={capability} className="flex items-center justify-between gap-3 rounded-xl border border-slate-200 bg-slate-50 px-4 py-3">
                  <span>{capability}</span>
                  <StatusPill tone="success">Read-only</StatusPill>
                </li>
              ))}
            </ul>
          </CardBody>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Disabled Mutations</CardTitle>
          </CardHeader>
          <CardBody>
            <div className="grid gap-3 sm:grid-cols-2">
              {shellState.disabledMutations.map((item) => (
                <div key={item} className="rounded-xl border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-700">
                  {item}
                </div>
              ))}
            </div>
          </CardBody>
        </Card>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Product Browse / Read-Only Surface</CardTitle>
        </CardHeader>
        <CardBody>
          <form
            className="grid gap-3 md:grid-cols-[1fr_auto_auto]"
            onSubmit={(event) => {
              event.preventDefault();
              setProductPage(1);
              void productQuery.refetch();
            }}
          >
            <TextInput
              id="remote-readonly-product-search"
              value={productSearchText}
              onChange={(event) => {
                setProductSearchText(event.target.value);
                setProductPage(1);
              }}
              placeholder="Search barcode, SKU, name, or category"
            />
            <select
              className="rounded-lg border border-slate-300 px-3 py-2 text-sm text-slate-900"
              value={productPageSize}
              onChange={(event) => {
                setProductPageSize(Number(event.target.value));
                setProductPage(1);
              }}
            >
              <option value={5}>5 rows</option>
              <option value={10}>10 rows</option>
              <option value={25}>25 rows</option>
            </select>
            <Button type="submit" variant="secondary">
              Search read-only
            </Button>
          </form>

          {productQuery.isLoading ? <LoadingState label="Loading read-only product browse..." /> : null}
          {productQuery.isError ? <ErrorState message={(productQuery.error as Error).message} /> : null}
          {products.length > 0 ? (
            <div className="mt-4 space-y-2">
              {products.map((item) => (
                <div key={item.id} className="rounded-xl border border-slate-200 bg-white px-4 py-3 text-sm">
                  <p className="font-medium text-slate-900">{item.name}</p>
                  <p className="text-slate-600">SKU {item.sku ?? "n/a"} / Barcode {item.barcode ?? "n/a"} / Category {item.categoryName ?? "Uncategorized"}</p>
                  <p className="text-slate-600">Price {item.sellingPrice.toLocaleString()} / Stock {item.stockOnHand.toLocaleString()}</p>
                </div>
              ))}
            </div>
          ) : (
            <p className="mt-4 text-sm text-slate-500">Read-only product result area. No catalog mutation controls are present.</p>
          )}

          <div className="mt-4 flex items-center justify-between text-sm text-slate-600">
            <span>Page {productPageData?.pageNumber ?? productPage} of {productPageData?.totalPages ?? 1} / {productPageData?.totalCount ?? 0} products</span>
            <div className="flex gap-2">
              <Button variant="secondary" size="sm" disabled={productPage <= 1} onClick={() => setProductPage((page) => Math.max(1, page - 1))}>Previous</Button>
              <Button variant="secondary" size="sm" disabled={productPageData ? productPage >= productPageData.totalPages : true} onClick={() => setProductPage((page) => page + 1)}>Next</Button>
            </div>
          </div>
        </CardBody>
      </Card>

      <div className="grid gap-4 xl:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle>Dashboard / Status Area</CardTitle>
          </CardHeader>
          <CardBody>
            {dashboardQuery.isLoading ? <LoadingState label="Loading read-only dashboard status..." /> : null}
            {dashboardQuery.isError ? <ErrorState message={(dashboardQuery.error as Error).message} /> : null}
            {dashboard ? (
              <dl className="space-y-3 text-sm">
                <Row label="Status" value={dashboard.status} />
                <Row label="Products" value={dashboard.productCount.toLocaleString()} />
                <Row label="Low stock" value={dashboard.lowStockCount.toLocaleString()} />
                <Row label="Transactions today" value={dashboard.todayTransactionCount.toLocaleString()} />
                <Row label="Refreshed" value={new Date(dashboard.refreshedAt).toLocaleString()} />
              </dl>
            ) : (
              <p className="text-sm text-slate-500">Read-only dashboard/status summary.</p>
            )}
          </CardBody>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Remote Operator Audit Visibility</CardTitle>
          </CardHeader>
          <CardBody>
            {auditQuery.isLoading ? <LoadingState label="Loading audit visibility state..." /> : null}
            {auditQuery.isError ? <ErrorState message={(auditQuery.error as Error).message} /> : null}
            {auditVisibility ? (
              <div className="space-y-3 text-sm text-slate-700">
                <Row label="Audit visibility status" value={auditVisibility.status} />
                <Row label="Visible events" value={auditVisibility.totalCount.toLocaleString()} />
                <p className="text-slate-600">{auditVisibility.message}</p>
              </div>
            ) : (
              <p className="text-sm text-slate-500">Audit visibility is read-only and currently deferred until a safe API read path is confirmed.</p>
            )}
          </CardBody>
        </Card>
      </div>
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center justify-between gap-4">
      <dt className="text-slate-500">{label}</dt>
      <dd className="text-right font-medium text-slate-900">{value}</dd>
    </div>
  );
}
