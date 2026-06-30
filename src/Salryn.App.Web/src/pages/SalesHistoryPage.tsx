import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { fetchSaleDetail, fetchSalesHistory, type SaleHistoryItem } from "../api/sales";
import {
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
  }, [salesQuery.data?.totalPages]);

  useEffect(() => {
    setSelectedId(null);
  }, [receiptNo, status]);

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
  ];

  return (
    <div className="space-y-6">
      <CommandBar
        title="Sales History"
        subtitle="Read-only completed and voided sales from the local database."
        actions={
          <Button variant="secondary" onClick={() => void salesQuery.refetch()}>
            Refresh
          </Button>
        }
      />

      <Card>
        <CardBody className="space-y-4">
          <div className="grid gap-3 md:grid-cols-2">
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
              <DataTable
                columns={columns}
                rows={salesQuery.data.items}
                getRowKey={(row) => row.id}
                onRowClick={(row) => setSelectedId(row.id)}
                selectedRowKey={selectedId}
                emptyTitle="No sales found"
                emptyDescription="Try adjusting receipt or status filters."
              />
              <div className="flex items-center justify-between text-sm text-slate-500">
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

      {selectedId && detailQuery.data ? (
        <Card>
          <CardHeader>
            <div className="flex items-center justify-between">
              <CardTitle>Receipt {detailQuery.data.receiptNo}</CardTitle>
              <Button variant="secondary" size="sm" onClick={() => setSelectedId(null)}>
                Close
              </Button>
            </div>
          </CardHeader>
          <CardBody className="space-y-3 text-sm text-slate-600">
            <p>
              {formatDateTime(detailQuery.data.saleDate)} · {detailQuery.data.cashierDisplayName} ·{" "}
              <StatusPill tone={detailQuery.data.status === "Completed" ? "success" : "warning"}>
                {detailQuery.data.status}
              </StatusPill>
            </p>
            <p>Subtotal: {formatMoney(detailQuery.data.subtotal)}</p>
            <p>Discount: {formatMoney(detailQuery.data.discount)}</p>
            <p>Tax: {formatMoney(detailQuery.data.tax)}</p>
            <p className="font-semibold text-slate-900">Total: {formatMoney(detailQuery.data.total)}</p>
            {detailQuery.data.voidReason ? <p>Void reason: {detailQuery.data.voidReason}</p> : null}
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
          </CardBody>
        </Card>
      ) : null}
    </div>
  );
}
