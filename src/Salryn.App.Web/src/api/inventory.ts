import { readJson } from "./http";
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
