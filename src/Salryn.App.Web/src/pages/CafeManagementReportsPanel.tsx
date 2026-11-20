import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  fetchOwnerReportsSummary,
  type CafeReportPerformanceRow,
  type CafeReportRecipeCostRow,
  type CafeReportSessionRow,
  type OwnerCashierTotalRow,
  type OwnerCategoryTotalRow,
  type OwnerLowStockRow,
  type OwnerSalesPeriodSummary,
  type OwnerTopProductRow,
} from "../api/ownerReports";
import {
  Alert,
  Button,
  Card,
  CardBody,
  CardHeader,
  CardTitle,
  CommandBar,
  DataTable,
  ErrorState,
  LoadingState,
  StatusPill,
  TextInput,
  type DataTableColumn,
} from "../components/ui";
import { formatDateTime, formatMoney, formatQuantity } from "../utils/format";

function money(cents: number | null | undefined): string {
  return cents == null ? "Unavailable" : formatMoney(cents / 100);
}

function qty(value: number | null | undefined): string {
  return formatQuantity((value ?? 0) / 1000);
}

function today(): string {
  return new Date().toLocaleDateString("en-CA", { timeZone: "Asia/Manila" });
}

function monthStart(): string {
  return today().slice(0, 7) + "-01";
}

function SummaryCard({ title, period }: { title: string; period: OwnerSalesPeriodSummary }) {
  return (
    <Card>
      <CardHeader>
        <div className="flex items-start justify-between gap-3">
          <CardTitle>{title}</CardTitle>
          <StatusPill tone={period.voidedCount > 0 ? "warning" : "success"}>
            {period.salesCount} active
          </StatusPill>
        </div>
      </CardHeader>
      <CardBody className="space-y-2 text-sm">
        <p className="text-2xl font-bold text-slate-950">{money(period.salesAmountCents)}</p>
        <p className="text-slate-600">
          {period.salesCount} transaction(s) · average {money(period.averageSaleCents)}
        </p>
        <p className="text-xs text-slate-500">
          {period.voidedCount} void(s) · {money(period.voidedAmountCents)}
        </p>
        <p className="text-xs text-slate-500">
          {formatDateTime(period.rangeStart)} to {formatDateTime(period.rangeEnd)}
        </p>
      </CardBody>
    </Card>
  );
}

function Metric({
  label,
  value,
  note,
}: {
  label: string;
  value: string;
  note?: string;
}) {
  return (
    <div className="rounded-lg border border-slate-200 bg-white p-3">
      <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">{label}</p>
      <p className="mt-1 text-xl font-bold text-slate-950">{value}</p>
      {note ? <p className="mt-1 text-xs text-slate-500">{note}</p> : null}
    </div>
  );
}

export function CafeManagementReportsPanel() {
  const [fromDate, setFromDate] = useState(monthStart);
  const [toDate, setToDate] = useState(today);

  const range = useMemo(() => {
    const from = new Date(fromDate + "T00:00:00+08:00");
    const to = new Date(toDate + "T00:00:00+08:00");
    to.setTime(to.getTime() + 86_400_000);
    if (!Number.isFinite(from.getTime()) || !Number.isFinite(to.getTime()) || to <= from) return null;
    return { fromUtc: from.toISOString(), toUtc: to.toISOString() };
  }, [fromDate, toDate]);

  const query = useQuery({
    queryKey: ["owner-reports-summary", range?.fromUtc, range?.toUtc],
    queryFn: () => fetchOwnerReportsSummary(range ?? undefined),
    enabled: range !== null,
  });

  const data = query.data;
  const cafe = data?.cafeManagement;

  const performanceColumns: DataTableColumn<CafeReportPerformanceRow>[] = [
    {
      key: "dimension",
      header: "Dimension",
      render: (row) => (
        <div>
          <p className="font-semibold text-slate-900">{row.displayName}</p>
          <p className="text-xs text-slate-500">{row.dimensionType}</p>
        </div>
      ),
    },
    { key: "qty", header: "Qty", align: "right", render: (row) => qty(row.quantityX1000) },
    { key: "revenue", header: "Net revenue", align: "right", render: (row) => money(row.netProductRevenueCents) },
    { key: "cogs", header: "Historical COGS", align: "right", render: (row) => money(row.historicalCogsCents) },
    { key: "profit", header: "Gross profit", align: "right", render: (row) => money(row.grossProfitCents) },
    {
      key: "coverage",
      header: "Cost",
      render: (row) => (
        <StatusPill tone={row.costCoverage === "Complete" ? "success" : "warning"}>
          {row.costCoverage}
        </StatusPill>
      ),
    },
  ];

  const recipeColumns: DataTableColumn<CafeReportRecipeCostRow>[] = [
    { key: "recipe", header: "Recipe", render: (row) => row.recipeName },
    { key: "yield", header: "Yield", align: "right", render: (row) => qty(row.yieldX1000) },
    { key: "cost", header: "Known cost", align: "right", render: (row) => money(row.knownCostCents) },
    {
      key: "coverage",
      header: "Coverage",
      render: (row) => (
        <StatusPill tone={row.costCoverage === "Complete" ? "success" : "warning"}>
          {row.costCoverage}
        </StatusPill>
      ),
    },
  ];

  const sessionColumns: DataTableColumn<CafeReportSessionRow>[] = [
    {
      key: "cashier",
      header: "Cashier",
      render: (row) => (
        <div>
          <p className="font-semibold">{row.cashierDisplayName}</p>
          <p className="text-xs text-slate-500">{formatDateTime(row.closedAtUtc)}</p>
        </div>
      ),
    },
    { key: "net", header: "Net sales", align: "right", render: (row) => money(row.netSalesCents) },
    { key: "expected", header: "Expected cash", align: "right", render: (row) => money(row.expectedClosingCashCents) },
    { key: "variance", header: "Variance", align: "right", render: (row) => row.varianceCents == null ? "—" : money(row.varianceCents) },
    { key: "basis", header: "Basis", render: (row) => row.reconciliationBasis },
  ];

  const topProductColumns: DataTableColumn<OwnerTopProductRow>[] = [
    { key: "product", header: "Product", render: (row) => row.productName },
    { key: "qty", header: "Qty sold", align: "right", render: (row) => formatQuantity(row.quantitySold) },
    { key: "amount", header: "Sales", align: "right", render: (row) => money(row.salesAmountCents) },
  ];

  const cashierColumns: DataTableColumn<OwnerCashierTotalRow>[] = [
    { key: "cashier", header: "Cashier", render: (row) => row.cashierDisplayName },
    { key: "count", header: "Sales", align: "right", render: (row) => row.salesCount.toLocaleString() },
    { key: "amount", header: "Amount", align: "right", render: (row) => money(row.salesAmountCents) },
    { key: "voided", header: "Voided", align: "right", render: (row) => row.voidedCount + " · " + money(row.voidedAmountCents) },
  ];

  const categoryColumns: DataTableColumn<OwnerCategoryTotalRow>[] = [
    { key: "category", header: "Category", render: (row) => row.categoryName || "Uncategorized" },
    { key: "qty", header: "Qty", align: "right", render: (row) => formatQuantity(row.quantitySold) },
    { key: "amount", header: "Sales", align: "right", render: (row) => money(row.salesAmountCents) },
  ];

  const lowStockColumns: DataTableColumn<OwnerLowStockRow>[] = [
    { key: "product", header: "Product", render: (row) => row.productName },
    { key: "stock", header: "Stock", align: "right", render: (row) => formatQuantity(row.stockOnHand) + " " + row.unitName },
    { key: "reorder", header: "Reorder", align: "right", render: (row) => formatQuantity(row.reorderLevel) },
  ];

  return (
    <div className="flex h-[calc(100vh-9rem)] max-h-[calc(100vh-9rem)] min-h-0 flex-col gap-3 overflow-hidden">
      <CommandBar
        title="Reports"
        subtitle="Store operations, historical COGS, and profitability visibility."
        status={
          cafe ? (
            <StatusPill tone={cafe.profitability.authority === "Authoritative" ? "success" : "warning"}>
              {cafe.profitability.authority}
            </StatusPill>
          ) : (
            <StatusPill tone="info">Reports.View</StatusPill>
          )
        }
        actions={
          <Button variant="secondary" onClick={() => void query.refetch()} disabled={query.isFetching}>
            {query.isFetching ? "Refreshing..." : "Refresh"}
          </Button>
        }
      />

      <div className="grid gap-2 sm:grid-cols-[180px_180px_auto] sm:items-end">
        <label className="text-xs font-semibold uppercase tracking-wide text-slate-500">
          From
          <TextInput className="mt-1" type="date" value={fromDate} onChange={(event) => setFromDate(event.target.value)} />
        </label>
        <label className="text-xs font-semibold uppercase tracking-wide text-slate-500">
          To
          <TextInput className="mt-1" type="date" value={toDate} onChange={(event) => setToDate(event.target.value)} />
        </label>
        {cafe ? (
          <p className="pb-2 text-xs text-slate-500">
            Cafe range: {formatDateTime(cafe.rangeStartUtc)} to {formatDateTime(cafe.rangeEndUtc)}
          </p>
        ) : null}
      </div>

      {!range ? <Alert tone="warning" title="Report dates">Choose valid From and To dates in Asia/Manila.</Alert> : null}
      {query.isLoading ? <LoadingState label="Loading reports..." /> : null}
      {query.isError ? <ErrorState message={(query.error as Error).message} /> : null}

      {range && data && cafe ? (
        <div className="min-h-0 flex-1 overflow-y-auto overscroll-contain pr-1">
          <div className="space-y-4">
            <Alert
              tone={cafe.profitability.authority === "Authoritative" ? "success" : "warning"}
              title="Profitability authority"
            >
              <div className="space-y-1">
                <p>
                  Cost coverage <strong>{cafe.costCoverage.state}</strong> · Synchronization{" "}
                  <strong>{cafe.syncCompleteness.state}</strong>
                </p>
                {cafe.profitability.authorityReasons.map((reason) => (
                  <p key={reason}>{reason}</p>
                ))}
                <p className="text-xs">{cafe.officialBoundary}</p>
              </div>
            </Alert>

            <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-6">
              <Metric label="Recognized net revenue" value={money(cafe.profitability.recognizedNetRevenueCents)} note={cafe.sales.completedSaleCount + " completed sale(s)"} />
              <Metric label="Committed refunds" value={money(cafe.sales.committedRefundCents)} />
              <Metric label="Historical COGS" value={money(cafe.profitability.historicalCogsCents)} note={cafe.costCoverage.costedConsumptionLineCount + "/" + cafe.costCoverage.consumptionLineCount + " lines costed"} />
              <Metric label="Gross profit" value={money(cafe.profitability.grossProfitCents)} />
              <Metric label="Operational expense" value={money(cafe.profitability.operationalExpenseCents)} />
              <Metric label="Operational contribution" value={money(cafe.profitability.operationalContributionCents)} note="Operational metric; not net income." />
            </div>

            {cafe.costCoverage.state === "Incomplete" ? (
              <Alert tone="warning" title="Historical cost coverage incomplete">
                {cafe.costCoverage.costIncompleteSaleCount} Sale(s),{" "}
                {cafe.costCoverage.uncostedConsumptionLineCount} consumption line(s), and{" "}
                {money(cafe.costCoverage.affectedRevenueCents)} recognized revenue are affected.
              </Alert>
            ) : null}

            <div className="grid gap-3 xl:grid-cols-3">
              <Card>
                <CardHeader><CardTitle>Cash movement</CardTitle></CardHeader>
                <CardBody className="grid gap-2 sm:grid-cols-2">
                  <Metric label="Sale cash" value={money(cafe.cash?.saleCashReceiptCents)} />
                  <Metric label="AR cash collection" value={money(cafe.cash?.receivableCashCollectionCents)} />
                  <Metric label="Cash in" value={money(cafe.cash?.cashInCents)} />
                  <Metric label="Operational expense" value={money(cafe.cash?.operationalExpenseCashOutCents)} />
                  <Metric label="Generic cash out" value={money(cafe.cash?.genericCashOutCents)} />
                  <Metric label="Net cash delta" value={money(cafe.cash?.netCashDeltaCents)} />
                </CardBody>
              </Card>

              <Card>
                <CardHeader><CardTitle>Purchases & AR</CardTitle></CardHeader>
                <CardBody className="grid gap-2 sm:grid-cols-2">
                  <Metric label="Purchase receipts" value={cafe.purchases.receiptCount.toLocaleString()} note={cafe.purchases.correctionReceiptCount + " correction receipt(s)"} />
                  <Metric label="Net acquisition cost" value={money(cafe.purchases.netAcquisitionCostCents)} />
                  <Metric label="AR debits" value={money(cafe.accountsReceivable?.debitCentsInRange)} />
                  <Metric label="AR credits" value={money(cafe.accountsReceivable?.creditCentsInRange)} />
                  <Metric label="AR cash collection" value={money(cafe.accountsReceivable?.cashCollectionCentsInRange)} />
                  <Metric label="AR outstanding" value={money(cafe.accountsReceivable?.outstandingBalanceCents)} />
                </CardBody>
              </Card>

              <Card>
                <CardHeader><CardTitle>Inventory & waste</CardTitle></CardHeader>
                <CardBody className="grid gap-2 sm:grid-cols-2">
                  <Metric label="Ingredient stock" value={qty(cafe.inventory.ingredientStockQuantityX1000)} />
                  <Metric label="Product stock" value={qty(cafe.inventory.productStockQuantityX1000)} />
                  <Metric label="Known inventory value" value={money(cafe.inventory.valuation.knownValueCents)} note={cafe.inventory.valuation.state} />
                  <Metric label="Recognized waste cost" value={money(cafe.waste.costCoverage.knownValueCents)} note={cafe.waste.costCoverage.state} />
                </CardBody>
              </Card>
            </div>

            <Card>
              <CardHeader><CardTitle>Product / variant / modifier / category performance</CardTitle></CardHeader>
              <CardBody>
                <div className="max-h-96 overflow-y-auto">
                  <DataTable
                    columns={performanceColumns}
                    rows={cafe.performance}
                    getRowKey={(row) => row.dimensionType + ":" + row.dimensionId}
                    emptyTitle="No Cafe performance facts"
                    emptyDescription="Completed Sales in the selected range will appear here."
                  />
                </div>
              </CardBody>
            </Card>

            <div className="grid gap-3 xl:grid-cols-2">
              <Card>
                <CardHeader><CardTitle>Current recipe cost visibility</CardTitle></CardHeader>
                <CardBody>
                  <div className="max-h-80 overflow-y-auto">
                    <DataTable
                      columns={recipeColumns}
                      rows={cafe.recipeCosts}
                      getRowKey={(row) => row.recipeId}
                      emptyTitle="No published recipes"
                      emptyDescription="Published recipe-book evidence will appear here."
                    />
                  </div>
                </CardBody>
              </Card>

              <Card>
                <CardHeader><CardTitle>Cashier session history</CardTitle></CardHeader>
                <CardBody>
                  <div className="max-h-80 overflow-y-auto">
                    <DataTable
                      columns={sessionColumns}
                      rows={cafe.cashierSessions}
                      getRowKey={(row) => row.sessionId}
                      emptyTitle="No closed sessions"
                      emptyDescription="Immutable session reports in the selected range will appear here."
                    />
                  </div>
                </CardBody>
              </Card>
            </div>

            <div className="grid gap-3 xl:grid-cols-3">
              <Card>
                <CardHeader><CardTitle>Booking / Event / Coffee Cart</CardTitle></CardHeader>
                <CardBody className="grid gap-2 sm:grid-cols-2">
                  <Metric label="Bookings" value={cafe.bookings.bookingCount.toLocaleString()} note={cafe.bookings.eventCount + " Event · " + cafe.bookings.coffeeCartCount + " Coffee Cart"} />
                  <Metric label="Fulfilled / cancelled" value={cafe.bookings.fulfilledCount + " / " + cafe.bookings.cancelledCount} />
                  <Metric label="Quoted" value={money(cafe.bookings.quotedTotalCents)} />
                  <Metric label="Deposit received" value={money(cafe.bookings.depositReceivedCents)} />
                  <Metric label="Deposit refunded" value={money(cafe.bookings.depositRefundedCents)} />
                  <Metric label="Deposit applied" value={money(cafe.bookings.depositAppliedCents)} />
                </CardBody>
              </Card>

              <Card>
                <CardHeader><CardTitle>Delivery</CardTitle></CardHeader>
                <CardBody className="grid gap-2 sm:grid-cols-2">
                  <Metric label="Deliveries" value={cafe.delivery.deliveryCount.toLocaleString()} />
                  <Metric label="Delivered" value={cafe.delivery.deliveredCount.toLocaleString()} />
                  <Metric label="Pending / dispatched" value={cafe.delivery.pendingCount + " / " + cafe.delivery.dispatchedCount} />
                  <Metric label="Failed / cancelled" value={cafe.delivery.failedCount + " / " + cafe.delivery.cancelledCount} />
                  <Metric label="Recognized delivery fees" value={money(cafe.delivery.recognizedDeliveryFeeCents)} />
                </CardBody>
              </Card>

              <Card>
                <CardHeader>
                  <div className="flex items-center justify-between gap-3">
                    <CardTitle>Synchronization completeness</CardTitle>
                    <StatusPill
                      tone={
                        cafe.syncCompleteness.state === "Complete"
                          ? "success"
                          : cafe.syncCompleteness.state === "Incomplete"
                            ? "danger"
                            : "warning"
                      }
                    >
                      {cafe.syncCompleteness.state}
                    </StatusPill>
                  </div>
                </CardHeader>
                <CardBody className="space-y-2 text-sm text-slate-600">
                  <p>Pending upload: {cafe.syncCompleteness.pendingUploadCount}</p>
                  <p>Needs review / rejected: {cafe.syncCompleteness.needsReviewOrRejectedCount}</p>
                  <p>Remote not Ready: {cafe.syncCompleteness.nonReadyRemoteStateCount}</p>
                  <p>Cursor lag: {cafe.syncCompleteness.cursorLagCount}</p>
                  <p>Unapplied inbox: {cafe.syncCompleteness.unappliedInboxCount}</p>
                  <p>Active terminals missing sync evidence: {cafe.syncCompleteness.activeTerminalMissingSyncEvidenceCount}</p>
                  {cafe.syncCompleteness.issues.map((issue) => (
                    <Alert key={issue.code} tone="warning" title={issue.code}>
                      {issue.description} ({issue.count})
                    </Alert>
                  ))}
                </CardBody>
              </Card>
            </div>

            <div className="border-t border-slate-200 pt-4">
              <h2 className="text-base font-semibold text-slate-950">Legacy owner drilldown</h2>
              <p className="mb-3 text-xs text-slate-500">
                Existing daily, weekly, monthly, product, cashier, category, and low-stock views are retained.
              </p>
              <div className="grid gap-3 xl:grid-cols-3">
                <SummaryCard title="Daily sales" period={data.daily} />
                <SummaryCard title="Weekly sales" period={data.weekly} />
                <SummaryCard title="Monthly sales" period={data.monthly} />
              </div>
            </div>

            <div className="grid gap-3 xl:grid-cols-2">
              <Card>
                <CardHeader><CardTitle>Top products</CardTitle></CardHeader>
                <CardBody><DataTable columns={topProductColumns} rows={data.topProducts} getRowKey={(row) => row.productId} emptyTitle="No product sales" /></CardBody>
              </Card>
              <Card>
                <CardHeader><CardTitle>Cashier totals</CardTitle></CardHeader>
                <CardBody><DataTable columns={cashierColumns} rows={data.cashierTotals} getRowKey={(row) => row.cashierUserId} emptyTitle="No cashier totals" /></CardBody>
              </Card>
              <Card>
                <CardHeader><CardTitle>Category totals</CardTitle></CardHeader>
                <CardBody><DataTable columns={categoryColumns} rows={data.categoryTotals} getRowKey={(row) => row.categoryName} emptyTitle="No category totals" /></CardBody>
              </Card>
              <Card>
                <CardHeader><CardTitle>Low stock</CardTitle></CardHeader>
                <CardBody><DataTable columns={lowStockColumns} rows={data.lowStock} getRowKey={(row) => row.productId} emptyTitle="No low stock" /></CardBody>
              </Card>
            </div>

            <Alert tone="info" title="Legacy inventory estimate">
              The existing product current-cost estimate remains visible for compatibility only. It is not the historical Cafe COGS authority.
            </Alert>

            <p className="pb-4 text-xs text-slate-500">
              Week rule: {data.weekRule} Generated: {formatDateTime(data.generatedAt)}
            </p>
          </div>
        </div>
      ) : null}
    </div>
  );
}
