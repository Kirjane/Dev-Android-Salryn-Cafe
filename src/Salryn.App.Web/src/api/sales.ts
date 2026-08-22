import { readJson } from "./http";
import type { PagedResponse } from "./types";
export { printReceiptForSale, testPrintReceipt } from "./receiptPrint";
export type { ReceiptPrintEnvelope, ReceiptPrintResult } from "./receiptPrint";

export type SaleHistoryItem = {
  id: string;
  receiptNo: string;
  saleDate: string;
  cashierDisplayName: string;
  total: number;
  amountPaid: number;
  change: number;
  status: string;
  voidReason: string | null;
};

export type SaleDetail = {
  id: string;
  receiptNo: string;
  saleDate: string;
  cashierDisplayName: string;
  subtotal: number;
  discount: number;
  tax: number;
  total: number;
  amountPaid: number;
  change: number;
  status: string;
  voidReason: string | null;
  voidedAt: string | null;
  receiptSnapshot: string | null;
  items: {
    id: string;
    productNameSnapshot: string;
    barcodeSnapshot: string | null;
    unitNameSnapshot: string;
    quantity: number;
    unitPrice: number;
    discount: number;
    lineTotal: number;
  }[];
  payments: {
    id: string;
    method: string;
    amount: number;
    paidAt: string;
  }[];
};

export type SaleSearchParams = {
  from?: string;
  to?: string;
  status?: string;
  receiptNo?: string;
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

export async function fetchSalesHistory(
  params: SaleSearchParams = {},
): Promise<PagedResponse<SaleHistoryItem>> {
  const response = await fetch(
    `/api/sales/history${toQuery({
      from: params.from,
      to: params.to,
      status: params.status,
      receiptNo: params.receiptNo,
      page: params.page ?? 1,
      pageSize: params.pageSize ?? 25,
    })}`,
    { credentials: "include" },
  );
  return readJson<PagedResponse<SaleHistoryItem>>(response);
}

export async function fetchSaleDetail(saleId: string): Promise<SaleDetail> {
  const response = await fetch(`/api/sales/${saleId}`, { credentials: "include" });
  return readJson<SaleDetail>(response);
}
