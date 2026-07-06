import { useQuery } from "@tanstack/react-query";
import { fetchDashboardSummary } from "../api/dashboard";
import { ApiError } from "../api/http";
import type { SessionState } from "../api/session";
import {
  Alert,
  Button,
  Card,
  CardBody,
  CardHeader,
  CardTitle,
  CommandBar,
  Dialog,
  ErrorState,
  LoadingState,
  MetricCard,
  StatusPill,
} from "../components/ui";
import { formatMoney } from "../utils/format";

type DashboardPageProps = {
  session: SessionState;
  onLogout: () => void;
  onOpenHostLanControl: () => void;
};

export function DashboardPage({ session, onLogout, onOpenHostLanControl }: DashboardPageProps) {
  const { data, isLoading, isError, error, refetch, isFetching } = useQuery({
    queryKey: ["dashboard-summary"],
    queryFn: fetchDashboardSummary,
    enabled: session.isAuthenticated,
    retry: false,
  });

  const sessionExpired = isError && error instanceof ApiError && error.status === 401;

  const hasWarnings = data ? data.warnings.length > 0 : false;
  const lowStock = data?.lowStockCount ?? 0;

  return (
    <>
      <div className="space-y-6">
        <CommandBar
          title="Dashboard"
          subtitle={`Signed in as ${session.user?.displayName ?? ""} (${
            session.user?.roleName ?? ""
          }).`}
          status={
            data ? (
              <StatusPill tone={data.status === "ok" ? "success" : "warning"}>
                {data.status === "ok" ? "All reads healthy" : "Read warnings present"}
              </StatusPill>
            ) : null
          }
          actions={
            <Button variant="secondary" onClick={() => refetch()} disabled={isFetching}>
              {isFetching ? "Refreshing..." : "Refresh"}
            </Button>
          }
        />

        <Alert tone="primary" title="This computer is the POS host">
          <ul className="list-disc space-y-1 pl-5">
            <li>The SQLite database, users, roles, terminal trust, and official journal/sequence/inventory writes are local to this host.</li>
            <li>Remote POS clients (other computers) connect to this host's explicit LAN address; they never open or share this database.</li>
            <li>Opening this app on another computer's localhost makes THAT computer its own independent host, not a client of this host. Same app installed elsewhere does not mean sync.</li>
            <li>LAN access stays disabled until an operator explicitly enables it here on the host; there is no default or wildcard bind.</li>
          </ul>
        </Alert>

        <Card>
          <CardHeader>
            <div className="flex flex-wrap items-center justify-between gap-3">
              <CardTitle>Host LAN Control</CardTitle>
              <StatusPill tone="info">Local-only</StatusPill>
            </div>
          </CardHeader>
          <CardBody className="flex flex-wrap items-center justify-between gap-4">
            <p className="max-w-3xl text-sm text-slate-600">
              Use Host LAN Control to show or rotate the join code, approve pending Remote POS devices,
              and see paired-terminal capacity before sending the client LAN URL to another device.
            </p>
            <Button onClick={onOpenHostLanControl}>
              Open Host LAN Control
            </Button>
          </CardBody>
        </Card>

        {isLoading ? <LoadingState label="Loading dashboard..." /> : null}

        {isError && !sessionExpired ? (
          <ErrorState
            message={(error as Error).message || "Failed to load dashboard summary."}
            action={
              <Button variant="secondary" size="sm" onClick={() => refetch()}>
                Try again
              </Button>
            }
          />
        ) : null}

        {data ? (
          <>
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
              <MetricCard
                title="Today Sales"
                value={formatMoney(data.todaySalesTotal)}
                subtitle="Completed sales total"
              />
              <MetricCard
                title="Transactions"
                value={data.todayTransactionCount.toLocaleString()}
                subtitle="Completed today"
              />
              <MetricCard
                title="Products"
                value={data.productCount.toLocaleString()}
                subtitle="Catalog records"
              />
              <MetricCard
                title="Low Stock"
                value={lowStock.toLocaleString()}
                subtitle="Needs attention"
                tone={lowStock > 0 ? "warning" : "success"}
                badge={lowStock > 0 ? "Review" : "OK"}
                emphasis={lowStock > 0}
              />
            </div>

            <Card>
              <CardHeader>
                <div className="flex items-center justify-between gap-3">
                  <CardTitle>Backend status</CardTitle>
                  <StatusPill tone={hasWarnings ? "warning" : "success"}>
                    {hasWarnings ? "Warnings" : "Healthy"}
                  </StatusPill>
                </div>
              </CardHeader>
              <CardBody>
                <p className="text-sm text-slate-600">
                  {data.status === "ok"
                    ? `Dashboard refreshed ${new Date(data.refreshedAt).toLocaleString()}.`
                    : "Dashboard loaded with one or more repository warnings."}
                </p>
                {hasWarnings ? (
                  <div className="mt-3 space-y-2">
                    {data.warnings.map((warning) => (
                      <Alert key={warning} tone="warning">
                        {warning}
                      </Alert>
                    ))}
                  </div>
                ) : null}
              </CardBody>
            </Card>
          </>
        ) : null}
      </div>

      <Dialog
        open={sessionExpired}
        title="Session expired"
        description="Your session is no longer valid. Please sign in again to continue."
        onClose={() => onLogout()}
        footer={<Button onClick={() => onLogout()}>Sign in again</Button>}
      />
    </>
  );
}
