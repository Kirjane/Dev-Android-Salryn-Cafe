using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services.Reporting;
using Salryn.Core.Services.Reports;

namespace Salryn.Infrastructure.Reports;

public sealed class PreCrystalReportDatasetService
{
    private const string SchemaCode = "SALRYN_PRECRYSTAL_REPORT_DATASET_V1";

    private static readonly JsonSerializerOptions StableJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IOfficialReportingProjectionService _projection;
    private readonly Func<DateTimeOffset> _clock;

    public PreCrystalReportDatasetService(
        IOfficialReportingProjectionService projection,
        Func<DateTimeOffset>? clock = null)
    {
        _projection = projection ?? throw new ArgumentNullException(nameof(projection));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public IReadOnlyList<ProductionReportCatalogItem> GetCatalog() => PreCrystalReportCatalog.All;

    public async Task<Result<ProductionReportDatasetEnvelope>> BuildDatasetAsync(
        ProductionReportDatasetRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!PreCrystalReportCatalog.TryGet(request.ReportKind, out ProductionReportCatalogItem catalogItem))
        {
            return Result<ProductionReportDatasetEnvelope>.Fail(Error.Validation(
                "Reports.UnknownReportKind",
                "Report kind is not allow-listed."));
        }

        if (ValidateRange(request) is Error rangeError)
        {
            return Result<ProductionReportDatasetEnvelope>.Fail(rangeError);
        }

        if (!TryNormalizeDocumentKind(
            request.DocumentKind,
            out string normalizedDocumentKind,
            out ElectronicJournalDocumentKind documentKind,
            out Error? documentKindError))
        {
            return Result<ProductionReportDatasetEnvelope>.Fail(documentKindError!);
        }

        ProductionReportDatasetRequest normalizedRequest = request with
        {
            ReportKind = catalogItem.Kind,
            RegistrationScopeCode = string.IsNullOrWhiteSpace(request.RegistrationScopeCode)
                ? "LOCAL_SINGLE_STORE"
                : request.RegistrationScopeCode.Trim(),
            DocumentKind = normalizedDocumentKind,
            TerminalId = string.IsNullOrWhiteSpace(request.TerminalId) ? null : request.TerminalId.Trim()
        };

        if (!catalogItem.DatasetAvailable)
        {
            return Result<ProductionReportDatasetEnvelope>.Success(
                Seal(BuildUnavailableDataset(normalizedRequest, catalogItem)));
        }

        OfficialReportingProjectionRequest projectionRequest = new(
            normalizedRequest.RangeStart,
            normalizedRequest.RangeEnd,
            normalizedRequest.BirMachineProfileId,
            normalizedRequest.RegistrationScopeCode,
            documentKind);

        Result<OfficialReportingProjection> projected = await _projection
            .ProjectAsync(projectionRequest, cancellationToken)
            .ConfigureAwait(false);

        if (projected.IsFailure)
        {
            return Result<ProductionReportDatasetEnvelope>.Fail(projected.ErrorDetail);
        }

        if (ValidateProjectionSize(catalogItem.Kind, projected.Value) is Error rowLimitError)
        {
            return Result<ProductionReportDatasetEnvelope>.Fail(rowLimitError);
        }

        ProductionReportDatasetEnvelope dataset = BuildFromProjection(
            normalizedRequest,
            catalogItem,
            projected.Value);

        return Result<ProductionReportDatasetEnvelope>.Success(Seal(dataset));
    }

    private static Error? ValidateRange(ProductionReportDatasetRequest request)
    {
        if (request.RangeEnd <= request.RangeStart)
        {
            return Error.Validation(
                "Reports.InvalidRange",
                "Range end must be after range start.");
        }

        if (request.RangeEnd - request.RangeStart > ProductionReportDatasetLimits.MaxRange)
        {
            return Error.Validation(
                "Reports.RangeTooLarge",
                $"Report range exceeds the PRE-CRYSTAL cap of {ProductionReportDatasetLimits.MaxRangeDays} days.");
        }

        return null;
    }

    private static Error? ValidateProjectionSize(string reportKind, OfficialReportingProjection projection)
    {
        int rowCount = reportKind switch
        {
            ProductionReportKinds.EjExport => checked(projection.Documents.Count + projection.Events.Count),
            ProductionReportKinds.SalesRefundVoidSummary => 1,
            ProductionReportKinds.TaxBucketSummary => 1,
            _ => projection.Documents.Count
        };

        if (rowCount > ProductionReportDatasetLimits.MaxRows)
        {
            return Error.Validation(
                "Reports.RowLimitExceeded",
                $"Report dataset would contain {rowCount} rows, exceeding the PRE-CRYSTAL cap of {ProductionReportDatasetLimits.MaxRows} rows.");
        }

        return null;
    }

    private ProductionReportDatasetEnvelope BuildFromProjection(
        ProductionReportDatasetRequest request,
        ProductionReportCatalogItem catalogItem,
        OfficialReportingProjection projection)
    {
        return catalogItem.Kind switch
        {
            ProductionReportKinds.TraditionalXReading => OfficialDataset(
                request,
                catalogItem,
                projection,
                "X-reading preview dataset. Read-only; no sequence mutation."),
            ProductionReportKinds.TraditionalZReading => OfficialDataset(
                request,
                catalogItem,
                projection,
                "Z-reading dataset preview. This does not finalize or persist a Z reading."),
            ProductionReportKinds.EjExport => EjDataset(request, catalogItem, projection),
            ProductionReportKinds.InternalESalesDraft => ESalesDataset(request, catalogItem, projection),
            ProductionReportKinds.SalesRefundVoidSummary => SalesRefundVoidSummary(request, catalogItem, projection),
            ProductionReportKinds.TaxBucketSummary => TaxBucketSummary(request, catalogItem, projection),
            _ => BuildUnavailableDataset(request, catalogItem)
        };
    }

    private ProductionReportDatasetEnvelope OfficialDataset(
        ProductionReportDatasetRequest request,
        ProductionReportCatalogItem catalogItem,
        OfficialReportingProjection projection,
        string summary)
    {
        List<ProductionReportRow> rows = new();
        int sort = 0;
        foreach (OfficialReportingDocumentRow doc in projection.Documents)
        {
            rows.Add(new ProductionReportRow(
                "official-document",
                ++sort,
                Ordered(new Dictionary<string, string>
                {
                    ["officialDocumentNumber"] = doc.OfficialDocumentNumber,
                    ["postedAtUtc"] = doc.PostedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    ["lifecycleState"] = doc.LifecycleState,
                    ["grossTotalCents"] = doc.GrossTotalCents.ToString(CultureInfo.InvariantCulture),
                    ["refundedTotalCents"] = doc.RefundedTotalCents.ToString(CultureInfo.InvariantCulture),
                    ["netTotalCents"] = doc.NetTotalCents.ToString(CultureInfo.InvariantCulture),
                    ["isVoided"] = doc.IsVoided ? "true" : "false",
                    ["snapshotPayloadHash"] = doc.SnapshotPayloadHash
                })));
        }

        return BaseEnvelope(request, catalogItem, projection, ControlTotals(projection), rows, summary);
    }

    private ProductionReportDatasetEnvelope EjDataset(
        ProductionReportDatasetRequest request,
        ProductionReportCatalogItem catalogItem,
        OfficialReportingProjection projection)
    {
        List<ProductionReportRow> rows = new();
        int sort = 0;
        foreach (OfficialReportingDocumentRow doc in projection.Documents)
        {
            rows.Add(new ProductionReportRow(
                "ej-document",
                ++sort,
                Ordered(new Dictionary<string, string>
                {
                    ["kind"] = "ORIGINAL",
                    ["officialDocumentNumber"] = doc.OfficialDocumentNumber,
                    ["postedAtUtc"] = doc.PostedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    ["lifecycleState"] = doc.LifecycleState,
                    ["netTotalCents"] = doc.NetTotalCents.ToString(CultureInfo.InvariantCulture),
                    ["snapshotPayloadHash"] = doc.SnapshotPayloadHash
                })));
        }

        foreach (OfficialReportingEventRow ev in projection.Events)
        {
            rows.Add(new ProductionReportRow(
                "ej-event",
                ++sort,
                Ordered(new Dictionary<string, string>
                {
                    ["eventType"] = ev.EventType,
                    ["officialDocumentNumber"] = ev.OfficialDocumentNumber,
                    ["createdAtUtc"] = ev.CreatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    ["reason"] = ev.Reason ?? string.Empty,
                    ["actorUserId"] = ev.ActorUserId?.ToString("D") ?? string.Empty
                })));
        }

        return BaseEnvelope(
            request,
            catalogItem,
            projection,
            ControlTotals(projection),
            rows,
            "EJ examiner-readable dataset from canonical projection. Does not submit to BIR.");
    }

    private ProductionReportDatasetEnvelope ESalesDataset(
        ProductionReportDatasetRequest request,
        ProductionReportCatalogItem catalogItem,
        OfficialReportingProjection projection)
    {
        List<ProductionReportRow> rows = new();
        int sort = 0;
        foreach (OfficialReportingDocumentRow doc in projection.Documents)
        {
            rows.Add(new ProductionReportRow(
                "internal-esales-row",
                ++sort,
                Ordered(new Dictionary<string, string>
                {
                    ["officialDocumentNumber"] = doc.OfficialDocumentNumber,
                    ["postedAtUtc"] = doc.PostedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    ["lifecycleState"] = doc.LifecycleState,
                    ["grossCents"] = doc.GrossTotalCents.ToString(CultureInfo.InvariantCulture),
                    ["refundedCents"] = doc.RefundedTotalCents.ToString(CultureInfo.InvariantCulture),
                    ["presentationOnlyNetCents"] = doc.NetTotalCents.ToString(CultureInfo.InvariantCulture),
                    ["snapshotVatableBaseCents"] = doc.VatableBaseCents.ToString(CultureInfo.InvariantCulture),
                    ["snapshotVatAmountCents"] = doc.VatAmountCents.ToString(CultureInfo.InvariantCulture),
                    ["snapshotVatExemptCents"] = doc.VatExemptCents.ToString(CultureInfo.InvariantCulture),
                    ["snapshotZeroRatedCents"] = doc.ZeroRatedCents.ToString(CultureInfo.InvariantCulture),
                    ["snapshotSsptCents"] = doc.SsptCents.ToString(CultureInfo.InvariantCulture),
                    ["snapshotNonVatExemptCents"] = doc.NonVatExemptCents.ToString(CultureInfo.InvariantCulture),
                    ["isVoided"] = doc.IsVoided ? "true" : "false",
                    ["presentationBoundary"] = "Per-document rows are presentation-only. Official totals come from M5 canonical projection control totals."
                })));
        }

        return BaseEnvelope(
            request,
            catalogItem,
            projection,
            ControlTotals(projection),
            rows,
            "Internal eSales draft dataset only. Authoritative BIR schema unconfirmed; transmission disabled. Official totals are projection control totals.");
    }

    private ProductionReportDatasetEnvelope SalesRefundVoidSummary(
        ProductionReportDatasetRequest request,
        ProductionReportCatalogItem catalogItem,
        OfficialReportingProjection projection)
    {
        ProductionReportRow row = new(
            "sales-refund-void-summary",
            1,
            Ordered(new Dictionary<string, string>
            {
                ["documentCount"] = projection.DocumentCount.ToString(CultureInfo.InvariantCulture),
                ["voidedDocumentCount"] = projection.VoidedDocumentCount.ToString(CultureInfo.InvariantCulture),
                ["refundEventCount"] = projection.RefundEventCount.ToString(CultureInfo.InvariantCulture),
                ["reprintEventCount"] = projection.ReprintEventCount.ToString(CultureInfo.InvariantCulture),
                ["grossPostedSalesCents"] = projection.GrossPostedSalesCents.ToString(CultureInfo.InvariantCulture),
                ["voidedSalesCents"] = projection.VoidedSalesCents.ToString(CultureInfo.InvariantCulture),
                ["refundedValueCents"] = projection.RefundedValueCents.ToString(CultureInfo.InvariantCulture),
                ["netSalesCents"] = projection.NetSalesCents.ToString(CultureInfo.InvariantCulture)
            }));

        return BaseEnvelope(
            request,
            catalogItem,
            projection,
            ControlTotals(projection),
            new[] { row },
            "Sales/refund/void summary using M5 lifecycle-aware projection totals.");
    }

    private ProductionReportDatasetEnvelope TaxBucketSummary(
        ProductionReportDatasetRequest request,
        ProductionReportCatalogItem catalogItem,
        OfficialReportingProjection projection)
    {
        ProductionReportRow row = new(
            "tax-bucket-summary",
            1,
            Ordered(new Dictionary<string, string>
            {
                ["vatableSalesCents"] = projection.VatableSalesCents.ToString(CultureInfo.InvariantCulture),
                ["vatAmountCents"] = projection.VatAmountCents.ToString(CultureInfo.InvariantCulture),
                ["vatExemptSalesCents"] = projection.VatExemptSalesCents.ToString(CultureInfo.InvariantCulture),
                ["zeroRatedSalesCents"] = projection.ZeroRatedSalesCents.ToString(CultureInfo.InvariantCulture),
                ["ssptSalesCents"] = projection.SsptSalesCents.ToString(CultureInfo.InvariantCulture),
                ["nonVatExemptSalesCents"] = projection.NonVatExemptSalesCents.ToString(CultureInfo.InvariantCulture)
            }));

        return BaseEnvelope(
            request,
            catalogItem,
            projection,
            TaxControlTotals(projection),
            new[] { row },
            "Tax bucket summary using M5 canonical projection totals, not report-template formulas.");
    }

    private ProductionReportDatasetEnvelope BuildUnavailableDataset(
        ProductionReportDatasetRequest request,
        ProductionReportCatalogItem catalogItem)
    {
        ProductionReportScope scope = Scope(request);
        ProductionReportSafetyFlags flags = new(
            DatasetAvailable: false,
            UsesCanonicalM5Projection: catalogItem.UsesCanonicalM5Projection,
            ProductionActivationEnabled: false,
            BirESalesSchemaConfirmed: false,
            ESalesTransmissionEnabled: false,
            BirAccreditationClaimed: false,
            CrystalRuntimeAvailable: false,
            RendererCanProducePdf: false,
            BoundaryNote: catalogItem.UnavailableReason ?? "Dataset unavailable in PRE-CRYSTAL M0.");

        return new ProductionReportDatasetEnvelope(
            SchemaCode,
            1,
            catalogItem.Kind,
            catalogItem.Title,
            _clock(),
            scope,
            Array.Empty<ProductionReportControlTotal>(),
            new[]
            {
                new ProductionReportRow(
                    "unavailable",
                    1,
                    Ordered(new Dictionary<string, string>
                    {
                        ["reason"] = flags.BoundaryNote
                    }))
            },
            Array.Empty<ProductionReportSourceHash>(),
            flags,
            string.Empty,
            string.Empty,
            flags.BoundaryNote);
    }

    private ProductionReportDatasetEnvelope BaseEnvelope(
        ProductionReportDatasetRequest request,
        ProductionReportCatalogItem catalogItem,
        OfficialReportingProjection projection,
        IReadOnlyList<ProductionReportControlTotal> totals,
        IReadOnlyList<ProductionReportRow> rows,
        string summary)
    {
        ProductionReportSafetyFlags flags = new(
            DatasetAvailable: true,
            UsesCanonicalM5Projection: catalogItem.UsesCanonicalM5Projection,
            ProductionActivationEnabled: projection.ProductionActivationEnabled,
            BirESalesSchemaConfirmed: projection.BirAuthoritativeESalesSchemaConfirmed,
            ESalesTransmissionEnabled: projection.TransmissionEnabled,
            BirAccreditationClaimed: false,
            CrystalRuntimeAvailable: false,
            RendererCanProducePdf: false,
            BoundaryNote: "PRE-CRYSTAL dataset only. Crystal runtime unavailable; PDF/rendering not claimed.");

        return new ProductionReportDatasetEnvelope(
            SchemaCode,
            1,
            catalogItem.Kind,
            catalogItem.Title,
            _clock(),
            Scope(request),
            totals,
            rows,
            new[]
            {
                new ProductionReportSourceHash(
                    "m5-canonical-projection",
                    projection.ProjectionContentSha256,
                    "M5-RC2 lifecycle-aware official reporting projection")
            },
            flags,
            string.Empty,
            string.Empty,
            summary);
    }

    private ProductionReportDatasetEnvelope Seal(ProductionReportDatasetEnvelope envelope)
    {
        var contentPayload = new
        {
            envelope.SchemaCode,
            envelope.SchemaVersion,
            envelope.ReportKind,
            envelope.ReportTitle,
            envelope.Scope,
            envelope.ControlTotals,
            envelope.Rows,
            envelope.SourceHashes,
            envelope.SafetyFlags,
            envelope.Summary
        };
        string contentHash = Sha256(JsonSerializer.Serialize(contentPayload, StableJson));
        ProductionReportDatasetEnvelope withContentHash = envelope with
        {
            ContentSha256 = contentHash,
            DatasetSha256 = string.Empty
        };
        string envelopeHash = Sha256(JsonSerializer.Serialize(withContentHash, StableJson));
        return withContentHash with { DatasetSha256 = envelopeHash };
    }

    private static IReadOnlyList<ProductionReportControlTotal> ControlTotals(OfficialReportingProjection p) => new[]
    {
        new ProductionReportControlTotal("gross-posted-sales", "Gross posted sales", p.GrossPostedSalesCents, p.DocumentCount),
        new ProductionReportControlTotal("voided-sales", "Voided sales", p.VoidedSalesCents, p.VoidedDocumentCount),
        new ProductionReportControlTotal("refunded-value", "Refunded value", p.RefundedValueCents, p.RefundEventCount),
        new ProductionReportControlTotal("net-sales", "Net sales", p.NetSalesCents, p.DocumentCount),
        new ProductionReportControlTotal("vat-amount", "VAT amount", p.VatAmountCents, p.DocumentCount)
    };

    private static IReadOnlyList<ProductionReportControlTotal> TaxControlTotals(OfficialReportingProjection p) => new[]
    {
        new ProductionReportControlTotal("vatable-sales", "Vatable sales", p.VatableSalesCents, p.DocumentCount),
        new ProductionReportControlTotal("vat-amount", "VAT amount", p.VatAmountCents, p.DocumentCount),
        new ProductionReportControlTotal("vat-exempt-sales", "VAT-exempt sales", p.VatExemptSalesCents, p.DocumentCount),
        new ProductionReportControlTotal("zero-rated-sales", "Zero-rated sales", p.ZeroRatedSalesCents, p.DocumentCount),
        new ProductionReportControlTotal("sspt-sales", "SSPT sales", p.SsptSalesCents, p.DocumentCount),
        new ProductionReportControlTotal("non-vat-exempt-sales", "Non-VAT exempt sales", p.NonVatExemptSalesCents, p.DocumentCount)
    };

    private static ProductionReportScope Scope(ProductionReportDatasetRequest request) => new(
        request.RangeStart,
        request.RangeEnd,
        request.BirMachineProfileId,
        string.IsNullOrWhiteSpace(request.RegistrationScopeCode) ? "LOCAL_SINGLE_STORE" : request.RegistrationScopeCode.Trim(),
        string.IsNullOrWhiteSpace(request.DocumentKind) ? "OFFICIAL_INVOICE" : request.DocumentKind.Trim().ToUpperInvariant(),
        request.ActorUserId,
        request.TerminalId);

    private static bool TryNormalizeDocumentKind(
        string? value,
        out string normalized,
        out ElectronicJournalDocumentKind kind,
        out Error? error)
    {
        normalized = "OFFICIAL_INVOICE";
        kind = ElectronicJournalDocumentKind.OfficialInvoice;
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        string candidate = value.Trim().ToUpperInvariant();
        if (string.Equals(candidate, "OFFICIAL_INVOICE", StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(candidate, "OFFICIAL_RECEIPT", StringComparison.Ordinal))
        {
            normalized = "OFFICIAL_RECEIPT";
            kind = ElectronicJournalDocumentKind.OfficialReceipt;
            return true;
        }

        error = Error.Validation(
            "Reports.InvalidDocumentKind",
            "DocumentKind must be OFFICIAL_INVOICE or OFFICIAL_RECEIPT.");
        return false;
    }

    private static string Sha256(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static IReadOnlyDictionary<string, string> Ordered(Dictionary<string, string> values) =>
        values.OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
}
