using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Salryn.Core.Common;

namespace Salryn.Core.Services;

public enum OfficialOutputArtifactFamily
{
    ReceiptInvoice = 1,
    ReadingReport = 2,
    AuditEvidenceReport = 3
}

public sealed record OfficialOutputArtifactManifestSource(
    string SourceKind,
    string SourceReference,
    string? DocumentKind,
    string? OfficialDocumentNumber,
    string? ReportKind,
    string? ReportNumber,
    string? ReferenceNumber,
    string? TerminalId,
    string? CashierIdentifier,
    DateOnly? BusinessDate,
    DateTimeOffset? PeriodStart,
    DateTimeOffset? PeriodEnd,
    bool? IsCopyOrReprint,
    string? LifecycleState,
    DateTimeOffset? CreatedAt,
    IReadOnlyDictionary<string, string>? Metadata);

public sealed record OfficialOutputArtifactManifestEntry(
    OfficialOutputArtifactFamily ArtifactFamily,
    string ArtifactLabel,
    string ArtifactReference,
    string ContentHash,
    string HashAlgorithm,
    int LineCount,
    string? RenderFormat,
    OfficialOutputArtifactManifestSource? Source,
    IReadOnlyDictionary<string, string>? Metadata);

public sealed record OfficialOutputArtifactManifestPackage(
    IReadOnlyList<OfficialOutputArtifactManifestEntry> Entries,
    string ManifestHash,
    string ManifestReference,
    int EntryCount,
    DateTimeOffset? CreatedAt,
    IReadOnlyDictionary<string, string> Metadata,
    OfficialOutputArtifactManifestTextRenderResult TextResult);

public sealed record OfficialOutputArtifactManifestTextRenderResult(
    IReadOnlyList<string> Lines,
    string Text);

// Kirch: why - all official preview artifacts need one deterministic Core index before any concrete output adapter exists.
public sealed class OfficialOutputArtifactManifestBuilder
{
    private const string HashAlgorithmName = "SHA-256";
    private const string ManifestReferencePrefix = "salryn://official-output-manifest/";

    private readonly OfficialOutputArtifactManifestTextRenderer _textRenderer;

    public OfficialOutputArtifactManifestBuilder(
        OfficialOutputArtifactManifestTextRenderer? textRenderer = null)
    {
        _textRenderer = textRenderer ?? new OfficialOutputArtifactManifestTextRenderer();
    }

    public Result<OfficialOutputArtifactManifestPackage> Build(
        IReadOnlyList<OfficialOutputArtifactManifestEntry>? entries,
        DateTimeOffset? createdAt = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        Result validation = ValidateInput(entries, metadata);

        if (validation.IsFailure)
        {
            return Result<OfficialOutputArtifactManifestPackage>.Fail(validation.ErrorDetail);
        }

        IReadOnlyList<OfficialOutputArtifactManifestEntry> orderedEntries =
            OrderEntries(NormalizeEntries(entries!));
        IReadOnlyDictionary<string, string> normalizedMetadata = NormalizeMetadata(metadata);

        Result<OfficialOutputArtifactManifestTextRenderResult> textResult =
            _textRenderer.Render(orderedEntries, createdAt, normalizedMetadata);

        if (textResult.IsFailure)
        {
            return Result<OfficialOutputArtifactManifestPackage>.Fail(textResult.ErrorDetail);
        }

        string manifestHash = ComputeContentHash(textResult.Value.Text);
        string manifestReference = ManifestReferencePrefix + manifestHash;

        return Result<OfficialOutputArtifactManifestPackage>.Success(
            new OfficialOutputArtifactManifestPackage(
                orderedEntries,
                manifestHash,
                manifestReference,
                orderedEntries.Count,
                createdAt,
                normalizedMetadata,
                textResult.Value));
    }

    public static Result<OfficialOutputArtifactManifestEntry> FromReceiptInvoicePreview(
        OfficialTextPreviewRenderResult? result,
        OfficialOutputArtifactManifestSource? source,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (result is null || source is null)
        {
            return Result<OfficialOutputArtifactManifestEntry>.Fail(Error.Validation(
                "OfficialOutputArtifactManifest.PreviewArtifactRequired",
                "Official output artifact manifest requires a receipt/invoice preview artifact."));
        }

        OfficialOutputArtifactManifestSource normalizedSource = FillSourceDefaults(
            source,
            reportKind: null,
            createdAt: result.ContractResult.Artifact.CreatedAt);

        return Result<OfficialOutputArtifactManifestEntry>.Success(
            new OfficialOutputArtifactManifestEntry(
                OfficialOutputArtifactFamily.ReceiptInvoice,
                result.ContractResult.Artifact.ArtifactLabel,
                result.ArtifactReference,
                result.ContentHash,
                HashAlgorithmName,
                result.LineCount,
                result.ContractResult.Artifact.Format.ToString(),
                normalizedSource,
                metadata));
    }

    public static Result<OfficialOutputArtifactManifestEntry> FromReadingReportPreview(
        OfficialReadingReportPreviewResult? result,
        OfficialOutputArtifactManifestSource? source,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (result is null || source is null)
        {
            return Result<OfficialOutputArtifactManifestEntry>.Fail(Error.Validation(
                "OfficialOutputArtifactManifest.PreviewArtifactRequired",
                "Official output artifact manifest requires a reading report preview artifact."));
        }

        OfficialOutputArtifactManifestSource normalizedSource = FillSourceDefaults(
            source,
            result.ReportKind.ToString(),
            createdAt: null);

        return Result<OfficialOutputArtifactManifestEntry>.Success(
            new OfficialOutputArtifactManifestEntry(
                OfficialOutputArtifactFamily.ReadingReport,
                result.ArtifactLabel,
                result.ArtifactReference,
                result.ContentHash,
                HashAlgorithmName,
                result.LineCount,
                OfficialReportRenderFormat.Preview.ToString(),
                normalizedSource,
                metadata));
    }

    public static Result<OfficialOutputArtifactManifestEntry> FromAuditEvidenceReportPreview(
        OfficialAuditEvidenceReportPreviewResult? result,
        OfficialOutputArtifactManifestSource? source,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (result is null || source is null)
        {
            return Result<OfficialOutputArtifactManifestEntry>.Fail(Error.Validation(
                "OfficialOutputArtifactManifest.PreviewArtifactRequired",
                "Official output artifact manifest requires an audit/electronic journal evidence preview artifact."));
        }

        OfficialOutputArtifactManifestSource normalizedSource = FillSourceDefaults(
            source,
            result.ReportKind.ToString(),
            createdAt: null);

        return Result<OfficialOutputArtifactManifestEntry>.Success(
            new OfficialOutputArtifactManifestEntry(
                OfficialOutputArtifactFamily.AuditEvidenceReport,
                result.ArtifactLabel,
                result.ArtifactReference,
                result.ContentHash,
                HashAlgorithmName,
                result.LineCount,
                OfficialReportRenderFormat.Preview.ToString(),
                normalizedSource,
                metadata));
    }

    internal static Result ValidateInput(
        IReadOnlyList<OfficialOutputArtifactManifestEntry>? entries,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (entries is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputArtifactManifest.EntriesRequired",
                "Official output artifact manifest entries are required."));
        }

        if (entries.Count == 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputArtifactManifest.EntryRequired",
                "Official output artifact manifest requires at least one entry."));
        }

        Result metadataValidation = ValidateMetadata(metadata);

        if (metadataValidation.IsFailure)
        {
            return metadataValidation;
        }

        HashSet<string> artifactReferences = new(StringComparer.Ordinal);
        HashSet<string> sourceKeys = new(StringComparer.Ordinal);

        foreach (OfficialOutputArtifactManifestEntry entry in entries)
        {
            Result entryValidation = ValidateEntry(entry);

            if (entryValidation.IsFailure)
            {
                return entryValidation;
            }

            OfficialOutputArtifactManifestEntry normalizedEntry = NormalizeEntry(entry);

            if (!artifactReferences.Add(normalizedEntry.ArtifactReference))
            {
                return Result.Fail(Error.Validation(
                    "OfficialOutputArtifactManifest.DuplicateArtifactReference",
                    "Official output artifact manifest contains duplicate artifact references."));
            }

            string sourceKey = normalizedEntry.ArtifactFamily.ToString() + "|" + normalizedEntry.Source!.SourceReference;

            if (!sourceKeys.Add(sourceKey))
            {
                return Result.Fail(Error.Validation(
                    "OfficialOutputArtifactManifest.DuplicateSourceReference",
                    "Official output artifact manifest contains duplicate source references for a family."));
            }
        }

        return Result.Success();
    }

    internal static IReadOnlyList<OfficialOutputArtifactManifestEntry> NormalizeEntries(
        IReadOnlyList<OfficialOutputArtifactManifestEntry> entries)
    {
        return entries.Select(NormalizeEntry).ToArray();
    }

    internal static IReadOnlyList<OfficialOutputArtifactManifestEntry> OrderEntries(
        IReadOnlyList<OfficialOutputArtifactManifestEntry> entries)
    {
        return entries
            .OrderBy(static entry => (int)entry.ArtifactFamily)
            .ThenBy(static entry => entry.Source!.SourceReference, StringComparer.Ordinal)
            .ThenBy(static entry => entry.ArtifactReference, StringComparer.Ordinal)
            .ThenBy(static entry => entry.ArtifactLabel, StringComparer.Ordinal)
            .ToArray();
    }

    internal static IReadOnlyDictionary<string, string> NormalizeMetadata(
        IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return metadata
            .Select(pair => new KeyValuePair<string, string>(
                Normalize(pair.Key),
                Normalize(pair.Value)))
            .Where(pair => pair.Key.Length > 0)
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.Ordinal);
    }

    internal static string FormatMetadata(
        IReadOnlyDictionary<string, string>? metadata)
    {
        IReadOnlyDictionary<string, string> normalized = NormalizeMetadata(metadata);

        return string.Join(" | ", normalized.Select(static pair => pair.Key + "=" + pair.Value));
    }

    private static Result ValidateEntry(
        OfficialOutputArtifactManifestEntry? entry)
    {
        if (entry is null ||
            !Enum.IsDefined(typeof(OfficialOutputArtifactFamily), entry.ArtifactFamily))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputArtifactManifest.InvalidEntry",
                "Official output artifact manifest entry is invalid."));
        }

        if (string.IsNullOrWhiteSpace(entry.ArtifactLabel))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputArtifactManifest.ArtifactLabelRequired",
                "Official output artifact manifest artifact label is required."));
        }

        if (string.IsNullOrWhiteSpace(entry.ArtifactReference))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputArtifactManifest.ArtifactReferenceRequired",
                "Official output artifact manifest artifact reference is required."));
        }

        if (string.IsNullOrWhiteSpace(entry.ContentHash))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputArtifactManifest.ContentHashRequired",
                "Official output artifact manifest content hash is required."));
        }

        if (string.IsNullOrWhiteSpace(entry.HashAlgorithm))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputArtifactManifest.HashAlgorithmRequired",
                "Official output artifact manifest hash algorithm is required."));
        }

        if (entry.LineCount < 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputArtifactManifest.InvalidLineCount",
                "Official output artifact manifest line count is invalid."));
        }

        Result sourceValidation = ValidateSource(entry.Source);

        if (sourceValidation.IsFailure)
        {
            return sourceValidation;
        }

        return ValidateMetadata(entry.Metadata);
    }

    private static Result ValidateSource(
        OfficialOutputArtifactManifestSource? source)
    {
        if (source is null ||
            string.IsNullOrWhiteSpace(source.SourceKind) ||
            string.IsNullOrWhiteSpace(source.SourceReference))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputArtifactManifest.SourceRequired",
                "Official output artifact manifest source is required."));
        }

        if (source.PeriodStart.HasValue &&
            source.PeriodEnd.HasValue &&
            source.PeriodEnd.Value <= source.PeriodStart.Value)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputArtifactManifest.InvalidPeriod",
                "Official output artifact manifest source period is invalid."));
        }

        return ValidateMetadata(source.Metadata);
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
                    "OfficialOutputArtifactManifest.MetadataKeyRequired",
                    "Official output artifact manifest metadata key is required."));
            }

            if (!keys.Add(key))
            {
                return Result.Fail(Error.Validation(
                    "OfficialOutputArtifactManifest.DuplicateMetadataKey",
                    "Official output artifact manifest metadata contains duplicate keys after trimming."));
            }
        }

        return Result.Success();
    }

    private static OfficialOutputArtifactManifestEntry NormalizeEntry(
        OfficialOutputArtifactManifestEntry entry)
    {
        return entry with
        {
            ArtifactLabel = Normalize(entry.ArtifactLabel),
            ArtifactReference = Normalize(entry.ArtifactReference),
            ContentHash = Normalize(entry.ContentHash),
            HashAlgorithm = Normalize(entry.HashAlgorithm),
            RenderFormat = NormalizeOptional(entry.RenderFormat),
            Source = NormalizeSource(entry.Source!),
            Metadata = NormalizeMetadata(entry.Metadata)
        };
    }

    private static OfficialOutputArtifactManifestSource NormalizeSource(
        OfficialOutputArtifactManifestSource source)
    {
        return source with
        {
            SourceKind = Normalize(source.SourceKind),
            SourceReference = Normalize(source.SourceReference),
            DocumentKind = NormalizeOptional(source.DocumentKind),
            OfficialDocumentNumber = NormalizeOptional(source.OfficialDocumentNumber),
            ReportKind = NormalizeOptional(source.ReportKind),
            ReportNumber = NormalizeOptional(source.ReportNumber),
            ReferenceNumber = NormalizeOptional(source.ReferenceNumber),
            TerminalId = NormalizeOptional(source.TerminalId),
            CashierIdentifier = NormalizeOptional(source.CashierIdentifier),
            LifecycleState = NormalizeOptional(source.LifecycleState),
            Metadata = NormalizeMetadata(source.Metadata)
        };
    }

    private static OfficialOutputArtifactManifestSource FillSourceDefaults(
        OfficialOutputArtifactManifestSource source,
        string? reportKind,
        DateTimeOffset? createdAt)
    {
        return source with
        {
            ReportKind = Normalize(source.ReportKind).Length == 0
                ? NormalizeOptional(reportKind)
                : NormalizeOptional(source.ReportKind),
            CreatedAt = source.CreatedAt ?? createdAt
        };
    }

    private static string ComputeContentHash(string text)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));

        return Convert.ToHexString(hash).ToLowerInvariant();
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

public sealed class OfficialOutputArtifactManifestTextRenderer
{
    private const string Title = "SALRYN OFFICIAL OUTPUT ARTIFACT MANIFEST";

    public Result<OfficialOutputArtifactManifestTextRenderResult> Render(
        IReadOnlyList<OfficialOutputArtifactManifestEntry>? entries,
        DateTimeOffset? createdAt = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        Result validation = OfficialOutputArtifactManifestBuilder.ValidateInput(entries, metadata);

        if (validation.IsFailure)
        {
            return Result<OfficialOutputArtifactManifestTextRenderResult>.Fail(validation.ErrorDetail);
        }

        IReadOnlyList<OfficialOutputArtifactManifestEntry> orderedEntries =
            OfficialOutputArtifactManifestBuilder.OrderEntries(
                OfficialOutputArtifactManifestBuilder.NormalizeEntries(entries!));
        IReadOnlyDictionary<string, string> normalizedMetadata =
            OfficialOutputArtifactManifestBuilder.NormalizeMetadata(metadata);

        List<string> lines = new()
        {
            Title,
            "[MANIFEST]",
            "EntryCount: " + orderedEntries.Count.ToString(CultureInfo.InvariantCulture)
        };

        if (createdAt.HasValue)
        {
            lines.Add("CreatedAt: " + Format(createdAt.Value));
        }

        lines.Add("ManifestHash: COMPUTED_IN_MANIFEST_PACKAGE");
        lines.Add("ManifestReference: COMPUTED_IN_MANIFEST_PACKAGE");

        string manifestMetadata = OfficialOutputArtifactManifestBuilder.FormatMetadata(normalizedMetadata);

        if (manifestMetadata.Length > 0)
        {
            lines.Add("Metadata: " + manifestMetadata);
        }

        lines.Add("[ARTIFACTS]");

        foreach (OfficialOutputArtifactManifestEntry entry in orderedEntries)
        {
            lines.Add(RenderEntry(entry));
        }

        lines.Add("[EVIDENCE]");
        lines.Add("HashAlgorithm: SHA-256");
        lines.Add("ManifestReferencePrefix: salryn://official-output-manifest/");
        lines.Add("OutputSideEffectMarker: MANIFEST_INDEX_ONLY_NO_OUTPUT_SIDE_EFFECTS");

        return Result<OfficialOutputArtifactManifestTextRenderResult>.Success(
            new OfficialOutputArtifactManifestTextRenderResult(
                lines,
                string.Join("\n", lines)));
    }

    private static string RenderEntry(
        OfficialOutputArtifactManifestEntry entry)
    {
        List<(string Name, string Value)> fields = new()
        {
            ("ArtifactFamily", entry.ArtifactFamily.ToString()),
            ("ArtifactLabel", entry.ArtifactLabel),
            ("ArtifactReference", entry.ArtifactReference),
            ("ContentHash", entry.ContentHash),
            ("HashAlgorithm", entry.HashAlgorithm),
            ("LineCount", entry.LineCount.ToString(CultureInfo.InvariantCulture))
        };

        AddOptional(fields, "RenderFormat", entry.RenderFormat);

        OfficialOutputArtifactManifestSource source = entry.Source!;

        fields.Add(("SourceKind", source.SourceKind));
        fields.Add(("SourceReference", source.SourceReference));
        AddOptional(fields, "DocumentKind", source.DocumentKind);
        AddOptional(fields, "OfficialDocumentNumber", source.OfficialDocumentNumber);
        AddOptional(fields, "ReportKind", source.ReportKind);
        AddOptional(fields, "ReportNumber", source.ReportNumber);
        AddOptional(fields, "ReferenceNumber", source.ReferenceNumber);
        AddOptional(fields, "TerminalId", source.TerminalId);
        AddOptional(fields, "CashierIdentifier", source.CashierIdentifier);
        AddOptional(fields, "BusinessDate", source.BusinessDate);
        AddOptional(fields, "PeriodStart", source.PeriodStart);
        AddOptional(fields, "PeriodEnd", source.PeriodEnd);
        AddOptional(fields, "IsCopyOrReprint", source.IsCopyOrReprint);
        AddOptional(fields, "LifecycleState", source.LifecycleState);
        AddOptional(fields, "CreatedAt", source.CreatedAt);

        string sourceMetadata = OfficialOutputArtifactManifestBuilder.FormatMetadata(source.Metadata);

        if (sourceMetadata.Length > 0)
        {
            fields.Add(("SourceMetadata", sourceMetadata));
        }

        string entryMetadata = OfficialOutputArtifactManifestBuilder.FormatMetadata(entry.Metadata);

        if (entryMetadata.Length > 0)
        {
            fields.Add(("Metadata", entryMetadata));
        }

        return "Artifact: " + string.Join(
            " | ",
            fields.Select(static field => field.Name + "=" + field.Value));
    }

    private static void AddOptional(
        List<(string Name, string Value)> fields,
        string name,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            fields.Add((name, value.Trim()));
        }
    }

    private static void AddOptional(
        List<(string Name, string Value)> fields,
        string name,
        DateOnly? value)
    {
        if (value.HasValue)
        {
            fields.Add((name, value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }
    }

    private static void AddOptional(
        List<(string Name, string Value)> fields,
        string name,
        DateTimeOffset? value)
    {
        if (value.HasValue)
        {
            fields.Add((name, Format(value.Value)));
        }
    }

    private static void AddOptional(
        List<(string Name, string Value)> fields,
        string name,
        bool? value)
    {
        if (value.HasValue)
        {
            fields.Add((name, value.Value ? "true" : "false"));
        }
    }

    private static string Format(DateTimeOffset value)
    {
        return value.ToString("O", CultureInfo.InvariantCulture);
    }
}
