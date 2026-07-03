import { useState } from "react";
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
import { useLocalLanControl } from "../hooks/useLocalLanControl";

export function RemoteReadOnlyClientPage() {
  const {
    shellState,
    statusQuery,
    bindPolicyQuery,
    diagnosticsQuery,
    preflightQuery,
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

  const {
    statusQuery: localControlQuery,
    remoteTransportMutation,
    disableTerminalMutation,
    revokeTerminalMutation,
  } = useLocalLanControl();

  const [bindAddressInput, setBindAddressInput] = useState("");

  const bindPolicy = bindPolicyQuery.data ?? shellState.bindPolicy;
  const diagnostics = diagnosticsQuery.data;
  const preflight = preflightQuery.data;
  const terminalStatus = terminalStatusQuery.data;
  const sessionResilience = shellState.sessionResilience ?? terminalStatus?.sessionResilience ?? null;
  const lastSuccessfulStatusReadAt =
    statusQuery.isSuccess && statusQuery.dataUpdatedAt > 0
      ? new Date(statusQuery.dataUpdatedAt).toLocaleString()
      : "No successful read yet";
  const statusReadStale = statusQuery.isError && statusQuery.dataUpdatedAt > 0;
  const dashboard = dashboardQuery.data;
  const productPageData = productQuery.data;
  const products = productPageData?.items ?? [];
  const auditVisibility = auditQuery.data;
  const localControl = localControlQuery.data;
  const localControlAvailable = localControlQuery.isSuccess && Boolean(localControl);
  const localControlBusy =
    remoteTransportMutation.isPending ||
    disableTerminalMutation.isPending ||
    revokeTerminalMutation.isPending;
  const localControlActionError =
    (remoteTransportMutation.error as Error | null)?.message ??
    (disableTerminalMutation.error as Error | null)?.message ??
    (revokeTerminalMutation.error as Error | null)?.message ??
    null;

  function refreshAll() {
    void statusQuery.refetch();
    void bindPolicyQuery.refetch();
    void diagnosticsQuery.refetch();
    void preflightQuery.refetch();
    void terminalStatusQuery.refetch();
    void dashboardQuery.refetch();
    void productQuery.refetch();
    void auditQuery.refetch();
    void localControlQuery.refetch();
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
              <Row label="Mode" value={diagnostics?.mode ?? shellState.mode ?? "LocalOnlyReadMode"} />
              <Row label="Overall status" value={diagnostics?.overallStatus ?? shellState.connectionStatus} />
              <Row label="LAN readiness" value={bindPolicy.disabledByDefault ? "Disabled by default" : "Explicitly enabled"} />
              <Row label="Effective bind mode" value={bindPolicy.effectiveMode} />
              <Row label="Terminal trust" value={diagnostics?.terminalIdentityRequired ? "Required" : "Not required"} />
              <Row label="Handshake proof" value={diagnostics?.handshakeProofRequired ? "Required" : "Not required"} />
              <Row label="Cookie LAN trust" value={diagnostics?.cookieTrustAcceptedForRemoteLan ? "Accepted" : "Rejected"} />
              <Row label="Session expiration" value={shellState.sessionExpiresAtUtc ?? shellState.sessionExpiresAt ?? "Not configured"} />
              <Row label="Operator instruction" value="Operator localhost session is separate from LAN remote terminal trust. Pairing and handshake remain required for remote reads." />
            </dl>
            {diagnostics?.lastDenialMessage ? (
              <p className="mt-4 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900">
                Last safe denial: {diagnostics.lastDenialCode ?? "Denied"} — {diagnostics.lastDenialMessage}
              </p>
            ) : null}
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
              <Row label="No wildcard bind" value={bindPolicy.noWildcardBind === false ? "No" : "Yes"} />
              <Row label="No discovery" value={bindPolicy.noDiscovery === false ? "No" : "Yes"} />
            </dl>
            <p className="mt-4 text-sm text-slate-600">{bindPolicy.message}</p>
          </CardBody>
        </Card>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Session Resilience / Lifecycle</CardTitle>
        </CardHeader>
        <CardBody>
          <dl className="grid gap-3 text-sm md:grid-cols-2">
            <Row label="Lifecycle state" value={sessionResilience?.lifecycleState ?? "Unknown"} />
            <Row label="Last successful read" value={lastSuccessfulStatusReadAt} />
            <Row label="Read-only refresh safe" value={sessionResilience?.readOnlyRefreshSafe === false ? "Blocked by operator" : "Yes"} />
            <Row label="Re-pair required" value={sessionResilience?.repairRequired ? "Yes (manual, on POS host)" : "No"} />
            <Row label="Automatic re-pair" value="Disabled" />
            <Row label="Automatic discovery" value="Disabled" />
          </dl>
          <p className="mt-4 text-sm text-slate-600">
            {sessionResilience?.guidance ??
              "Read-only session guidance will appear here after a status refresh."}
          </p>
          {sessionResilience?.denialMessage ? (
            <p className="mt-3 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900">
              Denial reason: {sessionResilience.denialCode ?? "Denied"} — {sessionResilience.denialMessage}
            </p>
          ) : null}
          {statusReadStale ? (
            <Alert tone="warning">
              {sessionResilience?.staleDataWarning ??
                "The last read-only refresh failed. Displayed values may be stale. Use Refresh read-only state to retry; automatic retry is disabled to avoid request floods."}
            </Alert>
          ) : null}
          <Alert tone="info">
            Remote clients stay read-only. Session recovery never triggers checkout, admin, mutation, QR, discovery, or
            network scan behavior.
          </Alert>
        </CardBody>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Runtime Diagnostics</CardTitle>
        </CardHeader>
        <CardBody>
          {diagnosticsQuery.isLoading ? <LoadingState label="Loading safe runtime diagnostics..." /> : null}
          {diagnosticsQuery.isError ? <ErrorState message={(diagnosticsQuery.error as Error).message} /> : null}
          {diagnostics ? (
            <div className="space-y-4 text-sm">
              <dl className="grid gap-3 md:grid-cols-2">
                <Row label="Read-only only" value={diagnostics.readOnlyOnly ? "Yes" : "No"} />
                <Row label="LAN disabled by default" value={diagnostics.lanDisabledByDefault ? "Yes" : "No"} />
                <Row label="Audit policy" value={diagnostics.auditPolicyAvailable ? "Available" : "Unavailable"} />
                <Row label="CORS enabled" value={diagnostics.corsEnabled ? "Yes" : "No"} />
                <Row label="Status refresh" value={statusQuery.isSuccess ? "Last status read succeeded" : statusQuery.isError ? "Last status read failed" : "Not refreshed yet"} />
              </dl>
              <p className="text-slate-600">{diagnostics.message}</p>
              <ul className="grid gap-2 md:grid-cols-2">
                {diagnostics.items.map((item) => (
                  <li key={item.code} className="rounded-xl border border-slate-200 bg-slate-50 px-4 py-3">
                    <p className="font-medium text-slate-900">{item.code}</p>
                    <p className="text-slate-600">{item.message}</p>
                    <p className="mt-1 text-xs uppercase tracking-wide text-slate-500">{item.category} / {item.severity}</p>
                  </li>
                ))}
              </ul>
            </div>
          ) : (
            <p className="text-sm text-slate-500">Safe runtime diagnostics will appear here after refresh.</p>
          )}
        </CardBody>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Preflight / Troubleshooting</CardTitle>
        </CardHeader>
        <CardBody>
          {preflightQuery.isLoading ? <LoadingState label="Loading read-only LAN preflight..." /> : null}
          {preflightQuery.isError ? <ErrorState message={(preflightQuery.error as Error).message} /> : null}
          {preflight ? (
            <div className="space-y-4 text-sm">
              <dl className="grid gap-3 md:grid-cols-2">
                <Row label="Overall readiness" value={preflight.overallReadiness} />
                <Row label="Read-only only" value={preflight.readOnlyOnly ? "Yes" : "No"} />
                <Row label="Preflight refresh" value={preflightQuery.isSuccess ? "Last preflight read succeeded" : preflightQuery.isError ? "Last preflight read failed" : "Not refreshed yet"} />
              </dl>
              <p className="text-slate-600">{preflight.message}</p>
              {preflight.blockedReasons.length > 0 ? (
                <div className="rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-amber-900">
                  <p className="font-medium">Blocked reasons</p>
                  <ul className="mt-2 list-disc space-y-1 pl-5">
                    {preflight.blockedReasons.map((reason) => (
                      <li key={reason}>{reason}</li>
                    ))}
                  </ul>
                </div>
              ) : null}
              <div>
                <p className="mb-2 font-medium text-slate-900">Preflight checklist</p>
                <ul className="grid gap-2 md:grid-cols-2">
                  {preflight.checks.map((check) => (
                    <li key={check.code} className="rounded-xl border border-slate-200 bg-slate-50 px-4 py-3">
                      <p className="font-medium text-slate-900">{check.code}</p>
                      <p className="text-slate-600">{check.message}</p>
                      <p className="mt-1 text-xs uppercase tracking-wide text-slate-500">{check.category} / {check.status}</p>
                    </li>
                  ))}
                </ul>
              </div>
              <div>
                <p className="mb-2 font-medium text-slate-900">Safe next steps</p>
                <ul className="list-disc space-y-1 pl-5 text-slate-700">
                  {preflight.safeNextSteps.map((step) => (
                    <li key={step}>{step}</li>
                  ))}
                </ul>
              </div>
              <Alert tone="warning">
                LAN remote transport is disabled by default. No checkout, admin, mutation, discovery, or network scan controls are exposed here.
              </Alert>
            </div>
          ) : (
            <p className="text-sm text-slate-500">Read-only LAN preflight results will appear here after refresh.</p>
          )}
        </CardBody>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Local LAN Control (Operator-Only)</CardTitle>
        </CardHeader>
        <CardBody>
          {localControlQuery.isLoading ? <LoadingState label="Checking local operator LAN control access..." /> : null}
          {!localControlQuery.isLoading && !localControlAvailable ? (
            <Alert tone="info">
              Local LAN control is available only to the localhost operator on the POS host. Remote read-only clients cannot
              enable/disable LAN, configure the bind address, or disable/revoke terminals.
            </Alert>
          ) : null}
          {localControlAvailable && localControl ? (
            <div className="space-y-4 text-sm">
              <dl className="grid gap-3 md:grid-cols-2">
                <Row label="Remote transport" value={localControl.remoteTransportEnabled ? "Enabled" : "Disabled (default)"} />
                <Row label="Bind mode" value={localControl.bindMode} />
                <Row label="Configured bind address" value={localControl.configuredBindAddress} />
                <Row label="Terminal visibility" value={localControl.terminalVisibilityAvailable ? "Available" : "Restricted"} />
              </dl>
              <p className="text-slate-600">{localControl.message}</p>

              <div className="space-y-2">
                {localControl.warnings.map((warning) => (
                  <Alert key={warning} tone="warning">
                    {warning}
                  </Alert>
                ))}
              </div>

              {localControlActionError ? <ErrorState message={localControlActionError} /> : null}

              <div className="rounded-xl border border-slate-200 bg-slate-50 px-4 py-4">
                <p className="mb-2 font-medium text-slate-900">Remote read-only transport</p>
                <p className="mb-3 text-slate-600">
                  Enabling requires an explicit LAN IP address. Wildcard bind is never allowed. Remote clients always remain read-only.
                </p>
                <div className="grid gap-3 md:grid-cols-[1fr_auto_auto]">
                  <TextInput
                    id="local-lan-control-bind-address"
                    value={bindAddressInput}
                    onChange={(event) => setBindAddressInput(event.target.value)}
                    placeholder="Explicit LAN IP (e.g. 192.168.1.10)"
                    disabled={localControlBusy}
                  />
                  <Button
                    variant="secondary"
                    disabled={localControlBusy || bindAddressInput.trim().length === 0}
                    onClick={() => remoteTransportMutation.mutate({ enable: true, bindAddress: bindAddressInput.trim() })}
                  >
                    Enable on explicit IP
                  </Button>
                  <Button
                    variant="secondary"
                    disabled={localControlBusy}
                    onClick={() => remoteTransportMutation.mutate({ enable: false, bindAddress: null })}
                  >
                    Disable transport
                  </Button>
                </div>
              </div>

              <div>
                <p className="mb-2 font-medium text-slate-900">Terminal trust management</p>
                <p className="mb-3 text-slate-600">{localControl.terminalVisibilityMessage}</p>
                {localControl.terminals.length > 0 ? (
                  <ul className="space-y-2">
                    {localControl.terminals.map((terminal) => (
                      <li key={terminal.terminalId} className="rounded-xl border border-slate-200 bg-white px-4 py-3">
                        <div className="flex flex-wrap items-center justify-between gap-3">
                          <div>
                            <p className="font-medium text-slate-900">{terminal.terminalName}</p>
                            <p className="text-slate-600">{terminal.terminalRole} / {terminal.trustState}</p>
                          </div>
                          <div className="flex items-center gap-2">
                            <StatusPill tone={terminal.trusted ? "success" : "warning"}>{terminal.status}</StatusPill>
                            <Button
                              variant="secondary"
                              size="sm"
                              disabled={localControlBusy}
                              onClick={() => disableTerminalMutation.mutate(terminal.terminalId)}
                            >
                              Disable
                            </Button>
                            <Button
                              variant="secondary"
                              size="sm"
                              disabled={localControlBusy}
                              onClick={() => revokeTerminalMutation.mutate(terminal.terminalId)}
                            >
                              Revoke
                            </Button>
                          </div>
                        </div>
                      </li>
                    ))}
                  </ul>
                ) : (
                  <p className="text-sm text-slate-500">No terminals are registered yet, or terminal visibility is restricted for this operator.</p>
                )}
              </div>
            </div>
          ) : null}
        </CardBody>
      </Card>

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
