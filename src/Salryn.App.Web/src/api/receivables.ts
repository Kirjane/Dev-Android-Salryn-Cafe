import { readJson } from "./http";

export type ReceivableCustomer = {
  customer: {
    id: string;
    fullName: string;
    phone: string | null;
    note: string | null;
    isActive: boolean;
  };
  balanceCents: number;
  oldestDueDate: string | null;
  lastTransactionAt: string | null;
};

export const ReceivableEntryType = {
  Collection: 2,
  CollectionReversal: 6,
} as const;

export type ReceivableEntry = {
  id: string;
  collectionId: string | null;
  entryType: number;
  debitCents: number;
  creditCents: number;
  balanceAfterCents: number;
  dueDate: string | null;
  note: string | null;
  createdAt: string;
};

export type ReceivablesSummary = {
  totalReceivablesCents: number;
  dueTodayCents: number;
  overdueCents: number;
  collectedTodayCents: number;
};

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, { credentials: "include", ...init });
  return readJson<T>(response);
}

export const fetchReceivableCustomers = (search = "") =>
  request<ReceivableCustomer[]>(`/api/receivables/customers?search=${encodeURIComponent(search)}`);
export const fetchReceivablesSummary = () => request<ReceivablesSummary>("/api/receivables/summary");
export const fetchCustomerLedger = (id: string) =>
  request<ReceivableEntry[]>(`/api/receivables/customers/${id}/ledger`);
export const createCustomer = (body: { fullName: string; phone?: string; note?: string }) =>
  request<ReceivableCustomer["customer"]>("/api/customers", {
    method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body),
  });
export const collectReceivable = (body: { customerId: string; amountCents: number; method: string; note?: string; clientRequestId: string }) =>
  request("/api/receivables/collections", {
    method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body),
  });
export const voidReceivableCollection = (collectionId: string, reason: string) =>
  request<{ status: string }>(`/api/receivables/collections/${collectionId}/void`, {
    method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ reason }),
  });
