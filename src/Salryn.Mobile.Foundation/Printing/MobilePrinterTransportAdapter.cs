using System.Text;
using Salryn.Core.Services;

namespace Salryn.Mobile.Foundation.Printing;

/// <summary>Bridges mobile ESC/POS IPrinterTransport (Bluetooth/simulated) to the canonical
/// ICafePrintService IReceiptPrinterAdapter contract without claiming unconfirmed print success.</summary>
public sealed class MobilePrinterTransportAdapter(
    IPrinterTransport transport,
    string targetId,
    string adapterName = "MobileEscPosPrinterAdapter",
    bool isEnabled = true) : IReceiptPrinterAdapter
{
    private readonly IPrinterTransport _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    private readonly string _targetId = string.IsNullOrWhiteSpace(targetId)
        ? throw new ArgumentException("Printer target ID is required.", nameof(targetId))
        : targetId.Trim();

    public string AdapterName { get; } = string.IsNullOrWhiteSpace(adapterName) ? "MobileEscPosPrinterAdapter" : adapterName.Trim();
    public bool IsEnabled { get; } = isEnabled;

    public async Task<ReceiptPrinterAdapterResult> PrintAsync(
        string jobName,
        string receiptText,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return ReceiptPrinterAdapterResult.Disabled("Mobile printer transport is disabled or unconfigured.");
        }

        byte[] payload = BuildPlainTextEscPosPayload(receiptText);
        PrinterTransportResult result = await _transport.SendAsync(_targetId, payload, cancellationToken).ConfigureAwait(false);
        if (result.Accepted)
        {
            return ReceiptPrinterAdapterResult.Success(result.Message);
        }

        if (result.OutcomeUnknown)
        {
            return ReceiptPrinterAdapterResult.Failed(
                "Printer transport outcome is unknown. Inspect physical output before reprinting.",
                $"{result.Code}: {result.Message}");
        }

        return ReceiptPrinterAdapterResult.Failed(
            result.Message,
            $"{result.Code}: {result.Message}");
    }

    private static byte[] BuildPlainTextEscPosPayload(string text)
    {
        using MemoryStream ms = new();
        ms.Write(EscPosDocumentBuilder.InitializeCommand);
        byte[] body = Encoding.UTF8.GetBytes((text ?? string.Empty) + "\n\n");
        ms.Write(body, 0, body.Length);
        ms.Write(EscPosDocumentBuilder.PartialCut);
        return ms.ToArray();
    }
}
