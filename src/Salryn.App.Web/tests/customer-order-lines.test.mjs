import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import {
  customerOrderIntegrityIssue,
  customerOrderDisplayItemCount,
} from "../src/utils/customerOrderIntegrity.ts";

const order = () => ({
  id: "saved-order",
  status: "Confirmed",
  totalCents: 7000,
  lines: [
    {
      id: "line-1", productId: "product-1",
      productNameSnapshot: "Coffee Beans", unitNameSnapshot: "pack",
      quantityX1000: 2000, unitPriceCents: 3500, lineTotalCents: 7000,
    },
  ],
});

test("CO-LINE-01: one persisted line shows one item with PHP 70 total", () => {
  assert.equal(customerOrderIntegrityIssue(order()), null);
  assert.equal(customerOrderDisplayItemCount(order()), 1);
});

test("CO-LINE-02: mult-line prices, quantity and line totals reconcile", () => {
  const multi = order();
  multi.lines.push({
    id: "line-2", productId: "product-2",
    productNameSnapshot: "Paper Cups", unitNameSnapshot: "pack",
    quantityX1000: 500, unitPriceCents: 2000, lineTotalCents: 1000,
  });
  multi.totalCents = 8000;
  assert.equal(customerOrderIntegrityIssue(multi), null);
  assert.equal(customerOrderDisplayItemCount(multi), 2);
});

test("CO-LINE-07/08: empty or unhydrated lines never become a printable zero-item order", () => {
  const corrupt = { ...order(), lines: [] };
  assert.match(customerOrderIntegrityIssue(corrupt), /no persisted item lines/);
  assert.equal(customerOrderDisplayItemCount(corrupt), null);
  assert.match(customerOrderIntegrityIssue({ ...order(), lines: undefined }), /no persisted item lines/);
});

test("CO-LINE-02/08: amount, quantity, content and total drift blocks printing", () => {
  const source = order();
  assert.match(customerOrderIntegrityIssue({ ...source, totalCents: 7001 }), /do not reconcile/);
  assert.match(customerOrderIntegrityIssue({ ...source, lines: [{ ...source.lines[0], quantityX1000: 0 }] }), /invalid quantity/);
  assert.match(customerOrderIntegrityIssue({ ...source, lines: [{ ...source.lines[0], unitPriceCents: -1 }] }), /invalid quantity/);
  assert.match(customerOrderIntegrityIssue({ ...source, lines: [{ ...source.lines[0], lineTotalCents: 6999 }] }), /does not reconcile/);
  assert.match(customerOrderIntegrityIssue({ ...source, lines: [{ ...source.lines[0], productNameSnapshot: "" }] }), /incomplete product/);
});

test("CO-LINE-07: UI loads persisted detail for slip; print component gates invalid lines", () => {
  const page = readFileSync(new URL("../src/pages/CustomerOrdersPage.tsx", import.meta.url), "utf8");
  const slip = readFileSync(new URL("../src/components/documents/PrintableCustomerOrderModal.tsx", import.meta.url), "utf8");
  assert.match(page, /const detail = await fetchCustomerOrder\(orderId\)/);
  assert.match(page, /customerOrderDisplayItemCount\(o\)/);
  assert.match(page, /Verify line data/);
  assert.doesNotMatch(page, /onClick=\{\(\) => setViewingOrder\(o\)\}/);
  assert.match(slip, /customerOrderIntegrityIssue\(order\)/);
  assert.match(slip, /disabled=\{Boolean\(printIssue\)\}/);
  assert.match(slip, /print:hidden/);
});
