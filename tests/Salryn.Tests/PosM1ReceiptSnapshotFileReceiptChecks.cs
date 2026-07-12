using Salryn.Api.Local.Services;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;

namespace Salryn.Tests;

public static class PosM1ReceiptSnapshotFileReceiptChecks
{
    public static void Run()
    {
        Sale sale = CreateSale("POS-M1-000001");
        IReadOnlyList<SaleItem> items = CreateItems(sale.Id);
        IReadOnlyList<Payment> payments = CreatePayments(sale.Id);

        string snapshot = PosReceiptTextRenderer.Render(sale, items, payments);
        Require(snapshot.Contains("Snapshot Name", StringComparison.Ordinal), "Receipt text must contain sale item snapshot name.");
        Require(!snapshot.Contains("Edited Live Product Name", StringComparison.Ordinal), "Receipt text must not depend on later live product names.");
        Require(snapshot.Contains("PHP 120.00", StringComparison.Ordinal), "Receipt text must contain readable money values.");
        Require(snapshot.Contains("This is not a physical printer output.", StringComparison.Ordinal), "Receipt text must not claim physical printer output.");

        string tempDirectory = Path.Combine(Path.GetTempPath(), "salryn-pos-m1-receipts-" + Guid.NewGuid().ToString("N"));
        try
        {
            SaleDetailReadModel detail = CreateSaleDetail(sale, items, payments, snapshot);
            FileReceiptWriter writer = new(tempDirectory);
            FileReceiptWriteResult written = writer.WriteAsync(detail).GetAwaiter().GetResult();
            Require(written.Succeeded, "File receipt writer must report success for writable path.");
            Require(string.Equals(written.Status, "Written", StringComparison.Ordinal), "File receipt writer must return Written status.");
            Require(written.FilePath is not null && File.Exists(written.FilePath), "File receipt must exist on disk.");
            Require(File.ReadAllText(written.FilePath).Contains("Snapshot Name", StringComparison.Ordinal), "File receipt must contain snapshot text.");
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }

        string blockedDirectory = Path.Combine(Path.GetTempPath(), "salryn-pos-m1-blocked-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blockedDirectory, "this file intentionally blocks Directory.CreateDirectory");
        try
        {
            SaleDetailReadModel detail = CreateSaleDetail(sale, items, payments, snapshot);
            FileReceiptWriter writer = new(blockedDirectory);
            FileReceiptWriteResult failed = writer.WriteAsync(detail).GetAwaiter().GetResult();
            Require(!failed.Succeeded, "File receipt writer failure must not throw or report success.");
            Require(string.Equals(failed.Status, "Failed", StringComparison.Ordinal), "File receipt writer must return Failed status for blocked path.");
            Require(!string.IsNullOrWhiteSpace(failed.ErrorMessage), "File receipt writer failure must be diagnosable.");
        }
        finally
        {
            if (File.Exists(blockedDirectory))
            {
                File.Delete(blockedDirectory);
            }
        }
    }

    private static Sale CreateSale(string receiptNo)
    {
        return new Sale
        {
            Id = Guid.NewGuid(),
            ReceiptNumber = receiptNo,
            SaleDate = new DateTimeOffset(2026, 7, 12, 8, 30, 0, TimeSpan.Zero),
            CashierUserId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            CashierSessionId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Subtotal = Money.FromCents(12000),
            Discount = Money.Zero,
            Tax = Money.Zero,
            Total = Money.FromCents(12000),
            AmountPaid = Money.FromCents(15000),
            Change = Money.FromCents(3000),
            Status = SaleStatus.Completed,
            ReceiptSnapshot = null,
            CreatedAt = new DateTimeOffset(2026, 7, 12, 8, 30, 0, TimeSpan.Zero)
        };
    }

    private static IReadOnlyList<SaleItem> CreateItems(Guid saleId)
    {
        return new[]
        {
            new SaleItem
            {
                Id = Guid.NewGuid(),
                SaleId = saleId,
                ProductId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                ProductNameSnapshot = "Snapshot Name",
                BarcodeSnapshot = "480000000001",
                UnitNameSnapshot = "pc",
                Quantity = Quantity.FromX1000(2000).Value,
                UnitPrice = Money.FromCents(6000),
                CostPrice = Money.FromCents(4000),
                Discount = Money.Zero,
                LineTotal = Money.FromCents(12000)
            }
        };
    }

    private static IReadOnlyList<Payment> CreatePayments(Guid saleId)
    {
        return new[]
        {
            new Payment
            {
                Id = Guid.NewGuid(),
                SaleId = saleId,
                Method = PaymentMethod.Cash,
                Amount = Money.FromCents(15000),
                PaidAt = new DateTimeOffset(2026, 7, 12, 8, 31, 0, TimeSpan.Zero)
            }
        };
    }

    private static SaleDetailReadModel CreateSaleDetail(
        Sale sale,
        IReadOnlyList<SaleItem> items,
        IReadOnlyList<Payment> payments,
        string snapshot)
    {
        return new SaleDetailReadModel(
            sale.Id,
            sale.ReceiptNumber,
            sale.SaleDate,
            "POS-M1 Cashier",
            sale.Subtotal,
            sale.Discount,
            sale.Tax,
            sale.Total,
            sale.AmountPaid,
            sale.Change,
            sale.Status,
            sale.VoidReason,
            sale.VoidedAt,
            snapshot,
            items.Select(item => new SaleItemDetailReadModel(
                item.Id,
                item.ProductId,
                item.ProductNameSnapshot,
                item.BarcodeSnapshot,
                item.UnitNameSnapshot,
                item.Quantity,
                item.UnitPrice,
                item.CostPrice,
                item.Discount,
                item.LineTotal)).ToList(),
            payments.Select(payment => new PaymentReadModel(
                payment.Id,
                payment.Method,
                payment.Amount,
                payment.PaidAt)).ToList());
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
