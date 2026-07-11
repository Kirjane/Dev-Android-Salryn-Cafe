using Salryn.Core.Common;
using Salryn.Core.Services.Reports;
using Salryn.Infrastructure.Reports.NativePdf;

namespace Salryn.Tests;

public static class NativePdfM3RendererCoreChecks
{
    public static void Run()
    {
        string root = FindRoot();

        RequireTrue(File.Exists(Path.Combine(root, "00-HANDOFF", "REPORTING_CLOSURE_M3_NATIVE_PDF_RENDERER_CORE_REPORT.md")), "M3 report required.");

        string coreSmoke = File.ReadAllText(Path.Combine(root, "tools", "Salryn.Tools.CoreSmoke", "Program.cs"));
        RequireContains(coreSmoke, "reports-native-pdf-m3-renderer-core", "Focused native PDF M3 gate required.");
        RequireContains(coreSmoke, "NativePdfM3RendererCoreChecks.Run()", "Full CoreSmoke must run native PDF M3 gate.");

        string rendererSource = File.ReadAllText(Path.Combine(root, "src", "Salryn.Infrastructure", "Reports", "NativePdf", "NativePdfReportRenderer.cs"));
        string builderSource = File.ReadAllText(Path.Combine(root, "src", "Salryn.Infrastructure", "Reports", "NativePdf", "NativePdfDocumentBuilder.cs"));
        string artifactSource = File.ReadAllText(Path.Combine(root, "src", "Salryn.Infrastructure", "Reports", "NativePdf", "NativePdfArtifactFactory.cs"));
        string combinedSource = rendererSource + "\n" + builderSource + "\n" + artifactSource;

        RequireNotContains(combinedSource, "File.WriteAll", "Native PDF M3 must not write files to disk.");
        RequireNotContains(combinedSource, "FileStream", "Native PDF M3 must not require filesystem output streams.");
        RequireNotContains(combinedSource, "OutputPath", "Native PDF M3 must not accept caller output paths.");
        RequireNotContains(combinedSource, "Printer", "Native PDF M3 must not add printer logic.");
        RequireNotContains(combinedSource, "CrystalDecisions", "Native PDF M3 must not reference Crystal assemblies.");
        RequireNotContains(combinedSource, "ReportDocument", "Native PDF M3 must not use Crystal report documents.");
        RequireNotContains(combinedSource, ".rpt", "Native PDF M3 must not use Crystal template files.");
        RequireNotContains(combinedSource, "SqlText", "Native PDF M3 must not accept arbitrary SQL text.");
        RequireNotContains(combinedSource, "TemplatePath", "Native PDF M3 must not accept template paths.");

        NativePdfReportRenderer renderer = new();
        ProductionReportRendererStatus status = renderer.GetStatus();
        RequireTrue(status.Available, "Native PDF renderer status must be available.");
        RequireTrue(status.CanRenderPdf, "Native PDF renderer must report PDF capability.");
        RequireTrue(!status.CanPrint, "Native PDF renderer must not report printer support.");
        RequireTrue(!status.RequiresExternalRuntime, "Native PDF renderer must not require an external runtime.");
        RequireTrue(status.StatusCode == NativePdfRendererIdentity.StatusAvailable, "Native PDF status code must be stable.");

        ProductionReportRendererCapability capability = renderer.GetCapability();
        RequireTrue(capability.RendererName == NativePdfRendererIdentity.RendererName, "Capability renderer name must match identity.");
        RequireTrue(capability.DirectInProcessSupported, "Native PDF can run in-process as Salryn primary renderer.");
        RequireTrue(!capability.IsolatedWorkerSupported, "Native PDF M3 is not an isolated worker path.");
        RequireTrue(!capability.LicensedRuntimeConfirmed, "M3 must not claim legal/release approval.");
        RequireTrue(capability.SupportedOutputKinds.SequenceEqual(new[] { ProductionReportOutputKinds.Pdf }), "Native PDF supports only PDF output.");

        ProductionReportDatasetEnvelope dataset = SyntheticDataset();
        Result<ProductionReportRenderResult> result = renderer.RenderAsync(new ProductionReportRenderRequest(
            dataset,
            ProductionReportOutputKinds.Pdf,
            30000,
            CancellationToken.None)).GetAwaiter().GetResult();

        RequireTrue(result.IsSuccess, "Valid synthetic dataset must render successfully in memory.");
        RequireTrue(result.Value.Success, "Render result success flag must be true.");
        RequireTrue(result.Value.Artifact is not null, "Artifact metadata must be populated.");
        ProductionReportArtifactMetadata artifact = result.Value.Artifact!;
        RequireTrue(artifact.ContentType == NativePdfArtifactFactory.PdfContentType, "Artifact content type must be application/pdf.");
        RequireTrue(artifact.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase), "Artifact file name must end with .pdf.");
        RequireTrue(artifact.ByteLength > 0, "Artifact byte length must be greater than zero.");
        RequireTrue(!string.IsNullOrWhiteSpace(artifact.Sha256) && artifact.Sha256.Length == 64, "Artifact SHA-256 must be populated.");
        RequireTrue(artifact.SourceDatasetSha256 == dataset.DatasetSha256, "Artifact source dataset hash must match dataset envelope hash.");
        RequireTrue(artifact.RendererName == NativePdfRendererIdentity.RendererName, "Artifact renderer name must match native renderer.");

        byte[] bytes = NativePdfDocumentBuilder.BuildPdfBytes(dataset, CancellationToken.None);
        RequireTrue(bytes.Length > 0, "Builder must produce in-memory PDF bytes.");
        RequireTrue(bytes[0] == (byte)'%' && bytes[1] == (byte)'P' && bytes[2] == (byte)'D' && bytes[3] == (byte)'F', "Builder bytes must look like a PDF document.");
        string hashA = NativePdfArtifactFactory.Sha256(bytes);
        string hashB = NativePdfArtifactFactory.Sha256(bytes);
        RequireTrue(hashA == hashB && hashA.Length == 64, "SHA-256 helper must be stable for the returned bytes.");

        Result<ProductionReportRenderResult> unsupported = renderer.RenderAsync(new ProductionReportRenderRequest(
            dataset,
            "html",
            30000,
            CancellationToken.None)).GetAwaiter().GetResult();
        RequireTrue(unsupported.IsFailure && unsupported.ErrorDetail.Code == NativePdfRendererIdentity.StatusUnsupportedOutputKind, "Unsupported output must fail closed.");

        ProductionReportDatasetEnvelope invalidDataset = dataset with { DatasetSha256 = string.Empty };
        Result<ProductionReportRenderResult> invalid = renderer.RenderAsync(new ProductionReportRenderRequest(
            invalidDataset,
            ProductionReportOutputKinds.Pdf,
            30000,
            CancellationToken.None)).GetAwaiter().GetResult();
        RequireTrue(invalid.IsFailure && invalid.ErrorDetail.Code == NativePdfRendererIdentity.StatusInvalidDataset, "Invalid dataset must fail closed.");
    }

    private static ProductionReportDatasetEnvelope SyntheticDataset()
    {
        DateTimeOffset generatedAt = new(2026, 7, 11, 6, 0, 0, TimeSpan.Zero);
        ProductionReportScope scope = new(
            generatedAt.AddHours(-8),
            generatedAt,
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "LOCAL_SINGLE_STORE",
            "OFFICIAL_INVOICE",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "HOST-M3");

        ProductionReportSafetyFlags flags = new(
            DatasetAvailable: true,
            UsesCanonicalM5Projection: true,
            ProductionActivationEnabled: false,
            BirESalesSchemaConfirmed: false,
            ESalesTransmissionEnabled: false,
            BirAccreditationClaimed: false,
            CrystalRuntimeAvailable: false,
            RendererCanProducePdf: false,
            BoundaryNote: "M3 synthetic dataset; renderer-core proof only.");

        IReadOnlyList<ProductionReportControlTotal> totals = new[]
        {
            new ProductionReportControlTotal("gross-posted-sales", "Gross posted sales", 1234500, 2),
            new ProductionReportControlTotal("net-sales", "Net sales", 1200000, 2),
            new ProductionReportControlTotal("vat-amount", "VAT amount", 128571, 2)
        };

        IReadOnlyList<ProductionReportRow> rows = new[]
        {
            new ProductionReportRow("official-document", 1, new Dictionary<string, string>
            {
                ["officialDocumentNumber"] = "SI-00000001",
                ["postedAtUtc"] = generatedAt.AddHours(-7).ToString("O"),
                ["netTotalCents"] = "500000",
                ["snapshotPayloadHash"] = new string('a', 64)
            }),
            new ProductionReportRow("official-document", 2, new Dictionary<string, string>
            {
                ["officialDocumentNumber"] = "SI-00000002",
                ["postedAtUtc"] = generatedAt.AddHours(-1).ToString("O"),
                ["netTotalCents"] = "700000",
                ["snapshotPayloadHash"] = new string('b', 64)
            })
        };

        IReadOnlyList<ProductionReportSourceHash> sourceHashes = new[]
        {
            new ProductionReportSourceHash("m5-canonical-projection", new string('c', 64), "M3 synthetic source hash")
        };

        return new ProductionReportDatasetEnvelope(
            "SALRYN_PRECRYSTAL_REPORT_DATASET_V1",
            1,
            ProductionReportKinds.TraditionalXReading,
            "Traditional X Reading",
            generatedAt,
            scope,
            totals,
            rows,
            sourceHashes,
            flags,
            new string('d', 64),
            new string('e', 64),
            "Synthetic M3 dataset for native PDF renderer core proof.");
    }

    private static string FindRoot()
    {
        DirectoryInfo? current = new(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate Salryn.sln.");
    }

    private static void RequireTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireContains(string text, string expected, string message)
    {
        if (!text.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message + " Missing: " + expected);
        }
    }

    private static void RequireNotContains(string text, string forbidden, string message)
    {
        if (text.Contains(forbidden, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message + " Forbidden: " + forbidden);
        }
    }
}
