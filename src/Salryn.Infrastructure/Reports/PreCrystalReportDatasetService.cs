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

        if (request.RangeEnd <= request.RangeStart)
        {
            return Result<ProductionReportDatasetEnvelope>.Fail(Error.Validation(
                "Reports.InvalidRange",
                "Range end must be after range start."));
        }

        if (!catalogItem.DatasetAvailable)
        {
            return Result<ProductionReportDatasetEnvelope>.Success(
                Seal(BuildUnavailableDataset(request, catalogItem)));
        }

        ElectronicJournalDocumentKind documentKind = ParseDocumentKind(request.DocumentKind);
        OfficialReportingProjectionRequest projectionRequest = new(
            request.RangeStart,
            request.RangeEnd,
            request.BirMachineProfileId,
            string.IsNullOrWhiteSpace(request.RegistrationScopeCode) ? "LOCAL_SINGLE_STORE" : request.RegistrationScopeCode.Trim(),
            documentKind);

        Result<OfficialReportingProjection> projected = await _projection
            .ProjectAsync(projectionRequest, cancellationToken)
            .ConfigureAwait(false);

        if (projected.IsFailure)
        {
            return Result<ProductionReportDatasetEnvelope>.Fail(projected.ErrorDetail);
        }

        ProductionReportDatasetEnvelope dataset = BuildFromProjection(
            request,
            catalogItem,
            projected.Value);

        return Result<ProductionReportDatasetEnvelope>.Success(Seal(dataset));
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
                    ["grossCents"] = doc.GrossTotalCents.ToString(CultureInfo.InvariantCulture),
                    ["refundedCents"] = doc.RefundedTotalCents.ToString(CultureInfo.InvariantCulture),
                    ["netCents"] = doc.NetTotalCents.ToString(CultureInfo.InvariantCulture),
                    ["vatableBaseCents"] = Math.Max(0, doc.VatableBaseCents - doc.RefundedVatableBaseCents).ToString(CultureInfo.InvariantCulture),
                    ["vatAmountCents"] = Math.Max(0, doc.VatAmountCents - doc.RefundedVatAmountCents).ToString(CultureInfo.InvariantCulture),
                    ["vatExemptCents"] = Math.Max(0, doc.VatExemptCents - doc.RefundedVatExemptCents).ToString(CultureInfo.InvariantCulture),
                    ["zeroRatedCents"] = Math.Max(0, doc.ZeroRatedCents - doc.RefundedZeroRatedCents).ToString(CultureInfo.InvariantCulture),
                    ["ssptCents"] = Math.Max(0, doc.SsptCents - doc.RefundedSsptCents).ToString(CultureInfo.InvariantCulture),
                    ["nonVatExemptCents"] = Math.Max(0, doc.NonVatExemptCents - doc.RefundedNonVatExemptCents).ToString(CultureInfo.InvariantCulture),
                    ["isVoided"] = doc.IsVoided ? "true" : "false"
                })));
        }

        return BaseEnvelope(
            request,
            catalogItem,
            projection,
            ControlTotals(projection),
            rows,
            "Internal eSales draft dataset only. Authoritative BIR schema unconfirmed; transmission disabled.");
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
            summary);
    }

    private ProductionReportDatasetEnvelope Seal(ProductionReportDatasetEnvelope envelope)
    {
        ProductionReportDatasetEnvelope blank = envelope with { DatasetSha256 = string.Empty };
        string json = JsonSerializer.Serialize(blank, StableJson);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        return envelope with { DatasetSha256 = hash };
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
        string.IsNullOrWhiteSpace(request.DocumentKind) ? "OFFICIAL_INVOICE" : request.DocumentKind.Trim(),
        request.ActorUserId,
        request.TerminalId);

    private static ElectronicJournalDocumentKind ParseDocumentKind(string? value) =>
        string.Equals(value, "OFFICIAL_RECEIPT", StringComparison.OrdinalIgnoreCase)
            ? ElectronicJournalDocumentKind.OfficialReceipt
            : ElectronicJournalDocumentKind.OfficialInvoice;

    private static IReadOnlyDictionary<string, string> Ordered(Dictionary<string, string> values) =>
        values.OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
}
