import { useEffect, useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  fetchProductBrands,
  fetchProductCategories,
  fetchProductDetail,
  fetchProducts,
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
} from "../components/ui";
import { formatDateTime, formatMoney, formatQuantity } from "../utils/format";

function productStockTone(product: ProductListItem): "success" | "warning" | "danger" | "info" | "neutral" {
  if (!product.isActive) {
    return "neutral";
  }

  if (!product.trackStock) {
    return "info";
  }

  if (product.stockOnHand <= 0) {
    return "danger";
  }

  if (product.stockOnHand <= product.reorderLevel) {
    return "warning";
  }

  return "success";
}

function productStockLabel(product: ProductListItem): string {
  if (!product.isActive) {
    return "Inactive";
  }

  if (!product.trackStock) {
    return "Untracked";
  }

  if (product.stockOnHand <= 0) {
    return "Out of stock";
  }

  if (product.stockOnHand <= product.reorderLevel) {
    return "Low stock";
  }

  return "In stock";
}

export function ProductsPage() {
  const [search, setSearch] = useState("");
  const [categoryId, setCategoryId] = useState("");
  const [brandId, setBrandId] = useState("");
  const [statusFilter, setStatusFilter] = useState<"" | "active" | "inactive">("");
  const [page, setPage] = useState(1);
  const [selectedId, setSelectedId] = useState<string | null>(null);

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

  const detailQuery = useQuery({
    queryKey: ["product-detail", selectedId],
    queryFn: () => fetchProductDetail(selectedId!),
    enabled: selectedId !== null,
  });

  useEffect(() => {
    if (productsQuery.data && productsQuery.data.totalPages > 0 && page > productsQuery.data.totalPages) {
      setPage(productsQuery.data.totalPages);
    }
  }, [page, productsQuery.data]);

  useEffect(() => {
    setSelectedId(null);
  }, [search, categoryId, brandId, statusFilter]);

  const activeSummary = useMemo(() => {
    const items = productsQuery.data?.items ?? [];
    const low = items.filter((item) => item.trackStock && item.stockOnHand > 0 && item.stockOnHand <= item.reorderLevel).length;
    const out = items.filter((item) => item.trackStock && item.stockOnHand <= 0).length;
    const untracked = items.filter((item) => !item.trackStock).length;
    return { low, out, untracked };
  }, [productsQuery.data]);

  const columns = [
    {
      key: "product",
      header: "Product",
      render: (row: ProductListItem) => (
        <div>
          <p className="font-semibold text-slate-900">{row.name}</p>
          <p className="mt-1 text-xs text-slate-500">SKU {row.sku ?? "—"} · Barcode {row.barcode ?? "—"}</p>
        </div>
      ),
    },
    { key: "unit", header: "Unit", render: (row: ProductListItem) => row.unitName },
    { key: "category", header: "Category", render: (row: ProductListItem) => row.categoryName ?? "—" },
    { key: "brand", header: "Brand", render: (row: ProductListItem) => row.brandName ?? "—" },
    {
      key: "price",
      header: "Selling",
      align: "right" as const,
      render: (row: ProductListItem) => formatMoney(row.sellingPrice),
    },
    {
      key: "stock",
      header: "Stock",
      align: "right" as const,
      render: (row: ProductListItem) =>
        row.trackStock ? `${formatQuantity(row.stockOnHand)} / reorder ${formatQuantity(row.reorderLevel)}` : "Not tracked",
    },
    {
      key: "state",
      header: "State",
      render: (row: ProductListItem) => <StatusPill tone={productStockTone(row)}>{productStockLabel(row)}</StatusPill>,
    },
  ];

  function updateSearch(value: string) {
    setPage(1);
    setSearch(value);
  }

  function updateCategory(value: string) {
    setPage(1);
    setCategoryId(value);
  }

  function updateBrand(value: string) {
    setPage(1);
    setBrandId(value);
  }

  function updateStatus(value: "" | "active" | "inactive") {
    setPage(1);
    setStatusFilter(value);
  }

  const resultCount = productsQuery.data?.totalCount ?? 0;
  const currentPage = productsQuery.data?.pageNumber ?? page;
  const totalPages = Math.max(1, productsQuery.data?.totalPages || 1);
  const canGoPrevious = currentPage > 1;
  const canGoNext = productsQuery.data ? currentPage < productsQuery.data.totalPages : false;

  return (
    <div className="flex min-h-0 flex-col gap-3 overflow-hidden xl:h-[calc(100vh-9rem)]">
      <div className="shrink-0">
        <CommandBar
          title="Product Catalog"
          subtitle="Stable read-only catalog view backed by the local product API. Catalog write actions are unavailable in this lane."
          status={
            <div className="flex flex-wrap gap-2">
              <StatusPill tone="info">{resultCount.toLocaleString()} records</StatusPill>
              {activeSummary.low > 0 ? <StatusPill tone="warning">{activeSummary.low} low on this page</StatusPill> : null}
              {activeSummary.out > 0 ? <StatusPill tone="danger">{activeSummary.out} out on this page</StatusPill> : null}
            </div>
          }
          actions={
            <Button variant="secondary" onClick={() => void productsQuery.refetch()} disabled={productsQuery.isFetching}>
              {productsQuery.isFetching ? "Refreshing..." : "Refresh"}
            </Button>
          }
        />
      </div>

      <div className="shrink-0">
        <Alert tone="info" title="Read-only catalog lane">
          Product Catalog is a lookup surface only. Product create, edit, deactivate, Fast Add, and image management remain parked.
        </Alert>
      </div>

      <div className="grid min-h-0 flex-1 gap-3 overflow-hidden xl:grid-cols-[minmax(0,1.35fr)_minmax(18rem,0.65fr)]">
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
                  onChange={(event) => updateCategory(event.target.value)}
                >
                  <option value="">All categories</option>
                  {(categoriesQuery.data ?? []).map((item) => (
                    <option key={item.id} value={item.id}>{item.name}</option>
                  ))}
                </select>
                <select
                  className="min-h-9 rounded-md border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900"
                  value={brandId}
                  onChange={(event) => updateBrand(event.target.value)}
                >
                  <option value="">All brands</option>
                  {(brandsQuery.data ?? []).map((item) => (
                    <option key={item.id} value={item.id}>{item.name}</option>
                  ))}
                </select>
                <select
                  className="min-h-9 rounded-md border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900"
                  value={statusFilter}
                  onChange={(event) => updateStatus(event.target.value as "" | "active" | "inactive")}
                >
                  <option value="">All statuses</option>
                  <option value="active">Active</option>
                  <option value="inactive">Inactive</option>
                </select>
              </div>
              <div className="mt-2 flex min-h-5 flex-wrap gap-2 text-xs text-slate-500">
                <span>Page stock: {activeSummary.low} low, {activeSummary.out} out, {activeSummary.untracked} untracked.</span>
                <span>Typing stays in this shell; results scroll internally.</span>
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
                <span>{productsQuery.isFetching && !productsQuery.isLoading ? "Refreshing..." : "Bounded results"}</span>
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
                  <Button variant="secondary" size="sm" disabled={!canGoPrevious} onClick={() => setPage((current) => Math.max(1, current - 1))}>
                    Previous
                  </Button>
                  <Button variant="secondary" size="sm" disabled={!canGoNext} onClick={() => setPage((current) => current + 1)}>
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
              {selectedId ? <Button variant="secondary" size="sm" onClick={() => setSelectedId(null)}>Clear</Button> : null}
            </div>
          </CardHeader>
          <CardBody className="min-h-0 flex-1 overflow-y-auto overscroll-contain text-sm text-slate-600">
            {!selectedId ? (
              <div className="rounded-lg border border-dashed border-slate-300 p-6 text-slate-500">
                Select a product row to view read-only catalog details. This fixed side panel does not push the page down.
              </div>
            ) : null}
            {selectedId && detailQuery.isLoading ? (
              <div className="rounded-lg border border-slate-200 bg-slate-50 p-4 text-slate-500">Loading product detail...</div>
            ) : null}
            {selectedId && detailQuery.isError ? (
              <div className="rounded-lg border border-red-200 bg-red-50 p-4 text-red-700">{(detailQuery.error as Error).message}</div>
            ) : null}
            {selectedId && detailQuery.data ? (
              <div className="space-y-4">
                <div>
                  <h3 className="text-lg font-semibold text-slate-950">{detailQuery.data.name}</h3>
                  <div className="mt-2 flex flex-wrap gap-2">
                    <StatusPill tone={detailQuery.data.isActive ? "success" : "neutral"}>{detailQuery.data.isActive ? "Active" : "Inactive"}</StatusPill>
                    <StatusPill tone={detailQuery.data.trackStock ? "primary" : "info"}>{detailQuery.data.trackStock ? "Tracked" : "Untracked"}</StatusPill>
                  </div>
                </div>
                <div className="grid gap-2">
                  <p>SKU: {detailQuery.data.sku ?? "—"}</p>
                  <p>Barcode: {detailQuery.data.barcode ?? "—"}</p>
                  <p>Category: {detailQuery.data.categoryName ?? "—"}</p>
                  <p>Brand: {detailQuery.data.brandName ?? "—"}</p>
                  <p>Unit: {detailQuery.data.unitName}</p>
                  <p>Supplier: {detailQuery.data.supplierName ?? "—"}</p>
                  <p>Selling: {formatMoney(detailQuery.data.sellingPrice)}</p>
                  <p>Cost: {detailQuery.data.costPrice === null ? "—" : formatMoney(detailQuery.data.costPrice)}</p>
                  <p>Wholesale: {detailQuery.data.wholesalePrice === null ? "—" : formatMoney(detailQuery.data.wholesalePrice)}</p>
                  <p>Stock: {formatQuantity(detailQuery.data.stockOnHand)}</p>
                  <p>Reorder: {formatQuantity(detailQuery.data.reorderLevel)}</p>
                  <p>Updated: {detailQuery.data.updatedAt ? formatDateTime(detailQuery.data.updatedAt) : "—"}</p>
                </div>
                {detailQuery.data.description ? <p>{detailQuery.data.description}</p> : null}
                {detailQuery.data.notes ? <p>Notes: {detailQuery.data.notes}</p> : null}
              </div>
            ) : null}
          </CardBody>
        </Card>
      </div>
    </div>
  );
}
