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
};

export function DashboardPage({ session, onLogout }: DashboardPageProps) {
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
