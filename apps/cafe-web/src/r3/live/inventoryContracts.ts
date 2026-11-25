import { hostRequest } from "../../api/hostClient";

// Prepared command objects must be retained verbatim by the caller before dispatch.
// This module neither issues mutations automatically nor retries with new identities.
export type StockTarget = {
  stockTargetType: "Ingredient" | "Product";
  stockTargetId: string;
};

export type AdjustmentCommand = StockTarget & {
  operationId: string;
  adjustmentEventId: string;
  quantityDeltaX1000: number;
  reason: string;
  occurredAtUtc: string;
  referenceType?: string | null;
  referenceId?: string | null;
  correctionOfReferenceId?: string | null;
};

export type InventoryAdjustmentEvidence = StockTarget & {
  adjustmentEventId: string;
  operationId: string;
  storeId: string;
  nameSnapshot: string;
  stockUnitSnapshot: string;
  quantityDeltaX1000: number;
  reason: string;
  referenceType: string | null;
  referenceId: string | null;
  correctionOfReferenceId: string | null;
  actorUserId: string;
  occurredAtUtc: string;
  contentHash: string;
};

export type CountLine = StockTarget & {
  lineId: string;
  expectedBeforeX1000: number;
  countedX1000: number;
};

export type CountDraft = {
  countSessionId: string;
  startedAtUtc: string;
  notes: string | null;
  lines: CountLine[];
};

export type CountFinalize = {
  operationId: string;
  finalizedAtUtc: string;
};

export type InventoryCountLine = CountLine & {
  nameSnapshot: string;
  stockUnitSnapshot: string;
  quantityDeltaX1000: number;
};

export type CountSession = {
  countSessionId: string;
  storeId: string;
  state: "Draft" | "Finalized";
  startedByUserId: string;
  startedAtUtc: string;
  finalizedByUserId: string | null;
  finalizedAtUtc: string | null;
  notes: string | null;
  finalizeOperationId: string | null;
  finalizedContentHash: string | null;
  lines: InventoryCountLine[];
};

export type InventoryCommitResult<T> = {
  evidence: T;
  isReplay: boolean;
};

const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function id(value: string) {
  if (!guid.test(value) || value === "00000000-0000-0000-0000-000000000000") {
    throw new Error("Nonempty GUID required.");
  }
}

function target(x: StockTarget) {
  id(x.stockTargetId);
  if (x.stockTargetType !== "Ingredient" && x.stockTargetType !== "Product") {
    throw new Error("Invalid stock target type.");
  }
}

function instant(value: string) {
  if (!Number.isFinite(Date.parse(value)) || !/(Z|[+-]\d\d:\d\d)$/.test(value)) {
    throw new Error("Explicit timestamp required.");
  }
}

function quantity(n: number) {
  if (!Number.isSafeInteger(n) || n < 0) {
    throw new Error("Nonnegative integer stock quantity required.");
  }
}

export async function commitAdjustment(command: AdjustmentCommand) {
  target(command);
  id(command.operationId);
  id(command.adjustmentEventId);
  instant(command.occurredAtUtc);
  if (
    !Number.isSafeInteger(command.quantityDeltaX1000) ||
    command.quantityDeltaX1000 === 0 ||
    !command.reason.trim()
  ) {
    throw new Error("Nonzero integer adjustment and reason required.");
  }
  return hostRequest<InventoryCommitResult<InventoryAdjustmentEvidence>>(
    "/api/cafe/inventory/adjust",
    command,
    "POST",
    command.operationId,
  );
}

export async function saveCountDraft(operationId: string, draft: CountDraft) {
  id(operationId);
  id(draft.countSessionId);
  instant(draft.startedAtUtc);
  if (!draft.lines.length) throw new Error("At least one count line required.");
  const targets = new Set<string>();
  const lineIds = new Set<string>();
  for (const line of draft.lines) {
    target(line);
    id(line.lineId);
    quantity(line.expectedBeforeX1000);
    quantity(line.countedX1000);
    const key = `${line.stockTargetType}:${line.stockTargetId.toLowerCase()}`;
    const lineKey = line.lineId.toLowerCase();
    if (targets.has(key) || lineIds.has(lineKey)) {
      throw new Error("Duplicate count target or line.");
    }
    targets.add(key);
    lineIds.add(lineKey);
  }
  return hostRequest<CountSession>(
    "/api/cafe/inventory/counts/draft",
    draft,
    "POST",
    operationId,
  );
}

export async function finalizeCount(countSessionId: string, command: CountFinalize) {
  id(countSessionId);
  id(command.operationId);
  instant(command.finalizedAtUtc);
  return hostRequest<InventoryCommitResult<CountSession>>(
    `/api/cafe/inventory/counts/${countSessionId}/finalize`,
    command,
    "POST",
    command.operationId,
  );
}

export async function readCount(countSessionId: string) {
  id(countSessionId);
  return hostRequest<CountSession>(
    `/api/cafe/inventory/counts/${countSessionId}`,
    undefined,
    "GET",
  );
}

export type LiveIngredient = {
  id: string;
  name: string;
  stockUnit: string;
  stockOnHandX1000: number;
  reorderLevelX1000: number;
  isActive: boolean;
  notes: string | null;
};

export type LiveSupplier = {
  id: string;
  name: string;
  contactNumber: string | null;
  notes: string | null;
  isActive: boolean;
};

export type LiveLowStockItem = {
  stockTargetType: "Ingredient" | "Product";
  stockTargetId: string;
  name: string;
  stockUnit: string;
  stockOnHandX1000: number;
  reorderLevelX1000: number;
};

export type PurchaseReceiveLineCommand = StockTarget & {
  lineId: string;
  quantityReceivedX1000: number;
  lineAcquisitionCostCents: number;
};

export type PurchaseReceiveCommand = {
  operationId: string;
  purchaseReceiptId: string;
  supplierId: string;
  receivedAtUtc: string;
  externalReference: string | null;
  notes: string | null;
  lines: PurchaseReceiveLineCommand[];
};

export type WasteReason = "Waste" | "Spoilage" | "Damage" | "Expired";

export type WasteLineCommand = StockTarget & {
  lineId: string;
  quantityWastedX1000: number;
};

export type WasteCommitCommand = {
  operationId: string;
  wasteEventId: string;
  occurredAtUtc: string;
  reason: WasteReason;
  notes: string | null;
  lines: WasteLineCommand[];
};

export async function readLiveIngredients(): Promise<LiveIngredient[]> {
  const res = await hostRequest<{ ingredients: LiveIngredient[] }>(
    "/api/cafe/ingredients-recipes",
    undefined,
    "GET",
  );
  return Array.isArray(res?.ingredients) ? res.ingredients : [];
}

export async function readLiveSuppliers(): Promise<LiveSupplier[]> {
  const res = await hostRequest<LiveSupplier[]>("/api/cafe/suppliers", undefined, "GET");
  return Array.isArray(res) ? res : [];
}

export async function readLiveLowStock(): Promise<LiveLowStockItem[]> {
  const res = await hostRequest<LiveLowStockItem[]>(
    "/api/cafe/inventory/low-stock",
    undefined,
    "GET",
  );
  return Array.isArray(res) ? res : [];
}

export async function commitPurchase(command: PurchaseReceiveCommand) {
  id(command.operationId);
  id(command.purchaseReceiptId);
  id(command.supplierId);
  instant(command.receivedAtUtc);
  if (!command.lines.length) throw new Error("At least one purchase line required.");
  for (const line of command.lines) {
    target(line);
    id(line.lineId);
    if (!Number.isSafeInteger(line.quantityReceivedX1000) || line.quantityReceivedX1000 <= 0) {
      throw new Error("Positive integer purchase quantity required.");
    }
    if (!Number.isSafeInteger(line.lineAcquisitionCostCents) || line.lineAcquisitionCostCents < 0) {
      throw new Error("Nonnegative integer line acquisition cost required.");
    }
  }
  return hostRequest<InventoryCommitResult<unknown>>(
    "/api/cafe/purchases/receive",
    command,
    "POST",
    command.operationId,
  );
}

export async function commitWaste(command: WasteCommitCommand) {
  id(command.operationId);
  id(command.wasteEventId);
  instant(command.occurredAtUtc);
  if (!command.lines.length) throw new Error("At least one waste line required.");
  for (const line of command.lines) {
    target(line);
    id(line.lineId);
    if (!Number.isSafeInteger(line.quantityWastedX1000) || line.quantityWastedX1000 <= 0) {
      throw new Error("Positive integer waste quantity required.");
    }
  }
  return hostRequest<InventoryCommitResult<unknown>>(
    "/api/cafe/inventory/waste",
    command,
    "POST",
    command.operationId,
  );
}
