using Salryn.Core.Common;
using Salryn.Core.Services.Reports;
using Salryn.Infrastructure.Reports;

namespace Salryn.Tests;

public static class CrystalM1RuntimeDecisionMinimalProofChecks
{
    public static void Run()
    {
        string root = FindRoot();

        RequireTrue(File.Exists(Path.Combine(root, "00-HANDOFF", "CRYSTAL_M1_RUNTIME_DECISION_STUDY.md")), "Crystal M1 study document required.");
        RequireTrue(File.Exists(Path.Combine(root, "00-HANDOFF", "CRYSTAL_M1_MINIMAL_RENDERER_PROOF_REPORT.md")), "Crystal M1 proof report required.");

        string coreSmoke = File.ReadAllText(Path.Combine(root, "tools", "Salryn.Tools.CoreSmoke", "Program.cs"));
        RequireContains(coreSmoke, "reports-crystal-m1-runtime-decision-minimal-proof", "Focused Crystal M1 gate required.");
        RequireContains(coreSmoke, "CrystalM1RuntimeDecisionMinimalProofChecks.Run()", "Full CoreSmoke must run Crystal M1 gate.");

        RequireTrue(ProductionReportOutputKinds.TryNormalize(null, out string defaultOutput, out Error? defaultError), "Null output kind defaults safely.");
        RequireTrue(defaultOutput == ProductionReportOutputKinds.Pdf && defaultError is null, "Default output kind is PDF.");
        RequireTrue(ProductionReportOutputKinds.TryNormalize("PDF", out string pdfOutput, out Error? pdfError), "PDF output kind is allow-listed.");
        RequireTrue(pdfOutput == ProductionReportOutputKinds.Pdf && pdfError is null, "PDF output normalizes to lowercase pdf.");
        RequireTrue(!ProductionReportOutputKinds.TryNormalize("html", out _, out Error? htmlError), "HTML output is not allow-listed.");
        RequireTrue(htmlError?.Code == "Reports.UnsupportedOutputKind", "Unsupported output kind has stable error code.");

        string endpointSource = File.ReadAllText(Path.Combine(root, "src", "Salryn.Api.Local", "LocalProductionReportEndpoints.cs"));
        RequireContains(endpointSource, "ProductionReportOutputKinds.TryNormalize", "Render endpoint must validate output kind before renderer call.");
        RequireNotContains(endpointSource, "body.Dataset", "Render endpoint must not accept caller-supplied dataset envelope.");
        RequireNotContains(endpointSource, "TemplatePath", "Endpoint must not accept template paths.");
        RequireNotContains(endpointSource, "SqlText", "Endpoint must not accept SQL text.");
        RequireNotContains(endpointSource, "OutputPath", "Endpoint must not accept caller output paths.");

        string datasetContracts = File.ReadAllText(Path.Combine(root, "src", "Salryn.Core", "Services", "Reports", "ReportDatasetContracts.cs"));
        RequireContains(datasetContracts, "CrystalWorkerProtocol", "Worker protocol marker required.");
        RequireContains(datasetContracts, "CrystalWorkerDatasetPackage", "Worker dataset package required.");
        RequireContains(datasetContracts, "CrystalWorkerArtifactPolicy", "Worker artifact policy required.");
        RequireContains(datasetContracts, "CrystalWorkerLaunchPolicy", "Worker launch policy required.");
        RequireContains(datasetContracts, "CrystalWorkerRuntimeReadiness", "Worker runtime readiness required.");
        RequireContains(datasetContracts, "AllowCallerSuppliedOutputPath", "Artifact policy must explicitly model caller path denial.");
        RequireContains(datasetContracts, "AllowArbitrarySql", "Artifact policy must explicitly model arbitrary SQL denial.");

        string rendererContracts = File.ReadAllText(Path.Combine(root, "src", "Salryn.Core", "Services", "Reports", "ReportRendererContracts.cs"));
        RequireContains(rendererContracts, "ProductionReportRendererCapability", "Renderer capability extension required.");
        RequireContains(rendererContracts, "DirectInProcessSupported", "Capability must record direct in-process support decision.");
        RequireContains(rendererContracts, "IsolatedWorkerSupported", "Capability must record isolated worker readiness.");

        UnavailableCrystalReportRenderer renderer = new();
        ProductionReportRendererStatus status = renderer.GetStatus();
        RequireTrue(!status.Available && !status.CanRenderPdf && !status.CanPrint, "Crystal renderer remains unavailable and makes no PDF/print claim.");
        ProductionReportRendererCapability capability = renderer.GetCapability();
        RequireTrue(!capability.DirectInProcessSupported, "Direct in-process Crystal integration must remain unsupported for net10.0 Salryn.");
        RequireTrue(!capability.RuntimeInstalled && !capability.LicensedRuntimeConfirmed, "No runtime or license confirmation may be claimed.");
        RequireTrue(capability.SupportedOutputKinds.SequenceEqual(new[] { ProductionReportOutputKinds.Pdf }), "Capability exposes PDF-only output allow-list.");

        ProductionReportDatasetEnvelope dataset = MinimalDatasetEnvelope();
        Result<ProductionReportRenderResult> unsupported = renderer.RenderAsync(new ProductionReportRenderRequest(
            dataset,
            "html",
            30000,
            CancellationToken.None)).GetAwaiter().GetResult();
        RequireTrue(unsupported.IsFailure && unsupported.ErrorDetail.Code == "Reports.UnsupportedOutputKind", "Renderer rejects unsupported output kind before unavailable runtime path.");

        Result<ProductionReportRenderResult> pdf = renderer.RenderAsync(new ProductionReportRenderRequest(
            dataset,
            ProductionReportOutputKinds.Pdf,
            30000,
            CancellationToken.None)).GetAwaiter().GetResult();
        RequireTrue(pdf.IsFailure && pdf.ErrorDetail.Code == "Crystal.WorkerPending", "PDF request still fails closed until worker/runtime is wired.");

        string unavailableRenderer = File.ReadAllText(Path.Combine(root, "src", "Salryn.Infrastructure", "Reports", "UnavailableCrystalReportRenderer.cs"));
        RequireNotContains(unavailableRenderer, "CrystalDecisions", "No SAP Crystal assembly reference allowed.");
        RequireNotContains(unavailableRenderer, "ReportDocument", "No direct Crystal runtime object allowed.");
        RequireNotContains(unavailableRenderer, ".Load(", "Unavailable renderer must not load templates.");
        RequireNotContains(unavailableRenderer, "ExportTo", "Unavailable renderer must not export artifacts.");
    }

    private static ProductionReportDatasetEnvelope MinimalDatasetEnvelope()
    {
        DateTimeOffset now = new(2026, 7, 11, 0, 0, 0, TimeSpan.Zero);
        ProductionReportSafetyFlags flags = new(
            DatasetAvailable: true,
            UsesCanonicalM5Projection: true,
            ProductionActivationEnabled: false,
            BirESalesSchemaConfirmed: false,
            ESalesTransmissionEnabled: false,
            BirAccreditationClaimed: false,
            CrystalRuntimeAvailable: false,
            RendererCanProducePdf: false,
            BoundaryNote: "test dataset");

        return new ProductionReportDatasetEnvelope(
            "SALRYN_PRECRYSTAL_REPORT_DATASET_V1",
            1,
            ProductionReportKinds.TraditionalXReading,
            "Traditional X Reading",
            now,
            new ProductionReportScope(
                now.AddDays(-1),
                now,
                null,
                "LOCAL_SINGLE_STORE",
                "OFFICIAL_INVOICE",
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "HOST-TEST"),
            Array.Empty<ProductionReportControlTotal>(),
            Array.Empty<ProductionReportRow>(),
            new[] { new ProductionReportSourceHash("m5-canonical-projection", new string('b', 64), "test source") },
            flags,
            new string('c', 64),
            new string('d', 64),
            "minimal dataset");
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
