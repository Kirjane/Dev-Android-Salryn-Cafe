using Salryn.Core.Common;
using Salryn.Core.Services;
using Salryn.Infrastructure.OfficialOutput;

namespace Salryn.App.WinForms.Composition;

public sealed class CompositionOfficialOutputTextFileWorkflowGatewayAdapter : IAppOfficialOutputTextFileWorkflowGateway
{
    private readonly OfficialExternalOutputTextFileWorkflowRunner _workflowRunner;

    public CompositionOfficialOutputTextFileWorkflowGatewayAdapter(
        OfficialExternalOutputTextFileWorkflowRunner? workflowRunner = null)
    {
        _workflowRunner = workflowRunner ?? new OfficialExternalOutputTextFileWorkflowRunner();
    }

    public async Task<AppOfficialOutputTextFileWorkflowResult> RunAsync(
        AppOfficialOutputTextFileWorkflowRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return await Task.FromCanceled<AppOfficialOutputTextFileWorkflowResult>(cancellationToken).ConfigureAwait(false);
        }

        Result validation = AppOfficialOutputTextFileWorkflowRules.ValidateRequest(request);

        if (validation.IsFailure)
        {
            return AppOfficialOutputTextFileWorkflowResult.Failure(validation.ErrorDetail);
        }

        AppOfficialOutputTextFileWorkflowRequest normalizedRequest =
            AppOfficialOutputTextFileWorkflowRules.NormalizeRequest(request!);

        OfficialExternalOutputWorkflowRequest workflowRequest = new(
            normalizedRequest.ExportPackage,
            normalizedRequest.OutputRootPath,
            normalizedRequest.RequestedAt,
            normalizedRequest.RequestedByUserId,
            normalizedRequest.TerminalId,
            normalizedRequest.Metadata);

        Result<OfficialExternalOutputWorkflowResult> workflowResult =
            await _workflowRunner.RunAsync(workflowRequest, cancellationToken).ConfigureAwait(false);

        return workflowResult.IsSuccess
            ? AppOfficialOutputTextFileWorkflowResult.Success(workflowResult.Value)
            : AppOfficialOutputTextFileWorkflowResult.Failure(workflowResult.ErrorDetail);
    }
}
