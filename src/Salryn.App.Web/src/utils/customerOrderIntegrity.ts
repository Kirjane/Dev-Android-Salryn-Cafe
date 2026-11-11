import type { CustomerOrder } from "../api/customerOrdersAndQuotes";

// Financial and line integrity of the exact serialized order received from the API.
// Never infer product rows from a parent total or interpret unhydrated [] as zero items.
export function customerOrderIntegrityIssue(
  order: Pick<CustomerOrder, "lines" | "totalCents">,
): string | null {
  if (!Number.isSafeInteger(order.totalCents) || order.totalCents < 0)
    return "Invalid order total.";
  if (!Array.isArray(order.lines) || order.lines.length === 0)
    return "Order has no persisted item lines. Verify the database before printing.";

  let sum = 0n;
  for (const line of order.lines) {
    if (!line || !line.productId || !line.productNameSnapshot?.trim() ||
        !line.unitNameSnapshot?.trim())
      return "Order contains an incomplete product line.";
    if (!Number.isSafeInteger(line.quantityX1000) || line.quantityX1000 <= 0 ||
        !Number.isSafeInteger(line.unitPriceCents) || line.unitPriceCents < 0 ||
        !Number.isSafeInteger(line.lineTotalCents) || line.lineTotalCents < 0)
      return "Order has an invalid quantity, unit price or line amount.";

    const expected = (BigInt(line.quantityX1000) * BigInt(line.unitPriceCents) + 500n) / 1000n;
    if (expected !== BigInt(line.lineTotalCents))
      return "An order line does not reconcile to its quantity and unit price.";
    sum += expected;
  }
  if (sum !== BigInt(order.totalCents))
    return "Order line totals do not reconcile to the saved order total.";
  return null;
}

export function customerOrderDisplayItemCount(
  order: Pick<CustomerOrder, "lines" | "totalCents">,
): number | null {
  return customerOrderIntegrityIssue(order) === null ? order.lines.length : null;
}
