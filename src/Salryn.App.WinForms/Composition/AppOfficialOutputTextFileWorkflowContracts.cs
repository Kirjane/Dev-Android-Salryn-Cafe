using Salryn.Core.Common;
using Salryn.Core.Services;

namespace Salryn.App.WinForms.Composition;

public sealed record AppOfficialOutputTextFileWorkflowRequest(
    OfficialOutputExportPackage? ExportPackage,
    string? OutputRootPath,
    DateTimeOffset? RequestedAt,
    Guid? RequestedByUserId,
    string? TerminalId,
    IReadOnlyDictionary<string, string>? Metadata);

public sealed record AppOfficialOutputTextFileWorkflowReferenceSummary(
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

public sealed record AppOfficialOutputTextFileWorkflowResult(
    bool Succeeded,
    bool Verified,
    string StatusCode,
    string StatusMessage,
    string? ErrorCode,
    string? ErrorMessage,
    AppOfficialOutputTextFileWorkflowReferenceSummary? References,
    string? RuntimeOutputRootPath)
{
    public static AppOfficialOutputTextFileWorkflowResult Success(
        OfficialExternalOutputWorkflowResult workflowResult)
    {
        ArgumentNullException.ThrowIfNull(workflowResult);

        return new AppOfficialOutputTextFileWorkflowResult(
            Succeeded: true,
            Verified: workflowResult.Verified,
            StatusCode: "AppOfficialOutputTextFileWorkflow.Verified",
            StatusMessage: "Official output text-file workflow completed and verification result was returned.",
            ErrorCode: null,
            ErrorMessage: null,
            References: new AppOfficialOutputTextFileWorkflowReferenceSummary(
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

    public static AppOfficialOutputTextFileWorkflowResult Failure(
        Error error)
    {
        return new AppOfficialOutputTextFileWorkflowResult(
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

public interface IAppOfficialOutputTextFileWorkflowGateway
{
    Task<AppOfficialOutputTextFileWorkflowResult> RunAsync(
        AppOfficialOutputTextFileWorkflowRequest? request,
        CancellationToken cancellationToken = default);
}

public static class AppOfficialOutputTextFileWorkflowRules
{
    public static Result ValidateRequest(
        AppOfficialOutputTextFileWorkflowRequest? request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "AppOfficialOutputTextFileWorkflow.RequestRequired",
                "Official output text-file workflow request is required."));
        }

        if (request.ExportPackage is null)
        {
            return Result.Fail(Error.Validation(
                "AppOfficialOutputTextFileWorkflow.PackageRequired",
                "Official output text-file workflow requires a trusted official output export package."));
        }

        if (request.ExportPackage.PackageKind != OfficialOutputExportPackageKind.PreviewTextBundle)
        {
            return Result.Fail(Error.Validation(
                "AppOfficialOutputTextFileWorkflow.PackageKindInvalid",
                "Official output text-file workflow requires the official preview text bundle package kind."));
        }

        if (string.IsNullOrWhiteSpace(request.ExportPackage.PackageReference) ||
            string.IsNullOrWhiteSpace(request.ExportPackage.PackageHash) ||
            !request.ExportPackage.PackageReference.Trim().StartsWith("salryn://official-output-export-package/", StringComparison.Ordinal))
        {
            return Result.Fail(Error.Validation(
                "AppOfficialOutputTextFileWorkflow.PackageIdentityInvalid",
                "Official output text-file workflow package identity is invalid."));
        }

        if (string.IsNullOrWhiteSpace(request.OutputRootPath))
        {
            return Result.Fail(Error.Validation(
                "AppOfficialOutputTextFileWorkflow.OutputRootRequired",
                "Official output text-file workflow requires an explicit output root path."));
        }

        if (!request.RequestedAt.HasValue)
        {
            return Result.Fail(Error.Validation(
                "AppOfficialOutputTextFileWorkflow.RequestedAtRequired",
                "Official output text-file workflow requires an explicit requested timestamp."));
        }

        if (!request.RequestedByUserId.HasValue || request.RequestedByUserId.Value == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "AppOfficialOutputTextFileWorkflow.RequestedByUserIdRequired",
                "Official output text-file workflow requires an explicit requested user id."));
        }

        if (string.IsNullOrWhiteSpace(request.TerminalId))
        {
            return Result.Fail(Error.Validation(
                "AppOfficialOutputTextFileWorkflow.TerminalIdRequired",
                "Official output text-file workflow requires an explicit terminal id."));
        }

        if (request.Metadata is null)
        {
            return Result.Fail(Error.Validation(
                "AppOfficialOutputTextFileWorkflow.MetadataRequired",
                "Official output text-file workflow requires explicit metadata, even when empty."));
        }

        return Result.Success();
    }

    public static AppOfficialOutputTextFileWorkflowRequest NormalizeRequest(
        AppOfficialOutputTextFileWorkflowRequest request)
    {
        return request with
        {
            OutputRootPath = request.OutputRootPath?.Trim(),
            TerminalId = request.TerminalId?.Trim(),
            Metadata = NormalizeMetadata(request.Metadata)
        };
    }

    private static IReadOnlyDictionary<string, string> NormalizeMetadata(
        IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return metadata
            .Select(static pair => new KeyValuePair<string, string>(pair.Key.Trim(), pair.Value.Trim()))
            .Where(static pair => pair.Key.Length > 0)
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
    }
}
