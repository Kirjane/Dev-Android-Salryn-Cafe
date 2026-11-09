using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Core.Services.InvoiceOutput;
using Salryn.Core.ValueObjects;

namespace Salryn.Tests;

/// <summary>
/// TRAD-M3: VAT/Non-VAT invoice projection from immutable snapshot. Not accreditation. Not legal issuance.
/// </summary>
public static class RmoBirTradM3InvoiceOutputChecks
{
    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        AssertDocs(projectRoot);
        AssertSourceInvariants(projectRoot);
        AssertVariantsAndDeterminism();
        AssertFailClosedRules();
        AssertNonMutationAndActivationOff(projectRoot);
        Console.WriteLine("[TRAD-M3] Invoice output checks passed.");
    }

    private static void AssertDocs(string projectRoot)
    {
        string handoff = Path.Combine(projectRoot, "00-HANDOFF");
        RequireTrue(File.Exists(Path.Combine(handoff, "RMO_BIR_TRAD_M3_VAT_NONVAT_INVOICE_OUTPUT_STUDY.md")), "Study required.");
        RequireTrue(File.Exists(Path.Combine(handoff, "RMO_BIR_TRAD_M3_GO_STOP_DECISION.md")), "GO/STOP required.");
    }

    private static void AssertSourceInvariants(string projectRoot)
    {
        string parser = File.ReadAllText(Path.Combine(projectRoot, "src", "Salryn.Core", "Services", "InvoiceOutput", "OfficialInvoiceSnapshotParser.cs"));
        string builder = File.ReadAllText(Path.Combine(projectRoot, "src", "Salryn.Core", "Services", "InvoiceOutput", "InvoiceRenderModelBuilder.cs"));
        string html = File.ReadAllText(Path.Combine(projectRoot, "src", "Salryn.Core", "Services", "InvoiceOutput", "InvoiceHtmlRenderer.cs"));
        string thermal = File.ReadAllText(Path.Combine(projectRoot, "src", "Salryn.Core", "Services", "InvoiceOutput", "InvoiceThermalTextRenderer.cs"));
        string service = File.ReadAllText(Path.Combine(projectRoot, "src", "Salryn.Core", "Services", "InvoiceOutput", "InvoiceOutputService.cs"));
        string sqlite = File.ReadAllText(Path.Combine(projectRoot, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteOfficialInvoiceSnapshotReadService.cs"));
        string endpoints = File.ReadAllText(Path.Combine(projectRoot, "src", "Salryn.Api.Local", "LocalOfficialInvoiceOutputEndpoints.cs"));
        string factory = File.ReadAllText(Path.Combine(projectRoot, "src", "Salryn.Api.Local", "LocalApiApplicationFactory.cs"));
        string setupPage = File.ReadAllText(Path.Combine(projectRoot, "src", "Salryn.App.Web", "src", "pages", "OfficialSetupPage.tsx"));
        string api = File.ReadAllText(Path.Combine(projectRoot, "src", "Salryn.App.Web", "src", "api", "officialSetup.ts"));
        string coreSmoke = File.ReadAllText(Path.Combine(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs"));
        string catalog = File.ReadAllText(Path.Combine(projectRoot, "src", "Salryn.Infrastructure", "Data", "SalrynDatabaseMigrationCatalog.cs"));

        RequireContains(parser, "SALRYN_OFFICIAL_DOCUMENT_SNAPSHOT_V1", "Parser must require V1 schema.");
        RequireContains(parser, "HashMismatch", "Parser must verify hash.");
        RequireContains(builder, "TaxBucketsRequired", "Builder must require tax buckets for production-capable.");
        RequireContains(builder, "NonVatCannotCarryVatAmount", "Non-VAT must fail closed on VAT amount.");
        RequireContains(html, "WebUtility.HtmlEncode", "HTML must escape text.");
        RequireContains(html, "InternalWatermark", "HTML must render internal watermark field.");
        RequireContains(thermal, "InternalWatermark", "Thermal must render internal watermark field.");
        string constants = File.ReadAllText(Path.Combine(projectRoot, "src", "Salryn.Core", "Services", "InvoiceOutput", "InvoiceOutputContracts.cs"));
        RequireContains(constants, "INTERNAL TEST — NOT AN OFFICIAL INVOICE", "Watermark constant required.");
        RequireContains(constants, "THIS DOCUMENT IS NOT VALID FOR CLAIM OF INPUT TAX.", "Non-VAT disclaimer constant required.");
        RequireContains(service, "LiveOfficialPostingAllowed: false", "Manifest must keep live posting false.");
        RequireContains(sqlite, "bir_electronic_journal_entries", "Read must use EJ table.");
        RequireContains(sqlite, "snapshot_payload_json", "Read must use payload.");
        RequireFalse(sqlite.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase), "Snapshot read must not UPDATE.");
        RequireFalse(sqlite.Contains("INSERT ", StringComparison.OrdinalIgnoreCase), "Snapshot read must not INSERT.");
        RequireContains(endpoints, "/api/official/invoice-output/generate", "API route required.");
        RequireContains(endpoints, "RequireLoopbackOperatorRequest", "Host-local only.");
        RequireContains(factory, "MapLocalOfficialInvoiceOutputEndpoints", "Factory must map invoice output.");
        RequireContains(setupPage, "INTERNAL TEST — NOT AN OFFICIAL INVOICE", "UI must show internal marker.");
        RequireContains(setupPage, "generateOfficialInvoiceOutput", "UI must call invoice output.");
        RequireContains(api, "/api/official/invoice-output/generate", "Web API client required.");
        RequireContains(coreSmoke, "rmo-bir-trad-invoice-output", "CoreSmoke gate required.");
        RequireContains(coreSmoke, "RmoBirTradM3InvoiceOutputChecks.Run()", "CoreSmoke must run M3 checks.");
        RequireContains(catalog, "SupportedMigrationVersion = 26", "Catalog version must include the current additive migrations.");
        RequireFalse(html.Contains("is accredited", StringComparison.OrdinalIgnoreCase), "No accreditation claim.");
        RequireFalse(setupPage.Contains("BIR compliant", StringComparison.OrdinalIgnoreCase), "UI no compliance claim.");
    }

    private static void AssertVariantsAndDeterminism()
    {
        InvoiceOutputService service = new(new ThrowingSnapshotRead());

        var cases = new (string Name, OfficialDocumentTaxBucketDraftTaxpayerMode Mode, OfficialDocumentLineTaxClassification[] Lines, InvoiceDocumentVariant Expected)[]
        {
            ("vatable", OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered, new[] { OfficialDocumentLineTaxClassification.Vatable }, InvoiceDocumentVariant.VatAllVatable),
            ("vat-exempt", OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered, new[] { OfficialDocumentLineTaxClassification.VatExempt }, InvoiceDocumentVariant.VatExemptOnly),
            ("zero", OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered, new[] { OfficialDocumentLineTaxClassification.ZeroRated }, InvoiceDocumentVariant.VatZeroRatedOnly),
            ("vat-mixed", OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered, new[]
            {
                OfficialDocumentLineTaxClassification.Vatable,
                OfficialDocumentLineTaxClassification.VatExempt,
                OfficialDocumentLineTaxClassification.ZeroRated
            }, InvoiceDocumentVariant.VatMixed),
            ("sspt", OfficialDocumentTaxBucketDraftTaxpayerMode.NonVatPercentageTax, new[] { OfficialDocumentLineTaxClassification.NonVat }, InvoiceDocumentVariant.NonVatSsptOnly),
            ("nv-exempt", OfficialDocumentTaxBucketDraftTaxpayerMode.NonVatExempt, new[] { OfficialDocumentLineTaxClassification.NonVatExempt }, InvoiceDocumentVariant.NonVatExemptOnly),
            ("nv-mixed", OfficialDocumentTaxBucketDraftTaxpayerMode.NonVatPercentageTax, new[]
            {
                OfficialDocumentLineTaxClassification.NonVat,
                OfficialDocumentLineTaxClassification.NonVatExempt
            }, InvoiceDocumentVariant.NonVatMixed),
        };

        foreach (var testCase in cases)
        {
            OfficialInvoiceSnapshotIdentity identity = BuildIdentity(testCase.Mode, testCase.Lines, "INV-M3-" + testCase.Name.ToUpperInvariant());
            Result<InvoiceOutputPackage> first = service.GenerateFromIdentity(
                identity,
                InvoiceOutputMode.InternalDemo,
                new[] { InvoiceOutputFormat.Html, InvoiceOutputFormat.ThermalText });
            RequireTrue(first.IsSuccess, testCase.Name + " must render: " + (first.IsFailure ? first.ErrorDetail.Message : ""));
            RequireTrue(first.Value.Model.Variant == testCase.Expected, testCase.Name + " variant expected " + testCase.Expected + " got " + first.Value.Model.Variant);
            RequireTrue(first.Value.Manifest.InternalWatermarkApplied, testCase.Name + " must watermark internal demo.");
            RequireTrue(first.Value.Manifest.ComplianceClaimed == false, "complianceClaimed must be false.");
            RequireTrue(first.Value.Manifest.LiveOfficialPostingAllowed == false, "liveOfficialPostingAllowed must be false.");
            RequireContains(first.Value.Artifacts.First(a => a.Format == InvoiceOutputFormat.Html).Utf8Text, "INVOICE", "HTML must say INVOICE.");
            RequireContains(first.Value.Artifacts.First(a => a.Format == InvoiceOutputFormat.Html).Utf8Text, "INTERNAL TEST — NOT AN OFFICIAL INVOICE", "HTML watermark.");

            if (testCase.Mode != OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered)
            {
                RequireContains(
                    first.Value.Artifacts.First(a => a.Format == InvoiceOutputFormat.Html).Utf8Text,
                    "THIS DOCUMENT IS NOT VALID FOR CLAIM OF INPUT TAX.",
                    testCase.Name + " non-VAT disclaimer required.");
                RequireContains(
                    first.Value.Artifacts.First(a => a.Format == InvoiceOutputFormat.Html).Utf8Text,
                    "NON-VAT REG TIN",
                    testCase.Name + " NON-VAT REG phrase required.");
                RequireFalse(
                    first.Value.Model.RegistrationPhrase.Equals("VAT REG TIN", StringComparison.Ordinal),
                    testCase.Name + " registration phrase must not be VAT REG TIN.");
            }
            else
            {
                RequireContains(
                    first.Value.Artifacts.First(a => a.Format == InvoiceOutputFormat.Html).Utf8Text,
                    "VAT REG TIN",
                    testCase.Name + " VAT REG phrase required.");
                RequireFalse(
                    first.Value.Artifacts.First(a => a.Format == InvoiceOutputFormat.Html).Utf8Text.Contains("NON-VAT REG TIN", StringComparison.Ordinal),
                    testCase.Name + " must not show NON-VAT REG.");
            }

            Result<InvoiceOutputPackage> second = service.GenerateFromIdentity(
                identity,
                InvoiceOutputMode.InternalDemo,
                new[] { InvoiceOutputFormat.Html, InvoiceOutputFormat.ThermalText });
            RequireTrue(second.IsSuccess, testCase.Name + " retry must succeed.");
            RequireTrue(
                first.Value.Manifest.CanonicalContentSha256 == second.Value.Manifest.CanonicalContentSha256,
                testCase.Name + " retry must be byte-deterministic for HTML hash.");
            RequireTrue(
                first.Value.Artifacts.First(a => a.Format == InvoiceOutputFormat.ThermalText).ContentSha256
                == second.Value.Artifacts.First(a => a.Format == InvoiceOutputFormat.ThermalText).ContentSha256,
                testCase.Name + " thermal retry must be deterministic.");
            RequireTrue(
                first.Value.Model.OfficialDocumentNumber == second.Value.Model.OfficialDocumentNumber,
                "Retry must keep same official number.");
        }
    }

    private static void AssertFailClosedRules()
    {
        InvoiceOutputService service = new(new ThrowingSnapshotRead());

        // Missing tax buckets production-capable
        OfficialInvoiceSnapshotIdentity noBuckets = BuildIdentityWithoutBuckets();
        Result<InvoiceOutputPackage> prodFail = service.GenerateFromIdentity(
            noBuckets,
            InvoiceOutputMode.ProductionCapable,
            new[] { InvoiceOutputFormat.Html });
        RequireTrue(prodFail.IsFailure, "Production-capable without tax buckets must fail.");
        RequireTrue(prodFail.ErrorDetail.Code == "InvoiceRender.TaxBucketsRequired", "Expected TaxBucketsRequired.");

        // Hash mismatch
        OfficialInvoiceSnapshotIdentity good = BuildIdentity(
            OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
            new[] { OfficialDocumentLineTaxClassification.Vatable },
            "INV-HASH");
        OfficialInvoiceSnapshotIdentity badHash = good with { SnapshotPayloadHash = "00" + good.SnapshotPayloadHash[2..] };
        Result<InvoiceOutputPackage> hashFail = service.GenerateFromIdentity(
            badHash,
            InvoiceOutputMode.InternalDemo,
            new[] { InvoiceOutputFormat.Html });
        RequireTrue(hashFail.IsFailure, "Hash mismatch must fail.");
        RequireTrue(hashFail.ErrorDetail.Code == "InvoiceSnapshot.HashMismatch", "Expected HashMismatch.");

        // HTML escape
        OfficialInvoiceSnapshotIdentity xss = BuildIdentity(
            OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
            new[] { OfficialDocumentLineTaxClassification.Vatable },
            "INV-XSS",
            sellerName: "<script>alert(1)</script>");
        Result<InvoiceOutputPackage> escaped = service.GenerateFromIdentity(
            xss,
            InvoiceOutputMode.InternalDemo,
            new[] { InvoiceOutputFormat.Html });
        RequireTrue(escaped.IsSuccess, "XSS seller name must still render.");
        string html = escaped.Value.Artifacts.First(a => a.Format == InvoiceOutputFormat.Html).Utf8Text;
        RequireFalse(html.Contains("<script>alert(1)</script>", StringComparison.Ordinal), "Raw script must not appear.");
        RequireContains(html, "&lt;script&gt;", "HTML must encode angle brackets.");
    }

    private static void AssertNonMutationAndActivationOff(string projectRoot)
    {
        RequireFalse(ProductionOfficialPostingActivation.IsEnabled, "Activation must remain OFF.");
        string activation = File.ReadAllText(Path.Combine(projectRoot, "src", "Salryn.Core", "Services", "ProductionOfficialPostingActivation.cs"));
        RequireContains(activation, "false", "Activation default OFF must remain.");
        string service = File.ReadAllText(Path.Combine(projectRoot, "src", "Salryn.Core", "Services", "InvoiceOutput", "InvoiceOutputService.cs"));
        RequireFalse(service.Contains("AllocateNext", StringComparison.Ordinal), "Output must not allocate.");
        RequireFalse(service.Contains("CompleteOfficialSale", StringComparison.Ordinal), "Output must not post.");
    }

    private static OfficialInvoiceSnapshotIdentity BuildIdentity(
        OfficialDocumentTaxBucketDraftTaxpayerMode mode,
        OfficialDocumentLineTaxClassification[] classifications,
        string documentNumber,
        string sellerName = "TRAD M3 Taxpayer")
    {
        List<OfficialDocumentSnapshotDraftLine> lines = new();
        List<OfficialDocumentTaxBucketCalculationLine> calcLines = new();
        long total = 0;
        for (int i = 0; i < classifications.Length; i++)
        {
            long lineTotal = 11200 + (i * 1000);
            total += lineTotal;
            lines.Add(new OfficialDocumentSnapshotDraftLine(
                Description: "Item " + (i + 1),
                Barcode: null,
                UnitName: "pc",
                QuantityX1000: 1000,
                UnitPriceCents: lineTotal,
                DiscountCents: 0,
                LineTotalCents: lineTotal));
            calcLines.Add(new OfficialDocumentTaxBucketCalculationLine(lineTotal, 0, classifications[i]));
        }

        OfficialDocumentTaxBucketEngine engine = new();
        Result<OfficialDocumentTaxBucketDraft> buckets = engine.Calculate(
            new OfficialDocumentTaxBucketCalculationRequest(mode, calcLines, 0));
        if (buckets.IsFailure)
        {
            throw new InvalidOperationException("Bucket calc failed: " + buckets.ErrorDetail.Message);
        }

        OfficialDocumentSnapshotDraft draft = new(
            DocumentKind: OfficialDocumentSnapshotDraftKind.OfficialInvoice,
            Header: new OfficialDocumentSnapshotDraftHeader(
                RegisteredTaxpayerName: sellerName,
                BusinessName: "M3 Store",
                TaxpayerIdentificationNumber: "123-456-789-000",
                BranchCode: "00001",
                RegisteredBusinessAddress: "1 Test Street, Cebu",
                TransactionDateTime: new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero),
                DocumentNumber: documentNumber,
                MachineSerialNumber: "SN-M3-001",
                MachineIdentificationNumber: "MIN-M3-001",
                AccreditationNumber: null,
                PermitToUseNumber: null,
                CashierIdentifier: "cashier@test",
                TerminalIdentifier: "POS-1"),
            Lines: lines,
            Amounts: new OfficialDocumentSnapshotDraftAmounts(
                SubtotalCents: total,
                DiscountCents: 0,
                TaxCents: 0,
                TotalCents: total,
                AmountPaidCents: total,
                ChangeCents: 0),
            Payments: new[]
            {
                new OfficialDocumentSnapshotDraftPayment(PaymentMethod.Cash, total, new DateTimeOffset(2026, 7, 10, 10, 1, 0, TimeSpan.Zero))
            },
            TaxMode: new OfficialDocumentSnapshotDraftTaxMode(
                SalrynOfficialTaxBucketPolicy.EngineTaxModeCode,
                VatBucketEngineDeferred: false,
                Notes: SalrynOfficialTaxBucketPolicy.EngineBoundaryNote),
            DeferredComplianceNotes: new[]
            {
                SalrynOfficialTaxBucketPolicy.EngineBoundaryNote,
                SalrynOfficialTaxBucketPolicy.NoLegalVatClaimNote,
                SalrynOfficialTaxBucketPolicy.NoOfficialOutputRenderingNote
            },
            TaxBuckets: buckets.Value);

        Result<ElectronicJournalSnapshotEvidence> evidence = new OfficialDocumentSnapshotEvidenceBuilder().Build(draft);
        if (evidence.IsFailure)
        {
            throw new InvalidOperationException("Evidence build failed: " + evidence.ErrorDetail.Message);
        }

        return new OfficialInvoiceSnapshotIdentity(
            JournalEntryId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            SaleId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            BirMachineProfileId: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            OfficialDocumentNumber: documentNumber,
            DocumentKind: "OFFICIAL_INVOICE",
            AppendedAt: new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero),
            SnapshotPayloadJson: evidence.Value.PayloadJson,
            SnapshotPayloadHash: evidence.Value.PayloadHash,
            SnapshotHashAlgorithm: "SHA-256",
            HashVerified: false);
    }

    private static OfficialInvoiceSnapshotIdentity BuildIdentityWithoutBuckets()
    {
        OfficialDocumentSnapshotDraft draft = new(
            DocumentKind: OfficialDocumentSnapshotDraftKind.OfficialInvoice,
            Header: new OfficialDocumentSnapshotDraftHeader(
                "Taxpayer", null, "111-111-111-000", "00000", "Address",
                new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero),
                "INV-NOBUCKET", "SN", null, null, null, "cashier", "POS"),
            Lines: new[]
            {
                new OfficialDocumentSnapshotDraftLine("Item", null, "pc", 1000, 10000, 0, 10000)
            },
            Amounts: new OfficialDocumentSnapshotDraftAmounts(10000, 0, 0, 10000, 10000, 0),
            Payments: new[]
            {
                new OfficialDocumentSnapshotDraftPayment(PaymentMethod.Cash, 10000, new DateTimeOffset(2026, 7, 10, 10, 1, 0, TimeSpan.Zero))
            },
            TaxMode: new OfficialDocumentSnapshotDraftTaxMode("ENGINE", false, "note"),
            DeferredComplianceNotes: new[] { "note" },
            TaxBuckets: null);

        Result<ElectronicJournalSnapshotEvidence> evidence = new OfficialDocumentSnapshotEvidenceBuilder().Build(draft);
        if (evidence.IsFailure)
        {
            throw new InvalidOperationException(evidence.ErrorDetail.Message);
        }

        return new OfficialInvoiceSnapshotIdentity(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "INV-NOBUCKET", "OFFICIAL_INVOICE",
            DateTimeOffset.UtcNow, evidence.Value.PayloadJson, evidence.Value.PayloadHash, "SHA-256", false);
    }

    private sealed class ThrowingSnapshotRead : IOfficialInvoiceSnapshotReadService
    {
        public Task<Result<OfficialInvoiceSnapshotIdentity>> GetBySaleIdAsync(Guid saleId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Pure tests must not hit SQLite.");

        public Task<Result<OfficialInvoiceSnapshotIdentity>> GetByOfficialDocumentNumberAsync(string officialDocumentNumber, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Pure tests must not hit SQLite.");
    }

    private static string FindProjectRoot(string start)
    {
        DirectoryInfo? current = new(start);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate Salryn.sln");
    }

    private static void RequireTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireFalse(bool condition, string message) => RequireTrue(!condition, message);

    private static void RequireContains(string haystack, string needle, string message)
    {
        if (!haystack.Contains(needle, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message + " Missing: " + needle);
        }
    }
}
