using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Salryn.Core.Common;

namespace Salryn.Core.Services;

public sealed record OfficialOutputDeliveryLedgerEntry(
    int EntryNumber,
    OfficialOutputDeliveryTarget DeliveryTarget,
    string DeliveryReference,
    string DeliveryHash,
    string HashAlgorithm,
    string AdapterName,
    string PackageReference,
    string PackageHash,
    string? ManifestReference,
    string? ManifestHash,
    int EntryCount,
    int PayloadCount,
    int TotalLineCount,
    string SideEffectMarker,
    DateTimeOffset? DeliveredAt,
    Guid? RequestedByUserId,
    string? TerminalId,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record OfficialOutputDeliveryLedgerPackage(
    IReadOnlyList<OfficialOutputDeliveryLedgerEntry> Entries,
    int EntryCount,
    string LedgerHash,
    string LedgerReference,
    DateTimeOffset? CreatedAt,
    IReadOnlyDictionary<string, string> Metadata,
    OfficialOutputDeliveryLedgerTextRenderResult TextResult);

public sealed record OfficialOutputDeliveryLedgerTextRenderResult(
    IReadOnlyList<string> Lines,
    string Text);

// Kirch: why - delivery receipts need one deterministic Core ledger before persistence, export, rendering, or device adapters exist.
public sealed class OfficialOutputDeliveryReceiptLedgerBuilder
{
    private const string LedgerReferencePrefix = "salryn://official-output-delivery-ledger/";

    private readonly OfficialOutputDeliveryReceiptLedgerTextRenderer _textRenderer;

    public OfficialOutputDeliveryReceiptLedgerBuilder(
        OfficialOutputDeliveryReceiptLedgerTextRenderer? textRenderer = null)
    {
        _textRenderer = textRenderer ?? new OfficialOutputDeliveryReceiptLedgerTextRenderer();
    }

    public Result<OfficialOutputDeliveryLedgerPackage> Build(
        IReadOnlyList<OfficialOutputDeliveryResult>? deliveryResults,
        DateTimeOffset? createdAt = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        Result validation = OfficialOutputDeliveryReceiptLedgerValidator.ValidateInput(
            deliveryResults,
            metadata);

        if (validation.IsFailure)
        {
            return Result<OfficialOutputDeliveryLedgerPackage>.Fail(validation.ErrorDetail);
        }

        IReadOnlyList<OfficialOutputDeliveryLedgerEntry> entries =
            NumberEntries(OrderEntries(NormalizeEntries(BuildEntries(deliveryResults!))));
        IReadOnlyDictionary<string, string> normalizedMetadata =
            OfficialOutputDeliveryReceiptLedgerValidator.NormalizeMetadata(metadata);

        Result<OfficialOutputDeliveryLedgerTextRenderResult> textResult =
            _textRenderer.Render(entries, createdAt, normalizedMetadata);

        if (textResult.IsFailure)
        {
            return Result<OfficialOutputDeliveryLedgerPackage>.Fail(textResult.ErrorDetail);
        }

        string ledgerHash = OfficialOutputDeliveryReceiptLedgerValidator.ComputeContentHash(textResult.Value.Text);
        string ledgerReference = LedgerReferencePrefix + ledgerHash;

        return Result<OfficialOutputDeliveryLedgerPackage>.Success(
            new OfficialOutputDeliveryLedgerPackage(
                entries,
                entries.Count,
                ledgerHash,
                ledgerReference,
                createdAt,
                normalizedMetadata,
                textResult.Value));
    }

    internal static IReadOnlyList<OfficialOutputDeliveryLedgerEntry> BuildEntries(
        IReadOnlyList<OfficialOutputDeliveryResult> deliveryResults)
    {
        return deliveryResults
            .Select(result =>
            {
                OfficialOutputDeliveryArtifact artifact = result.Artifact;

                return new OfficialOutputDeliveryLedgerEntry(
                    0,
                    artifact.Target,
                    artifact.DeliveryReference,
                    artifact.DeliveryHash,
                    artifact.HashAlgorithm,
                    artifact.AdapterName,
                    artifact.SourcePackageReference,
                    artifact.SourcePackageHash,
                    artifact.SourceManifestReference,
                    artifact.SourceManifestHash,
                    artifact.EntryCount,
                    artifact.PayloadCount,
                    artifact.TotalLineCount,
                    artifact.SideEffectMarker,
                    artifact.DeliveredAt,
                    RequestedByUserId: null,
                    artifact.TerminalId,
                    artifact.Metadata);
            })
            .ToArray();
    }

    internal static IReadOnlyList<OfficialOutputDeliveryLedgerEntry> NormalizeEntries(
        IReadOnlyList<OfficialOutputDeliveryLedgerEntry> entries)
    {
        return entries.Select(NormalizeEntry).ToArray();
    }

    internal static OfficialOutputDeliveryLedgerEntry NormalizeEntry(
        OfficialOutputDeliveryLedgerEntry entry)
    {
        return entry with
        {
            DeliveryReference = Normalize(entry.DeliveryReference),
            DeliveryHash = Normalize(entry.DeliveryHash),
            HashAlgorithm = Normalize(entry.HashAlgorithm),
            AdapterName = Normalize(entry.AdapterName),
            PackageReference = Normalize(entry.PackageReference),
            PackageHash = Normalize(entry.PackageHash),
            ManifestReference = NormalizeOptional(entry.ManifestReference),
            ManifestHash = NormalizeOptional(entry.ManifestHash),
            SideEffectMarker = Normalize(entry.SideEffectMarker),
            TerminalId = NormalizeOptional(entry.TerminalId),
            Metadata = OfficialOutputDeliveryReceiptLedgerValidator.NormalizeMetadata(entry.Metadata)
        };
    }

    internal static IReadOnlyList<OfficialOutputDeliveryLedgerEntry> OrderEntries(
        IReadOnlyList<OfficialOutputDeliveryLedgerEntry> entries)
    {
        return entries
            .OrderBy(static entry => entry.DeliveryReference, StringComparer.Ordinal)
            .ThenBy(static entry => entry.DeliveryHash, StringComparer.Ordinal)
            .ThenBy(static entry => entry.PackageReference, StringComparer.Ordinal)
            .ThenBy(static entry => entry.PackageHash, StringComparer.Ordinal)
            .ThenBy(static entry => entry.AdapterName, StringComparer.Ordinal)
            .ToArray();
    }

    internal static IReadOnlyList<OfficialOutputDeliveryLedgerEntry> NumberEntries(
        IReadOnlyList<OfficialOutputDeliveryLedgerEntry> entries)
    {
        return entries
            .Select((entry, index) => entry with { EntryNumber = index + 1 })
            .ToArray();
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

public sealed class OfficialOutputDeliveryReceiptLedgerTextRenderer
{
    private const string Title = "SALRYN OFFICIAL OUTPUT DELIVERY RECEIPT LEDGER";
    private const string LedgerReferencePrefix = "salryn://official-output-delivery-ledger/";
    private const string HashAlgorithmName = "SHA-256";
    private const string LedgerSideEffectMarker = "DELIVERY_LEDGER_IN_MEMORY_ONLY_NO_PERSISTENCE";

    public Result<OfficialOutputDeliveryLedgerTextRenderResult> Render(
        IReadOnlyList<OfficialOutputDeliveryLedgerEntry>? entries,
        DateTimeOffset? createdAt = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        Result validation = OfficialOutputDeliveryReceiptLedgerValidator.ValidateEntries(
            entries,
            metadata);

        if (validation.IsFailure)
        {
            return Result<OfficialOutputDeliveryLedgerTextRenderResult>.Fail(validation.ErrorDetail);
        }

        IReadOnlyList<OfficialOutputDeliveryLedgerEntry> orderedEntries =
            OfficialOutputDeliveryReceiptLedgerBuilder.OrderEntries(
                OfficialOutputDeliveryReceiptLedgerBuilder.NormalizeEntries(entries!));
        IReadOnlyDictionary<string, string> normalizedMetadata =
            OfficialOutputDeliveryReceiptLedgerValidator.NormalizeMetadata(metadata);

        List<string> lines = new()
        {
            Title,
            "[LEDGER]",
            "EntryCount: " + orderedEntries.Count.ToString(CultureInfo.InvariantCulture)
        };

        if (createdAt.HasValue)
        {
            lines.Add("CreatedAt: " + Format(createdAt.Value));
        }

        lines.Add("LedgerHash: COMPUTED_IN_LEDGER_PACKAGE");
        lines.Add("LedgerReference: COMPUTED_IN_LEDGER_PACKAGE");

        string ledgerMetadata = OfficialOutputDeliveryReceiptLedgerValidator.FormatMetadata(normalizedMetadata);

        if (ledgerMetadata.Length > 0)
        {
            lines.Add("Metadata: " + ledgerMetadata);
        }

        lines.Add("[DELIVERIES]");

        foreach (OfficialOutputDeliveryLedgerEntry entry in orderedEntries)
        {
            lines.Add(RenderEntry(entry));
        }

        lines.Add("[EVIDENCE]");
        lines.Add("HashAlgorithm: " + HashAlgorithmName);
        lines.Add("LedgerReferencePrefix: " + LedgerReferencePrefix);
        lines.Add("LedgerSideEffectMarker: " + LedgerSideEffectMarker);

        return Result<OfficialOutputDeliveryLedgerTextRenderResult>.Success(
            new OfficialOutputDeliveryLedgerTextRenderResult(
                lines,
                string.Join("\n", lines)));
    }

    private static string RenderEntry(
        OfficialOutputDeliveryLedgerEntry entry)
    {
        List<(string Name, string Value)> fields = new()
        {
            ("EntryNumber", entry.EntryNumber.ToString(CultureInfo.InvariantCulture)),
            ("DeliveryTarget", entry.DeliveryTarget.ToString()),
            ("DeliveryReference", entry.DeliveryReference),
            ("DeliveryHash", entry.DeliveryHash),
            ("HashAlgorithm", entry.HashAlgorithm),
            ("AdapterName", entry.AdapterName),
            ("PackageReference", entry.PackageReference),
            ("PackageHash", entry.PackageHash)
        };

        AddOptional(fields, "ManifestReference", entry.ManifestReference);
        AddOptional(fields, "ManifestHash", entry.ManifestHash);
        fields.Add(("EntryCount", entry.EntryCount.ToString(CultureInfo.InvariantCulture)));
        fields.Add(("PayloadCount", entry.PayloadCount.ToString(CultureInfo.InvariantCulture)));
        fields.Add(("TotalLineCount", entry.TotalLineCount.ToString(CultureInfo.InvariantCulture)));
        fields.Add(("SideEffectMarker", entry.SideEffectMarker));
        AddOptional(fields, "DeliveredAt", entry.DeliveredAt);
        AddOptional(fields, "RequestedByUserId", entry.RequestedByUserId);
        AddOptional(fields, "TerminalId", entry.TerminalId);

        string metadata = OfficialOutputDeliveryReceiptLedgerValidator.FormatMetadata(entry.Metadata);

        if (metadata.Length > 0)
        {
            fields.Add(("Metadata", metadata));
        }

        return "Delivery: " + string.Join(
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
        Guid? value)
    {
        if (value.HasValue)
        {
            fields.Add((name, value.Value.ToString("D")));
        }
    }

    private static string Format(DateTimeOffset value)
    {
        return value.ToString("O", CultureInfo.InvariantCulture);
    }
}

public static class OfficialOutputDeliveryReceiptLedgerValidator
{
    private const string DeliveryReferencePrefix = "salryn://official-output-delivery/in-memory/";
    private const string RequiredDeliveryMarker = "DELIVERY_IN_MEMORY_ONLY_NO_EXTERNAL_OUTPUT";

    public static Result ValidateInput(
        IReadOnlyList<OfficialOutputDeliveryResult>? deliveryResults,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (deliveryResults is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDeliveryLedger.ResultsRequired",
                "Official output delivery ledger requires delivery results."));
        }

        if (deliveryResults.Count == 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDeliveryLedger.ResultRequired",
                "Official output delivery ledger requires at least one delivery result."));
        }

        Result metadataValidation = ValidateMetadata(metadata);

        if (metadataValidation.IsFailure)
        {
            return metadataValidation;
        }

        HashSet<string> deliveryReferences = new(StringComparer.Ordinal);

        foreach (OfficialOutputDeliveryResult result in deliveryResults)
        {
            Result resultValidation = ValidateDeliveryResult(result);

            if (resultValidation.IsFailure)
            {
                return resultValidation;
            }

            string deliveryReference = Normalize(result.Artifact.DeliveryReference);

            if (!deliveryReferences.Add(deliveryReference))
            {
                return Result.Fail(Error.Validation(
                    "OfficialOutputDeliveryLedger.DuplicateDeliveryReference",
                    "Official output delivery ledger contains duplicate delivery references."));
            }
        }

        return Result.Success();
    }

    public static Result ValidateEntries(
        IReadOnlyList<OfficialOutputDeliveryLedgerEntry>? entries,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (entries is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDeliveryLedger.EntriesRequired",
                "Official output delivery ledger entries are required."));
        }

        if (entries.Count == 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDeliveryLedger.EntryRequired",
                "Official output delivery ledger requires at least one entry."));
        }

        Result metadataValidation = ValidateMetadata(metadata);

        if (metadataValidation.IsFailure)
        {
            return metadataValidation;
        }

        HashSet<int> entryNumbers = new();
        HashSet<string> deliveryReferences = new(StringComparer.Ordinal);

        foreach (OfficialOutputDeliveryLedgerEntry entry in entries)
        {
            Result entryValidation = ValidateEntry(entry);

            if (entryValidation.IsFailure)
            {
                return entryValidation;
            }

            OfficialOutputDeliveryLedgerEntry normalized =
                OfficialOutputDeliveryReceiptLedgerBuilder.NormalizeEntry(entry);

            if (!entryNumbers.Add(normalized.EntryNumber))
            {
                return Result.Fail(Error.Validation(
                    "OfficialOutputDeliveryLedger.DuplicateEntryNumber",
                    "Official output delivery ledger contains duplicate entry numbers."));
            }

            if (!deliveryReferences.Add(normalized.DeliveryReference))
            {
                return Result.Fail(Error.Validation(
                    "OfficialOutputDeliveryLedger.DuplicateDeliveryReference",
                    "Official output delivery ledger contains duplicate delivery references."));
            }
        }

        return Result.Success();
    }

    internal static Result ValidateDeliveryResult(
        OfficialOutputDeliveryResult? result)
    {
        if (result is null || result.Artifact is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDeliveryLedger.InvalidDeliveryResult",
                "Official output delivery ledger delivery result is invalid."));
        }

        Result artifactValidation = ValidateArtifact(result.Artifact);

        if (artifactValidation.IsFailure)
        {
            return artifactValidation;
        }

        if (result.Package is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDeliveryLedger.PackageRequired",
                "Official output delivery ledger requires delivery package identity."));
        }

        if (!string.Equals(
            Normalize(result.Artifact.SourcePackageReference),
            Normalize(result.Package.PackageReference),
            StringComparison.Ordinal) ||
            !string.Equals(
                Normalize(result.Artifact.SourcePackageHash),
                Normalize(result.Package.PackageHash),
                StringComparison.Ordinal))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDeliveryLedger.PackageIdentityMismatch",
                "Official output delivery ledger package identity does not match delivery result package."));
        }

        return Result.Success();
    }

    internal static Result ValidateEntry(
        OfficialOutputDeliveryLedgerEntry? entry)
    {
        if (entry is null || entry.EntryNumber <= 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDeliveryLedger.InvalidEntry",
                "Official output delivery ledger entry is invalid."));
        }

        Result artifactValidation = ValidateArtifactShape(
            entry.DeliveryTarget,
            entry.DeliveryReference,
            entry.DeliveryHash,
            entry.HashAlgorithm,
            entry.AdapterName,
            entry.PackageReference,
            entry.PackageHash,
            entry.EntryCount,
            entry.PayloadCount,
            entry.TotalLineCount,
            entry.SideEffectMarker,
            entry.Metadata);

        if (artifactValidation.IsFailure)
        {
            return artifactValidation;
        }

        if (entry.RequestedByUserId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDeliveryLedger.InvalidRequestedByUserId",
                "Official output delivery ledger requester user id is invalid."));
        }

        return Result.Success();
    }

    private static Result ValidateArtifact(
        OfficialOutputDeliveryArtifact artifact)
    {
        Result artifactValidation = ValidateArtifactShape(
            artifact.Target,
            artifact.DeliveryReference,
            artifact.DeliveryHash,
            artifact.HashAlgorithm,
            artifact.AdapterName,
            artifact.SourcePackageReference,
            artifact.SourcePackageHash,
            artifact.EntryCount,
            artifact.PayloadCount,
            artifact.TotalLineCount,
            artifact.SideEffectMarker,
            artifact.Metadata);

        if (artifactValidation.IsFailure)
        {
            return artifactValidation;
        }

        return Result.Success();
    }

    private static Result ValidateArtifactShape(
        OfficialOutputDeliveryTarget target,
        string? deliveryReference,
        string? deliveryHash,
        string? hashAlgorithm,
        string? adapterName,
        string? packageReference,
        string? packageHash,
        int entryCount,
        int payloadCount,
        int totalLineCount,
        string? sideEffectMarker,
        IReadOnlyDictionary<string, string>? metadata)
    {
        if (!Enum.IsDefined(typeof(OfficialOutputDeliveryTarget), target) ||
            target != OfficialOutputDeliveryTarget.InMemory)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDeliveryLedger.UnsupportedTarget",
                "Official output delivery ledger supports in-memory delivery results only."));
        }

        if (string.IsNullOrWhiteSpace(deliveryReference))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDeliveryLedger.DeliveryReferenceRequired",
                "Official output delivery ledger requires delivery reference."));
        }

        if (string.IsNullOrWhiteSpace(deliveryHash))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDeliveryLedger.DeliveryHashRequired",
                "Official output delivery ledger requires delivery hash."));
        }

        if (!string.Equals(
            Normalize(deliveryReference),
            DeliveryReferencePrefix + Normalize(deliveryHash),
            StringComparison.Ordinal))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDeliveryLedger.DeliveryReferenceMismatch",
                "Official output delivery ledger delivery reference does not match delivery hash."));
        }

        if (string.IsNullOrWhiteSpace(hashAlgorithm))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDeliveryLedger.HashAlgorithmRequired",
                "Official output delivery ledger requires hash algorithm."));
        }

        if (string.IsNullOrWhiteSpace(adapterName))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDeliveryLedger.AdapterNameRequired",
                "Official output delivery ledger requires adapter name."));
        }

        if (string.IsNullOrWhiteSpace(packageReference))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDeliveryLedger.PackageReferenceRequired",
                "Official output delivery ledger requires package reference."));
        }

        if (string.IsNullOrWhiteSpace(packageHash))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDeliveryLedger.PackageHashRequired",
                "Official output delivery ledger requires package hash."));
        }

        if (entryCount < 0 || payloadCount < 0 || totalLineCount < 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDeliveryLedger.InvalidCounts",
                "Official output delivery ledger counts are invalid."));
        }

        if (string.IsNullOrWhiteSpace(sideEffectMarker) ||
            !Normalize(sideEffectMarker).Contains(RequiredDeliveryMarker, StringComparison.Ordinal))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDeliveryLedger.DeliveryMarkerRequired",
                "Official output delivery ledger requires in-memory delivery marker."));
        }

        return ValidateMetadata(metadata);
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

    internal static Result ValidateMetadata(
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
                    "OfficialOutputDeliveryLedger.MetadataKeyRequired",
                    "Official output delivery ledger metadata key is required."));
            }

            if (!keys.Add(key))
            {
                return Result.Fail(Error.Validation(
                    "OfficialOutputDeliveryLedger.DuplicateMetadataKey",
                    "Official output delivery ledger metadata contains duplicate keys after trimming."));
            }
        }

        return Result.Success();
    }

    internal static string FormatMetadata(
        IReadOnlyDictionary<string, string>? metadata)
    {
        IReadOnlyDictionary<string, string> normalized = NormalizeMetadata(metadata);

        return string.Join(" | ", normalized.Select(static pair => pair.Key + "=" + pair.Value));
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
