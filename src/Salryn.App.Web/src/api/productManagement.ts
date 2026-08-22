import { readJson } from "./http";
import type { ProductListItem } from "./products";

export type ProductManagementWriteRequest = {
  productName: string;
  barcode: string;
  unitId: string;
  categoryId: string | null;
  brandId: string | null;
  sellingPrice: number;
  costPrice: number | null;
  reorderLevel: number;
};

export type ProductManagementWriteResponse = {
  code: string;
  message: string;
  product: ProductListItem;
};

export type ProductDeactivateResponse = {
  code: string;
  message: string;
  productId: string;
};

export async function createManagedProduct(request: ProductManagementWriteRequest): Promise<ProductManagementWriteResponse> {
  const response = await fetch("/api/products/manage", {
    method: "POST",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });
  return readJson<ProductManagementWriteResponse>(response);
}

export async function updateManagedProduct(productId: string, request: ProductManagementWriteRequest): Promise<ProductManagementWriteResponse> {
  const response = await fetch(`/api/products/${productId}/manage`, {
    method: "PUT",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });
  return readJson<ProductManagementWriteResponse>(response);
}

export async function deactivateManagedProduct(productId: string, reason: string): Promise<ProductDeactivateResponse> {
  const response = await fetch(`/api/products/${productId}/deactivate`, {
    method: "POST",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ reason }),
  });
  return readJson<ProductDeactivateResponse>(response);
}
