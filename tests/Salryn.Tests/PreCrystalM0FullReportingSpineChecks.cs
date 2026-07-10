using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Core.Services.Reporting;
using Salryn.Core.Services.Reports;
using Salryn.Infrastructure.Reports;

namespace Salryn.Tests;

public static class PreCrystalM0FullReportingSpineChecks
{
    public static void Run()
    {
        string root = FindRoot();

        RequireTrue(File.Exists(Path.Combine(root, "00-HANDOFF", "PRECRYSTAL_M0_FULL_SPINE_STUDY.md")), "Study document required.");
        RequireTrue(File.Exists(Path.Combine(root, "00-HANDOFF", "PRECRYSTAL_M0_FULL_REPORT_CATALOG.md")), "Catalog document required.");
        RequireTrue(File.Exists(Path.Combine(root, "00-HANDOFF", "PRECRYSTAL_M0_QA1_HARDENING_REPORT.md")), "QA1 hardening report required.");

        string coreSmoke = File.ReadAllText(Path.Combine(root, "tools", "Salryn.Tools.CoreSmoke", "Program.cs"));
        RequireContains(coreSmoke, "reports-precrystal-m0-full-reporting-spine", "Focused CoreSmoke gate required.");
        RequireContains(coreSmoke, "PreCrystalM0FullReportingSpineChecks.Run()", "Full CoreSmoke must run PRE-CRYSTAL M0 gate.");

        RequireTrue(PreCrystalReportCatalog.IsKnown(ProductionReportKinds.TraditionalXReading), "X report kind allow-listed.");
        RequireTrue(PreCrystalReportCatalog.IsKnown(ProductionReportKinds.InternalESalesDraft), "Internal eSales draft report kind allow-listed.");
        RequireTrue(!PreCrystalReportCatalog.IsKnown("../unsafe.rpt"), "Unknown/path-like report kind rejected.");
        RequireTrue(!PreCrystalReportCatalog.IsKnown("SELECT * FROM sales"), "SQL-like report kind rejected.");

        PreCrystalReportCatalog.TryGet(ProductionReportKinds.TraditionalXReading, out ProductionReportCatalogItem xItem);
        RequireTrue(xItem.RequiredPermission == ProductionReportPermissions.OfficialReadingView, "X reading permission mapped.");
        PreCrystalReportCatalog.TryGet(ProductionReportKinds.InventoryOnHand, out ProductionReportCatalogItem inventoryItem);
        RequireTrue(!inventoryItem.DatasetAvailable && inventoryItem.UnavailableReason is not null, "Unsafe inventory source boundary is unavailable with reason.");

        FakeProjectionService fakeProjection = new();
        DateTimeOffset fixedNow = new(2026, 7, 10, 0, 0, 0, TimeSpan.Zero);
        PreCrystalReportDatasetService datasets = new(fakeProjection, () => fixedNow);

        ProductionReportDatasetRequest valid = new(
            ProductionReportKinds.TraditionalXReading,
            new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 11, 0, 0, 0, TimeSpan.Zero),
            null,
            "LOCAL_SINGLE_STORE",
            "OFFICIAL_INVOICE",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "HOST-TEST");

        Result<ProductionReportDatasetEnvelope> first = datasets.BuildDatasetAsync(valid).GetAwaiter().GetResult();
        Result<ProductionReportDatasetEnvelope> second = datasets.BuildDatasetAsync(valid).GetAwaiter().GetResult();
        RequireTrue(first.IsSuccess, "First deterministic dataset succeeds: " + Fail(first));
        RequireTrue(second.IsSuccess, "Second deterministic dataset succeeds: " + Fail(second));
        RequireTrue(first.Value.ContentSha256 == second.Value.ContentSha256, "Equivalent dataset content hash deterministic.");
        RequireTrue(first.Value.DatasetSha256 == second.Value.DatasetSha256, "Equivalent envelope hash deterministic when generated-at is fixed.");
        RequireTrue(first.Value.SourceHashes.Any(x => x.SourceKind == "m5-canonical-projection"), "Dataset records M5 canonical projection source hash.");
        RequireTrue(first.Value.SafetyFlags.UsesCanonicalM5Projection, "Official dataset marks canonical M5 projection boundary.");
        RequireTrue(fakeProjection.CallCount >= 2, "Dataset builder consumed canonical projection service.");

        PreCrystalReportDatasetService laterClock = new(fakeProjection, () => fixedNow.AddMinutes(5));
        Result<ProductionReportDatasetEnvelope> later = laterClock.BuildDatasetAsync(valid).GetAwaiter().GetResult();
        RequireTrue(later.IsSuccess, "Later dataset succeeds: " + Fail(later));
        RequireTrue(first.Value.ContentSha256 == later.Value.ContentSha256, "Stable content hash excludes runtime envelope timestamp.");
        RequireTrue(first.Value.DatasetSha256 != later.Value.DatasetSha256, "Envelope hash changes when runtime generated-at changes.");

        Result<ProductionReportDatasetEnvelope> invalidRange = datasets.BuildDatasetAsync(valid with
        {
            RangeStart = valid.RangeEnd,
            RangeEnd = valid.RangeStart
        }).GetAwaiter().GetResult();
        RequireTrue(invalidRange.IsFailure && invalidRange.ErrorDetail.Code == "Reports.InvalidRange", "Invalid ranges rejected.");

        Result<ProductionReportDatasetEnvelope> tooWide = datasets.BuildDatasetAsync(valid with
        {
            RangeEnd = valid.RangeStart.AddDays(ProductionReportDatasetLimits.MaxRangeDays + 1)
        }).GetAwaiter().GetResult();
        RequireTrue(tooWide.IsFailure && tooWide.ErrorDetail.Code == "Reports.RangeTooLarge", "Range cap enforced.");

        Result<ProductionReportDatasetEnvelope> invalidKind = datasets.BuildDatasetAsync(valid with
        {
            DocumentKind = "OFFICIAL_CREDIT_MEMO"
        }).GetAwaiter().GetResult();
        RequireTrue(invalidKind.IsFailure && invalidKind.ErrorDetail.Code == "Reports.InvalidDocumentKind", "Unknown document kind rejected.");

        Result<ProductionReportDatasetEnvelope> unknownKind = datasets.BuildDatasetAsync(valid with
        {
            ReportKind = "not-allow-listed"
        }).GetAwaiter().GetResult();
        RequireTrue(unknownKind.IsFailure && unknownKind.ErrorDetail.Code == "Reports.UnknownReportKind", "Unknown report kind rejected.");

        Result<ProductionReportDatasetEnvelope> unavailable = datasets.BuildDatasetAsync(valid with
        {
            ReportKind = ProductionReportKinds.InventoryOnHand
        }).GetAwaiter().GetResult();
        RequireTrue(unavailable.IsSuccess, "Unavailable placeholder still returns a safe dataset envelope.");
        RequireTrue(!unavailable.Value.SafetyFlags.DatasetAvailable, "Unavailable placeholder marks dataset unavailable.");
        RequireTrue(unavailable.Value.Rows.Any(r => r.RowType == "unavailable"), "Unavailable placeholder includes explicit reason row.");

        PreCrystalReportDatasetService rowCapDatasets = new(new FakeProjectionService(ProductionReportDatasetLimits.MaxRows + 1), () => fixedNow);
        Result<ProductionReportDatasetEnvelope> rowCap = rowCapDatasets.BuildDatasetAsync(valid).GetAwaiter().GetResult();
        RequireTrue(rowCap.IsFailure && rowCap.ErrorDetail.Code == "Reports.RowLimitExceeded", "Row cap enforced before materializing oversized report response.");

        Result<ProductionReportDatasetEnvelope> esales = datasets.BuildDatasetAsync(valid with
        {
            ReportKind = ProductionReportKinds.InternalESalesDraft
        }).GetAwaiter().GetResult();
        RequireTrue(esales.IsSuccess, "Internal eSales dataset succeeds: " + Fail(esales));
        RequireTrue(esales.Value.ControlTotals.Any(x => x.Code == "net-sales" && x.ValueCents == 10000), "Internal eSales official totals come from projection control totals.");
        RequireTrue(esales.Value.Rows.Any(r =>
            r.Values.TryGetValue("presentationBoundary", out string? boundary) &&
            boundary.Contains("Official totals come from M5 canonical projection", StringComparison.Ordinal)),
            "Internal eSales rows are explicitly presentation-only.");

        UnavailableCrystalReportRenderer renderer = new();
        ProductionReportRendererStatus status = renderer.GetStatus();
        RequireTrue(!status.Available && !status.CanRenderPdf && !status.CanPrint, "Crystal renderer unavailable and no PDF/print claim.");
        Result<ProductionReportRenderResult> render = renderer.RenderAsync(new ProductionReportRenderRequest(
            first.Value,
            "pdf",
            30000,
            CancellationToken.None)).GetAwaiter().GetResult();
        RequireTrue(render.IsFailure && render.ErrorDetail.Code == "Crystal.Unavailable", "Unavailable renderer fails closed.");

        RequireTrue(!ProductionOfficialPostingActivation.IsEnabled, "Production official posting activation remains OFF.");
        RequireTrue(!first.Value.SafetyFlags.ESalesTransmissionEnabled, "eSales transmission remains disabled in dataset safety flags.");
        RequireTrue(!first.Value.SafetyFlags.BirAccreditationClaimed, "Dataset does not claim BIR accreditation.");

        string endpointSource = File.ReadAllText(Path.Combine(root, "src", "Salryn.Api.Local", "LocalProductionReportEndpoints.cs"));
        RequireNotContains(endpointSource, "TemplatePath", "Endpoint must not accept template paths.");
        RequireNotContains(endpointSource, "SqlText", "Endpoint must not accept SQL text.");
        RequireNotContains(endpointSource, "FilePath", "Endpoint must not accept caller file paths.");
        RequireNotContains(endpointSource, "ProductionReportDatasetEnvelope Dataset", "Render endpoint must not accept caller-supplied dataset envelopes.");
        RequireNotContains(endpointSource, "body.Dataset", "Render endpoint must not read caller-supplied datasets.");
        RequireContains(endpointSource, "GetRequiredSession", "Endpoint actor boundary must use server session.");
        RequireContains(endpointSource, "TryParseMachineId", "Endpoint must reject malformed machine profile filters.");
        RequireContains(endpointSource, "Reports.InvalidMachineProfileId", "Endpoint must expose invalid machine profile error.");
        RequireContains(endpointSource, "Reports.InvalidDocumentKind", "Endpoint must reject unknown document kinds.");
        RequireContains(endpointSource, "Reports.RangeTooLarge", "Endpoint must enforce range cap.");
        RequireContains(endpointSource, "ComposeDatasetService(connectionFactory)", "Render endpoint must build a server-owned dataset before calling renderer.");
        RequireContains(endpointSource, "LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest", "Endpoint must preserve loopback-only boundary.");
        RequireContains(endpointSource, "Reports.Forbidden", "Endpoint must enforce permission failures.");

        string datasetSource = File.ReadAllText(Path.Combine(root, "src", "Salryn.Infrastructure", "Reports", "PreCrystalReportDatasetService.cs"));
        RequireContains(datasetSource, "IOfficialReportingProjectionService", "Dataset service must depend on M5 canonical projection interface.");
        RequireContains(datasetSource, "ProductionReportDatasetLimits.MaxRows", "Dataset service must enforce row cap.");
        RequireContains(datasetSource, "ContentSha256", "Dataset service must expose stable content hash.");
        RequireContains(datasetSource, "DatasetSha256", "Dataset service must expose runtime envelope hash.");
        RequireContains(datasetSource, "presentationOnlyNetCents", "eSales row net must be marked presentation-only.");
        RequireNotContains(datasetSource, "Math.Max(0, doc.Vatable", "eSales dataset must not recompute per-document VAT net values.");
        RequireNotContains(datasetSource, "CrystalDecisions", "No Crystal package reference allowed.");
        RequireNotContains(datasetSource, ".rpt", "No .rpt template binding allowed in M0.");

        string m5ProjectionSource = File.ReadAllText(Path.Combine(root, "src", "Salryn.Core", "Services", "Reporting", "OfficialReportingProjectionContracts.cs"));
        RequireContains(m5ProjectionSource, "ProjectionContentSha256", "M5 projection hash contract remains present.");
        RequireContains(m5ProjectionSource, "TransmissionEnabled", "M5 eSales transmission flag remains present.");
    }

    private sealed class FakeProjectionService : IOfficialReportingProjectionService
    {
        private readonly int _documentCount;

        public FakeProjectionService(int documentCount = 1)
        {
            _documentCount = documentCount;
        }

        public int CallCount { get; private set; }

        public Task<Result<OfficialReportingProjection>> ProjectAsync(
            OfficialReportingProjectionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            OfficialReportingDocumentRow[] docs = Enumerable.Range(1, _documentCount)
                .Select(i => BuildDocument(request, i))
                .ToArray();

            OfficialReportingEventRow ev = new(
                Guid.Parse("44444444-4444-4444-4444-444444444444"),
                docs[0].JournalEntryId,
                docs[0].OfficialDocumentNumber,
                "REFUND",
                request.RangeStart.AddHours(2),
                "test refund",
                Guid.Parse("55555555-5555-5555-5555-555555555555"),
                null);

            long gross = checked(11200L * _documentCount);
            long refunded = checked(1200L * _documentCount);
            long net = checked(10000L * _documentCount);

            OfficialReportingProjection projection = new(
                request.RangeStart,
                request.RangeEnd,
                request.BirMachineProfileId,
                request.RegistrationScopeCode,
                request.DocumentKind,
                docs,
                new[] { ev },
                GrossPostedSalesCents: gross,
                VoidedSalesCents: 0,
                RefundedValueCents: refunded,
                NetSalesCents: net,
                VatableSalesCents: checked(9000L * _documentCount),
                VatAmountCents: checked(1080L * _documentCount),
                VatExemptSalesCents: 0,
                ZeroRatedSalesCents: 0,
                SsptSalesCents: 0,
                NonVatExemptSalesCents: 0,
                CashSettlementRefundCents: refunded,
                DocumentCount: _documentCount,
                VoidedDocumentCount: 0,
                RefundEventCount: 1,
                ReprintEventCount: 1,
                AdjustmentEventCount: 0,
                ProjectionContentSha256: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                AllSnapshotHashesMatch: true,
                ProductionActivationEnabled: false,
                BirAuthoritativeESalesSchemaConfirmed: false,
                TransmissionEnabled: false,
                Summary: "fake canonical projection");

            return Task.FromResult(Result<OfficialReportingProjection>.Success(projection));
        }

        private static OfficialReportingDocumentRow BuildDocument(OfficialReportingProjectionRequest request, int ordinal)
        {
            return new OfficialReportingDocumentRow(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                "INV-" + ordinal.ToString("000000"),
                request.RangeStart.AddHours(1).AddTicks(ordinal),
                "SNAPSHOT_V1",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                SnapshotHashMatches: true,
                "Committed",
                GrossTotalCents: 11200,
                RefundedTotalCents: 1200,
                NetTotalCents: 10000,
                IsVoided: false,
                VatableBaseCents: 10000,
                VatAmountCents: 1200,
                VatExemptCents: 0,
                ZeroRatedCents: 0,
                SsptCents: 0,
                NonVatExemptCents: 0,
                RefundedVatableBaseCents: 1000,
                RefundedVatAmountCents: 120,
                RefundedVatExemptCents: 0,
                RefundedZeroRatedCents: 0,
                RefundedSsptCents: 0,
                RefundedNonVatExemptCents: 0,
                ReprintCount: 1,
                VoidCount: 0,
                RefundCount: 1,
                AdjustmentCount: 0);
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

    private static string Fail<T>(Result<T> result) => result.IsFailure ? result.ErrorDetail.Message : string.Empty;

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
