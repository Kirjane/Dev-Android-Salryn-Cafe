using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Salryn.Core.Common;

namespace Salryn.Core.Services;

public enum OfficialOutputDeliveryTarget
{
    InMemory = 1
}

public sealed record OfficialOutputDeliveryRequest(
    OfficialOutputExportPackage? Package,
    OfficialOutputDeliveryTarget Target,
    Guid? RequestedByUserId,
    DateTimeOffset? RequestedAt,
    string? TerminalId,
    IReadOnlyDictionary<string, string>? Metadata);

public sealed record OfficialOutputDeliveryArtifact(
    OfficialOutputDeliveryTarget Target,
    string DeliveryReference,
    string DeliveryHash,
    string HashAlgorithm,
    string AdapterName,
    string SourcePackageReference,
    string SourcePackageHash,
    string? SourceManifestReference,
    string? SourceManifestHash,
    int EntryCount,
    int PayloadCount,
    int TotalLineCount,
    string SideEffectMarker,
    DateTimeOffset? DeliveredAt,
    string? TerminalId,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record OfficialOutputDeliveryResult(
    OfficialOutputDeliveryArtifact Artifact,
    OfficialOutputExportPackage Package,
    OfficialOutputDeliveryAdapterContract AdapterContract);

public sealed record OfficialOutputDeliveryAdapterContract(
    string AdapterName,
    IReadOnlyList<OfficialOutputDeliveryTarget> SupportedTargets,
    string SideEffectMarker);

public interface IOfficialOutputDeliveryAdapter
{
    Task<Result<OfficialOutputDeliveryResult>> DeliverAsync(
        OfficialOutputDeliveryRequest request,
        CancellationToken cancellationToken = default);
}

// Kirch: why - delivery adapters must prove the post-09L boundary without creating devices, templates, or external output.
public static class OfficialOutputDeliveryAdapterValidator
{
    private const string ExportPackageReferencePrefix = "salryn://official-output-export-package/";

    public static Result ValidateContract(
        OfficialOutputDeliveryAdapterContract? contract)
    {
        if (contract is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDelivery.ContractRequired",
                "Official output delivery adapter contract is required."));
        }

        if (string.IsNullOrWhiteSpace(contract.AdapterName))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDelivery.AdapterNameRequired",
                "Official output delivery adapter name is required."));
        }

        if (string.IsNullOrWhiteSpace(contract.SideEffectMarker))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDelivery.SideEffectMarkerRequired",
                "Official output delivery adapter side-effect marker is required."));
        }

        if (contract.SupportedTargets is null || contract.SupportedTargets.Count == 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDelivery.TargetsRequired",
                "Official output delivery adapter must declare supported targets."));
        }

        HashSet<OfficialOutputDeliveryTarget> targets = new();

        foreach (OfficialOutputDeliveryTarget target in contract.SupportedTargets)
        {
            if (!Enum.IsDefined(typeof(OfficialOutputDeliveryTarget), target) ||
                target != OfficialOutputDeliveryTarget.InMemory)
            {
                return Result.Fail(Error.Validation(
                    "OfficialOutputDelivery.UnsupportedTarget",
                    "Official output delivery target is unsupported."));
            }

            if (!targets.Add(target))
            {
                return Result.Fail(Error.Validation(
                    "OfficialOutputDelivery.DuplicateTarget",
                    "Official output delivery adapter contains duplicate targets."));
            }
        }

        return Result.Success();
    }

    public static Result ValidateRequest(
        OfficialOutputDeliveryRequest? request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDelivery.RequestRequired",
                "Official output delivery request is required."));
        }

        if (!Enum.IsDefined(typeof(OfficialOutputDeliveryTarget), request.Target) ||
            request.Target != OfficialOutputDeliveryTarget.InMemory)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDelivery.UnsupportedTarget",
                "Official output delivery target is unsupported."));
        }

        if (request.RequestedByUserId.HasValue &&
            request.RequestedByUserId.Value == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDelivery.InvalidRequestedByUserId",
                "Official output delivery requester user id is invalid."));
        }

        Result packageValidation = ValidatePackage(request.Package);

        if (packageValidation.IsFailure)
        {
            return packageValidation;
        }

        return ValidateMetadata(request.Metadata);
    }

    public static OfficialOutputDeliveryRequest NormalizeRequest(
        OfficialOutputDeliveryRequest request)
    {
        return request with
        {
            TerminalId = NormalizeOptional(request.TerminalId),
            Metadata = NormalizeMetadata(request.Metadata)
        };
    }

    internal static Result ValidatePackage(
        OfficialOutputExportPackage? package)
    {
        if (package is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDelivery.PackageRequired",
                "Official output delivery requires an official output export package."));
        }

        if (!Enum.IsDefined(typeof(OfficialOutputExportPackageKind), package.PackageKind))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDelivery.InvalidPackageKind",
                "Official output delivery package kind is invalid."));
        }

        if (package.ManifestPackage is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDelivery.ManifestRequired",
                "Official output delivery requires a manifest package."));
        }

        if (package.Items is null || package.Items.Count == 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDelivery.ItemsRequired",
                "Official output delivery requires export package items."));
        }

        if (package.TextResult is null ||
            package.TextResult.Lines is null ||
            string.IsNullOrWhiteSpace(package.TextResult.Text))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDelivery.PackageTextRequired",
                "Official output delivery requires package text."));
        }

        if (string.IsNullOrWhiteSpace(package.PackageHash))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDelivery.PackageHashRequired",
                "Official output delivery requires a package hash."));
        }

        if (string.IsNullOrWhiteSpace(package.PackageReference))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDelivery.PackageReferenceRequired",
                "Official output delivery requires a package reference."));
        }

        if (!string.Equals(
            package.PackageHash.Trim(),
            ComputeContentHash(package.TextResult.Text),
            StringComparison.Ordinal))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDelivery.PackageHashMismatch",
                "Official output delivery package hash does not match package text."));
        }

        if (!string.Equals(
            package.PackageReference.Trim(),
            ExportPackageReferencePrefix + package.PackageHash.Trim(),
            StringComparison.Ordinal))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDelivery.PackageReferenceMismatch",
                "Official output delivery package reference does not match package hash."));
        }

        if (string.IsNullOrWhiteSpace(package.ManifestPackage.ManifestHash) ||
            string.IsNullOrWhiteSpace(package.ManifestPackage.ManifestReference))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDelivery.ManifestIdentityRequired",
                "Official output delivery requires manifest identity."));
        }

        if (package.EntryCount != package.ManifestPackage.EntryCount)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDelivery.EntryCountMismatch",
                "Official output delivery package entry count does not match manifest entry count."));
        }

        if (package.PayloadCount != package.Items.Count)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDelivery.PayloadCountMismatch",
                "Official output delivery package payload count does not match package items."));
        }

        int totalLineCount = 0;

        foreach (OfficialOutputExportPackageItem item in package.Items)
        {
            if (item is null || item.ManifestEntry is null || item.Payload is null)
            {
                return Result.Fail(Error.Validation(
                    "OfficialOutputDelivery.InvalidItem",
                    "Official output delivery package item is invalid."));
            }

            totalLineCount += item.Payload.LineCount;
        }

        if (package.TotalLineCount != totalLineCount)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputDelivery.TotalLineCountMismatch",
                "Official output delivery package total line count does not match package items."));
        }

        Result exportValidation = OfficialOutputExportPackageBuilder.Validate(
            package.ManifestPackage,
            package.Items.Select(static item => item.Payload).ToArray());

        if (exportValidation.IsFailure)
        {
            return exportValidation;
        }

        return Result.Success();
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
                    "OfficialOutputDelivery.MetadataKeyRequired",
                    "Official output delivery metadata key is required."));
            }

            if (!keys.Add(key))
            {
                return Result.Fail(Error.Validation(
                    "OfficialOutputDelivery.DuplicateMetadataKey",
                    "Official output delivery metadata contains duplicate keys after trimming."));
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

public sealed class OfficialInMemoryOutputDeliveryAdapter : IOfficialOutputDeliveryAdapter
{
    public const string DeliverySideEffectMarker = "DELIVERY_IN_MEMORY_ONLY_NO_EXTERNAL_OUTPUT";

    private const string AdapterName = nameof(OfficialInMemoryOutputDeliveryAdapter);
    private const string DeliveryReferencePrefix = "salryn://official-output-delivery/in-memory/";
    private const string HashAlgorithmName = "SHA-256";
    private const string Title = "SALRYN OFFICIAL OUTPUT DELIVERY RECEIPT";

    public static OfficialOutputDeliveryAdapterContract AdapterContract { get; } = new(
        AdapterName,
        new[]
        {
            OfficialOutputDeliveryTarget.InMemory
        },
        DeliverySideEffectMarker);

    public Task<Result<OfficialOutputDeliveryResult>> DeliverAsync(
        OfficialOutputDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result contractValidation =
            OfficialOutputDeliveryAdapterValidator.ValidateContract(AdapterContract);

        if (contractValidation.IsFailure)
        {
            return Task.FromResult(Result<OfficialOutputDeliveryResult>.Fail(contractValidation.ErrorDetail));
        }

        Result requestValidation =
            OfficialOutputDeliveryAdapterValidator.ValidateRequest(request);

        if (requestValidation.IsFailure)
        {
            return Task.FromResult(Result<OfficialOutputDeliveryResult>.Fail(requestValidation.ErrorDetail));
        }

        OfficialOutputDeliveryRequest normalizedRequest =
            OfficialOutputDeliveryAdapterValidator.NormalizeRequest(request);
        OfficialOutputExportPackage package = normalizedRequest.Package!;

        string deliveryReceiptText = RenderDeliveryReceipt(normalizedRequest, package);
        string deliveryHash = OfficialOutputDeliveryAdapterValidator.ComputeContentHash(deliveryReceiptText);
        string deliveryReference = DeliveryReferencePrefix + deliveryHash;
        IReadOnlyDictionary<string, string> metadata =
            OfficialOutputDeliveryAdapterValidator.NormalizeMetadata(normalizedRequest.Metadata);

        OfficialOutputDeliveryArtifact artifact = new(
            OfficialOutputDeliveryTarget.InMemory,
            deliveryReference,
            deliveryHash,
            HashAlgorithmName,
            AdapterContract.AdapterName,
            package.PackageReference.Trim(),
            package.PackageHash.Trim(),
            package.ManifestPackage.ManifestReference.Trim(),
            package.ManifestPackage.ManifestHash.Trim(),
            package.EntryCount,
            package.PayloadCount,
            package.TotalLineCount,
            DeliverySideEffectMarker,
            normalizedRequest.RequestedAt,
            normalizedRequest.TerminalId,
            metadata);

        OfficialOutputDeliveryResult result = new(
            artifact,
            package,
            AdapterContract);

        return Task.FromResult(Result<OfficialOutputDeliveryResult>.Success(result));
    }

    private static string RenderDeliveryReceipt(
        OfficialOutputDeliveryRequest request,
        OfficialOutputExportPackage package)
    {
        List<string> lines = new()
        {
            Title,
            "[DELIVERY]",
            "AdapterName: " + AdapterContract.AdapterName,
            "DeliveryTarget: " + OfficialOutputDeliveryTarget.InMemory,
            "DeliveredAt: " + FormatOptional(request.RequestedAt),
            "TerminalId: " + (request.TerminalId ?? string.Empty),
            "SourcePackageReference: " + package.PackageReference.Trim(),
            "SourcePackageHash: " + package.PackageHash.Trim(),
            "SourceManifestReference: " + package.ManifestPackage.ManifestReference.Trim(),
            "SourceManifestHash: " + package.ManifestPackage.ManifestHash.Trim(),
            "EntryCount: " + package.EntryCount.ToString(CultureInfo.InvariantCulture),
            "PayloadCount: " + package.PayloadCount.ToString(CultureInfo.InvariantCulture),
            "TotalLineCount: " + package.TotalLineCount.ToString(CultureInfo.InvariantCulture),
            "Metadata: " + OfficialOutputDeliveryAdapterValidator.FormatMetadata(request.Metadata),
            "[EVIDENCE]",
            "HashAlgorithm: " + HashAlgorithmName,
            "DeliveryReferencePrefix: " + DeliveryReferencePrefix,
            "DeliveryHash: COMPUTED_IN_DELIVERY_RESULT",
            "DeliveryReference: COMPUTED_IN_DELIVERY_RESULT",
            "OutputSideEffectMarker: " + DeliverySideEffectMarker
        };

        return string.Join("\n", lines);
    }

    private static string FormatOptional(DateTimeOffset? value)
    {
        return value.HasValue
            ? value.Value.ToString("O", CultureInfo.InvariantCulture)
            : string.Empty;
    }
}
