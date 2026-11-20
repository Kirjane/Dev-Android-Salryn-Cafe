namespace Salryn.Mobile.Foundation.Printing;

public sealed record PrinterTransportResult(bool Accepted, string Code, string Message)
{
    public static PrinterTransportResult NotReady(string code, string message) =>
        new(false, code, message);

    public static PrinterTransportResult AcceptedForTransport(string message) =>
        new(true, "PrinterTransport.Accepted", message);
}

public interface IPrinterTransport
{
    Task<PrinterTransportResult> SendAsync(
        string targetId,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default);
}
