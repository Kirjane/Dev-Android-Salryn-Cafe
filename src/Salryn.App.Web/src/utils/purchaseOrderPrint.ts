import type { PurchaseOrder } from "../api/procureToPay";

// Monetary figures are integer cent amounts. Never print a PO unless the
// server's gross, discount, tax, shipping, lines and total reconcile exactly.
export function purchaseOrderPrintIssue(po: PurchaseOrder): string | null {
  const isCents = (value: number) => Number.isSafeInteger(value) && value >= 0;
  const fields = [
    po.subtotalCents,
    po.discountCents,
    po.taxCents,
    po.shippingCents,
    po.totalCents,
  ];
  if (!fields.every(isCents)) return "Required purchase order amounts are missing or invalid.";
  if (!Array.isArray(po.items) || po.items.length === 0)
    return "Purchase order must contain at least one line.";
  if (po.discountCents > po.subtotalCents)
    return "Purchase order discount exceeds the subtotal.";

  let grossLines = 0n;
  let netLines = 0n;
  for (const line of po.items) {
    if (!Number.isSafeInteger(line.orderedQuantityX1000) || line.orderedQuantityX1000 <= 0 ||
        !isCents(line.unitCostCents) || !isCents(line.totalCostCents))
      return "Purchase order contains invalid line quantities or amounts.";
    grossLines += BigInt(line.orderedQuantityX1000) * BigInt(line.unitCostCents) / 1000n;
    netLines += BigInt(line.totalCostCents);
  }

  if (grossLines !== BigInt(po.subtotalCents))
    return "Purchase order line subtotals do not match the document subtotal.";
  const reconciledTotal = BigInt(po.subtotalCents) - BigInt(po.discountCents) +
    BigInt(po.taxCents) + BigInt(po.shippingCents);
  if (reconciledTotal !== BigInt(po.totalCents))
    return "Subtotal − discount + tax + shipping does not equal the stated total.";
  if (netLines + BigInt(po.shippingCents) !== BigInt(po.totalCents))
    return "Purchase order line totals and shipping do not reconcile to the total.";
  return null;
}
