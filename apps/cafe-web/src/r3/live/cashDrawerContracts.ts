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

export type OpenShiftCommand = {
  openingCashCents: number;
  clientRequestId: string;
};

export type CloseShiftBlindCommand = {
  countedCashCents: number;
  physicalCountConfirmed: true;
  clientRequestId: string;
  note: string | null;
};

export type ShiftOperationEnvelope = {
  ok: boolean;
  code?: string;
  message?: string;
  shift?: {
    shiftId?: string;
    shiftState?: string;
    openingCashCents?: number;
    expectedCashCents?: number | null;
    countedCashCents?: number | null;
    varianceCents?: number | null;
  } | null;
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

export async function openCashShift(
  command: OpenShiftCommand,
): Promise<ShiftOperationEnvelope> {
  if (!Number.isSafeInteger(command.openingCashCents) || command.openingCashCents < 0) {
    throw new Error("Opening cash float must be a nonnegative integer.");
  }
  if (!command.clientRequestId.trim()) {
    throw new Error("clientRequestId is required to open a shift.");
  }
  return hostRequest<ShiftOperationEnvelope>("/api/shift/open", command, "POST");
}

export async function closeCashShiftBlind(
  command: CloseShiftBlindCommand,
): Promise<ShiftOperationEnvelope> {
  if (!Number.isSafeInteger(command.countedCashCents) || command.countedCashCents < 0) {
    throw new Error("Counted cash must be a nonnegative integer.");
  }
  if (command.physicalCountConfirmed !== true) {
    throw new Error("Explicit physical cash count confirmation is required for blind shift close.");
  }
  if (!command.clientRequestId.trim()) {
    throw new Error("clientRequestId is required to close a shift.");
  }
  return hostRequest<ShiftOperationEnvelope>("/api/shift/close", command, "POST");
}

// --- C6 Canonical Cash / Expense Operations (/api/cafe/cash/movements) ---

export type CafeCashMovementType = "CashIn" | "CashOut" | "OperationalExpense" | "InventoryPurchase";

export type CafeCashMovementLedgerRow = {
  movementId: string;
  movementType: string;
  deltaCents: number;
  reason: string | null;
  referenceType: string;
  referenceId: string;
  reversalOfMovementId: string | null;
  createdAtUtc: string;
  actorUserId: string;
  isReversed: boolean;
};

const CASH_MOVEMENT_TYPE_LABELS: Record<number, string> = {
  1: "SaleCashReceipt",
  2: "ReceivableCashCollection",
  3: "CashIn",
  4: "InventoryPurchase",
  5: "OperationalExpense",
  6: "CashRefund",
  7: "CashOut",
  8: "Reversal",
};

function normalizeCashMovementType(raw: string | number | undefined): string {
  if (typeof raw === "number") {
    return CASH_MOVEMENT_TYPE_LABELS[raw] ?? `Unknown(${raw})`;
  }
  if (typeof raw === "string") {
    const num = Number(raw);
    if (Number.isInteger(num) && CASH_MOVEMENT_TYPE_LABELS[num]) {
      return CASH_MOVEMENT_TYPE_LABELS[num];
    }
    return raw;
  }
  return "Unknown";
}

type RawCafeCashMovementRow = {
  movementId?: string;
  movementType?: string | number;
  deltaCents?: number;
  signedAmountCents?: number;
  reason?: string | null;
  referenceType?: string;
  referenceId?: string;
  reversalOfMovementId?: string | null;
  createdAtUtc?: string;
  occurredAtUtc?: string;
  actorUserId?: string;
  isReversed?: boolean;
};

export type PostCafeCashMovementCommand = {
  operationId: string;
  movementId: string;
  movementType: CafeCashMovementType;
  amountCents: number;
  reason: string;
  referenceId?: string | null;
  occurredAtUtc: string;
};

export type ReverseCafeCashMovementCommand = {
  operationId: string;
  movementId: string;
  reason: string;
  occurredAtUtc: string;
};

export async function readCafeCashMovements(): Promise<CafeCashMovementLedgerRow[]> {
  const res = await hostRequest<RawCafeCashMovementRow[] | { movements?: RawCafeCashMovementRow[] }>(
    "/api/cafe/cash/movements",
    undefined,
    "GET",
  );
  const rawRows = Array.isArray(res) ? res : res && Array.isArray(res.movements) ? res.movements : [];
  const reversedIds = new Set(
    rawRows
      .map((r) => r.reversalOfMovementId)
      .filter((id): id is string => typeof id === "string" && id.length > 0),
  );
  return rawRows.map((r) => {
    const id = r.movementId ?? "";
    const delta =
      typeof r.deltaCents === "number"
        ? r.deltaCents
        : typeof r.signedAmountCents === "number"
          ? r.signedAmountCents
          : 0;
    return {
      movementId: id,
      movementType: normalizeCashMovementType(r.movementType),
      deltaCents: delta,
      reason: r.reason ?? null,
      referenceType: r.referenceType ?? "",
      referenceId: r.referenceId ?? "",
      reversalOfMovementId: r.reversalOfMovementId ?? null,
      createdAtUtc: r.createdAtUtc ?? r.occurredAtUtc ?? "",
      actorUserId: r.actorUserId ?? "",
      isReversed: Boolean(r.isReversed) || (id.length > 0 && reversedIds.has(id)),
    };
  });
}

export async function postCafeCashMovement(command: PostCafeCashMovementCommand): Promise<unknown> {
  requireGuid(command.operationId, "operationId");
  requireGuid(command.movementId, "movementId");
  if (!Number.isSafeInteger(command.amountCents) || command.amountCents <= 0) {
    throw new Error("Amount must be a positive integer.");
  }
  if (!command.reason.trim()) {
    throw new Error("Reason is required.");
  }
  return hostRequest<unknown>("/api/cafe/cash/movements", command, "POST", command.operationId);
}

export async function reverseCafeCashMovement(
  originalMovementId: string,
  command: ReverseCafeCashMovementCommand,
): Promise<unknown> {
  requireGuid(originalMovementId, "originalMovementId");
  requireGuid(command.operationId, "operationId");
  requireGuid(command.movementId, "movementId");
  if (!command.reason.trim()) {
    throw new Error("Reversal reason is required.");
  }
  return hostRequest<unknown>(
    `/api/cafe/cash/movements/${originalMovementId}/reverse`,
    command,
    "POST",
    command.operationId,
  );
}
