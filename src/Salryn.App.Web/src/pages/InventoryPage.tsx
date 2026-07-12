import { useEffect, useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { fetchLowStockProducts, fetchStockMovements, type LowStockProduct, type StockMovement } from "../api/inventory";
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
import { formatDateTime, formatQuantity } from "../utils/format";

function lowStockTone(row: LowStockProduct): "warning" | "danger" {
  return row.stockOnHand <= 0 ? "danger" : "warning";
}

function movementTone(type: string): "success" | "warning" | "danger" | "info" | "neutral" {
  const normalized = type.toLowerCase();
  if (normalized.includes("sale") || normalized.includes("out") || normalized.includes("void")) {
    return "danger";
  }

  if (normalized.includes("in") || normalized.includes("receive")) {
    return "success";
  }

  if (normalized.includes("adjust")) {
    return "warning";
  }

  return "info";
}

export function InventoryPage() {
  const [page, setPage] = useState(1);
  const [movementType, setMovementType] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");

  const lowStockQuery = useQuery({
    queryKey: ["inventory-low-stock"],
    queryFn: fetchLowStockProducts,
  });

  const movementsQuery = useQuery({
    queryKey: ["inventory-movements", page, movementType, from, to],
    queryFn: () =>
      fetchStockMovements({
        movementType: movementType.trim() || undefined,
        from: from.trim() || undefined,
        to: to.trim() || undefined,
        page,
        pageSize: 25,
      }),
  });

  useEffect(() => {
    if (movementsQuery.data && movementsQuery.data.totalPages > 0 && page > movementsQuery.data.totalPages) {
      setPage(movementsQuery.data.totalPages);
    }
  }, [page, movementsQuery.data]);

  const lowStockSummary = useMemo(() => {
    const items = lowStockQuery.data ?? [];
    return {
      total: items.length,
      out: items.filter((item) => item.stockOnHand <= 0).length,
      low: items.filter((item) => item.stockOnHand > 0).length,
    };
  }, [lowStockQuery.data]);

  function refreshAll() {
    void lowStockQuery.refetch();
    void movementsQuery.refetch();
  }

  function resetStockFilters() {
    setPage(1);
    setMovementType("");
    setFrom("");
    setTo("");
  }

  const lowStockColumns = [
    {
      key: "product",
      header: "Product",
      render: (row: LowStockProduct) => (
        <div>
          <p className="font-semibold text-slate-900">{row.productName}</p>
          <p className="mt-1 text-xs text-slate-500">Barcode {row.barcode ?? "—"}</p>
        </div>
      ),
    },
    {
      key: "stock",
      header: "Stock",
      align: "right" as const,
      render: (row: LowStockProduct) => `${formatQuantity(row.stockOnHand)} ${row.unitName}`,
    },
    {
      key: "reorder",
      header: "Reorder",
      align: "right" as const,
      render: (row: LowStockProduct) => formatQuantity(row.reorderLevel),
    },
    {
      key: "status",
      header: "Status",
      render: (row: LowStockProduct) => <StatusPill tone={lowStockTone(row)}>{row.stockOnHand <= 0 ? "Out of stock" : "Low stock"}</StatusPill>,
    },
  ];

  const movementColumns = [
    {
      key: "when",
      header: "Date",
      render: (row: StockMovement) => formatDateTime(row.createdAt),
    },
    { key: "product", header: "Product", render: (row: StockMovement) => row.productName },
    {
      key: "type",
      header: "Type",
      render: (row: StockMovement) => <StatusPill tone={movementTone(row.movementType)}>{row.movementType}</StatusPill>,
    },
    {
      key: "qty",
      header: "Qty",
      align: "right" as const,
      render: (row: StockMovement) => formatQuantity(row.quantityChange),
    },
    { key: "user", header: "User", render: (row: StockMovement) => row.userDisplayName },
    { key: "reference", header: "Reference", render: (row: StockMovement) => row.referenceType ? `${row.referenceType} ${row.referenceId ?? ""}` : "—" },
    { key: "reason", header: "Reason", render: (row: StockMovement) => row.reason ?? "—" },
  ];

  return (
    <div className="space-y-6">
      <CommandBar
        title="Inventory"
        subtitle="Read-only stock levels and stock movement history from approved inventory endpoints."
        status={
          <div className="flex flex-wrap gap-2">
            <StatusPill tone={lowStockSummary.total > 0 ? "warning" : "success"}>{lowStockSummary.total} attention item(s)</StatusPill>
            {lowStockSummary.out > 0 ? <StatusPill tone="danger">{lowStockSummary.out} out</StatusPill> : null}
          </div>
        }
        actions={
          <Button variant="secondary" onClick={refreshAll} disabled={lowStockQuery.isFetching || movementsQuery.isFetching}>
            {lowStockQuery.isFetching || movementsQuery.isFetching ? "Refreshing..." : "Refresh"}
          </Button>
        }
      />

      <Alert tone="info" title="Read-only inventory lane">
        This page does not perform stock adjustments because no approved React adjustment contract is wired in this lane.
      </Alert>

      <div className="grid gap-6 xl:grid-cols-[minmax(0,0.9fr)_minmax(0,1.3fr)]">
        <Card>
          <CardHeader>
            <div className="flex items-center justify-between gap-3">
              <CardTitle>Stock attention</CardTitle>
              <StatusPill tone={lowStockSummary.total > 0 ? "warning" : "success"}>
                {lowStockSummary.total > 0 ? "Review" : "Clear"}
              </StatusPill>
            </div>
          </CardHeader>
          <CardBody className="space-y-4">
            {lowStockQuery.isLoading ? <LoadingState label="Loading low stock..." /> : null}
            {lowStockQuery.isError ? <ErrorState message={(lowStockQuery.error as Error).message} /> : null}
            {lowStockQuery.data ? (
              <DataTable
                columns={lowStockColumns}
                rows={lowStockQuery.data}
                getRowKey={(row) => row.productId}
                emptyTitle="No low stock items"
                emptyDescription="All tracked products are above reorder level."
              />
            ) : null}
          </CardBody>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Stock movements</CardTitle>
          </CardHeader>
          <CardBody className="space-y-4">
            <div className="grid gap-3 md:grid-cols-[minmax(12rem,1fr)_minmax(11rem,0.8fr)_minmax(11rem,0.8fr)_auto]">
              <TextInput
                placeholder="Movement type filter..."
                value={movementType}
                onChange={(event) => {
                  setPage(1);
                  setMovementType(event.target.value);
                }}
              />
              <TextInput
                placeholder="From ISO/date"
                value={from}
                onChange={(event) => {
                  setPage(1);
                  setFrom(event.target.value);
                }}
              />
              <TextInput
                placeholder="To ISO/date"
                value={to}
                onChange={(event) => {
                  setPage(1);
                  setTo(event.target.value);
                }}
              />
              <Button variant="secondary" onClick={resetStockFilters} disabled={!movementType && !from && !to}>
                Clear
              </Button>
            </div>

            {movementsQuery.isLoading ? <LoadingState label="Loading movements..." /> : null}
            {movementsQuery.isError ? <ErrorState message={(movementsQuery.error as Error).message} /> : null}
            {movementsQuery.data ? (
              <>
                <DataTable
                  columns={movementColumns}
                  rows={movementsQuery.data.items}
                  getRowKey={(row) => row.id}
                  emptyTitle="No stock movements"
                  emptyDescription="Movement history will appear here when records exist."
                />
                <div className="flex items-center justify-between text-sm text-slate-500">
                  <span>
                    {movementsQuery.data.totalCount.toLocaleString()} movements · page {movementsQuery.data.pageNumber} of {Math.max(1, movementsQuery.data.totalPages || 1)}
                  </span>
                  <div className="flex gap-2">
                    <Button variant="secondary" size="sm" disabled={page <= 1} onClick={() => setPage((current) => current - 1)}>
                      Previous
                    </Button>
                    <Button
                      variant="secondary"
                      size="sm"
                      disabled={page >= movementsQuery.data.totalPages}
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
      </div>
    </div>
  );
}
