using Salryn.App.WinForms.Composition;
using Salryn.Core.Common;
using Salryn.Core.Services;

namespace Salryn.App.WinForms.Features.OfficialOutput;

public sealed record OfficialOutputTextFileWorkflowUiTriggerRequest(
    OfficialOutputExportPackage? ExportPackage,
    string? OutputRootPath,
    DateTimeOffset? RequestedAt,
    Guid? RequestedByUserId,
    string? TerminalId,
    IReadOnlyDictionary<string, string>? Metadata);

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

    public OfficialOutputTextFileWorkflowUiBoundary(
        IAppOfficialOutputTextFileWorkflowReadinessGateway? readinessGateway,
        IAppOfficialOutputTextFileWorkflowGateway? workflowGateway)
    {
        _readinessGateway = readinessGateway;
        _workflowGateway = workflowGateway;
    }

    public OfficialOutputTextFileWorkflowUiStatus GetStatus(
        OfficialOutputTextFileWorkflowUiTriggerRequest? request)
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
                request?.ExportPackage,
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

        OfficialOutputTextFileWorkflowUiStatus status = GetStatus(request);

        if (!status.IsAvailable)
        {
            return OfficialOutputTextFileWorkflowUiCommandResult.Blocked(status);
        }

        IAppOfficialOutputTextFileWorkflowGateway? workflowGateway = _workflowGateway;

        if (workflowGateway is null)
        {
            OfficialOutputTextFileWorkflowUiStatus blockedStatus =
                OfficialOutputTextFileWorkflowUiStatus.Unavailable(
                    "OfficialOutputTextFileWorkflowUi.WorkflowGatewayUnavailable",
                    "Official output text-file workflow gateway is not available.");

            return OfficialOutputTextFileWorkflowUiCommandResult.Blocked(blockedStatus);
        }

        AppOfficialOutputTextFileWorkflowRequest appRequest = new(
            request?.ExportPackage,
            request?.OutputRootPath,
            request?.RequestedAt,
            request?.RequestedByUserId,
            request?.TerminalId,
            request?.Metadata);

        Result validation = AppOfficialOutputTextFileWorkflowRules.ValidateRequest(appRequest);

        if (validation.IsFailure)
        {
            return OfficialOutputTextFileWorkflowUiCommandResult.ValidationBlocked(
                status,
                validation.ErrorDetail);
        }

        AppOfficialOutputTextFileWorkflowResult result =
            await workflowGateway.RunAsync(appRequest, cancellationToken).ConfigureAwait(false);

        return OfficialOutputTextFileWorkflowUiCommandResult.FromWorkflowResult(status, result);
    }
}
