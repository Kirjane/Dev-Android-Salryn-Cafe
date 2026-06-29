import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  fetchProductBrands,
  fetchProductCategories,
  fetchProductDetail,
  fetchProducts,
  type ProductListItem,
} from "../api/products";
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
import { formatDateTime, formatMoney, formatQuantity } from "../utils/format";

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
        search: search || undefined,
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

  const columns = [
    { key: "sku", header: "SKU", render: (row: ProductListItem) => row.sku ?? "—" },
    { key: "name", header: "Product", render: (row: ProductListItem) => row.name },
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
      render: (row: ProductListItem) => formatQuantity(row.stockOnHand),
    },
    {
      key: "status",
      header: "Status",
      render: (row: ProductListItem) => (
        <StatusPill tone={row.isActive ? "success" : "neutral"}>
          {row.isActive ? "Active" : "Inactive"}
        </StatusPill>
      ),
    },
  ];

  return (
    <div className="space-y-6">
      <CommandBar
        title="Product Management"
        subtitle="Read-only catalog view backed by the local database."
        actions={
          <Button variant="secondary" onClick={() => void productsQuery.refetch()}>
            Refresh
          </Button>
        }
      />

      <Card>
        <CardBody className="space-y-4">
          <div className="grid gap-3 md:grid-cols-4">
            <TextInput
              placeholder="Search SKU, barcode, or name..."
              value={search}
              onChange={(event) => {
                setPage(1);
                setSearch(event.target.value);
              }}
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
                <option key={item.id} value={item.id}>
                  {item.name}
                </option>
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
                <option key={item.id} value={item.id}>
                  {item.name}
                </option>
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

          {productsQuery.isLoading ? <LoadingState label="Loading products..." /> : null}
          {productsQuery.isError ? (
            <ErrorState message={(productsQuery.error as Error).message} />
          ) : null}

          {productsQuery.data ? (
            <>
              <DataTable
                columns={columns}
                rows={productsQuery.data.items}
                getRowKey={(row) => row.id}
                onRowClick={(row) => setSelectedId(row.id)}
                selectedRowKey={selectedId}
                emptyTitle="No products found"
                emptyDescription="Try adjusting your search or filters."
              />
              <div className="flex items-center justify-between text-sm text-slate-500">
                <span>
                  {productsQuery.data.totalCount.toLocaleString()} products · page{" "}
                  {productsQuery.data.pageNumber} of {productsQuery.data.totalPages || 1}
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
                    disabled={page >= productsQuery.data.totalPages}
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
            <CardTitle>{detailQuery.data.name}</CardTitle>
          </CardHeader>
          <CardBody className="grid gap-2 text-sm text-slate-600 md:grid-cols-2">
            <p>SKU: {detailQuery.data.sku ?? "—"}</p>
            <p>Barcode: {detailQuery.data.barcode ?? "—"}</p>
            <p>Category: {detailQuery.data.categoryName ?? "—"}</p>
            <p>Brand: {detailQuery.data.brandName ?? "—"}</p>
            <p>Unit: {detailQuery.data.unitName}</p>
            <p>Supplier: {detailQuery.data.supplierName ?? "—"}</p>
            <p>Selling: {formatMoney(detailQuery.data.sellingPrice)}</p>
            <p>Stock: {formatQuantity(detailQuery.data.stockOnHand)}</p>
            <p>Reorder: {formatQuantity(detailQuery.data.reorderLevel)}</p>
            <p>Updated: {detailQuery.data.updatedAt ? formatDateTime(detailQuery.data.updatedAt) : "—"}</p>
            {detailQuery.data.description ? (
              <p className="md:col-span-2">{detailQuery.data.description}</p>
            ) : null}
          </CardBody>
        </Card>
      ) : null}
    </div>
  );
}
