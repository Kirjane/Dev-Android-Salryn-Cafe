import { readJson } from "./http";

export type HostShift = {
  shiftState: string;
  shiftId: string | null;
  openedAtUtc: string | null;
  openingCashCents: number | null;
  message: string;
  expectedCashCents?: number | null;
  countedCashCents?: number | null;
  varianceCents?: number | null;
  coverageStatus?: string | null;
  reviewStatus?: string | null;
};

export type HostShiftEnvelope = {
  code: string;
  message: string;
  shift: HostShift;
};

export type OpenShiftRequest = {
  openingCashCents: number;
};

export type CheckoutLineRequest = {
  productId: string;
  quantityX1000: number;
};

export type CheckoutPaymentRequest = {
  method: "Cash" | "AccountsReceivable";
  amountCents: number;
};

export type CheckoutRequest = {
  clientRequestId: string;
  lines: CheckoutLineRequest[];
  payments: CheckoutPaymentRequest[];
  customerId?: string;
  customerFullName?: string;
  customerPhone?: string;
  dueDate?: string;
  note?: string;
};

export type CheckoutResult = {
  status: string;
  receiptNumber: string | null;
  saleId: string | null;
  totalCents: number | null;
  changeCents: number | null;
  message: string;
  clientRequestId?: string | null;
  duplicate?: boolean;
};

export type CheckoutEnvelope = {
  code: string;
  message: string;
  checkout: CheckoutResult;
};

export async function fetchHostShift(): Promise<HostShiftEnvelope> {
  const response = await fetch("/api/shift", {
    credentials: "include",
  });

  return readJson<HostShiftEnvelope>(response);
}

export async function openHostShift(request: OpenShiftRequest): Promise<HostShiftEnvelope> {
  const response = await fetch("/api/shift/open", {
    method: "POST",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(request),
  });

  return readJson<HostShiftEnvelope>(response);
}

export type CloseShiftRequest = {
  countedCashCents: number;
  physicalCountConfirmed: boolean;
  clientRequestId: string;
  note?: string;
};

export async function closeHostShift(request?: CloseShiftRequest): Promise<HostShiftEnvelope> {
  const response = await fetch("/api/shift/close", {
    method: "POST",
    credentials: "include",
    headers: request ? { "Content-Type": "application/json" } : undefined,
    body: request ? JSON.stringify(request) : undefined,
  });

  return readJson<HostShiftEnvelope>(response);
}

export async function submitCheckout(request: CheckoutRequest): Promise<CheckoutEnvelope> {
  const response = await fetch("/api/checkout", {
    method: "POST",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(request),
  });

  return readJson<CheckoutEnvelope>(response);
}
