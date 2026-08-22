using System.Globalization;
using System.Text;
using System.Text.Json;
using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Core.Services;

public interface IOfficialEngineeringEvidencePackageAssembler
{
    Task<Result<OfficialEngineeringEvidencePackageResult>> AssembleAsync(
        OfficialOutputSourceResult source,
        CancellationToken cancellationToken = default);
}

public sealed class OfficialEngineeringEvidencePackageAssembler : IOfficialEngineeringEvidencePackageAssembler
{
    private const string ArtifactReferencePrefix = "salryn://official-engineering-evidence/";

    private readonly OfficialEngineeringEvidencePackageBuilder _builder;
    private readonly IOfficialXReadingService? _xReadingService;
    private readonly IOfficialZReadingReadService? _zReadingReadService;

    public OfficialEngineeringEvidencePackageAssembler(
        OfficialEngineeringEvidencePackageBuilder? builder = null,
        IOfficialXReadingService? xReadingService = null,
        IOfficialZReadingReadService? zReadingReadService = null)
    {
        _builder = builder ?? new OfficialEngineeringEvidencePackageBuilder();
        _xReadingService = xReadingService;
        _zReadingReadService = zReadingReadService;
    }

    public async Task<Result<OfficialEngineeringEvidencePackageResult>> AssembleAsync(
        OfficialOutputSourceResult source,
        CancellationToken cancellationToken = default)
    {
        string xReadingText = await BuildXReadingTextAsync(source, cancellationToken).ConfigureAwait(false);
        string zReadingText = await BuildZReadingTextAsync(source, cancellationToken).ConfigureAwait(false);

        return _builder.Build(new OfficialEngineeringEvidencePackageRequest(
            BuildMachineProfileSummary(source.MachineProfile),
            BuildOfficialDocumentEvidenceText(source),
            BuildLifecycleEventsText(source.Lifecycle),
            xReadingText,
            zReadingText,
            BuildVatBucketSummaryText(source.Evidence.SnapshotPayloadJson),
            BuildPaymentSummaryText(source.Payments),
            BuildAuditHashSummaryText(source.Evidence, source.Lifecycle)));
    }

    public static string BuildArtifactReference(string packageHash)
    {
        return ArtifactReferencePrefix + packageHash;
    }

    public static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return text.Split('\n', StringSplitOptions.None).Length;
    }

    private async Task<string> BuildXReadingTextAsync(
        OfficialOutputSourceResult source,
        CancellationToken cancellationToken)
    {
        if (_xReadingService is null)
        {
            return "journalEntryId=" + source.Header.JournalEntryId.ToString("D") +
                   " documentNumber=" + source.Header.OfficialDocumentNumber;
        }

        DateTimeOffset transactionTimestamp = source.Header.TransactionTimestamp;
        DateTimeOffset rangeStartedAt = new(
            transactionTimestamp.Year,
            transactionTimestamp.Month,
            transactionTimestamp.Day,
            0,
            0,
            0,
            transactionTimestamp.Offset);
        DateTimeOffset rangeEndedAt = rangeStartedAt.AddDays(1);

        Result<OfficialXReadingResult> xReadingResult = await _xReadingService.ReadAsync(
            new OfficialXReadingRequest(
                rangeStartedAt,
                rangeEndedAt,
                source.Header.BirMachineProfileId,
                source.MachineProfile.RegistrationScopeCode,
                source.Header.DocumentKind),
            cancellationToken).ConfigureAwait(false);

        if (xReadingResult.IsFailure)
        {
            return "unavailable:" + xReadingResult.ErrorDetail?.Code;
        }

        OfficialXReadingResult xReading = xReadingResult.Value;
        StringBuilder builder = new();
        builder.Append("entryCount=").Append(xReading.Summary.EntryCount.ToString(CultureInfo.InvariantCulture));
        builder.Append(" adjustedActiveEntryCount=").Append(xReading.AdjustedSummary.ActiveEntryCount.ToString(CultureInfo.InvariantCulture));
        builder.Append(" reprintCount=").Append(xReading.Lifecycle.ReprintCount.ToString(CultureInfo.InvariantCulture));
        builder.Append(" voidCount=").Append(xReading.Lifecycle.VoidCount.ToString(CultureInfo.InvariantCulture));
        builder.Append(" cancelCount=").Append(xReading.Lifecycle.CancelCount.ToString(CultureInfo.InvariantCulture));
        builder.Append(" correctionCount=").Append(xReading.Lifecycle.CorrectionCount.ToString(CultureInfo.InvariantCulture));
        builder.Append(" refundCount=").Append(xReading.Lifecycle.RefundCount.ToString(CultureInfo.InvariantCulture));
        builder.Append(" evidenceManifestHash=").Append(xReading.Summary.EvidenceManifestHash);
        return builder.ToString();
    }

    private async Task<string> BuildZReadingTextAsync(
        OfficialOutputSourceResult source,
        CancellationToken cancellationToken)
    {
        if (!source.Lifecycle.IsCoveredByFinalizedZReading ||
            !source.Lifecycle.CoveringZReadingId.HasValue)
        {
            return "coveredByFinalizedZ=false";
        }

        Guid zReadingId = source.Lifecycle.CoveringZReadingId.Value;
        StringBuilder builder = new();
        builder.Append("coveredByFinalizedZ=true zReadingId=").Append(zReadingId.ToString("D"));

        if (_zReadingReadService is null)
        {
            return builder.ToString();
        }

        Result<OfficialZReadingPersistedRecord> persistedResult =
            await _zReadingReadService.ReadByIdAsync(zReadingId, cancellationToken).ConfigureAwait(false);

        if (persistedResult.IsFailure)
        {
            builder.Append(" readStatus=unavailable");
            return builder.ToString();
        }

        OfficialZReadingPersistedRecord persisted = persistedResult.Value;

        if (persisted.Enrichment is null)
        {
            builder.Append(" enrichment=base-only");
            return builder.ToString();
        }

        builder.Append(" enrichment=").Append(OfficialZReadingEnrichmentPayload.SchemaV1);
        builder.Append(" voidCount=").Append(persisted.Enrichment.Lifecycle.VoidCount.ToString(CultureInfo.InvariantCulture));
        builder.Append(" refundCount=").Append(persisted.Enrichment.Lifecycle.RefundCount.ToString(CultureInfo.InvariantCulture));
        builder.Append(" adjustedTotalSalesCents=").Append(persisted.Enrichment.AdjustedSummary.TotalSalesCents.ToString(CultureInfo.InvariantCulture));
        builder.Append(" vatBucketCount=").Append(persisted.Enrichment.VatBuckets.Count.ToString(CultureInfo.InvariantCulture));
        return builder.ToString();
    }

    private static string BuildMachineProfileSummary(OfficialOutputMachineProfile profile)
    {
        return string.Join(
            ' ',
            "profileId=" + profile.BirMachineProfileId.ToString("D"),
            "taxpayer=" + profile.RegisteredTaxpayerName,
            "tin=" + profile.TaxpayerIdentificationNumber,
            "branch=" + profile.BranchCode);
    }

    private static string BuildOfficialDocumentEvidenceText(OfficialOutputSourceResult source)
    {
        return string.Join(
            ' ',
            "journalEntryId=" + source.Header.JournalEntryId.ToString("D"),
            "documentNumber=" + source.Header.OfficialDocumentNumber,
            "documentKind=" + source.Header.DocumentKind,
            "snapshotPayloadHash=" + source.Evidence.SnapshotPayloadHash,
            "originalEventId=" + source.Evidence.OriginalEventId.ToString("D"));
    }

    private static string BuildLifecycleEventsText(OfficialOutputLifecycle lifecycle)
    {
        if (lifecycle.Events.Count == 0)
        {
            return "state=" + lifecycle.State + " eventCount=0";
        }

        IEnumerable<string> events = lifecycle.Events
            .OrderBy(static eventRow => eventRow.CreatedAt)
            .ThenBy(static eventRow => eventRow.EventType, StringComparer.Ordinal)
            .Select(static eventRow =>
                eventRow.EventType + "@" + eventRow.CreatedAt.ToString("O", CultureInfo.InvariantCulture));

        return "state=" + lifecycle.State + " events=" + string.Join(';', events);
    }

    private static string BuildVatBucketSummaryText(string snapshotPayloadJson)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(snapshotPayloadJson);
            JsonElement root = document.RootElement;

            if (!root.TryGetProperty("taxBuckets", out JsonElement taxBuckets))
            {
                return "taxBuckets=absent";
            }

            if (!taxBuckets.TryGetProperty("buckets", out JsonElement buckets) ||
                buckets.ValueKind != JsonValueKind.Array)
            {
                return "taxBuckets=present buckets=absent";
            }

            List<string> lines = new();

            foreach (JsonElement bucket in buckets.EnumerateArray())
            {
                string kind = bucket.TryGetProperty("bucketKind", out JsonElement kindElement)
                    ? kindElement.GetString() ?? "unknown"
                    : "unknown";
                long sales = bucket.TryGetProperty("salesAmountCents", out JsonElement salesElement)
                    ? salesElement.GetInt64()
                    : 0;
                long tax = bucket.TryGetProperty("taxAmountCents", out JsonElement taxElement)
                    ? taxElement.GetInt64()
                    : 0;
                lines.Add(kind + ":" + sales + "/" + tax);
            }

            return lines.Count == 0 ? "taxBuckets=empty" : string.Join(';', lines);
        }
        catch (JsonException)
        {
            return "taxBuckets=invalid-json";
        }
    }

    private static string BuildPaymentSummaryText(IReadOnlyList<OfficialOutputPayment> payments)
    {
        if (payments.Count == 0)
        {
            return "paymentCount=0";
        }

        return string.Join(
            ';',
            payments
                .OrderBy(static payment => payment.Method)
                .ThenBy(static payment => payment.PaidAt)
                .Select(static payment =>
                    payment.Method + ":" + payment.AmountCents.ToString(CultureInfo.InvariantCulture)));
    }

    private static string BuildAuditHashSummaryText(
        OfficialOutputEvidence evidence,
        OfficialOutputLifecycle lifecycle)
    {
        return string.Join(
            ' ',
            "snapshotPayloadHash=" + evidence.SnapshotPayloadHash,
            "recomputedSnapshotPayloadHash=" + evidence.RecomputedSnapshotPayloadHash,
            "snapshotHashMatches=" + evidence.SnapshotHashMatches,
            "correctionCount=" + lifecycle.CorrectionCount.ToString(CultureInfo.InvariantCulture),
            "coveredByFinalizedZ=" + lifecycle.IsCoveredByFinalizedZReading);
    }
}
