import { type ReactNode, useEffect, useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ApiError } from "../api/http";
import {
  submitStockAdjustment,
  submitStockIn,
} from "../api/inventoryManagement";
import {
  createManagedProduct,
  deactivateManagedProduct,
  updateManagedProduct,
  type ProductManagementWriteRequest,
} from "../api/productManagement";
import {
  fetchProductBrands,
  fetchProductCategories,
  fetchProductDetail,
  fetchProducts,
  fetchProductUnits,
  type ProductDetail,
  type ProductListItem,
} from "../api/products";
import {
  Alert,
  Button,
  Card,
  CardBody,
  CardHeader,
  CardTitle,
  CommandBar,
  DataTable,
  StatusPill,
  TextInput,
  useNotifications,
} from "../components/ui";
import { formatDateTime, formatMoney, formatQuantity } from "../utils/format";
import { safeNotificationDescription } from "../utils/notificationError";

type ProductFormMode = "create" | "edit";
type CatalogStockMode = "stock-in" | "adjustment";
type AdjustmentDirection = "add" | "remove";
type ProductFormState = {
  productName: string;
  barcode: string;
  unitId: string;
  categoryId: string;
  brandId: string;
  sellingPrice: string;
  costPrice: string;
  reorderLevel: string;
};
type CatalogStockFormState = {
  quantity: string;
  reason: string;
  reference: string;
  direction: AdjustmentDirection;
};
const emptyForm: ProductFormState = {
  productName: "",
  barcode: "",
  unitId: "",
  categoryId: "",
  brandId: "",
  sellingPrice: "",
  costPrice: "",
  reorderLevel: "0",
};
const emptyStockForm: CatalogStockFormState = {
  quantity: "",
  reason: "",
  reference: "",
  direction: "add",
};

function productStockTone(
  product: ProductListItem | ProductDetail,
): "success" | "warning" | "danger" | "info" | "neutral" {
  if (!product.isActive) return "neutral";
  if (!product.trackStock) return "info";
  if (product.stockOnHand <= 0) return "danger";
  if (product.stockOnHand <= product.reorderLevel) return "warning";
  return "success";
}
function productStockLabel(product: ProductListItem | ProductDetail): string {
  if (!product.isActive) return "Inactive";
  if (!product.trackStock) return "Untracked";
  if (product.stockOnHand <= 0) return "Out of stock";
  if (product.stockOnHand <= product.reorderLevel) return "Low stock";
  return "In stock";
}
function toNumber(value: string): number {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : 0;
}
function optionalNumber(value: string): number | null {
  return value.trim() === "" ? null : toNumber(value);
}
function describeProductWriteError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 401) return "Session expired or login required.";
    if (error.status === 403)
      return "Permission denied. Product management requires Products.Manage.";
    if (error.code === "Products.DuplicateBarcode")
      return "Duplicate barcode. An active product already uses this barcode.";
    return error.message;
  }
  return error instanceof Error
    ? error.message
    : "Unexpected product write error.";
}
function describeCatalogStockError(error: unknown): string {
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
    : "Unexpected stock write error.";
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
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/60 p-4"
      role="dialog"
      aria-modal="true"
    >
      <div className="max-h-[90vh] w-full max-w-2xl overflow-hidden rounded-xl bg-white shadow-2xl">
        <div className="flex items-start justify-between gap-4 border-b border-slate-200 px-5 py-4">
          <div>
            <h2 className="text-lg font-semibold text-slate-950">{title}</h2>
            <p className="mt-1 text-xs text-slate-500">
              Product Catalog is the operator owner screen for product edit,
              deactivate, Stock-In, stock adjustment, and movement-path
              guidance.
            </p>
          </div>
          <Button variant="ghost" size="sm" onClick={onClose}>
            Close
          </Button>
        </div>
        <div className="max-h-[62vh] overflow-y-auto px-5 py-4">{children}</div>
        <div className="flex flex-wrap justify-end gap-2 border-t border-slate-200 bg-slate-50 px-5 py-4">
          {footer}
        </div>
      </div>
    </div>
  );
}

export function ProductsPage() {
  const { notify } = useNotifications();
  const [search, setSearch] = useState("");
  const [categoryId, setCategoryId] = useState("");
  const [brandId, setBrandId] = useState("");
  const [statusFilter, setStatusFilter] = useState<"" | "active" | "inactive">(
    "",
  );
  const [page, setPage] = useState(1);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [showProductForm, setShowProductForm] = useState(false);
  const [formMode, setFormMode] = useState<ProductFormMode>("create");
  const [formProductId, setFormProductId] = useState<string | null>(null);
  const [form, setForm] = useState<ProductFormState>(emptyForm);
  const [formError, setFormError] = useState<string | null>(null);
  const [writeMessage, setWriteMessage] = useState<string | null>(null);
  const [isSavingProduct, setIsSavingProduct] = useState(false);
  const [deactivateReason, setDeactivateReason] = useState(
    "Product deactivated from Product Catalog.",
  );
  const [showDeactivateConfirm, setShowDeactivateConfirm] = useState(false);
  const [isDeactivating, setIsDeactivating] = useState(false);
  const [showStockDialog, setShowStockDialog] = useState(false);
  const [stockMode, setStockMode] = useState<CatalogStockMode>("stock-in");
  const [stockForm, setStockForm] =
    useState<CatalogStockFormState>(emptyStockForm);
  const [stockError, setStockError] = useState<string | null>(null);
  const [isSavingStock, setIsSavingStock] = useState(false);
  const isActive = statusFilter === "" ? undefined : statusFilter === "active";
  const productsQuery = useQuery({
    queryKey: ["products", search, categoryId, brandId, statusFilter, page],
    queryFn: () =>
      fetchProducts({
        search: search.trim() || undefined,
        categoryId: categoryId || undefined,
        brandId: brandId || undefined,
        isActive,
        page,
        pageSize: 25,
      }),
  });
  const categoriesQuery = useQuery({
    queryKey: ["product-categories"],
    queryFn: fetchProductCategories,
  });
  const brandsQuery = useQuery({
    queryKey: ["product-brands"],
    queryFn: fetchProductBrands,
  });
  const unitsQuery = useQuery({
    queryKey: ["product-units"],
    queryFn: fetchProductUnits,
  });
  const detailQuery = useQuery({
    queryKey: ["product-detail", selectedId],
    queryFn: () => fetchProductDetail(selectedId!),
    enabled: selectedId !== null,
  });

  useEffect(() => {
    if (
      productsQuery.data &&
      productsQuery.data.totalPages > 0 &&
      page > productsQuery.data.totalPages
    )
      setPage(productsQuery.data.totalPages);
  }, [page, productsQuery.data]);
  useEffect(() => {
    setSelectedId(null);
  }, [search, categoryId, brandId, statusFilter]);
  useEffect(() => {
    if (
      showProductForm &&
      !form.unitId &&
      unitsQuery.data &&
      unitsQuery.data.length > 0
    )
      setForm((current) => ({ ...current, unitId: unitsQuery.data[0].id }));
  }, [form.unitId, showProductForm, unitsQuery.data]);
  const activeSummary = useMemo(() => {
    const items = productsQuery.data?.items ?? [];
    return {
      low: items.filter(
        (item) =>
          item.trackStock &&
          item.stockOnHand > 0 &&
          item.stockOnHand <= item.reorderLevel,
      ).length,
      out: items.filter((item) => item.trackStock && item.stockOnHand <= 0)
        .length,
      untracked: items.filter((item) => !item.trackStock).length,
    };
  }, [productsQuery.data]);
  const columns = [
    {
      key: "product",
      header: "Product",
      render: (row: ProductListItem) => (
        <div>
          <p className="font-semibold text-slate-900">{row.name}</p>
          <p className="mt-1 text-xs text-slate-500">
            SKU {row.sku ?? "—"} · Barcode {row.barcode ?? "—"}
          </p>
        </div>
      ),
    },
    {
      key: "unit",
      header: "Unit of measure",
      render: (row: ProductListItem) => row.unitName,
    },
    {
      key: "category",
      header: "Category",
      render: (row: ProductListItem) => row.categoryName ?? "No category",
    },
    {
      key: "brand",
      header: "Brand",
      render: (row: ProductListItem) => row.brandName ?? "No brand",
    },
    {
      key: "price",
      header: "Selling",
      align: "right" as const,
      render: (row: ProductListItem) => formatMoney(row.sellingPrice),
    },
    {
      key: "stock",
      header: "Stock on hand",
      align: "right" as const,
      render: (row: ProductListItem) =>
        row.trackStock
          ? `${formatQuantity(row.stockOnHand)} ${row.unitName} / reorder ${formatQuantity(row.reorderLevel)}`
          : "Not tracked",
    },
    {
      key: "state",
      header: "State",
      render: (row: ProductListItem) => (
        <StatusPill tone={productStockTone(row)}>
          {productStockLabel(row)}
        </StatusPill>
      ),
    },
  ];
  function updateSearch(value: string) {
    setPage(1);
    setSearch(value);
  }
  function updateForm<K extends keyof ProductFormState>(
    key: K,
    value: ProductFormState[K],
  ) {
    setForm((current) => ({ ...current, [key]: value }));
    setFormError(null);
  }
  function updateStockForm<K extends keyof CatalogStockFormState>(
    key: K,
    value: CatalogStockFormState[K],
  ) {
    setStockForm((current) => ({ ...current, [key]: value }));
    setStockError(null);
  }
  function openCreateProduct() {
    setFormMode("create");
    setFormProductId(null);
    setForm({ ...emptyForm, unitId: unitsQuery.data?.[0]?.id ?? "" });
    setFormError(null);
    setShowProductForm(true);
  }
  function openEditProduct(detail: ProductDetail) {
    setFormMode("edit");
    setFormProductId(detail.id);
    setForm({
      productName: detail.name,
      barcode: detail.barcode ?? "",
      unitId: detail.unitId,
      categoryId: detail.categoryId ?? "",
      brandId: detail.brandId ?? "",
      sellingPrice: String(detail.sellingPrice),
      costPrice: detail.costPrice === null ? "" : String(detail.costPrice),
      reorderLevel: String(detail.reorderLevel),
    });
    setFormError(null);
    setShowProductForm(true);
  }
  function openCatalogStockDialog(
    mode: CatalogStockMode,
    detail: ProductDetail,
  ) {
    if (!detail.isActive) {
      setWriteMessage("Inactive products cannot receive stock movements.");
      notify.warning("Stock movement unavailable", "Inactive products cannot receive stock movements.");
      return;
    }
    if (!detail.trackStock) {
      setWriteMessage(
        "This product is not stock-tracked. Stock-In and Adjustment are unavailable.",
      );
      notify.warning("Stock movement unavailable", "This product is not stock-tracked.");
      return;
    }
    setStockMode(mode);
    setStockForm({ ...emptyStockForm, direction: "add" });
    setStockError(null);
    setWriteMessage(null);
    setShowStockDialog(true);
  }
  function validateForm(): string | null {
    if (!form.productName.trim()) return "Product name is required.";
    if (!form.barcode.trim()) return "Barcode is required.";
    if (!form.unitId) return "Unit is required.";
    if (toNumber(form.sellingPrice) <= 0)
      return "Selling price must be greater than zero.";
    if (form.costPrice.trim() !== "" && toNumber(form.costPrice) < 0)
      return "Cost price cannot be negative.";
    if (toNumber(form.reorderLevel) < 0)
      return "Reorder level cannot be negative.";
    return null;
  }
  function validateStockForm(detail: ProductDetail): string | null {
    const quantity = toNumber(stockForm.quantity);
    if (quantity <= 0)
      return stockMode === "stock-in"
        ? "Stock-In quantity must be greater than zero."
        : "Adjustment quantity must be greater than zero.";
    if (!stockForm.reason.trim())
      return stockMode === "stock-in"
        ? "Stock-In reason is required."
        : "Adjustment reason is required.";
    if (
      stockMode === "adjustment" &&
      stockForm.direction === "remove" &&
      quantity > detail.stockOnHand
    )
      return "Invalid adjustment. Stock cannot go below zero.";
    return null;
  }
  function toWriteRequest(): ProductManagementWriteRequest {
    return {
      productName: form.productName.trim(),
      barcode: form.barcode.trim(),
      unitId: form.unitId,
      categoryId: form.categoryId || null,
      brandId: form.brandId || null,
      sellingPrice: toNumber(form.sellingPrice),
      costPrice: optionalNumber(form.costPrice),
      reorderLevel: toNumber(form.reorderLevel),
    };
  }
  async function refreshCatalog(productId?: string) {
    await Promise.all([
      productsQuery.refetch(),
      categoriesQuery.refetch(),
      brandsQuery.refetch(),
      unitsQuery.refetch(),
    ]);
    if (productId) {
      setSelectedId(productId);
      await detailQuery.refetch();
    }
  }
  async function submitProductForm() {
    const validation = validateForm();
    if (validation) {
      setFormError(validation);
      notify.warning("Product was not saved", validation);
      return;
    }
    if (formMode === "edit" && !formProductId) {
      setFormError("Product ID is missing for edit.");
      notify.error("Product was not saved", "Select the product again and retry the edit.");
      return;
    }
    setIsSavingProduct(true);
    setFormError(null);
    setWriteMessage(null);
    try {
      const result =
        formMode === "create"
          ? await createManagedProduct(toWriteRequest())
          : await updateManagedProduct(formProductId!, toWriteRequest());
      setWriteMessage(result.message);
      setShowProductForm(false);
      setSelectedId(result.product.id);
      notify.success(
        formMode === "create" ? "Product created" : "Product updated",
        formMode === "create" ? "Use Stock-In to add starting quantity." : "The catalog changes were saved.",
      );
      await refreshCatalog(result.product.id);
    } catch (error) {
      const description = describeProductWriteError(error);
      setFormError(description);
      notify.error("Product was not saved", safeNotificationDescription(new Error(description), "Check the required fields and try again."));
    } finally {
      setIsSavingProduct(false);
    }
  }
  async function submitCatalogStockWrite() {
    const detail = detailQuery.data;
    if (!detail) {
      setStockError("Select a product before recording stock movement.");
      notify.warning("Stock movement was not saved", "Select a product first.");
      return;
    }
    const validation = validateStockForm(detail);
    if (validation) {
      setStockError(validation);
      notify.warning("Stock movement was not saved", validation);
      return;
    }
    setIsSavingStock(true);
    setStockError(null);
    setWriteMessage(null);
    try {
      const result =
        stockMode === "stock-in"
          ? await submitStockIn({
              productId: detail.id,
              quantity: toNumber(stockForm.quantity),
              reason: stockForm.reason.trim(),
              reference: stockForm.reference.trim() || null,
            })
          : await submitStockAdjustment({
              productId: detail.id,
              direction: stockForm.direction,
              quantity: toNumber(stockForm.quantity),
              reason: stockForm.reason.trim(),
              reference: stockForm.reference.trim() || null,
            });
      setWriteMessage(
        `${result.message} ${result.product.name} current stock is ${formatQuantity(result.product.stockOnHand)}.`,
      );
      notify.success(
        stockMode === "stock-in" ? "Stock-In completed" : "Stock adjustment completed",
        `${result.product.name} is now at ${formatQuantity(result.product.stockOnHand)}.`,
      );
      setShowStockDialog(false);
      await refreshCatalog(detail.id);
    } catch (error) {
      const description = describeCatalogStockError(error);
      setStockError(description);
      notify.error("Stock movement failed", safeNotificationDescription(new Error(description), "No stock movement was recorded."));
    } finally {
      setIsSavingStock(false);
    }
  }
  async function deactivateSelectedProduct() {
    if (!selectedId || isDeactivating) return;
    if (!deactivateReason.trim()) {
      setWriteMessage("Deactivate reason is required.");
      return;
    }
    setIsDeactivating(true);
    setWriteMessage(null);
    try {
      const result = await deactivateManagedProduct(
        selectedId,
        deactivateReason.trim(),
      );
      setWriteMessage(
        `${result.message} Product is inactive and hidden from active sale flow. Historical sales remain preserved.`,
      );
      notify.success("Product deactivated", "The product is hidden from active sale flow.");
      setShowDeactivateConfirm(false);
      await productsQuery.refetch();
      await detailQuery.refetch();
    } catch (error) {
      const description = describeProductWriteError(error);
      setWriteMessage(description);
      notify.error("Product was not deactivated", safeNotificationDescription(new Error(description), "The product remains active."));
    } finally {
      setIsDeactivating(false);
    }
  }
  function showMovementPath(detail: ProductDetail) {
    setWriteMessage(
      `View Movements shortcut: open Stock Movements, then review movement ledger entries for ${detail.name} / Product ID ${detail.id}.`,
    );
  }
  const resultCount = productsQuery.data?.totalCount ?? 0;
  const currentPage = productsQuery.data?.pageNumber ?? page;
  const totalPages = Math.max(1, productsQuery.data?.totalPages || 1);
  const canGoPrevious = currentPage > 1;
  const canGoNext = productsQuery.data
    ? currentPage < productsQuery.data.totalPages
    : false;

  return (
    <div className="flex h-[calc(100vh-9rem)] max-h-[calc(100vh-9rem)] min-h-0 flex-col gap-3 overflow-hidden">
      <div className="shrink-0">
        <CommandBar
          title="Product Catalog"
          subtitle="Manage products, pricing, barcodes, and stock settings."
          status={
            <div className="flex flex-wrap gap-2">
              <StatusPill tone="info">
                {resultCount.toLocaleString()} records
              </StatusPill>
              {activeSummary.low > 0 ? (
                <StatusPill tone="warning">
                  {activeSummary.low} low on this page
                </StatusPill>
              ) : null}
              {activeSummary.out > 0 ? (
                <StatusPill tone="danger">
                  {activeSummary.out} out on this page
                </StatusPill>
              ) : null}
            </div>
          }
          actions={
            <div className="flex flex-wrap gap-2">
              <Button variant="primary" onClick={openCreateProduct}>
                Create Product
              </Button>
              <Button
                variant="secondary"
                onClick={() => void productsQuery.refetch()}
                disabled={productsQuery.isFetching}
              >
                {productsQuery.isFetching ? "Refreshing..." : "Refresh"}
              </Button>
            </div>
          }
        />
      </div>
      <div className="shrink-0">
        <Alert tone="info" title="Adding stock">
          Add stock through Stock-In after saving a product.
        </Alert>
      </div>
      {writeMessage ? (
        <Alert
          tone={
            writeMessage.includes("denied") ||
            writeMessage.includes("required") ||
            writeMessage.includes("unavailable")
              ? "warning"
              : "success"
          }
        >
          {writeMessage}
        </Alert>
      ) : null}
      <div className="grid min-h-0 flex-1 grid-rows-[minmax(0,1fr)_minmax(10rem,0.45fr)] gap-3 overflow-hidden xl:grid-cols-[minmax(0,1.35fr)_minmax(18rem,0.65fr)] xl:grid-rows-none">
        <Card className="flex min-h-0 min-w-0 flex-col overflow-hidden">
          <CardHeader className="shrink-0">
            <CardTitle>Catalog search</CardTitle>
          </CardHeader>
          <CardBody className="flex min-h-0 flex-1 flex-col gap-3 overflow-hidden">
            <div className="shrink-0 rounded-lg border border-slate-200 bg-slate-50 p-3">
              <div className="grid gap-3 lg:grid-cols-[minmax(16rem,1fr)_12rem_12rem_10rem]">
                <TextInput
                  placeholder="Search SKU, barcode, or name..."
                  value={search}
                  onChange={(event) => updateSearch(event.target.value)}
                  autoComplete="off"
                />
                <select
                  className="min-h-9 rounded-md border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900"
                  value={categoryId}
                  onChange={(event) => {
                    setPage(1);
                    setCategoryId(event.target.value);
                  }}
                >
                  <option value="">All categories</option>
                  {(categoriesQuery.data ?? []).map((item) => (
                    <option key={item.id} value={item.id}>
                      {item.name}
                    </option>
                  ))}
                </select>
                <select
                  className="min-h-9 rounded-md border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900"
                  value={brandId}
                  onChange={(event) => {
                    setPage(1);
                    setBrandId(event.target.value);
                  }}
                >
                  <option value="">All brands</option>
                  {(brandsQuery.data ?? []).map((item) => (
                    <option key={item.id} value={item.id}>
                      {item.name}
                    </option>
                  ))}
                </select>
                <select
                  className="min-h-9 rounded-md border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900"
                  value={statusFilter}
                  onChange={(event) => {
                    setPage(1);
                    setStatusFilter(
                      event.target.value as "" | "active" | "inactive",
                    );
                  }}
                >
                  <option value="">All statuses</option>
                  <option value="active">Active</option>
                  <option value="inactive">Inactive</option>
                </select>
              </div>
              <div className="mt-2 flex min-h-5 flex-wrap gap-2 text-xs text-slate-500">
                <span>
                  Page stock: {activeSummary.low} low, {activeSummary.out} out,{" "}
                  {activeSummary.untracked} untracked.
                </span>
              </div>
            </div>
            <div className="flex min-h-0 flex-1 flex-col overflow-hidden rounded-lg border border-slate-200">
              <div className="flex h-10 shrink-0 items-center justify-between gap-3 border-b border-slate-200 bg-white px-3 text-xs text-slate-500">
                <span>
                  {productsQuery.isLoading
                    ? "Loading products..."
                    : productsQuery.isError
                      ? `Catalog error: ${(productsQuery.error as Error).message}`
                      : `${resultCount.toLocaleString()} products · page ${currentPage} of ${totalPages}`}
                </span>
                <span>
                  {productsQuery.isFetching && !productsQuery.isLoading
                    ? "Refreshing..."
                    : "Bounded results"}
                </span>
              </div>
              <div className="min-h-0 flex-1 overflow-y-auto overscroll-contain bg-white p-3">
                {productsQuery.data ? (
                  <DataTable
                    columns={columns}
                    rows={productsQuery.data.items}
                    getRowKey={(row) => row.id}
                    onRowClick={(row) => setSelectedId(row.id)}
                    selectedRowKey={selectedId}
                    emptyTitle="No products found"
                    emptyDescription="Try adjusting search, category, brand, or status filters."
                  />
                ) : productsQuery.isLoading ? (
                  <div className="rounded-lg border border-dashed border-slate-300 p-6 text-sm text-slate-500">
                    Loading catalog records inside the fixed results panel.
                  </div>
                ) : productsQuery.isError ? (
                  <div className="rounded-lg border border-red-200 bg-red-50 p-6 text-sm text-red-700">
                    {(productsQuery.error as Error).message}
                  </div>
                ) : null}
              </div>
              <div className="flex h-12 shrink-0 items-center justify-between border-t border-slate-200 bg-white px-3 text-sm text-slate-500">
                <span>{resultCount.toLocaleString()} products</span>
                <div className="flex gap-2">
                  <Button
                    variant="secondary"
                    size="sm"
                    disabled={!canGoPrevious}
                    onClick={() =>
                      setPage((current) => Math.max(1, current - 1))
                    }
                  >
                    Previous
                  </Button>
                  <Button
                    variant="secondary"
                    size="sm"
                    disabled={!canGoNext}
                    onClick={() => setPage((current) => current + 1)}
                  >
                    Next
                  </Button>
                </div>
              </div>
            </div>
          </CardBody>
        </Card>
        <Card className="flex min-h-0 min-w-0 flex-col overflow-hidden">
          <CardHeader className="shrink-0">
            <div className="flex items-center justify-between gap-3">
              <CardTitle>Selected product</CardTitle>
              {selectedId ? (
                <Button
                  variant="secondary"
                  size="sm"
                  onClick={() => setSelectedId(null)}
                >
                  Clear
                </Button>
              ) : null}
            </div>
          </CardHeader>
          <CardBody className="min-h-0 flex-1 overflow-y-auto overscroll-contain text-sm text-slate-600">
            {!selectedId ? (
              <div className="rounded-lg border border-dashed border-slate-300 p-6 text-slate-500">
                Select a product row to edit product metadata, deactivate,
                Stock-In this product, Adjust Stock, or View Movements.
              </div>
            ) : null}
            {selectedId && detailQuery.isLoading ? (
              <div className="rounded-lg border border-slate-200 bg-slate-50 p-4 text-slate-500">
                Loading product detail...
              </div>
            ) : null}
            {selectedId && detailQuery.isError ? (
              <div className="rounded-lg border border-red-200 bg-red-50 p-4 text-red-700">
                {(detailQuery.error as Error).message}
              </div>
            ) : null}
            {selectedId && detailQuery.data ? (
              <div className="space-y-4">
                <div>
                  <h3 className="text-lg font-semibold text-slate-950">
                    {detailQuery.data.name}
                  </h3>
                  <div className="mt-2 flex flex-wrap gap-2">
                    <StatusPill
                      tone={detailQuery.data.isActive ? "success" : "neutral"}
                    >
                      {detailQuery.data.isActive ? "Active" : "Inactive"}
                    </StatusPill>
                    <StatusPill
                      tone={detailQuery.data.trackStock ? "primary" : "info"}
                    >
                      {detailQuery.data.trackStock ? "Tracked" : "Untracked"}
                    </StatusPill>
                    <StatusPill tone={productStockTone(detailQuery.data)}>
                      {productStockLabel(detailQuery.data)}
                    </StatusPill>
                  </div>
                </div>
                <div className="rounded-lg border border-slate-200 bg-white p-3">
                  <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">
                    Current stock
                  </p>
                  <p className="mt-1 text-2xl font-bold text-slate-950">
                    {formatQuantity(detailQuery.data.stockOnHand)}{" "}
                    {detailQuery.data.unitName}
                  </p>
                  <p className="text-xs text-slate-500">
                    Reorder level:{" "}
                    {formatQuantity(detailQuery.data.reorderLevel)} · Stock
                    changes only through Stock-In, Fast Add, Adjustment, or Sale
                    commit.
                  </p>
                </div>
                <div className="grid gap-2">
                  <p>SKU: {detailQuery.data.sku ?? "—"}</p>
                  <p>Barcode: {detailQuery.data.barcode ?? "—"}</p>
                  <p>
                    Category: {detailQuery.data.categoryName ?? "No category"}
                  </p>
                  <p>Brand: {detailQuery.data.brandName ?? "No brand"}</p>
                  <p>Unit: {detailQuery.data.unitName}</p>
                  <p>Supplier: {detailQuery.data.supplierName ?? "—"}</p>
                  <p>Selling: {formatMoney(detailQuery.data.sellingPrice)}</p>
                  <p>
                    Cost:{" "}
                    {detailQuery.data.costPrice === null
                      ? "—"
                      : formatMoney(detailQuery.data.costPrice)}
                  </p>
                  <p>
                    Wholesale:{" "}
                    {detailQuery.data.wholesalePrice === null
                      ? "—"
                      : formatMoney(detailQuery.data.wholesalePrice)}
                  </p>
                  <p>
                    Updated:{" "}
                    {detailQuery.data.updatedAt
                      ? formatDateTime(detailQuery.data.updatedAt)
                      : "—"}
                  </p>
                </div>
                {detailQuery.data.description ? (
                  <p>{detailQuery.data.description}</p>
                ) : null}
                {detailQuery.data.notes ? (
                  <p>Notes: {detailQuery.data.notes}</p>
                ) : null}
                {detailQuery.data.isActive ? (
                  <div className="space-y-3 border-t border-slate-100 pt-3">
                    <div className="flex flex-wrap gap-2">
                      <Button
                        variant="primary"
                        size="sm"
                        onClick={() => openEditProduct(detailQuery.data!)}
                      >
                        Edit Product
                      </Button>
                      <Button
                        variant="secondary"
                        size="sm"
                        onClick={() =>
                          openCatalogStockDialog("stock-in", detailQuery.data!)
                        }
                      >
                        Stock-In this product
                      </Button>
                      <Button
                        variant="secondary"
                        size="sm"
                        onClick={() =>
                          openCatalogStockDialog(
                            "adjustment",
                            detailQuery.data!,
                          )
                        }
                      >
                        Adjust Stock
                      </Button>
                      <Button
                        variant="secondary"
                        size="sm"
                        onClick={() => showMovementPath(detailQuery.data!)}
                      >
                        View Movements
                      </Button>
                      <Button
                        variant="danger"
                        size="sm"
                        onClick={() => setShowDeactivateConfirm(true)}
                        disabled={isDeactivating}
                      >
                        {isDeactivating
                          ? "Deactivating..."
                          : "Deactivate Product"}
                      </Button>
                    </div>
                    <div>
                      <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-500">
                        Deactivate reason
                      </label>
                      <TextInput
                        value={deactivateReason}
                        onChange={(event) =>
                          setDeactivateReason(event.target.value)
                        }
                      />
                    </div>
                  </div>
                ) : (
                  <Alert tone="warning" title="Reactivation unavailable">
                    This product cannot be reactivated from the current catalog
                    screen. No reactivation action is offered until a safe
                    product contract is available.
                  </Alert>
                )}
              </div>
            ) : null}
          </CardBody>
        </Card>
      </div>
      {showProductForm ? (
        <Dialog
          title={formMode === "create" ? "Create Product" : "Edit Product"}
          onClose={() => setShowProductForm(false)}
          footer={
            <>
              <Button
                variant="secondary"
                onClick={() => setShowProductForm(false)}
                disabled={isSavingProduct}
              >
                Cancel
              </Button>
              <Button
                variant="success"
                onClick={() => void submitProductForm()}
                disabled={isSavingProduct || unitsQuery.isLoading}
              >
                {isSavingProduct
                  ? "Saving..."
                  : formMode === "create"
                    ? "Create Product"
                    : "Save Product"}
              </Button>
            </>
          }
        >
          <div className="space-y-4 text-sm text-slate-700">
            <Alert tone="info" title="Add stock after saving">
              Save the product first, then use Stock-In to record its opening quantity.
            </Alert>
            {formError ? (
              <Alert tone="danger" title="Product validation">
                {formError}
              </Alert>
            ) : null}
            {unitsQuery.isError ? (
              <Alert tone="danger" title="Units unavailable">
                {(unitsQuery.error as Error).message}
              </Alert>
            ) : null}
            <div className="grid gap-3 sm:grid-cols-2">
              <div>
                <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-500">
                  Product name
                </label>
                <TextInput
                  value={form.productName}
                  onChange={(event) =>
                    updateForm("productName", event.target.value)
                  }
                  placeholder="Product name"
                />
              </div>
              <div>
                <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-500">
                  Barcode
                </label>
                <TextInput
                  value={form.barcode}
                  onChange={(event) =>
                    updateForm("barcode", event.target.value)
                  }
                  placeholder="Barcode"
                />
              </div>
              <div>
                <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-500">
                  Unit
                </label>
                <select
                  className="h-10 w-full rounded-md border border-slate-300 bg-white px-3 text-sm text-slate-900"
                  value={form.unitId}
                  onChange={(event) => updateForm("unitId", event.target.value)}
                >
                  <option value="">Select unit</option>
                  {(unitsQuery.data ?? []).map((unit) => (
                    <option key={unit.id} value={unit.id}>
                      {unit.name}
                    </option>
                  ))}
                </select>
              </div>
              <div>
                <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-500">
                  Selling price
                </label>
                <TextInput
                  value={form.sellingPrice}
                  onChange={(event) =>
                    updateForm("sellingPrice", event.target.value)
                  }
                  placeholder="0.00"
                  inputMode="decimal"
                />
              </div>
              <div>
                <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-500">
                  Category (optional)
                </label>
                <select
                  className="h-10 w-full rounded-md border border-slate-300 bg-white px-3 text-sm text-slate-900"
                  value={form.categoryId}
                  onChange={(event) =>
                    updateForm("categoryId", event.target.value)
                  }
                >
                  <option value="">No category</option>
                  {(categoriesQuery.data ?? []).map((category) => (
                    <option key={category.id} value={category.id}>
                      {category.name}
                    </option>
                  ))}
                </select>
                <p className="mt-1 text-xs text-slate-500">
                  Optional. No category is accepted.
                </p>
              </div>
              <div>
                <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-500">
                  Brand (optional)
                </label>
                <select
                  className="h-10 w-full rounded-md border border-slate-300 bg-white px-3 text-sm text-slate-900"
                  value={form.brandId}
                  onChange={(event) =>
                    updateForm("brandId", event.target.value)
                  }
                >
                  <option value="">No brand</option>
                  {(brandsQuery.data ?? []).map((brand) => (
                    <option key={brand.id} value={brand.id}>
                      {brand.name}
                    </option>
                  ))}
                </select>
                <p className="mt-1 text-xs text-slate-500">
                  Optional. No brand is accepted.
                </p>
              </div>
              <div>
                <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-500">
                  Cost price
                </label>
                <TextInput
                  value={form.costPrice}
                  onChange={(event) =>
                    updateForm("costPrice", event.target.value)
                  }
                  placeholder="Optional"
                  inputMode="decimal"
                />
              </div>
              <div>
                <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-500">
                  Reorder level
                </label>
                <TextInput
                  value={form.reorderLevel}
                  onChange={(event) =>
                    updateForm("reorderLevel", event.target.value)
                  }
                  placeholder="0"
                  inputMode="decimal"
                />
              </div>
            </div>
            <p className="rounded-md bg-slate-100 p-3 text-xs text-slate-600">
              Duplicate active barcodes are rejected. Editing a product does not
              rewrite historical receipt or sale item snapshots.
            </p>
            <Alert tone="info" title="Unit of measure and stock are separate">
              Unit of measure describes how the item is sold or counted, such as
              piece, pack, box, or kilogram. Product details do not change stock
              on hand; use Stock-In or Stock Adjustment so every quantity change
              is recorded.
            </Alert>
          </div>
        </Dialog>
      ) : null}
      {showDeactivateConfirm && detailQuery.data ? (
        <Dialog
          title="Confirm product deactivation"
          onClose={() => setShowDeactivateConfirm(false)}
          footer={
            <>
              <Button
                variant="secondary"
                onClick={() => setShowDeactivateConfirm(false)}
                disabled={isDeactivating}
              >
                Cancel
              </Button>
              <Button
                variant="danger"
                onClick={() => void deactivateSelectedProduct()}
                disabled={isDeactivating || !deactivateReason.trim()}
              >
                {isDeactivating
                  ? "Deactivating..."
                  : "Confirm Deactivate Product"}
              </Button>
            </>
          }
        >
          <div className="space-y-4 text-sm text-slate-700">
            <Alert tone="warning" title="This does not delete history">
              Deactivating this product makes it inactive and hidden from the
              active sale flow. Historical sales remain preserved. This does not
              delete past records or rewrite saved receipt snapshots.
            </Alert>
            <div className="rounded-lg border border-slate-200 bg-slate-50 p-3">
              <p className="font-semibold text-slate-950">
                {detailQuery.data.name}
              </p>
              <p className="text-xs text-slate-500">
                Barcode {detailQuery.data.barcode ?? "—"} · Current stock{" "}
                {formatQuantity(detailQuery.data.stockOnHand)}{" "}
                {detailQuery.data.unitName}
              </p>
            </div>
            <label className="block text-xs font-semibold uppercase tracking-wide text-slate-500">
              Reason required
            </label>
            <TextInput
              value={deactivateReason}
              onChange={(event) => setDeactivateReason(event.target.value)}
            />
          </div>
        </Dialog>
      ) : null}
      {showStockDialog && detailQuery.data ? (
        <Dialog
          title={
            stockMode === "stock-in" ? "Stock-In this product" : "Adjust Stock"
          }
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
                onClick={() => void submitCatalogStockWrite()}
                disabled={isSavingStock}
              >
                {isSavingStock
                  ? "Saving..."
                  : stockMode === "stock-in"
                    ? "Record Stock-In"
                    : "Record Adjustment"}
              </Button>
            </>
          }
        >
          <div className="space-y-4 text-sm text-slate-700">
            <Alert tone="warning" title="Recorded inventory movement">
              Stock-In and Stock Adjustment do not create sales, payments, or
              receipts. Every quantity change creates an inventory movement entry for the selected product.
            </Alert>
            {stockError ? (
              <Alert tone="danger" title="Stock write validation">
                {stockError}
              </Alert>
            ) : null}
            <div className="rounded-lg border border-slate-200 bg-slate-50 p-3">
              <p className="font-semibold text-slate-950">
                {detailQuery.data.name}
              </p>
              <p className="text-xs text-slate-500">
                Barcode {detailQuery.data.barcode ?? "—"} · Current stock{" "}
                {formatQuantity(detailQuery.data.stockOnHand)}{" "}
                {detailQuery.data.unitName}
              </p>
            </div>
            {stockMode === "adjustment" ? (
              <div>
                <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-500">
                  Adjustment direction
                </label>
                <select
                  className="h-10 w-full rounded-md border border-slate-300 bg-white px-3 text-sm text-slate-900"
                  value={stockForm.direction}
                  onChange={(event) =>
                    updateStockForm(
                      "direction",
                      event.target.value as AdjustmentDirection,
                    )
                  }
                >
                  <option value="add">AdjustmentAdd</option>
                  <option value="remove">AdjustmentRemove</option>
                </select>
                <p className="mt-1 text-xs text-slate-500">
                  Adjustments add or remove the entered quantity; they do not
                  replace stock with a new count.
                </p>
              </div>
            ) : null}
            <div className="grid gap-3 sm:grid-cols-2">
              <div>
                <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-500">
                  Quantity
                </label>
                <TextInput
                  value={stockForm.quantity}
                  onChange={(event) =>
                    updateStockForm("quantity", event.target.value)
                  }
                  placeholder="0"
                  inputMode="decimal"
                />
              </div>
              <div>
                <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-500">
                  Reason
                </label>
                <TextInput
                  value={stockForm.reason}
                  onChange={(event) =>
                    updateStockForm("reason", event.target.value)
                  }
                  placeholder="Required reason"
                />
              </div>
              <div className="sm:col-span-2">
                <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-500">
                  Reference
                </label>
                <TextInput
                  value={stockForm.reference}
                  onChange={(event) =>
                    updateStockForm("reference", event.target.value)
                  }
                  placeholder="Optional reference"
                />
              </div>
            </div>
          </div>
        </Dialog>
      ) : null}
    </div>
  );
}
