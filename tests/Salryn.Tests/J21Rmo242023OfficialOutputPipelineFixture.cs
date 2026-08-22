using System.Security.Cryptography;
using System.Text;
using Salryn.Core.Common;
using Salryn.Core.Services;
using Salryn.Infrastructure.OfficialOutput;

namespace Salryn.Tests;

internal static class J21Rmo242023OfficialOutputPipelineFixture
{
    public static readonly DateTimeOffset Timestamp = new(2026, 6, 26, 19, 0, 0, TimeSpan.Zero);
    public static readonly Guid UserId = Guid.Parse("dddddddd-eeee-ffff-aaaa-bbbbbbbbbbbb");

    public static OfficialOutputExportPackage CreatePackage()
    {
        IReadOnlyList<OfficialOutputExportPayload> payloads = new[]
        {
            Payload(OfficialOutputArtifactFamily.ReceiptInvoice, "OFFICIAL_TEXT_PREVIEW", "salryn://official-text-preview/receipt", "receipt-line-1\nreceipt-line-2"),
            Payload(OfficialOutputArtifactFamily.ReadingReport, "OFFICIAL_X_READING_PREVIEW", "salryn://official-reading-preview/XReading/x", "x-reading-line"),
            Payload(OfficialOutputArtifactFamily.ReadingReport, "OFFICIAL_Z_READING_PREVIEW", "salryn://official-reading-preview/ZReading/z", "z-reading-line"),
            Payload(OfficialOutputArtifactFamily.AuditEvidenceReport, "OFFICIAL_AUDIT_TRAIL_PREVIEW", "salryn://official-audit-evidence-preview/AuditTrail/audit", "audit-line"),
            Payload(OfficialOutputArtifactFamily.AuditEvidenceReport, "OFFICIAL_ELECTRONIC_JOURNAL_PREVIEW", "salryn://official-audit-evidence-preview/ElectronicJournal/ej", "electronic-journal-line")
        };

        OfficialOutputArtifactManifestPackage manifest = Value(new OfficialOutputArtifactManifestBuilder().Build(
            payloads.Select(payload => new OfficialOutputArtifactManifestEntry(
                payload.ArtifactFamily,
                payload.ArtifactLabel,
                payload.ArtifactReference,
                payload.ContentHash,
                payload.HashAlgorithm,
                payload.LineCount,
                "Preview",
                Source(payload),
                new Dictionary<string, string> { ["entry"] = payload.ArtifactFamily.ToString() })).ToArray(),
            Timestamp,
            new Dictionary<string, string> { ["phase"] = "09P-09T" }));

        return Value(new OfficialOutputExportPackageBuilder().Build(manifest, payloads));
    }

    public static OfficialOutputDeliveryResult CreateDelivery(OfficialOutputExportPackage package)
    {
        return Value(new OfficialInMemoryOutputDeliveryAdapter().DeliverAsync(new OfficialOutputDeliveryRequest(
            package,
            OfficialOutputDeliveryTarget.InMemory,
            UserId,
            Timestamp,
            "POS-09P",
            new Dictionary<string, string> { ["phase"] = "09P-09T" })).GetAwaiter().GetResult());
    }

    public static OfficialOutputDeliveryLedgerPackage CreateDeliveryLedger(OfficialOutputDeliveryResult delivery)
    {
        return Value(new OfficialOutputDeliveryReceiptLedgerBuilder().Build(
            new[] { delivery },
            Timestamp,
            new Dictionary<string, string> { ["phase"] = "09P-09T" }));
    }

    public static OfficialExternalOutputDispatchPlan CreateFileDispatchPlan(
        OfficialOutputExportPackage package,
        OfficialOutputDeliveryLedgerPackage ledger)
    {
        OfficialExternalOutputAdapterContract contract = new(
            "contract-file-text-output",
            nameof(OfficialTextFileExternalOutputAdapter),
            new[] { new OfficialExternalOutputCapability(OfficialExternalOutputChannel.File, OfficialExternalOutputIntent.ContractOnly, "Controlled text-file output contract.") },
            OfficialExternalOutputAdapterContractValidator.ContractOnlySideEffectMarker);

        return Value(new OfficialExternalOutputDispatchPlanBuilder().Build(new OfficialExternalOutputDispatchRequest(
            package,
            ledger,
            OfficialExternalOutputChannel.File,
            OfficialExternalOutputIntent.ContractOnly,
            "TextFile",
            contract,
            Timestamp,
            UserId,
            "POS-09P",
            new Dictionary<string, string> { ["phase"] = "09P-09T" }),
            Timestamp,
            new Dictionary<string, string> { ["phase"] = "09P-09T" }));
    }

    public static OfficialExternalOutputExecutionResult ExecuteTextFile(string outputRoot)
    {
        OfficialOutputExportPackage package = CreatePackage();
        OfficialOutputDeliveryLedgerPackage ledger = CreateDeliveryLedger(CreateDelivery(package));
        OfficialExternalOutputDispatchPlan plan = CreateFileDispatchPlan(package, ledger);
        return Value(new OfficialTextFileExternalOutputAdapter().ExecuteAsync(new OfficialExternalOutputExecutionRequest(
            plan,
            package,
            ledger,
            outputRoot,
            Timestamp,
            UserId,
            "POS-09P",
            new Dictionary<string, string> { ["phase"] = "09P-09T" })).GetAwaiter().GetResult());
    }

    public static OfficialExternalOutputWorkflowResult RunWorkflow(string outputRoot)
    {
        return Value(new OfficialExternalOutputTextFileWorkflowRunner().RunAsync(new OfficialExternalOutputWorkflowRequest(
            CreatePackage(),
            outputRoot,
            Timestamp,
            UserId,
            "POS-09P",
            new Dictionary<string, string> { ["phase"] = "09P-09T" })).GetAwaiter().GetResult());
    }

    public static string CreateTempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "salryn-official-output-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    public static void CleanupTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    public static T Value<T>(Result<T> result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(result.Error);
        }

        return result.Value;
    }

    public static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static string Hash(string text)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private static OfficialOutputExportPayload Payload(OfficialOutputArtifactFamily family, string label, string reference, string text)
    {
        return new OfficialOutputExportPayload(family, label, reference, Hash(text), "SHA-256", text.Split('\n').Length, text);
    }

    private static OfficialOutputArtifactManifestSource Source(OfficialOutputExportPayload payload)
    {
        return new OfficialOutputArtifactManifestSource(
            payload.ArtifactFamily.ToString() + "Preview",
            payload.ArtifactReference,
            payload.ArtifactFamily == OfficialOutputArtifactFamily.ReceiptInvoice ? "OfficialReceipt" : null,
            payload.ArtifactFamily == OfficialOutputArtifactFamily.ReceiptInvoice ? "OR-2026-000001" : null,
            payload.ArtifactFamily == OfficialOutputArtifactFamily.ReceiptInvoice ? null : payload.ArtifactFamily.ToString(),
            payload.ArtifactFamily == OfficialOutputArtifactFamily.ReceiptInvoice ? null : "RPT-2026-000001",
            null,
            "POS-09P",
            "cashier-09p",
            new DateOnly(2026, 6, 26),
            null,
            null,
            payload.ArtifactFamily == OfficialOutputArtifactFamily.ReceiptInvoice ? false : null,
            payload.ArtifactFamily == OfficialOutputArtifactFamily.ReceiptInvoice ? "Active" : null,
            Timestamp,
            null);
    }
}
