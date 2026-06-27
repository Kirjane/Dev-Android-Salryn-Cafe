using Salryn.Core.Common;

namespace Salryn.Core.Services;

public sealed record OfficialOutputTextFileTrustedPackageProducerRequest(
    OfficialOutputSourceReadRequest? SourceReadRequest,
    DateTimeOffset? RequestedAt = null,
    Guid? RequestedByUserId = null,
    string? TerminalId = null,
    IReadOnlyDictionary<string, string>? Metadata = null,
    OfficialPrintableDocumentCopyMode CopyMode = OfficialPrintableDocumentCopyMode.Original);

public interface IOfficialOutputTextFileTrustedPackageProducer
{
    Task<Result<OfficialOutputExportPackage>> ProduceAsync(
        OfficialOutputTextFileTrustedPackageProducerRequest? request,
        CancellationToken cancellationToken = default);
}

// Kirch: why - text-file workflow UI needs a trusted package produced from official evidence, never from UI screen state.
public sealed class OfficialOutputTextFileTrustedPackageProducer : IOfficialOutputTextFileTrustedPackageProducer
{
    private readonly IOfficialOutputSourceReadService _sourceReadService;
    private readonly OfficialPrintableDocumentBuilder _printableDocumentBuilder;
    private readonly OfficialReportBindingBuilder _reportBindingBuilder;
    private readonly OfficialTextPreviewReportRenderer _textPreviewRenderer;
    private readonly OfficialOutputArtifactManifestBuilder _manifestBuilder;
    private readonly OfficialOutputExportPackageBuilder _exportPackageBuilder;

    public OfficialOutputTextFileTrustedPackageProducer(
        IOfficialOutputSourceReadService sourceReadService,
        OfficialPrintableDocumentBuilder? printableDocumentBuilder = null,
        OfficialReportBindingBuilder? reportBindingBuilder = null,
        OfficialTextPreviewReportRenderer? textPreviewRenderer = null,
        OfficialOutputArtifactManifestBuilder? manifestBuilder = null,
        OfficialOutputExportPackageBuilder? exportPackageBuilder = null)
    {
        _sourceReadService = sourceReadService ?? throw new ArgumentNullException(nameof(sourceReadService));
        _printableDocumentBuilder = printableDocumentBuilder ?? new OfficialPrintableDocumentBuilder();
        _reportBindingBuilder = reportBindingBuilder ?? new OfficialReportBindingBuilder();
        _textPreviewRenderer = textPreviewRenderer ?? new OfficialTextPreviewReportRenderer();
        _manifestBuilder = manifestBuilder ?? new OfficialOutputArtifactManifestBuilder();
        _exportPackageBuilder = exportPackageBuilder ?? new OfficialOutputExportPackageBuilder();
    }

    public async Task<Result<OfficialOutputExportPackage>> ProduceAsync(
        OfficialOutputTextFileTrustedPackageProducerRequest? request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result validation = ValidateRequest(request);

        if (validation.IsFailure)
        {
            return Result<OfficialOutputExportPackage>.Fail(validation.ErrorDetail);
        }

        OfficialOutputTextFileTrustedPackageProducerRequest normalizedRequest =
            NormalizeRequest(request!);

        Result<OfficialOutputSourceResult> sourceResult =
            await _sourceReadService.ReadAsync(normalizedRequest.SourceReadRequest!, cancellationToken)
                .ConfigureAwait(false);

        if (sourceResult.IsFailure)
        {
            return Result<OfficialOutputExportPackage>.Fail(Error.Failure(
                "OfficialOutputTextFileTrustedPackageProducer.SourceUnavailable",
                sourceResult.Error));
        }

        return await BuildPackageAsync(
                sourceResult.Value,
                normalizedRequest,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public static Result ValidateRequest(
        OfficialOutputTextFileTrustedPackageProducerRequest? request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputTextFileTrustedPackageProducer.RequestRequired",
                "Official output text-file trusted package producer request is required."));
        }

        Result sourceRequestValidation =
            OfficialOutputSourceReadService.ValidateRequest(request.SourceReadRequest);

        if (sourceRequestValidation.IsFailure)
        {
            return sourceRequestValidation;
        }

        if (!Enum.IsDefined(typeof(OfficialPrintableDocumentCopyMode), request.CopyMode))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputTextFileTrustedPackageProducer.UnsupportedCopyMode",
                "Official output text-file trusted package producer copy mode is unsupported."));
        }

        if (request.RequestedByUserId.HasValue &&
            request.RequestedByUserId.Value == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputTextFileTrustedPackageProducer.InvalidRequestedByUserId",
                "Official output text-file trusted package producer requester user id is invalid."));
        }

        return ValidateMetadata(request.Metadata);
    }

    public static OfficialOutputTextFileTrustedPackageProducerRequest NormalizeRequest(
        OfficialOutputTextFileTrustedPackageProducerRequest request)
    {
        return request with
        {
            SourceReadRequest = OfficialOutputSourceReadService.NormalizeRequest(request.SourceReadRequest!),
            TerminalId = NormalizeOptional(request.TerminalId),
            Metadata = NormalizeMetadata(request.Metadata)
        };
    }

    private async Task<Result<OfficialOutputExportPackage>> BuildPackageAsync(
        OfficialOutputSourceResult source,
        OfficialOutputTextFileTrustedPackageProducerRequest request,
        CancellationToken cancellationToken)
    {
        Result<OfficialPrintableDocument> printableResult =
            _printableDocumentBuilder.Build(source, request.CopyMode);

        if (printableResult.IsFailure)
        {
            return Result<OfficialOutputExportPackage>.Fail(printableResult.ErrorDetail);
        }

        Result<OfficialReportBindingDocument> bindingResult =
            _reportBindingBuilder.Build(printableResult.Value);

        if (bindingResult.IsFailure)
        {
            return Result<OfficialOutputExportPackage>.Fail(bindingResult.ErrorDetail);
        }

        DateTimeOffset requestedAt = request.RequestedAt ?? source.Header.TransactionTimestamp;
        Guid requestedByUserId = request.RequestedByUserId ?? source.Header.CashierUserId;
        string? terminalId = request.TerminalId ??
            source.Header.TerminalId ??
            source.Header.SnapshotTerminalIdentifier;
        IReadOnlyDictionary<string, string> metadata =
            BuildPackageMetadata(source, request.Metadata);

        OfficialReportRenderRequest renderRequest = new(
            SourceDocument: bindingResult.Value,
            Format: OfficialReportRenderFormat.Preview,
            ArtifactLabel: "OFFICIAL_TEXT_PREVIEW",
            RequestedByUserId: requestedByUserId,
            RequestedAt: requestedAt,
            TerminalId: terminalId,
            CopyModeMarker: printableResult.Value.CopyAudit.OutputMarker,
            Metadata: metadata);

        Result<OfficialTextPreviewRenderResult> previewResult =
            await _textPreviewRenderer.RenderPreviewAsync(renderRequest, cancellationToken)
                .ConfigureAwait(false);

        if (previewResult.IsFailure)
        {
            return Result<OfficialOutputExportPackage>.Fail(previewResult.ErrorDetail);
        }

        OfficialOutputArtifactManifestSource manifestSource =
            BuildManifestSource(
                source,
                printableResult.Value.CopyAudit,
                requestedAt,
                terminalId);

        Result<OfficialOutputArtifactManifestEntry> manifestEntryResult =
            OfficialOutputArtifactManifestBuilder.FromReceiptInvoicePreview(
                previewResult.Value,
                manifestSource,
                BuildEntryMetadata(printableResult.Value.CopyAudit));

        if (manifestEntryResult.IsFailure)
        {
            return Result<OfficialOutputExportPackage>.Fail(manifestEntryResult.ErrorDetail);
        }

        Result<OfficialOutputExportPayload> payloadResult =
            OfficialOutputExportPackageBuilder.FromReceiptInvoicePreview(previewResult.Value);

        if (payloadResult.IsFailure)
        {
            return Result<OfficialOutputExportPackage>.Fail(payloadResult.ErrorDetail);
        }

        Result<OfficialOutputArtifactManifestPackage> manifestPackageResult =
            _manifestBuilder.Build(
                new[] { manifestEntryResult.Value },
                requestedAt,
                metadata);

        if (manifestPackageResult.IsFailure)
        {
            return Result<OfficialOutputExportPackage>.Fail(manifestPackageResult.ErrorDetail);
        }

        return _exportPackageBuilder.Build(
            manifestPackageResult.Value,
            new[] { payloadResult.Value });
    }

    private static OfficialOutputArtifactManifestSource BuildManifestSource(
        OfficialOutputSourceResult source,
        OfficialPrintableCopyInfo copyAudit,
        DateTimeOffset createdAt,
        string? terminalId)
    {
        return new OfficialOutputArtifactManifestSource(
            SourceKind: "OfficialOutputSourceRead",
            SourceReference: "journal:" + source.Header.JournalEntryId.ToString("D"),
            DocumentKind: source.Header.DocumentKind.ToString(),
            OfficialDocumentNumber: source.Header.OfficialDocumentNumber,
            ReportKind: null,
            ReportNumber: null,
            ReferenceNumber: source.Evidence.OriginalEventId.ToString("D"),
            TerminalId: terminalId,
            CashierIdentifier: source.Header.CashierIdentifier,
            BusinessDate: DateOnly.FromDateTime(source.Header.TransactionTimestamp.Date),
            PeriodStart: null,
            PeriodEnd: null,
            IsCopyOrReprint: copyAudit.IsCopyOrReprint,
            LifecycleState: source.Lifecycle.State.ToString(),
            CreatedAt: createdAt,
            Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["copyMode"] = copyAudit.CopyMode.ToString(),
                ["outputMarker"] = copyAudit.OutputMarker,
                ["snapshotPayloadHash"] = source.Evidence.SnapshotPayloadHash,
                ["snapshotHashAlgorithm"] = source.Evidence.SnapshotHashAlgorithm
            });
    }

    private static IReadOnlyDictionary<string, string> BuildPackageMetadata(
        OfficialOutputSourceResult source,
        IReadOnlyDictionary<string, string>? requestMetadata)
    {
        Dictionary<string, string> metadata = new(NormalizeMetadata(requestMetadata), StringComparer.Ordinal);

        AddIfMissing(metadata, "producer", nameof(OfficialOutputTextFileTrustedPackageProducer));
        AddIfMissing(metadata, "sourceKind", "OfficialOutputSourceRead");
        AddIfMissing(metadata, "journalEntryId", source.Header.JournalEntryId.ToString("D"));
        AddIfMissing(metadata, "officialDocumentNumber", source.Header.OfficialDocumentNumber);
        AddIfMissing(metadata, "snapshotPayloadHash", source.Evidence.SnapshotPayloadHash);

        return metadata
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, string> BuildEntryMetadata(
        OfficialPrintableCopyInfo copyAudit)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["copyMode"] = copyAudit.CopyMode.ToString(),
            ["entry"] = OfficialOutputArtifactFamily.ReceiptInvoice.ToString(),
            ["outputMarker"] = copyAudit.OutputMarker
        };
    }

    private static Result ValidateMetadata(
        IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return Result.Success();
        }

        HashSet<string> keys = new(StringComparer.Ordinal);

        foreach (KeyValuePair<string, string> pair in metadata)
        {
            string key = Normalize(pair.Key);

            if (key.Length == 0)
            {
                return Result.Fail(Error.Validation(
                    "OfficialOutputTextFileTrustedPackageProducer.MetadataKeyRequired",
                    "Official output text-file trusted package producer metadata key is required."));
            }

            if (!keys.Add(key))
            {
                return Result.Fail(Error.Validation(
                    "OfficialOutputTextFileTrustedPackageProducer.DuplicateMetadataKey",
                    "Official output text-file trusted package producer metadata contains duplicate keys after trimming."));
            }
        }

        return Result.Success();
    }

    private static IReadOnlyDictionary<string, string> NormalizeMetadata(
        IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return metadata
            .Select(static pair => new KeyValuePair<string, string>(
                Normalize(pair.Key),
                Normalize(pair.Value)))
            .Where(static pair => pair.Key.Length > 0)
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.Ordinal);
    }

    private static void AddIfMissing(
        IDictionary<string, string> metadata,
        string key,
        string value)
    {
        if (!metadata.ContainsKey(key))
        {
            metadata.Add(key, value);
        }
    }

    private static string? NormalizeOptional(string? value)
    {
        string normalized = Normalize(value);

        return normalized.Length == 0 ? null : normalized;
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }
}
