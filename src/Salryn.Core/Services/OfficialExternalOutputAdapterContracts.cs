using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Salryn.Core.Common;

namespace Salryn.Core.Services;

public enum OfficialExternalOutputChannel
{
    InMemory = 1,
    File = 2,
    Pdf = 3,
    Print = 4,
    Crystal = 5
}

public enum OfficialExternalOutputIntent
{
    ContractOnly = 1
}

public sealed record OfficialExternalOutputCapability(
    OfficialExternalOutputChannel Channel,
    OfficialExternalOutputIntent Intent,
    string Description);

public sealed record OfficialExternalOutputAdapterContract(
    string ContractId,
    string AdapterName,
    IReadOnlyList<OfficialExternalOutputCapability> Capabilities,
    string SideEffectMarker);

public sealed record OfficialExternalOutputDispatchRequest(
    OfficialOutputExportPackage? ExportPackage,
    OfficialOutputDeliveryLedgerPackage? LedgerPackage,
    OfficialExternalOutputChannel Channel,
    OfficialExternalOutputIntent Intent,
    string? RequestedFormat,
    OfficialExternalOutputAdapterContract? AdapterContract,
    DateTimeOffset? RequestedAt,
    Guid? RequestedByUserId,
    string? TerminalId,
    IReadOnlyDictionary<string, string>? Metadata);

public sealed record OfficialExternalOutputDispatchPlanItem(
    int ItemNumber,
    OfficialExternalOutputChannel Channel,
    OfficialExternalOutputIntent Intent,
    string? RequestedFormat,
    string Status,
    string AdapterName,
    string AdapterContractId,
    string PackageReference,
    string PackageHash,
    string LedgerReference,
    string LedgerHash,
    string? ManifestReference,
    string? ManifestHash,
    int EntryCount,
    int PayloadCount,
    int TotalLineCount,
    string SideEffectMarker,
    DateTimeOffset? RequestedAt,
    Guid? RequestedByUserId,
    string? TerminalId,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record OfficialExternalOutputDispatchPlan(
    IReadOnlyList<OfficialExternalOutputDispatchPlanItem> Items,
    int ItemCount,
    string DispatchPlanHash,
    string DispatchPlanReference,
    DateTimeOffset? CreatedAt,
    IReadOnlyDictionary<string, string> Metadata,
    OfficialExternalOutputDispatchPlanTextRenderResult TextResult);

public sealed record OfficialExternalOutputDispatchPlanTextRenderResult(
    IReadOnlyList<string> Lines,
    string Text);

public interface IOfficialExternalOutputAdapter
{
    Task<Result<OfficialExternalOutputDispatchPlan>> PlanAsync(
        OfficialExternalOutputDispatchRequest request,
        CancellationToken cancellationToken = default);
}

// Kirch: why - future official external output needs a Core contract and dispatch plan before any external side effects exist.
public sealed class OfficialExternalOutputDispatchPlanBuilder
{
    private const string DispatchPlanReferencePrefix = "salryn://official-external-output-dispatch-plan/";
    private const string StatusContractOnly = "ContractOnly";

    private readonly OfficialExternalOutputDispatchPlanTextRenderer _textRenderer;

    public OfficialExternalOutputDispatchPlanBuilder(
        OfficialExternalOutputDispatchPlanTextRenderer? textRenderer = null)
    {
        _textRenderer = textRenderer ?? new OfficialExternalOutputDispatchPlanTextRenderer();
    }

    public Result<OfficialExternalOutputDispatchPlan> Build(
        OfficialExternalOutputDispatchRequest? request,
        DateTimeOffset? createdAt = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (request is null)
        {
            return Result<OfficialExternalOutputDispatchPlan>.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.RequestRequired",
                "Official external output dispatch request is required."));
        }

        return Build(new[] { request }, createdAt, metadata);
    }

    public Result<OfficialExternalOutputDispatchPlan> Build(
        IReadOnlyList<OfficialExternalOutputDispatchRequest>? requests,
        DateTimeOffset? createdAt = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        Result validation = OfficialExternalOutputAdapterContractValidator.ValidateRequests(
            requests,
            metadata);

        if (validation.IsFailure)
        {
            return Result<OfficialExternalOutputDispatchPlan>.Fail(validation.ErrorDetail);
        }

        IReadOnlyList<OfficialExternalOutputDispatchPlanItem> items =
            NumberItems(OrderItems(NormalizeItems(BuildItems(requests!))));
        IReadOnlyDictionary<string, string> normalizedMetadata =
            OfficialExternalOutputAdapterContractValidator.NormalizeMetadata(metadata);

        Result<OfficialExternalOutputDispatchPlanTextRenderResult> textResult =
            _textRenderer.Render(items, createdAt, normalizedMetadata);

        if (textResult.IsFailure)
        {
            return Result<OfficialExternalOutputDispatchPlan>.Fail(textResult.ErrorDetail);
        }

        string dispatchPlanHash = OfficialExternalOutputAdapterContractValidator.ComputeContentHash(textResult.Value.Text);
        string dispatchPlanReference = DispatchPlanReferencePrefix + dispatchPlanHash;

        return Result<OfficialExternalOutputDispatchPlan>.Success(
            new OfficialExternalOutputDispatchPlan(
                items,
                items.Count,
                dispatchPlanHash,
                dispatchPlanReference,
                createdAt,
                normalizedMetadata,
                textResult.Value));
    }

    internal static IReadOnlyList<OfficialExternalOutputDispatchPlanItem> BuildItems(
        IReadOnlyList<OfficialExternalOutputDispatchRequest> requests)
    {
        return requests.Select(request =>
        {
            OfficialOutputExportPackage package = request.ExportPackage!;
            OfficialOutputDeliveryLedgerPackage ledger = request.LedgerPackage!;
            OfficialExternalOutputAdapterContract contract = request.AdapterContract!;
            OfficialOutputArtifactManifestPackage manifest = package.ManifestPackage;

            return new OfficialExternalOutputDispatchPlanItem(
                0,
                request.Channel,
                request.Intent,
                request.RequestedFormat,
                StatusContractOnly,
                contract.AdapterName,
                contract.ContractId,
                package.PackageReference,
                package.PackageHash,
                ledger.LedgerReference,
                ledger.LedgerHash,
                manifest.ManifestReference,
                manifest.ManifestHash,
                package.EntryCount,
                package.PayloadCount,
                package.TotalLineCount,
                contract.SideEffectMarker,
                request.RequestedAt,
                request.RequestedByUserId,
                request.TerminalId,
                request.Metadata ?? new Dictionary<string, string>(StringComparer.Ordinal));
        }).ToArray();
    }

    internal static IReadOnlyList<OfficialExternalOutputDispatchPlanItem> NormalizeItems(
        IReadOnlyList<OfficialExternalOutputDispatchPlanItem> items)
    {
        return items.Select(NormalizeItem).ToArray();
    }

    internal static OfficialExternalOutputDispatchPlanItem NormalizeItem(
        OfficialExternalOutputDispatchPlanItem item)
    {
        return item with
        {
            RequestedFormat = NormalizeOptional(item.RequestedFormat),
            Status = Normalize(item.Status),
            AdapterName = Normalize(item.AdapterName),
            AdapterContractId = Normalize(item.AdapterContractId),
            PackageReference = Normalize(item.PackageReference),
            PackageHash = Normalize(item.PackageHash),
            LedgerReference = Normalize(item.LedgerReference),
            LedgerHash = Normalize(item.LedgerHash),
            ManifestReference = NormalizeOptional(item.ManifestReference),
            ManifestHash = NormalizeOptional(item.ManifestHash),
            SideEffectMarker = Normalize(item.SideEffectMarker),
            TerminalId = NormalizeOptional(item.TerminalId),
            Metadata = OfficialExternalOutputAdapterContractValidator.NormalizeMetadata(item.Metadata)
        };
    }

    internal static IReadOnlyList<OfficialExternalOutputDispatchPlanItem> OrderItems(
        IReadOnlyList<OfficialExternalOutputDispatchPlanItem> items)
    {
        return items
            .OrderBy(static item => (int)item.Channel)
            .ThenBy(static item => (int)item.Intent)
            .ThenBy(static item => item.AdapterContractId, StringComparer.Ordinal)
            .ThenBy(static item => item.PackageReference, StringComparer.Ordinal)
            .ThenBy(static item => item.LedgerReference, StringComparer.Ordinal)
            .ToArray();
    }

    internal static IReadOnlyList<OfficialExternalOutputDispatchPlanItem> NumberItems(
        IReadOnlyList<OfficialExternalOutputDispatchPlanItem> items)
    {
        return items
            .Select((item, index) => item with { ItemNumber = index + 1 })
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

public sealed class OfficialExternalOutputDispatchPlanTextRenderer
{
    private const string Title = "SALRYN OFFICIAL EXTERNAL OUTPUT DISPATCH PLAN";
    private const string DispatchPlanReferencePrefix = "salryn://official-external-output-dispatch-plan/";
    private const string HashAlgorithmName = "SHA-256";
    private const string ContractOnlyMarker = "EXTERNAL_OUTPUT_CONTRACT_ONLY_NO_SIDE_EFFECTS";

    public Result<OfficialExternalOutputDispatchPlanTextRenderResult> Render(
        IReadOnlyList<OfficialExternalOutputDispatchPlanItem>? items,
        DateTimeOffset? createdAt = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        Result validation = OfficialExternalOutputAdapterContractValidator.ValidatePlanItems(
            items,
            metadata);

        if (validation.IsFailure)
        {
            return Result<OfficialExternalOutputDispatchPlanTextRenderResult>.Fail(validation.ErrorDetail);
        }

        IReadOnlyList<OfficialExternalOutputDispatchPlanItem> orderedItems =
            OfficialExternalOutputDispatchPlanBuilder.OrderItems(
                OfficialExternalOutputDispatchPlanBuilder.NormalizeItems(items!));
        IReadOnlyDictionary<string, string> normalizedMetadata =
            OfficialExternalOutputAdapterContractValidator.NormalizeMetadata(metadata);

        List<string> lines = new()
        {
            Title,
            "[DISPATCH PLAN]",
            "ItemCount: " + orderedItems.Count.ToString(CultureInfo.InvariantCulture)
        };

        if (createdAt.HasValue)
        {
            lines.Add("CreatedAt: " + Format(createdAt.Value));
        }

        lines.Add("DispatchPlanHash: COMPUTED_IN_DISPATCH_PLAN");
        lines.Add("DispatchPlanReference: COMPUTED_IN_DISPATCH_PLAN");

        string planMetadata = OfficialExternalOutputAdapterContractValidator.FormatMetadata(normalizedMetadata);

        if (planMetadata.Length > 0)
        {
            lines.Add("Metadata: " + planMetadata);
        }

        lines.Add("[CONTRACT]");
        lines.Add("SideEffectMarker: " + ContractOnlyMarker);
        lines.Add("AllowedChannels: InMemory|File|Pdf|Print|Crystal");
        lines.Add("ContractMode: PlannedOnly");
        lines.Add("[ITEMS]");

        foreach (OfficialExternalOutputDispatchPlanItem item in orderedItems)
        {
            lines.Add(RenderItem(item));
        }

        lines.Add("[EVIDENCE]");
        lines.Add("HashAlgorithm: " + HashAlgorithmName);
        lines.Add("DispatchPlanReferencePrefix: " + DispatchPlanReferencePrefix);
        lines.Add("ExternalOutputSideEffectMarker: " + ContractOnlyMarker);

        return Result<OfficialExternalOutputDispatchPlanTextRenderResult>.Success(
            new OfficialExternalOutputDispatchPlanTextRenderResult(
                lines,
                string.Join("\n", lines)));
    }

    private static string RenderItem(
        OfficialExternalOutputDispatchPlanItem item)
    {
        List<(string Name, string Value)> fields = new()
        {
            ("ItemNumber", item.ItemNumber.ToString(CultureInfo.InvariantCulture)),
            ("Channel", item.Channel.ToString()),
            ("Intent", item.Intent.ToString()),
            ("Status", item.Status),
            ("AdapterName", item.AdapterName),
            ("AdapterContractId", item.AdapterContractId),
            ("PackageReference", item.PackageReference),
            ("PackageHash", item.PackageHash),
            ("LedgerReference", item.LedgerReference),
            ("LedgerHash", item.LedgerHash)
        };

        AddOptional(fields, "RequestedFormat", item.RequestedFormat);
        AddOptional(fields, "ManifestReference", item.ManifestReference);
        AddOptional(fields, "ManifestHash", item.ManifestHash);
        fields.Add(("EntryCount", item.EntryCount.ToString(CultureInfo.InvariantCulture)));
        fields.Add(("PayloadCount", item.PayloadCount.ToString(CultureInfo.InvariantCulture)));
        fields.Add(("TotalLineCount", item.TotalLineCount.ToString(CultureInfo.InvariantCulture)));
        fields.Add(("SideEffectMarker", item.SideEffectMarker));
        AddOptional(fields, "RequestedAt", item.RequestedAt);
        AddOptional(fields, "RequestedByUserId", item.RequestedByUserId);
        AddOptional(fields, "TerminalId", item.TerminalId);

        string metadata = OfficialExternalOutputAdapterContractValidator.FormatMetadata(item.Metadata);

        if (metadata.Length > 0)
        {
            fields.Add(("Metadata", metadata));
        }

        return "DispatchItem: " + string.Join(" | ", fields.Select(static field => field.Name + "=" + field.Value));
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

public static class OfficialExternalOutputAdapterContractValidator
{
    public const string ContractOnlySideEffectMarker = "EXTERNAL_OUTPUT_CONTRACT_ONLY_NO_SIDE_EFFECTS";

    private const string PackageReferencePrefix = "salryn://official-output-export-package/";
    private const string LedgerReferencePrefix = "salryn://official-output-delivery-ledger/";
    private const string LedgerMarker = "DELIVERY_LEDGER_IN_MEMORY_ONLY_NO_PERSISTENCE";

    public static Result ValidateRequests(
        IReadOnlyList<OfficialExternalOutputDispatchRequest>? requests,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (requests is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.RequestsRequired",
                "Official external output dispatch requests are required."));
        }

        if (requests.Count == 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.RequestRequired",
                "Official external output dispatch requires at least one request."));
        }

        Result metadataValidation = ValidateMetadata(metadata);

        if (metadataValidation.IsFailure)
        {
            return metadataValidation;
        }

        foreach (OfficialExternalOutputDispatchRequest request in requests)
        {
            Result validation = ValidateRequest(request);

            if (validation.IsFailure)
            {
                return validation;
            }
        }

        return Result.Success();
    }

    public static Result ValidateRequest(
        OfficialExternalOutputDispatchRequest? request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.RequestRequired",
                "Official external output dispatch request is required."));
        }

        Result packageValidation = ValidatePackage(request.ExportPackage);

        if (packageValidation.IsFailure)
        {
            return packageValidation;
        }

        Result ledgerValidation = ValidateLedger(request.LedgerPackage);

        if (ledgerValidation.IsFailure)
        {
            return ledgerValidation;
        }

        Result channelValidation = ValidateChannel(request.Channel);

        if (channelValidation.IsFailure)
        {
            return channelValidation;
        }

        if (!Enum.IsDefined(typeof(OfficialExternalOutputIntent), request.Intent))
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.UnsupportedIntent",
                "Official external output dispatch intent is unsupported."));
        }

        if (request.RequestedByUserId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.InvalidRequestedByUserId",
                "Official external output dispatch requester user id is invalid."));
        }

        Result contractValidation = ValidateContract(request.AdapterContract);

        if (contractValidation.IsFailure)
        {
            return contractValidation;
        }

        if (!Supports(request.AdapterContract!, request.Channel, request.Intent))
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.UnsupportedAdapterCapability",
                "Official external output adapter contract does not support the requested channel and intent."));
        }

        Result identityValidation = ValidatePackageLedgerIdentity(
            request.ExportPackage!,
            request.LedgerPackage!);

        if (identityValidation.IsFailure)
        {
            return identityValidation;
        }

        return ValidateMetadata(request.Metadata);
    }

    public static Result ValidateContract(
        OfficialExternalOutputAdapterContract? contract)
    {
        if (contract is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.AdapterContractRequired",
                "Official external output adapter contract is required."));
        }

        if (string.IsNullOrWhiteSpace(contract.ContractId))
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.AdapterContractIdRequired",
                "Official external output adapter contract id is required."));
        }

        if (string.IsNullOrWhiteSpace(contract.AdapterName))
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.AdapterNameRequired",
                "Official external output adapter name is required."));
        }

        if (string.IsNullOrWhiteSpace(contract.SideEffectMarker) ||
            !Normalize(contract.SideEffectMarker).Contains(ContractOnlySideEffectMarker, StringComparison.Ordinal))
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.ContractMarkerRequired",
                "Official external output adapter contract marker is required."));
        }

        if (contract.Capabilities is null || contract.Capabilities.Count == 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.CapabilityRequired",
                "Official external output adapter contract requires capabilities."));
        }

        HashSet<string> capabilityKeys = new(StringComparer.Ordinal);

        foreach (OfficialExternalOutputCapability capability in contract.Capabilities)
        {
            Result capabilityValidation = ValidateCapability(capability);

            if (capabilityValidation.IsFailure)
            {
                return capabilityValidation;
            }

            string key = ((int)capability.Channel).ToString(CultureInfo.InvariantCulture) + "|" +
                ((int)capability.Intent).ToString(CultureInfo.InvariantCulture);

            if (!capabilityKeys.Add(key))
            {
                return Result.Fail(Error.Validation(
                    "OfficialExternalOutputDispatch.DuplicateCapability",
                    "Official external output adapter contract contains duplicate capabilities."));
            }
        }

        return Result.Success();
    }

    public static Result ValidatePlanItems(
        IReadOnlyList<OfficialExternalOutputDispatchPlanItem>? items,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (items is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.ItemsRequired",
                "Official external output dispatch plan items are required."));
        }

        if (items.Count == 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.ItemRequired",
                "Official external output dispatch plan requires at least one item."));
        }

        Result metadataValidation = ValidateMetadata(metadata);

        if (metadataValidation.IsFailure)
        {
            return metadataValidation;
        }

        HashSet<int> itemNumbers = new();

        foreach (OfficialExternalOutputDispatchPlanItem item in items)
        {
            if (item is null || item.ItemNumber <= 0)
            {
                return Result.Fail(Error.Validation(
                    "OfficialExternalOutputDispatch.InvalidItem",
                    "Official external output dispatch plan item is invalid."));
            }

            if (!itemNumbers.Add(item.ItemNumber))
            {
                return Result.Fail(Error.Validation(
                    "OfficialExternalOutputDispatch.DuplicateItemNumber",
                    "Official external output dispatch plan contains duplicate item numbers."));
            }

            Result itemValidation = ValidatePlanItemShape(item);

            if (itemValidation.IsFailure)
            {
                return itemValidation;
            }
        }

        return Result.Success();
    }

    private static Result ValidatePlanItemShape(
        OfficialExternalOutputDispatchPlanItem item)
    {
        Result channelValidation = ValidateChannel(item.Channel);

        if (channelValidation.IsFailure)
        {
            return channelValidation;
        }

        if (!Enum.IsDefined(typeof(OfficialExternalOutputIntent), item.Intent))
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.UnsupportedIntent",
                "Official external output dispatch intent is unsupported."));
        }

        if (string.IsNullOrWhiteSpace(item.Status) ||
            !string.Equals(item.Status.Trim(), "ContractOnly", StringComparison.Ordinal))
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.ContractOnlyStatusRequired",
                "Official external output dispatch plan item must remain contract-only."));
        }

        if (string.IsNullOrWhiteSpace(item.AdapterName) ||
            string.IsNullOrWhiteSpace(item.AdapterContractId) ||
            string.IsNullOrWhiteSpace(item.PackageReference) ||
            string.IsNullOrWhiteSpace(item.PackageHash) ||
            string.IsNullOrWhiteSpace(item.LedgerReference) ||
            string.IsNullOrWhiteSpace(item.LedgerHash))
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.ItemIdentityRequired",
                "Official external output dispatch plan item identity is required."));
        }

        if (item.EntryCount < 0 || item.PayloadCount < 0 || item.TotalLineCount < 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.InvalidCounts",
                "Official external output dispatch plan counts are invalid."));
        }

        if (string.IsNullOrWhiteSpace(item.SideEffectMarker) ||
            !Normalize(item.SideEffectMarker).Contains(ContractOnlySideEffectMarker, StringComparison.Ordinal))
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.ContractMarkerRequired",
                "Official external output dispatch plan item marker is required."));
        }

        return ValidateMetadata(item.Metadata);
    }

    private static Result ValidatePackage(
        OfficialOutputExportPackage? package)
    {
        if (package is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.ExportPackageRequired",
                "Official external output dispatch requires an official export package."));
        }

        if (string.IsNullOrWhiteSpace(package.PackageReference))
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.PackageReferenceRequired",
                "Official external output dispatch requires package reference."));
        }

        if (string.IsNullOrWhiteSpace(package.PackageHash))
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.PackageHashRequired",
                "Official external output dispatch requires package hash."));
        }

        if (!string.Equals(
            Normalize(package.PackageReference),
            PackageReferencePrefix + Normalize(package.PackageHash),
            StringComparison.Ordinal))
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.PackageReferenceMismatch",
                "Official external output dispatch package reference does not match package hash."));
        }

        if (package.ManifestPackage is null ||
            string.IsNullOrWhiteSpace(package.ManifestPackage.ManifestReference) ||
            string.IsNullOrWhiteSpace(package.ManifestPackage.ManifestHash))
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.ManifestIdentityRequired",
                "Official external output dispatch requires manifest identity."));
        }

        if (package.EntryCount < 0 || package.PayloadCount < 0 || package.TotalLineCount < 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.InvalidPackageCounts",
                "Official external output dispatch package counts are invalid."));
        }

        return Result.Success();
    }

    private static Result ValidateLedger(
        OfficialOutputDeliveryLedgerPackage? ledger)
    {
        if (ledger is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.LedgerPackageRequired",
                "Official external output dispatch requires a delivery ledger package."));
        }

        if (string.IsNullOrWhiteSpace(ledger.LedgerReference))
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.LedgerReferenceRequired",
                "Official external output dispatch requires ledger reference."));
        }

        if (string.IsNullOrWhiteSpace(ledger.LedgerHash))
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.LedgerHashRequired",
                "Official external output dispatch requires ledger hash."));
        }

        if (!string.Equals(
            Normalize(ledger.LedgerReference),
            LedgerReferencePrefix + Normalize(ledger.LedgerHash),
            StringComparison.Ordinal))
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.LedgerReferenceMismatch",
                "Official external output dispatch ledger reference does not match ledger hash."));
        }

        if (ledger.Entries is null || ledger.Entries.Count == 0 || ledger.EntryCount != ledger.Entries.Count)
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.InvalidLedgerEntries",
                "Official external output dispatch requires ledger entries."));
        }

        if (ledger.TextResult is null ||
            string.IsNullOrWhiteSpace(ledger.TextResult.Text) ||
            !ledger.TextResult.Text.Contains(LedgerMarker, StringComparison.Ordinal))
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.LedgerMarkerRequired",
                "Official external output dispatch requires in-memory delivery ledger marker."));
        }

        return Result.Success();
    }

    private static Result ValidatePackageLedgerIdentity(
        OfficialOutputExportPackage package,
        OfficialOutputDeliveryLedgerPackage ledger)
    {
        foreach (OfficialOutputDeliveryLedgerEntry entry in ledger.Entries)
        {
            if (!string.Equals(Normalize(entry.PackageReference), Normalize(package.PackageReference), StringComparison.Ordinal) ||
                !string.Equals(Normalize(entry.PackageHash), Normalize(package.PackageHash), StringComparison.Ordinal))
            {
                return Result.Fail(Error.Validation(
                    "OfficialExternalOutputDispatch.PackageLedgerMismatch",
                    "Official external output dispatch package and ledger package identity do not match."));
            }

            if (!string.IsNullOrWhiteSpace(entry.ManifestReference) &&
                !string.Equals(Normalize(entry.ManifestReference), Normalize(package.ManifestPackage.ManifestReference), StringComparison.Ordinal))
            {
                return Result.Fail(Error.Validation(
                    "OfficialExternalOutputDispatch.ManifestLedgerMismatch",
                    "Official external output dispatch manifest and ledger manifest identity do not match."));
            }

            if (!string.IsNullOrWhiteSpace(entry.ManifestHash) &&
                !string.Equals(Normalize(entry.ManifestHash), Normalize(package.ManifestPackage.ManifestHash), StringComparison.Ordinal))
            {
                return Result.Fail(Error.Validation(
                    "OfficialExternalOutputDispatch.ManifestLedgerMismatch",
                    "Official external output dispatch manifest and ledger manifest identity do not match."));
            }
        }

        return Result.Success();
    }

    private static Result ValidateCapability(
        OfficialExternalOutputCapability capability)
    {
        Result channelValidation = ValidateChannel(capability.Channel);

        if (channelValidation.IsFailure)
        {
            return channelValidation;
        }

        if (!Enum.IsDefined(typeof(OfficialExternalOutputIntent), capability.Intent))
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.UnsupportedIntent",
                "Official external output dispatch capability intent is unsupported."));
        }

        if (string.IsNullOrWhiteSpace(capability.Description))
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.CapabilityDescriptionRequired",
                "Official external output dispatch capability description is required."));
        }

        return Result.Success();
    }

    private static Result ValidateChannel(
        OfficialExternalOutputChannel channel)
    {
        if (!Enum.IsDefined(typeof(OfficialExternalOutputChannel), channel))
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputDispatch.UnsupportedChannel",
                "Official external output dispatch channel is unsupported."));
        }

        return Result.Success();
    }

    private static bool Supports(
        OfficialExternalOutputAdapterContract contract,
        OfficialExternalOutputChannel channel,
        OfficialExternalOutputIntent intent)
    {
        return contract.Capabilities.Any(capability =>
            capability.Channel == channel && capability.Intent == intent);
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
                    "OfficialExternalOutputDispatch.MetadataKeyRequired",
                    "Official external output dispatch metadata key is required."));
            }

            if (!keys.Add(key))
            {
                return Result.Fail(Error.Validation(
                    "OfficialExternalOutputDispatch.DuplicateMetadataKey",
                    "Official external output dispatch metadata contains duplicate keys after trimming."));
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
