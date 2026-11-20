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
    string? ReceiptFooter = null,
    string? CashierDisplayName = null,
    string? CustomerFullName = null,
    string? CustomerPhone = null,
    Money? CashPaid = null,
    Money? UtangAmount = null,
    Money? BalanceDue = null,
    DateOnly? DueDate = null,
    string? ReceivableNote = null);

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

        AppendCentered(builder, "SALES RECEIPT");
        builder.AppendLine(new string('-', ReceiptWidth));
        builder.AppendLine("Receipt No: " + Safe(draft.ReceiptNumber));
        builder.AppendLine("Date      : " + draft.SaleDate.ToLocalTime().ToString("yyyy-MM-dd hh:mm tt", CultureInfo.InvariantCulture));
        builder.AppendLine("Cashier   : " + ResolveCashierText(draft.CashierDisplayName));
        if (draft.Status != SaleStatus.Completed)
        {
            builder.AppendLine("Status    : " + draft.Status);
        }

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
                builder.AppendLine($"  {FormatQuantity(item.Quantity)} {Safe(item.UnitNameSnapshot)} x {FormatMoney(item.UnitPrice)}");

                if (item.Discount.Cents > 0)
                {
                    builder.AppendLine("  Line discount: " + FormatMoney(item.Discount));
                }

                builder.AppendLine("  Line total: " + FormatMoney(item.LineTotal));
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
        Money cashPaid = draft.CashPaid ?? Money.FromCents(
            draft.Payments.Where(payment => payment.Method == PaymentMethod.Cash).Sum(payment => payment.Amount.Cents));
        if (cashPaid.Cents > 0)
        {
            builder.AppendLine("Cash    : " + FormatMoney(cashPaid));
        }
        if (draft.UtangAmount is Money utang && utang.Cents > 0)
        {
            builder.AppendLine("Utang   : " + FormatMoney(utang));
            builder.AppendLine("Balance : " + FormatMoney(draft.BalanceDue ?? utang));
            builder.AppendLine("Customer: " + Safe(draft.CustomerFullName));
            if (!string.IsNullOrWhiteSpace(draft.CustomerPhone)) builder.AppendLine("Phone   : " + Safe(draft.CustomerPhone));
            if (draft.DueDate.HasValue) builder.AppendLine("Due date: " + draft.DueDate.Value.ToString("yyyy-MM-dd"));
            if (!string.IsNullOrWhiteSpace(draft.ReceivableNote)) builder.AppendLine("Note    : " + Safe(draft.ReceivableNote));
        }
        builder.AppendLine("Change  : " + FormatMoney(draft.Change));
        builder.AppendLine("Payment : " + FormatPaymentMethods(draft.Payments));
        builder.AppendLine(new string('-', ReceiptWidth));
        AppendCentered(builder, string.IsNullOrWhiteSpace(draft.ReceiptFooter) ? "Thank you!" : draft.ReceiptFooter);
        return builder.ToString();
    }

    public static string Render(SaleDetailReadModel sale)
    {
        ArgumentNullException.ThrowIfNull(sale);
        return RenderClientReceipt(sale);
    }

    public static string RenderClientReceipt(SaleDetailReadModel sale)
    {
        ArgumentNullException.ThrowIfNull(sale);

        var builder = new StringBuilder();
        AppendCentered(builder, "SALES RECEIPT");
        builder.AppendLine(new string('-', ReceiptWidth));
        builder.AppendLine("Receipt No: " + Safe(sale.ReceiptNo));
        builder.AppendLine("Date      : " + sale.SaleDate.ToLocalTime().ToString("yyyy-MM-dd hh:mm tt", CultureInfo.InvariantCulture));
        builder.AppendLine("Cashier   : " + ResolveCashierText(sale.CashierDisplayName));
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
        long cashPaidCents = sale.Payments.Where(payment => payment.Method == PaymentMethod.Cash).Sum(payment => payment.Amount.Cents);
        if (cashPaidCents > 0)
        {
            builder.AppendLine("Cash    : " + FormatMoney(Money.FromCents(cashPaidCents)));
        }
        long utangCents = sale.Payments.Where(payment => payment.Method == PaymentMethod.AccountsReceivable).Sum(payment => payment.Amount.Cents);
        if (utangCents > 0)
        {
            builder.AppendLine("Utang   : " + FormatMoney(Money.FromCents(utangCents)));
        }
        builder.AppendLine("Change  : " + FormatMoney(sale.Change));
        builder.AppendLine("Payment : " + FormatPaymentMethods(sale.Payments));
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

    private static string FormatPaymentMethods(IReadOnlyList<Payment> payments)
    {
        return payments.Count == 0
            ? "Not recorded"
            : string.Join(", ", payments.Select(payment => payment.Method.ToString()).Distinct(StringComparer.Ordinal));
    }

    private static string FormatPaymentMethods(IReadOnlyList<PaymentReadModel> payments)
    {
        return payments.Count == 0
            ? "Not recorded"
            : string.Join(", ", payments.Select(payment => payment.Method.ToString()).Distinct(StringComparer.Ordinal));
    }

    private static string ResolveCashierText(string? cashierDisplayName)
    {
        if (string.IsNullOrWhiteSpace(cashierDisplayName))
        {
            return "Cashier";
        }

        string trimmed = cashierDisplayName.Trim();
        return Guid.TryParse(trimmed, out _) ? "Cashier" : trimmed;
    }

    private static string Safe(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
    }
}
