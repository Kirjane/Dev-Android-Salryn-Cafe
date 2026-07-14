import { useQuery } from "@tanstack/react-query";
import {
  fetchOwnerReportsSummary,
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
  type DataTableColumn,
} from "../components/ui";
import { formatDateTime, formatMoney, formatQuantity } from "../utils/format";

function moneyFromCents(cents: number | null | undefined): string {
  return formatMoney((cents ?? 0) / 100);
}

function rangeLabel(period: OwnerSalesPeriodSummary): string {
  return `${formatDateTime(period.rangeStart)} to ${formatDateTime(period.rangeEnd)}`;
}

function SummaryCard({ title, period }: { title: string; period: OwnerSalesPeriodSummary }) {
  return (
    <Card>
      <CardHeader>
        <div className="flex items-start justify-between gap-3">
          <CardTitle>{title}</CardTitle>
          <StatusPill tone={period.voidedCount > 0 ? "warning" : "success"}>{period.salesCount} active</StatusPill>
        </div>
      </CardHeader>
      <CardBody className="space-y-3 text-sm text-slate-600">
        <div>
          <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Active sales</p>
          <p className="mt-1 text-2xl font-bold text-slate-950">{moneyFromCents(period.salesAmountCents)}</p>
          <p className="text-xs text-slate-500">{period.salesCount} transaction(s) · average {moneyFromCents(period.averageSaleCents)}</p>
        </div>
        <div className="rounded-lg border border-slate-200 bg-slate-50 p-3">
          <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Voided separately</p>
          <p className="font-semibold text-slate-800">{period.voidedCount} void(s) · {moneyFromCents(period.voidedAmountCents)}</p>
        </div>
        <p className="text-xs text-slate-500">{rangeLabel(period)}</p>
      </CardBody>
    </Card>
  );
}

export function OwnerReportsPage() {
  const query = useQuery({ queryKey: ["owner-reports-summary"], queryFn: fetchOwnerReportsSummary });
  const data = query.data;

  const topProductColumns: DataTableColumn<OwnerTopProductRow>[] = [
    {
      key: "product",
      header: "Product",
      render: (row) => (
        <div>
          <p className="font-semibold text-slate-900">{row.productName}</p>
          <p className="mt-1 text-xs text-slate-500">Barcode {row.barcode ?? "—"}</p>
        </div>
      ),
    },
    { key: "qty", header: "Qty sold", align: "right", render: (row) => formatQuantity(row.quantitySold) },
    { key: "amount", header: "Sales", align: "right", render: (row) => moneyFromCents(row.salesAmountCents) },
  ];

  const cashierColumns: DataTableColumn<OwnerCashierTotalRow>[] = [
    { key: "cashier", header: "Cashier", render: (row) => row.cashierDisplayName },
    { key: "count", header: "Sales", align: "right", render: (row) => row.salesCount.toLocaleString() },
    { key: "amount", header: "Amount", align: "right", render: (row) => moneyFromCents(row.salesAmountCents) },
    { key: "voided", header: "Voided", align: "right", render: (row) => `${row.voidedCount} · ${moneyFromCents(row.voidedAmountCents)}` },
  ];

  const categoryColumns: DataTableColumn<OwnerCategoryTotalRow>[] = [
    { key: "category", header: "Category", render: (row) => row.categoryName || "Uncategorized" },
    { key: "qty", header: "Qty", align: "right", render: (row) => formatQuantity(row.quantitySold) },
    { key: "amount", header: "Sales", align: "right", render: (row) => moneyFromCents(row.salesAmountCents) },
  ];

  const lowStockColumns: DataTableColumn<OwnerLowStockRow>[] = [
    {
      key: "product",
      header: "Product",
      render: (row) => (
        <div>
          <p className="font-semibold text-slate-900">{row.productName}</p>
          <p className="mt-1 text-xs text-slate-500">Barcode {row.barcode ?? "—"}</p>
        </div>
      ),
    },
    { key: "stock", header: "Stock", align: "right", render: (row) => `${formatQuantity(row.stockOnHand)} ${row.unitName}` },
    { key: "reorder", header: "Reorder", align: "right", render: (row) => formatQuantity(row.reorderLevel) },
  ];

  return (
    <div className="flex h-[calc(100vh-9rem)] max-h-[calc(100vh-9rem)] min-h-0 flex-col gap-3 overflow-hidden">
      <CommandBar
        title="Owner Reports"
        subtitle="Store Reports for sales, products, cashier totals, category totals, low stock, and inventory value."
        status={data ? <StatusPill tone="success">Read-only</StatusPill> : <StatusPill tone="info">Reports.View</StatusPill>}
        actions={<Button variant="secondary" onClick={() => void query.refetch()} disabled={query.isFetching}>{query.isFetching ? "Refreshing..." : "Refresh"}</Button>}
      />

      <Alert tone="info" title="Operational report boundary">
        Owner Reports are store operational reports only. Not an official or regulatory filing. These cards and tables are read-only and do not create sales, payments, receipts, stock movements, or report filings.
      </Alert>

      {query.isLoading ? <LoadingState label="Loading owner reports..." /> : null}
      {query.isError ? <ErrorState message={(query.error as Error).message} /> : null}

      {data ? (
        <div className="min-h-0 flex-1 overflow-y-auto overscroll-contain pr-1">
          <div className="space-y-4">
            <div className="grid gap-3 xl:grid-cols-3">
              <SummaryCard title="Daily sales" period={data.daily} />
              <SummaryCard title="Weekly sales" period={data.weekly} />
              <SummaryCard title="Monthly sales" period={data.monthly} />
            </div>

            <div className="grid gap-3 xl:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
              <Card>
                <CardHeader><CardTitle>Top products</CardTitle></CardHeader>
                <CardBody>
                <div className="max-h-80 overflow-y-auto overscroll-contain">
                  <DataTable columns={topProductColumns} rows={data.topProducts} getRowKey={(row) => row.productId} emptyTitle="No product sales" emptyDescription="Completed non-voided sales will appear here." />
                </div>
                </CardBody>
              </Card>

              <Card>
                <CardHeader><CardTitle>Cashier totals</CardTitle></CardHeader>
                <CardBody>
                <div className="max-h-80 overflow-y-auto overscroll-contain">
                  <DataTable columns={cashierColumns} rows={data.cashierTotals} getRowKey={(row) => row.cashierUserId} emptyTitle="No cashier totals" emptyDescription="Completed non-voided sales will appear here by cashier." />
                </div>
                </CardBody>
              </Card>
            </div>

            <div className="grid gap-3 xl:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
              <Card>
                <CardHeader><CardTitle>Category totals</CardTitle></CardHeader>
                <CardBody>
                <div className="max-h-80 overflow-y-auto overscroll-contain">
                  <DataTable columns={categoryColumns} rows={data.categoryTotals} getRowKey={(row) => row.categoryName} emptyTitle="No category totals" emptyDescription="Products without category are grouped as Uncategorized." />
                </div>
                </CardBody>
              </Card>

              <Card>
                <CardHeader><CardTitle>Low stock</CardTitle></CardHeader>
                <CardBody>
                <div className="max-h-80 overflow-y-auto overscroll-contain">
                  <DataTable columns={lowStockColumns} rows={data.lowStock} getRowKey={(row) => row.productId} emptyTitle="No low stock" emptyDescription="Tracked products are above reorder level." />
                </div>
                </CardBody>
              </Card>
            </div>

            <Card>
              <CardHeader><CardTitle>Inventory value</CardTitle></CardHeader>
              <CardBody className="grid gap-3 text-sm text-slate-600 md:grid-cols-[1fr_1fr_1fr] md:items-center">
                <div>
                  <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Estimated value</p>
                  <p className="mt-1 text-2xl font-bold text-slate-950">{data.inventoryValue.isAvailable ? moneyFromCents(data.inventoryValue.estimatedValueCents) : "Unavailable"}</p>
                </div>
                <div>
                  <p>Valued products: {data.inventoryValue.valuedProductCount}</p>
                  <p>Missing cost products: {data.inventoryValue.missingCostProductCount}</p>
                </div>
                <Alert tone={data.inventoryValue.isAvailable ? "success" : "warning"}>{data.inventoryValue.message}</Alert>
              </CardBody>
            </Card>

            <p className="pb-4 text-xs text-slate-500">Week rule: {data.weekRule} Generated: {formatDateTime(data.generatedAt)}</p>
          </div>
        </div>
      ) : null}
    </div>
  );
}
