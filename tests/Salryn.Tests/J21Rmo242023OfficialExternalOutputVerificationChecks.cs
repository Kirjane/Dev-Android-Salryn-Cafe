using Salryn.Core.Services;
using Salryn.Infrastructure.OfficialOutput;

namespace Salryn.Tests;

public static class J21Rmo242023OfficialExternalOutputVerificationChecks
{
    public static void Run()
    {
        string root = J21Rmo242023OfficialOutputPipelineFixture.CreateTempRoot();
        try
        {
            OfficialExternalOutputExecutionResult execution = J21Rmo242023OfficialOutputPipelineFixture.ExecuteTextFile(root);
            OfficialTextFileExternalOutputVerifier verifier = new();
            OfficialExternalOutputVerificationResult verification = J21Rmo242023OfficialOutputPipelineFixture.Value(verifier.Verify(root, execution, J21Rmo242023OfficialOutputPipelineFixture.Timestamp));
            J21Rmo242023OfficialOutputPipelineFixture.Require(verification.VerificationStatus == OfficialExternalOutputVerificationStatus.Passed, "09R verification must pass correct output.");
            J21Rmo242023OfficialOutputPipelineFixture.Require(verification.VerificationReference.StartsWith("salryn://official-external-output-verification/", StringComparison.Ordinal), "09R verification reference must be hash-addressed.");
            J21Rmo242023OfficialOutputPipelineFixture.Require(verification.TextResult.Text.Contains("EXTERNAL_OUTPUT_TEXT_FILE_VERIFICATION_READ_ONLY", StringComparison.Ordinal), "09R verification marker must be present.");

            OfficialExternalOutputWrittenArtifact first = execution.WrittenArtifacts.First(static artifact => artifact.Role != OfficialExternalOutputWrittenArtifactRole.ExecutionReceipt);
            string firstPath = Path.Combine(root, first.RelativePath);
            string original = File.ReadAllText(firstPath);
            File.WriteAllText(firstPath, original + "\ntampered");
            OfficialExternalOutputVerificationResult tampered = J21Rmo242023OfficialOutputPipelineFixture.Value(verifier.Verify(root, execution, J21Rmo242023OfficialOutputPipelineFixture.Timestamp));
            J21Rmo242023OfficialOutputPipelineFixture.Require(tampered.VerificationStatus == OfficialExternalOutputVerificationStatus.Failed, "09R verification must fail tampered file.");

            File.WriteAllText(firstPath, original);
            File.Delete(firstPath);
            OfficialExternalOutputVerificationResult missing = J21Rmo242023OfficialOutputPipelineFixture.Value(verifier.Verify(root, execution, J21Rmo242023OfficialOutputPipelineFixture.Timestamp));
            J21Rmo242023OfficialOutputPipelineFixture.Require(missing.VerificationStatus == OfficialExternalOutputVerificationStatus.Failed, "09R verification must fail missing file.");

            OfficialExternalOutputExecutionResult traversal = execution with
            {
                WrittenArtifacts = execution.WrittenArtifacts.Select((artifact, index) => index == 0 ? artifact with { RelativePath = "../escape.txt" } : artifact).ToArray()
            };
            J21Rmo242023OfficialOutputPipelineFixture.Require(verifier.Verify(root, traversal, J21Rmo242023OfficialOutputPipelineFixture.Timestamp).IsFailure, "09R verification must reject traversal relative path.");
        }
        finally
        {
            J21Rmo242023OfficialOutputPipelineFixture.CleanupTempRoot(root);
        }
    }
}
