import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { purchaseOrderPrintIssue } from "../src/utils/purchaseOrderPrint.ts";

// 2 × ₱500.00 gross = ₱1,000.00; discount ₱100.00;
// tax ₱108.00 on the net ₱900.00; freight ₱25.00 => ₱1,033.00.
const approvedPo = () => ({
  id: "po-print-regression",
  subtotalCents: 100000,
  discountCents: 10000,
  taxCents: 10800,
  shippingCents: 2500,
  totalCents: 103300,
  items: [{
    id: "line-1",
    orderedQuantityX1000: 2000,
    unitCostCents: 50000,
    totalCostCents: 100800,
  }],
});

test("nonzero discount, VAT and freight reconcile to the exact authoritative total", () => {
  assert.equal(purchaseOrderPrintIssue(approvedPo()), null);
});

test("rejects an inconsistent gross-minus-discount-plus-tax-plus-shipping equation", () => {
  assert.match(purchaseOrderPrintIssue({ ...approvedPo(), totalCents: 103301 }), /does not equal/);
});

test("rejects a missing shipping or discount value instead of inventing zero", () => {
  assert.match(purchaseOrderPrintIssue({ ...approvedPo(), shippingCents: undefined }), /missing or invalid/);
  assert.match(purchaseOrderPrintIssue({ ...approvedPo(), discountCents: undefined }), /missing or invalid/);
});

test("rejects invalid cents, negative charges, and line subtotal drift", () => {
  assert.match(purchaseOrderPrintIssue({ ...approvedPo(), taxCents: NaN }), /missing or invalid/);
  assert.match(purchaseOrderPrintIssue({ ...approvedPo(), shippingCents: -1 }), /missing or invalid/);
  assert.match(purchaseOrderPrintIssue({ ...approvedPo(), subtotalCents: 99999 }), /line subtotals/);
  assert.match(purchaseOrderPrintIssue({ ...approvedPo(), discountCents: 100001 }), /discount exceeds/);
});

test("rejects net line totals that disagree with the authoritative total", () => {
  const po = approvedPo();
  po.items[0].totalCostCents = 100799;
  assert.match(purchaseOrderPrintIssue(po), /line totals and shipping/);
});

test("PO contract and print view include explicit discount and shipping data", () => {
  const api = readFileSync(new URL("../src/api/procureToPay.ts", import.meta.url), "utf8");
  const modal = readFileSync(new URL("../src/components/documents/PrintablePurchaseOrderModal.tsx", import.meta.url), "utf8");
  assert.match(api, /discountCents: number;/);
  assert.match(api, /shippingCents: number;/);
  assert.match(api, /const discountCents = amount\(raw\.discountCents\)/);
  assert.match(api, /const shippingCents = amount\(raw\.shippingCents\)/);
  assert.match(modal, /Discount:/);
  assert.match(modal, /Shipping \/ Freight:/);
  assert.match(modal, /purchaseOrderPrintIssue\(po\)/);
  assert.match(modal, /print:hidden/);
});
