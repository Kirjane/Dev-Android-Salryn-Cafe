import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { fetchSaleDetail, fetchSalesHistory, type SaleHistoryItem } from "../api/sales";
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
} from "../components/ui";
import { formatDateTime, formatMoney } from "../utils/format";

export function SalesHistoryPage() {
  const [receiptNo, setReceiptNo] = useState("");
  const [status, setStatus] = useState("");
  const [page, setPage] = useState(1);
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const salesQuery = useQuery({
    queryKey: ["sales-history", receiptNo, status, page],
    queryFn: () =>
      fetchSalesHistory({
        receiptNo: receiptNo || undefined,
        status: status || undefined,
        page,
        pageSize: 25,
      }),
  });

  const detailQuery = useQuery({
    queryKey: ["sale-detail", selectedId],
    queryFn: () => fetchSaleDetail(selectedId!),
    enabled: selectedId !== null,
  });

  useEffect(() => {
    if (salesQuery.data && salesQuery.data.totalPages > 0 && page > salesQuery.data.totalPages) {
      setPage(salesQuery.data.totalPages);
    }
  }, [page, salesQuery.data]);

  useEffect(() => {
    setSelectedId(null);
  }, [receiptNo, status]);

  function closeDetail() {
    setSelectedId(null);
  }

  const columns = [
    { key: "receipt", header: "Receipt", render: (row: SaleHistoryItem) => row.receiptNo },
    {
      key: "date",
      header: "Date",
      render: (row: SaleHistoryItem) => formatDateTime(row.saleDate),
    },
    { key: "cashier", header: "Cashier", render: (row: SaleHistoryItem) => row.cashierDisplayName },
    {
      key: "total",
      header: "Total",
      align: "right" as const,
      render: (row: SaleHistoryItem) => formatMoney(row.total),
    },
    {
      key: "status",
      header: "Status",
      render: (row: SaleHistoryItem) => (
        <StatusPill tone={row.status === "Completed" ? "success" : "warning"}>
          {row.status}
        </StatusPill>
      ),
    },
    {
      key: "view",
      header: "View",
      align: "right" as const,
      render: (row: SaleHistoryItem) => (
        <Button size="sm" variant="secondary" onClick={() => setSelectedId(row.id)}>
          View
        </Button>
      ),
    },
  ];

  return (
    <div className="space-y-4">
      <CommandBar
        title="Sales History"
        subtitle="Read-only sale lookup. Detail opens in a separated dialog without changing sales backend behavior."
        actions={
          <Button variant="secondary" onClick={() => void salesQuery.refetch()}>
            Refresh
          </Button>
        }
      />

      <Alert tone="info" title="Read-only sales lane">
        POS-UX0 only improves sales lookup usability. It does not add reprint, void, refund, printer, or official output actions.
      </Alert>

      <Card className="flex min-h-[32rem] flex-col">
        <CardHeader className="shrink-0">
          <CardTitle>Sales list</CardTitle>
        </CardHeader>
        <CardBody className="flex min-h-0 flex-1 flex-col gap-4">
          <div className="grid shrink-0 gap-3 md:grid-cols-2">
            <TextInput
              placeholder="Receipt number..."
              value={receiptNo}
              onChange={(event) => {
                setPage(1);
                setReceiptNo(event.target.value);
              }}
            />
            <select
              className="rounded-lg border border-slate-300 px-3 py-2 text-sm"
              value={status}
              onChange={(event) => {
                setPage(1);
                setStatus(event.target.value);
              }}
            >
              <option value="">All statuses</option>
              <option value="Completed">Completed</option>
              <option value="Voided">Voided</option>
            </select>
          </div>

          {salesQuery.isLoading ? <LoadingState label="Loading sales..." /> : null}
          {salesQuery.isError ? <ErrorState message={(salesQuery.error as Error).message} /> : null}

          {salesQuery.data ? (
            <>
              <div className="min-h-0 flex-1 overflow-y-auto pr-1">
                <DataTable
                  columns={columns}
                  rows={salesQuery.data.items}
                  getRowKey={(row) => row.id}
                  onRowClick={(row) => setSelectedId(row.id)}
                  selectedRowKey={selectedId}
                  emptyTitle="No sales found"
                  emptyDescription="Try adjusting receipt or status filters."
                />
              </div>
              <div className="flex shrink-0 items-center justify-between border-t border-slate-100 pt-3 text-sm text-slate-500">
                <span>
                  {salesQuery.data.totalCount.toLocaleString()} sales · page {salesQuery.data.pageNumber}{" "}
                  of {Math.max(1, salesQuery.data.totalPages || 1)}
                </span>
                <div className="flex gap-2">
                  <Button
                    variant="secondary"
                    size="sm"
                    disabled={page <= 1}
                    onClick={() => setPage((current) => current - 1)}
                  >
                    Previous
                  </Button>
                  <Button
                    variant="secondary"
                    size="sm"
                    disabled={page >= salesQuery.data.totalPages}
                    onClick={() => setPage((current) => current + 1)}
                  >
                    Next
                  </Button>
                </div>
              </div>
            </>
          ) : null}
        </CardBody>
      </Card>

      {selectedId ? (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/60 p-4">
          <div className="max-h-[90vh] w-full max-w-5xl overflow-hidden rounded-xl bg-white shadow-2xl">
            <div className="flex items-center justify-between border-b border-slate-200 px-5 py-4">
              <div>
                <h2 className="text-base font-semibold text-slate-950">
                  {detailQuery.data ? `Receipt ${detailQuery.data.receiptNo}` : "Sale detail"}
                </h2>
                <p className="mt-1 text-xs text-slate-500">Read-only detail panel. No reprint, void, or official output action is exposed here.</p>
              </div>
              <Button variant="secondary" size="sm" onClick={closeDetail}>
                Close
              </Button>
            </div>
            <div className="max-h-[calc(90vh-5rem)] overflow-y-auto p-5">
              {detailQuery.isLoading ? <LoadingState label="Loading sale detail..." /> : null}
              {detailQuery.isError ? <ErrorState message={(detailQuery.error as Error).message} /> : null}
              {detailQuery.data ? (
                <div className="space-y-4 text-sm text-slate-600">
                  <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
                    <p>{formatDateTime(detailQuery.data.saleDate)}</p>
                    <p>{detailQuery.data.cashierDisplayName}</p>
                    <p>
                      <StatusPill tone={detailQuery.data.status === "Completed" ? "success" : "warning"}>
                        {detailQuery.data.status}
                      </StatusPill>
                    </p>
                    <p className="font-semibold text-slate-900">Total: {formatMoney(detailQuery.data.total)}</p>
                    <p>Subtotal: {formatMoney(detailQuery.data.subtotal)}</p>
                    <p>Discount: {formatMoney(detailQuery.data.discount)}</p>
                    <p>Tax: {formatMoney(detailQuery.data.tax)}</p>
                    <p>Paid: {formatMoney(detailQuery.data.amountPaid)}</p>
                    <p>Change: {formatMoney(detailQuery.data.change)}</p>
                  </div>
                  {detailQuery.data.voidReason ? <Alert tone="warning">Void reason: {detailQuery.data.voidReason}</Alert> : null}
                  <DataTable
                    columns={[
                      { key: "item", header: "Item", render: (row) => row.productNameSnapshot },
                      {
                        key: "qty",
                        header: "Qty",
                        align: "right",
                        render: (row) => row.quantity.toString(),
                      },
                      {
                        key: "total",
                        header: "Line total",
                        align: "right",
                        render: (row) => formatMoney(row.lineTotal),
                      },
                    ]}
                    rows={detailQuery.data.items}
                    getRowKey={(row) => row.id}
                  />
                </div>
              ) : null}
            </div>
          </div>
        </div>
      ) : null}
    </div>
  );
}
