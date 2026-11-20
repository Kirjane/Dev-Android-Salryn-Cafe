using Salryn.Mobile.Foundation.Printing;
using Xunit;

namespace Salryn.Cafe.I6BTests;

public sealed class MobilePrinterOutcomeBoundaryTests
{
    private static readonly byte[] SyntheticEscPos = [0x1B, 0x40, 0x1B, 0x61, 0x00, 0x0A];

    [Fact]
    public async Task SimulatedDisconnectBeforeWrite_IsCleanFailureWithNoCapturedBytes()
    {
        SimulatedEscPosPrinterTransport printer = new()
        {
            SimulateOfflineFailure = true
        };

        PrinterTransportResult result = await printer.SendAsync("AA:BB:CC:11:22:01", SyntheticEscPos);

        Assert.False(result.Accepted);
        Assert.False(result.OutcomeUnknown);
        Assert.Empty(printer.CapturedPrints);
        Assert.Equal("SimPrinter.BluetoothDisconnected", result.Code);
    }

    [Fact]
    public async Task SimulatedDisconnectAfterBytes_RecordsUnknownDelivery_NotCleanRetry()
    {
        SimulatedEscPosPrinterTransport printer = new()
        {
            SimulateUnknownDelivery = true
        };

        PrinterTransportResult result = await printer.SendAsync("AA:BB:CC:11:22:01", SyntheticEscPos);

        Assert.False(result.Accepted);
        Assert.True(result.OutcomeUnknown);
        Assert.Equal("SimPrinter.UnknownDelivery", result.Code);
        CapturedEscPosPrint attempt = Assert.Single(printer.CapturedPrints);
        Assert.Equal(SyntheticEscPos, attempt.Payload);
        // A durable print dispatcher must pause for inspection, not silently resend.
    }
}
