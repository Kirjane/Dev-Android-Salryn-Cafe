import { hostRequest } from "../../api/hostClient";

// Prepared command objects must be retained verbatim by the caller before dispatch.
// This module neither issues mutations automatically nor retries with new identities.
export type StockTarget = { stockTargetType: "Ingredient" | "Product"; stockTargetId: string };
export type AdjustmentCommand = StockTarget & {
  operationId: string; adjustmentEventId: string; quantityDeltaX1000: number; reason: string; occurredAtUtc: string;
};
export type CountLine = StockTarget & { lineId: string; expectedBeforeX1000: number; countedX1000: number };
export type CountDraft = { countSessionId: string; startedAtUtc: string; notes: string | null; lines: CountLine[] };
export type CountFinalize = { operationId: string; finalizedAtUtc: string };
export type CountSession = { countSessionId: string; state: "Draft" | "Finalized"; lines: (CountLine & {quantityDeltaX1000:number})[] };
type Commit<T> = { evidence: T; isReplay: boolean };
const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
function id(value:string) {
  if (!guid.test(value) || value === "00000000-0000-0000-0000-000000000000") throw new Error("Nonempty GUID required.");
}
function target(x:StockTarget) {
  id(x.stockTargetId);
  if (x.stockTargetType !== "Ingredient" && x.stockTargetType !== "Product") throw new Error("Invalid stock target type.");
}
function instant(value:string) {
  if (!Number.isFinite(Date.parse(value)) || !/(Z|[+-]\d\d:\d\d)$/.test(value)) throw new Error("Explicit timestamp required.");
}
function quantity(n:number) {
  if (!Number.isSafeInteger(n)||n<0) throw new Error("Nonnegative integer stock quantity required.");
}
export async function commitAdjustment(command:AdjustmentCommand) {
  target(command);id(command.operationId);id(command.adjustmentEventId);instant(command.occurredAtUtc);
  if (!Number.isSafeInteger(command.quantityDeltaX1000)||command.quantityDeltaX1000===0||!command.reason.trim()) throw new Error("Nonzero integer adjustment and reason required.");
  return hostRequest<Commit<AdjustmentCommand>>("/api/cafe/inventory/adjust",command,"POST",command.operationId);
}
export async function saveCountDraft(operationId:string,draft:CountDraft) {
  id(operationId);id(draft.countSessionId);instant(draft.startedAtUtc);
  if (!draft.lines.length) throw new Error("At least one count line required.");
  const targets=new Set<string>(), lineIds=new Set<string>();
  for(const line of draft.lines) {
    target(line);id(line.lineId);quantity(line.expectedBeforeX1000);quantity(line.countedX1000);
    const key=line.stockTargetType+":"+line.stockTargetId.toLowerCase(), lineKey=line.lineId.toLowerCase();
    if(targets.has(key)||lineIds.has(lineKey)) throw new Error("Duplicate count target or line.");
    targets.add(key);lineIds.add(lineKey);
  }
  return hostRequest<CountSession>("/api/cafe/inventory/counts/draft",draft,"POST",operationId);
}
export async function finalizeCount(countSessionId:string,command:CountFinalize) {
  id(countSessionId);id(command.operationId);instant(command.finalizedAtUtc);
  return hostRequest<Commit<CountSession>>("/api/cafe/inventory/counts/"+countSessionId+"/finalize",command,"POST",command.operationId);
}
export async function readCount(countSessionId:string) {
  id(countSessionId);
  return hostRequest<CountSession>("/api/cafe/inventory/counts/"+countSessionId,undefined,"GET");
}
