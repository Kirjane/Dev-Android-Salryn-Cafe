import { readJson } from "./http";
import type { PagedResponse } from "./types";

export type ProductListItem = {
  id: string;
  sku: string | null;
  barcode: string | null;
  name: string;
  unitName: string;
  categoryName: string | null;
  brandName: string | null;
  sellingPrice: number;
  costPrice: number | null;
  stockOnHand: number;
  reorderLevel: number;
  trackStock: boolean;
  isActive: boolean;
};

export type ProductDetail = {
  id: string;
  sku: string | null;
  barcode: string | null;
  name: string;
  description: string | null;
  categoryName: string | null;
  brandName: string | null;
  unitName: string;
  supplierName: string | null;
  costPrice: number | null;
  sellingPrice: number;
  wholesalePrice: number | null;
  stockOnHand: number;
  reorderLevel: number;
  trackStock: boolean;
  allowDiscount: boolean;
  isActive: boolean;
  notes: string | null;
  createdAt: string;
  updatedAt: string | null;
};

export type CatalogOption = {
  id: string;
  name: string;
  isActive: boolean;
};

export type ProductSearchParams = {
  search?: string;
  categoryId?: string;
  brandId?: string;
  isActive?: boolean;
  page?: number;
  pageSize?: number;
};

function toQuery(params: Record<string, string | number | boolean | undefined>): string {
  const query = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== "") {
      query.set(key, String(value));
    }
  }
  const text = query.toString();
  return text ? `?${text}` : "";
}

export async function fetchProducts(
  params: ProductSearchParams = {},
): Promise<PagedResponse<ProductListItem>> {
  const response = await fetch(
    `/api/products${toQuery({
      search: params.search,
      categoryId: params.categoryId,
      brandId: params.brandId,
      isActive: params.isActive,
      page: params.page ?? 1,
      pageSize: params.pageSize ?? 25,
    })}`,
    { credentials: "include" },
  );
  return readJson<PagedResponse<ProductListItem>>(response);
}

export async function fetchProductDetail(productId: string): Promise<ProductDetail> {
  const response = await fetch(`/api/products/${productId}`, { credentials: "include" });
  return readJson<ProductDetail>(response);
}

export async function fetchProductCategories(): Promise<CatalogOption[]> {
  const response = await fetch("/api/products/catalog/categories", { credentials: "include" });
  return readJson<CatalogOption[]>(response);
}

export async function fetchProductBrands(): Promise<CatalogOption[]> {
  const response = await fetch("/api/products/catalog/brands", { credentials: "include" });
  return readJson<CatalogOption[]>(response);
}

export async function fetchProductUnits(): Promise<CatalogOption[]> {
  const response = await fetch("/api/products/catalog/units", { credentials: "include" });
  return readJson<CatalogOption[]>(response);
}
