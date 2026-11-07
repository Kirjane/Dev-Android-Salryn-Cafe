import { useEffect, useMemo, useState, type ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import { ApiError } from "../api/http";
import {
  fetchLowStockProducts,
  fetchStockMovements,
  type LowStockProduct,
  type StockMovement,
} from "../api/inventory";
import {
  submitStockAdjustment,
  submitStockIn,
} from "../api/inventoryManagement";
import { fetchProducts, type ProductListItem } from "../api/products";
import {
  Alert,
  Button,
  Card,
  CardBody,
  CardHeader,
  CardTitle,
  CommandBar,
  DataTable,
  Dialog as UiDialog,
  ErrorState,
  LoadingState,
  StatusPill,
  TextInput,
  useNotifications,
} from "../components/ui";
import { formatDateTime, formatQuantity } from "../utils/format";
import { safeNotificationDescription } from "../utils/notificationError";

const movementTypeOptions = [
  "Sale",
  "StockIn",
  "AdjustmentAdd",
  "AdjustmentRemove",
  "VoidSale",
  "CountCorrection",
];
type StockDialogMode = "stock-in" | "adjustment";
type AdjustmentDirection = "add" | "remove";
type StockFormState = {
  productId: string;
  productSearch: string;
  quantity: string;
  reason: string;
  reference: string;
  direction: AdjustmentDirection;
};
const emptyStockForm: StockFormState = {
  productId: "",
  productSearch: "",
  quantity: "",
  reason: "",
  reference: "",
  direction: "add",
};
function lowStockTone(row: LowStockProduct): "warning" | "danger" {
  return row.stockOnHand <= 0 ? "danger" : "warning";
}
function movementTone(
  type: string,
): "success" | "warning" | "danger" | "info" | "neutral" {
  const normalized = type.toLowerCase();
  if (
    normalized.includes("sale") ||
    normalized.includes("remove") ||
    normalized.includes("damage") ||
    normalized.includes("expired")
  )
    return "danger";
  if (
    normalized.includes("stockin") ||
    normalized.includes("adjustmentadd") ||
    normalized.includes("return")
  )
    return "success";
  if (normalized.includes("adjust") || normalized.includes("count"))
    return "warning";
  return "info";
}
function toNumber(value: string): number {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : 0;
}
function describeInventoryWriteError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 401) return "Session expired or login required.";
    if (error.status === 403)
      return "Permission denied. Stock writes require Inventory.Adjust.";
    if (error.code === "Inventory.NegativeStockNotAllowed")
      return "Invalid adjustment. Stock cannot go below zero.";
    if (error.code === "Inventory.ProductNotFoundOrInactive")
      return "Product was not found or is inactive. Inactive products cannot receive stock movements.";
    return error.message;
  }
  return error instanceof Error
    ? error.message
    : "Unexpected inventory write error.";
}
function Dialog({
  title,
  children,
  footer,
  onClose,
}: {
  title: string;
  children: ReactNode;
  footer: ReactNode;
  onClose: () => void;
}) {
  return (
    <UiDialog
      open={true}
      title={title}
      subtitle="Stock-In and Stock Adjustment does not create sales, payments, receipts. Every write creates an inventory movement ledger entry only."
      size="xl"
      onClose={onClose}
      footer={footer}
    >
      <div className="max-h-[62vh] overflow-y-auto">{children}</div>
    </UiDialog>
  );
}

function readInitialProductFilter(): { productId: string; productName: string } {
  if (typeof window === "undefined") {
    return { productId: "", productName: "" };
  }
  const params = new URLSearchParams(window.location.search);
  return {
    productId: params.get("productId")?.trim() ?? "",
    productName: params.get("productName")?.trim() ?? "",
  };
}

export function InventoryPage() {
  const { notify } = useNotifications();
  const initialProductFilter = useMemo(() => readInitialProductFilter(), []);
  const [page, setPage] = useState(1);
  const [filterProductId, setFilterProductId] = useState(initialProductFilter.productId);
  const [filterProductName, setFilterProductName] = useState(initialProductFilter.productName);
  const [movementType, setMovementType] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [showStockDialog, setShowStockDialog] = useState(false);
  const [dialogMode, setDialogMode] = useState<StockDialogMode>("stock-in");
  const [form, setForm] = useState<StockFormState>(emptyStockForm);
  const [formError, setFormError] = useState<string | null>(null);
  const [writeMessage, setWriteMessage] = useState<string | null>(null);
  const [isSavingStock, setIsSavingStock] = useState(false);
  const lowStockQuery = useQuery({
    queryKey: ["inventory-low-stock"],
    queryFn: fetchLowStockProducts,
  });
  const movementsQuery = useQuery({
    queryKey: ["inventory-movements", page, filterProductId, movementType, from, to],
    queryFn: () =>
      fetchStockMovements({
        productId: filterProductId || undefined,
        movementType: movementType || undefined,
        from: from || undefined,
        to: to || undefined,
        page,
        pageSize: 25,
      }),
  });
  const productLookupQuery = useQuery({
    queryKey: [
      "inventory-stock-write-products",
      form.productSearch,
      showStockDialog,
    ],
    queryFn: () =>
      fetchProducts({
        search: form.productSearch.trim() || undefined,
        isActive: true,
        page: 1,
        pageSize: 25,
      }),
    enabled: showStockDialog,
  });
  useEffect(() => {
    if (
      movementsQuery.data &&
      movementsQuery.data.totalPages > 0 &&
      page > movementsQuery.data.totalPages
    )
      setPage(movementsQuery.data.totalPages);
  }, [page, movementsQuery.data]);
  const lowStockSummary = useMemo(() => {
    const items = lowStockQuery.data ?? [];
    return {
      total: items.length,
      out: items.filter((item) => item.stockOnHand <= 0).length,
      low: items.filter((item) => item.stockOnHand > 0).length,
    };
  }, [lowStockQuery.data]);
  const selectedProduct = useMemo<ProductListItem | null>(
    () =>
      (productLookupQuery.data?.items ?? []).find(
        (item) => item.id === form.productId,
      ) ?? null,
    [form.productId, productLookupQuery.data],
  );
  function refreshAll() {
    void lowStockQuery.refetch();
    void movementsQuery.refetch();
    void productLookupQuery.refetch();
  }
  function resetStockFilters() {
    setPage(1);
    setFilterProductId("");
    setFilterProductName("");
    setMovementType("");
    setFrom("");
    setTo("");
    if (typeof window !== "undefined") {
      const url = new URL(window.location.href);
      url.searchParams.delete("productId");
      url.searchParams.delete("productName");
      window.history.replaceState(window.history.state, "", `${url.pathname}${url.search}${url.hash}`);
    }
  }
  function updateForm<K extends keyof StockFormState>(
    key: K,
    value: StockFormState[K],
  ) {
    setForm((current) => ({ ...current, [key]: value }));
    setFormError(null);
  }
  function openStockDialog(mode: StockDialogMode) {
    setDialogMode(mode);
    setForm({ ...emptyStockForm, direction: "add" });
    setFormError(null);
    setWriteMessage(null);
    setShowStockDialog(true);
  }
  function validateStockForm(): string | null {
    if (!form.productId) return "Product is required.";
    if (toNumber(form.quantity) <= 0)
      return dialogMode === "stock-in"
        ? "Stock-In quantity must be greater than zero."
        : "Adjustment quantity must be greater than zero.";
    if (!form.reason.trim())
      return dialogMode === "stock-in"
        ? "Stock-In reason is required."
        : "Adjustment reason is required.";
    if (
      dialogMode === "adjustment" &&
      form.direction === "remove" &&
      selectedProduct &&
      toNumber(form.quantity) > selectedProduct.stockOnHand
    )
      return "Invalid adjustment. Stock cannot go below zero.";
    return null;
  }
  async function submitStockWrite() {
    const validation = validateStockForm();
    if (validation) {
      setFormError(validation);
      notify.warning("Stock movement was not saved", validation);
      return;
    }
    setIsSavingStock(true);
    setFormError(null);
    setWriteMessage(null);
    try {
      const result =
        dialogMode === "stock-in"
          ? await submitStockIn({
              productId: form.productId,
              quantity: toNumber(form.quantity),
              reason: form.reason.trim(),
              reference: form.reference.trim() || null,
            })
          : await submitStockAdjustment({
              productId: form.productId,
              direction: form.direction,
              quantity: toNumber(form.quantity),
              reason: form.reason.trim(),
              reference: form.reference.trim() || null,
            });
      setWriteMessage(
        `${result.message} ${result.product.name} is now at ${formatQuantity(result.product.stockOnHand)}.`,
      );
      notify.success(
        dialogMode === "stock-in" ? "Stock-In completed" : "Stock adjustment completed",
        `${result.product.name} is now at ${formatQuantity(result.product.stockOnHand)}.`,
      );
      setShowStockDialog(false);
      setMovementType(result.movementType);
      setPage(1);
      refreshAll();
    } catch (error) {
      const description = describeInventoryWriteError(error);
      setFormError(description);
      notify.error("Stock movement failed", safeNotificationDescription(new Error(description), "No stock movement was recorded."));
    } finally {
      setIsSavingStock(false);
    }
  }
  const lowStockColumns = [
    {
      key: "product",
      header: "Product",
      render: (row: LowStockProduct) => (
        <div>
          <p className="font-semibold text-slate-900">{row.productName}</p>
          <p className="mt-1 text-xs text-slate-500">
            Barcode {row.barcode ?? "—"}
          </p>
        </div>
      ),
    },
    {
      key: "stock",
      header: "Stock on hand",
      align: "right" as const,
      render: (row: LowStockProduct) =>
        `${formatQuantity(row.stockOnHand)} ${row.unitName}`,
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
      render: (row: LowStockProduct) => (
        <StatusPill tone={lowStockTone(row)}>
          {row.stockOnHand <= 0 ? "Out of stock" : "Low stock"}
        </StatusPill>
      ),
    },
  ];
  const movementColumns = [
    {
      key: "when",
      header: "Date",
      render: (row: StockMovement) => formatDateTime(row.createdAt),
    },
    {
      key: "product",
      header: "Product",
      render: (row: StockMovement) => row.productName,
    },
    {
      key: "type",
      header: "Type",
      render: (row: StockMovement) => (
        <StatusPill tone={movementTone(row.movementType)}>
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
    {
      key: "user",
      header: "User",
      render: (row: StockMovement) => row.userDisplayName,
    },
    {
      key: "reference",
      header: "Reference",
      render: (row: StockMovement) =>
        row.referenceType
          ? `${row.referenceType} ${row.referenceId ?? ""}`
          : "—",
    },
    {
      key: "reason",
      header: "Reason",
      render: (row: StockMovement) => row.reason ?? "—",
    },
  ];

  return (
    <div className="space-y-4 overflow-hidden">
      <CommandBar
        title="Stock Movements"
        subtitle="Change product quantity through a ledger entry."
        status={
          <div className="flex flex-wrap gap-2">
            <StatusPill
              tone={lowStockSummary.total > 0 ? "warning" : "success"}
            >
              {lowStockSummary.total} attention item(s)
            </StatusPill>
            {lowStockSummary.out > 0 ? (
              <StatusPill tone="danger">{lowStockSummary.out} out</StatusPill>
            ) : null}
          </div>
        }
        actions={
          <div className="flex flex-wrap gap-2">
            <Button
              variant="primary"
              onClick={() => openStockDialog("stock-in")}
            >
              Stock-In
            </Button>
            <Button
              variant="secondary"
              onClick={() => openStockDialog("adjustment")}
            >
              Stock Adjustment
            </Button>
            <Button
              variant="secondary"
              onClick={refreshAll}
              disabled={lowStockQuery.isFetching || movementsQuery.isFetching}
            >
              {lowStockQuery.isFetching || movementsQuery.isFetching
                ? "Refreshing..."
                : "Refresh"}
            </Button>
          </div>
        }
      />
      <Alert tone="info" title="Recorded stock changes">
        Quantity changes are recorded as stock movements.
      </Alert>
      {writeMessage ? (
        <Alert
          tone={
            writeMessage.includes("denied") || writeMessage.includes("required")
              ? "warning"
              : "success"
          }
        >
          {writeMessage}
        </Alert>
      ) : null}
      <div className="grid min-h-0 gap-4 xl:h-[calc(100vh-13rem)] xl:min-h-[34rem] xl:grid-cols-[minmax(0,0.9fr)_minmax(0,1.3fr)]">
        <Card className="flex min-h-[24rem] min-w-0 flex-col xl:min-h-0">
          <CardHeader className="shrink-0">
            <div className="flex items-center justify-between gap-3">
              <CardTitle>Stock attention</CardTitle>
              <StatusPill
                tone={lowStockSummary.total > 0 ? "warning" : "success"}
              >
                {lowStockSummary.total > 0 ? "Review" : "Clear"}
              </StatusPill>
            </div>
          </CardHeader>
          <CardBody className="min-h-0 flex-1 overflow-y-auto overscroll-contain">
            {lowStockQuery.isLoading ? (
              <LoadingState label="Loading low stock..." />
            ) : null}
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
        <Card className="flex min-h-[28rem] min-w-0 flex-col xl:min-h-0">
          <CardHeader className="shrink-0">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <CardTitle>Movement ledger</CardTitle>
              {filterProductId ? (
                <StatusPill tone="primary">
                  Filtered: {filterProductName || filterProductId}
                </StatusPill>
              ) : null}
            </div>
          </CardHeader>
          <CardBody className="flex min-h-0 flex-1 flex-col gap-3 overflow-hidden">
            <div className="min-h-[5.25rem] shrink-0 rounded-lg border border-slate-200 bg-slate-50 p-3">
              <div className="flex flex-wrap gap-3">
                <select
                  className="w-auto min-w-0 flex-[1_1_12rem] rounded-lg border border-slate-300 px-3 py-2 text-sm"
                  value={movementType}
                  onChange={(event) => {
                    setPage(1);
                    setMovementType(event.target.value);
                  }}
                >
                  <option value="">All movement types</option>
                  {movementTypeOptions.map((type) => (
                    <option key={type} value={type}>
                      {type}
                    </option>
                  ))}
                </select>
                <TextInput
                  className="min-w-0 flex-[1_1_11rem]"
                  type="date"
                  aria-label="From date"
                  value={from}
                  onChange={(event) => {
                    setPage(1);
                    setFrom(event.target.value);
                  }}
                />
                <TextInput
                  className="min-w-0 flex-[1_1_11rem]"
                  type="date"
                  aria-label="To date"
                  value={to}
                  onChange={(event) => {
                    setPage(1);
                    setTo(event.target.value);
                  }}
                />
                <Button
                  className="flex-none"
                  variant="secondary"
                  onClick={resetStockFilters}
                  disabled={!filterProductId && !movementType && !from && !to}
                >
                  Clear
                </Button>
              </div>
            </div>
            {movementsQuery.isLoading ? (
              <LoadingState label="Loading movements..." />
            ) : null}
            {movementsQuery.isError ? (
              <ErrorState message={(movementsQuery.error as Error).message} />
            ) : null}
            <div className="min-h-0 flex-1 overflow-y-auto overscroll-contain pr-1">
              {movementsQuery.data ? (
                <DataTable
                  columns={movementColumns}
                  rows={movementsQuery.data.items}
                  getRowKey={(row) => row.id}
                  emptyTitle="No stock movements"
                  emptyDescription="Movement history will appear here when records exist."
                />
              ) : null}
            </div>
            {movementsQuery.data ? (
              <div className="flex shrink-0 items-center justify-between border-t border-slate-100 pt-3 text-sm text-slate-500">
                <span>
                  {movementsQuery.data.totalCount.toLocaleString()} movements ·
                  page {movementsQuery.data.pageNumber} of{" "}
                  {Math.max(1, movementsQuery.data.totalPages || 1)}
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
            ) : null}
          </CardBody>
        </Card>
      </div>
      {showStockDialog ? (
        <Dialog
          title={dialogMode === "stock-in" ? "Stock-In" : "Stock Adjustment"}
          onClose={() => setShowStockDialog(false)}
          footer={
            <>
              <Button
                variant="secondary"
                onClick={() => setShowStockDialog(false)}
                disabled={isSavingStock}
              >
                Cancel
              </Button>
              <Button
                variant="success"
                onClick={() => void submitStockWrite()}
                disabled={isSavingStock || productLookupQuery.isLoading}
              >
                {isSavingStock
                  ? "Saving..."
                  : dialogMode === "stock-in"
                    ? "Record Stock-In"
                    : "Record Adjustment"}
              </Button>
            </>
          }
        >
          <div className="space-y-4 text-sm text-slate-700">
            <Alert
              tone="warning"
              title="Confirm stock change"
            >
              Product Catalog defines the product. Stock Movements changes the quantity. Use Stock-In, adjustments, and
              corrections here so every quantity change is recorded in movement history.
            </Alert>
            {formError ? (
              <Alert tone="danger" title="Stock write validation">
                {formError}
              </Alert>
            ) : null}
            <div className="min-h-[5.25rem] rounded-lg border border-slate-200 bg-slate-50 p-3">
              <div className="grid gap-3 md:grid-cols-[1fr_12rem]">
                <div>
                  <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-500">
                    Product barcode
                  </label>
                  <TextInput
                    value={form.productSearch}
                    onChange={(event) =>
                      updateForm("productSearch", event.target.value)
                    }
                    placeholder="Scan or enter product barcode"
                    autoComplete="off"
                  />
                </div>
                <div>
                  <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-500">
                    Quantity
                  </label>
                  <TextInput
                    value={form.quantity}
                    onChange={(event) =>
                      updateForm("quantity", event.target.value)
                    }
                    placeholder="0"
                    inputMode="decimal"
                  />
                </div>
              </div>
              <p className="mt-2 text-xs text-slate-500">
                Scan or enter the product barcode to select the product, then record the quantity movement.
              </p>
            </div>
            {dialogMode === "adjustment" ? (
              <div>
                <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-500">
                  Adjustment direction
                </label>
                <select
                  className="h-10 w-full rounded-md border border-slate-300 bg-white px-3 text-sm text-slate-900"
                  value={form.direction}
                  onChange={(event) =>
                    updateForm(
                      "direction",
                      event.target.value as AdjustmentDirection,
                    )
                  }
                >
                  <option value="add">Add delta</option>
                  <option value="remove">Remove delta</option>
                </select>
                <p className="mt-1 text-xs text-slate-500">
                  Adjustments add or remove the entered quantity. They do not
                  replace stock with an untracked count.
                </p>
              </div>
            ) : null}
            <div className="grid gap-3 sm:grid-cols-2">
              <div>
                <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-500">
                  Reason
                </label>
                <TextInput
                  value={form.reason}
                  onChange={(event) => updateForm("reason", event.target.value)}
                  placeholder="Required reason"
                />
              </div>
              <div>
                <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-500">
                  Reference
                </label>
                <TextInput
                  value={form.reference}
                  onChange={(event) =>
                    updateForm("reference", event.target.value)
                  }
                  placeholder="Optional reference"
                />
              </div>
            </div>
            <div className="rounded-lg border border-slate-200 bg-white">
              <div className="border-b border-slate-100 px-3 py-2 text-xs font-semibold uppercase tracking-wide text-slate-500">
                Active products
              </div>
              <div className="max-h-56 overflow-y-auto p-2">
                {productLookupQuery.isLoading ? (
                  <LoadingState label="Loading products..." />
                ) : null}
                {productLookupQuery.isError ? (
                  <ErrorState
                    message={(productLookupQuery.error as Error).message}
                  />
                ) : null}
                {productLookupQuery.data?.items.length === 0 ? (
                  <p className="p-3 text-sm text-slate-500">
                    No active product was found for that barcode.
                  </p>
                ) : null}
                {(productLookupQuery.data?.items ?? []).map((product) => (
                  <button
                    key={product.id}
                    type="button"
                    className={`mb-2 w-full rounded-lg border p-3 text-left text-sm ${form.productId === product.id ? "border-slate-900 bg-slate-100" : "border-slate-200 bg-white hover:bg-slate-50"}`}
                    onClick={() => updateForm("productId", product.id)}
                  >
                    <span className="block font-semibold text-slate-900">
                      {product.name}
                    </span>
                    <span className="block text-xs text-slate-500">
                      Barcode {product.barcode ?? "—"} · {product.unitName} ·
                      Stock {formatQuantity(product.stockOnHand)}
                    </span>
                  </button>
                ))}
              </div>
            </div>
            {selectedProduct ? (
              <p className="rounded-md bg-slate-100 p-3 text-xs text-slate-600">
                Selected: {selectedProduct.name}. Current stock:{" "}
                {formatQuantity(selectedProduct.stockOnHand)}. Inactive products
                are excluded from this lookup.
              </p>
            ) : null}
          </div>
        </Dialog>
      ) : null}
    </div>
  );
}
