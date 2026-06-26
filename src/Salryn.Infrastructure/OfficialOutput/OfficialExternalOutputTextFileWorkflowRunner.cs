using Salryn.Core.Common;
using Salryn.Core.Services;

namespace Salryn.Infrastructure.OfficialOutput;

public sealed class OfficialExternalOutputTextFileWorkflowRunner
{
    private readonly OfficialInMemoryOutputDeliveryAdapter _deliveryAdapter;
    private readonly OfficialOutputDeliveryReceiptLedgerBuilder _deliveryLedgerBuilder;
    private readonly OfficialExternalOutputDispatchPlanBuilder _dispatchPlanBuilder;
    private readonly OfficialTextFileExternalOutputAdapter _textFileAdapter;
    private readonly OfficialExternalOutputExecutionLedgerBuilder _executionLedgerBuilder;
    private readonly OfficialTextFileExternalOutputVerifier _verifier;
    private readonly OfficialExternalOutputWorkflowResultBuilder _workflowResultBuilder;

    public OfficialExternalOutputTextFileWorkflowRunner(
        OfficialInMemoryOutputDeliveryAdapter? deliveryAdapter = null,
        OfficialOutputDeliveryReceiptLedgerBuilder? deliveryLedgerBuilder = null,
        OfficialExternalOutputDispatchPlanBuilder? dispatchPlanBuilder = null,
        OfficialTextFileExternalOutputAdapter? textFileAdapter = null,
        OfficialExternalOutputExecutionLedgerBuilder? executionLedgerBuilder = null,
        OfficialTextFileExternalOutputVerifier? verifier = null,
        OfficialExternalOutputWorkflowResultBuilder? workflowResultBuilder = null)
    {
        _deliveryAdapter = deliveryAdapter ?? new OfficialInMemoryOutputDeliveryAdapter();
        _deliveryLedgerBuilder = deliveryLedgerBuilder ?? new OfficialOutputDeliveryReceiptLedgerBuilder();
        _dispatchPlanBuilder = dispatchPlanBuilder ?? new OfficialExternalOutputDispatchPlanBuilder();
        _textFileAdapter = textFileAdapter ?? new OfficialTextFileExternalOutputAdapter();
        _executionLedgerBuilder = executionLedgerBuilder ?? new OfficialExternalOutputExecutionLedgerBuilder();
        _verifier = verifier ?? new OfficialTextFileExternalOutputVerifier();
        _workflowResultBuilder = workflowResultBuilder ?? new OfficialExternalOutputWorkflowResultBuilder();
    }

    public async Task<Result<OfficialExternalOutputWorkflowResult>> RunAsync(
        OfficialExternalOutputWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return Result<OfficialExternalOutputWorkflowResult>.Fail(Error.Validation(
                "OfficialExternalOutputWorkflow.RequestRequired",
                "Official external output workflow request is required."));
        }

        if (request.ExportPackage is null)
        {
            return Result<OfficialExternalOutputWorkflowResult>.Fail(Error.Validation(
                "OfficialExternalOutputWorkflow.PackageRequired",
                "Official external output workflow requires an export package."));
        }

        OfficialOutputDeliveryRequest deliveryRequest = new(
            request.ExportPackage,
            OfficialOutputDeliveryTarget.InMemory,
            request.RequestedByUserId,
            request.RequestedAt,
            request.TerminalId,
            request.Metadata);
        Result<OfficialOutputDeliveryResult> delivery = await _deliveryAdapter.DeliverAsync(deliveryRequest, cancellationToken);

        if (delivery.IsFailure)
        {
            return Result<OfficialExternalOutputWorkflowResult>.Fail(delivery.ErrorDetail);
        }

        Result<OfficialOutputDeliveryLedgerPackage> deliveryLedger = _deliveryLedgerBuilder.Build(
            new[] { delivery.Value },
            request.RequestedAt,
            request.Metadata);

        if (deliveryLedger.IsFailure)
        {
            return Result<OfficialExternalOutputWorkflowResult>.Fail(deliveryLedger.ErrorDetail);
        }

        OfficialExternalOutputAdapterContract contract = new(
            "contract-file-text-output",
            nameof(OfficialTextFileExternalOutputAdapter),
            new[]
            {
                new OfficialExternalOutputCapability(
                    OfficialExternalOutputChannel.File,
                    OfficialExternalOutputIntent.ContractOnly,
                    "Controlled text-file output contract.")
            },
            OfficialExternalOutputAdapterContractValidator.ContractOnlySideEffectMarker);

        OfficialExternalOutputDispatchRequest dispatchRequest = new(
            request.ExportPackage,
            deliveryLedger.Value,
            OfficialExternalOutputChannel.File,
            OfficialExternalOutputIntent.ContractOnly,
            "TextFile",
            contract,
            request.RequestedAt,
            request.RequestedByUserId,
            request.TerminalId,
            request.Metadata);
        Result<OfficialExternalOutputDispatchPlan> dispatchPlan = _dispatchPlanBuilder.Build(
            dispatchRequest,
            request.RequestedAt,
            request.Metadata);

        if (dispatchPlan.IsFailure)
        {
            return Result<OfficialExternalOutputWorkflowResult>.Fail(dispatchPlan.ErrorDetail);
        }

        OfficialExternalOutputExecutionRequest executionRequest = new(
            dispatchPlan.Value,
            request.ExportPackage,
            deliveryLedger.Value,
            request.OutputRootPath,
            request.RequestedAt,
            request.RequestedByUserId,
            request.TerminalId,
            request.Metadata);
        Result<OfficialExternalOutputExecutionResult> execution = await _textFileAdapter.ExecuteAsync(
            executionRequest,
            cancellationToken);

        if (execution.IsFailure)
        {
            return Result<OfficialExternalOutputWorkflowResult>.Fail(execution.ErrorDetail);
        }

        Result<OfficialExternalOutputExecutionLedgerPackage> executionLedger = _executionLedgerBuilder.Build(
            new[] { execution.Value },
            request.RequestedAt);

        if (executionLedger.IsFailure)
        {
            return Result<OfficialExternalOutputWorkflowResult>.Fail(executionLedger.ErrorDetail);
        }

        Result<OfficialExternalOutputVerificationResult> verification = _verifier.Verify(
            request.OutputRootPath,
            execution.Value,
            request.RequestedAt);

        if (verification.IsFailure)
        {
            return Result<OfficialExternalOutputWorkflowResult>.Fail(verification.ErrorDetail);
        }

        return _workflowResultBuilder.Build(
            request.ExportPackage,
            delivery.Value,
            deliveryLedger.Value,
            dispatchPlan.Value,
            execution.Value,
            executionLedger.Value,
            verification.Value,
            request.OutputRootPath,
            request.RequestedAt);
    }
}
