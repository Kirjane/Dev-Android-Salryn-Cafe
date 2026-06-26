using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Salryn.Core.Common;

namespace Salryn.Core.Services;

public enum OfficialExternalOutputExecutionStatus
{
    Succeeded = 1,
    Failed = 2
}

public enum OfficialExternalOutputWrittenArtifactRole
{
    Manifest = 1,
    Package = 2,
    Ledger = 3,
    DispatchPlan = 4,
    Payload = 5,
    ExecutionReceipt = 6
}

public sealed record OfficialExternalOutputWrittenArtifact(
    OfficialExternalOutputWrittenArtifactRole Role,
    string RelativePath,
    string ContentHash,
    string HashAlgorithm,
    int LineCount,
    long ByteCount,
    string? ArtifactReference,
    string? ArtifactLabel,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record OfficialExternalOutputExecutionRequest(
    OfficialExternalOutputDispatchPlan? DispatchPlan,
    OfficialOutputExportPackage? ExportPackage,
    OfficialOutputDeliveryLedgerPackage? DeliveryLedgerPackage,
    string? OutputRootPath,
    DateTimeOffset? RequestedAt,
    Guid? RequestedByUserId,
    string? TerminalId,
    IReadOnlyDictionary<string, string>? Metadata);

public sealed record OfficialExternalOutputExecutionResult(
    OfficialExternalOutputExecutionStatus Status,
    OfficialExternalOutputChannel Channel,
    string AdapterName,
    string DispatchPlanReference,
    string DispatchPlanHash,
    string PackageReference,
    string PackageHash,
    string LedgerReference,
    string LedgerHash,
    string? ManifestReference,
    string? ManifestHash,
    IReadOnlyList<OfficialExternalOutputWrittenArtifact> WrittenArtifacts,
    int WrittenFileCount,
    int TotalLineCount,
    string ExecutionHash,
    string ExecutionReference,
    DateTimeOffset? ExecutedAt,
    Guid? RequestedByUserId,
    string? TerminalId,
    string SideEffectMarker,
    IReadOnlyDictionary<string, string> Metadata,
    OfficialExternalOutputExecutionTextRenderResult TextResult);

public sealed record OfficialExternalOutputExecutionTextRenderResult(
    IReadOnlyList<string> Lines,
    string Text);

public interface IOfficialExternalOutputExecutionAdapter
{
    Task<Result<OfficialExternalOutputExecutionResult>> ExecuteAsync(
        OfficialExternalOutputExecutionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class OfficialExternalOutputExecutionReceiptBuilder
{
    private const string ExecutionReferencePrefix = "salryn://official-external-output-execution/";

    private readonly OfficialExternalOutputExecutionReceiptTextRenderer _textRenderer;

    public OfficialExternalOutputExecutionReceiptBuilder(
        OfficialExternalOutputExecutionReceiptTextRenderer? textRenderer = null)
    {
        _textRenderer = textRenderer ?? new OfficialExternalOutputExecutionReceiptTextRenderer();
    }

    public Result<OfficialExternalOutputExecutionResult> Build(
        OfficialExternalOutputExecutionRequest? request,
        IReadOnlyList<OfficialExternalOutputWrittenArtifact>? writtenArtifacts,
        string adapterName,
        DateTimeOffset? executedAt = null)
    {
        Result validation = OfficialExternalOutputExecutionValidator.ValidateReceiptInput(
            request,
            writtenArtifacts,
            adapterName);

        if (validation.IsFailure)
        {
            return Result<OfficialExternalOutputExecutionResult>.Fail(validation.ErrorDetail);
        }

        OfficialExternalOutputExecutionRequest normalizedRequest =
            OfficialExternalOutputExecutionValidator.NormalizeRequest(request!);
        IReadOnlyList<OfficialExternalOutputWrittenArtifact> artifacts =
            OfficialExternalOutputExecutionValidator.NormalizeArtifacts(writtenArtifacts!);
        string normalizedAdapterName = adapterName.Trim();

        Result<OfficialExternalOutputExecutionTextRenderResult> textResult = _textRenderer.Render(
            normalizedRequest,
            artifacts,
            normalizedAdapterName,
            executedAt);

        if (textResult.IsFailure)
        {
            return Result<OfficialExternalOutputExecutionResult>.Fail(textResult.ErrorDetail);
        }

        string executionHash = OfficialExternalOutputExecutionValidator.ComputeContentHash(textResult.Value.Text);
        string executionReference = ExecutionReferencePrefix + executionHash;
        OfficialExternalOutputDispatchPlan plan = normalizedRequest.DispatchPlan!;
        OfficialOutputExportPackage package = normalizedRequest.ExportPackage!;
        OfficialOutputDeliveryLedgerPackage ledger = normalizedRequest.DeliveryLedgerPackage!;
        OfficialExternalOutputDispatchPlanItem fileItem = plan.Items.Single(static item => item.Channel == OfficialExternalOutputChannel.File);

        return Result<OfficialExternalOutputExecutionResult>.Success(
            new OfficialExternalOutputExecutionResult(
                OfficialExternalOutputExecutionStatus.Succeeded,
                OfficialExternalOutputChannel.File,
                normalizedAdapterName,
                plan.DispatchPlanReference,
                plan.DispatchPlanHash,
                package.PackageReference,
                package.PackageHash,
                ledger.LedgerReference,
                ledger.LedgerHash,
                fileItem.ManifestReference,
                fileItem.ManifestHash,
                artifacts,
                artifacts.Count + 1,
                artifacts.Sum(static artifact => artifact.LineCount),
                executionHash,
                executionReference,
                executedAt,
                normalizedRequest.RequestedByUserId,
                normalizedRequest.TerminalId,
                OfficialExternalOutputExecutionValidator.TextFileWriteConfirmedMarker,
                OfficialExternalOutputExecutionValidator.NormalizeMetadata(normalizedRequest.Metadata),
                textResult.Value));
    }
}

public sealed class OfficialExternalOutputExecutionReceiptTextRenderer
{
    private const string Title = "SALRYN OFFICIAL EXTERNAL OUTPUT EXECUTION RECEIPT";
    private const string HashAlgorithmName = "SHA-256";
    private const string ExecutionReferencePrefix = "salryn://official-external-output-execution/";
    private const string ExecutionReceiptRelativePath = "official-output/{packageHash}/execution-receipt.txt";

    public Result<OfficialExternalOutputExecutionTextRenderResult> Render(
        OfficialExternalOutputExecutionRequest? request,
        IReadOnlyList<OfficialExternalOutputWrittenArtifact>? writtenArtifacts,
        string adapterName,
        DateTimeOffset? executedAt = null)
    {
        Result validation = OfficialExternalOutputExecutionValidator.ValidateReceiptInput(
            request,
            writtenArtifacts,
            adapterName);

        if (validation.IsFailure)
        {
            return Result<OfficialExternalOutputExecutionTextRenderResult>.Fail(validation.ErrorDetail);
        }

        OfficialExternalOutputExecutionRequest normalizedRequest =
            OfficialExternalOutputExecutionValidator.NormalizeRequest(request!);
        IReadOnlyList<OfficialExternalOutputWrittenArtifact> artifacts =
            OfficialExternalOutputExecutionValidator.NormalizeArtifacts(writtenArtifacts!);
        OfficialExternalOutputDispatchPlan plan = normalizedRequest.DispatchPlan!;
        OfficialOutputExportPackage package = normalizedRequest.ExportPackage!;
        OfficialOutputDeliveryLedgerPackage ledger = normalizedRequest.DeliveryLedgerPackage!;
        OfficialExternalOutputDispatchPlanItem fileItem = plan.Items.Single(static item => item.Channel == OfficialExternalOutputChannel.File);

        List<string> lines = new()
        {
            Title,
            "[EXECUTION]",
            "Status: " + OfficialExternalOutputExecutionStatus.Succeeded,
            "Channel: " + OfficialExternalOutputChannel.File,
            "AdapterName: " + adapterName.Trim(),
            "ExecutedAt: " + Format(executedAt),
            "DispatchPlanReference: " + plan.DispatchPlanReference.Trim(),
            "DispatchPlanHash: " + plan.DispatchPlanHash.Trim(),
            "PackageReference: " + package.PackageReference.Trim(),
            "PackageHash: " + package.PackageHash.Trim(),
            "LedgerReference: " + ledger.LedgerReference.Trim(),
            "LedgerHash: " + ledger.LedgerHash.Trim(),
            "ManifestReference: " + (fileItem.ManifestReference ?? string.Empty),
            "ManifestHash: " + (fileItem.ManifestHash ?? string.Empty),
            "WrittenFileCount: " + (artifacts.Count + 1).ToString(CultureInfo.InvariantCulture),
            "TotalLineCount: " + artifacts.Sum(static artifact => artifact.LineCount).ToString(CultureInfo.InvariantCulture),
            "ExecutionHash: COMPUTED_IN_EXECUTION_RESULT",
            "ExecutionReference: COMPUTED_IN_EXECUTION_RESULT"
        };

        string metadata = OfficialExternalOutputExecutionValidator.FormatMetadata(normalizedRequest.Metadata);

        if (metadata.Length > 0)
        {
            lines.Add("Metadata: " + metadata);
        }

        lines.Add("[FILES]");

        foreach (OfficialExternalOutputWrittenArtifact artifact in artifacts.OrderBy(static artifact => artifact.RelativePath, StringComparer.Ordinal))
        {
            lines.Add(RenderArtifact(artifact));
        }

        lines.Add("File: Role=ExecutionReceipt | RelativePath=" +
            ExecutionReceiptRelativePath.Replace("{packageHash}", package.PackageHash.Trim(), StringComparison.Ordinal));
        lines.Add("[EVIDENCE]");
        lines.Add("HashAlgorithm: " + HashAlgorithmName);
        lines.Add("ExecutionReferencePrefix: " + ExecutionReferencePrefix);
        lines.Add("ExecutionSideEffectMarker: " + OfficialExternalOutputExecutionValidator.TextFileWriteConfirmedMarker);

        return Result<OfficialExternalOutputExecutionTextRenderResult>.Success(
            new OfficialExternalOutputExecutionTextRenderResult(lines, string.Join("\n", lines)));
    }

    private static string RenderArtifact(
        OfficialExternalOutputWrittenArtifact artifact)
    {
        return "File: Role=" + artifact.Role +
            " | RelativePath=" + artifact.RelativePath +
            " | ContentHash=" + artifact.ContentHash +
            " | HashAlgorithm=" + artifact.HashAlgorithm +
            " | LineCount=" + artifact.LineCount.ToString(CultureInfo.InvariantCulture) +
            " | ByteCount=" + artifact.ByteCount.ToString(CultureInfo.InvariantCulture) +
            " | ArtifactReference=" + (artifact.ArtifactReference ?? string.Empty) +
            " | ArtifactLabel=" + (artifact.ArtifactLabel ?? string.Empty) +
            " | Metadata=" + OfficialExternalOutputExecutionValidator.FormatMetadata(artifact.Metadata);
    }

    private static string Format(DateTimeOffset? value)
    {
        return value.HasValue ? value.Value.ToString("O", CultureInfo.InvariantCulture) : string.Empty;
    }
}

public static class OfficialExternalOutputExecutionValidator
{
    public const string TextFileWriteConfirmedMarker = "EXTERNAL_OUTPUT_TEXT_FILE_WRITE_CONFIRMED";
    public const string ExecutionReceiptRelativeFileName = "execution-receipt.txt";

    private const string DispatchPlanReferencePrefix = "salryn://official-external-output-dispatch-plan/";
    private const string PackageReferencePrefix = "salryn://official-output-export-package/";
    private const string LedgerReferencePrefix = "salryn://official-output-delivery-ledger/";
    private const string ContractOnlyMarker = "EXTERNAL_OUTPUT_CONTRACT_ONLY_NO_SIDE_EFFECTS";

    public static Result ValidateExecutionRequest(
        OfficialExternalOutputExecutionRequest? request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputExecution.RequestRequired",
                "Official external output execution request is required."));
        }

        Result commonValidation = ValidateRequestCommon(request);

        if (commonValidation.IsFailure)
        {
            return commonValidation;
        }

        if (string.IsNullOrWhiteSpace(request.OutputRootPath))
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputExecution.OutputRootRequired",
                "Official external output execution requires an output root path."));
        }

        return Result.Success();
    }

    public static Result ValidateReceiptInput(
        OfficialExternalOutputExecutionRequest? request,
        IReadOnlyList<OfficialExternalOutputWrittenArtifact>? writtenArtifacts,
        string adapterName)
    {
        Result commonValidation = ValidateRequestCommon(request);

        if (commonValidation.IsFailure)
        {
            return commonValidation;
        }

        if (string.IsNullOrWhiteSpace(adapterName))
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputExecution.AdapterNameRequired",
                "Official external output execution adapter name is required."));
        }

        if (writtenArtifacts is null || writtenArtifacts.Count == 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputExecution.WrittenArtifactRequired",
                "Official external output execution requires written artifacts."));
        }

        HashSet<string> paths = new(StringComparer.Ordinal);

        foreach (OfficialExternalOutputWrittenArtifact artifact in writtenArtifacts)
        {
            Result artifactValidation = ValidateWrittenArtifact(artifact);

            if (artifactValidation.IsFailure)
            {
                return artifactValidation;
            }

            if (!paths.Add(artifact.RelativePath.Trim()))
            {
                return Result.Fail(Error.Validation(
                    "OfficialExternalOutputExecution.DuplicateRelativePath",
                    "Official external output execution contains duplicate relative paths."));
            }
        }

        return Result.Success();
    }

    private static Result ValidateRequestCommon(
        OfficialExternalOutputExecutionRequest? request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputExecution.RequestRequired",
                "Official external output execution request is required."));
        }

        if (request.DispatchPlan is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputExecution.DispatchPlanRequired",
                "Official external output execution requires a dispatch plan."));
        }

        if (request.ExportPackage is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputExecution.ExportPackageRequired",
                "Official external output execution requires an export package."));
        }

        if (request.DeliveryLedgerPackage is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputExecution.DeliveryLedgerRequired",
                "Official external output execution requires a delivery ledger package."));
        }

        if (string.IsNullOrWhiteSpace(request.DispatchPlan.DispatchPlanReference) ||
            string.IsNullOrWhiteSpace(request.DispatchPlan.DispatchPlanHash) ||
            !string.Equals(Normalize(request.DispatchPlan.DispatchPlanReference), DispatchPlanReferencePrefix + Normalize(request.DispatchPlan.DispatchPlanHash), StringComparison.Ordinal))
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputExecution.DispatchPlanIdentityInvalid",
                "Official external output execution dispatch plan identity is invalid."));
        }

        if (request.DispatchPlan.Items is null ||
            request.DispatchPlan.Items.Count(static item => item.Channel == OfficialExternalOutputChannel.File) != 1)
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputExecution.FileDispatchPlanRequired",
                "Official external output execution requires exactly one file-channel dispatch plan item."));
        }

        if (request.DispatchPlan.Items.Any(static item =>
            item.Channel is OfficialExternalOutputChannel.Pdf or OfficialExternalOutputChannel.Print or OfficialExternalOutputChannel.Crystal))
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputExecution.UnsupportedExternalChannel",
                "Official external output execution supports controlled text-file output only."));
        }

        OfficialExternalOutputDispatchPlanItem fileItem =
            request.DispatchPlan.Items.Single(static item => item.Channel == OfficialExternalOutputChannel.File);

        if (string.IsNullOrWhiteSpace(fileItem.SideEffectMarker) ||
            !fileItem.SideEffectMarker.Contains(ContractOnlyMarker, StringComparison.Ordinal))
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputExecution.DispatchPlanMarkerRequired",
                "Official external output execution requires a contract-only dispatch plan marker."));
        }

        if (string.IsNullOrWhiteSpace(request.ExportPackage.PackageReference) ||
            string.IsNullOrWhiteSpace(request.ExportPackage.PackageHash) ||
            !string.Equals(Normalize(request.ExportPackage.PackageReference), PackageReferencePrefix + Normalize(request.ExportPackage.PackageHash), StringComparison.Ordinal))
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputExecution.PackageIdentityInvalid",
                "Official external output execution package identity is invalid."));
        }

        if (string.IsNullOrWhiteSpace(request.DeliveryLedgerPackage.LedgerReference) ||
            string.IsNullOrWhiteSpace(request.DeliveryLedgerPackage.LedgerHash) ||
            !string.Equals(Normalize(request.DeliveryLedgerPackage.LedgerReference), LedgerReferencePrefix + Normalize(request.DeliveryLedgerPackage.LedgerHash), StringComparison.Ordinal))
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputExecution.LedgerIdentityInvalid",
                "Official external output execution ledger identity is invalid."));
        }

        if (!string.Equals(fileItem.PackageReference, request.ExportPackage.PackageReference, StringComparison.Ordinal) ||
            !string.Equals(fileItem.PackageHash, request.ExportPackage.PackageHash, StringComparison.Ordinal) ||
            !string.Equals(fileItem.LedgerReference, request.DeliveryLedgerPackage.LedgerReference, StringComparison.Ordinal) ||
            !string.Equals(fileItem.LedgerHash, request.DeliveryLedgerPackage.LedgerHash, StringComparison.Ordinal))
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputExecution.IdentityMismatch",
                "Official external output execution dispatch plan identity does not match package or ledger."));
        }

        if (request.RequestedByUserId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputExecution.InvalidRequestedByUserId",
                "Official external output execution requester user id is invalid."));
        }

        return ValidateMetadata(request.Metadata);
    }

    public static Result ValidateWrittenArtifact(
        OfficialExternalOutputWrittenArtifact? artifact)
    {
        if (artifact is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputExecution.WrittenArtifactRequired",
                "Official external output execution written artifact is required."));
        }

        if (!Enum.IsDefined(typeof(OfficialExternalOutputWrittenArtifactRole), artifact.Role))
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputExecution.WrittenArtifactRoleInvalid",
                "Official external output execution written artifact role is invalid."));
        }

        if (string.IsNullOrWhiteSpace(artifact.RelativePath) ||
            artifact.RelativePath.Contains("..", StringComparison.Ordinal) ||
            artifact.RelativePath.StartsWith("/", StringComparison.Ordinal) ||
            artifact.RelativePath.StartsWith("\\", StringComparison.Ordinal))
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputExecution.RelativePathInvalid",
                "Official external output execution written artifact relative path is invalid."));
        }

        if (string.IsNullOrWhiteSpace(artifact.ContentHash) ||
            string.IsNullOrWhiteSpace(artifact.HashAlgorithm))
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputExecution.WrittenArtifactHashRequired",
                "Official external output execution written artifact hash is required."));
        }

        if (artifact.LineCount < 0 || artifact.ByteCount < 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialExternalOutputExecution.WrittenArtifactCountsInvalid",
                "Official external output execution written artifact counts are invalid."));
        }

        return ValidateMetadata(artifact.Metadata);
    }

    public static OfficialExternalOutputExecutionRequest NormalizeRequest(
        OfficialExternalOutputExecutionRequest request)
    {
        return request with
        {
            OutputRootPath = NormalizeOptional(request.OutputRootPath),
            TerminalId = NormalizeOptional(request.TerminalId),
            Metadata = NormalizeMetadata(request.Metadata)
        };
    }

    public static IReadOnlyList<OfficialExternalOutputWrittenArtifact> NormalizeArtifacts(
        IReadOnlyList<OfficialExternalOutputWrittenArtifact> artifacts)
    {
        return artifacts
            .Select(static artifact => artifact with
            {
                RelativePath = NormalizePath(artifact.RelativePath),
                ContentHash = Normalize(artifact.ContentHash),
                HashAlgorithm = Normalize(artifact.HashAlgorithm),
                ArtifactReference = NormalizeOptional(artifact.ArtifactReference),
                ArtifactLabel = NormalizeOptional(artifact.ArtifactLabel),
                Metadata = NormalizeMetadata(artifact.Metadata)
            })
            .OrderBy(static artifact => artifact.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    public static OfficialExternalOutputWrittenArtifact BuildArtifact(
        OfficialExternalOutputWrittenArtifactRole role,
        string relativePath,
        string content,
        string? artifactReference = null,
        string? artifactLabel = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        string normalizedContent = NormalizeText(content);
        return new OfficialExternalOutputWrittenArtifact(
            role,
            NormalizePath(relativePath),
            ComputeContentHash(normalizedContent),
            "SHA-256",
            CountLines(normalizedContent),
            Encoding.UTF8.GetByteCount(normalizedContent),
            NormalizeOptional(artifactReference),
            NormalizeOptional(artifactLabel),
            NormalizeMetadata(metadata));
    }

    public static int CountLines(string text)
    {
        string normalized = NormalizeText(text);
        return normalized.Length == 0 ? 0 : normalized.Split('\n').Length;
    }

    public static string NormalizeText(string? text)
    {
        return text?.Replace("\r\n", "\n").Replace('\r', '\n').Trim() ?? string.Empty;
    }

    public static string ComputeContentHash(string text)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeText(text)));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static IReadOnlyDictionary<string, string> NormalizeMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return metadata
            .Select(pair => new KeyValuePair<string, string>(Normalize(pair.Key), Normalize(pair.Value)))
            .Where(static pair => pair.Key.Length > 0)
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
    }

    public static Result ValidateMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return Result.Success();
        }

        HashSet<string> keys = new(StringComparer.Ordinal);

        foreach (KeyValuePair<string, string> pair in metadata)
        {
            string key = Normalize(pair.Key);
            if (key.Length == 0 || !keys.Add(key))
            {
                return Result.Fail(Error.Validation(
                    "OfficialExternalOutputExecution.MetadataInvalid",
                    "Official external output execution metadata is invalid."));
            }
        }

        return Result.Success();
    }

    public static string FormatMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        return string.Join(" | ", NormalizeMetadata(metadata).Select(static pair => pair.Key + "=" + pair.Value));
    }

    private static string NormalizePath(string? value)
    {
        return Normalize(value).Replace('\\', '/');
    }

    private static string? NormalizeOptional(string? value)
    {
        string normalized = Normalize(value);
        return normalized.Length == 0 ? null : normalized;
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}

public sealed record OfficialExternalOutputExecutionLedgerEntry(
    int EntryNumber,
    string ExecutionReference,
    string ExecutionHash,
    OfficialExternalOutputExecutionStatus Status,
    OfficialExternalOutputChannel Channel,
    string AdapterName,
    string DispatchPlanReference,
    string DispatchPlanHash,
    string PackageReference,
    string PackageHash,
    string LedgerReference,
    string LedgerHash,
    int WrittenFileCount,
    int TotalLineCount,
    DateTimeOffset? ExecutedAt,
    IReadOnlyList<OfficialExternalOutputWrittenArtifact> WrittenArtifacts);

public sealed record OfficialExternalOutputExecutionLedgerPackage(
    IReadOnlyList<OfficialExternalOutputExecutionLedgerEntry> Entries,
    int EntryCount,
    string ExecutionLedgerHash,
    string ExecutionLedgerReference,
    DateTimeOffset? CreatedAt,
    OfficialExternalOutputExecutionLedgerTextRenderResult TextResult);

public sealed record OfficialExternalOutputExecutionLedgerTextRenderResult(
    IReadOnlyList<string> Lines,
    string Text);

public sealed class OfficialExternalOutputExecutionLedgerBuilder
{
    private const string ExecutionLedgerReferencePrefix = "salryn://official-external-output-execution-ledger/";

    public Result<OfficialExternalOutputExecutionLedgerPackage> Build(
        IReadOnlyList<OfficialExternalOutputExecutionResult>? results,
        DateTimeOffset? createdAt = null)
    {
        if (results is null)
        {
            return Result<OfficialExternalOutputExecutionLedgerPackage>.Fail(Error.Validation(
                "OfficialExternalOutputExecutionLedger.ResultsRequired",
                "Official external output execution ledger requires execution results."));
        }

        if (results.Count == 0)
        {
            return Result<OfficialExternalOutputExecutionLedgerPackage>.Fail(Error.Validation(
                "OfficialExternalOutputExecutionLedger.ResultRequired",
                "Official external output execution ledger requires at least one execution result."));
        }

        HashSet<string> references = new(StringComparer.Ordinal);
        List<OfficialExternalOutputExecutionLedgerEntry> entries = new();

        foreach (OfficialExternalOutputExecutionResult result in results)
        {
            if (result is null || string.IsNullOrWhiteSpace(result.ExecutionReference) || string.IsNullOrWhiteSpace(result.ExecutionHash))
            {
                return Result<OfficialExternalOutputExecutionLedgerPackage>.Fail(Error.Validation(
                    "OfficialExternalOutputExecutionLedger.ResultInvalid",
                    "Official external output execution ledger result is invalid."));
            }

            if (!references.Add(result.ExecutionReference.Trim()))
            {
                return Result<OfficialExternalOutputExecutionLedgerPackage>.Fail(Error.Validation(
                    "OfficialExternalOutputExecutionLedger.DuplicateExecutionReference",
                    "Official external output execution ledger contains duplicate execution references."));
            }

            entries.Add(new OfficialExternalOutputExecutionLedgerEntry(
                0,
                result.ExecutionReference.Trim(),
                result.ExecutionHash.Trim(),
                result.Status,
                result.Channel,
                result.AdapterName.Trim(),
                result.DispatchPlanReference.Trim(),
                result.DispatchPlanHash.Trim(),
                result.PackageReference.Trim(),
                result.PackageHash.Trim(),
                result.LedgerReference.Trim(),
                result.LedgerHash.Trim(),
                result.WrittenFileCount,
                result.TotalLineCount,
                result.ExecutedAt,
                OfficialExternalOutputExecutionValidator.NormalizeArtifacts(result.WrittenArtifacts)));
        }

        IReadOnlyList<OfficialExternalOutputExecutionLedgerEntry> ordered = entries
            .OrderBy(static entry => entry.ExecutionReference, StringComparer.Ordinal)
            .Select((entry, index) => entry with { EntryNumber = index + 1 })
            .ToArray();

        OfficialExternalOutputExecutionLedgerTextRenderResult text = Render(ordered, createdAt);
        string hash = OfficialExternalOutputExecutionValidator.ComputeContentHash(text.Text);

        return Result<OfficialExternalOutputExecutionLedgerPackage>.Success(
            new OfficialExternalOutputExecutionLedgerPackage(
                ordered,
                ordered.Count,
                hash,
                ExecutionLedgerReferencePrefix + hash,
                createdAt,
                text));
    }

    private static OfficialExternalOutputExecutionLedgerTextRenderResult Render(
        IReadOnlyList<OfficialExternalOutputExecutionLedgerEntry> entries,
        DateTimeOffset? createdAt)
    {
        List<string> lines = new()
        {
            "SALRYN OFFICIAL EXTERNAL OUTPUT EXECUTION LEDGER",
            "[LEDGER]",
            "EntryCount: " + entries.Count.ToString(CultureInfo.InvariantCulture),
            "CreatedAt: " + (createdAt.HasValue ? createdAt.Value.ToString("O", CultureInfo.InvariantCulture) : string.Empty),
            "ExecutionLedgerHash: COMPUTED_IN_EXECUTION_LEDGER",
            "ExecutionLedgerReference: COMPUTED_IN_EXECUTION_LEDGER",
            "[EXECUTIONS]"
        };

        foreach (OfficialExternalOutputExecutionLedgerEntry entry in entries)
        {
            lines.Add("Execution: EntryNumber=" + entry.EntryNumber.ToString(CultureInfo.InvariantCulture) +
                " | ExecutionReference=" + entry.ExecutionReference +
                " | ExecutionHash=" + entry.ExecutionHash +
                " | Status=" + entry.Status +
                " | Channel=" + entry.Channel +
                " | AdapterName=" + entry.AdapterName +
                " | DispatchPlanReference=" + entry.DispatchPlanReference +
                " | DispatchPlanHash=" + entry.DispatchPlanHash +
                " | PackageReference=" + entry.PackageReference +
                " | PackageHash=" + entry.PackageHash +
                " | LedgerReference=" + entry.LedgerReference +
                " | LedgerHash=" + entry.LedgerHash +
                " | WrittenFileCount=" + entry.WrittenFileCount.ToString(CultureInfo.InvariantCulture) +
                " | TotalLineCount=" + entry.TotalLineCount.ToString(CultureInfo.InvariantCulture));
        }

        lines.Add("[EVIDENCE]");
        lines.Add("HashAlgorithm: SHA-256");
        lines.Add("ExecutionLedgerReferencePrefix: salryn://official-external-output-execution-ledger/");
        lines.Add("ExecutionLedgerSideEffectMarker: EXTERNAL_OUTPUT_EXECUTION_LEDGER_IN_MEMORY_ONLY_NO_DATABASE");

        return new OfficialExternalOutputExecutionLedgerTextRenderResult(lines, string.Join("\n", lines));
    }
}
