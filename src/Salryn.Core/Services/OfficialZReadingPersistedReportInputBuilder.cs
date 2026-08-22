using Salryn.Core.Models;

namespace Salryn.Core.Services;

public static class OfficialZReadingPersistedReportInputBuilder
{
    public static OfficialReadingReportInput Build(OfficialZReadingPersistedRecord record)
    {
        OfficialZReadingSummary summary = record.Summary;
        OfficialZReadingEnrichmentPayload? enrichment = record.Enrichment;

        int voidCount = enrichment?.Lifecycle.VoidCount ?? 0;
        int cancelCount = enrichment?.Lifecycle.CancelCount ?? 0;
        int correctionCount = enrichment?.Lifecycle.CorrectionCount ?? 0;

        long grossSales = enrichment?.AdjustedSummary.GrossSalesCents ?? summary.GrossSalesCents;
        long netSales = enrichment?.AdjustedSummary.TotalSalesCents ?? summary.NetSalesCents;
        long discount = enrichment?.AdjustedSummary.DiscountTotalCents ?? summary.DiscountTotalCents;
        long tax = enrichment?.AdjustedSummary.TaxTotalCents ?? summary.TaxTotalCents;
        long totalSales = enrichment?.AdjustedSummary.TotalSalesCents ?? summary.TotalSalesCents;
        int transactionCount = enrichment?.AdjustedSummary.ActiveEntryCount ?? summary.EntryCount;

        return new OfficialReadingReportInput(
            ReportKind: OfficialReadingReportKind.ZReading,
            ReadingNumber: summary.ZReadingId.ToString("D"),
            ReportNumber: summary.ZReadingId.ToString("D"),
            GeneratedAt: summary.FinalizedAt,
            TerminalId: summary.TerminalId ?? "(unknown)",
            CashierUserId: summary.CreatedByUserId,
            CashierIdentifier: null,
            ReportLabel: "OFFICIAL_Z_READING",
            MachineProfile: null,
            BusinessPeriod: new OfficialReadingReportBusinessPeriod(
                DateOnly.FromDateTime(summary.RangeStartedAt.UtcDateTime),
                summary.RangeStartedAt,
                summary.RangeEndedAt,
                IsFinalizedZReading: true,
                PreviousZReadingId: null,
                CurrentZReadingId: summary.ZReadingId),
            SalesTotals: new OfficialReadingReportSalesTotals(
                grossSales,
                netSales,
                discount,
                RefundVoidCancelCents: null,
                TaxableSalesCents: null,
                NonTaxableSalesCents: null,
                totalSales,
                transactionCount,
                voidCount,
                cancelCount,
                correctionCount),
            TaxTotals: BuildTaxTotals(record.TaxModes, enrichment),
            PaymentTotals: record.Payments
                .Select(payment => new OfficialReadingReportPaymentTotal(
                    payment.Method,
                    payment.AmountCents,
                    payment.PaymentCount))
                .ToArray(),
            CashierOrTerminalBreakdown: Array.Empty<OfficialReadingReportBreakdownLine>(),
            LifecycleCoverage: null,
            Evidence: new OfficialReadingReportEvidence(
                summary.EvidenceManifestHash,
                null,
                "SHA-256",
                enrichment is null
                    ? "OfficialZReadingPersistedRecord base summary"
                    : "OfficialZReadingPersistedRecord enriched metadata"));
    }

    private static OfficialReadingReportTaxTotals BuildTaxTotals(
        IReadOnlyList<OfficialXReadingTaxModeSummary> taxModes,
        OfficialZReadingEnrichmentPayload? enrichment)
    {
        OfficialXReadingTaxModeSummary primary = taxModes.Count > 0
            ? taxModes[0]
            : new OfficialXReadingTaxModeSummary(
                "UNKNOWN",
                0,
                0,
                0,
                0,
                0,
                VatBucketEngineDeferred: true,
                Notes: null);

        IReadOnlyList<string>? deferredNotes = enrichment?.VatBuckets.Count > 0
            ? enrichment.VatBuckets
                .Select(bucket => bucket.BucketKind + ":" + bucket.SalesAmountCents + "/" + bucket.TaxAmountCents)
                .ToArray()
            : null;

        return new OfficialReadingReportTaxTotals(
            primary.TaxModeCode,
            primary.TaxTotalCents,
            primary.VatBucketEngineDeferred,
            primary.Notes,
            deferredNotes);
    }
}
