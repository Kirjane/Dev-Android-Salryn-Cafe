using Salryn.Core.Common;
using Salryn.Core.Services;

namespace Salryn.Host.Local.Contracts;

public sealed record LocalHostOfficialOutputTextFileWorkflowRequest(
    OfficialOutputExportPackage? ExportPackage,
    string? OutputRootPath,
    DateTimeOffset? RequestedAt,
    Guid? RequestedByUserId,
    string? TerminalId,
    IReadOnlyDictionary<string, string>? Metadata);

public sealed record LocalHostOfficialOutputTextFileWorkflowReferenceSummary(
    string PackageReference,
    string PackageHash,
    string DeliveryReference,
    string DeliveryHash,
    string DeliveryLedgerReference,
    string DeliveryLedgerHash,
    string DispatchPlanReference,
    string DispatchPlanHash,
    string ExecutionReference,
    string ExecutionHash,
    string ExecutionLedgerReference,
    string ExecutionLedgerHash,
    string VerificationReference,
    string VerificationHash,
    string WorkflowReference,
    string WorkflowHash);

public sealed record LocalHostOfficialOutputTextFileWorkflowResponse(
    bool Succeeded,
    bool Verified,
    string StatusCode,
    string StatusMessage,
    string? ErrorCode,
    string? ErrorMessage,
    LocalHostOfficialOutputTextFileWorkflowReferenceSummary? References,
    string? RuntimeOutputRootPath)
{
    public static LocalHostOfficialOutputTextFileWorkflowResponse Success(
        OfficialExternalOutputWorkflowResult workflowResult)
    {
        ArgumentNullException.ThrowIfNull(workflowResult);

        return new LocalHostOfficialOutputTextFileWorkflowResponse(
            Succeeded: true,
            Verified: workflowResult.Verified,
            StatusCode: "LocalHostOfficialOutputTextFileWorkflow.Verified",
            StatusMessage: "Official output text-file workflow completed and verification result was returned.",
            ErrorCode: null,
            ErrorMessage: null,
            References: new LocalHostOfficialOutputTextFileWorkflowReferenceSummary(
                workflowResult.PackageReference,
                workflowResult.PackageHash,
                workflowResult.DeliveryReference,
                workflowResult.DeliveryHash,
                workflowResult.DeliveryLedgerReference,
                workflowResult.DeliveryLedgerHash,
                workflowResult.DispatchPlanReference,
                workflowResult.DispatchPlanHash,
                workflowResult.ExecutionReference,
                workflowResult.ExecutionHash,
                workflowResult.ExecutionLedgerReference,
                workflowResult.ExecutionLedgerHash,
                workflowResult.VerificationReference,
                workflowResult.VerificationHash,
                workflowResult.WorkflowReference,
                workflowResult.WorkflowHash),
            RuntimeOutputRootPath: workflowResult.OutputRootPath);
    }

    public static LocalHostOfficialOutputTextFileWorkflowResponse Failure(
        Error error)
    {
        return new LocalHostOfficialOutputTextFileWorkflowResponse(
            Succeeded: false,
            Verified: false,
            StatusCode: error.Code,
            StatusMessage: error.Message,
            ErrorCode: error.Code,
            ErrorMessage: error.Message,
            References: null,
            RuntimeOutputRootPath: null);
    }
}

public static class LocalHostOfficialOutputTextFileWorkflowRules
{
    private const string PackageReferencePrefix = "salryn://official-output-export-package/";

    public static Result ValidateRequest(
        LocalHostOfficialOutputTextFileWorkflowRequest? request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "LocalHostOfficialOutputTextFileWorkflow.RequestRequired",
                "Local Host official output text-file workflow request is required."));
        }

        if (request.ExportPackage is null)
        {
            return Result.Fail(Error.Validation(
                "LocalHostOfficialOutputTextFileWorkflow.PackageRequired",
                "Local Host official output text-file workflow requires a trusted official output export package."));
        }

        if (request.ExportPackage.PackageKind != OfficialOutputExportPackageKind.PreviewTextBundle)
        {
            return Result.Fail(Error.Validation(
                "LocalHostOfficialOutputTextFileWorkflow.PackageKindInvalid",
                "Local Host official output text-file workflow requires the official preview text bundle package kind."));
        }

        string packageReference = Normalize(request.ExportPackage.PackageReference);
        string packageHash = Normalize(request.ExportPackage.PackageHash);

        if (packageReference.Length == 0 ||
            packageHash.Length == 0 ||
            !string.Equals(packageReference, PackageReferencePrefix + packageHash, StringComparison.Ordinal))
        {
            return Result.Fail(Error.Validation(
                "LocalHostOfficialOutputTextFileWorkflow.PackageIdentityInvalid",
                "Local Host official output text-file workflow package identity is invalid."));
        }

        if (string.IsNullOrWhiteSpace(request.OutputRootPath))
        {
            return Result.Fail(Error.Validation(
                "LocalHostOfficialOutputTextFileWorkflow.OutputRootRequired",
                "Local Host official output text-file workflow requires an explicit output root path."));
        }

        if (!request.RequestedAt.HasValue)
        {
            return Result.Fail(Error.Validation(
                "LocalHostOfficialOutputTextFileWorkflow.RequestedAtRequired",
                "Local Host official output text-file workflow requires an explicit requested timestamp."));
        }

        if (!request.RequestedByUserId.HasValue || request.RequestedByUserId.Value == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "LocalHostOfficialOutputTextFileWorkflow.RequestedByUserIdRequired",
                "Local Host official output text-file workflow requires an explicit requested user id."));
        }

        if (string.IsNullOrWhiteSpace(request.TerminalId))
        {
            return Result.Fail(Error.Validation(
                "LocalHostOfficialOutputTextFileWorkflow.TerminalIdRequired",
                "Local Host official output text-file workflow requires an explicit terminal id."));
        }

        if (request.Metadata is null)
        {
            return Result.Fail(Error.Validation(
                "LocalHostOfficialOutputTextFileWorkflow.MetadataRequired",
                "Local Host official output text-file workflow requires explicit metadata, even when empty."));
        }

        return ValidateMetadata(request.Metadata);
    }

    public static LocalHostOfficialOutputTextFileWorkflowRequest NormalizeRequest(
        LocalHostOfficialOutputTextFileWorkflowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request with
        {
            OutputRootPath = Normalize(request.OutputRootPath),
            TerminalId = Normalize(request.TerminalId),
            Metadata = NormalizeMetadata(request.Metadata)
        };
    }

    private static Result ValidateMetadata(
        IReadOnlyDictionary<string, string> metadata)
    {
        HashSet<string> keys = new(StringComparer.Ordinal);

        foreach (KeyValuePair<string, string> pair in metadata)
        {
            string key = Normalize(pair.Key);

            if (key.Length == 0 || !keys.Add(key))
            {
                return Result.Fail(Error.Validation(
                    "LocalHostOfficialOutputTextFileWorkflow.MetadataInvalid",
                    "Local Host official output text-file workflow metadata is invalid."));
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

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }
}
