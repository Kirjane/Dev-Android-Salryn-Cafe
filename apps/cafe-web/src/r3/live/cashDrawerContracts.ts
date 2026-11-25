import { hostRequest } from "../../api/hostClient";

export type CashDrawerEventType = "CashIn" | "CashOut" | "CashRefund";

export type CashDrawerEventRow = {
  eventType: string;
  signedAmountCents: number;
  sourceType: string;
  sourceId: string;
  actorUserId: string;
  occurredAt: string;
  note: string | null;
};

export type CashDrawerReviewRow = {
  decision: string;
  reason: string;
  reviewerUserId: string;
  createdAt: string;
};

export type CashDrawerSnapshot = {
  sessionId: string;
  state: "Open" | "Closed";
  openingCashCents: number;
  expectedCashCents: number | null;
  countedCashCents: number | null;
  varianceCents: number | null;
  coverageStatus: string;
  reviewStatus: string;
  events: CashDrawerEventRow[];
  reviews: CashDrawerReviewRow[];
};

export type CashDrawerHistoryPage = {
  items: CashDrawerSnapshot[];
  limit: number;
  offset: number;
};

export type DrawerMovementCommand = {
  eventType: CashDrawerEventType;
  amountCents: number;
  clientRequestId: string;
  reason: string;
};

export type DrawerReviewCommand = {
  decision: "Acknowledged";
  reason: string;
  clientRequestId: string;
};

const GUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const EMPTY_GUID = "00000000-0000-0000-0000-000000000000";

function requireGuid(value: string, label: string): void {
  if (!GUID_RE.test(value) || value === EMPTY_GUID) {
    throw new Error(`${label} must be a non-empty GUID.`);
  }
}

export async function readCurrentCashDrawer(): Promise<CashDrawerSnapshot> {
  return hostRequest<CashDrawerSnapshot>("/api/cash-drawer", undefined, "GET");
}

export async function recordCashDrawerMovement(
  command: DrawerMovementCommand,
): Promise<CashDrawerSnapshot> {
  if (
    command.eventType !== "CashIn" &&
    command.eventType !== "CashOut" &&
    command.eventType !== "CashRefund"
  ) {
    throw new Error("Movement eventType must be CashIn, CashOut, or CashRefund.");
  }
  if (!Number.isSafeInteger(command.amountCents) || command.amountCents <= 0) {
    throw new Error("Movement amountCents must be a positive integer.");
  }
  if (!command.reason.trim() || !command.clientRequestId.trim()) {
    throw new Error("Movement reason and clientRequestId are required.");
  }
  return hostRequest<CashDrawerSnapshot>("/api/cash-drawer/movements", command, "POST");
}

export async function readCashDrawerHistory(
  limit = 50,
  offset = 0,
): Promise<CashDrawerHistoryPage> {
  if (!Number.isSafeInteger(limit) || limit < 1 || limit > 100) {
    throw new Error("History limit must be between 1 and 100.");
  }
  if (!Number.isSafeInteger(offset) || offset < 0) {
    throw new Error("History offset must be nonnegative.");
  }
  return hostRequest<CashDrawerHistoryPage>(
    `/api/cash-drawer/history?limit=${limit}&offset=${offset}`,
    undefined,
    "GET",
  );
}

export async function reviewCashDrawerVariance(
  sessionId: string,
  command: DrawerReviewCommand,
): Promise<CashDrawerSnapshot> {
  requireGuid(sessionId, "sessionId");
  if (command.decision !== "Acknowledged") {
    throw new Error("Variance review decision must be 'Acknowledged'.");
  }
  if (!command.reason.trim() || !command.clientRequestId.trim()) {
    throw new Error("Review reason and clientRequestId are required.");
  }
  return hostRequest<CashDrawerSnapshot>(
    `/api/cash-drawer/${sessionId}/review`,
    command,
    "POST",
  );
}
