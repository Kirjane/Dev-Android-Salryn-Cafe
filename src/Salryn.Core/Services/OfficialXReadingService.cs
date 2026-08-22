using System.Text.Json;
using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Core.Services;

public sealed record OfficialXReadingRequest(
    DateTimeOffset RangeStartedAt,
    DateTimeOffset RangeEndedAt,
    Guid? BirMachineProfileId,
    string? RegistrationScopeCode,
    ElectronicJournalDocumentKind DocumentKind);

public sealed record OfficialXReadingSummary(
    DateTimeOffset RangeStartedAt,
    DateTimeOffset RangeEndedAt,
    Guid? BirMachineProfileId,
    string? RegistrationScopeCode,
    ElectronicJournalDocumentKind DocumentKind,
    int EntryCount,
    long GrossSalesCents,
    long SubtotalCents,
    long DiscountTotalCents,
    long TaxTotalCents,
    long NetSalesCents,
    long TotalSalesCents,
    string EvidenceManifestHash,
    bool AllSnapshotHashesMatch);

public sealed record OfficialXReadingDocumentRange(
    string FirstOfficialDocumentNumber,
    string LastOfficialDocumentNumber);

public sealed record OfficialXReadingPaymentSummary(
    string Method,
    long AmountCents,
    int PaymentCount);

public sealed record OfficialXReadingTaxModeSummary(
    string TaxModeCode,
    int EntryCount,
    long SubtotalCents,
    long DiscountTotalCents,
    long TaxTotalCents,
    long TotalSalesCents,
    bool VatBucketEngineDeferred,
    string? Notes);

public sealed record OfficialXReadingResult(
    OfficialXReadingSummary Summary,
    OfficialXReadingDocumentRange? DocumentRange,
    IReadOnlyList<OfficialXReadingPaymentSummary> Payments,
    IReadOnlyList<OfficialXReadingTaxModeSummary> TaxModes,
    OfficialXReadingLifecycleSummary Lifecycle,
    IReadOnlyList<OfficialXReadingVatBucketSummary> VatBuckets,
    OfficialXReadingAdjustedSummary AdjustedSummary);

public interface IOfficialXReadingService
{
    Task<Result<OfficialXReadingResult>> ReadAsync(
        OfficialXReadingRequest request,
        CancellationToken cancellationToken = default);
}

// Kirch: why - X-reading is a read-only summary over official journal evidence; it must not reset, finalize, print, or rebuild from UI text.
public sealed class OfficialXReadingService : IOfficialXReadingService
{
    private readonly IOfficialEvidenceReadService _officialEvidenceRead;
    private readonly IOfficialDocumentLifecycleReadService? _lifecycleRead;

    public OfficialXReadingService(
        IOfficialEvidenceReadService officialEvidenceRead,
        IOfficialDocumentLifecycleReadService? lifecycleRead = null)
    {
        _officialEvidenceRead = officialEvidenceRead ?? throw new ArgumentNullException(nameof(officialEvidenceRead));
        _lifecycleRead = lifecycleRead;
    }

    public async Task<Result<OfficialXReadingResult>> ReadAsync(
        OfficialXReadingRequest request,
        CancellationToken cancellationToken = default)
    {
        Result validation = ValidateRequest(request);

        if (validation.IsFailure)
        {
            return Result<OfficialXReadingResult>.Fail(validation.ErrorDetail);
        }

        OfficialEvidenceReadRequest evidenceRequest = new(
            request.RangeStartedAt,
            request.RangeEndedAt,
            request.BirMachineProfileId,
            request.RegistrationScopeCode,
            request.DocumentKind);

        Result<OfficialEvidenceExportResult> evidenceResult =
            await _officialEvidenceRead.ExportAsync(evidenceRequest, cancellationToken);

        if (evidenceResult.IsFailure)
        {
            return Result<OfficialXReadingResult>.Fail(Error.Failure(
                "OfficialXReading.EvidenceReadFailed",
                evidenceResult.Error));
        }

        OfficialEvidenceExportResult evidence = evidenceResult.Value;

        if (!evidence.Manifest.AllSnapshotHashesMatch ||
            evidence.Entries.Any(entry => !entry.SnapshotHashMatches))
        {
            return Result<OfficialXReadingResult>.Fail(Error.Failure(
                "OfficialXReading.EvidenceHashMismatch",
                "X-reading is blocked because official evidence snapshot hash verification failed."));
        }

        if (evidence.Entries.Count == 0)
        {
            return Result<OfficialXReadingResult>.Success(CreateEmptyResult(request, evidence.Manifest.ManifestHash));
        }

        List<ParsedXReadingSnapshot> snapshots = new();
        List<OfficialEvidenceEntryReadModel> evidenceEntries = new();

        foreach (OfficialEvidenceEntryReadModel entry in evidence.Entries)
        {
            Result<ParsedXReadingSnapshot> parseResult = ParseSnapshot(entry, request.DocumentKind);

            if (parseResult.IsFailure)
            {
                return Result<OfficialXReadingResult>.Fail(parseResult.ErrorDetail);
            }

            snapshots.Add(parseResult.Value);
            evidenceEntries.Add(entry);
        }

        OfficialXReadingResult baseResult = BuildResult(request, evidence.Manifest.ManifestHash, snapshots);

        List<OfficialXReadingLifecycleSnapshotTotals> lifecycleTotals = snapshots
            .Select(snapshot => new OfficialXReadingLifecycleSnapshotTotals(
                snapshot.GrossSalesCents,
                snapshot.SubtotalCents,
                snapshot.DiscountTotalCents,
                snapshot.TaxTotalCents,
                snapshot.TotalSalesCents,
                snapshot.VatBucketLines))
            .ToList();

        Result<(OfficialXReadingLifecycleSummary Lifecycle, OfficialXReadingAdjustedSummary Adjusted, IReadOnlyList<OfficialXReadingVatBucketSummary> VatBuckets)> enrichmentResult =
            await OfficialXReadingEnrichment.BuildAsync(
                evidenceEntries,
                lifecycleTotals,
                _lifecycleRead,
                cancellationToken);

        if (enrichmentResult.IsFailure)
        {
            return Result<OfficialXReadingResult>.Fail(enrichmentResult.ErrorDetail);
        }

        OfficialXReadingResult result = baseResult with
        {
            Lifecycle = enrichmentResult.Value.Lifecycle,
            VatBuckets = enrichmentResult.Value.VatBuckets,
            AdjustedSummary = enrichmentResult.Value.Adjusted
        };

        return Result<OfficialXReadingResult>.Success(result);
    }

    public static Result ValidateRequest(OfficialXReadingRequest? request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialXReading.RequestRequired",
                "Official X-reading request is required."));
        }

        if (request.RangeStartedAt == default ||
            request.RangeEndedAt == default ||
            request.RangeEndedAt <= request.RangeStartedAt)
        {
            return Result.Fail(Error.Validation(
                "OfficialXReading.InvalidDateRange",
                "Official X-reading date range is invalid."));
        }

        if (!request.BirMachineProfileId.HasValue &&
            string.IsNullOrWhiteSpace(request.RegistrationScopeCode))
        {
            return Result.Fail(Error.Validation(
                "OfficialXReading.MachineProfileOrScopeRequired",
                "Official X-reading requires a BIR machine profile id or registration scope."));
        }

        if (request.BirMachineProfileId.HasValue &&
            request.BirMachineProfileId.Value == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialXReading.InvalidMachineProfileId",
                "Official X-reading machine profile id is invalid."));
        }

        if (!Enum.IsDefined(typeof(ElectronicJournalDocumentKind), request.DocumentKind))
        {
            return Result.Fail(Error.Validation(
                "OfficialXReading.UnsupportedDocumentKind",
                "Official X-reading document kind is unsupported."));
        }

        return Result.Success();
    }

    private static OfficialXReadingResult CreateEmptyResult(
        OfficialXReadingRequest request,
        string evidenceManifestHash)
    {
        OfficialXReadingSummary summary = new(
            RangeStartedAt: request.RangeStartedAt,
            RangeEndedAt: request.RangeEndedAt,
            BirMachineProfileId: request.BirMachineProfileId,
            RegistrationScopeCode: NormalizeOptional(request.RegistrationScopeCode),
            DocumentKind: request.DocumentKind,
            EntryCount: 0,
            GrossSalesCents: 0,
            SubtotalCents: 0,
            DiscountTotalCents: 0,
            TaxTotalCents: 0,
            NetSalesCents: 0,
            TotalSalesCents: 0,
            EvidenceManifestHash: evidenceManifestHash,
            AllSnapshotHashesMatch: true);

        return new OfficialXReadingResult(
            summary,
            DocumentRange: null,
            Payments: Array.Empty<OfficialXReadingPaymentSummary>(),
            TaxModes: Array.Empty<OfficialXReadingTaxModeSummary>(),
            Lifecycle: OfficialXReadingEnrichment.EmptyLifecycle,
            VatBuckets: Array.Empty<OfficialXReadingVatBucketSummary>(),
            AdjustedSummary: OfficialXReadingEnrichment.EmptyAdjusted);
    }

    private static OfficialXReadingResult BuildResult(
        OfficialXReadingRequest request,
        string evidenceManifestHash,
        IReadOnlyList<ParsedXReadingSnapshot> snapshots)
    {
        OfficialXReadingSummary summary = new(
            RangeStartedAt: request.RangeStartedAt,
            RangeEndedAt: request.RangeEndedAt,
            BirMachineProfileId: request.BirMachineProfileId,
            RegistrationScopeCode: NormalizeOptional(request.RegistrationScopeCode),
            DocumentKind: request.DocumentKind,
            EntryCount: snapshots.Count,
            GrossSalesCents: Sum(snapshots, snapshot => snapshot.GrossSalesCents),
            SubtotalCents: Sum(snapshots, snapshot => snapshot.SubtotalCents),
            DiscountTotalCents: Sum(snapshots, snapshot => snapshot.DiscountTotalCents),
            TaxTotalCents: Sum(snapshots, snapshot => snapshot.TaxTotalCents),
            NetSalesCents: Sum(snapshots, snapshot => snapshot.NetSalesCents),
            TotalSalesCents: Sum(snapshots, snapshot => snapshot.TotalSalesCents),
            EvidenceManifestHash: evidenceManifestHash,
            AllSnapshotHashesMatch: true);

        OfficialXReadingDocumentRange documentRange = new(
            snapshots[0].OfficialDocumentNumber,
            snapshots[^1].OfficialDocumentNumber);

        IReadOnlyList<OfficialXReadingPaymentSummary> payments =
            snapshots
                .SelectMany(snapshot => snapshot.Payments)
                .GroupBy(payment => payment.Method, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new OfficialXReadingPaymentSummary(
                    group.Key,
                    Sum(group, payment => payment.AmountCents),
                    group.Count()))
                .ToArray();

        IReadOnlyList<OfficialXReadingTaxModeSummary> taxModes =
            snapshots
                .GroupBy(snapshot => snapshot.TaxModeCode, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new OfficialXReadingTaxModeSummary(
                    TaxModeCode: group.Key,
                    EntryCount: group.Count(),
                    SubtotalCents: Sum(group, snapshot => snapshot.SubtotalCents),
                    DiscountTotalCents: Sum(group, snapshot => snapshot.DiscountTotalCents),
                    TaxTotalCents: Sum(group, snapshot => snapshot.TaxTotalCents),
                    TotalSalesCents: Sum(group, snapshot => snapshot.TotalSalesCents),
                    VatBucketEngineDeferred: group.Any(snapshot => snapshot.VatBucketEngineDeferred),
                    Notes: JoinDistinctNotes(group.Select(snapshot => snapshot.TaxModeNotes))))
                .ToArray();

        return new OfficialXReadingResult(
            summary,
            documentRange,
            payments,
            taxModes,
            OfficialXReadingEnrichment.EmptyLifecycle,
            Array.Empty<OfficialXReadingVatBucketSummary>(),
            OfficialXReadingEnrichment.EmptyAdjusted);
    }

    private static Result<ParsedXReadingSnapshot> ParseSnapshot(
        OfficialEvidenceEntryReadModel entry,
        ElectronicJournalDocumentKind expectedDocumentKind)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(entry.SnapshotPayloadJson);
            JsonElement root = document.RootElement;

            RequireString(root, "schema", out string schema);

            if (!string.Equals(schema, "SALRYN_OFFICIAL_DOCUMENT_SNAPSHOT_V1", StringComparison.Ordinal))
            {
                return SnapshotInvalid("Official X-reading only supports SALRYN_OFFICIAL_DOCUMENT_SNAPSHOT_V1 evidence.");
            }

            RequireString(root, "documentKind", out string snapshotDocumentKind);

            if (!string.Equals(snapshotDocumentKind, expectedDocumentKind.ToString(), StringComparison.Ordinal))
            {
                return SnapshotInvalid("Official X-reading evidence document kind does not match the request.");
            }

            JsonElement header = RequireObject(root, "header");
            string documentNumber = RequireNonBlankString(header, "documentNumber");

            if (!string.Equals(documentNumber, entry.OfficialDocumentNumber, StringComparison.Ordinal))
            {
                return SnapshotInvalid("Official X-reading evidence document number does not match the journal entry.");
            }

            long lineDiscountCents = 0;
            long lineGrossCents = 0;
            JsonElement lines = RequireArray(root, "lines");

            foreach (JsonElement line in lines.EnumerateArray())
            {
                long lineTotal = RequireNonNegativeInt64(line, "lineTotalCents");
                long discount = RequireNonNegativeInt64(line, "discountCents");
                lineDiscountCents = checked(lineDiscountCents + discount);
                lineGrossCents = checked(lineGrossCents + lineTotal + discount);
            }

            JsonElement amounts = RequireObject(root, "amounts");
            long subtotalCents = RequireNonNegativeInt64(amounts, "subtotalCents");
            long saleDiscountCents = RequireNonNegativeInt64(amounts, "discountCents");
            long taxCents = RequireNonNegativeInt64(amounts, "taxCents");
            long totalCents = RequireNonNegativeInt64(amounts, "totalCents");
            long discountTotalCents = checked(lineDiscountCents + saleDiscountCents);
            long grossSalesCents = checked(lineGrossCents + saleDiscountCents);
            long netSalesCents = checked(totalCents - taxCents);

            if (netSalesCents < 0)
            {
                return SnapshotInvalid("Official X-reading evidence total cannot be less than tax.");
            }

            JsonElement paymentsElement = RequireArray(root, "payments");
            List<ParsedXReadingPayment> payments = new();

            foreach (JsonElement payment in paymentsElement.EnumerateArray())
            {
                payments.Add(new ParsedXReadingPayment(
                    Method: RequireNonBlankString(payment, "method"),
                    AmountCents: RequirePositiveInt64(payment, "amountCents")));
            }

            JsonElement taxMode = RequireObject(root, "taxMode");
            string taxModeCode = RequireNonBlankString(taxMode, "taxModeCode");
            bool vatBucketEngineDeferred = RequireBoolean(taxMode, "vatBucketEngineDeferred");
            string? taxModeNotes = ReadOptionalString(taxMode, "notes");
            IReadOnlyList<ParsedVatBucketLine> vatBucketLines =
                OfficialXReadingEnrichment.ParseVatBucketLines(entry.SnapshotPayloadJson);

            return Result<ParsedXReadingSnapshot>.Success(new ParsedXReadingSnapshot(
                OfficialDocumentNumber: documentNumber,
                GrossSalesCents: grossSalesCents,
                SubtotalCents: subtotalCents,
                DiscountTotalCents: discountTotalCents,
                TaxTotalCents: taxCents,
                NetSalesCents: netSalesCents,
                TotalSalesCents: totalCents,
                Payments: payments,
                TaxModeCode: taxModeCode,
                VatBucketEngineDeferred: vatBucketEngineDeferred,
                TaxModeNotes: taxModeNotes,
                VatBucketLines: vatBucketLines));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or OverflowException)
        {
            return SnapshotInvalid("Official X-reading snapshot evidence is malformed.");
        }
    }

    private static Result<ParsedXReadingSnapshot> SnapshotInvalid(string message)
    {
        return Result<ParsedXReadingSnapshot>.Fail(Error.Failure(
            "OfficialXReading.SnapshotEvidenceInvalid",
            message));
    }

    private static void RequireString(JsonElement element, string propertyName, out string value)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String)
        {
            throw new KeyNotFoundException(propertyName);
        }

        value = property.GetString() ?? string.Empty;
    }

    private static string RequireNonBlankString(JsonElement element, string propertyName)
    {
        RequireString(element, propertyName, out string value);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(propertyName);
        }

        return value;
    }

    private static string? ReadOptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException(propertyName);
        }

        string? value = property.GetString();

        return string.IsNullOrWhiteSpace(value)
            ? null
            : value;
    }

    private static JsonElement RequireObject(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.Object)
        {
            throw new KeyNotFoundException(propertyName);
        }

        return property;
    }

    private static JsonElement RequireArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            throw new KeyNotFoundException(propertyName);
        }

        return property;
    }

    private static long RequireNonNegativeInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            !property.TryGetInt64(out long value) ||
            value < 0)
        {
            throw new InvalidOperationException(propertyName);
        }

        return value;
    }

    private static long RequirePositiveInt64(JsonElement element, string propertyName)
    {
        long value = RequireNonNegativeInt64(element, propertyName);

        if (value <= 0)
        {
            throw new InvalidOperationException(propertyName);
        }

        return value;
    }

    private static bool RequireBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            throw new InvalidOperationException(propertyName);
        }

        return property.GetBoolean();
    }

    private static long Sum<T>(IEnumerable<T> values, Func<T, long> selector)
    {
        long sum = 0;

        foreach (T value in values)
        {
            sum = checked(sum + selector(value));
        }

        return sum;
    }

    private static string? JoinDistinctNotes(IEnumerable<string?> notes)
    {
        string[] distinctNotes =
            notes
                .Where(note => !string.IsNullOrWhiteSpace(note))
                .Select(note => note!.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(note => note, StringComparer.Ordinal)
                .ToArray();

        return distinctNotes.Length == 0
            ? null
            : string.Join("; ", distinctNotes);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private sealed record ParsedXReadingSnapshot(
        string OfficialDocumentNumber,
        long GrossSalesCents,
        long SubtotalCents,
        long DiscountTotalCents,
        long TaxTotalCents,
        long NetSalesCents,
        long TotalSalesCents,
        IReadOnlyList<ParsedXReadingPayment> Payments,
        string TaxModeCode,
        bool VatBucketEngineDeferred,
        string? TaxModeNotes,
        IReadOnlyList<ParsedVatBucketLine> VatBucketLines);

    private sealed record ParsedXReadingPayment(
        string Method,
        long AmountCents);
}
