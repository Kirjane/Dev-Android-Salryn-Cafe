using Salryn.Core.Common;
using Salryn.Core.Services;
using Salryn.Infrastructure.OfficialOutput;

namespace Salryn.Tests;

public static class J21Rmo242023OfficialTextFileExternalOutputAdapterChecks
{
    public static void Run()
    {
        string root = J21Rmo242023OfficialOutputPipelineFixture.CreateTempRoot();
        try
        {
            OfficialExternalOutputExecutionResult result = J21Rmo242023OfficialOutputPipelineFixture.ExecuteTextFile(root);
            J21Rmo242023OfficialOutputPipelineFixture.Require(result.Status == OfficialExternalOutputExecutionStatus.Succeeded, "09P execution must succeed.");
            J21Rmo242023OfficialOutputPipelineFixture.Require(result.Channel == OfficialExternalOutputChannel.File, "09P execution must use File channel.");
            J21Rmo242023OfficialOutputPipelineFixture.Require(result.WrittenArtifacts.Count >= 10, "09P must write manifest, package, ledger, dispatch, payloads, and receipt.");
            J21Rmo242023OfficialOutputPipelineFixture.Require(result.ExecutionReference.StartsWith("salryn://official-external-output-execution/", StringComparison.Ordinal), "09P execution reference must be hash-addressed.");
            J21Rmo242023OfficialOutputPipelineFixture.Require(result.SideEffectMarker == "EXTERNAL_OUTPUT_TEXT_FILE_WRITE_CONFIRMED", "09P execution marker must be present.");

            foreach (OfficialExternalOutputWrittenArtifact artifact in result.WrittenArtifacts)
            {
                string fullPath = Path.GetFullPath(Path.Combine(root, artifact.RelativePath));
                J21Rmo242023OfficialOutputPipelineFixture.Require(fullPath.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase), "09P output must stay under output root.");
                J21Rmo242023OfficialOutputPipelineFixture.Require(File.Exists(fullPath), "09P expected file must exist: " + artifact.RelativePath);
            }

            OfficialExternalOutputExecutionLedgerPackage ledger = J21Rmo242023OfficialOutputPipelineFixture.Value(new OfficialExternalOutputExecutionLedgerBuilder().Build(new[] { result }, J21Rmo242023OfficialOutputPipelineFixture.Timestamp));
            J21Rmo242023OfficialOutputPipelineFixture.Require(ledger.ExecutionLedgerReference.StartsWith("salryn://official-external-output-execution-ledger/", StringComparison.Ordinal), "09Q execution ledger reference must be hash-addressed.");
            J21Rmo242023OfficialOutputPipelineFixture.Require(ledger.TextResult.Text.Contains("EXTERNAL_OUTPUT_EXECUTION_LEDGER_IN_MEMORY_ONLY_NO_DATABASE", StringComparison.Ordinal), "09Q execution ledger marker must be present.");

            OfficialTextFileExternalOutputAdapter adapter = new();
            OfficialOutputExportPackage package = J21Rmo242023OfficialOutputPipelineFixture.CreatePackage();
            OfficialOutputDeliveryLedgerPackage deliveryLedger = J21Rmo242023OfficialOutputPipelineFixture.CreateDeliveryLedger(J21Rmo242023OfficialOutputPipelineFixture.CreateDelivery(package));
            OfficialExternalOutputDispatchPlan plan = J21Rmo242023OfficialOutputPipelineFixture.CreateFileDispatchPlan(package, deliveryLedger);
            OfficialExternalOutputExecutionRequest request = new(plan, package, deliveryLedger, root, J21Rmo242023OfficialOutputPipelineFixture.Timestamp, J21Rmo242023OfficialOutputPipelineFixture.UserId, "POS-09P", null);

            RequireFailure(adapter.ExecuteAsync(null!).GetAwaiter().GetResult(), "OfficialExternalOutputExecution.RequestRequired", "09P must reject null request.");
            RequireFailure(adapter.ExecuteAsync(request with { ExportPackage = null }).GetAwaiter().GetResult(), "OfficialExternalOutputExecution.ExportPackageRequired", "09P must reject null package.");
            RequireFailure(adapter.ExecuteAsync(request with { DispatchPlan = null }).GetAwaiter().GetResult(), "OfficialExternalOutputExecution.DispatchPlanRequired", "09P must reject null dispatch plan.");
            RequireFailure(adapter.ExecuteAsync(request with { DeliveryLedgerPackage = null }).GetAwaiter().GetResult(), "OfficialExternalOutputExecution.DeliveryLedgerRequired", "09P must reject null ledger.");
            RequireFailure(adapter.ExecuteAsync(request with { OutputRootPath = " " }).GetAwaiter().GetResult(), "OfficialExternalOutputExecution.OutputRootRequired", "09P must reject blank output root.");
            RequireFailure(adapter.ExecuteAsync(request with { DispatchPlan = plan with { Items = plan.Items.Select(i => i with { Channel = OfficialExternalOutputChannel.Pdf }).ToArray() } }).GetAwaiter().GetResult(), "OfficialExternalOutputExecution.FileDispatchPlanRequired", "09P must reject non-File channels.");

            OfficialTextFileOutputPathPolicy policy = new();
            J21Rmo242023OfficialOutputPipelineFixture.Require(policy.ResolveChild(root, "../escape.txt").IsFailure, "09P path policy must reject traversal.");
        }
        finally
        {
            J21Rmo242023OfficialOutputPipelineFixture.CleanupTempRoot(root);
        }
    }

    private static void RequireFailure<T>(Result<T> result, string expectedCode, string message)
    {
        if (result.IsSuccess || result.ErrorDetail.Code != expectedCode)
        {
            throw new InvalidOperationException(message);
        }
    }
}
