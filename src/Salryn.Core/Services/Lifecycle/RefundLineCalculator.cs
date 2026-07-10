using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;

namespace Salryn.Core.Services.Lifecycle;

/// <summary>
/// Pure line-level refund calculator from frozen V3 evidence.
/// Never uses refund-total / original-total stock ratios.
/// </summary>
public static class RefundLineCalculator
{
    public sealed record PriorReturn(Guid SaleItemId, long QuantityX1000, long RefundAmountCents);

    public static Result<(IReadOnlyList<LifecycleRefundLineResult> Lines, LifecycleTaxReversalTotals Totals)> Calculate(
        FrozenSnapshotDocument original,
        IReadOnlyList<LifecycleRefundLineRequest> requests,
        IReadOnlyList<PriorReturn> priorReturns)
    {
        if (requests is null || requests.Count == 0)
        {
            return Result<(IReadOnlyList<LifecycleRefundLineResult>, LifecycleTaxReversalTotals)>.Fail(
                Error.Validation("DocumentLifecycle.RefundLinesRequired", "At least one refund line is required."));
        }

        if (original.Lines.Count == 0)
        {
            return Result<(IReadOnlyList<LifecycleRefundLineResult>, LifecycleTaxReversalTotals)>.Fail(
                Error.Validation(
                    "DocumentLifecycle.LineTaxEvidenceRequired",
                    "Partial refund requires snapshot V3 line-tax evidence. Full-document void may still use aggregates."));
        }

        Dictionary<Guid, long> priorQty = new();
        foreach (PriorReturn p in priorReturns)
        {
            priorQty[p.SaleItemId] = priorQty.GetValueOrDefault(p.SaleItemId) + p.QuantityX1000;
        }

        List<LifecycleRefundLineResult> results = new();
        long sumGross = 0, sumVatable = 0, sumVat = 0, sumExempt = 0, sumZero = 0, sumSspt = 0, sumNv = 0;

        foreach (LifecycleRefundLineRequest req in requests)
        {
            if (req.QuantityX1000 <= 0)
            {
                return Result<(IReadOnlyList<LifecycleRefundLineResult>, LifecycleTaxReversalTotals)>.Fail(
                    Error.Validation("DocumentLifecycle.QuantityRequired", "Return quantity must be positive."));
            }

            FrozenSnapshotLine? line = original.Lines.FirstOrDefault(l => l.SaleItemId == req.SaleItemId);
            if (line is null)
            {
                return Result<(IReadOnlyList<LifecycleRefundLineResult>, LifecycleTaxReversalTotals)>.Fail(
                    Error.NotFound("DocumentLifecycle.SaleItemNotOnOriginal", "Sale item is not on the original document."));
            }

            long already = priorQty.GetValueOrDefault(line.SaleItemId);
            long remaining = line.QuantityX1000 - already;
            if (req.QuantityX1000 > remaining)
            {
                return Result<(IReadOnlyList<LifecycleRefundLineResult>, LifecycleTaxReversalTotals)>.Fail(
                    Error.Validation("DocumentLifecycle.OverQuantity", "Return quantity exceeds remaining quantity for line."));
            }

            LineMoney money = AllocateProportion(line, req.QuantityX1000);
            if (req.ExpectedRefundAmountCents is long expected && expected != money.Gross)
            {
                return Result<(IReadOnlyList<LifecycleRefundLineResult>, LifecycleTaxReversalTotals)>.Fail(
                    Error.Validation(
                        "DocumentLifecycle.ExpectedAmountMismatch",
                        "Client expected refund amount does not match server calculation."));
            }

            if (req.Disposition == StockReturnDisposition.ServiceNoStock && line.StockTracked)
            {
                // Allowed: operator may choose service disposition even if product was stock-tracked only when not restocking.
            }

            if (req.Disposition == StockReturnDisposition.Restock && !line.StockTracked)
            {
                return Result<(IReadOnlyList<LifecycleRefundLineResult>, LifecycleTaxReversalTotals)>.Fail(
                    Error.Validation("DocumentLifecycle.RestockNotStockTracked", "Cannot restock a non-stock-tracked line."));
            }

            results.Add(new LifecycleRefundLineResult(
                line.SaleItemId,
                line.LineIndex,
                req.QuantityX1000,
                money.Gross,
                money.VatableBase,
                money.Vat,
                money.VatExempt,
                money.ZeroRated,
                money.Sspt,
                money.NonVatExempt,
                req.Disposition));

            priorQty[line.SaleItemId] = already + req.QuantityX1000;
            sumGross = checked(sumGross + money.Gross);
            sumVatable = checked(sumVatable + money.VatableBase);
            sumVat = checked(sumVat + money.Vat);
            sumExempt = checked(sumExempt + money.VatExempt);
            sumZero = checked(sumZero + money.ZeroRated);
            sumSspt = checked(sumSspt + money.Sspt);
            sumNv = checked(sumNv + money.NonVatExempt);
        }

        LifecycleTaxReversalTotals totals = new(
            sumVatable, sumVat, sumExempt, sumZero, sumSspt, sumNv, sumGross);

        return Result<(IReadOnlyList<LifecycleRefundLineResult>, LifecycleTaxReversalTotals)>.Success((results, totals));
    }

    private sealed record LineMoney(
        long Gross, long VatableBase, long Vat, long VatExempt, long ZeroRated, long Sspt, long NonVatExempt);

    /// <summary>
    /// Deterministic proportional split by quantity. Remainder cents go to the last unit of this request.
    /// </summary>
    private static LineMoney AllocateProportion(FrozenSnapshotLine line, long qty)
    {
        if (qty == line.QuantityX1000)
        {
            return new LineMoney(
                line.LineTotalCents,
                line.VatableBaseCents,
                line.VatAmountCents,
                line.VatExemptCents,
                line.ZeroRatedCents,
                line.SsptCents,
                line.NonVatExemptCents);
        }

        long Scale(long full) =>
            full == 0 || line.QuantityX1000 == 0
                ? 0
                : (long)decimal.Round((decimal)full * qty / line.QuantityX1000, 0, MidpointRounding.AwayFromZero);

        long gross = Scale(line.LineTotalCents);
        long vatable = Scale(line.VatableBaseCents);
        long vat = Scale(line.VatAmountCents);
        long exempt = Scale(line.VatExemptCents);
        long zero = Scale(line.ZeroRatedCents);
        long sspt = Scale(line.SsptCents);
        long nv = Scale(line.NonVatExemptCents);

        // Keep VATable + VAT consistent with inclusive gross for vatable lines when possible.
        if (line.TaxClassification == OfficialDocumentLineTaxClassification.Vatable &&
            line.VatableBaseCents + line.VatAmountCents == line.LineTotalCents)
        {
            vat = OfficialDocumentTaxBucketEngine.ComputeInclusiveVatAmountCents(gross);
            vatable = checked(gross - vat);
        }

        return new LineMoney(gross, vatable, vat, exempt, zero, sspt, nv);
    }
}
