using Salryn.Core.Common;
using Salryn.Core.Contracts;
using Salryn.Core.Hosting;
using Salryn.Core.Services;
using Salryn.Host.Local.Contracts;
using Salryn.Host.Local.Dispatch;

namespace Salryn.Host.Local.Handlers;

public sealed class LocalHostOfficialOutputTextFileWorkflowHandler :
    IHostOperationHandler<LocalHostOfficialOutputTextFileWorkflowRequest, LocalHostOfficialOutputTextFileWorkflowResponse>
{
    private readonly IOfficialOutputTextFileWorkflowExecutionPort _executionPort;

    public LocalHostOfficialOutputTextFileWorkflowHandler(
        IOfficialOutputTextFileWorkflowExecutionPort executionPort)
    {
        _executionPort = executionPort ?? throw new ArgumentNullException(nameof(executionPort));
    }

    public async Task<HostResponseEnvelope<LocalHostOfficialOutputTextFileWorkflowResponse>> HandleAsync(
        HostRequestEnvelope<LocalHostOfficialOutputTextFileWorkflowRequest> request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return LocalHostDispatchResponses.Failure<LocalHostOfficialOutputTextFileWorkflowResponse>(
                requestId: Guid.Empty,
                code: LocalHostDispatchErrorCodes.RequestEnvelopeMissing,
                message: "Local Host official output text-file workflow request envelope must be provided.",
                type: ErrorType.Validation);
        }

        Result validation = LocalHostOfficialOutputTextFileWorkflowRules.ValidateRequest(request.Payload);

        if (validation.IsFailure)
        {
            return SuccessEnvelope(
                request.RequestId,
                LocalHostOfficialOutputTextFileWorkflowResponse.Failure(validation.ErrorDetail));
        }

        LocalHostOfficialOutputTextFileWorkflowRequest normalizedRequest =
            LocalHostOfficialOutputTextFileWorkflowRules.NormalizeRequest(request.Payload);

        OfficialExternalOutputWorkflowRequest workflowRequest = new(
            normalizedRequest.ExportPackage,
            normalizedRequest.OutputRootPath,
            normalizedRequest.RequestedAt,
            normalizedRequest.RequestedByUserId,
            normalizedRequest.TerminalId,
            normalizedRequest.Metadata);

        Result<OfficialExternalOutputWorkflowResult> workflowResult =
            await _executionPort.RunAsync(workflowRequest, cancellationToken).ConfigureAwait(false);

        return workflowResult.IsSuccess
            ? SuccessEnvelope(
                request.RequestId,
                LocalHostOfficialOutputTextFileWorkflowResponse.Success(workflowResult.Value))
            : SuccessEnvelope(
                request.RequestId,
                LocalHostOfficialOutputTextFileWorkflowResponse.Failure(workflowResult.ErrorDetail));
    }

    private static HostResponseEnvelope<LocalHostOfficialOutputTextFileWorkflowResponse> SuccessEnvelope(
        Guid requestId,
        LocalHostOfficialOutputTextFileWorkflowResponse payload)
    {
        return new HostResponseEnvelope<LocalHostOfficialOutputTextFileWorkflowResponse>(
            RequestId: requestId,
            IsSuccess: true,
            Payload: payload,
            Error: null);
    }
}
