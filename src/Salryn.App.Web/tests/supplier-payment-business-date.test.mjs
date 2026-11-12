import assert from "node:assert/strict";
import test from "node:test";
import { philippineBusinessToday, isValidActualFundsTransferDate } from "../src/utils/supplierPaymentBusinessDate.ts";

test("Asia/Manila UTC+08 rollover is independent of host local timezone", () => {
  assert.equal(philippineBusinessToday(new Date("2026-11-12T15:59:59Z")), "2026-11-12");
  assert.equal(philippineBusinessToday(new Date("2026-11-12T16:00:00Z")), "2026-11-13");
  assert.equal(philippineBusinessToday(new Date("2026-11-12T23:30:00Z")), "2026-11-13");
  assert.equal(philippineBusinessToday(new Date("2026-11-13T16:00:00Z")), "2026-11-14");
});

test("actual transfer date allows genuine past/current dates but rejects future/invalid/missing", () => {
  const businessToday = "2026-11-13";
  for (const date of ["2026-11-10", "2026-11-12", "2026-11-13"]) {
    assert.equal(isValidActualFundsTransferDate(date, businessToday), true, date);
  }
  for (const date of ["", "2026-11-14", "2099-01-01", "2026-02-30", "2026-13-01", "2026-2-03", "yes"]) {
    assert.equal(isValidActualFundsTransferDate(date, businessToday), false, date);
  }
});
