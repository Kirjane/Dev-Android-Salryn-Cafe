using System.Security.Cryptography;
using System.Text;
using Salryn.Core.Common;
using Salryn.Core.Services;
using Salryn.Infrastructure.OfficialOutput;

internal static class OfficialTextFileExternalOutputSmokeChecks
{
    private static readonly DateTimeOffset Timestamp = new(2026, 6, 26, 19, 30, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.Parse("eeeeeeee-ffff-aaaa-bbbb-cccccccccccc");

    public static void Run(string projectRoot)
    {
        _ = projectRoot;
        Console.WriteLine("===== OFFICIAL TEXT FILE OUTPUT SMOKE CHECKS =====");
        string root = Path.Combine(Path.GetTempPath(), "salryn-official-output-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            OfficialOutputExportPackage package = CreatePackage();
            OfficialOutputDeliveryResult delivery = Value(new OfficialInMemoryOutputDeliveryAdapter().DeliverAsync(new OfficialOutputDeliveryRequest(package, OfficialOutputDeliveryTarget.InMemory, UserId, Timestamp, "POS-SMOKE", null)).GetAwaiter().GetResult());
            OfficialOutputDeliveryLedgerPackage ledger = Value(new OfficialOutputDeliveryReceiptLedgerBuilder().Build(new[] { delivery }, Timestamp));
            OfficialExternalOutputDispatchPlan plan = CreateFileDispatchPlan(package, ledger);
            OfficialTextFileExternalOutputAdapter adapter = new();
            OfficialExternalOutputExecutionRequest executionRequest = new(plan, package, ledger, root, Timestamp, UserId, "POS-SMOKE", null);
            OfficialExternalOutputExecutionResult execution = Value(adapter.ExecuteAsync(executionRequest).GetAwaiter().GetResult());

            Require(execution.WrittenArtifacts.Count >= 10, "Expected controlled output files to be written.");
            foreach (OfficialExternalOutputWrittenArtifact artifact in execution.WrittenArtifacts)
            {
                string fullPath = Path.GetFullPath(Path.Combine(root, artifact.RelativePath));
                Require(fullPath.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase), "Output path escaped root.");
                Require(File.Exists(fullPath), "Expected file missing: " + artifact.RelativePath);
            }

            string manifestPath = Path.GetFullPath(Path.Combine(root, execution.WrittenArtifacts.First(static artifact => artifact.Role == OfficialExternalOutputWrittenArtifactRole.Manifest).RelativePath));
            string manifestBeforeDuplicate = File.ReadAllText(manifestPath, Encoding.UTF8);
            Result<OfficialExternalOutputExecutionResult> duplicateExecution = adapter.ExecuteAsync(executionRequest).GetAwaiter().GetResult();
            RequireFailure(
                duplicateExecution,
                "OfficialTextFileOutput.OutputPackageAlreadyExists",
                "Duplicate official output package run must be refused.");
            Require(
                string.Equals(File.ReadAllText(manifestPath, Encoding.UTF8), manifestBeforeDuplicate, StringComparison.Ordinal),
                "Existing official output package files must not be overwritten.");

            OfficialExternalOutputExecutionLedgerPackage executionLedger = Value(new OfficialExternalOutputExecutionLedgerBuilder().Build(new[] { execution }, Timestamp));
            OfficialExternalOutputVerificationResult verification = Value(new OfficialTextFileExternalOutputVerifier().Verify(root, execution, Timestamp));
            Require(verification.VerificationStatus == OfficialExternalOutputVerificationStatus.Passed, "Verification must pass after write.");

            string workflowRoot = Path.Combine(Path.GetTempPath(), "salryn-official-output-smoke-workflow-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workflowRoot);

            try
            {
                OfficialExternalOutputWorkflowResult workflow = Value(new OfficialExternalOutputTextFileWorkflowRunner().RunAsync(new OfficialExternalOutputWorkflowRequest(package, workflowRoot, Timestamp, UserId, "POS-SMOKE", null)).GetAwaiter().GetResult());
                Require(workflow.Verified, "Workflow must return verified result.");
                Require(workflow.ExecutionLedgerReference.StartsWith("salryn://official-external-output-execution-ledger/", StringComparison.Ordinal), "Execution ledger reference must be present.");
            }
            finally
            {
                if (Directory.Exists(workflowRoot))
                {
                    Directory.Delete(workflowRoot, recursive: true);
                }
            }

            Require(executionLedger.ExecutionLedgerReference.StartsWith("salryn://official-external-output-execution-ledger/", StringComparison.Ordinal), "Execution ledger smoke result must be hash-addressed.");

            Console.WriteLine("Official text-file output smoke checks succeeded.");
            Console.WriteLine();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static OfficialExternalOutputDispatchPlan CreateFileDispatchPlan(OfficialOutputExportPackage package, OfficialOutputDeliveryLedgerPackage ledger)
    {
        OfficialExternalOutputAdapterContract contract = new(
            "contract-file-text-output",
            nameof(OfficialTextFileExternalOutputAdapter),
            new[] { new OfficialExternalOutputCapability(OfficialExternalOutputChannel.File, OfficialExternalOutputIntent.ContractOnly, "Controlled text-file output contract.") },
            OfficialExternalOutputAdapterContractValidator.ContractOnlySideEffectMarker);

        return Value(new OfficialExternalOutputDispatchPlanBuilder().Build(new OfficialExternalOutputDispatchRequest(package, ledger, OfficialExternalOutputChannel.File, OfficialExternalOutputIntent.ContractOnly, "TextFile", contract, Timestamp, UserId, "POS-SMOKE", null), Timestamp));
    }

    private static OfficialOutputExportPackage CreatePackage()
    {
        IReadOnlyList<OfficialOutputExportPayload> payloads = new[]
        {
            Payload(OfficialOutputArtifactFamily.ReceiptInvoice, "OFFICIAL_TEXT_PREVIEW", "salryn://official-text-preview/receipt", "receipt-line-1\nreceipt-line-2"),
            Payload(OfficialOutputArtifactFamily.ReadingReport, "OFFICIAL_X_READING_PREVIEW", "salryn://official-reading-preview/XReading/x", "x-reading-line"),
            Payload(OfficialOutputArtifactFamily.ReadingReport, "OFFICIAL_Z_READING_PREVIEW", "salryn://official-reading-preview/ZReading/z", "z-reading-line"),
            Payload(OfficialOutputArtifactFamily.AuditEvidenceReport, "OFFICIAL_AUDIT_TRAIL_PREVIEW", "salryn://official-audit-evidence-preview/AuditTrail/audit", "audit-line"),
            Payload(OfficialOutputArtifactFamily.AuditEvidenceReport, "OFFICIAL_ELECTRONIC_JOURNAL_PREVIEW", "salryn://official-audit-evidence-preview/ElectronicJournal/ej", "electronic-journal-line")
        };
        OfficialOutputArtifactManifestPackage manifest = Value(new OfficialOutputArtifactManifestBuilder().Build(payloads.Select(payload => new OfficialOutputArtifactManifestEntry(payload.ArtifactFamily, payload.ArtifactLabel, payload.ArtifactReference, payload.ContentHash, payload.HashAlgorithm, payload.LineCount, "Preview", Source(payload), null)).ToArray(), Timestamp));
        return Value(new OfficialOutputExportPackageBuilder().Build(manifest, payloads));
    }

    private static OfficialOutputExportPayload Payload(OfficialOutputArtifactFamily family, string label, string reference, string text)
    {
        return new OfficialOutputExportPayload(family, label, reference, Hash(text), "SHA-256", text.Split('\n').Length, text);
    }

    private static OfficialOutputArtifactManifestSource Source(OfficialOutputExportPayload payload)
    {
        return new OfficialOutputArtifactManifestSource(payload.ArtifactFamily + "Preview", payload.ArtifactReference, payload.ArtifactFamily == OfficialOutputArtifactFamily.ReceiptInvoice ? "OfficialReceipt" : null, payload.ArtifactFamily == OfficialOutputArtifactFamily.ReceiptInvoice ? "OR-2026-000001" : null, null, null, null, "POS-SMOKE", "cashier-smoke", new DateOnly(2026, 6, 26), null, null, payload.ArtifactFamily == OfficialOutputArtifactFamily.ReceiptInvoice ? false : null, payload.ArtifactFamily == OfficialOutputArtifactFamily.ReceiptInvoice ? "Active" : null, Timestamp, null);
    }

    private static string Hash(string text)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private static T Value<T>(Result<T> result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(result.Error);
        }
        return result.Value;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireFailure<T>(Result<T> result, string expectedCode, string message)
    {
        if (result.IsSuccess || result.ErrorDetail.Code != expectedCode)
        {
            throw new InvalidOperationException(
                message + $" Expected {expectedCode}, got {(result.IsSuccess ? "success" : result.ErrorDetail.Code)}.");
        }
    }
}
