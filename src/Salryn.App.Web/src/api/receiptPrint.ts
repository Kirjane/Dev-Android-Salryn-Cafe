import { readJson } from "./http";

export type ReceiptPrintResult = {
  succeeded: boolean;
  status: "NotAttempted" | "Succeeded" | "Failed" | "Disabled/Unavailable" | string;
  saleId: string | null;
  receiptNumber: string | null;
  source: string;
  adapterName: string;
  message: string;
  errorMessage: string | null;
  printedAtUtc: string | null;
};

export type ReceiptPrintEnvelope = {
  code: string;
  message: string;
  print: ReceiptPrintResult;
};

export async function printReceiptForSale(saleId: string): Promise<ReceiptPrintEnvelope> {
  const response = await fetch(`/api/receipts/${saleId}/print`, {
    method: "POST",
    credentials: "include",
  });
  return readJson<ReceiptPrintEnvelope>(response);
}

export async function testPrintReceipt(): Promise<ReceiptPrintEnvelope> {
  const response = await fetch("/api/receipts/test-print", {
    method: "POST",
    credentials: "include",
  });
  return readJson<ReceiptPrintEnvelope>(response);
}
