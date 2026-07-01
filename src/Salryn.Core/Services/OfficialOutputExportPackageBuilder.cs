using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Salryn.Core.Common;

namespace Salryn.Core.Services;

public enum OfficialOutputExportPackageKind
{
    PreviewTextBundle = 1
}

public sealed record OfficialOutputExportPayload(
    OfficialOutputArtifactFamily ArtifactFamily,
    string ArtifactLabel,
    string ArtifactReference,
    string ContentHash,
    string HashAlgorithm,
    int LineCount,
    string Text);

public sealed record OfficialOutputExportPackageItem(
    OfficialOutputArtifactManifestEntry ManifestEntry,
    OfficialOutputExportPayload Payload);

public sealed record OfficialOutputExportPackage(
    OfficialOutputExportPackageKind PackageKind,
    OfficialOutputArtifactManifestPackage ManifestPackage,
    IReadOnlyList<OfficialOutputExportPackageItem> Items,
    string PackageHash,
    string PackageReference,
    int EntryCount,
    int PayloadCount,
    int TotalLineCount,
    OfficialOutputExportPackageTextRenderResult TextResult);

public sealed record OfficialOutputExportPackageTextRenderResult(
    IReadOnlyList<string> Lines,
    string Text);

// Kirch: why - this packages the current official preview payloads as one deterministic in-memory export bundle without creating files or adapters.
public sealed class OfficialOutputExportPackageBuilder
{
    private const string PackageReferencePrefix = "salryn://official-output-export-package/";
    private const string HashAlgorithmName = "SHA-256";

    private readonly OfficialOutputExportPackageTextRenderer _textRenderer;

    public OfficialOutputExportPackageBuilder(
        OfficialOutputExportPackageTextRenderer? textRenderer = null)
    {
        _textRenderer = textRenderer ?? new OfficialOutputExportPackageTextRenderer();
    }

    public Result<OfficialOutputExportPackage> Build(
        OfficialOutputArtifactManifestPackage? manifestPackage,
        IReadOnlyList<OfficialOutputExportPayload>? payloads)
    {
        Result validation = Validate(manifestPackage, payloads);

        if (validation.IsFailure)
        {
            return Result<OfficialOutputExportPackage>.Fail(validation.ErrorDetail);
        }

        IReadOnlyList<OfficialOutputExportPackageItem> items =
            BuildItems(manifestPackage!, payloads!);

        Result<OfficialOutputExportPackageTextRenderResult> textResult =
            _textRenderer.Render(manifestPackage!, items);

        if (textResult.IsFailure)
        {
            return Result<OfficialOutputExportPackage>.Fail(textResult.ErrorDetail);
        }

        string packageHash = ComputeContentHash(textResult.Value.Text);
        string packageReference = PackageReferencePrefix + packageHash;

        return Result<OfficialOutputExportPackage>.Success(
            new OfficialOutputExportPackage(
                OfficialOutputExportPackageKind.PreviewTextBundle,
                manifestPackage!,
                items,
                packageHash,
                packageReference,
                manifestPackage!.EntryCount,
                payloads!.Count,
                items.Sum(static item => item.Payload.LineCount),
                textResult.Value));
    }

    public static Result<OfficialOutputExportPayload> FromReceiptInvoicePreview(
        OfficialTextPreviewRenderResult? result)
    {
        if (result is null)
        {
            return Result<OfficialOutputExportPayload>.Fail(Error.Validation(
                "OfficialOutputExportPackage.PreviewPayloadRequired",
                "Official output export package requires a receipt/invoice preview payload."));
        }

        return Result<OfficialOutputExportPayload>.Success(
            new OfficialOutputExportPayload(
                OfficialOutputArtifactFamily.ReceiptInvoice,
                result.ContractResult.Artifact.ArtifactLabel,
                result.ArtifactReference,
                result.ContentHash,
                HashAlgorithmName,
                result.LineCount,
                result.TextResult.Text));
    }

    public static Result<OfficialOutputExportPayload> FromReadingReportPreview(
        OfficialReadingReportPreviewResult? result)
    {
        if (result is null)
        {
            return Result<OfficialOutputExportPayload>.Fail(Error.Validation(
                "OfficialOutputExportPackage.PreviewPayloadRequired",
                "Official output export package requires a reading report preview payload."));
        }

        return Result<OfficialOutputExportPayload>.Success(
            new OfficialOutputExportPayload(
                OfficialOutputArtifactFamily.ReadingReport,
                result.ArtifactLabel,
                result.ArtifactReference,
                result.ContentHash,
                HashAlgorithmName,
                result.LineCount,
                result.TextResult.Text));
    }

    public static Result<OfficialOutputExportPayload> FromAuditEvidenceReportPreview(
        OfficialAuditEvidenceReportPreviewResult? result)
    {
        if (result is null)
        {
            return Result<OfficialOutputExportPayload>.Fail(Error.Validation(
                "OfficialOutputExportPackage.PreviewPayloadRequired",
                "Official output export package requires an audit/electronic journal evidence preview payload."));
        }

        return Result<OfficialOutputExportPayload>.Success(
            new OfficialOutputExportPayload(
                OfficialOutputArtifactFamily.AuditEvidenceReport,
                result.ArtifactLabel,
                result.ArtifactReference,
                result.ContentHash,
                HashAlgorithmName,
                result.LineCount,
                result.TextResult.Text));
    }

    public static Result<OfficialOutputExportPayload> FromEngineeringEvidencePackage(
        OfficialEngineeringEvidencePackageResult? result)
    {
        if (result is null)
        {
            return Result<OfficialOutputExportPayload>.Fail(Error.Validation(
                "OfficialOutputExportPackage.EngineeringEvidenceRequired",
                "Official output export package requires an engineering evidence payload."));
        }

        string contentHash = OfficialOutputExportPackageBuilder.ComputeContentHash(result.PackageText);

        return Result<OfficialOutputExportPayload>.Success(
            new OfficialOutputExportPayload(
                OfficialOutputArtifactFamily.AuditEvidenceReport,
                "OFFICIAL_ENGINEERING_EVIDENCE",
                OfficialEngineeringEvidencePackageAssembler.BuildArtifactReference(contentHash),
                contentHash,
                HashAlgorithmName,
                OfficialOutputExportPackageBuilder.CountLines(result.PackageText),
                result.PackageText));
    }

    internal static Result Validate(
        OfficialOutputArtifactManifestPackage? manifestPackage,
        IReadOnlyList<OfficialOutputExportPayload>? payloads)
    {
        if (manifestPackage is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputExportPackage.ManifestRequired",
                "Official output export package requires a manifest package."));
        }

        if (manifestPackage.Entries is null || manifestPackage.Entries.Count == 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputExportPackage.ManifestEntriesRequired",
                "Official output export package requires manifest entries."));
        }

        if (payloads is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputExportPackage.PayloadsRequired",
                "Official output export package requires payloads."));
        }

        Result manifestEntryValidation =
            OfficialOutputArtifactManifestBuilder.ValidateInput(manifestPackage.Entries, manifestPackage.Metadata);

        if (manifestEntryValidation.IsFailure)
        {
            return manifestEntryValidation;
        }

        HashSet<string> payloadReferences = new(StringComparer.Ordinal);
        Dictionary<string, OfficialOutputArtifactManifestEntry> manifestByReference =
            manifestPackage.Entries.ToDictionary(
                static entry => Normalize(entry.ArtifactReference),
                static entry => entry,
                StringComparer.Ordinal);

        foreach (OfficialOutputExportPayload payload in payloads)
        {
            Result payloadValidation = ValidatePayload(payload);

            if (payloadValidation.IsFailure)
            {
                return payloadValidation;
            }

            OfficialOutputExportPayload normalizedPayload = NormalizePayload(payload);

            if (!payloadReferences.Add(normalizedPayload.ArtifactReference))
            {
                return Result.Fail(Error.Validation(
                    "OfficialOutputExportPackage.DuplicatePayloadArtifactReference",
                    "Official output export package contains duplicate payload artifact references."));
            }

            if (!manifestByReference.TryGetValue(
                normalizedPayload.ArtifactReference,
                out OfficialOutputArtifactManifestEntry? manifestEntry))
            {
                return Result.Fail(Error.Validation(
                    "OfficialOutputExportPackage.PayloadWithoutManifestEntry",
                    "Official output export package contains a payload without a matching manifest entry."));
            }

            Result matchValidation = ValidatePayloadMatchesManifest(
                normalizedPayload,
                NormalizeManifestEntry(manifestEntry));

            if (matchValidation.IsFailure)
            {
                return matchValidation;
            }
        }

        foreach (OfficialOutputArtifactManifestEntry entry in manifestPackage.Entries)
        {
            if (!payloadReferences.Contains(Normalize(entry.ArtifactReference)))
            {
                return Result.Fail(Error.Validation(
                    "OfficialOutputExportPackage.ManifestEntryWithoutPayload",
                    "Official output export package manifest entry has no matching payload."));
            }
        }

        return Result.Success();
    }

    internal static IReadOnlyList<OfficialOutputExportPackageItem> BuildItems(
        OfficialOutputArtifactManifestPackage manifestPackage,
        IReadOnlyList<OfficialOutputExportPayload> payloads)
    {
        Dictionary<string, OfficialOutputExportPayload> payloadsByReference =
            payloads
                .Select(NormalizePayload)
                .ToDictionary(
                    static payload => payload.ArtifactReference,
                    static payload => payload,
                    StringComparer.Ordinal);

        return manifestPackage.Entries
            .Select(NormalizeManifestEntry)
            .Select(entry => new OfficialOutputExportPackageItem(
                entry,
                payloadsByReference[entry.ArtifactReference]))
            .ToArray();
    }

    internal static OfficialOutputExportPayload NormalizePayload(
        OfficialOutputExportPayload payload)
    {
        return payload with
        {
            ArtifactLabel = Normalize(payload.ArtifactLabel),
            ArtifactReference = Normalize(payload.ArtifactReference),
            ContentHash = Normalize(payload.ContentHash),
            HashAlgorithm = Normalize(payload.HashAlgorithm),
            Text = NormalizeText(payload.Text)
        };
    }

    internal static OfficialOutputArtifactManifestEntry NormalizeManifestEntry(
        OfficialOutputArtifactManifestEntry entry)
    {
        return entry with
        {
            ArtifactLabel = Normalize(entry.ArtifactLabel),
            ArtifactReference = Normalize(entry.ArtifactReference),
            ContentHash = Normalize(entry.ContentHash),
            HashAlgorithm = Normalize(entry.HashAlgorithm)
        };
    }

    private static Result ValidatePayload(
        OfficialOutputExportPayload? payload)
    {
        if (payload is null ||
            !Enum.IsDefined(typeof(OfficialOutputArtifactFamily), payload.ArtifactFamily))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputExportPackage.InvalidPayload",
                "Official output export package payload is invalid."));
        }

        if (string.IsNullOrWhiteSpace(payload.ArtifactLabel))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputExportPackage.ArtifactLabelRequired",
                "Official output export package payload artifact label is required."));
        }

        if (string.IsNullOrWhiteSpace(payload.ArtifactReference))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputExportPackage.ArtifactReferenceRequired",
                "Official output export package payload artifact reference is required."));
        }

        if (string.IsNullOrWhiteSpace(payload.ContentHash))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputExportPackage.ContentHashRequired",
                "Official output export package payload content hash is required."));
        }

        if (string.IsNullOrWhiteSpace(payload.HashAlgorithm))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputExportPackage.HashAlgorithmRequired",
                "Official output export package payload hash algorithm is required."));
        }

        if (payload.LineCount < 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputExportPackage.InvalidLineCount",
                "Official output export package payload line count is invalid."));
        }

        if (string.IsNullOrWhiteSpace(payload.Text))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputExportPackage.PayloadTextRequired",
                "Official output export package payload text is required."));
        }

        OfficialOutputExportPayload normalizedPayload = NormalizePayload(payload);

        if (!string.Equals(
            normalizedPayload.ContentHash,
            ComputeContentHash(normalizedPayload.Text),
            StringComparison.Ordinal))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputExportPackage.PayloadHashMismatch",
                "Official output export package payload content hash does not match payload text."));
        }

        int actualLineCount = CountLines(normalizedPayload.Text);

        if (normalizedPayload.LineCount != actualLineCount)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputExportPackage.PayloadLineCountMismatch",
                "Official output export package payload line count does not match payload text."));
        }

        return Result.Success();
    }

    private static Result ValidatePayloadMatchesManifest(
        OfficialOutputExportPayload payload,
        OfficialOutputArtifactManifestEntry manifestEntry)
    {
        if (payload.ArtifactFamily != manifestEntry.ArtifactFamily)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputExportPackage.ArtifactFamilyMismatch",
                "Official output export package payload family does not match manifest entry."));
        }

        if (!string.Equals(payload.ArtifactLabel, manifestEntry.ArtifactLabel, StringComparison.Ordinal))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputExportPackage.ArtifactLabelMismatch",
                "Official output export package payload label does not match manifest entry."));
        }

        if (!string.Equals(payload.ContentHash, manifestEntry.ContentHash, StringComparison.Ordinal))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputExportPackage.ContentHashMismatch",
                "Official output export package payload content hash does not match manifest entry."));
        }

        if (!string.Equals(payload.HashAlgorithm, manifestEntry.HashAlgorithm, StringComparison.Ordinal))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputExportPackage.HashAlgorithmMismatch",
                "Official output export package payload hash algorithm does not match manifest entry."));
        }

        if (payload.LineCount != manifestEntry.LineCount)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputExportPackage.LineCountMismatch",
                "Official output export package payload line count does not match manifest entry."));
        }

        return Result.Success();
    }

    internal static int CountLines(string text)
    {
        return NormalizeText(text).Split('\n').Length;
    }

    internal static string ComputeContentHash(string text)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeText(text)));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormalizeText(string? text)
    {
        return text?.Replace("\r\n", "\n").Replace('\r', '\n').Trim() ?? string.Empty;
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }
}

public sealed class OfficialOutputExportPackageTextRenderer
{
    private const string Title = "SALRYN OFFICIAL OUTPUT EXPORT PACKAGE";

    public Result<OfficialOutputExportPackageTextRenderResult> Render(
        OfficialOutputArtifactManifestPackage? manifestPackage,
        IReadOnlyList<OfficialOutputExportPackageItem>? items)
    {
        if (manifestPackage is null)
        {
            return Result<OfficialOutputExportPackageTextRenderResult>.Fail(Error.Validation(
                "OfficialOutputExportPackage.ManifestRequired",
                "Official output export package text rendering requires a manifest package."));
        }

        if (items is null)
        {
            return Result<OfficialOutputExportPackageTextRenderResult>.Fail(Error.Validation(
                "OfficialOutputExportPackage.ItemsRequired",
                "Official output export package text rendering requires package items."));
        }

        List<string> lines = new()
        {
            Title,
            "[PACKAGE]",
            "PackageKind: " + OfficialOutputExportPackageKind.PreviewTextBundle,
            "EntryCount: " + manifestPackage.EntryCount.ToString(CultureInfo.InvariantCulture),
            "PayloadCount: " + items.Count.ToString(CultureInfo.InvariantCulture),
            "TotalLineCount: " + items.Sum(static item => item.Payload.LineCount).ToString(CultureInfo.InvariantCulture),
            "ManifestHash: " + manifestPackage.ManifestHash,
            "ManifestReference: " + manifestPackage.ManifestReference,
            "PackageHash: COMPUTED_IN_EXPORT_PACKAGE",
            "PackageReference: COMPUTED_IN_EXPORT_PACKAGE",
            "[MANIFEST]"
        };

        AddTextBlock(lines, "ManifestText", manifestPackage.TextResult.Text);

        lines.Add("[PAYLOADS]");

        foreach (OfficialOutputExportPackageItem item in items)
        {
            lines.Add(RenderItemHeader(item));
            AddTextBlock(lines, "PayloadText", item.Payload.Text);
        }

        lines.Add("[EVIDENCE]");
        lines.Add("HashAlgorithm: SHA-256");
        lines.Add("PackageReferencePrefix: salryn://official-output-export-package/");
        lines.Add("OutputSideEffectMarker: EXPORT_PACKAGE_IN_MEMORY_ONLY");

        return Result<OfficialOutputExportPackageTextRenderResult>.Success(
            new OfficialOutputExportPackageTextRenderResult(
                lines,
                string.Join("\n", lines)));
    }

    private static string RenderItemHeader(
        OfficialOutputExportPackageItem item)
    {
        OfficialOutputArtifactManifestEntry entry = item.ManifestEntry;
        OfficialOutputExportPayload payload = item.Payload;

        return string.Join(
            " | ",
            new[]
            {
                "Payload: ArtifactFamily=" + payload.ArtifactFamily,
                "ArtifactLabel=" + payload.ArtifactLabel,
                "ArtifactReference=" + payload.ArtifactReference,
                "ContentHash=" + payload.ContentHash,
                "HashAlgorithm=" + payload.HashAlgorithm,
                "LineCount=" + payload.LineCount.ToString(CultureInfo.InvariantCulture),
                "ManifestSourceReference=" + entry.Source!.SourceReference
            });
    }

    private static void AddTextBlock(
        List<string> lines,
        string label,
        string text)
    {
        string[] textLines = text.Split('\n');

        for (int index = 0; index < textLines.Length; index++)
        {
            lines.Add(label + "[" + (index + 1).ToString(CultureInfo.InvariantCulture) + "]: " + textLines[index]);
        }
    }
}
