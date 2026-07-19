using System.Globalization;
using System.Text;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.ValueObjects;

namespace Salryn.Core.Services;

public sealed record PosReceiptSnapshotDraft(
    string ReceiptNumber,
    DateTimeOffset SaleDate,
    Guid CashierUserId,
    Money Subtotal,
    Money Discount,
    Money Tax,
    Money Total,
    Money AmountPaid,
    Money Change,
    SaleStatus Status,
    IReadOnlyList<SaleItem> Items,
    IReadOnlyList<Payment> Payments,
    string? StoreName = null,
    string? BusinessName = null,
    string? Address = null,
    string? ContactNumber = null,
    string? ReceiptHeader = null,
    string? ReceiptFooter = null);

public static class PosReceiptTextRenderer
{
    private const int ReceiptWidth = 42;

    public static string Render(
        Sale sale,
        IReadOnlyList<SaleItem> items,
        IReadOnlyList<Payment> payments)
    {
        ArgumentNullException.ThrowIfNull(sale);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(payments);

        return Render(new PosReceiptSnapshotDraft(
            sale.ReceiptNumber,
            sale.SaleDate,
            sale.CashierUserId,
            sale.Subtotal,
            sale.Discount,
            sale.Tax,
            sale.Total,
            sale.AmountPaid,
            sale.Change,
            sale.Status,
            items,
            payments));
    }

    public static string Render(PosReceiptSnapshotDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(draft.Items);
        ArgumentNullException.ThrowIfNull(draft.Payments);

        var builder = new StringBuilder();
        AppendCentered(builder, string.IsNullOrWhiteSpace(draft.StoreName) ? "SALRYN POS" : draft.StoreName);
        if (!string.IsNullOrWhiteSpace(draft.BusinessName)) AppendCentered(builder, draft.BusinessName);
        if (!string.IsNullOrWhiteSpace(draft.Address)) AppendCentered(builder, draft.Address);
        if (!string.IsNullOrWhiteSpace(draft.ContactNumber)) AppendCentered(builder, draft.ContactNumber);
        if (!string.IsNullOrWhiteSpace(draft.ReceiptHeader)) AppendCentered(builder, draft.ReceiptHeader);
        AppendCentered(builder, "RECEIPT SNAPSHOT");
        builder.AppendLine(new string('-', ReceiptWidth));
        builder.AppendLine("Receipt : " + Safe(draft.ReceiptNumber));
        builder.AppendLine("Date UTC: " + draft.SaleDate.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        builder.AppendLine("Cashier : " + draft.CashierUserId.ToString("D"));
        builder.AppendLine("Status  : " + draft.Status);
        builder.AppendLine(new string('-', ReceiptWidth));
        builder.AppendLine("ITEMS");

        if (draft.Items.Count == 0)
        {
            builder.AppendLine("  (no items)");
        }
        else
        {
            foreach (SaleItem item in draft.Items)
            {
                builder.AppendLine(Safe(item.ProductNameSnapshot));

                if (!string.IsNullOrWhiteSpace(item.BarcodeSnapshot))
                {
                    builder.AppendLine("  Barcode: " + Safe(item.BarcodeSnapshot));
                }

                string quantity = FormatQuantity(item.Quantity);
                string unitPrice = FormatMoney(item.UnitPrice);
                string lineTotal = FormatMoney(item.LineTotal);
                builder.AppendLine($"  {quantity} {Safe(item.UnitNameSnapshot)} x {unitPrice}");

                if (item.Discount.Cents > 0)
                {
                    builder.AppendLine("  Line discount: " + FormatMoney(item.Discount));
                }

                builder.AppendLine("  Line total:    " + lineTotal);
            }
        }

        builder.AppendLine(new string('-', ReceiptWidth));
        builder.AppendLine("Subtotal: " + FormatMoney(draft.Subtotal));

        if (draft.Discount.Cents > 0)
        {
            builder.AppendLine("Discount: " + FormatMoney(draft.Discount));
        }

        if (draft.Tax.Cents > 0)
        {
            builder.AppendLine("Tax     : " + FormatMoney(draft.Tax));
        }

        builder.AppendLine("TOTAL   : " + FormatMoney(draft.Total));
        builder.AppendLine("Paid    : " + FormatMoney(draft.AmountPaid));
        builder.AppendLine("Change  : " + FormatMoney(draft.Change));
        builder.AppendLine(new string('-', ReceiptWidth));
        builder.AppendLine("PAYMENTS");

        foreach (Payment payment in draft.Payments)
        {
            builder.AppendLine($"  {payment.Method}: {FormatMoney(payment.Amount)} @ {payment.PaidAt.ToUniversalTime():yyyy-MM-dd HH:mm:ss}");
        }

        builder.AppendLine(new string('-', ReceiptWidth));
        builder.AppendLine("Snapshot source: persisted sale_items and payments.");
        builder.AppendLine("This is not a physical printer output.");
        if (!string.IsNullOrWhiteSpace(draft.ReceiptFooter)) AppendCentered(builder, draft.ReceiptFooter);
        return builder.ToString();
    }

    public static string Render(SaleDetailReadModel sale)
    {
        ArgumentNullException.ThrowIfNull(sale);

        IReadOnlyList<SaleItem> items = sale.Items.Select(item => new SaleItem
        {
            Id = item.Id,
            SaleId = sale.Id,
            ProductId = item.ProductId,
            ProductNameSnapshot = item.ProductNameSnapshot,
            BarcodeSnapshot = item.BarcodeSnapshot,
            UnitNameSnapshot = item.UnitNameSnapshot,
            Quantity = item.Quantity,
            UnitPrice = item.UnitPrice,
            CostPrice = item.CostPrice,
            Discount = item.Discount,
            LineTotal = item.LineTotal
        }).ToList();

        IReadOnlyList<Payment> payments = sale.Payments.Select(payment => new Payment
        {
            Id = payment.Id,
            SaleId = sale.Id,
            Method = payment.Method,
            Amount = payment.Amount,
            PaidAt = payment.PaidAt
        }).ToList();

        return Render(new PosReceiptSnapshotDraft(
            sale.ReceiptNo,
            sale.SaleDate,
            Guid.Empty,
            sale.Subtotal,
            sale.Discount,
            sale.Tax,
            sale.Total,
            sale.AmountPaid,
            sale.Change,
            sale.Status,
            items,
            payments));
    }

    public static string RenderClientReceipt(SaleDetailReadModel sale)
    {
        ArgumentNullException.ThrowIfNull(sale);

        var builder = new StringBuilder();
        AppendCentered(builder, "SALES RECEIPT");
        builder.AppendLine(new string('-', ReceiptWidth));
        builder.AppendLine("Receipt No: " + Safe(sale.ReceiptNo));
        builder.AppendLine("Date      : " + sale.SaleDate.ToLocalTime().ToString("yyyy-MM-dd hh:mm tt", CultureInfo.InvariantCulture));
        builder.AppendLine("Cashier   : " + Safe(sale.CashierDisplayName));
        builder.AppendLine(new string('-', ReceiptWidth));
        builder.AppendLine("ITEMS");

        foreach (SaleItemDetailReadModel item in sale.Items)
        {
            builder.AppendLine(Safe(item.ProductNameSnapshot));
            builder.AppendLine($"  {FormatQuantity(item.Quantity)} {Safe(item.UnitNameSnapshot)} x {FormatMoney(item.UnitPrice)}");
            builder.AppendLine("  Line total: " + FormatMoney(item.LineTotal));
        }

        builder.AppendLine(new string('-', ReceiptWidth));
        builder.AppendLine("Subtotal: " + FormatMoney(sale.Subtotal));
        if (sale.Discount.Cents > 0) builder.AppendLine("Discount: " + FormatMoney(sale.Discount));
        if (sale.Tax.Cents > 0) builder.AppendLine("Tax     : " + FormatMoney(sale.Tax));
        builder.AppendLine("TOTAL   : " + FormatMoney(sale.Total));
        builder.AppendLine("Cash    : " + FormatMoney(sale.AmountPaid));
        builder.AppendLine("Change  : " + FormatMoney(sale.Change));
        builder.AppendLine("Payment : " + (sale.Payments.Count == 0
            ? "Not recorded"
            : string.Join(", ", sale.Payments.Select(payment => payment.Method.ToString()))));
        builder.AppendLine(new string('-', ReceiptWidth));
        AppendCentered(builder, "Thank you!");
        return builder.ToString();
    }

    private static void AppendCentered(StringBuilder builder, string value)
    {
        string safe = Safe(value);
        int padding = Math.Max(0, (ReceiptWidth - safe.Length) / 2);
        builder.AppendLine(new string(' ', padding) + safe);
    }

    private static string FormatMoney(Money money)
    {
        return "PHP " + money.ToPesos().ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static string FormatQuantity(Quantity quantity)
    {
        return quantity.ToUnits().ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string Safe(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
    }
}
