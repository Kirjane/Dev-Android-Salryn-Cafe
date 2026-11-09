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

function storeDateStartUtcBoundary(value: string | undefined): string | undefined {
  return value && /^\d{4}-\d{2}-\d{2}$/.test(value)
    ? `${value}T00:00:00+08:00`
    : value;
}

function storeDateEndUtcBoundary(value: string | undefined): string | undefined {
  return value && /^\d{4}-\d{2}-\d{2}$/.test(value)
    ? `${value}T23:59:59.9999999+08:00`
    : value;
}

export async function fetchSalesHistory(
  params: SaleSearchParams = {},
): Promise<PagedResponse<SaleHistoryItem>> {
  const response = await fetch(
    `/api/sales/history${toQuery({
      // Sales & Receipts date controls are store dates, matching Reports. The
      // repository accepts instants, so send explicit Asia/Manila boundaries.
      from: storeDateStartUtcBoundary(params.from),
      to: storeDateEndUtcBoundary(params.to),
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
