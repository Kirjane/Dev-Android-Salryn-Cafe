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
  ErrorState,
  LoadingState,
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

  return (
    <div className="space-y-4 overflow-hidden">
      <CommandBar
        title="Product Catalog"
        subtitle="Stable read-only catalog view backed by the local product API. Catalog write actions are unavailable in this lane."
        status={
          productsQuery.data ? (
            <div className="flex flex-wrap gap-2">
              <StatusPill tone="info">{productsQuery.data.totalCount.toLocaleString()} records</StatusPill>
              {activeSummary.low > 0 ? <StatusPill tone="warning">{activeSummary.low} low on this page</StatusPill> : null}
              {activeSummary.out > 0 ? <StatusPill tone="danger">{activeSummary.out} out on this page</StatusPill> : null}
            </div>
          ) : null
        }
        actions={
          <Button variant="secondary" onClick={() => void productsQuery.refetch()} disabled={productsQuery.isFetching}>
            {productsQuery.isFetching ? "Refreshing..." : "Refresh"}
          </Button>
        }
      />

      <Alert tone="info" title="Read-only catalog lane">
        This page is a lookup catalog only. Product create, edit, deactivate, Fast Add, and image management remain parked.
      </Alert>

      <div className="grid min-h-0 gap-4 xl:h-[calc(100vh-13rem)] xl:min-h-[34rem] xl:grid-cols-[minmax(0,1.25fr)_minmax(18rem,0.75fr)]">
        <Card className="flex min-h-[32rem] min-w-0 flex-col xl:min-h-0">
          <CardHeader className="shrink-0">
            <CardTitle>Catalog search</CardTitle>
          </CardHeader>
          <CardBody className="flex min-h-0 flex-1 flex-col gap-3 overflow-hidden">
            <div className="grid shrink-0 gap-3 lg:grid-cols-[minmax(16rem,1fr)_12rem_12rem_10rem]">
              <TextInput
                placeholder="Search SKU, barcode, or name..."
                value={search}
                onChange={(event) => {
                  setPage(1);
                  setSearch(event.target.value);
                }}
                autoComplete="off"
              />
              <select
                className="rounded-lg border border-slate-300 px-3 py-2 text-sm"
                value={categoryId}
                onChange={(event) => {
                  setPage(1);
                  setCategoryId(event.target.value);
                }}
              >
                <option value="">All categories</option>
                {(categoriesQuery.data ?? []).map((item) => (
                  <option key={item.id} value={item.id}>{item.name}</option>
                ))}
              </select>
              <select
                className="rounded-lg border border-slate-300 px-3 py-2 text-sm"
                value={brandId}
                onChange={(event) => {
                  setPage(1);
                  setBrandId(event.target.value);
                }}
              >
                <option value="">All brands</option>
                {(brandsQuery.data ?? []).map((item) => (
                  <option key={item.id} value={item.id}>{item.name}</option>
                ))}
              </select>
              <select
                className="rounded-lg border border-slate-300 px-3 py-2 text-sm"
                value={statusFilter}
                onChange={(event) => {
                  setPage(1);
                  setStatusFilter(event.target.value as "" | "active" | "inactive");
                }}
              >
                <option value="">All statuses</option>
                <option value="active">Active</option>
                <option value="inactive">Inactive</option>
              </select>
            </div>

            <div className="flex shrink-0 flex-wrap gap-2 text-xs text-slate-500">
              <span>Current page stock: {activeSummary.low} low, {activeSummary.out} out, {activeSummary.untracked} untracked.</span>
              <span>Results are bounded so typing does not push the page.</span>
            </div>

            {productsQuery.isLoading ? <LoadingState label="Loading products..." /> : null}
            {productsQuery.isError ? <ErrorState message={(productsQuery.error as Error).message} /> : null}

            <div className="min-h-0 flex-1 overflow-y-auto overscroll-contain pr-1">
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
              ) : null}
            </div>

            {productsQuery.data ? (
              <div className="flex shrink-0 items-center justify-between border-t border-slate-100 pt-3 text-sm text-slate-500">
                <span>
                  {productsQuery.data.totalCount.toLocaleString()} products · page {productsQuery.data.pageNumber} of {Math.max(1, productsQuery.data.totalPages || 1)}
                </span>
                <div className="flex gap-2">
                  <Button variant="secondary" size="sm" disabled={page <= 1} onClick={() => setPage((current) => current - 1)}>
                    Previous
                  </Button>
                  <Button variant="secondary" size="sm" disabled={page >= productsQuery.data.totalPages} onClick={() => setPage((current) => current + 1)}>
                    Next
                  </Button>
                </div>
              </div>
            ) : null}
          </CardBody>
        </Card>

        <Card className="flex min-h-[24rem] min-w-0 flex-col xl:min-h-0">
          <CardHeader className="shrink-0">
            <div className="flex items-center justify-between gap-3">
              <CardTitle>Selected product</CardTitle>
              {selectedId ? <Button variant="secondary" size="sm" onClick={() => setSelectedId(null)}>Clear</Button> : null}
            </div>
          </CardHeader>
          <CardBody className="min-h-0 flex-1 overflow-y-auto overscroll-contain text-sm text-slate-600">
            {!selectedId ? (
              <div className="rounded-lg border border-dashed border-slate-300 p-6 text-slate-500">
                Select a product row to view read-only catalog details. This panel does not push the page down.
              </div>
            ) : null}
            {selectedId && detailQuery.isLoading ? <LoadingState label="Loading product detail..." /> : null}
            {selectedId && detailQuery.isError ? <ErrorState message={(detailQuery.error as Error).message} /> : null}
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
