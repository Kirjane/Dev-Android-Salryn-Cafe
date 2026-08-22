import { readJson } from "./http";

export type LifecycleRefundLine = {
  saleItemId: string;
  quantityX1000: number;
  disposition: "Restock" | "DamagedNonRestockable" | "ServiceNoStock";
  operatorNote?: string;
  expectedRefundAmountCents?: number;
};

export type LifecycleActionRequest = {
  action: "Reprint" | "Void" | "Refund" | "Adjust" | "Cancel";
  journalEntryId?: string;
  officialDocumentNumber?: string;
  saleId?: string;
  reason: string;
  terminalId?: string;
  idempotencyKey: string;
  expectedTotalRefundCents?: number;
  refundLines?: LifecycleRefundLine[];
  settlement?: {
    method: "Cash" | "CardExternal";
    expectedAmountCents?: number;
    externalReference?: string;
    immediateComplete?: boolean;
  };
};

export type LifecycleActionResponse = {
  action: string;
  operationId: string;
  eventId: string;
  journalEntryId: string;
  officialDocumentNumber: string;
  summary: string;
  lifecycleState: string;
  sequenceAdvanced: boolean;
  originalImmutable: boolean;
  adjustmentDocumentNumber?: string;
  reprintCanonicalSha256?: string;
  outputHtml?: string;
  code?: string;
  message?: string;
};

export async function postLifecycleAction(
  body: LifecycleActionRequest,
): Promise<LifecycleActionResponse> {
  const response = await fetch("/api/official/document-lifecycle/action", {
    method: "POST",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  return readJson<LifecycleActionResponse>(response);
}

export async function fetchLifecycleStatus(): Promise<Record<string, unknown>> {
  const response = await fetch("/api/official/document-lifecycle/status", { credentials: "include" });
  return readJson<Record<string, unknown>>(response);
}
