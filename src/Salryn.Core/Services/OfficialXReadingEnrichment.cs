using System.Text.Json;
using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Core.Services;

public sealed record OfficialXReadingLifecycleSummary(
    int ReprintCount,
    int VoidCount,
    int CancelCount,
    int CorrectionCount,
    int RefundCount);

public sealed record OfficialXReadingVatBucketSummary(
    string BucketKind,
    long SalesAmountCents,
    long TaxAmountCents,
    int EntryCount);

public sealed record OfficialXReadingAdjustedSummary(
    int ActiveEntryCount,
    long GrossSalesCents,
    long SubtotalCents,
    long DiscountTotalCents,
    long TaxTotalCents,
    long TotalSalesCents);

public static class OfficialXReadingEnrichment
{
    public static OfficialXReadingLifecycleSummary EmptyLifecycle =>
        new(0, 0, 0, 0, 0);

    public static OfficialXReadingAdjustedSummary EmptyAdjusted =>
        new(0, 0, 0, 0, 0, 0);

    public static async Task<Result<(OfficialXReadingLifecycleSummary Lifecycle, OfficialXReadingAdjustedSummary Adjusted, IReadOnlyList<OfficialXReadingVatBucketSummary> VatBuckets)>> BuildAsync(
        IReadOnlyList<OfficialEvidenceEntryReadModel> entries,
        IReadOnlyList<OfficialXReadingLifecycleSnapshotTotals> snapshotTotals,
        IOfficialDocumentLifecycleReadService? lifecycleRead,
        CancellationToken cancellationToken)
    {
        int reprintCount = 0;
        int voidCount = 0;
        int cancelCount = 0;
        int correctionCount = 0;
        int refundCount = 0;
        int activeEntryCount = 0;
        long adjustedGross = 0;
        long adjustedSubtotal = 0;
        long adjustedDiscount = 0;
        long adjustedTax = 0;
        long adjustedTotal = 0;
        Dictionary<string, (long Sales, long Tax, int Count)> vatBuckets = new(StringComparer.Ordinal);

        for (int index = 0; index < entries.Count; index++)
        {
            OfficialEvidenceEntryReadModel entry = entries[index];
            OfficialXReadingLifecycleSnapshotTotals totals = snapshotTotals[index];
            bool excludeFromAdjusted = false;

            if (lifecycleRead is not null)
            {
                Result<OfficialDocumentLifecycleResult> lifecycleResult = await lifecycleRead.ReadAsync(
                    new OfficialDocumentLifecycleReadRequest(
                        entry.JournalEntryId,
                        null,
                        null,
                        null,
                        null),
                    cancellationToken);

                if (lifecycleResult.IsFailure)
                {
                    return Result<(OfficialXReadingLifecycleSummary, OfficialXReadingAdjustedSummary, IReadOnlyList<OfficialXReadingVatBucketSummary>)>.Fail(
                        lifecycleResult.ErrorDetail);
                }

                foreach (OfficialDocumentLifecycleEventReadModel eventRow in lifecycleResult.Value.Events)
                {
                    if (eventRow.ReprintMarker)
                    {
                        reprintCount++;
                    }

                    if (eventRow.VoidMarker)
                    {
                        voidCount++;
                    }

                    if (eventRow.CancelMarker)
                    {
                        cancelCount++;
                    }

                    if (eventRow.RefundMarker)
                    {
                        refundCount++;
                    }

                    if (string.Equals(eventRow.EventType, "CORRECTION", StringComparison.Ordinal))
                    {
                        correctionCount++;
                    }
                }

                OfficialDocumentLifecycleState state = lifecycleResult.Value.Summary.State;

                excludeFromAdjusted = state is OfficialDocumentLifecycleState.Voided
                    or OfficialDocumentLifecycleState.Cancelled
                    or OfficialDocumentLifecycleState.Refunded
                    or OfficialDocumentLifecycleState.Invalid;
            }

            if (!excludeFromAdjusted)
            {
                activeEntryCount++;
                adjustedGross = checked(adjustedGross + totals.GrossSalesCents);
                adjustedSubtotal = checked(adjustedSubtotal + totals.SubtotalCents);
                adjustedDiscount = checked(adjustedDiscount + totals.DiscountTotalCents);
                adjustedTax = checked(adjustedTax + totals.TaxTotalCents);
                adjustedTotal = checked(adjustedTotal + totals.TotalSalesCents);
            }

            foreach (ParsedVatBucketLine bucket in totals.VatBucketLines)
            {
                string key = bucket.Kind;
                (long Sales, long Tax, int Count) current = vatBuckets.GetValueOrDefault(key);
                vatBuckets[key] = (
                    checked(current.Sales + bucket.SalesAmountCents),
                    checked(current.Tax + bucket.TaxAmountCents),
                    current.Count + 1);
            }
        }

        OfficialXReadingLifecycleSummary lifecycle = new(
            reprintCount,
            voidCount,
            cancelCount,
            correctionCount,
            refundCount);

        OfficialXReadingAdjustedSummary adjusted = new(
            activeEntryCount,
            adjustedGross,
            adjustedSubtotal,
            adjustedDiscount,
            adjustedTax,
            adjustedTotal);

        IReadOnlyList<OfficialXReadingVatBucketSummary> bucketSummaries =
            vatBuckets
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new OfficialXReadingVatBucketSummary(
                    pair.Key,
                    pair.Value.Sales,
                    pair.Value.Tax,
                    pair.Value.Count))
                .ToArray();

        return Result<(OfficialXReadingLifecycleSummary, OfficialXReadingAdjustedSummary, IReadOnlyList<OfficialXReadingVatBucketSummary>)>.Success(
            (lifecycle, adjusted, bucketSummaries));
    }

    public static IReadOnlyList<ParsedVatBucketLine> ParseVatBucketLines(string snapshotPayloadJson)
    {
        List<ParsedVatBucketLine> lines = new();

        try
        {
            using JsonDocument document = JsonDocument.Parse(snapshotPayloadJson);
            JsonElement root = document.RootElement;

            if (!root.TryGetProperty("taxBuckets", out JsonElement taxBuckets) ||
                taxBuckets.ValueKind != JsonValueKind.Object ||
                !taxBuckets.TryGetProperty("buckets", out JsonElement buckets) ||
                buckets.ValueKind != JsonValueKind.Array)
            {
                return lines;
            }

            foreach (JsonElement bucket in buckets.EnumerateArray())
            {
                string kind = bucket.GetProperty("kind").GetString() ?? string.Empty;
                long sales = bucket.GetProperty("salesAmountCents").GetInt64();
                long tax = bucket.GetProperty("taxAmountCents").GetInt64();
                lines.Add(new ParsedVatBucketLine(kind, sales, tax));
            }
        }
        catch (JsonException)
        {
            return lines;
        }

        return lines;
    }
}

public sealed record OfficialXReadingLifecycleSnapshotTotals(
    long GrossSalesCents,
    long SubtotalCents,
    long DiscountTotalCents,
    long TaxTotalCents,
    long TotalSalesCents,
    IReadOnlyList<ParsedVatBucketLine> VatBucketLines);

public sealed record ParsedVatBucketLine(
    string Kind,
    long SalesAmountCents,
    long TaxAmountCents);
