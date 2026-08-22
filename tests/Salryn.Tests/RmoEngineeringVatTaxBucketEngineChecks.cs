using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;

namespace Salryn.Tests;

public static class RmoEngineeringVatTaxBucketEngineChecks
{
    public static void Run()
    {
        OfficialDocumentTaxBucketEngine engine = new();

        RequireVatable(engine);
        RequireVatExempt(engine);
        RequireZeroRated(engine);
        RequireNonVat(engine);
        RequireSaleDiscountRounding(engine);
        RequireMixedPaymentIndependent(engine);
        RequireSnapshotIncludesBuckets();
    }

    private static void RequireVatable(OfficialDocumentTaxBucketEngine engine)
    {
        Result<OfficialDocumentTaxBucketDraft> result = engine.Calculate(new OfficialDocumentTaxBucketCalculationRequest(
            OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
            new[]
            {
                new OfficialDocumentTaxBucketCalculationLine(11200, 0, OfficialDocumentLineTaxClassification.Vatable)
            },
            0));

        RequireSuccess(result, "Vatable sale should compute.");
        RequireEqual(10000L, BucketSales(result.Value, OfficialDocumentTaxBucketDraftKind.VatableSales), "Vatable sales net.");
        RequireEqual(1200L, BucketTax(result.Value, OfficialDocumentTaxBucketDraftKind.VatAmount), "VAT amount.");
        RequireFalse(result.Value.DeferredComplianceNotes.Any(note => note.Contains("No VAT bucket engine.", StringComparison.OrdinalIgnoreCase)), "Engine output must not claim deferred engine.");
    }

    private static void RequireVatExempt(OfficialDocumentTaxBucketEngine engine)
    {
        Result<OfficialDocumentTaxBucketDraft> result = engine.Calculate(new OfficialDocumentTaxBucketCalculationRequest(
            OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
            new[] { new OfficialDocumentTaxBucketCalculationLine(5000, 0, OfficialDocumentLineTaxClassification.VatExempt) },
            0));

        RequireSuccess(result, "VAT-exempt sale should compute.");
        RequireEqual(5000L, BucketSales(result.Value, OfficialDocumentTaxBucketDraftKind.VatExemptSales), "VAT-exempt sales.");
    }

    private static void RequireZeroRated(OfficialDocumentTaxBucketEngine engine)
    {
        Result<OfficialDocumentTaxBucketDraft> result = engine.Calculate(new OfficialDocumentTaxBucketCalculationRequest(
            OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
            new[] { new OfficialDocumentTaxBucketCalculationLine(3000, 0, OfficialDocumentLineTaxClassification.ZeroRated) },
            0));

        RequireSuccess(result, "Zero-rated sale should compute.");
        RequireEqual(3000L, BucketSales(result.Value, OfficialDocumentTaxBucketDraftKind.ZeroRatedSales), "Zero-rated sales.");
    }

    private static void RequireNonVat(OfficialDocumentTaxBucketEngine engine)
    {
        Result<OfficialDocumentTaxBucketDraft> result = engine.Calculate(new OfficialDocumentTaxBucketCalculationRequest(
            OfficialDocumentTaxBucketDraftTaxpayerMode.NonVatPercentageTax,
            new[] { new OfficialDocumentTaxBucketCalculationLine(2500, 0, OfficialDocumentLineTaxClassification.NonVat) },
            0));

        RequireSuccess(result, "Non-VAT taxpayer sale should compute.");
        RequireEqual(2500L, BucketSales(result.Value, OfficialDocumentTaxBucketDraftKind.SsptSales), "SSPT / non-VAT sales.");
    }

    private static void RequireSaleDiscountRounding(OfficialDocumentTaxBucketEngine engine)
    {
        Result<OfficialDocumentTaxBucketDraft> result = engine.Calculate(new OfficialDocumentTaxBucketCalculationRequest(
            OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
            new[] { new OfficialDocumentTaxBucketCalculationLine(11200, 0, OfficialDocumentLineTaxClassification.Vatable) },
            1200));

        RequireSuccess(result, "Sale discount edge case should compute.");
        RequireEqual(8929L, BucketSales(result.Value, OfficialDocumentTaxBucketDraftKind.VatableSales), "Discounted vatable sales.");
        RequireEqual(1071L, BucketTax(result.Value, OfficialDocumentTaxBucketDraftKind.VatAmount), "Discounted VAT amount.");
    }

    private static void RequireMixedPaymentIndependent(OfficialDocumentTaxBucketEngine engine)
    {
        Result<OfficialDocumentTaxBucketDraft> cash = engine.Calculate(new OfficialDocumentTaxBucketCalculationRequest(
            OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
            new[] { new OfficialDocumentTaxBucketCalculationLine(11200, 0, OfficialDocumentLineTaxClassification.Vatable) },
            0));

        Result<OfficialDocumentTaxBucketDraft> card = engine.Calculate(new OfficialDocumentTaxBucketCalculationRequest(
            OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
            new[] { new OfficialDocumentTaxBucketCalculationLine(11200, 0, OfficialDocumentLineTaxClassification.Vatable) },
            0));

        RequireEqual(
            BucketTax(cash.Value, OfficialDocumentTaxBucketDraftKind.VatAmount),
            BucketTax(card.Value, OfficialDocumentTaxBucketDraftKind.VatAmount),
            "Payment method must not change VAT bucket result.");
    }

    private static void RequireSnapshotIncludesBuckets()
    {
        OfficialDocumentSnapshotDraftBuilder builder = new();
        Sale sale = BuildSale(11200, 0);
        SaleItem item = BuildItem(sale.Id, 11200);

        Result<OfficialDocumentSnapshotDraft> result = builder.Build(new OfficialDocumentSnapshotDraftBuildRequest(
            OfficialDocumentSnapshotDraftKind.OfficialReceipt,
            BuildProfile(),
            sale,
            new[] { item },
            new[] { BuildPayment(sale.Id, 11200) },
            "OR-2026-000001",
            "cashier-1",
            "POS-1",
            SalrynOfficialTaxBucketPolicy.EngineTaxModeCode,
            SalrynOfficialTaxBucketPolicy.EngineBoundaryNote,
            new[] { OfficialDocumentLineTaxClassification.Vatable },
            OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered));

        RequireSuccess(result, "Snapshot with VAT engine should build.");
        RequireFalse(result.Value.TaxMode.VatBucketEngineDeferred, "Official checkout snapshot must not defer VAT engine.");
        RequireNotNull(result.Value.TaxBuckets, "Snapshot must include tax buckets.");

        Result<ElectronicJournalSnapshotEvidence> evidence =
            new OfficialDocumentSnapshotEvidenceBuilder().Build(result.Value);

        RequireSuccess(evidence, "Snapshot evidence should build.");
        RequireContains(evidence.Value.PayloadJson, "\"taxBuckets\"", "Journal evidence must include tax buckets.");
        RequireContains(evidence.Value.PayloadJson, "\"VatAmount\"", "Journal evidence must include VAT bucket kind.");
    }

    private static long BucketSales(OfficialDocumentTaxBucketDraft draft, OfficialDocumentTaxBucketDraftKind kind)
    {
        return draft.Buckets.First(bucket => bucket.Kind == kind).SalesAmountCents;
    }

    private static long BucketTax(OfficialDocumentTaxBucketDraft draft, OfficialDocumentTaxBucketDraftKind kind)
    {
        return draft.Buckets.First(bucket => bucket.Kind == kind).TaxAmountCents;
    }

    private static Sale BuildSale(long totalCents, long discountCents) => new()
    {
        Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        ReceiptNumber = "PENDING",
        SaleDate = DateTimeOffset.UtcNow,
        CashierUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        CashierSessionId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
        Subtotal = Money.FromCents(totalCents + discountCents),
        Discount = Money.FromCents(discountCents),
        Tax = Money.FromCents(0),
        Total = Money.FromCents(totalCents),
        AmountPaid = Money.FromCents(totalCents),
        Change = Money.FromCents(0),
        Status = SaleStatus.Completed,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static SaleItem BuildItem(Guid saleId, long lineTotalCents) => new()
    {
        Id = Guid.NewGuid(),
        SaleId = saleId,
        ProductId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
        ProductNameSnapshot = "VAT Test Product",
        UnitNameSnapshot = "pc",
        Quantity = Quantity.FromX1000(1000).Value,
        UnitPrice = Money.FromCents(lineTotalCents),
        LineTotal = Money.FromCents(lineTotalCents)
    };

    private static Payment BuildPayment(Guid saleId, long amountCents) => new()
    {
        Id = Guid.NewGuid(),
        SaleId = saleId,
        Method = PaymentMethod.Cash,
        Amount = Money.FromCents(amountCents),
        PaidAt = DateTimeOffset.UtcNow
    };

    private static OfficialDocumentSnapshotDraftMachineProfileInput BuildProfile() => new(
        "Test Taxpayer",
        "Test Store",
        "000-000-000-000",
        "00000",
        "Test Address",
        "SERIAL",
        "MIN",
        "ACC",
        "PTU");

    private static void RequireSuccess<T>(Result<T> result, string message)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(message + " " + result.Error);
        }
    }

    private static void RequireEqual(long expected, long actual, string message)
    {
        if (expected != actual)
        {
            throw new InvalidOperationException(message + $" Expected {expected}, got {actual}.");
        }
    }

    private static void RequireFalse(bool value, string message)
    {
        if (value)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireNotNull(object? value, string message)
    {
        if (value is null)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireContains(string haystack, string needle, string message)
    {
        if (!haystack.Contains(needle, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}
