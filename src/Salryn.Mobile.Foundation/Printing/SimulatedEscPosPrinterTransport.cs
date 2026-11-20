using System.Collections.Concurrent;
using System.Text;

namespace Salryn.Mobile.Foundation.Printing;

public sealed record PairedBluetoothPrinterInfo(
    string Address,
    string Name);

/// <summary>
/// Peripheral Bluetooth printer transport boundary — strictly isolated from
/// device-trust enrollment, operator RBAC, and central sync credentials.
/// </summary>
public interface IBluetoothPrinterTransport : IPrinterTransport
{
    IReadOnlyList<PairedBluetoothPrinterInfo> ListPairedPrinters();
}

public sealed record CapturedEscPosPrint(
    string TargetId,
    byte[] Payload,
    string AsciiPreview,
    DateTimeOffset SentAtUtc);

/// <summary>
/// Deterministic simulated ESC/POS printer transport for automated CI/unit/emulator verification.
/// </summary>
public sealed class SimulatedEscPosPrinterTransport : IBluetoothPrinterTransport
{
    private readonly ConcurrentQueue<CapturedEscPosPrint> _captured = new();
    private readonly List<PairedBluetoothPrinterInfo> _pairedPrinters =
    [
        new("AA:BB:CC:11:22:01", "Salryn Receipt BT-58"),
        new("AA:BB:CC:11:22:02", "Salryn Kitchen BT-80")
    ];

    public bool SimulateOfflineFailure { get; set; }

    // Simulates bytes reaching the peripheral before the connection outcome is lost.
    public bool SimulateUnknownDelivery { get; set; }

    public IReadOnlyList<CapturedEscPosPrint> CapturedPrints => _captured.ToArray();

    public IReadOnlyList<PairedBluetoothPrinterInfo> ListPairedPrinters() => _pairedPrinters;

    public Task<PrinterTransportResult> SendAsync(
        string targetId,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(targetId))
        {
            return Task.FromResult(PrinterTransportResult.NotReady("SimPrinter.TargetRequired", "Target printer address is required."));
        }

        if (payload.Length < 6 || payload.Span[0] != 0x1B || payload.Span[1] != 0x40)
        {
            return Task.FromResult(PrinterTransportResult.NotReady("SimPrinter.InvalidEscPosFrame", "Payload must begin with ESC @ (0x1B 0x40)."));
        }

        if (SimulateOfflineFailure)
        {
            return Task.FromResult(PrinterTransportResult.NotReady("SimPrinter.BluetoothDisconnected", "Simulated Bluetooth RFCOMM disconnect."));
        }

        byte[] copy = payload.ToArray();
        string preview = Encoding.ASCII.GetString(copy);
        _captured.Enqueue(new CapturedEscPosPrint(targetId, copy, preview, DateTimeOffset.UtcNow));

        if (SimulateUnknownDelivery)
        {
            return Task.FromResult(PrinterTransportResult.Unknown(
                "SimPrinter.UnknownDelivery", "Print bytes may have reached the printer; do not automatically resend."));
        }

        return Task.FromResult(PrinterTransportResult.AcceptedForTransport($"Simulated ESC/POS print delivered ({copy.Length} bytes) to {targetId}."));
    }
}
