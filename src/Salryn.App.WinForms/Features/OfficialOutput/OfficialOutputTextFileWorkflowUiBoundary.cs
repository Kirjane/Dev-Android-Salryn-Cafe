using Salryn.App.WinForms.Composition;
using Salryn.Core.Common;
using Salryn.Core.Security;
using Salryn.Core.Services;

namespace Salryn.App.WinForms.Features.OfficialOutput;

public sealed record OfficialOutputTextFileWorkflowUiTriggerRequest(
    string? OutputRootPath,
    DateTimeOffset? RequestedAt,
    Guid? RequestedByUserId,
    string? TerminalId,
    IReadOnlyDictionary<string, string>? Metadata,
    OfficialOutputTextFileSourceSelectionRequest? SourceSelectionRequest = null,
    IReadOnlyList<string>? CallerPermissions = null);

public sealed record OfficialOutputTextFileWorkflowUiStatus(
    bool IsAvailable,
    string StatusCode,
    string StatusMessage,
    IReadOnlyList<string> ReasonCodes,
    AppOfficialOutputTextFileWorkflowReadinessState? ReadinessState)
{
    public static OfficialOutputTextFileWorkflowUiStatus FromReadiness(
        AppOfficialOutputTextFileWorkflowReadinessResult readiness)
    {
        ArgumentNullException.ThrowIfNull(readiness);

        return new OfficialOutputTextFileWorkflowUiStatus(
            readiness.IsAvailable,
            readiness.StatusCode,
            readiness.StatusMessage,
            readiness.Issues
                .Select(static issue => issue.Code)
                .OrderBy(static code => code, StringComparer.Ordinal)
                .ToArray(),
            readiness.State);
    }

    public static OfficialOutputTextFileWorkflowUiStatus Unavailable(
        string code,
        string message)
    {
        return new OfficialOutputTextFileWorkflowUiStatus(
            IsAvailable: false,
            StatusCode: code,
            StatusMessage: message,
            ReasonCodes: new[] { code },
            ReadinessState: null);
    }
}

public sealed record OfficialOutputTextFileWorkflowUiCommandResult(
    bool Succeeded,
    bool AttemptedExecution,
    string StatusCode,
    string StatusMessage,
    string? ErrorCode,
    string? ErrorMessage,
    AppOfficialOutputTextFileWorkflowReferenceSummary? References,
    string? RuntimeOutputRootPath,
    OfficialOutputTextFileWorkflowUiStatus Status)
{
    public static OfficialOutputTextFileWorkflowUiCommandResult Blocked(
        OfficialOutputTextFileWorkflowUiStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return new OfficialOutputTextFileWorkflowUiCommandResult(
            Succeeded: false,
            AttemptedExecution: false,
            StatusCode: "OfficialOutputTextFileWorkflowUi.ReadinessUnavailable",
            StatusMessage: "Official output text-file workflow is not ready for UI-triggered execution.",
            ErrorCode: status.StatusCode,
            ErrorMessage: status.StatusMessage,
            References: null,
            RuntimeOutputRootPath: null,
            Status: status);
    }

    public static OfficialOutputTextFileWorkflowUiCommandResult ValidationBlocked(
        OfficialOutputTextFileWorkflowUiStatus status,
        Error error)
    {
        ArgumentNullException.ThrowIfNull(status);

        return new OfficialOutputTextFileWorkflowUiCommandResult(
            Succeeded: false,
            AttemptedExecution: false,
            StatusCode: "OfficialOutputTextFileWorkflowUi.RequestInvalid",
            StatusMessage: "Official output text-file workflow UI trigger request is invalid.",
            ErrorCode: error.Code,
            ErrorMessage: error.Message,
            References: null,
            RuntimeOutputRootPath: null,
            Status: status);
    }

    public static OfficialOutputTextFileWorkflowUiCommandResult FromWorkflowResult(
        OfficialOutputTextFileWorkflowUiStatus status,
        AppOfficialOutputTextFileWorkflowResult result)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(result);

        return new OfficialOutputTextFileWorkflowUiCommandResult(
            result.Succeeded,
            AttemptedExecution: true,
            result.StatusCode,
            result.StatusMessage,
            result.ErrorCode,
            result.ErrorMessage,
            result.References,
            result.RuntimeOutputRootPath,
            status);
    }
}

public interface IOfficialOutputTextFileWorkflowUiBoundary
{
    OfficialOutputTextFileWorkflowUiStatus GetStatus(
        OfficialOutputTextFileWorkflowUiTriggerRequest? request);

    Task<OfficialOutputTextFileWorkflowUiCommandResult> TryRunAsync(
        OfficialOutputTextFileWorkflowUiTriggerRequest? request,
        CancellationToken cancellationToken = default);
}

public sealed class OfficialOutputTextFileWorkflowUiBoundary : IOfficialOutputTextFileWorkflowUiBoundary
{
    private readonly IAppOfficialOutputTextFileWorkflowReadinessGateway? _readinessGateway;
    private readonly IAppOfficialOutputTextFileWorkflowGateway? _workflowGateway;
    private readonly IOfficialOutputTextFilePackageSupplyBoundary? _packageSupplyBoundary;
    private readonly IOfficialOutputTextFileWorkflowAuditRecorder? _auditRecorder;

    public OfficialOutputTextFileWorkflowUiBoundary(
        IAppOfficialOutputTextFileWorkflowReadinessGateway? readinessGateway,
        IAppOfficialOutputTextFileWorkflowGateway? workflowGateway,
        IOfficialOutputTextFilePackageSupplyBoundary? packageSupplyBoundary,
        IOfficialOutputTextFileWorkflowAuditRecorder? auditRecorder = null)
    {
        _readinessGateway = readinessGateway;
        _workflowGateway = workflowGateway;
        _packageSupplyBoundary = packageSupplyBoundary;
        _auditRecorder = auditRecorder;
    }

    public OfficialOutputTextFileWorkflowUiStatus GetStatus(
        OfficialOutputTextFileWorkflowUiTriggerRequest? request)
    {
        Result<OfficialOutputExportPackage> packageResult =
            TryGetSuppliedPackage(request?.SourceSelectionRequest);

        return GetStatus(request, packageResult);
    }

    private OfficialOutputTextFileWorkflowUiStatus GetStatus(
        OfficialOutputTextFileWorkflowUiTriggerRequest? request,
        Result<OfficialOutputExportPackage> packageResult)
    {
        IAppOfficialOutputTextFileWorkflowReadinessGateway? readinessGateway = _readinessGateway;

        if (readinessGateway is null)
        {
            return OfficialOutputTextFileWorkflowUiStatus.Unavailable(
                "OfficialOutputTextFileWorkflowUi.ReadinessGatewayUnavailable",
                "Official output text-file workflow readiness gateway is not available.");
        }

        AppOfficialOutputTextFileWorkflowReadinessResult readiness =
            readinessGateway.CheckReadiness(new AppOfficialOutputTextFileWorkflowReadinessRequest(
                packageResult.IsSuccess ? packageResult.Value : null,
                request?.OutputRootPath));

        return OfficialOutputTextFileWorkflowUiStatus.FromReadiness(readiness);
    }

    public async Task<OfficialOutputTextFileWorkflowUiCommandResult> TryRunAsync(
        OfficialOutputTextFileWorkflowUiTriggerRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return await Task.FromCanceled<OfficialOutputTextFileWorkflowUiCommandResult>(cancellationToken)
                .ConfigureAwait(false);
        }

        Result callerPermission = ValidateCallerPermission(request);

        if (callerPermission.IsFailure)
        {
            OfficialOutputTextFileWorkflowUiStatus permissionStatus =
                OfficialOutputTextFileWorkflowUiStatus.Unavailable(
                    callerPermission.ErrorDetail.Code,
                    callerPermission.ErrorDetail.Message);

            return await FinalizeRunAsync(
                request,
                permissionStatus,
                OfficialOutputTextFileWorkflowUiCommandResult.ValidationBlocked(
                    permissionStatus,
                    callerPermission.ErrorDetail),
                package: null,
                filesGenerated: false,
                readinessBlocked: false,
                cancellationToken).ConfigureAwait(false);
        }

        Result<OfficialOutputExportPackage> packageResult =
            await TryGetSuppliedPackageAsync(request?.SourceSelectionRequest, cancellationToken).ConfigureAwait(false);
        OfficialOutputTextFileWorkflowUiStatus status = GetStatus(request, packageResult);

        if (!status.IsAvailable)
        {
            return await FinalizeRunAsync(
                request,
                status,
                OfficialOutputTextFileWorkflowUiCommandResult.Blocked(status),
                package: packageResult.IsSuccess ? packageResult.Value : null,
                filesGenerated: false,
                readinessBlocked: true,
                cancellationToken).ConfigureAwait(false);
        }

        if (packageResult.IsFailure)
        {
            return await FinalizeRunAsync(
                request,
                status,
                OfficialOutputTextFileWorkflowUiCommandResult.ValidationBlocked(
                    status,
                    packageResult.ErrorDetail),
                package: null,
                filesGenerated: false,
                readinessBlocked: false,
                cancellationToken).ConfigureAwait(false);
        }

        IAppOfficialOutputTextFileWorkflowGateway? workflowGateway = _workflowGateway;

        if (workflowGateway is null)
        {
            OfficialOutputTextFileWorkflowUiStatus blockedStatus =
                OfficialOutputTextFileWorkflowUiStatus.Unavailable(
                    "OfficialOutputTextFileWorkflowUi.WorkflowGatewayUnavailable",
                    "Official output text-file workflow gateway is not available.");

            return await FinalizeRunAsync(
                request,
                blockedStatus,
                OfficialOutputTextFileWorkflowUiCommandResult.Blocked(blockedStatus),
                package: packageResult.Value,
                filesGenerated: false,
                readinessBlocked: true,
                cancellationToken).ConfigureAwait(false);
        }

        AppOfficialOutputTextFileWorkflowRequest appRequest = new(
            packageResult.Value,
            request?.OutputRootPath,
            request?.RequestedAt,
            request?.RequestedByUserId,
            request?.TerminalId,
            request?.Metadata);

        Result validation = AppOfficialOutputTextFileWorkflowRules.ValidateRequest(appRequest);

        if (validation.IsFailure)
        {
            return await FinalizeRunAsync(
                request,
                status,
                OfficialOutputTextFileWorkflowUiCommandResult.ValidationBlocked(
                    status,
                    validation.ErrorDetail),
                package: packageResult.Value,
                filesGenerated: false,
                readinessBlocked: false,
                cancellationToken).ConfigureAwait(false);
        }

        AppOfficialOutputTextFileWorkflowResult result =
            await workflowGateway.RunAsync(appRequest, cancellationToken).ConfigureAwait(false);

        OfficialOutputTextFileWorkflowUiCommandResult commandResult =
            OfficialOutputTextFileWorkflowUiCommandResult.FromWorkflowResult(status, result);

        return await FinalizeRunAsync(
            request,
            status,
            commandResult,
            package: packageResult.Value,
            filesGenerated: result.Succeeded,
            readinessBlocked: false,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<OfficialOutputTextFileWorkflowUiCommandResult> FinalizeRunAsync(
        OfficialOutputTextFileWorkflowUiTriggerRequest? request,
        OfficialOutputTextFileWorkflowUiStatus status,
        OfficialOutputTextFileWorkflowUiCommandResult result,
        OfficialOutputExportPackage? package,
        bool filesGenerated,
        bool readinessBlocked,
        CancellationToken cancellationToken)
    {
        IOfficialOutputTextFileWorkflowAuditRecorder? auditRecorder = _auditRecorder;

        if (auditRecorder is null)
        {
            return result;
        }

        OfficialOutputTextFileWorkflowAuditRecordRequest auditRequest = new(
            UserId: request?.RequestedByUserId,
            TerminalId: request?.TerminalId,
            PackageHash: package?.PackageHash,
            WorkflowReference: result.References?.WorkflowReference,
            ErrorCode: result.ErrorCode ?? result.StatusCode,
            Succeeded: result.Succeeded,
            ReadinessBlocked: readinessBlocked,
            Metadata: request?.Metadata,
            RecordedAt: request?.RequestedAt);

        Result auditResult = await auditRecorder
            .RecordRunAttemptAsync(auditRequest, cancellationToken)
            .ConfigureAwait(false);

        if (auditResult.IsSuccess)
        {
            return result;
        }

        // Kirch: why - never echo the raw audit error detail (it may carry raw storage/IO/path text); only controlled, generic audit-failure strings may reach the UI.
        return BuildSanitizedAuditFailure(status, result, filesGenerated);
    }

    private static OfficialOutputTextFileWorkflowUiCommandResult BuildSanitizedAuditFailure(
        OfficialOutputTextFileWorkflowUiStatus status,
        OfficialOutputTextFileWorkflowUiCommandResult result,
        bool filesGenerated)
    {
        string sanitizedMessage =
            OfficialOutputTextFileWorkflowAuditFailure.SanitizedMessage(filesGenerated);

        if (filesGenerated)
        {
            return new OfficialOutputTextFileWorkflowUiCommandResult(
                Succeeded: false,
                AttemptedExecution: result.AttemptedExecution,
                StatusCode: OfficialOutputTextFileWorkflowAuditFailure.AuditRecordFailedCode,
                StatusMessage: sanitizedMessage,
                ErrorCode: OfficialOutputTextFileWorkflowAuditFailure.AuditRecordFailedCode,
                ErrorMessage: sanitizedMessage,
                References: result.References,
                RuntimeOutputRootPath: null,
                Status: status);
        }

        return new OfficialOutputTextFileWorkflowUiCommandResult(
            Succeeded: false,
            AttemptedExecution: false,
            StatusCode: OfficialOutputTextFileWorkflowAuditFailure.AuditRecordFailedCode,
            StatusMessage: sanitizedMessage,
            ErrorCode: OfficialOutputTextFileWorkflowAuditFailure.AuditRecordFailedCode,
            ErrorMessage: sanitizedMessage,
            References: null,
            RuntimeOutputRootPath: null,
            Status: status);
    }

    private Result<OfficialOutputExportPackage> TryGetSuppliedPackage(
        OfficialOutputTextFileSourceSelectionRequest? selectedSourceRequest)
    {
        IOfficialOutputTextFilePackageSupplyBoundary? packageSupplyBoundary = _packageSupplyBoundary;

        if (packageSupplyBoundary is null)
        {
            return Result<OfficialOutputExportPackage>.Fail(Error.Validation(
                "OfficialOutputTextFilePackageSupply.BoundaryUnavailable",
                "Official output text-file package supply boundary is not available."));
        }

        return packageSupplyBoundary.TryGetPackage(selectedSourceRequest);
    }

    private async Task<Result<OfficialOutputExportPackage>> TryGetSuppliedPackageAsync(
        OfficialOutputTextFileSourceSelectionRequest? selectedSourceRequest,
        CancellationToken cancellationToken)
    {
        IOfficialOutputTextFilePackageSupplyBoundary? packageSupplyBoundary = _packageSupplyBoundary;

        if (packageSupplyBoundary is null)
        {
            return Result<OfficialOutputExportPackage>.Fail(Error.Validation(
                "OfficialOutputTextFilePackageSupply.BoundaryUnavailable",
                "Official output text-file package supply boundary is not available."));
        }

        return await packageSupplyBoundary.TryGetPackageAsync(selectedSourceRequest, cancellationToken).ConfigureAwait(false);
    }

    private static Result ValidateCallerPermission(OfficialOutputTextFileWorkflowUiTriggerRequest? request)
    {
        IReadOnlyList<string>? callerPermissions = request?.CallerPermissions;

        if (callerPermissions is null ||
            !callerPermissions.Any(static permission =>
                string.Equals(permission, SalrynPermissions.OfficialOutputRun, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Fail(Error.Forbidden(
                "OfficialOutputTextFileWorkflowUi.PermissionRequired",
                "Official output text-file workflow requires OfficialOutput.Run permission."));
        }

        return Result.Success();
    }
}
