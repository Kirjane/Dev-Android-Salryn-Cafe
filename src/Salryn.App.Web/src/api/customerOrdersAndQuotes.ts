import { readJson } from "./http";

export type CustomerQuotationLine = {
  id: string;
  quotationId: string;
  productId: string;
  productNameSnapshot: string;
  barcodeSnapshot: string | null;
  unitNameSnapshot: string;
  quantityX1000: number;
  unitPriceCents: number;
  discountCents: number;
  lineTotalCents: number;
};

export type CustomerQuotation = {
  id: string;
  quoteNumber: string;
  customerName: string;
  customerContact: string | null;
  notes: string | null;
  subtotalCents: number;
  discountCents: number;
  totalCents: number;
  status: string;
  createdByUserId: string;
  createdByUserName: string;
  createdAt: string;
  validUntil: string | null;
  lines: CustomerQuotationLine[];
};

export type CreateCustomerQuotationLineRequest = {
  productId: string;
  productNameSnapshot: string;
  barcodeSnapshot?: string | null;
  unitNameSnapshot: string;
  quantityX1000: number;
  unitPriceCents: number;
  discountCents: number;
};

export type CreateCustomerQuotationRequest = {
  customerName: string;
  customerContact?: string;
  notes?: string;
  discountCents: number;
  validUntil?: string;
  lines: CreateCustomerQuotationLineRequest[];
};

export type CustomerOrderLine = {
  id: string;
  orderId: string;
  productId: string;
  productNameSnapshot: string;
  barcodeSnapshot: string | null;
  unitNameSnapshot: string;
  quantityX1000: number;
  unitPriceCents: number;
  lineTotalCents: number;
};

export type CustomerOrder = {
  id: string;
  orderNumber: string;
  customerId: string | null;
  customerName: string;
  customerPhone: string | null;
  promisedDate: string | null;
  notes: string | null;
  totalCents: number;
  status: string;
  saleId: string | null;
  createdByUserId: string;
  createdByUserName: string;
  createdAt: string;
  fulfilledAt: string | null;
  lines: CustomerOrderLine[];
};

export type CreateCustomerOrderLineRequest = {
  productId: string;
  productNameSnapshot: string;
  barcodeSnapshot?: string | null;
  unitNameSnapshot: string;
  quantityX1000: number;
  unitPriceCents: number;
};

export type CreateCustomerOrderRequest = {
  customerId?: string;
  customerName: string;
  customerPhone?: string;
  promisedDate?: string;
  notes?: string;
  lines: CreateCustomerOrderLineRequest[];
};

export async function fetchCustomerQuotations(): Promise<CustomerQuotation[]> {
  const response = await fetch("/api/customer-quotations");
  return readJson<CustomerQuotation[]>(response);
}

export async function fetchCustomerQuotation(id: string): Promise<CustomerQuotation> {
  const response = await fetch(`/api/customer-quotations/${encodeURIComponent(id)}`);
  return readJson<CustomerQuotation>(response);
}

export async function createCustomerQuotation(
  request: CreateCustomerQuotationRequest
): Promise<CustomerQuotation> {
  const response = await fetch("/api/customer-quotations", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });
  return readJson<CustomerQuotation>(response);
}

export async function updateCustomerQuotationStatus(
  id: string,
  status: string
): Promise<CustomerQuotation> {
  const response = await fetch(`/api/customer-quotations/${encodeURIComponent(id)}/status`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ status }),
  });
  return readJson<CustomerQuotation>(response);
}

export async function fetchCustomerOrders(status?: string): Promise<CustomerOrder[]> {
  const query = status ? `?status=${encodeURIComponent(status)}` : "";
  const response = await fetch(`/api/customer-orders${query}`);
  return readJson<CustomerOrder[]>(response);
}

export async function fetchCustomerOrder(id: string): Promise<CustomerOrder> {
  const response = await fetch(`/api/customer-orders/${encodeURIComponent(id)}`);
  return readJson<CustomerOrder>(response);
}

export async function createCustomerOrder(
  request: CreateCustomerOrderRequest
): Promise<CustomerOrder> {
  const response = await fetch("/api/customer-orders", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });
  return readJson<CustomerOrder>(response);
}

export async function updateCustomerOrderStatus(
  id: string,
  status: string
): Promise<CustomerOrder> {
  const response = await fetch(`/api/customer-orders/${encodeURIComponent(id)}/status`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ status }),
  });
  return readJson<CustomerOrder>(response);
}

export async function fulfillCustomerOrder(
  id: string,
  saleId: string
): Promise<CustomerOrder> {
  const response = await fetch(`/api/customer-orders/${encodeURIComponent(id)}/fulfill`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ saleId }),
  });
  return readJson<CustomerOrder>(response);
}
