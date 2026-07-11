using System.Text;
using Salryn.Core.Common;
using Salryn.Core.Services.Reporting;
using Salryn.Core.Services.Reports;
using Salryn.Infrastructure.Reports;
using Salryn.Infrastructure.Reports.NativePdf;

namespace Salryn.Tests;

public static class NativePdfM4CoreReportFamilyProofChecks
{
    private static readonly DateTimeOffset RangeStart = new(2026, 7, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RangeEnd = new(2026, 7, 11, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset GeneratedAt = new(2026, 7, 11, 8, 30, 0, TimeSpan.Zero);
    private static readonly Guid ActorUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static void Run()
    {
        NativePdfReportRenderer renderer = new();
        ProductionReportRendererStatus status = renderer.GetStatus();
        RequireTrue(status.Available, "Native PDF renderer must report available for M4 proof.");
        RequireTrue(status.CanRenderPdf, "Native PDF renderer must report PDF capability.");
        RequireTrue(!status.CanPrint, "Native PDF renderer must not claim printer support.");
        RequireTrue(!status.RequiresExternalRuntime, "Native PDF renderer must not require external runtime.");

        PreCrystalReportDatasetService datasetService = new(
            new ControlledOfficialReportingProjectionService(),
            () => GeneratedAt);

        ProofFamily(
            datasetService,
            renderer,
            ProductionReportKinds.TraditionalXReading,
            dataset =>
            {
                RequireTrue(dataset.ReportKind == ProductionReportKinds.TraditionalXReading, "X reading dataset kind must be stable.");
                RequireContains(dataset.Summary, "Read-only", "X reading summary must mark read-only preview boundary.");
                RequireContains(dataset.Summary, "no sequence mutation", "X reading must not claim sequence mutation.");
                RequireTrue(dataset.Rows.Any(row => row.RowType == "official-document"), "X reading must include official document rows.");
            });

        ProofFamily(
            datasetService,
            renderer,
            ProductionReportKinds.TraditionalZReading,
            dataset =>
            {
                RequireTrue(dataset.ReportKind == ProductionReportKinds.TraditionalZReading, "Z reading dataset kind must be stable.");
                RequireContains(dataset.Summary, "preview", "Z reading summary must mark preview boundary.");
                RequireContains(dataset.Summary, "does not finalize", "Z reading must not claim finalization.");
                RequireTrue(dataset.Rows.Any(row => row.RowType == "official-document"), "Z reading must include official document rows.");
            });

        ProofFamily(
            datasetService,
            renderer,
            ProductionReportKinds.EjExport,
            dataset =>
            {
                RequireTrue(dataset.ReportKind == ProductionReportKinds.EjExport, "EJ export dataset kind must be stable.");
                RequireTrue(dataset.Rows.Any(row => row.RowType == "ej-document"), "EJ export must include document rows.");
                RequireTrue(dataset.Rows.Any(row => row.RowType == "ej-event"), "EJ export must include event rows.");
                RequireContains(dataset.Summary, "Does not submit to BIR", "EJ export must not claim BIR submission.");
            });

        ProofFamily(
            datasetService,
            renderer,
            ProductionReportKinds.SalesRefundVoidSummary,
            dataset =>
            {
                RequireTrue(dataset.ReportKind == ProductionReportKinds.SalesRefundVoidSummary, "Sales/refund/void dataset kind must be stable.");
                RequireTrue(dataset.Rows.Any(row => row.RowType == "sales-refund-void-summary"), "Sales/refund/void summary row required.");
                RequireTrue(dataset.ControlTotals.Count > 0, "Sales/refund/void summary must include control totals.");
                ProductionReportRow row = dataset.Rows.Single(x => x.RowType == "sales-refund-void-summary");
                RequireTrue(row.Values.ContainsKey("grossPostedSalesCents"), "Gross sales must be represented.");
                RequireTrue(row.Values.ContainsKey("refundedValueCents"), "Refunded value must be represented.");
                RequireTrue(row.Values.ContainsKey("voidedDocumentCount"), "Void count must be represented.");
            });

        ProofFamily(
            datasetService,
            renderer,
            ProductionReportKinds.TaxBucketSummary,
            dataset =>
            {
                RequireTrue(dataset.ReportKind == ProductionReportKinds.TaxBucketSummary, "Tax bucket dataset kind must be stable.");
                RequireTrue(dataset.Rows.Any(row => row.RowType == "tax-bucket-summary"), "Tax bucket summary row required.");
                ProductionReportRow row = dataset.Rows.Single(x => x.RowType == "tax-bucket-summary");
                RequireTrue(row.Values.ContainsKey("vatableSalesCents"), "Vatable sales must be represented.");
                RequireTrue(row.Values.ContainsKey("vatAmountCents"), "VAT amount must be represented.");
                RequireTrue(row.Values.ContainsKey("vatExemptSalesCents"), "VAT-exempt sales must be represented.");
                RequireTrue(row.Values.ContainsKey("zeroRatedSalesCents"), "Zero-rated sales must be represented.");
            });

        ProofFamily(
            datasetService,
            renderer,
            ProductionReportKinds.InternalESalesDraft,
            dataset =>
            {
                RequireTrue(dataset.ReportKind == ProductionReportKinds.InternalESalesDraft, "Internal eSales draft dataset kind must be stable.");
                RequireContains(dataset.Summary, "Internal eSales draft", "eSales draft summary must identify internal draft.");
                RequireContains(dataset.Summary, "unconfirmed", "eSales draft summary must mark schema as unconfirmed.");
                RequireContains(dataset.Summary, "transmission disabled", "eSales draft summary must mark transmission disabled.");
                RequireTrue(dataset.Rows.Any(row => row.RowType == "internal-esales-row"), "Internal eSales draft rows required.");
                RequireTrue(!dataset.SafetyFlags.BirESalesSchemaConfirmed, "BIR eSales schema must remain unconfirmed.");
                RequireTrue(!dataset.SafetyFlags.ESalesTransmissionEnabled, "eSales transmission must remain disabled.");
                RequireTrue(!dataset.SafetyFlags.BirAccreditationClaimed, "BIR accreditation must not be claimed.");
                RequireTrue(dataset.Rows.Any(row => row.Values.Values.Any(value => value.Contains("presentation-only", StringComparison.OrdinalIgnoreCase))), "Internal eSales rows must mark presentation-only boundary.");
            });

        RequireNoDiskOutputCodePath();
    }

    private static void ProofFamily(
        PreCrystalReportDatasetService datasetService,
        NativePdfReportRenderer renderer,
        string reportKind,
        Action<ProductionReportDatasetEnvelope> assertDataset)
    {
        ProductionReportDatasetRequest request = new(
            reportKind,
            RangeStart,
            RangeEnd,
            BirMachineProfileId: null,
            RegistrationScopeCode: "LOCAL_SINGLE_STORE",
            DocumentKind: "OFFICIAL_INVOICE",
            ActorUserId,
            TerminalId: "HOST-M4-PROOF");

        Result<ProductionReportDatasetEnvelope> built = datasetService
            .BuildDatasetAsync(request, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        RequireTrue(built.IsSuccess, "Dataset build must succeed for " + reportKind + ": " + built.Error);
        ProductionReportDatasetEnvelope dataset = built.Value;

        RequireTrue(!string.IsNullOrWhiteSpace(dataset.ContentSha256), reportKind + " content hash must be populated.");
        RequireTrue(!string.IsNullOrWhiteSpace(dataset.DatasetSha256), reportKind + " dataset envelope hash must be populated.");
        RequireTrue(dataset.SafetyFlags.DatasetAvailable, reportKind + " dataset must be available.");
        RequireTrue(dataset.SafetyFlags.UsesCanonicalM5Projection, reportKind + " must use canonical M5 projection.");
        RequireTrue(!dataset.SafetyFlags.ProductionActivationEnabled, reportKind + " must not enable production activation.");
        RequireTrue(!dataset.SafetyFlags.CrystalRuntimeAvailable, reportKind + " must not claim Crystal runtime.");

        assertDataset(dataset);

        byte[] directBytes = NativePdfDocumentBuilder.BuildPdfBytes(dataset, CancellationToken.None);
        AssertPdfBytes(reportKind + " direct builder", directBytes);

        Result<ProductionReportRenderResult> rendered = renderer
            .RenderAsync(new ProductionReportRenderRequest(
                dataset,
                ProductionReportOutputKinds.Pdf,
                TimeoutMilliseconds: 30000,
                CancellationToken.None), CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        RequireTrue(rendered.IsSuccess, "Native PDF renderer must succeed for " + reportKind + ": " + rendered.Error);
        ProductionReportRenderResult result = rendered.Value;
        RequireTrue(result.Success, "Renderer result success flag must be true for " + reportKind + ".");
        RequireTrue(result.Artifact is not null, "Artifact metadata must be populated for " + reportKind + ".");

        ProductionReportArtifactMetadata artifact = result.Artifact!;
        RequireTrue(artifact.ContentType == NativePdfArtifactFactory.PdfContentType, "Artifact content type must be application/pdf for " + reportKind + ".");
        RequireTrue(artifact.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase), "Artifact filename must end with .pdf for " + reportKind + ".");
        RequireTrue(Path.GetFileName(artifact.FileName) == artifact.FileName, "Artifact filename must not include a caller path for " + reportKind + ".");
        RequireTrue(artifact.ByteLength > 0, "Artifact byte length must be greater than zero for " + reportKind + ".");
        RequireTrue(!string.IsNullOrWhiteSpace(artifact.Sha256), "Artifact SHA-256 must be populated for " + reportKind + ".");
        RequireTrue(artifact.SourceDatasetSha256 == dataset.DatasetSha256, "Artifact source dataset hash must match dataset envelope hash for " + reportKind + ".");
    }

    private static void AssertPdfBytes(string label, byte[] bytes)
    {
        RequireTrue(bytes.Length > 0, label + " PDF bytes must be non-empty.");
        RequireTrue(bytes.Length >= 4, label + " PDF bytes must include header.");
        string header = Encoding.ASCII.GetString(bytes, 0, 4);
        RequireTrue(header == "%PDF", label + " PDF bytes must start with %PDF.");
    }

    private static void RequireNoDiskOutputCodePath()
    {
        string root = FindRoot();
        string nativePdfRoot = Path.Combine(root, "src", "Salryn.Infrastructure", "Reports", "NativePdf");
        string combined = string.Join("\n", Directory.GetFiles(nativePdfRoot, "*.cs").Select(File.ReadAllText));

        RequireNotContains(combined, "File.WriteAllBytes", "Native PDF renderer must not write PDF files to disk.");
        RequireNotContains(combined, "new FileStream", "Native PDF renderer must not use FileStream.");
        RequireNotContains(combined, "File.Create", "Native PDF renderer must not create files.");
        RequireNotContains(combined, "File.Open", "Native PDF renderer must not open output files.");
        RequireNotContains(combined, "Directory.CreateDirectory", "Native PDF renderer must not create output directories.");
        RequireNotContains(combined, "OutputPath", "Native PDF renderer must not accept caller output paths.");
        RequireNotContains(combined, "CrystalDecisions", "Native PDF renderer must not call Crystal.");
        RequireNotContains(combined, "ReportDocument", "Native PDF renderer must not call Crystal ReportDocument.");
    }

    private sealed class ControlledOfficialReportingProjectionService : IOfficialReportingProjectionService
    {
        public Task<Result<OfficialReportingProjection>> ProjectAsync(
            OfficialReportingProjectionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Result<OfficialReportingProjection>.Success(CreateProjection(request)));
        }

        private static OfficialReportingProjection CreateProjection(OfficialReportingProjectionRequest request)
        {
            Guid doc1Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
            Guid doc2Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2");
            Guid actor = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

            OfficialReportingDocumentRow posted = new(
                JournalEntryId: doc1Id,
                SaleId: Guid.Parse("cccccccc-cccc-cccc-cccc-ccccccccccc1"),
                OfficialDocumentNumber: "SI-000001",
                PostedAt: RangeStart.AddHours(2),
                SnapshotSchema: "SALRYN_OFFICIAL_DOCUMENT_SNAPSHOT_V1",
                SnapshotPayloadHash: new string('a', 64),
                SnapshotHashMatches: true,
                LifecycleState: "POSTED",
                GrossTotalCents: 100000,
                RefundedTotalCents: 10000,
                NetTotalCents: 90000,
                IsVoided: false,
                VatableBaseCents: 80357,
                VatAmountCents: 9643,
                VatExemptCents: 5000,
                ZeroRatedCents: 2500,
                SsptCents: 1000,
                NonVatExemptCents: 0,
                RefundedVatableBaseCents: 8929,
                RefundedVatAmountCents: 1071,
                RefundedVatExemptCents: 0,
                RefundedZeroRatedCents: 0,
                RefundedSsptCents: 0,
                RefundedNonVatExemptCents: 0,
                ReprintCount: 1,
                VoidCount: 0,
                RefundCount: 1,
                AdjustmentCount: 1);

            OfficialReportingDocumentRow voided = new(
                JournalEntryId: doc2Id,
                SaleId: Guid.Parse("cccccccc-cccc-cccc-cccc-ccccccccccc2"),
                OfficialDocumentNumber: "SI-000002",
                PostedAt: RangeStart.AddHours(4),
                SnapshotSchema: "SALRYN_OFFICIAL_DOCUMENT_SNAPSHOT_V1",
                SnapshotPayloadHash: new string('b', 64),
                SnapshotHashMatches: true,
                LifecycleState: "VOIDED",
                GrossTotalCents: 50000,
                RefundedTotalCents: 0,
                NetTotalCents: 0,
                IsVoided: true,
                VatableBaseCents: 44643,
                VatAmountCents: 5357,
                VatExemptCents: 0,
                ZeroRatedCents: 0,
                SsptCents: 0,
                NonVatExemptCents: 0,
                RefundedVatableBaseCents: 0,
                RefundedVatAmountCents: 0,
                RefundedVatExemptCents: 0,
                RefundedZeroRatedCents: 0,
                RefundedSsptCents: 0,
                RefundedNonVatExemptCents: 0,
                ReprintCount: 0,
                VoidCount: 1,
                RefundCount: 0,
                AdjustmentCount: 0);

            OfficialReportingEventRow refund = new(
                EventId: Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddd01"),
                JournalEntryId: doc1Id,
                OfficialDocumentNumber: "SI-000001",
                EventType: "REFUND",
                CreatedAt: RangeStart.AddHours(3),
                Reason: "Controlled M4 refund proof",
                ActorUserId: actor,
                MetadataJson: "{\"amountCents\":10000}");

            OfficialReportingEventRow reprint = new(
                EventId: Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddd02"),
                JournalEntryId: doc1Id,
                OfficialDocumentNumber: "SI-000001",
                EventType: "REPRINT",
                CreatedAt: RangeStart.AddHours(3).AddMinutes(30),
                Reason: "Controlled M4 reprint proof",
                ActorUserId: actor,
                MetadataJson: "{\"copy\":1}");

            OfficialReportingEventRow adjustment = new(
                EventId: Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddd03"),
                JournalEntryId: doc1Id,
                OfficialDocumentNumber: "SI-000001",
                EventType: "ADJUSTMENT",
                CreatedAt: RangeStart.AddHours(3).AddMinutes(45),
                Reason: "Controlled M4 adjustment proof",
                ActorUserId: actor,
                MetadataJson: "{\"reason\":\"test\"}");

            OfficialReportingEventRow voidEvent = new(
                EventId: Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddd04"),
                JournalEntryId: doc2Id,
                OfficialDocumentNumber: "SI-000002",
                EventType: "VOID",
                CreatedAt: RangeStart.AddHours(5),
                Reason: "Controlled M4 void proof",
                ActorUserId: actor,
                MetadataJson: "{\"voided\":true}");

            return new OfficialReportingProjection(
                request.RangeStart,
                request.RangeEnd,
                request.BirMachineProfileId,
                request.RegistrationScopeCode,
                request.DocumentKind,
                new[] { posted, voided },
                new[] { refund, reprint, adjustment, voidEvent },
                GrossPostedSalesCents: 150000,
                VoidedSalesCents: 50000,
                RefundedValueCents: 10000,
                NetSalesCents: 90000,
                VatableSalesCents: 80357,
                VatAmountCents: 9643,
                VatExemptSalesCents: 5000,
                ZeroRatedSalesCents: 2500,
                SsptSalesCents: 1000,
                NonVatExemptSalesCents: 0,
                CashSettlementRefundCents: 10000,
                DocumentCount: 2,
                VoidedDocumentCount: 1,
                RefundEventCount: 1,
                ReprintEventCount: 1,
                AdjustmentEventCount: 1,
                ProjectionContentSha256: new string('c', 64),
                AllSnapshotHashesMatch: true,
                ProductionActivationEnabled: false,
                BirAuthoritativeESalesSchemaConfirmed: false,
                TransmissionEnabled: false,
                Summary: "Controlled M4 reporting projection: two documents, one void, one refund, one reprint, one adjustment, non-zero totals, eSales unconfirmed and non-transmitting.");
        }
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
        if (!text.Contains(expected, StringComparison.OrdinalIgnoreCase))
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
