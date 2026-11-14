import type { CustomerQuotation } from "../api/customerOrdersAndQuotes";

// Financial and line integrity of the exact serialized quotation received from the API.
// Never infer product rows from header totals or interpret unhydrated [] as zero items.
export function customerQuotationIntegrityIssue(
  quotation: Pick<CustomerQuotation, "lines" | "subtotalCents" | "discountCents" | "totalCents">,
): string | null {
  if (
    !Number.isSafeInteger(quotation.subtotalCents) ||
    quotation.subtotalCents < 0 ||
    !Number.isSafeInteger(quotation.discountCents) ||
    quotation.discountCents < 0 ||
    !Number.isSafeInteger(quotation.totalCents) ||
    quotation.totalCents < 0
  ) {
    return "Invalid quotation header amount.";
  }
  if (quotation.discountCents > quotation.subtotalCents) {
    return "Quotation discount exceeds subtotal.";
  }
  if (quotation.subtotalCents - quotation.discountCents !== quotation.totalCents) {
    return "Quotation total does not reconcile to subtotal minus discount.";
  }
  if (!Array.isArray(quotation.lines) || quotation.lines.length === 0) {
    return "Quotation has no persisted item lines. Verify the database before printing.";
  }

  let sum = 0n;
  for (const line of quotation.lines) {
    if (
      !line ||
      !line.productId ||
      !line.productNameSnapshot?.trim() ||
      !line.unitNameSnapshot?.trim()
    ) {
      return "Quotation contains an incomplete product line.";
    }
    if (
      !Number.isSafeInteger(line.quantityX1000) ||
      line.quantityX1000 <= 0 ||
      !Number.isSafeInteger(line.unitPriceCents) ||
      line.unitPriceCents < 0 ||
      !Number.isSafeInteger(line.discountCents) ||
      line.discountCents < 0 ||
      !Number.isSafeInteger(line.lineTotalCents) ||
      line.lineTotalCents < 0
    ) {
      return "Quotation has an invalid quantity, unit price, discount, or line amount.";
    }

    const gross = (BigInt(line.quantityX1000) * BigInt(line.unitPriceCents) + 500n) / 1000n;
    const lineDiscount = BigInt(line.discountCents);
    if (lineDiscount > gross) {
      return "A quotation line discount exceeds its gross amount.";
    }
    const expectedNet = gross - lineDiscount;
    if (expectedNet !== BigInt(line.lineTotalCents)) {
      return "A quotation line does not reconcile to its quantity, unit price, and discount.";
    }
    sum += expectedNet;
  }

  if (sum !== BigInt(quotation.subtotalCents)) {
    return "Quotation line totals do not reconcile to the saved subtotal.";
  }
  return null;
}

export function customerQuotationDisplayItemCount(
  quotation: Pick<CustomerQuotation, "lines" | "subtotalCents" | "discountCents" | "totalCents">,
): number | null {
  return customerQuotationIntegrityIssue(quotation) === null ? quotation.lines.length : null;
}
