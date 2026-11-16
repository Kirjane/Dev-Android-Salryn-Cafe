import assert from "node:assert/strict";
import test from "node:test";
import { localDateAfterDays } from "../src/utils/customerDocumentDates.ts";

const isDateOnly = (value) => /^\d{4}-\d{2}-\d{2}$/.test(value);

test("C07: quotation validity is DateOnly, including near local midnight and month/year boundaries", () => {
  const nearMidnight = new Date(2026, 10, 17, 23, 58);
  assert.equal(localDateAfterDays(7, nearMidnight), "2026-11-24");
  assert.equal(localDateAfterDays(14, nearMidnight), "2026-12-01");
  assert.equal(localDateAfterDays(30, nearMidnight), "2026-12-17");
  assert.equal(localDateAfterDays(60, nearMidnight), "2027-01-16");
  assert.equal(isDateOnly(localDateAfterDays(14, nearMidnight)), true);
});

test("C07: leap day and daylight-saving transition use calendar dates", () => {
  assert.equal(localDateAfterDays(1, new Date(2028, 1, 28, 23, 50)), "2028-02-29");
  assert.equal(localDateAfterDays(2, new Date(2028, 1, 28, 23, 50)), "2028-03-01");
  assert.equal(localDateAfterDays(2, new Date(2026, 2, 7, 23, 50)), "2026-03-09");
});

test("C07: invalid duration or clock is rejected instead of creating malformed dates", () => {
  for (const value of [-1, NaN, Infinity, 1.5]) {
    assert.throws(() => localDateAfterDays(value), RangeError);
  }
  assert.throws(() => localDateAfterDays(14, new Date(NaN)), RangeError);
});

test("C07/C08 contract repro: old timestamp shape differs from server DateOnly payload", () => {
  const selectedHtmlDate = "2026-11-21";
  const formerOrderPayload = new Date(selectedHtmlDate).toISOString();
  const formerQuotePayload = new Date("2026-11-17T23:30:00+08:00").toISOString();
  assert.equal(isDateOnly(formerOrderPayload), false);
  assert.equal(isDateOnly(formerQuotePayload), false);
  assert.equal(isDateOnly(selectedHtmlDate), true);
  assert.equal(isDateOnly(localDateAfterDays(14, new Date(2026, 10, 17, 23, 30))), true);
});
