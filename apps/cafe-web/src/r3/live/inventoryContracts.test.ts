import { beforeEach, describe, expect, it, vi } from "vitest";
import { hostRequest } from "../../api/hostClient";
import {
  commitAdjustment,
  finalizeCount,
  readCount,
  saveCountDraft,
  type AdjustmentCommand,
  type CountDraft,
  type CountSession,
  type InventoryAdjustmentEvidence,
} from "./inventoryContracts";

vi.mock("../../api/hostClient", () => ({ hostRequest: vi.fn() }));

const uuid = (n: number) => "40000000-0000-0000-0000-" + String(n).padStart(12, "0");
const at = "2026-11-25T00:00:00Z";

const adjustment: AdjustmentCommand = {
  operationId: uuid(1),
  adjustmentEventId: uuid(2),
  stockTargetType: "Ingredient",
  stockTargetId: uuid(3),
  quantityDeltaX1000: -1000,
  reason: "Correction",
  occurredAtUtc: at,
};

const draft: CountDraft = {
  countSessionId: uuid(4),
  startedAtUtc: at,
  notes: null,
  lines: [
    {
      lineId: uuid(5),
      stockTargetType: "Ingredient",
      stockTargetId: uuid(3),
      expectedBeforeX1000: 1000,
      countedX1000: 0,
    },
  ],
};

beforeEach(() => vi.clearAllMocks());

describe("canonical inventory transport", () => {
  it("retains adjustment body, timestamp and id on retry and returns full Host evidence shape", async () => {
    const evidence: InventoryAdjustmentEvidence = {
      adjustmentEventId: uuid(2),
      operationId: uuid(1),
      storeId: uuid(99),
      stockTargetType: "Ingredient",
      stockTargetId: uuid(3),
      nameSnapshot: "Whole Milk",
      stockUnitSnapshot: "Milliliter",
      quantityDeltaX1000: -1000,
      reason: "Correction",
      referenceType: null,
      referenceId: null,
      correctionOfReferenceId: null,
      actorUserId: uuid(98),
      occurredAtUtc: at,
      contentHash: "abc123",
    };
    vi.mocked(hostRequest)
      .mockResolvedValueOnce({ evidence, isReplay: false })
      .mockResolvedValueOnce({ evidence, isReplay: true });

    const first = await commitAdjustment(adjustment);
    const replay = await commitAdjustment(adjustment);
    expect(hostRequest).toHaveBeenNthCalledWith(1, "/api/cafe/inventory/adjust", adjustment, "POST", uuid(1));
    expect(hostRequest).toHaveBeenNthCalledWith(2, "/api/cafe/inventory/adjust", adjustment, "POST", uuid(1));
    expect(first.evidence.nameSnapshot).toBe("Whole Milk");
    expect(first.isReplay).toBe(false);
    expect(replay.isReplay).toBe(true);
  });

  it("uses separate draft and finalize requests with explicit stable IDs and full Host CountSession shape", async () => {
    const session: CountSession = {
      countSessionId: uuid(4),
      storeId: uuid(99),
      state: "Finalized",
      startedByUserId: uuid(98),
      startedAtUtc: at,
      finalizedByUserId: uuid(98),
      finalizedAtUtc: at,
      notes: null,
      finalizeOperationId: uuid(7),
      finalizedContentHash: "hash-final",
      lines: [
        {
          lineId: uuid(5),
          stockTargetType: "Ingredient",
          stockTargetId: uuid(3),
          nameSnapshot: "Whole Milk",
          stockUnitSnapshot: "Milliliter",
          expectedBeforeX1000: 1000,
          countedX1000: 0,
          quantityDeltaX1000: -1000,
        },
      ],
    };
    vi.mocked(hostRequest)
      .mockResolvedValueOnce({ ...session, state: "Draft", finalizedAtUtc: null, finalizedByUserId: null, finalizeOperationId: null, finalizedContentHash: null })
      .mockResolvedValueOnce({ evidence: session, isReplay: false })
      .mockResolvedValueOnce(session);

    const finish = { operationId: uuid(7), finalizedAtUtc: at };
    const saved = await saveCountDraft(uuid(6), draft);
    const finalized = await finalizeCount(uuid(4), finish);
    const loaded = await readCount(uuid(4));

    expect(hostRequest).toHaveBeenNthCalledWith(1, "/api/cafe/inventory/counts/draft", draft, "POST", uuid(6));
    expect(hostRequest).toHaveBeenNthCalledWith(
      2,
      "/api/cafe/inventory/counts/" + uuid(4) + "/finalize",
      finish,
      "POST",
      uuid(7),
    );
    expect(hostRequest).toHaveBeenNthCalledWith(3, "/api/cafe/inventory/counts/" + uuid(4), undefined, "GET");
    expect(saved.state).toBe("Draft");
    expect(finalized.evidence.finalizedContentHash).toBe("hash-final");
    expect(loaded.lines[0].nameSnapshot).toBe("Whole Milk");
  });

  it("rejects invalid quantities and duplicate targets before transport", async () => {
    await expect(commitAdjustment({ ...adjustment, quantityDeltaX1000: 0 })).rejects.toThrow();
    await expect(commitAdjustment({ ...adjustment, quantityDeltaX1000: 0.5 })).rejects.toThrow();
    await expect(
      saveCountDraft(uuid(6), { ...draft, lines: [{ ...draft.lines[0], countedX1000: -1 }] }),
    ).rejects.toThrow();
    await expect(
      saveCountDraft(uuid(6), { ...draft, lines: [draft.lines[0], { ...draft.lines[0], lineId: uuid(8) }] }),
    ).rejects.toThrow();
    expect(hostRequest).not.toHaveBeenCalled();
  });

  it("propagates uncertain outcome without sending a replacement operation", async () => {
    vi.mocked(hostRequest).mockRejectedValueOnce(new Error("ERR_IDEMPOTENCY_OUTCOME_UNKNOWN"));
    await expect(finalizeCount(uuid(4), { operationId: uuid(7), finalizedAtUtc: at })).rejects.toThrow(
      "UNKNOWN",
    );
    expect(hostRequest).toHaveBeenCalledTimes(1);
  });
});
