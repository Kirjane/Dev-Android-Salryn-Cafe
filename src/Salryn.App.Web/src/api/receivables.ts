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
  CreditSale: 1,
  Collection: 2,
  AdjustmentDebit: 3,
  AdjustmentCredit: 4,
  SaleReversal: 5,
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
  paymentStatus: number | null;
};

export type CollectionApiResponse = {
  collection: {
    id: string;
    customerId: string;
    amountCents: number;
    note: string | null;
    collectedAt: string;
  };
  acknowledgment: {
    collectionId: string;
    customerName: string;
    amountPaidCents: number;
    previousBalanceCents: number;
    newBalanceCents: number;
    collectedBy: string;
    collectedAt: string;
    note: string | null;
    method: string;
  };
  plainTextAcknowledgment: string;
};

export type ReceivablesSummary = {
  totalReceivablesCents: number;
  dueTodayCents: number;
  overdueCents: number;
  collectedTodayCents: number;
};
export type ReceivablePage<T> = { items: T[]; pageNumber: number; pageSize: number; totalCount: number; totalPages: number; hasMore: boolean; asOfUtc: string; timeZoneId: string };

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, { credentials: "include", ...init });
  return readJson<T>(response);
}

export const fetchReceivableCustomers = (search = "") =>
  request<ReceivableCustomer[]>(`/api/receivables/customers?search=${encodeURIComponent(search)}`);
export const fetchReceivableCustomersPage = (search = "", page = 1, pageSize = 50) =>
  request<ReceivablePage<ReceivableCustomer>>(`/api/receivables/customers/page?search=${encodeURIComponent(search)}&page=${page}&pageSize=${pageSize}`);
export const fetchReceivablesSummary = () => request<ReceivablesSummary>("/api/receivables/summary");
export const fetchCustomerLedger = (id: string) =>
  request<ReceivableEntry[]>(`/api/receivables/customers/${id}/ledger`);
export const fetchCustomerLedgerPage = (id: string, page = 1, pageSize = 100) =>
  request<ReceivablePage<ReceivableEntry>>(`/api/receivables/customers/${id}/ledger/page?page=${page}&pageSize=${pageSize}`);
export const createCustomer = (body: { fullName: string; phone?: string; note?: string }) =>
  request<ReceivableCustomer["customer"]>("/api/customers", {
    method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body),
  });
export const collectReceivable = (body: { customerId: string; amountCents: number; method: string; note?: string; clientRequestId: string }) =>
  request<CollectionApiResponse>("/api/receivables/collections", {
    method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body),
  });
export const voidReceivableCollection = (collectionId: string, reason: string) =>
  request<{ status: string }>(`/api/receivables/collections/${collectionId}/void`, {
    method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ reason }),
  });
