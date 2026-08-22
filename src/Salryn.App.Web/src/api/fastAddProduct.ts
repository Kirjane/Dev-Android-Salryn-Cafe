import { readJson } from "./http";
import type { ProductListItem } from "./products";

export type FastAddProductRequest = {
  productName: string;
  barcode: string;
  unitId: string;
  sellingPrice: number;
  initialStockQuantity: number;
};

export type FastAddProductResponse = {
  code: string;
  message: string;
  product: ProductListItem;
  initialStockMovementCreated: boolean;
};

export async function fastAddUnknownBarcode(request: FastAddProductRequest): Promise<FastAddProductResponse> {
  const response = await fetch("/api/products/fast-add", {
    method: "POST",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });
  return readJson<FastAddProductResponse>(response);
}
