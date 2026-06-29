import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { fetchLowStockProducts, fetchStockMovements, type StockMovement } from "../api/inventory";
import {
  Button,
  Card,
  CardBody,
  CommandBar,
  DataTable,
  ErrorState,
  LoadingState,
  StatusPill,
} from "../components/ui";
import { formatDateTime, formatQuantity } from "../utils/format";

export function InventoryPage() {
  const [page, setPage] = useState(1);

  const lowStockQuery = useQuery({
    queryKey: ["inventory-low-stock"],
    queryFn: fetchLowStockProducts,
  });

  const movementsQuery = useQuery({
    queryKey: ["inventory-movements", page],
    queryFn: () => fetchStockMovements({ page, pageSize: 25 }),
  });

  const lowStockColumns = [
    { key: "product", header: "Product", render: (row: { productName: string }) => row.productName },
    { key: "barcode", header: "Barcode", render: (row: { barcode: string | null }) => row.barcode ?? "—" },
    {
      key: "stock",
      header: "Stock",
      align: "right" as const,
      render: (row: { stockOnHand: number; unitName: string }) =>
        `${formatQuantity(row.stockOnHand)} ${row.unitName}`,
    },
    {
      key: "reorder",
      header: "Reorder",
      align: "right" as const,
      render: (row: { reorderLevel: number }) => formatQuantity(row.reorderLevel),
    },
    {
      key: "status",
      header: "Status",
      render: () => <StatusPill tone="warning">Low Stock</StatusPill>,
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
      render: (row: StockMovement) => (
        <StatusPill tone={row.movementType.includes("Sale") ? "danger" : "info"}>
          {row.movementType}
        </StatusPill>
      ),
    },
    {
      key: "qty",
      header: "Qty",
      align: "right" as const,
      render: (row: StockMovement) => formatQuantity(row.quantityChange),
    },
    { key: "user", header: "User", render: (row: StockMovement) => row.userDisplayName },
    { key: "reason", header: "Reason", render: (row: StockMovement) => row.reason ?? "—" },
  ];

  return (
    <div className="space-y-6">
      <CommandBar
        title="Inventory"
        subtitle="Read-only stock levels and movement history."
        actions={
          <Button
            variant="secondary"
            onClick={() => {
              void lowStockQuery.refetch();
              void movementsQuery.refetch();
            }}
          >
            Refresh
          </Button>
        }
      />

      <Card>
        <CardBody className="space-y-4">
          <h3 className="text-sm font-semibold text-slate-900">Low stock</h3>
          {lowStockQuery.isLoading ? <LoadingState label="Loading low stock..." /> : null}
          {lowStockQuery.isError ? (
            <ErrorState message={(lowStockQuery.error as Error).message} />
          ) : null}
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
        <CardBody className="space-y-4">
          <h3 className="text-sm font-semibold text-slate-900">Stock movements</h3>
          {movementsQuery.isLoading ? <LoadingState label="Loading movements..." /> : null}
          {movementsQuery.isError ? (
            <ErrorState message={(movementsQuery.error as Error).message} />
          ) : null}
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
                  {movementsQuery.data.totalCount.toLocaleString()} movements · page{" "}
                  {movementsQuery.data.pageNumber} of {movementsQuery.data.totalPages || 1}
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
  );
}
