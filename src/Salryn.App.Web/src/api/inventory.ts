import { readJson } from "./http";
import type { ProductListItem } from "./products";
import type { PagedResponse } from "./types";

export type LowStockProduct = {
  productId: string;
  barcode: string | null;
  productName: string;
  unitName: string;
  stockOnHand: number;
  reorderLevel: number;
};

export type StockMovement = {
  id: string;
  productId: string;
  productName: string;
  movementType: string;
  quantityChange: number;
  referenceType: string | null;
  referenceId: string | null;
  reason: string | null;
  userDisplayName: string;
  createdAt: string;
};

export type MovementSearchParams = {
  productId?: string;
  movementType?: string;
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
};

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

function toQuery(params: Record<string, string | number | undefined>): string {
  const query = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== "") {
      query.set(key, String(value));
    }
  }
  const text = query.toString();
  return text ? `?${text}` : "";
}

export async function fetchLowStockProducts(): Promise<LowStockProduct[]> {
  const response = await fetch("/api/inventory/low-stock", { credentials: "include" });
  return readJson<LowStockProduct[]>(response);
}

export async function fetchStockMovements(
  params: MovementSearchParams = {},
): Promise<PagedResponse<StockMovement>> {
  const response = await fetch(
    `/api/inventory/movements${toQuery({
      productId: params.productId,
      movementType: params.movementType,
      from: params.from,
      to: params.to,
      page: params.page ?? 1,
      pageSize: params.pageSize ?? 25,
    })}`,
    { credentials: "include" },
  );
  return readJson<PagedResponse<StockMovement>>(response);
}

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
