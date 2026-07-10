# M4-RC2 Line Tax Reversal Matrix

Source of truth: frozen snapshot V3 line tax components (never live product tax).

| Mode | Line class | Reversal components |
|------|------------|---------------------|
| VAT_REGISTERED | Vatable | VatableBase + VatAmount (inclusive split) |
| VAT_REGISTERED | VatExempt | VatExempt amount |
| VAT_REGISTERED | ZeroRated | ZeroRated amount |
| NON_VAT_PERCENTAGE_TAX | NonVat (SSPT) | Sspt amount |
| NON_VAT_PERCENTAGE_TAX | NonVatExempt | NonVatExempt amount |
| NON_VAT_EXEMPT | NonVatExempt | NonVatExempt only; VAT buckets must be 0 |

## Quantity math

`unitRefund = lineTotal / originalQty` (integer cents; remainder on last returned unit of that operation).  
Partial returns use floor division with remainder allocated to the last unit in the current request (deterministic AwayFromZero on bucket components when splitting VAT-inclusive).

## Guards

- Cumulative returned qty ≤ original qty  
- Cumulative reversed bucket ≤ original frozen bucket  
- Mixed V1/V2 partial refund without line tax evidence → fail closed  
- V1/V2 full document void may reverse aggregate buckets when complete
