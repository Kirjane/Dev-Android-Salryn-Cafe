using Salryn.Core.Common;
using Salryn.Core.Contracts;
using Salryn.Core.Hosting;
using Salryn.Host.Local.Contracts;

namespace Salryn.App.WinForms.Composition;

public sealed class CompositionLocalHostOfficialOutputTextFileWorkflowGatewayAdapter :
    IAppOfficialOutputTextFileWorkflowGateway
{
    private readonly SalrynLocalHostCompositionHolder? localHostCompositionHolder;
    private readonly SalrynLocalHostRequestContextProvider? requestContextProvider;
    private readonly SalrynLocalHostSessionIdentityProvider? sessionIdentityProvider;

    public CompositionLocalHostOfficialOutputTextFileWorkflowGatewayAdapter()
    {
    }

    public CompositionLocalHostOfficialOutputTextFileWorkflowGatewayAdapter(
        SalrynLocalHostCompositionHolder localHostCompositionHolder)
    {
        ArgumentNullException.ThrowIfNull(localHostCompositionHolder);

        this.localHostCompositionHolder = localHostCompositionHolder;
    }

    public CompositionLocalHostOfficialOutputTextFileWorkflowGatewayAdapter(
        SalrynLocalHostCompositionHolder localHostCompositionHolder,
        SalrynLocalHostRequestContextProvider requestContextProvider)
    {
        ArgumentNullException.ThrowIfNull(localHostCompositionHolder);
        ArgumentNullException.ThrowIfNull(requestContextProvider);

        this.localHostCompositionHolder = localHostCompositionHolder;
        this.requestContextProvider = requestContextProvider;
    }

    public CompositionLocalHostOfficialOutputTextFileWorkflowGatewayAdapter(
        SalrynLocalHostCompositionHolder localHostCompositionHolder,
        SalrynLocalHostRequestContextProvider requestContextProvider,
        SalrynLocalHostSessionIdentityProvider sessionIdentityProvider)
    {
        ArgumentNullException.ThrowIfNull(localHostCompositionHolder);
        ArgumentNullException.ThrowIfNull(requestContextProvider);
        ArgumentNullException.ThrowIfNull(sessionIdentityProvider);

        this.localHostCompositionHolder = localHostCompositionHolder;
        this.requestContextProvider = requestContextProvider;
        this.sessionIdentityProvider = sessionIdentityProvider;
    }

    public async Task<AppOfficialOutputTextFileWorkflowResult> RunAsync(
        AppOfficialOutputTextFileWorkflowRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return await Task.FromCanceled<AppOfficialOutputTextFileWorkflowResult>(cancellationToken)
                .ConfigureAwait(false);
        }

        Result validation = AppOfficialOutputTextFileWorkflowRules.ValidateRequest(request);

        if (validation.IsFailure)
        {
            return AppOfficialOutputTextFileWorkflowResult.Failure(validation.ErrorDetail);
        }

        AppOfficialOutputTextFileWorkflowRequest normalizedRequest =
            AppOfficialOutputTextFileWorkflowRules.NormalizeRequest(request!);

        SalrynLocalHostCompositionHolder? holder = localHostCompositionHolder;

        if (holder is null)
        {
            return Unavailable("Local Host official output text-file workflow holder is not available.");
        }

        SalrynLocalHostRequestContextProvider? requestProvider = requestContextProvider;

        if (requestProvider is null)
        {
            return Unavailable("Local Host official output text-file workflow request context provider is not available.");
        }

        SalrynLocalHostSessionIdentityProvider? sessionProvider = sessionIdentityProvider;

        if (sessionProvider is null)
        {
            return Unavailable("Local Host official output text-file workflow session identity provider is not available.");
        }

        return await TryDispatchThroughHolderAsync(
                holder,
                requestProvider,
                sessionProvider,
                normalizedRequest,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<AppOfficialOutputTextFileWorkflowResult> TryDispatchThroughHolderAsync(
        SalrynLocalHostCompositionHolder holder,
        SalrynLocalHostRequestContextProvider requestProvider,
        SalrynLocalHostSessionIdentityProvider sessionProvider,
        AppOfficialOutputTextFileWorkflowRequest normalizedRequest,
        CancellationToken cancellationToken)
    {
        if (!sessionProvider.TryGetAuthenticatedIdentity(
                out Guid userId,
                out Guid roleId))
        {
            return Unavailable("Local Host official output text-file workflow authenticated app session identity is not available.");
        }

        if (!requestProvider.TryCreateAuthenticatedContext(
                userId: userId,
                roleId: roleId,
                cashierSessionId: null,
                requestedAt: normalizedRequest.RequestedAt.GetValueOrDefault(),
                out HostRequestContext? context) ||
            context is null)
        {
            return Unavailable("Local Host official output text-file workflow request context is not available.");
        }

        HostRequestEnvelope<LocalHostOfficialOutputTextFileWorkflowRequest> hostRequest = new(
            RequestId: Guid.NewGuid(),
            OperationKind: HostOperationKind.Command,
            OperationName: HostOperationNames.OfficialOutputTextFileWorkflow,
            Context: context,
            Payload: new LocalHostOfficialOutputTextFileWorkflowRequest(
                normalizedRequest.ExportPackage,
                normalizedRequest.OutputRootPath,
                normalizedRequest.RequestedAt,
                normalizedRequest.RequestedByUserId,
                normalizedRequest.TerminalId,
                normalizedRequest.Metadata));

        HostResponseEnvelope<LocalHostOfficialOutputTextFileWorkflowResponse> response =
            await holder.Composition.Runtime.DispatchAsync<
                    LocalHostOfficialOutputTextFileWorkflowRequest,
                    LocalHostOfficialOutputTextFileWorkflowResponse>(
                    hostRequest,
                    cancellationToken)
                .ConfigureAwait(false);

        if (response.IsSuccess && response.Payload is not null)
        {
            return MapPayload(response.Payload);
        }

        return AppOfficialOutputTextFileWorkflowResult.Failure(
            Error.Failure(
                response.Error?.Code ?? "AppLocalHostOfficialOutputTextFileWorkflow.DispatchUnavailable",
                response.Error?.Message ?? "Local Host official output text-file workflow dispatch did not return a response."));
    }

    private static AppOfficialOutputTextFileWorkflowResult MapPayload(
        LocalHostOfficialOutputTextFileWorkflowResponse payload)
    {
        return new AppOfficialOutputTextFileWorkflowResult(
            payload.Succeeded,
            payload.Verified,
            payload.StatusCode,
            payload.StatusMessage,
            payload.ErrorCode,
            payload.ErrorMessage,
            payload.References is null
                ? null
                : new AppOfficialOutputTextFileWorkflowReferenceSummary(
                    payload.References.PackageReference,
                    payload.References.PackageHash,
                    payload.References.DeliveryReference,
                    payload.References.DeliveryHash,
                    payload.References.DeliveryLedgerReference,
                    payload.References.DeliveryLedgerHash,
                    payload.References.DispatchPlanReference,
                    payload.References.DispatchPlanHash,
                    payload.References.ExecutionReference,
                    payload.References.ExecutionHash,
                    payload.References.ExecutionLedgerReference,
                    payload.References.ExecutionLedgerHash,
                    payload.References.VerificationReference,
                    payload.References.VerificationHash,
                    payload.References.WorkflowReference,
                    payload.References.WorkflowHash),
            payload.RuntimeOutputRootPath);
    }

    private static AppOfficialOutputTextFileWorkflowResult Unavailable(string message)
    {
        return AppOfficialOutputTextFileWorkflowResult.Failure(
            Error.Failure(
                "AppLocalHostOfficialOutputTextFileWorkflow.LocalHostUnavailable",
                message));
    }
}
