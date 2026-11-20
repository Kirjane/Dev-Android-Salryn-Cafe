using System.Text;

namespace Salryn.Mobile.Foundation.Printing;

public sealed record EscPosReceiptLine(
    string ItemName,
    int Quantity,
    long LineTotalCents,
    IReadOnlyList<string>? Modifiers = null);

public sealed record EscPosReceiptDocument(
    string StoreName,
    string ReceiptNumber,
    string OrderType,
    DateTimeOffset OccurredAtLocal,
    string CashierDisplayName,
    IReadOnlyList<EscPosReceiptLine> Lines,
    long SubtotalCents,
    long DiscountCents,
    long TotalCents,
    string TenderMode,
    long CashCents,
    long ArCents,
    int ReprintNumber = 0,
    string? ReprintReason = null);

public sealed record EscPosKitchenTicketDocument(
    string OrderNumber,
    string OrderType,
    DateTimeOffset CreatedAtLocal,
    IReadOnlyList<EscPosReceiptLine> Lines,
    string? Notes = null,
    int ReprintNumber = 0);

/// <summary>
/// Builds deterministic ESC/POS byte streams for 58mm/80mm thermal Receipt and Kitchen stations.
/// </summary>
public static class EscPosDocumentBuilder
{
    public static readonly byte[] InitializeCommand = [0x1B, 0x40]; // ESC @
    public static readonly byte[] AlignCenter = [0x1B, 0x61, 0x01];
    public static readonly byte[] AlignLeft = [0x1B, 0x61, 0x00];
    public static readonly byte[] BoldOn = [0x1B, 0x45, 0x01];
    public static readonly byte[] BoldOff = [0x1B, 0x45, 0x00];
    public static readonly byte[] PartialCut = [0x1D, 0x56, 0x42, 0x00]; // GS V 66 0

    public static byte[] BuildReceipt(EscPosReceiptDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        using MemoryStream ms = new();

        ms.Write(InitializeCommand);
        ms.Write(AlignCenter);
        ms.Write(BoldOn);
        WriteLine(ms, doc.StoreName);
        ms.Write(BoldOff);
        WriteLine(ms, "SALRYN CAFE");

        if (doc.ReprintNumber > 0)
        {
            ms.Write(BoldOn);
            WriteLine(ms, $"*** REPRINT #{doc.ReprintNumber} ***");
            if (!string.IsNullOrWhiteSpace(doc.ReprintReason))
            {
                WriteLine(ms, $"Reason: {doc.ReprintReason}");
            }
            ms.Write(BoldOff);
        }

        ms.Write(AlignLeft);
        WriteLine(ms, "--------------------------------");
        WriteLine(ms, $"Receipt: {doc.ReceiptNumber}");
        WriteLine(ms, $"Type:    {doc.OrderType}");
        WriteLine(ms, $"Cashier: {doc.CashierDisplayName}");
        WriteLine(ms, $"Time:    {doc.OccurredAtLocal:yyyy-MM-dd HH:mm}");
        WriteLine(ms, "--------------------------------");

        foreach (EscPosReceiptLine line in doc.Lines)
        {
            WriteLine(ms, $"{line.Quantity}x {line.ItemName}  PHP {FormatCents(line.LineTotalCents)}");
            if (line.Modifiers is { Count: > 0 })
            {
                foreach (string mod in line.Modifiers)
                {
                    WriteLine(ms, $"   + {mod}");
                }
            }
        }

        WriteLine(ms, "--------------------------------");
        WriteLine(ms, $"Subtotal: PHP {FormatCents(doc.SubtotalCents)}");
        if (doc.DiscountCents > 0)
        {
            WriteLine(ms, $"Discount: -PHP {FormatCents(doc.DiscountCents)}");
        }
        ms.Write(BoldOn);
        WriteLine(ms, $"TOTAL:    PHP {FormatCents(doc.TotalCents)}");
        ms.Write(BoldOff);
        WriteLine(ms, $"Tender:   {doc.TenderMode}");
        if (doc.CashCents > 0)
        {
            WriteLine(ms, $"  Cash:   PHP {FormatCents(doc.CashCents)}");
        }
        if (doc.ArCents > 0)
        {
            WriteLine(ms, $"  AR:     PHP {FormatCents(doc.ArCents)}");
        }
        WriteLine(ms, "--------------------------------");
        WriteLine(ms, "Thank you!\n\n");
        ms.Write(PartialCut);

        return ms.ToArray();
    }

    public static byte[] BuildKitchenTicket(EscPosKitchenTicketDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        using MemoryStream ms = new();

        ms.Write(InitializeCommand);
        ms.Write(AlignCenter);
        ms.Write(BoldOn);
        WriteLine(ms, "=== KITCHEN TICKET ===");
        WriteLine(ms, $"{doc.OrderType.ToUpperInvariant()}  #{doc.OrderNumber}");
        if (doc.ReprintNumber > 0)
        {
            WriteLine(ms, $"*** REPRINT #{doc.ReprintNumber} ***");
        }
        ms.Write(BoldOff);
        ms.Write(AlignLeft);
        WriteLine(ms, $"Time: {doc.CreatedAtLocal:HH:mm:ss}");
        WriteLine(ms, "--------------------------------");

        foreach (EscPosReceiptLine line in doc.Lines)
        {
            ms.Write(BoldOn);
            WriteLine(ms, $"{line.Quantity}x {line.ItemName}");
            ms.Write(BoldOff);
            if (line.Modifiers is { Count: > 0 })
            {
                foreach (string mod in line.Modifiers)
                {
                    WriteLine(ms, $"   * {mod}");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(doc.Notes))
        {
            WriteLine(ms, "--------------------------------");
            WriteLine(ms, $"NOTE: {doc.Notes}");
        }

        WriteLine(ms, "\n\n");
        ms.Write(PartialCut);
        return ms.ToArray();
    }

    private static void WriteLine(MemoryStream ms, string text)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text + "\n");
        ms.Write(bytes, 0, bytes.Length);
    }

    private static string FormatCents(long cents) => (cents / 100m).ToString("0.00");
}
