using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Core.Services.InvoiceOutput;

namespace Salryn.Tests;

/// <summary>
/// TRAD-M3-RC1: download/print delivery, server-owned mode, optional buyer snapshot fields.
/// Not accreditation. Not legal issuance.
/// </summary>
public static class RmoBirTradM3Rc1InvoiceOutputDeliveryChecks
{
    public static void Run()
    {
        string root = FindProjectRoot(Directory.GetCurrentDirectory());
        AssertDocs(root);
        AssertSourceInvariants(root);
        AssertServerOwnedModeInSource(root);
        AssertBuyerSnapshotCapability();
        AssertV1WithoutBuyerStillRenders();
        Console.WriteLine("[TRAD-M3-RC1] Invoice output delivery checks passed.");
    }

    private static void AssertDocs(string root)
    {
        string handoff = Path.Combine(root, "00-HANDOFF");
        RequireTrue(File.Exists(Path.Combine(handoff, "RMO_BIR_TRAD_M3_RC1_INVOICE_OUTPUT_DELIVERY_STUDY.md")), "RC1 study required.");
        RequireTrue(File.Exists(Path.Combine(handoff, "RMO_BIR_TRAD_M3_RC1_GO_STOP_DECISION.md")), "RC1 GO/STOP required.");
        RequireTrue(File.Exists(Path.Combine(handoff, "RMO_BIR_TRAD_M3_MANUAL_OUTPUT_ACCEPTANCE.md")), "Manual acceptance checklist required.");
    }

    private static void AssertSourceInvariants(string root)
    {
        string endpoints = File.ReadAllText(Path.Combine(root, "src", "Salryn.Api.Local", "LocalOfficialInvoiceOutputEndpoints.cs"));
        string setupPage = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "OfficialSetupPage.tsx"));
        string api = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "api", "officialSetup.ts"));
        string draft = File.ReadAllText(Path.Combine(root, "src", "Salryn.Core", "Models", "OfficialDocumentSnapshotDraft.cs"));
        string evidence = File.ReadAllText(Path.Combine(root, "src", "Salryn.Core", "Services", "OfficialDocumentSnapshotEvidenceBuilder.cs"));
        string coreSmoke = File.ReadAllText(Path.Combine(root, "tools", "Salryn.Tools.CoreSmoke", "Program.cs"));
        string catalog = File.ReadAllText(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "SalrynDatabaseMigrationCatalog.cs"));

        RequireContains(endpoints, "/api/official/invoice-output/html", "HTML download route required.");
        RequireContains(endpoints, "/api/official/invoice-output/thermal", "Thermal download route required.");
        RequireContains(endpoints, "/api/official/invoice-output/print-view", "Print-view route required.");
        RequireContains(endpoints, "ClientModeNotAllowed", "Client ProductionCapable must be rejected.");
        RequireContains(endpoints, "InvoiceOutputMode.InternalDemo", "Server-owned InternalDemo required.");
        RequireContains(endpoints, "CacheControl", "no-store cache policy required.");
        RequireContains(endpoints, "no-store", "no-store cache value required.");
        RequireContains(endpoints, "X-Salryn-Content-Sha256", "Content hash header required.");
        RequireContains(endpoints, "Results.File", "Raw file delivery required.");
        RequireContains(setupPage, "Download HTML", "UI download HTML required.");
        RequireContains(setupPage, "Download thermal text", "UI download thermal required.");
        RequireContains(setupPage, "Open print preview", "UI print preview required.");
        RequireContains(setupPage, "print-view", "UI must open print-view.");
        RequireFalse(setupPage.Contains("Official output is still not implemented.", StringComparison.Ordinal),
            "Stale wording must be removed.");
        RequireContains(setupPage, "Production official issuance", "Precise production-off wording required.");
        RequireFalse(api.Contains("ProductionCapable", StringComparison.Ordinal), "Web client must not send ProductionCapable.");
        RequireContains(draft, "OfficialDocumentSnapshotDraftBuyer", "Buyer draft type required.");
        RequireContains(draft, "OfficialDocumentSnapshotDraftStatutory", "Statutory draft type required.");
        RequireContains(evidence, "WriteBuyer", "Evidence must write buyer when present.");
        RequireContains(evidence, "WriteStatutory", "Evidence must write statutory when present.");
        RequireContains(coreSmoke, "rmo-bir-trad-invoice-output-delivery", "CoreSmoke RC1 gate required.");
        RequireContains(coreSmoke, "RmoBirTradM3Rc1InvoiceOutputDeliveryChecks.Run()", "CoreSmoke must run RC1 checks.");
        RequireContains(catalog, "SupportedMigrationVersion = 17", "Catalog version after M4-RC2.");
    }

    private static void AssertServerOwnedModeInSource(string root)
    {
        string endpoints = File.ReadAllText(Path.Combine(root, "src", "Salryn.Api.Local", "LocalOfficialInvoiceOutputEndpoints.cs"));
        RequireContains(endpoints, "ProductionCapable", "Must detect client ProductionCapable string.");
        RequireContains(endpoints, "ClientModeNotAllowed", "Must reject client ProductionCapable.");
        // Ensure generate path does not map client mode to ProductionCapable anymore.
        RequireFalse(
            endpoints.Contains("? InvoiceOutputMode.ProductionCapable", StringComparison.Ordinal),
            "Endpoint must not promote client mode to ProductionCapable.");
    }

    private static void AssertBuyerSnapshotCapability()
    {
        OfficialDocumentSnapshotDraft draft = BuildDraftWithBuyer();
        Result<ElectronicJournalSnapshotEvidence> evidence = new OfficialDocumentSnapshotEvidenceBuilder().Build(draft);
        RequireTrue(evidence.IsSuccess, "Evidence with buyer must build: " + (evidence.IsFailure ? evidence.ErrorDetail.Message : ""));
        RequireContains(evidence.Value.PayloadJson, "\"buyer\"", "Payload must include buyer object.");
        RequireContains(evidence.Value.PayloadJson, "Acme Buyer Corp", "Buyer name must be snapshotted.");

        OfficialInvoiceSnapshotIdentity identity = new(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "INV-BUYER-1", "OFFICIAL_INVOICE",
            DateTimeOffset.UtcNow, evidence.Value.PayloadJson, evidence.Value.PayloadHash, "SHA-256", false);

        InvoiceOutputService service = new(new ThrowingRead());
        Result<InvoiceOutputPackage> package = service.GenerateFromIdentity(
            identity,
            InvoiceOutputMode.InternalDemo,
            new[] { InvoiceOutputFormat.Html, InvoiceOutputFormat.ThermalText });
        RequireTrue(package.IsSuccess, "Buyer package must render: " + (package.IsFailure ? package.ErrorDetail.Message : ""));
        RequireTrue(package.Value.Model.BuyerNameSpace == "Acme Buyer Corp", "Buyer name must reach render model.");
        RequireContains(package.Value.Artifacts.First(a => a.Format == InvoiceOutputFormat.Html).Utf8Text,
            "Acme Buyer Corp", "HTML must show snapshotted buyer name.");
        RequireFalse(package.Value.Manifest.MissingOptionalFields.Contains("BuyerName"),
            "BuyerName must not be listed missing when present.");

        // Deterministic retry
        Result<InvoiceOutputPackage> retry = service.GenerateFromIdentity(
            identity, InvoiceOutputMode.InternalDemo, new[] { InvoiceOutputFormat.Html });
        RequireTrue(retry.IsSuccess, "Retry must succeed.");
        RequireTrue(
            package.Value.Manifest.CanonicalContentSha256 == retry.Value.Manifest.CanonicalContentSha256,
            "Buyer render must be deterministic.");
    }

    private static void AssertV1WithoutBuyerStillRenders()
    {
        OfficialDocumentSnapshotDraft draft = BuildDraftWithoutBuyer();
        Result<ElectronicJournalSnapshotEvidence> evidence = new OfficialDocumentSnapshotEvidenceBuilder().Build(draft);
        RequireTrue(evidence.IsSuccess, "V1 draft must build.");
        RequireFalse(evidence.Value.PayloadJson.Contains("\"buyer\"", StringComparison.Ordinal),
            "V1 without buyer must omit buyer property for hash stability.");

        OfficialInvoiceSnapshotIdentity identity = new(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "INV-NOBUYER", "OFFICIAL_INVOICE",
            DateTimeOffset.UtcNow, evidence.Value.PayloadJson, evidence.Value.PayloadHash, "SHA-256", false);

        InvoiceOutputService service = new(new ThrowingRead());
        Result<InvoiceOutputPackage> package = service.GenerateFromIdentity(
            identity, InvoiceOutputMode.InternalDemo, new[] { InvoiceOutputFormat.Html });
        RequireTrue(package.IsSuccess, "V1 without buyer must still render demo.");
        RequireTrue(package.Value.Manifest.MissingOptionalFields.Contains("BuyerName"),
            "Missing buyer must be listed for V1 rows.");
        RequireContains(package.Value.Artifacts[0].Utf8Text, "INTERNAL TEST — NOT AN OFFICIAL INVOICE", "Watermark required.");
    }

    private static OfficialDocumentSnapshotDraft BuildDraftWithBuyer()
    {
        OfficialDocumentTaxBucketEngine engine = new();
        Result<OfficialDocumentTaxBucketDraft> buckets = engine.Calculate(new OfficialDocumentTaxBucketCalculationRequest(
            OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
            new[] { new OfficialDocumentTaxBucketCalculationLine(11200, 0, OfficialDocumentLineTaxClassification.Vatable) },
            0));
        RequireTrue(buckets.IsSuccess, buckets.IsFailure ? buckets.ErrorDetail.Message : "");

        return new OfficialDocumentSnapshotDraft(
            OfficialDocumentSnapshotDraftKind.OfficialInvoice,
            new OfficialDocumentSnapshotDraftHeader(
                "Seller", "Store", "123-456-789-000", "00001", "Addr",
                new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero),
                "INV-BUYER-1", "SN", "MIN", null, null, "cashier", "POS-1"),
            new[]
            {
                new OfficialDocumentSnapshotDraftLine("Item", null, "pc", 1000, 11200, 0, 11200)
            },
            new OfficialDocumentSnapshotDraftAmounts(11200, 0, 0, 11200, 11200, 0),
            new[]
            {
                new OfficialDocumentSnapshotDraftPayment(PaymentMethod.Cash, 11200, new DateTimeOffset(2026, 7, 10, 12, 1, 0, TimeSpan.Zero))
            },
            new OfficialDocumentSnapshotDraftTaxMode(SalrynOfficialTaxBucketPolicy.EngineTaxModeCode, false, "n"),
            new[]
            {
                SalrynOfficialTaxBucketPolicy.EngineBoundaryNote,
                SalrynOfficialTaxBucketPolicy.NoLegalVatClaimNote,
                SalrynOfficialTaxBucketPolicy.NoOfficialOutputRenderingNote
            },
            buckets.Value,
            Buyer: new OfficialDocumentSnapshotDraftBuyer("Acme Buyer Corp", "999-888-777-000", "Buyer Street 1"),
            Statutory: new OfficialDocumentSnapshotDraftStatutory("SC", "ID-123", null, null));
    }

    private static OfficialDocumentSnapshotDraft BuildDraftWithoutBuyer()
    {
        OfficialDocumentTaxBucketEngine engine = new();
        Result<OfficialDocumentTaxBucketDraft> buckets = engine.Calculate(new OfficialDocumentTaxBucketCalculationRequest(
            OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
            new[] { new OfficialDocumentTaxBucketCalculationLine(11200, 0, OfficialDocumentLineTaxClassification.Vatable) },
            0));
        RequireTrue(buckets.IsSuccess, buckets.IsFailure ? buckets.ErrorDetail.Message : "");

        return new OfficialDocumentSnapshotDraft(
            OfficialDocumentSnapshotDraftKind.OfficialInvoice,
            new OfficialDocumentSnapshotDraftHeader(
                "Seller", null, "123-456-789-000", "00001", "Addr",
                new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero),
                "INV-NOBUYER", "SN", null, null, null, "cashier", "POS-1"),
            new[]
            {
                new OfficialDocumentSnapshotDraftLine("Item", null, "pc", 1000, 11200, 0, 11200)
            },
            new OfficialDocumentSnapshotDraftAmounts(11200, 0, 0, 11200, 11200, 0),
            new[]
            {
                new OfficialDocumentSnapshotDraftPayment(PaymentMethod.Cash, 11200, new DateTimeOffset(2026, 7, 10, 12, 1, 0, TimeSpan.Zero))
            },
            new OfficialDocumentSnapshotDraftTaxMode(SalrynOfficialTaxBucketPolicy.EngineTaxModeCode, false, "n"),
            new[]
            {
                SalrynOfficialTaxBucketPolicy.EngineBoundaryNote,
                SalrynOfficialTaxBucketPolicy.NoLegalVatClaimNote,
                SalrynOfficialTaxBucketPolicy.NoOfficialOutputRenderingNote
            },
            buckets.Value);
    }

    private sealed class ThrowingRead : IOfficialInvoiceSnapshotReadService
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

        throw new InvalidOperationException("Salryn.sln not found");
    }

    private static void RequireTrue(bool c, string m)
    {
        if (!c)
        {
            throw new InvalidOperationException(m);
        }
    }

    private static void RequireFalse(bool c, string m) => RequireTrue(!c, m);

    private static void RequireContains(string haystack, string needle, string message)
    {
        if (!haystack.Contains(needle, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message + " Missing: " + needle);
        }
    }
}
