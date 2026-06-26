using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Salryn.Core.Common;

namespace Salryn.Core.Services;

public sealed record OfficialExternalOutputWorkflowRequest(
    OfficialOutputExportPackage? ExportPackage,
    string? OutputRootPath,
    DateTimeOffset? RequestedAt,
    Guid? RequestedByUserId,
    string? TerminalId,
    IReadOnlyDictionary<string, string>? Metadata);

public sealed record OfficialExternalOutputWorkflowResult(
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
    bool Verified,
    string WorkflowHash,
    string WorkflowReference,
    string? OutputRootPath,
    string SideEffectMarker,
    DateTimeOffset? CompletedAt,
    OfficialExternalOutputWorkflowTextRenderResult TextResult);

public sealed record OfficialExternalOutputWorkflowTextRenderResult(
    IReadOnlyList<string> Lines,
    string Text);

public sealed class OfficialExternalOutputWorkflowResultBuilder
{
    private const string WorkflowReferencePrefix = "salryn://official-external-output-workflow/";
    public const string WorkflowVerifiedMarker = "OFFICIAL_OUTPUT_TEXT_FILE_WORKFLOW_VERIFIED";

    public Result<OfficialExternalOutputWorkflowResult> Build(
        OfficialOutputExportPackage? package,
        OfficialOutputDeliveryResult? delivery,
        OfficialOutputDeliveryLedgerPackage? deliveryLedger,
        OfficialExternalOutputDispatchPlan? dispatchPlan,
        OfficialExternalOutputExecutionResult? execution,
        OfficialExternalOutputExecutionLedgerPackage? executionLedger,
        OfficialExternalOutputVerificationResult? verification,
        string? outputRootPath,
        DateTimeOffset? completedAt = null)
    {
        if (package is null || delivery is null || deliveryLedger is null || dispatchPlan is null ||
            execution is null || executionLedger is null || verification is null)
        {
            return Result<OfficialExternalOutputWorkflowResult>.Fail(Error.Validation(
                "OfficialExternalOutputWorkflow.ChainRequired",
                "Official external output workflow requires a complete output chain."));
        }

        OfficialExternalOutputWorkflowTextRenderResult text = Render(
            package,
            delivery,
            deliveryLedger,
            dispatchPlan,
            execution,
            executionLedger,
            verification,
            outputRootPath,
            completedAt);
        string hash = ComputeContentHash(text.Text);

        return Result<OfficialExternalOutputWorkflowResult>.Success(
            new OfficialExternalOutputWorkflowResult(
                package.PackageReference,
                package.PackageHash,
                delivery.Artifact.DeliveryReference,
                delivery.Artifact.DeliveryHash,
                deliveryLedger.LedgerReference,
                deliveryLedger.LedgerHash,
                dispatchPlan.DispatchPlanReference,
                dispatchPlan.DispatchPlanHash,
                execution.ExecutionReference,
                execution.ExecutionHash,
                executionLedger.ExecutionLedgerReference,
                executionLedger.ExecutionLedgerHash,
                verification.VerificationReference,
                verification.VerificationHash,
                verification.VerificationStatus == OfficialExternalOutputVerificationStatus.Passed,
                hash,
                WorkflowReferencePrefix + hash,
                outputRootPath,
                WorkflowVerifiedMarker,
                completedAt,
                text));
    }

    private static OfficialExternalOutputWorkflowTextRenderResult Render(
        OfficialOutputExportPackage package,
        OfficialOutputDeliveryResult delivery,
        OfficialOutputDeliveryLedgerPackage deliveryLedger,
        OfficialExternalOutputDispatchPlan dispatchPlan,
        OfficialExternalOutputExecutionResult execution,
        OfficialExternalOutputExecutionLedgerPackage executionLedger,
        OfficialExternalOutputVerificationResult verification,
        string? outputRootPath,
        DateTimeOffset? completedAt)
    {
        List<string> lines = new()
        {
            "SALRYN OFFICIAL OUTPUT TEXT FILE WORKFLOW",
            "[WORKFLOW]",
            "CompletedAt: " + (completedAt.HasValue ? completedAt.Value.ToString("O", CultureInfo.InvariantCulture) : string.Empty),
            "OutputRootRuntimePath: " + (outputRootPath ?? string.Empty),
            "Verified: " + (verification.VerificationStatus == OfficialExternalOutputVerificationStatus.Passed),
            "WorkflowHash: COMPUTED_IN_WORKFLOW_RESULT",
            "WorkflowReference: COMPUTED_IN_WORKFLOW_RESULT",
            "[CHAIN]",
            "PackageReference: " + package.PackageReference,
            "PackageHash: " + package.PackageHash,
            "DeliveryReference: " + delivery.Artifact.DeliveryReference,
            "DeliveryHash: " + delivery.Artifact.DeliveryHash,
            "DeliveryLedgerReference: " + deliveryLedger.LedgerReference,
            "DeliveryLedgerHash: " + deliveryLedger.LedgerHash,
            "DispatchPlanReference: " + dispatchPlan.DispatchPlanReference,
            "DispatchPlanHash: " + dispatchPlan.DispatchPlanHash,
            "ExecutionReference: " + execution.ExecutionReference,
            "ExecutionHash: " + execution.ExecutionHash,
            "ExecutionLedgerReference: " + executionLedger.ExecutionLedgerReference,
            "ExecutionLedgerHash: " + executionLedger.ExecutionLedgerHash,
            "VerificationReference: " + verification.VerificationReference,
            "VerificationHash: " + verification.VerificationHash,
            "[EVIDENCE]",
            "HashAlgorithm: SHA-256",
            "WorkflowReferencePrefix: " + WorkflowReferencePrefix,
            "WorkflowSideEffectMarker: " + WorkflowVerifiedMarker
        };

        return new OfficialExternalOutputWorkflowTextRenderResult(lines, string.Join("\n", lines));
    }

    private static string NormalizeText(string? text)
    {
        return text?.Replace("\r\n", "\n").Replace('\r', '\n').Trim() ?? string.Empty;
    }

    private static string ComputeContentHash(string text)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeText(text)));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
