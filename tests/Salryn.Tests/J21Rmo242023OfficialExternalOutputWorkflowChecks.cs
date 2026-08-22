using Salryn.Core.Services;

namespace Salryn.Tests;

public static class J21Rmo242023OfficialExternalOutputWorkflowChecks
{
    public static void Run()
    {
        string root = J21Rmo242023OfficialOutputPipelineFixture.CreateTempRoot();
        try
        {
            OfficialExternalOutputWorkflowResult workflow = J21Rmo242023OfficialOutputPipelineFixture.RunWorkflow(root);
            J21Rmo242023OfficialOutputPipelineFixture.Require(workflow.Verified, "09S workflow must complete verified.");
            J21Rmo242023OfficialOutputPipelineFixture.Require(workflow.WorkflowReference.StartsWith("salryn://official-external-output-workflow/", StringComparison.Ordinal), "09S workflow reference must be hash-addressed.");
            J21Rmo242023OfficialOutputPipelineFixture.Require(workflow.SideEffectMarker == "OFFICIAL_OUTPUT_TEXT_FILE_WORKFLOW_VERIFIED", "09S workflow marker must be present.");
            J21Rmo242023OfficialOutputPipelineFixture.Require(workflow.PackageReference.StartsWith("salryn://official-output-export-package/", StringComparison.Ordinal), "09S must include package reference.");
            J21Rmo242023OfficialOutputPipelineFixture.Require(workflow.DeliveryReference.StartsWith("salryn://official-output-delivery/in-memory/", StringComparison.Ordinal), "09S must include delivery reference.");
            J21Rmo242023OfficialOutputPipelineFixture.Require(workflow.DeliveryLedgerReference.StartsWith("salryn://official-output-delivery-ledger/", StringComparison.Ordinal), "09S must include delivery ledger reference.");
            J21Rmo242023OfficialOutputPipelineFixture.Require(workflow.DispatchPlanReference.StartsWith("salryn://official-external-output-dispatch-plan/", StringComparison.Ordinal), "09S must include dispatch plan reference.");
            J21Rmo242023OfficialOutputPipelineFixture.Require(workflow.ExecutionReference.StartsWith("salryn://official-external-output-execution/", StringComparison.Ordinal), "09S must include execution reference.");
            J21Rmo242023OfficialOutputPipelineFixture.Require(workflow.ExecutionLedgerReference.StartsWith("salryn://official-external-output-execution-ledger/", StringComparison.Ordinal), "09S must include execution ledger reference.");
            J21Rmo242023OfficialOutputPipelineFixture.Require(workflow.VerificationReference.StartsWith("salryn://official-external-output-verification/", StringComparison.Ordinal), "09S must include verification reference.");
            J21Rmo242023OfficialOutputPipelineFixture.Require(workflow.TextResult.Text.Contains("SALRYN OFFICIAL OUTPUT TEXT FILE WORKFLOW", StringComparison.Ordinal), "09S workflow text title must be present.");
            J21Rmo242023OfficialOutputPipelineFixture.Require(workflow.TextResult.Text.Contains("[WORKFLOW]", StringComparison.Ordinal), "09S workflow text must include workflow section.");
            J21Rmo242023OfficialOutputPipelineFixture.Require(workflow.TextResult.Text.Contains("[CHAIN]", StringComparison.Ordinal), "09S workflow text must include chain section.");
            J21Rmo242023OfficialOutputPipelineFixture.Require(workflow.TextResult.Text.Contains("[EVIDENCE]", StringComparison.Ordinal), "09S workflow text must include evidence section.");
        }
        finally
        {
            J21Rmo242023OfficialOutputPipelineFixture.CleanupTempRoot(root);
        }
    }
}
