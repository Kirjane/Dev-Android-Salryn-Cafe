using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Core.Services.InvoiceOutput;
using Salryn.Core.ValueObjects;

namespace Salryn.Tests;

/// <summary>
/// TRAD-M3-RC2: buyer/statutory capture through draft builder + V2 schema + wording gates.
/// </summary>
public static class RmoBirTradM3Rc2SnapshotCaptureChecks
{
    public static void Run()
    {
        string root = FindRoot();
        AssertSource(root);
        AssertBuilderWiresBuyerAndV2Schema();
        AssertV1WithoutBuyer();
        Console.WriteLine("[TRAD-M3-RC2] Snapshot capture checks passed.");
    }

    private static void AssertSource(string root)
    {
        string builder = File.ReadAllText(Path.Combine(root, "src", "Salryn.Core", "Services", "OfficialDocumentSnapshotDraftBuilder.cs"));
        string persistence = File.ReadAllText(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteOfficialCheckoutPersistenceService.cs"));
        string host = File.ReadAllText(Path.Combine(root, "src", "Salryn.Core", "Services", "HostAtomicOfficialPostingService.cs"));
        string evidence = File.ReadAllText(Path.Combine(root, "src", "Salryn.Core", "Services", "OfficialDocumentSnapshotEvidenceBuilder.cs"));
        string setup = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "OfficialSetupPage.tsx"));
        string coreSmoke = File.ReadAllText(Path.Combine(root, "tools", "Salryn.Tools.CoreSmoke", "Program.cs"));

        RequireContains(builder, "Buyer = null", "Build request must accept Buyer.");
        RequireContains(builder, "Statutory = null", "Build request must accept Statutory.");
        RequireContains(builder, "NormalizeBuyer", "Builder must normalize buyer.");
        RequireContains(persistence, "request.Buyer", "Persistence must pass buyer to snapshot builder.");
        RequireContains(persistence, "request.Statutory", "Persistence must pass statutory to snapshot builder.");
        RequireContains(host, "Buyer = null", "Host atomic request must carry buyer.");
        RequireContains(evidence, "SALRYN_OFFICIAL_DOCUMENT_SNAPSHOT_V2", "Evidence must write V2 when optional parties present.");
        RequireContains(evidence, "SALRYN_OFFICIAL_DOCUMENT_SNAPSHOT_V1", "Evidence must keep V1 when optional parties absent.");
        RequireFalse(setup.Contains("No device output. No official output.", StringComparison.Ordinal),
            "Stale no-output wording must be removed.");
        RequireFalse(setup.Contains("No official output generation.", StringComparison.Ordinal),
            "Stale no-generation wording must be removed.");
        RequireContains(setup, "HTML/thermal invoice projection", "UI must acknowledge internal projection.");
        RequireContains(coreSmoke, "rmo-bir-trad-snapshot-capture-rc2", "RC2 CoreSmoke gate required.");
        RequireContains(coreSmoke, "RmoBirTradM3Rc2SnapshotCaptureChecks.Run()", "RC2 checks must run.");
        // RC1 gate remains canonical name.
        RequireContains(coreSmoke, "rmo-bir-trad-invoice-output-delivery", "RC1 delivery gate name preserved.");
    }

    private static void AssertBuilderWiresBuyerAndV2Schema()
    {
        OfficialDocumentSnapshotDraftBuilder builder = new();
        Sale sale = BuildSale(11200);
        SaleItem item = BuildItem(sale.Id, 11200);
        Payment payment = BuildPayment(sale.Id, 11200);

        Result<OfficialDocumentSnapshotDraft> draft = builder.Build(new OfficialDocumentSnapshotDraftBuildRequest(
            OfficialDocumentSnapshotDraftKind.OfficialInvoice,
            BuildProfile(),
            sale,
            new[] { item },
            new[] { payment },
            "INV-RC2-BUYER",
            "cashier@test",
            "POS-1",
            SalrynOfficialTaxBucketPolicy.EngineTaxModeCode,
            SalrynOfficialTaxBucketPolicy.EngineBoundaryNote,
            new[] { OfficialDocumentLineTaxClassification.Vatable },
            OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
            Buyer: new OfficialDocumentSnapshotDraftBuyer("Buyer Co", "111-222-333-000", "Buyer Addr"),
            Statutory: new OfficialDocumentSnapshotDraftStatutory("PWD", "PWD-9", null, null)));

        RequireTrue(draft.IsSuccess, "Draft with buyer must build: " + (draft.IsFailure ? draft.ErrorDetail.Message : ""));
        RequireTrue(draft.Value.Buyer?.RegisteredName == "Buyer Co", "Buyer must be on draft.");

        Result<ElectronicJournalSnapshotEvidence> evidence = new OfficialDocumentSnapshotEvidenceBuilder().Build(draft.Value);
        RequireTrue(evidence.IsSuccess, "Evidence must succeed.");
        RequireContains(evidence.Value.PayloadJson, "SALRYN_OFFICIAL_DOCUMENT_SNAPSHOT_V2", "V2 schema required when buyer present.");
        RequireContains(evidence.Value.PayloadJson, "Buyer Co", "Buyer name in payload.");

        // Parse + render
        OfficialInvoiceSnapshotIdentity identity = new(
            Guid.NewGuid(), sale.Id, Guid.NewGuid(), "INV-RC2-BUYER", "OFFICIAL_INVOICE",
            DateTimeOffset.UtcNow, evidence.Value.PayloadJson, evidence.Value.PayloadHash, "SHA-256", false);
        InvoiceOutputService service = new(new ThrowingRead());
        Result<InvoiceOutputPackage> package = service.GenerateFromIdentity(
            identity, InvoiceOutputMode.InternalDemo, new[] { InvoiceOutputFormat.Html });
        RequireTrue(package.IsSuccess, "Render V2 must succeed.");
        RequireTrue(package.Value.Model.BuyerNameSpace == "Buyer Co", "Buyer reaches model.");
    }

    private static void AssertV1WithoutBuyer()
    {
        OfficialDocumentSnapshotDraftBuilder builder = new();
        Sale sale = BuildSale(11200);
        Result<OfficialDocumentSnapshotDraft> draft = builder.Build(new OfficialDocumentSnapshotDraftBuildRequest(
            OfficialDocumentSnapshotDraftKind.OfficialInvoice,
            BuildProfile(),
            sale,
            new[] { BuildItem(sale.Id, 11200) },
            new[] { BuildPayment(sale.Id, 11200) },
            "INV-RC2-V1",
            "cashier@test",
            "POS-1",
            SalrynOfficialTaxBucketPolicy.EngineTaxModeCode,
            null,
            new[] { OfficialDocumentLineTaxClassification.Vatable },
            OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered));

        RequireTrue(draft.IsSuccess, "V1 draft must build.");
        Result<ElectronicJournalSnapshotEvidence> evidence = new OfficialDocumentSnapshotEvidenceBuilder().Build(draft.Value);
        RequireTrue(evidence.IsSuccess, "V1 evidence must succeed.");
        RequireContains(evidence.Value.PayloadJson, "SALRYN_OFFICIAL_DOCUMENT_SNAPSHOT_V1", "V1 schema without buyer.");
        RequireFalse(evidence.Value.PayloadJson.Contains("\"buyer\"", StringComparison.Ordinal), "V1 omits buyer property.");
    }

    private static OfficialDocumentSnapshotDraftMachineProfileInput BuildProfile() =>
        new("Seller", "Store", "123-456-789-000", "00001", "Addr", "SN", "MIN", null, null);

    private static Sale BuildSale(long cents) => new()
    {
        Id = Guid.NewGuid(),
        ReceiptNumber = "PENDING",
        SaleDate = new DateTimeOffset(2026, 7, 10, 14, 0, 0, TimeSpan.Zero),
        CashierUserId = Guid.NewGuid(),
        CashierSessionId = Guid.NewGuid(),
        Subtotal = Money.FromCents(cents),
        Discount = Money.FromCents(0),
        Tax = Money.FromCents(0),
        Total = Money.FromCents(cents),
        AmountPaid = Money.FromCents(cents),
        Change = Money.FromCents(0),
        Status = SaleStatus.Completed,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static SaleItem BuildItem(Guid saleId, long cents) => new()
    {
        Id = Guid.NewGuid(),
        SaleId = saleId,
        ProductId = Guid.NewGuid(),
        ProductNameSnapshot = "Item",
        UnitNameSnapshot = "pc",
        Quantity = Quantity.FromX1000(1000).Value,
        UnitPrice = Money.FromCents(cents),
        LineTotal = Money.FromCents(cents)
    };

    private static Payment BuildPayment(Guid saleId, long cents) => new()
    {
        Id = Guid.NewGuid(),
        SaleId = saleId,
        Method = PaymentMethod.Cash,
        Amount = Money.FromCents(cents),
        PaidAt = DateTimeOffset.UtcNow
    };

    private sealed class ThrowingRead : IOfficialInvoiceSnapshotReadService
    {
        public Task<Result<OfficialInvoiceSnapshotIdentity>> GetBySaleIdAsync(Guid saleId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("no sqlite");
        public Task<Result<OfficialInvoiceSnapshotIdentity>> GetByOfficialDocumentNumberAsync(string officialDocumentNumber, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("no sqlite");
    }

    private static string FindRoot()
    {
        DirectoryInfo? d = new(Directory.GetCurrentDirectory());
        while (d is not null)
        {
            if (File.Exists(Path.Combine(d.FullName, "Salryn.sln")))
            {
                return d.FullName;
            }

            d = d.Parent;
        }

        throw new InvalidOperationException("sln missing");
    }

    private static void RequireTrue(bool c, string m)
    {
        if (!c)
        {
            throw new InvalidOperationException(m);
        }
    }

    private static void RequireFalse(bool c, string m) => RequireTrue(!c, m);

    private static void RequireContains(string h, string n, string m)
    {
        if (!h.Contains(n, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(m + " Missing: " + n);
        }
    }
}
