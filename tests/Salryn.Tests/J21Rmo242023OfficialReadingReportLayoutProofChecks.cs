using System.Security.Cryptography;
using System.Text;
using Salryn.Core.Common;
using Salryn.Core.Services;

namespace Salryn.Tests;

public static class J21Rmo242023OfficialReadingReportLayoutProofChecks
{
    private static readonly Guid CashierUserId = Guid.Parse("29111111-1111-1111-1111-111111111111");
    private static readonly Guid PreviousZReadingId = Guid.Parse("29111111-1111-1111-1111-111111111112");
    private static readonly Guid CurrentZReadingId = Guid.Parse("29111111-1111-1111-1111-111111111113");
    private static readonly Guid FirstOriginalEventId = Guid.Parse("29111111-1111-1111-1111-111111111114");
    private static readonly Guid LastOriginalEventId = Guid.Parse("29111111-1111-1111-1111-111111111115");
    private static readonly Guid FirstJournalEntryId = Guid.Parse("29111111-1111-1111-1111-111111111116");
    private static readonly Guid LastJournalEntryId = Guid.Parse("29111111-1111-1111-1111-111111111117");

    private static readonly IReadOnlyList<string> RequiredSections = new[]
    {
        "ReportHeader",
        "MachineProfile",
        "BusinessPeriod",
        "SalesTotals",
        "TaxTotals",
        "PaymentTotals",
        "CashierOrTerminalBreakdown",
        "LifecycleCoverage",
        "EvidenceFooter"
    };

    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string layoutText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "OfficialReadingReportLayoutBuilder.cs");
        string coreProjectText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Salryn.Core.csproj");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        VerifySourceGuards(layoutText, coreProjectText, coreSmokeText);
        VerifyXReadingBuildsAllSections();
        VerifyZReadingBuildsAllSections();
        VerifyReportHeaders();
        VerifyMachineProfile();
        VerifyBusinessPeriod();
        VerifySalesTotals();
        VerifyTaxTotals();
        VerifyPaymentOrder();
        VerifyBreakdownOrder();
        VerifyLifecycleCoverage();
        VerifyEvidenceFooter();
        VerifyTextRendererMarkersAndDeterminism();
        VerifyPreviewArtifactBehavior();
        VerifyValidationFailures();
    }

    private static void VerifySourceGuards(
        string layoutText,
        string coreProjectText,
        string coreSmokeText)
    {
        RequireContains(layoutText, "OfficialReadingReportKind", "09I must expose reading report kind.");
        RequireContains(layoutText, "OfficialReadingReportInput", "09I must expose reading report input.");
        RequireContains(layoutText, "OfficialReadingReportLayoutDocument", "09I must expose reading layout document.");
        RequireContains(layoutText, "OfficialReadingReportLayoutSection", "09I must expose reading layout sections.");
        RequireContains(layoutText, "OfficialReadingReportLayoutLine", "09I must expose reading layout lines.");
        RequireContains(layoutText, "OfficialReadingReportLayoutBuilder", "09I must expose reading layout builder.");
        RequireContains(layoutText, "OfficialReadingReportTextRenderResult", "09I must expose reading text result.");
        RequireContains(layoutText, "OfficialReadingReportTextRenderer", "09I must expose reading text renderer.");
        RequireContains(layoutText, "OfficialReadingReportPreviewArtifactPackage", "09I must expose preview artifact package.");
        RequireContains(layoutText, "OfficialReadingReportPreviewResult", "09I must expose preview result.");
        RequireContains(layoutText, "OfficialReadingReportPreviewBuilder", "09I must expose preview builder.");
        RequireContains(layoutText, "SALRYN OFFICIAL X/Z READING LAYOUT PROOF", "09I text renderer must include stable title.");
        RequireContains(layoutText, "REPORT_LAYOUT_ONLY_NO_STATE_MUTATION", "09I lifecycle coverage must include no-state-mutation marker.");
        RequireContains(layoutText, "SHA256.HashData(Encoding.UTF8.GetBytes(text))", "09I preview must hash UTF-8 rendered text.");
        RequireContains(layoutText, "Convert.ToHexString(hash).ToLowerInvariant()", "09I preview must use lowercase SHA-256 hex.");
        RequireContains(layoutText, "salryn://official-reading-preview/", "09I preview must use stable reading artifact references.");
        RequireContains(coreSmokeText, "J21Rmo242023OfficialReadingReportLayoutProofChecks.Run();", "CoreSmoke must run 09I reading layout proof checks.");

        int previousIndex = coreSmokeText.IndexOf(
            "J21Rmo242023OfficialTextPreviewRendererChecks.Run();",
            StringComparison.Ordinal);
        int readingIndex = coreSmokeText.IndexOf(
            "J21Rmo242023OfficialReadingReportLayoutProofChecks.Run();",
            StringComparison.Ordinal);

        if (previousIndex < 0 || readingIndex < 0 || readingIndex < previousIndex)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. 09I must run after 09H.");
        }

        string[] forbidden =
        {
            "CrystalDecisions",
            ".rpt",
            "System.Windows.Forms",
            "Salryn.Infrastructure",
            "Microsoft.Data.Sqlite",
            "Sqlite",
            "SQLite",
            "File.Write",
            "FileStream",
            "Process.Start",
            "ESC/POS",
            "EscPos",
            "PrintDocument",
            "PrinterSettings",
            "System.Drawing.Printing",
            "PdfDocument",
            "ExportToDisk",
            "GetTempPath",
            "GetTempFileName",
            "CompleteSale",
            "OfficialCheckout",
            "CheckoutCompletion",
            "ElectronicJournalAppend",
            "AppendOriginal",
            "CommitOriginal",
            "JournalAnchorInsert",
            "OfficialZReadingService",
            "IOfficialZReadingPersistence",
            "FinalizeAsync",
            "SaveAsync",
            "InsertZReading",
            "FiscalReset"
        };

        foreach (string marker in forbidden)
        {
            RequireDoesNotContain(layoutText, marker, $"09I layout proof must not contain forbidden marker: {marker}");
        }

        RequireDoesNotContain(coreProjectText, "Crystal", "09I must not add Crystal packages to Core.");
        RequireDoesNotContain(coreProjectText, "Pdf", "09I must not add PDF packages to Core.");
    }

    private static void VerifyXReadingBuildsAllSections()
    {
        OfficialReadingReportLayoutDocument layout =
            BuildLayout(CreateInput(OfficialReadingReportKind.XReading));

        RequireEqual(OfficialReadingReportKind.XReading, layout.ReportKind, "09I X-reading layout must preserve report kind.");
        RequireSections(layout);
    }

    private static void VerifyZReadingBuildsAllSections()
    {
        OfficialReadingReportLayoutDocument layout =
            BuildLayout(CreateInput(OfficialReadingReportKind.ZReading));

        RequireEqual(OfficialReadingReportKind.ZReading, layout.ReportKind, "09I Z-reading layout must preserve report kind.");
        RequireSections(layout);
    }

    private static void VerifyReportHeaders()
    {
        OfficialReadingReportLayoutSection xHeader =
            RequireSection(BuildLayout(CreateInput(OfficialReadingReportKind.XReading)), "ReportHeader");
        OfficialReadingReportLayoutSection zHeader =
            RequireSection(BuildLayout(CreateInput(OfficialReadingReportKind.ZReading)), "ReportHeader");

        RequireLineValue(xHeader, "ReportKind", "XReading");
        RequireLineValue(xHeader, "ReadingNumber", "XR-2026-000001");
        RequireLineValue(xHeader, "GeneratedAt", "2026-06-26T12:05:00.0000000+00:00");
        RequireLineValue(xHeader, "TerminalId", "POS-READ-01");
        RequireLineValue(xHeader, "CashierUserId", CashierUserId.ToString("D"));
        RequireLineValue(xHeader, "CashierIdentifier", "cashier-xz");
        RequireLineValue(xHeader, "ReportLabel", "OFFICIAL_X_READING_PREVIEW");

        RequireLineValue(zHeader, "ReportKind", "ZReading");
        RequireLineValue(zHeader, "ReportNumber", "ZR-2026-000001");
        RequireLineValue(zHeader, "ReportLabel", "OFFICIAL_Z_READING_PREVIEW");
    }

    private static void VerifyMachineProfile()
    {
        OfficialReadingReportLayoutSection machine =
            RequireSection(BuildLayout(CreateInput(OfficialReadingReportKind.XReading)), "MachineProfile");

        RequireLineValue(machine, "RegisteredTaxpayerName", "KIRCH-NAIRU TRADING");
        RequireLineValue(machine, "BusinessName", "SALRYN TEST STORE");
        RequireLineValue(machine, "RegisteredAddress", "123 Sample Registered Address");
        RequireLineValue(machine, "BranchAddress", "456 Sample Branch Address");
        RequireLineValue(machine, "TaxpayerIdentificationNumber", "123-456-789-000");
        RequireLineValue(machine, "BranchCode", "001");
        RequireLineValue(machine, "MachineIdentificationNumber", "MIN-09I-001");
        RequireLineValue(machine, "MachineSerialNumber", "MSN-09I-001");
        RequireLineValue(machine, "PosTerminalNumber", "PTN-09I-001");
        RequireLineValue(machine, "AccreditationNumber", "ACC-09I-001");
        RequireLineValue(machine, "PermitToUseNumber", "PTU-09I-001");
        RequireLineValue(machine, "SoftwareName", "SALRYN");
        RequireLineValue(machine, "SoftwareVersion", "0.9I");
        RequireLineValue(machine, "SoftwareBuild", "ac1355e");
        RequireLineValue(machine, "ComplianceMode", "FoundationOnly");
    }

    private static void VerifyBusinessPeriod()
    {
        OfficialReadingReportLayoutSection period =
            RequireSection(BuildLayout(CreateInput(OfficialReadingReportKind.ZReading)), "BusinessPeriod");

        RequireLineValue(period, "BusinessDate", "2026-06-26");
        RequireLineValue(period, "PeriodStart", "2026-06-26T00:00:00.0000000+00:00");
        RequireLineValue(period, "PeriodEnd", "2026-06-27T00:00:00.0000000+00:00");
        RequireLineValue(period, "IsFinalizedZReading", "true");
        RequireLineValue(period, "PreviousZReadingId", PreviousZReadingId.ToString("D"));
        RequireLineValue(period, "CurrentZReadingId", CurrentZReadingId.ToString("D"));
    }

    private static void VerifySalesTotals()
    {
        OfficialReadingReportLayoutSection sales =
            RequireSection(BuildLayout(CreateInput(OfficialReadingReportKind.XReading)), "SalesTotals");

        RequireLineValue(sales, "GrossSalesCents", "40100");
        RequireLineValue(sales, "NetSalesCents", "35650");
        RequireLineValue(sales, "DiscountCents", "550");
        RequireLineValue(sales, "RefundVoidCancelCents", "120");
        RequireLineValue(sales, "TaxableSalesCents", "30000");
        RequireLineValue(sales, "NonTaxableSalesCents", "5650");
        RequireLineValue(sales, "TotalSalesCents", "35650");
        RequireLineValue(sales, "TransactionCount", "12");
        RequireLineValue(sales, "VoidedCount", "1");
        RequireLineValue(sales, "CancelledCount", "2");
        RequireLineValue(sales, "CorrectedCount", "3");
    }

    private static void VerifyTaxTotals()
    {
        OfficialReadingReportLayoutSection tax =
            RequireSection(BuildLayout(CreateInput(OfficialReadingReportKind.XReading)), "TaxTotals");

        RequireLineValue(tax, "TaxModeCode", "VAT-DEFERRED");
        RequireLineValue(tax, "TaxCents", "3450");
        RequireLineValue(tax, "VatBucketEngineDeferred", "true");
        RequireLineValue(tax, "TaxModeNotes", "Tax bucket proof only.");
        RequireLineValue(tax, "DeferredComplianceNotes", "VAT bucket engine deferred for 09I proof. | Reading report is layout-only.");
    }

    private static void VerifyPaymentOrder()
    {
        OfficialReadingReportLayoutSection payments =
            RequireSection(BuildLayout(CreateInput(OfficialReadingReportKind.XReading)), "PaymentTotals");

        RequireEqual(3, payments.Lines.Count, "09I payment totals must preserve input count.");
        RequireEqual("PaymentTotal", payments.Lines[0].Label, "09I first payment line label must be stable.");
        RequireEqual("Method=Cash | AmountCents=20000 | Count=8", payments.Lines[0].Value, "09I first payment order must be preserved.");
        RequireEqual("Method=Card | AmountCents=15000 | Count=3", payments.Lines[1].Value, "09I second payment order must be preserved.");
        RequireEqual("Method=GiftCertificate | AmountCents=650 | Count=1", payments.Lines[2].Value, "09I third payment order must be preserved.");
    }

    private static void VerifyBreakdownOrder()
    {
        OfficialReadingReportLayoutSection breakdown =
            RequireSection(BuildLayout(CreateInput(OfficialReadingReportKind.XReading)), "CashierOrTerminalBreakdown");

        RequireEqual(2, breakdown.Lines.Count, "09I breakdown must preserve input count.");
        RequireEqual("CashierIdentifier=cashier-a | TerminalId=POS-READ-01 | TransactionCount=7 | TotalCents=20000", breakdown.Lines[0].Value, "09I first breakdown order must be preserved.");
        RequireEqual("CashierIdentifier=cashier-b | TerminalId=POS-READ-02 | TransactionCount=5 | TotalCents=15650", breakdown.Lines[1].Value, "09I second breakdown order must be preserved.");
    }

    private static void VerifyLifecycleCoverage()
    {
        OfficialReadingReportLayoutSection xCoverage =
            RequireSection(BuildLayout(CreateInput(OfficialReadingReportKind.XReading)), "LifecycleCoverage");
        OfficialReadingReportLayoutSection zCoverage =
            RequireSection(BuildLayout(CreateInput(OfficialReadingReportKind.ZReading)), "LifecycleCoverage");

        RequireLineValue(xCoverage, "CoverageMode", "NON_FINALIZING_PREVIEW_REPORT_COVERAGE");
        RequireLineValue(zCoverage, "CoverageMode", "FINALIZING_COVERAGE_DATA_ONLY");
        RequireLineValue(xCoverage, "StateMutationMarker", "REPORT_LAYOUT_ONLY_NO_STATE_MUTATION");
        RequireLineValue(zCoverage, "StateMutationMarker", "REPORT_LAYOUT_ONLY_NO_STATE_MUTATION");
        RequireLineValue(zCoverage, "CoveredEventCount", "14");
        RequireLineValue(zCoverage, "CoveredSaleCount", "12");
        RequireLineValue(zCoverage, "FirstOriginalEventId", FirstOriginalEventId.ToString("D"));
        RequireLineValue(zCoverage, "LastOriginalEventId", LastOriginalEventId.ToString("D"));
        RequireLineValue(zCoverage, "FirstJournalEntryId", FirstJournalEntryId.ToString("D"));
        RequireLineValue(zCoverage, "LastJournalEntryId", LastJournalEntryId.ToString("D"));
    }

    private static void VerifyEvidenceFooter()
    {
        OfficialReadingReportLayoutSection evidence =
            RequireSection(BuildLayout(CreateInput(OfficialReadingReportKind.XReading)), "EvidenceFooter");

        RequireLineValue(evidence, "SourceEvidenceHash", "SOURCE-EVIDENCE-HASH-09I");
        RequireLineValue(evidence, "SnapshotPayloadHash", "SNAPSHOT-HASH-09I");
        RequireLineValue(evidence, "HashAlgorithm", "SHA-256");
        RequireLineValue(evidence, "GeneratedFrom", "OfficialXReadingResult + machine profile reporting input");
    }

    private static void VerifyTextRendererMarkersAndDeterminism()
    {
        OfficialReadingReportLayoutDocument layout = BuildLayout(CreateInput(OfficialReadingReportKind.XReading));
        OfficialReadingReportTextRenderer renderer = new();
        OfficialReadingReportTextRenderResult first = RequireValue(renderer.Render(layout));
        OfficialReadingReportTextRenderResult second = RequireValue(renderer.Render(layout));

        RequireEqual(first.Text, second.Text, "09I text renderer must be deterministic.");
        RequireDoesNotContain(first.Text, "\r", "09I text renderer must use stable line feeds.");

        foreach (string marker in new[]
        {
            "SALRYN OFFICIAL X/Z READING LAYOUT PROOF",
            "[REPORT]",
            "[MACHINE]",
            "[PERIOD]",
            "[SALES_TOTALS]",
            "[TAX_TOTALS]",
            "[PAYMENT_TOTALS]",
            "[BREAKDOWN]",
            "[LIFECYCLE_COVERAGE]",
            "[EVIDENCE]"
        })
        {
            RequireContains(first.Text, marker, $"09I text renderer must include marker {marker}.");
        }
    }

    private static void VerifyPreviewArtifactBehavior()
    {
        OfficialReadingReportPreviewBuilder previewBuilder = new();
        OfficialReadingReportPreviewResult first =
            RequireValue(previewBuilder.BuildPreview(CreateInput(OfficialReadingReportKind.XReading)));
        OfficialReadingReportPreviewResult second =
            RequireValue(previewBuilder.BuildPreview(CreateInput(OfficialReadingReportKind.XReading)));
        OfficialReadingReportPreviewResult zPreview =
            RequireValue(previewBuilder.BuildPreview(CreateInput(OfficialReadingReportKind.ZReading)));
        OfficialReadingReportPreviewResult changed =
            RequireValue(previewBuilder.BuildPreview(CreateInput(
                OfficialReadingReportKind.XReading,
                totalSalesCents: 45650)));

        RequireEqual(OfficialReadingReportKind.XReading, first.ReportKind, "09I preview must preserve XReading kind.");
        RequireEqual("OFFICIAL_X_READING_PREVIEW", first.ArtifactLabel, "09I XReading default artifact label must be stable.");
        RequireEqual("OFFICIAL_Z_READING_PREVIEW", zPreview.ArtifactLabel, "09I ZReading default artifact label must be stable.");
        RequireEqual(first.ContentHash, second.ContentHash, "09I content hash must be deterministic for same input.");
        RequireEqual(first.ArtifactReference, second.ArtifactReference, "09I artifact reference must be deterministic for same input.");
        RequireEqual(ExpectedHash(first.TextResult.Text), first.ContentHash, "09I content hash must be SHA-256 over UTF-8 rendered text.");
        RequireEqual(first.ContentHash.ToLowerInvariant(), first.ContentHash, "09I content hash must be lowercase.");
        RequireEqual(64, first.ContentHash.Length, "09I SHA-256 hash must be 64 lowercase hex characters.");
        RequireStartsWith(first.ArtifactReference, "salryn://official-reading-preview/", "09I artifact reference must use reading preview prefix.");
        RequireEqual("salryn://official-reading-preview/XReading/" + first.ContentHash, first.ArtifactReference, "09I XReading artifact reference must include kind and hash.");
        RequireEqual("salryn://official-reading-preview/ZReading/" + zPreview.ContentHash, zPreview.ArtifactReference, "09I ZReading artifact reference must include kind and hash.");
        RequireEqual(first.TextResult.Lines.Count, first.LineCount, "09I preview line count must match rendered text.");
        RequireEqual(first.TextResult, first.ArtifactPackage.TextResult, "09I package must include text result.");
        RequireEqual(first.LineCount, first.ArtifactPackage.LineCount, "09I package must include line count.");
        RequireEqual(first.ContentHash, first.ArtifactPackage.ContentHash, "09I package must include content hash.");
        RequireEqual(first.ArtifactReference, first.ArtifactPackage.ArtifactReference, "09I package must include artifact reference.");

        if (string.Equals(first.ContentHash, changed.ContentHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("09I content hash must change when meaningful totals change.");
        }

        OfficialReadingReportPreviewResult custom =
            RequireValue(previewBuilder.BuildPreview(
                CreateInput(OfficialReadingReportKind.XReading),
                "  CUSTOM_READING_PREVIEW  "));

        RequireEqual("CUSTOM_READING_PREVIEW", custom.ArtifactLabel, "09I custom artifact label must be normalized.");
    }

    private static void VerifyValidationFailures()
    {
        OfficialReadingReportLayoutBuilder builder = new();

        RequireFailureCode(
            builder.Build(null),
            "OfficialReadingReport.InputRequired",
            "09I builder must reject null input.");

        RequireFailureCode(
            builder.Build(CreateInput((OfficialReadingReportKind)999)),
            "OfficialReadingReport.InvalidKind",
            "09I builder must reject invalid report kind.");

        RequireFailureCode(
            builder.Build(CreateInput(OfficialReadingReportKind.XReading) with
            {
                SalesTotals = CreateSalesTotals(totalSalesCents: -1)
            }),
            "OfficialReadingReport.InvalidSalesTotals",
            "09I builder must reject negative cent totals.");

        RequireFailureCode(
            builder.Build(CreateInput(OfficialReadingReportKind.XReading) with
            {
                PaymentTotals = new[]
                {
                    new OfficialReadingReportPaymentTotal("Cash", 100, 1),
                    new OfficialReadingReportPaymentTotal("Cash", 200, 1)
                }
            }),
            "OfficialReadingReport.DuplicatePaymentMethod",
            "09I builder must reject duplicate payment methods.");

        RequireFailureCode(
            builder.Build(CreateInput(OfficialReadingReportKind.XReading) with
            {
                CashierOrTerminalBreakdown = new[]
                {
                    new OfficialReadingReportBreakdownLine("cashier-a", "POS-1", 1, 100),
                    new OfficialReadingReportBreakdownLine("cashier-a", "POS-2", 1, 200)
                }
            }),
            "OfficialReadingReport.DuplicateBreakdownKey",
            "09I builder must reject duplicate breakdown keys.");
    }

    private static OfficialReadingReportInput CreateInput(
        OfficialReadingReportKind kind,
        long totalSalesCents = 35650)
    {
        return new OfficialReadingReportInput(
            ReportKind: kind,
            ReadingNumber: kind == OfficialReadingReportKind.XReading ? "XR-2026-000001" : null,
            ReportNumber: kind == OfficialReadingReportKind.ZReading ? "ZR-2026-000001" : null,
            GeneratedAt: new DateTimeOffset(2026, 6, 26, 12, 5, 0, TimeSpan.Zero),
            TerminalId: "POS-READ-01",
            CashierUserId,
            CashierIdentifier: "cashier-xz",
            ReportLabel: null,
            MachineProfile: CreateMachineProfile(),
            BusinessPeriod: new OfficialReadingReportBusinessPeriod(
                new DateOnly(2026, 6, 26),
                new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 6, 27, 0, 0, 0, TimeSpan.Zero),
                kind == OfficialReadingReportKind.ZReading,
                PreviousZReadingId,
                kind == OfficialReadingReportKind.ZReading ? CurrentZReadingId : null),
            SalesTotals: CreateSalesTotals(totalSalesCents),
            TaxTotals: new OfficialReadingReportTaxTotals(
                "VAT-DEFERRED",
                3450,
                VatBucketEngineDeferred: true,
                TaxModeNotes: "Tax bucket proof only.",
                DeferredComplianceNotes: new[]
                {
                    "VAT bucket engine deferred for 09I proof.",
                    "Reading report is layout-only."
                }),
            PaymentTotals: new[]
            {
                new OfficialReadingReportPaymentTotal("Cash", 20000, 8),
                new OfficialReadingReportPaymentTotal("Card", 15000, 3),
                new OfficialReadingReportPaymentTotal("GiftCertificate", 650, 1)
            },
            CashierOrTerminalBreakdown: new[]
            {
                new OfficialReadingReportBreakdownLine("cashier-a", "POS-READ-01", 7, 20000),
                new OfficialReadingReportBreakdownLine("cashier-b", "POS-READ-02", 5, 15650)
            },
            LifecycleCoverage: new OfficialReadingReportLifecycleCoverage(
                CoveredEventCount: 14,
                CoveredSaleCount: 12,
                FirstOriginalEventId,
                LastOriginalEventId,
                FirstJournalEntryId,
                LastJournalEntryId),
            Evidence: new OfficialReadingReportEvidence(
                SourceEvidenceHash: "SOURCE-EVIDENCE-HASH-09I",
                SnapshotPayloadHash: "SNAPSHOT-HASH-09I",
                HashAlgorithm: "SHA-256",
                GeneratedFrom: "OfficialXReadingResult + machine profile reporting input"));
    }

    private static OfficialReadingReportMachineProfile CreateMachineProfile()
    {
        return new OfficialReadingReportMachineProfile(
            RegisteredTaxpayerName: "KIRCH-NAIRU TRADING",
            BusinessName: "SALRYN TEST STORE",
            RegisteredAddress: "123 Sample Registered Address",
            BranchAddress: "456 Sample Branch Address",
            TaxpayerIdentificationNumber: "123-456-789-000",
            BranchCode: "001",
            MachineIdentificationNumber: "MIN-09I-001",
            MachineSerialNumber: "MSN-09I-001",
            PosTerminalNumber: "PTN-09I-001",
            AccreditationNumber: "ACC-09I-001",
            PermitToUseNumber: "PTU-09I-001",
            SoftwareName: "SALRYN",
            SoftwareVersion: "0.9I",
            SoftwareBuild: "ac1355e",
            ComplianceMode: "FoundationOnly");
    }

    private static OfficialReadingReportSalesTotals CreateSalesTotals(
        long totalSalesCents)
    {
        return new OfficialReadingReportSalesTotals(
            GrossSalesCents: 40100,
            NetSalesCents: 35650,
            DiscountCents: 550,
            RefundVoidCancelCents: 120,
            TaxableSalesCents: 30000,
            NonTaxableSalesCents: 5650,
            TotalSalesCents: totalSalesCents,
            TransactionCount: 12,
            VoidedCount: 1,
            CancelledCount: 2,
            CorrectedCount: 3);
    }

    private static OfficialReadingReportLayoutDocument BuildLayout(
        OfficialReadingReportInput input)
    {
        return RequireValue(new OfficialReadingReportLayoutBuilder().Build(input));
    }

    private static void RequireSections(OfficialReadingReportLayoutDocument document)
    {
        RequireEqual(RequiredSections.Count, document.Sections.Count, "09I layout must include exactly the required sections.");

        for (int index = 0; index < RequiredSections.Count; index++)
        {
            RequireEqual(RequiredSections[index], document.Sections[index].Name, "09I layout section order must be stable.");
        }
    }

    private static OfficialReadingReportLayoutSection RequireSection(
        OfficialReadingReportLayoutDocument document,
        string sectionName)
    {
        OfficialReadingReportLayoutSection? section =
            document.Sections.FirstOrDefault(item => string.Equals(item.Name, sectionName, StringComparison.Ordinal));

        if (section is null)
        {
            throw new InvalidOperationException($"Expected 09I layout section {sectionName}.");
        }

        return section;
    }

    private static void RequireLineValue(
        OfficialReadingReportLayoutSection section,
        string label,
        string expectedValue)
    {
        OfficialReadingReportLayoutLine? line =
            section.Lines.FirstOrDefault(item => string.Equals(item.Label, label, StringComparison.Ordinal));

        if (line is null)
        {
            throw new InvalidOperationException($"Expected 09I layout line {section.Name}.{label}.");
        }

        RequireEqual(expectedValue, line.Value, $"Unexpected 09I layout line value for {section.Name}.{label}.");
    }

    private static string ExpectedHash(string text)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
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

    private static string ReadRequiredFile(DirectoryInfo repoRoot, params string[] pathParts)
    {
        string path = repoRoot.FullName;

        foreach (string pathPart in pathParts)
        {
            path = Path.Combine(path, pathPart);
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Required file was not found: {path}");
        }

        return File.ReadAllText(path);
    }

    private static T RequireValue<T>(Result<T> result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Expected 09I success, got {result.ErrorDetail.Code}: {result.Error}");
        }

        return result.Value;
    }

    private static void RequireFailureCode<T>(
        Result<T> result,
        string expectedCode,
        string message)
    {
        if (result.IsSuccess || result.ErrorDetail.Code != expectedCode)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireContains(
        string text,
        string value,
        string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireDoesNotContain(
        string text,
        string value,
        string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireStartsWith(
        string text,
        string value,
        string message)
    {
        if (!text.StartsWith(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireEqual<T>(
        T expected,
        T actual,
        string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected: {expected}. Actual: {actual}.");
        }
    }
}
