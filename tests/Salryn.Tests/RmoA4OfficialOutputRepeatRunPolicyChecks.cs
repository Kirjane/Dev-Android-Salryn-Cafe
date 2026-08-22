using System.Security.Cryptography;
using System.Text;
using Salryn.Core.Common;
using Salryn.Core.Services;
using Salryn.Infrastructure.OfficialOutput;

namespace Salryn.Tests;

public static class RmoA4OfficialOutputRepeatRunPolicyChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();
        string coreSmoke = Read(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string policySource = Read(repoRoot, "src", "Salryn.Infrastructure", "OfficialOutput", "OfficialTextFileOutputPathPolicy.cs");
        string adapterSource = Read(repoRoot, "src", "Salryn.Infrastructure", "OfficialOutput", "OfficialTextFileExternalOutputAdapter.cs");
        string smokeChecks = Read(repoRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "OfficialTextFileExternalOutputSmokeChecks.cs");
        string navigation = Read(repoRoot, "src", "Salryn.App.Web", "src", "navigation.ts");
        string readOnlyEndpoints = Read(repoRoot, "src", "Salryn.Api.Local", "ReadOnlyEndpointExtensions.cs");

        RequireContains(coreSmoke, "RmoA4OfficialOutputRepeatRunPolicyChecks.Run();", "CoreSmoke must run RMO-A4 repeat-run policy checks.");
        RequireContains(policySource, "RejectIfOutputPackageDirectoryExists", "Output-root policy must reject existing official output package directories.");
        RequireContains(policySource, "OfficialTextFileOutput.OutputPackageAlreadyExists", "Repeat-run policy must use OutputPackageAlreadyExists error code.");
        RequireContains(policySource, "Error.Conflict", "Repeat-run policy must use conflict semantics.");
        RequireContains(policySource, "repeat-run workflow", "Repeat-run policy must describe future authorized repeat-run workflow.");
        RequireContains(adapterSource, "RejectIfOutputPackageDirectoryExists", "Adapter must refuse duplicate official output package runs before writing.");
        RequireContains(smokeChecks, "OfficialTextFileOutput.OutputPackageAlreadyExists", "Official-output smoke must prove duplicate package runs are refused.");
        RequireNotContains(policySource, "reprint", "Repeat-run policy must not introduce reprint wording.");
        RequireNotContains(adapterSource, "reprint", "Adapter must not introduce reprint wording.");
        RequireNotContains(navigation, "Official Output Text File", "Unsupported official-output work must not appear in store navigation.");
        RequireNotContains(navigation, "enabled: false", "Store navigation must not render disabled developer-only surfaces.");
        RequireNotContains(readOnlyEndpoints, "official-output", "Read-only API must not expose official-output endpoints.");

        string root = CreateTempDirectory();
        string alternateRoot = CreateTempDirectory();

        try
        {
            OfficialOutputExportPackage package = J21Rmo242023OfficialOutputPipelineFixture.CreatePackage();
            OfficialOutputDeliveryLedgerPackage deliveryLedger =
                J21Rmo242023OfficialOutputPipelineFixture.CreateDeliveryLedger(
                    J21Rmo242023OfficialOutputPipelineFixture.CreateDelivery(package));
            OfficialExternalOutputDispatchPlan plan =
                J21Rmo242023OfficialOutputPipelineFixture.CreateFileDispatchPlan(package, deliveryLedger);
            OfficialTextFileExternalOutputAdapter adapter = new();
            OfficialExternalOutputExecutionRequest request = new(
                plan,
                package,
                deliveryLedger,
                root,
                J21Rmo242023OfficialOutputPipelineFixture.Timestamp,
                J21Rmo242023OfficialOutputPipelineFixture.UserId,
                "POS-RMO-A4",
                null);

            Result<OfficialExternalOutputExecutionResult> firstRun = adapter.ExecuteAsync(request).GetAwaiter().GetResult();
            RequireSuccess(firstRun, "First official output package run must succeed.");

            string manifestRelativePath = firstRun.Value.WrittenArtifacts
                .First(static artifact => artifact.Role == OfficialExternalOutputWrittenArtifactRole.Manifest)
                .RelativePath;
            string manifestFullPath = Path.GetFullPath(Path.Combine(root, manifestRelativePath));
            string manifestBeforeDuplicate = File.ReadAllText(manifestFullPath);
            DateTime manifestWriteTimeUtc = File.GetLastWriteTimeUtc(manifestFullPath);

            Result<OfficialExternalOutputExecutionResult> duplicateRun = adapter.ExecuteAsync(request).GetAwaiter().GetResult();
            RequireFailure(
                duplicateRun,
                "OfficialTextFileOutput.OutputPackageAlreadyExists",
                "Duplicate official output package run must be refused.");
            Require(
                duplicateRun.ErrorDetail.Type == ErrorType.Conflict,
                "Duplicate official output package run must return conflict semantics.");
            Require(
                string.Equals(File.ReadAllText(manifestFullPath), manifestBeforeDuplicate, StringComparison.Ordinal),
                "Existing official output package files must not be overwritten.");
            Require(
                File.GetLastWriteTimeUtc(manifestFullPath) == manifestWriteTimeUtc,
                "Existing official output package files must not be modified.");

            OfficialOutputExportPackage alternatePackage = CreateAlternatePackage();
            Require(
                !string.Equals(alternatePackage.PackageHash, package.PackageHash, StringComparison.Ordinal),
                "Alternate package must use a different package hash.");
            OfficialOutputDeliveryLedgerPackage alternateLedger =
                J21Rmo242023OfficialOutputPipelineFixture.CreateDeliveryLedger(
                    J21Rmo242023OfficialOutputPipelineFixture.CreateDelivery(alternatePackage));
            OfficialExternalOutputDispatchPlan alternatePlan =
                J21Rmo242023OfficialOutputPipelineFixture.CreateFileDispatchPlan(alternatePackage, alternateLedger);
            Result<OfficialExternalOutputExecutionResult> sameRootDifferentPackage = adapter.ExecuteAsync(new OfficialExternalOutputExecutionRequest(
                alternatePlan,
                alternatePackage,
                alternateLedger,
                root,
                J21Rmo242023OfficialOutputPipelineFixture.Timestamp,
                J21Rmo242023OfficialOutputPipelineFixture.UserId,
                "POS-RMO-A4",
                null)).GetAwaiter().GetResult();
            RequireSuccess(sameRootDifferentPackage, "Same output root with a different package hash must succeed.");

            Result<OfficialExternalOutputExecutionResult> differentRootRun = adapter.ExecuteAsync(new OfficialExternalOutputExecutionRequest(
                plan,
                package,
                deliveryLedger,
                alternateRoot,
                J21Rmo242023OfficialOutputPipelineFixture.Timestamp,
                J21Rmo242023OfficialOutputPipelineFixture.UserId,
                "POS-RMO-A4",
                null)).GetAwaiter().GetResult();
            RequireSuccess(differentRootRun, "Different output root with the same package hash must succeed.");

            foreach (OfficialExternalOutputWrittenArtifact artifact in firstRun.Value.WrittenArtifacts)
            {
                RequireContains(artifact.RelativePath, "official-output/", "Artifact layout must remain official-output/{packageHash}/.");
            }
        }
        finally
        {
            TryDeleteDirectory(root);
            TryDeleteDirectory(alternateRoot);
        }
    }

    private static OfficialOutputExportPackage CreateAlternatePackage()
    {
        OfficialOutputExportPackage baseline = J21Rmo242023OfficialOutputPipelineFixture.CreatePackage();
        List<OfficialOutputExportPayload> payloads = new();

        foreach (OfficialOutputExportPackageItem item in baseline.Items)
        {
            if (payloads.Count == 0)
            {
                payloads.Add(BuildPayload(
                    item.Payload.ArtifactFamily,
                    item.Payload.ArtifactLabel,
                    item.Payload.ArtifactReference,
                    item.Payload.Text + "\nalternate-package-marker"));
            }
            else
            {
                payloads.Add(item.Payload);
            }
        }

        OfficialOutputArtifactManifestPackage manifest = J21Rmo242023OfficialOutputPipelineFixture.Value(
            new OfficialOutputArtifactManifestBuilder().Build(
                payloads.Select(payload => new OfficialOutputArtifactManifestEntry(
                    payload.ArtifactFamily,
                    payload.ArtifactLabel,
                    payload.ArtifactReference,
                    payload.ContentHash,
                    payload.HashAlgorithm,
                    payload.LineCount,
                    "Preview",
                    BuildSource(payload),
                    new Dictionary<string, string> { ["entry"] = payload.ArtifactFamily.ToString() })).ToArray(),
                J21Rmo242023OfficialOutputPipelineFixture.Timestamp,
                new Dictionary<string, string> { ["phase"] = "RMO-A4" }));

        return J21Rmo242023OfficialOutputPipelineFixture.Value(
            new OfficialOutputExportPackageBuilder().Build(manifest, payloads));
    }

    private static OfficialOutputExportPayload BuildPayload(
        OfficialOutputArtifactFamily family,
        string label,
        string reference,
        string text)
    {
        return new OfficialOutputExportPayload(
            family,
            label,
            reference,
            Hash(text),
            "SHA-256",
            text.Split('\n').Length,
            text);
    }

    private static OfficialOutputArtifactManifestSource BuildSource(OfficialOutputExportPayload payload)
    {
        return new OfficialOutputArtifactManifestSource(
            payload.ArtifactFamily.ToString() + "Preview",
            payload.ArtifactReference,
            payload.ArtifactFamily == OfficialOutputArtifactFamily.ReceiptInvoice ? "OfficialReceipt" : null,
            payload.ArtifactFamily == OfficialOutputArtifactFamily.ReceiptInvoice ? "OR-2026-000001" : null,
            payload.ArtifactFamily == OfficialOutputArtifactFamily.ReceiptInvoice ? null : payload.ArtifactFamily.ToString(),
            payload.ArtifactFamily == OfficialOutputArtifactFamily.ReceiptInvoice ? null : "RPT-2026-000001",
            null,
            "POS-RMO-A4",
            "cashier-rmo-a4",
            new DateOnly(2026, 6, 26),
            null,
            null,
            payload.ArtifactFamily == OfficialOutputArtifactFamily.ReceiptInvoice ? false : null,
            payload.ArtifactFamily == OfficialOutputArtifactFamily.ReceiptInvoice ? "Active" : null,
            J21Rmo242023OfficialOutputPipelineFixture.Timestamp,
            null);
    }

    private static string Hash(string text)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "salryn-rmo-a4-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        current = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate Salryn repository root.");
    }

    private static string Read(DirectoryInfo root, params string[] parts)
    {
        string path = root.FullName;

        foreach (string part in parts)
        {
            path = Path.Combine(path, part);
        }

        return File.ReadAllText(path);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireNotContains(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
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

    private static void RequireSuccess<T>(Result<T> result, string message)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(message + $" Error: {result.ErrorDetail.Code}");
        }
    }
}
