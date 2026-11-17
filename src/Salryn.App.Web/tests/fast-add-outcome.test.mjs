import assert from "node:assert/strict";
import test from "node:test";
import { refreshAfterCommittedFastAdd } from "../src/utils/fastAddOutcome.ts";

test("committed Fast Add: catalog refresh success is advisory", async () => {
  let calls = 0;
  const refreshed = await refreshAfterCommittedFastAdd(async () => {
    calls++;
    return { isSuccess: true };
  });
  assert.equal(refreshed, true);
  assert.equal(calls, 1);
});

test("committed Fast Add: rejected catalog refresh must not reject creation", async () => {
  let calls = 0;
  const refreshed = await refreshAfterCommittedFastAdd(async () => {
    calls++;
    throw new Error("catalog unavailable");
  });
  assert.equal(refreshed, false);
  assert.equal(calls, 1);
});

test("committed Fast Add: synchronous refresh error must not reject creation", async () => {
  const refreshed = await refreshAfterCommittedFastAdd(() => {
    throw new Error("query client unavailable");
  });
  assert.equal(refreshed, false);
});
