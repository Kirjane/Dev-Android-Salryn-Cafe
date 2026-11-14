import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import {
  customerQuotationIntegrityIssue,
  customerQuotationDisplayItemCount,
} from "../src/utils/customerQuotationIntegrity.ts";

const quotation = () => ({
  id: "saved-quote",
  status: "Issued",
  subtotalCents: 165000,
  discountCents: 0,
  totalCents: 165000,
  lines: [
    {
      id: "qline-1",
      quotationId: "saved-quote",
      productId: "product-1",
      productNameSnapshot: "Industrial Tool Set",
      unitNameSnapshot: "pcs",
      quantityX1000: 2000,
      unitPriceCents: 85000,
      discountCents: 5000,
      lineTotalCents: 165000,
    },
  ],
});

test("QUOTE-46/47: single persisted line with discount reconciles", () => {
  assert.equal(customerQuotationIntegrityIssue(quotation()), null);
  assert.equal(customerQuotationDisplayItemCount(quotation()), 1);
});

test("QUOTE-51: multi-line quotation with same SKU and distinct prices/discounts reconciles", () => {
  const multi = quotation();
  multi.lines.push({
    id: "qline-2",
    quotationId: "saved-quote",
    productId: "product-1",
    productNameSnapshot: "Industrial Tool Set",
    unitNameSnapshot: "pcs",
    quantityX1000: 1000,
    unitPriceCents: 80000,
    discountCents: 0,
    lineTotalCents: 80000,
  });
  multi.subtotalCents = 245000;
  multi.discountCents = 5000;
  multi.totalCents = 240000;
  assert.equal(customerQuotationIntegrityIssue(multi), null);
  assert.equal(customerQuotationDisplayItemCount(multi), 2);
});

test("QUOTE-46: empty or unhydrated lines never render as valid zero-item printable quote", () => {
  const unhydrated = { ...quotation(), lines: [] };
  assert.match(customerQuotationIntegrityIssue(unhydrated), /no persisted item lines/);
  assert.equal(customerQuotationDisplayItemCount(unhydrated), null);
  assert.match(
    customerQuotationIntegrityIssue({ ...quotation(), lines: undefined }),
    /no persisted item lines/,
  );
});

test("QUOTE-47: invalid amounts, discounts, quantities, and mismatched totals block printing", () => {
  const base = quotation();
  assert.match(customerQuotationIntegrityIssue({ ...base, totalCents: 164999 }), /does not reconcile/);
  assert.match(customerQuotationIntegrityIssue({ ...base, subtotalCents: 165001, totalCents: 165001 }), /do not reconcile/);
  assert.match(customerQuotationIntegrityIssue({ ...base, discountCents: 200000, totalCents: 0 }), /discount exceeds subtotal/);
  assert.match(customerQuotationIntegrityIssue({ ...base, discountCents: -1 }), /Invalid quotation header/);
  assert.match(
    customerQuotationIntegrityIssue({ ...base, lines: [{ ...base.lines[0], quantityX1000: 0 }] }),
    /invalid quantity/,
  );
  assert.match(
    customerQuotationIntegrityIssue({ ...base, lines: [{ ...base.lines[0], discountCents: 180000 }] }),
    /line discount exceeds its gross/,
  );
  assert.match(
    customerQuotationIntegrityIssue({ ...base, lines: [{ ...base.lines[0], lineTotalCents: 170000 }] }),
    /does not reconcile/,
  );
  assert.match(
    customerQuotationIntegrityIssue({ ...base, lines: [{ ...base.lines[0], unitNameSnapshot: " " }] }),
    /incomplete product line/,
  );
});

test("QUOTE-46/51: UI loads verified quotation detail, preserves distinct pricing lines, and gates print modal", () => {
  const page = readFileSync(new URL("../src/pages/PriceCanvassPage.tsx", import.meta.url), "utf8");
  const modal = readFileSync(
    new URL("../src/components/documents/PrintableCustomerQuotationModal.tsx", import.meta.url),
    "utf8",
  );
  assert.match(page, /fetchCustomerQuotation\(quoteId\)/);
  assert.match(page, /customerQuotationDisplayItemCount\(q\)/);
  assert.match(page, /Verify line data/);
  assert.doesNotMatch(page, /onClick=\{\(\) => setViewingQuote\(q\)\}/);
  assert.match(page, /l\.productId === prod\.id && l\.unitPriceCents === priceCents && l\.discountCents === 0 && discountCents === 0/);
  assert.match(page, /key=\{line\.lineKey\}/);
  assert.match(modal, /customerQuotationIntegrityIssue\(quotation\)/);
  assert.match(modal, /disabled=\{Boolean\(printIssue\)\}/);
  assert.match(modal, /print:hidden/);
});
