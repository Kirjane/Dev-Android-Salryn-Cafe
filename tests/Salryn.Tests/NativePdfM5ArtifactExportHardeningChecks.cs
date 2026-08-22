using System.Text.Json;
using Salryn.Core.Common;
using Salryn.Core.Services.Reports;
using Salryn.Infrastructure.Reports.NativePdf;

namespace Salryn.Tests;

public static class NativePdfM5ArtifactExportHardeningChecks
{
    private static readonly DateTimeOffset FixedGeneratedAt = new(2026, 7, 11, 5, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FixedArtifactCreatedAt = new(2026, 7, 11, 5, 1, 0, TimeSpan.Zero);

    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "salryn-native-pdf-m5-" + Guid.NewGuid().ToString("N"));
        try
        {
            NativePdfArtifactRootPolicy policy = NativePdfArtifactRootPolicy.CreateForTesting(root);
            NativePdfArtifactStore store = new(policy, () => FixedArtifactCreatedAt);
            NativePdfReportExportService service = new(new NativePdfReportRenderer(), store);

            NativePdfArtifactRootPolicy.EnsureSafeRelativePath("20260711/sales-refund-void-summary/report.pdf");
            RequireThrows(
                () => NativePdfArtifactRootPolicy.EnsureSafeRelativePath("../escape.pdf"),
                "Path traversal must be rejected.");
            RequireTrue(policy.RejectCallerPath(Path.Combine(root, "caller.pdf")).IsFailure, "Caller output path must be rejected.");

            ProductionReportDatasetEnvelope dataset = Dataset(
                ProductionReportKinds.SalesRefundVoidSummary,
                "Sales / Refund / Void Summary",
                "sales-refund-void-summary",
                SafetyFlags(internalESales: false));

            Result<ProductionReportArtifactExportResult> exported = service.ExportAsync(
                new NativePdfReportExportRequest(
                    dataset,
                    ProductionReportOutputKinds.Pdf,
                    "m5-hardening-test",
                    CancellationToken.None),
                CancellationToken.None).GetAwaiter().GetResult();

            RequireTrue(exported.IsSuccess, "Controlled native PDF export must succeed for a valid server-owned dataset.");
            ProductionReportArtifactManifest manifest = exported.Value.Manifest;
            AssertManifest(dataset, manifest, root);

            string artifactPath = Path.Combine(root, manifest.RelativeArtifactPath.Replace('/', Path.DirectorySeparatorChar));
            string manifestPath = Path.Combine(root, manifest.RelativeManifestPath.Replace('/', Path.DirectorySeparatorChar));
            RequireTrue(File.Exists(artifactPath), "PDF artifact must be written under the controlled test root.");
            RequireTrue(File.Exists(manifestPath), "Sidecar manifest JSON must be written under the controlled test root.");

            byte[] writtenPdf = File.ReadAllBytes(artifactPath);
            RequireTrue(writtenPdf.Length > 0, "Written PDF must be non-empty.");
            RequireTrue(writtenPdf.Length >= 4 &&
                writtenPdf[0] == (byte)'%' &&
                writtenPdf[1] == (byte)'P' &&
                writtenPdf[2] == (byte)'D' &&
                writtenPdf[3] == (byte)'F', "Written artifact must start with the PDF header.");

            string writtenHash = NativePdfArtifactFactory.Sha256(writtenPdf);
            RequireTrue(writtenHash == manifest.PdfSha256, "Manifest hash must match actual written PDF bytes.");

            ProductionReportArtifactManifest sidecar = JsonSerializer.Deserialize<ProductionReportArtifactManifest>(
                File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            RequireTrue(sidecar is not null, "Sidecar manifest must deserialize.");
            RequireTrue(sidecar.PdfSha256 == manifest.PdfSha256, "Sidecar manifest hash must match returned manifest.");
            RequireTrue(sidecar.DatasetContentSha256 == dataset.ContentSha256, "Sidecar manifest must preserve dataset content hash.");
            RequireTrue(sidecar.DatasetSha256 == dataset.DatasetSha256, "Sidecar manifest must preserve dataset envelope hash.");

            Result<ProductionReportArtifactExportResult> unsupported = service.ExportAsync(
                new NativePdfReportExportRequest(
                    dataset,
                    "html",
                    "m5-hardening-test",
                    CancellationToken.None),
                CancellationToken.None).GetAwaiter().GetResult();
            RequireTrue(unsupported.IsFailure, "Unsupported output kind must fail closed.");
            RequireTrue(unsupported.ErrorDetail.Code == "Reports.NativePdfUnsupportedOutputKind", "Unsupported output kind must use stable native export error code.");

            ProductionReportDatasetEnvelope eSales = Dataset(
                ProductionReportKinds.InternalESalesDraft,
                "Internal eSales Draft Dataset",
                "internal-esales-row",
                SafetyFlags(internalESales: true));

            Result<ProductionReportArtifactExportResult> eSalesExport = service.ExportAsync(
                new NativePdfReportExportRequest(
                    eSales,
                    ProductionReportOutputKinds.Pdf,
                    "m5-internal-esales-test",
                    CancellationToken.None),
                CancellationToken.None).GetAwaiter().GetResult();
            RequireTrue(eSalesExport.IsSuccess, "Internal eSales draft must export as controlled PDF artifact.");
            RequireTrue(!eSalesExport.Value.Manifest.SafetyFlags.BirESalesSchemaConfirmed, "Internal eSales manifest must preserve unconfirmed BIR schema flag.");
            RequireTrue(!eSalesExport.Value.Manifest.SafetyFlags.ESalesTransmissionEnabled, "Internal eSales manifest must preserve transmission-disabled flag.");
            RequireTrue(!eSalesExport.Value.Manifest.SafetyFlags.BirAccreditationClaimed, "Internal eSales manifest must not claim BIR accreditation.");

            NativePdfReportRenderer renderer = new();
            ProductionReportRendererStatus status = renderer.GetStatus();
            RequireTrue(!status.CanPrint, "Native PDF export must not claim printer support.");
            RequireTrue(!status.RequiresExternalRuntime, "Native PDF export must not claim Crystal/SAP external runtime.");
            ProductionReportRendererCapability capability = renderer.GetCapability();
            RequireTrue(!capability.IsolatedWorkerSupported, "Native PDF export must not claim Crystal worker behavior.");

            string exportServiceSource = ReadRepoFile("src", "Salryn.Infrastructure", "Reports", "NativePdf", "NativePdfReportExportService.cs");
            RequireNotContains(exportServiceSource, "OutputPath", "Export service request contract must not accept caller output path.");
            RequireNotContains(exportServiceSource, "TemplatePath", "Export service must not accept arbitrary templates.");
            RequireNotContains(exportServiceSource, "SqlText", "Export service must not accept arbitrary SQL.");

            string endpointSource = ReadRepoFile("src", "Salryn.Api.Local", "LocalProductionReportEndpoints.cs");
            RequireContains(endpointSource, "/api/production-reports/export", "Local export endpoint must be mapped.");
            RequireNotContains(endpointSource, "DownloadByPath", "No arbitrary download-by-path endpoint may be added.");
            RequireNotContains(endpointSource, "OutputPath", "Local export endpoint body must not accept output paths.");
            RequireNotContains(endpointSource, "TemplatePath", "Local export endpoint body must not accept arbitrary templates.");
            RequireNotContains(endpointSource, "SqlText", "Local export endpoint body must not accept SQL.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void AssertManifest(
        ProductionReportDatasetEnvelope dataset,
        ProductionReportArtifactManifest manifest,
        string root)
    {
        RequireTrue(!string.IsNullOrWhiteSpace(manifest.ArtifactId), "Artifact ID must be populated.");
        RequireTrue(manifest.ReportKind == dataset.ReportKind, "Manifest report kind must match dataset.");
        RequireTrue(manifest.FileName.EndsWith(".pdf", StringComparison.Ordinal), "Manifest file name must end with .pdf.");
        RequireTrue(!manifest.FileName.Contains(Path.DirectorySeparatorChar), "Manifest file name must not contain path separator.");
        RequireTrue(manifest.ContentType == NativePdfArtifactFactory.PdfContentType, "Manifest content type must be application/pdf.");
        RequireTrue(manifest.ByteLength > 0, "Manifest byte length must be greater than zero.");
        RequireTrue(!string.IsNullOrWhiteSpace(manifest.PdfSha256), "Manifest PDF SHA-256 must be populated.");
        RequireTrue(manifest.DatasetContentSha256 == dataset.ContentSha256, "Manifest must preserve dataset content SHA-256.");
        RequireTrue(manifest.DatasetSha256 == dataset.DatasetSha256, "Manifest must preserve dataset envelope SHA-256.");
        RequireTrue(manifest.RendererName == NativePdfRendererIdentity.RendererName, "Manifest renderer name must match native renderer.");
        RequireTrue(manifest.RendererVersion == NativePdfRendererIdentity.RendererVersion, "Manifest renderer version must match native renderer.");
        RequireTrue(manifest.ControlledRootPolicyLabel == "Salryn.TestControlledRoot.Reporting.NativePdf", "Manifest must record controlled root policy label.");
        RequireTrue(!Path.IsPathRooted(manifest.RelativeArtifactPath), "Artifact path must be relative.");
        RequireTrue(!Path.IsPathRooted(manifest.RelativeManifestPath), "Manifest path must be relative.");
        RequireTrue(!manifest.RelativeArtifactPath.Contains("..", StringComparison.Ordinal), "Artifact path must not contain traversal.");
        RequireTrue(!manifest.RelativeManifestPath.Contains("..", StringComparison.Ordinal), "Manifest path must not contain traversal.");
        RequireTrue(!manifest.CallerPathAccepted, "Manifest must record that caller paths are not accepted.");
        RequireTrue(!string.IsNullOrWhiteSpace(manifest.RetentionCleanupNote), "Manifest must include retention/cleanup note.");

        string artifactPath = Path.GetFullPath(Path.Combine(root, manifest.RelativeArtifactPath.Replace('/', Path.DirectorySeparatorChar)));
        string manifestPath = Path.GetFullPath(Path.Combine(root, manifest.RelativeManifestPath.Replace('/', Path.DirectorySeparatorChar)));
        string fullRoot = Path.GetFullPath(root);
        RequireTrue(artifactPath.StartsWith(fullRoot, StringComparison.Ordinal), "Artifact must stay under controlled root.");
        RequireTrue(manifestPath.StartsWith(fullRoot, StringComparison.Ordinal), "Manifest must stay under controlled root.");
    }

    private static ProductionReportDatasetEnvelope Dataset(
        string kind,
        string title,
        string rowType,
        ProductionReportSafetyFlags safetyFlags)
    {
        return new ProductionReportDatasetEnvelope(
            "SALRYN_PRECRYSTAL_REPORT_DATASET_V1",
            1,
            kind,
            title,
            FixedGeneratedAt,
            new ProductionReportScope(
                FixedGeneratedAt.AddHours(-8),
                FixedGeneratedAt,
                null,
                "LOCAL_SINGLE_STORE",
                "OFFICIAL_INVOICE",
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "HOST-TEST"),
            new[]
            {
                new ProductionReportControlTotal("gross-posted-sales", "Gross posted sales", 125000, 2),
                new ProductionReportControlTotal("refunded-value", "Refunded value", 15000, 1),
                new ProductionReportControlTotal("net-sales", "Net sales", 110000, 2),
                new ProductionReportControlTotal("vat-amount", "VAT amount", 12000, 2)
            },
            new[]
            {
                new ProductionReportRow(
                    rowType,
                    1,
                    new SortedDictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["grossPostedSalesCents"] = "125000",
                        ["netSalesCents"] = "110000",
                        ["refundedValueCents"] = "15000",
                        ["voidedDocumentCount"] = "1",
                        ["presentationBoundary"] = kind == ProductionReportKinds.InternalESalesDraft
                            ? "Internal/unconfirmed/presentation-only; eSales transmission disabled."
                            : "Controlled export hardening dataset."
                    })
            },
            new[]
            {
                new ProductionReportSourceHash(
                    "m5-controlled-export-test",
                    new string('a', 64),
                    "Controlled M5 artifact export test source")
            },
            safetyFlags,
            new string('b', 64),
            new string('c', 64),
            kind == ProductionReportKinds.InternalESalesDraft
                ? "Internal eSales draft dataset only. Authoritative BIR schema unconfirmed; transmission disabled."
                : "Controlled native PDF artifact export hardening dataset.");
    }

    private static ProductionReportSafetyFlags SafetyFlags(bool internalESales) => new(
        DatasetAvailable: true,
        UsesCanonicalM5Projection: true,
        ProductionActivationEnabled: false,
        BirESalesSchemaConfirmed: false,
        ESalesTransmissionEnabled: false,
        BirAccreditationClaimed: false,
        CrystalRuntimeAvailable: false,
        RendererCanProducePdf: true,
        BoundaryNote: internalESales
            ? "Internal eSales draft only. BIR schema unconfirmed and transmission disabled."
            : "Controlled native PDF export artifact.");

    private static string ReadRepoFile(params string[] pathParts)
    {
        string root = FindRoot();
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(pathParts).ToArray()));
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

    private static void RequireThrows(Action action, string message)
    {
        try
        {
            action();
        }
        catch
        {
            return;
        }

        throw new InvalidOperationException(message);
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
