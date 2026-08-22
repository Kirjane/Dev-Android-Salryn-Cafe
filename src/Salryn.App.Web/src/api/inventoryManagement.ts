import { readJson } from "./http";
import type { ProductListItem } from "./products";

export type StockInRequest = {
  productId: string;
  quantity: number;
  reason: string;
  reference: string | null;
};

export type StockAdjustmentRequest = {
  productId: string;
  direction: "add" | "remove";
  quantity: number;
  reason: string;
  reference: string | null;
};

export type StockWriteResponse = {
  code: string;
  message: string;
  product: ProductListItem;
  movementType: string;
  quantityChange: number;
  referenceType: string;
  reference: string | null;
};

export async function submitStockIn(request: StockInRequest): Promise<StockWriteResponse> {
  const response = await fetch("/api/inventory/stock-in", {
    method: "POST",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });
  return readJson<StockWriteResponse>(response);
}

export async function submitStockAdjustment(request: StockAdjustmentRequest): Promise<StockWriteResponse> {
  const response = await fetch("/api/inventory/adjustment", {
    method: "POST",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });
  return readJson<StockWriteResponse>(response);
}
