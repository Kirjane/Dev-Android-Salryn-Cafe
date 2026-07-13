import { readJson } from "./http";
import type { SaleDetail } from "./sales";

export type SaleVoidResponse = {
  code: string;
  message: string;
  sale: SaleDetail;
  reversalMovementCount: number;
};

export async function voidSale(saleId: string, reason: string): Promise<SaleVoidResponse> {
  const response = await fetch(`/api/sales/${saleId}/void`, {
    method: "POST",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ reason }),
  });
  return readJson<SaleVoidResponse>(response);
}
