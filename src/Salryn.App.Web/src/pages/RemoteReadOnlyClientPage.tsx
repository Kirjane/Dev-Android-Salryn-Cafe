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
    productSearchText,
    setProductSearchText,
    dashboardQuery,
    productQuery,
  } = useRemoteReadOnlyClient();

  const dashboard = dashboardQuery.data;
  const products = productQuery.data?.items ?? [];

  return (
    <div className="space-y-6">
      <CommandBar
        title="Remote Read-Only Client"
        subtitle="React/WebView shell for safe remote status, product, dashboard, and terminal reads only."
        status={<StatusPill tone="warning">{shellState.connectionStatus}</StatusPill>}
        actions={
          <Button variant="secondary" onClick={() => void dashboardQuery.refetch()}>
            Refresh read-only status
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
            <CardTitle>Connection / Session Status</CardTitle>
          </CardHeader>
          <CardBody>
            <dl className="space-y-3 text-sm">
              <Row label="Connection status" value={shellState.connectionStatus} />
              <Row label="Session expiration" value={shellState.sessionExpiresAt ?? "Not configured"} />
              <Row label="Host identity" value={shellState.hostId ?? "Not configured"} />
              <Row label="Terminal identity" value={shellState.terminalId ?? "Not configured"} />
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
              <Row label="Terminal id" value={shellState.terminalId ?? "Not available"} />
              <Row label="Terminal name" value={shellState.terminalName ?? "Not available"} />
              <Row label="Terminal role" value={shellState.terminalRole ?? "Read-only client"} />
              <Row label="Paired state" value={shellState.pairedState ?? "Handshake required"} />
            </dl>
          </CardBody>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Allowed Capabilities</CardTitle>
          </CardHeader>
          <CardBody>
            <ul className="space-y-2 text-sm text-slate-700">
              {shellState.capabilities.map((capability) => (
                <li key={capability} className="flex items-center justify-between gap-3">
                  <span>{capability}</span>
                  <StatusPill tone="success">Read-only</StatusPill>
                </li>
              ))}
            </ul>
          </CardBody>
        </Card>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Disabled Mutations</CardTitle>
        </CardHeader>
        <CardBody>
          <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
            {shellState.disabledMutations.map((item) => (
              <div key={item} className="rounded-xl border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-700">
                {item}
              </div>
            ))}
          </div>
        </CardBody>
      </Card>

      <div className="grid gap-4 xl:grid-cols-3">
        <Card>
          <CardHeader>
            <CardTitle>Product Lookup / Read-Only Area</CardTitle>
          </CardHeader>
          <CardBody>
            <form
              className="space-y-3"
              onSubmit={(event) => {
                event.preventDefault();
                void productQuery.refetch();
              }}
            >
              <label className="block text-sm font-medium text-slate-700" htmlFor="remote-readonly-product-search">
                Search field
              </label>
              <TextInput
                id="remote-readonly-product-search"
                value={productSearchText}
                onChange={(event) => setProductSearchText(event.target.value)}
                placeholder="Search product name, SKU, or barcode"
              />
              <Button type="submit" variant="secondary" disabled={!productSearchText.trim()}>
                Search read-only
              </Button>
            </form>

            {productQuery.isLoading ? <LoadingState label="Loading read-only product results..." /> : null}
            {productQuery.isError ? <ErrorState message={(productQuery.error as Error).message} /> : null}
            {products.length > 0 ? (
              <ul className="mt-4 space-y-2 text-sm text-slate-700">
                {products.map((product) => (
                  <li key={product.id} className="rounded-lg border border-slate-200 p-3">
                    <p className="font-medium text-slate-900">{product.name}</p>
                    <p className="text-xs text-slate-500">
                      SKU {product.sku ?? "n/a"} · Barcode {product.barcode ?? "n/a"} · Stock on hand {product.stockOnHand}
                    </p>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="mt-4 text-sm text-slate-500">Read-only product result area. No catalog mutation controls are present.</p>
            )}
          </CardBody>
        </Card>

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
                <Row label="Refreshed" value={new Date(dashboard.refreshedAt).toLocaleString()} />
              </dl>
            ) : (
              <p className="text-sm text-slate-500">Read-only dashboard/status summary.</p>
            )}
          </CardBody>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Terminal Status Area</CardTitle>
          </CardHeader>
          <CardBody>
            <dl className="space-y-3 text-sm">
              <Row label="Terminal status" value={shellState.pairedState ?? "Handshake required"} />
              <Row label="Allowed mode" value="Read-only operator surface" />
              <Row label="Remote setup" value="Manual session context only" />
            </dl>
            <p className="mt-4 text-sm text-slate-500">
              Terminal/session status is displayed as read-only state. Remote setup automation is not part of this shell.
            </p>
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
